// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Runtime.Versioning;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace SuperResScaler.Capture;

/// <summary>
/// Captures one window or one whole display with Windows.Graphics.Capture (Windows 10 1903 or later:
/// CreateForWindow / CreateForMonitor). Windows are captured even when occluded, and borderless
/// "fullscreen" games work, but not minimized windows or exclusive fullscreen.
/// </summary>
[SupportedOSPlatform("windows10.0.18362.0")]
internal sealed class FrameCapture : IFrameSource
{
    private const DirectXPixelFormat PixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;
    private const int BufferCount = 2;

    private readonly IDirect3DDevice _device;
    private readonly GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _framePool;
    private readonly GraphicsCaptureSession _session;
    private readonly AutoResetEvent _frameArrived = new(false);
    private SizeInt32 _poolSize;
    private SizeInt32? _pendingResize;
    private volatile bool _closed;

    public static FrameCapture ForWindow(ID3D11Device device, IntPtr hwnd, bool captureCursor)
        => new(device, () => WinRTInterop.CreateItemForWindow(hwnd), captureCursor);

    public static FrameCapture ForMonitor(ID3D11Device device, IntPtr hmonitor, bool captureCursor)
        => new(device, () => WinRTInterop.CreateItemForMonitor(hmonitor), captureCursor);

    private FrameCapture(ID3D11Device d3dDevice, Func<GraphicsCaptureItem> createItem, bool captureCursor)
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new NotSupportedException("Windows.Graphics.Capture n'est pas disponible (Windows 10 1903 ou plus récent requis).");

        using (var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>())
            _device = WinRTInterop.CreateDirect3DDevice(dxgiDevice);

        _item = createItem();
        _item.Closed += OnItemClosed;
        _poolSize = _item.Size;

        // Free-threaded: frames are pulled from the render thread, no dispatcher needed.
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, PixelFormat, BufferCount, _poolSize);
        _framePool.FrameArrived += OnFrameArrived; // raised on a system thread: only wakes the render loop
        _session = _framePool.CreateCaptureSession(_item);
        SetCursorCapture(captureCursor);
        DisableCaptureBorder(_session);
        _session.StartCapture();

        Log.Info($"Capture started (Windows.Graphics.Capture): item {_item.DisplayName} {_poolSize.Width}x{_poolSize.Height}");
    }

    public CaptureMethod Method => CaptureMethod.GraphicsCapture;

    public bool IsClosed => _closed;

    public void SetCursorCapture(bool enabled)
    {
        // Windows 10 1903 and 1909 always capture the cursor: the switch came with 2004.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return;
        try { _session.IsCursorCaptureEnabled = enabled; }
        catch (Exception ex) { Log.Warn($"IsCursorCaptureEnabled: {ex.Message}"); }
    }

    public CapturedFrame? TryGetLatestFrame()
    {
        if (_pendingResize is SizeInt32 size)
        {
            _pendingResize = null;
            _poolSize = size;
            _framePool.Recreate(_device, PixelFormat, BufferCount, size);
        }

        Direct3D11CaptureFrame? latest = null;
        int frames = 0;
        while (_framePool.TryGetNextFrame() is { } frame)
        {
            latest?.Dispose();
            latest = frame;
            frames++;
        }
        if (latest == null)
            return null;

        var contentSize = latest.ContentSize;
        if (contentSize.Width != _poolSize.Width || contentSize.Height != _poolSize.Height)
            _pendingResize = contentSize;

        return new CapturedFrame(WinRTInterop.GetTexture(latest.Surface), contentSize.Width, contentSize.Height,
            latest.SystemRelativeTime, latest.Dispose, frames);
    }

    public CapturedFrame? WaitForFrame(int timeoutMs)
    {
        if (TryGetLatestFrame() is { } frame)
            return frame;
        _frameArrived.WaitOne(timeoutMs);
        return TryGetLatestFrame();
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args) => _frameArrived.Set();

    private static void DisableCaptureBorder(GraphicsCaptureSession session)
    {
        // Windows 11 lets us hide the yellow "being captured" border.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)
            || !ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName, nameof(GraphicsCaptureSession.IsBorderRequired)))
            return;
        try
        {
            GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless).AsTask().Wait(2000);
        }
        catch (Exception ex)
        {
            Log.Warn($"Borderless capture access: {ex.Message}");
        }
        try
        {
            session.IsBorderRequired = false;
        }
        catch (Exception ex)
        {
            Log.Warn($"IsBorderRequired: {ex.Message}");
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args) => _closed = true;

    public void Dispose()
    {
        _item.Closed -= OnItemClosed;
        _framePool.FrameArrived -= OnFrameArrived;
        _session.Dispose();
        _framePool.Dispose();
        _device.Dispose();
        _frameArrived.Dispose();
    }
}
