// Copyright (c) Benjamin Maurin (njz3) 2026

using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace SuperResScaler.Capture;

/// <summary>A captured frame. Its texture is only valid until the frame is disposed.</summary>
internal sealed class CapturedFrame : IDisposable
{
    private readonly Direct3D11CaptureFrame _frame;

    public CapturedFrame(Direct3D11CaptureFrame frame)
    {
        _frame = frame;
        Texture = WinRTInterop.GetTexture(frame.Surface);
        ContentWidth = frame.ContentSize.Width;
        ContentHeight = frame.ContentSize.Height;
    }

    public ID3D11Texture2D Texture { get; }

    /// <summary>Size of the window content inside the (possibly larger) texture.</summary>
    public int ContentWidth { get; }
    public int ContentHeight { get; }

    public void Dispose()
    {
        Texture.Dispose();
        _frame.Dispose();
    }
}

/// <summary>
/// Captures one window with Windows.Graphics.Capture. Works for occluded windows and
/// borderless "fullscreen" games, but not for minimized windows or exclusive fullscreen.
/// </summary>
internal sealed class WindowCapture : IDisposable
{
    private const DirectXPixelFormat PixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;
    private const int BufferCount = 2;

    private readonly IDirect3DDevice _device;
    private readonly GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _framePool;
    private readonly GraphicsCaptureSession _session;
    private SizeInt32 _poolSize;
    private SizeInt32? _pendingResize;
    private volatile bool _closed;

    public WindowCapture(ID3D11Device d3dDevice, IntPtr hwnd, bool captureCursor)
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new NotSupportedException("Windows.Graphics.Capture n'est pas disponible (Windows 10 1903 ou plus récent requis).");

        using (var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>())
            _device = WinRTInterop.CreateDirect3DDevice(dxgiDevice);

        _item = WinRTInterop.CreateItemForWindow(hwnd);
        _item.Closed += OnItemClosed;
        _poolSize = _item.Size;

        // Free-threaded: frames are pulled from the render thread, no dispatcher needed.
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, PixelFormat, BufferCount, _poolSize);
        _session = _framePool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = captureCursor;
        DisableCaptureBorder(_session);
        _session.StartCapture();

        Log.Info($"Capture started: item {_item.DisplayName} {_poolSize.Width}x{_poolSize.Height}");
    }

    /// <summary>True once the captured window has been destroyed.</summary>
    public bool IsClosed => _closed;

    public void SetCursorCapture(bool enabled)
    {
        try { _session.IsCursorCaptureEnabled = enabled; }
        catch (Exception ex) { Log.Warn($"IsCursorCaptureEnabled: {ex.Message}"); }
    }

    /// <summary>
    /// Returns the most recent frame (older queued frames are dropped), or null when the
    /// window content has not changed since the last call. The caller disposes the frame
    /// before calling this method again.
    /// </summary>
    public CapturedFrame? TryGetLatestFrame()
    {
        if (_pendingResize is SizeInt32 size)
        {
            _pendingResize = null;
            _poolSize = size;
            _framePool.Recreate(_device, PixelFormat, BufferCount, size);
        }

        Direct3D11CaptureFrame? latest = null;
        while (_framePool.TryGetNextFrame() is { } frame)
        {
            latest?.Dispose();
            latest = frame;
        }
        if (latest == null)
            return null;

        var contentSize = latest.ContentSize;
        if (contentSize.Width != _poolSize.Width || contentSize.Height != _poolSize.Height)
            _pendingResize = contentSize;

        return new CapturedFrame(latest);
    }

    private static void DisableCaptureBorder(GraphicsCaptureSession session)
    {
        // Windows 11 lets us hide the yellow "being captured" border.
        if (!ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName, nameof(GraphicsCaptureSession.IsBorderRequired)))
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
        _session.Dispose();
        _framePool.Dispose();
        _device.Dispose();
    }
}
