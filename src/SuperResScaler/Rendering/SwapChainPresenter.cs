using SharpGen.Runtime;
using SuperResScaler.Native;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SuperResScaler.Rendering;

/// <summary>
/// Flip-model swap chain on the overlay window, with a frame-latency waitable object so the
/// render loop runs once per display refresh with a single frame of queueing (low latency).
/// Falls back to the legacy blit model if the flip model is refused.
/// </summary>
internal sealed class SwapChainPresenter : IDisposable
{
    private readonly IDXGISwapChain1 _swapChain;
    private readonly IntPtr _waitHandle;
    private readonly bool _tearingSupported;
    private readonly SwapChainFlags _flags;
    private ID3D11RenderTargetView _renderTarget;

    public SwapChainPresenter(Renderer renderer, IntPtr hwnd, int width, int height)
    {
        Width = width;
        Height = height;

        using var dxgiDevice = renderer.Device.QueryInterface<IDXGIDevice>();
        dxgiDevice.GetAdapter(out IDXGIAdapter adapter).CheckError();
        using (adapter)
        using (var factory = adapter.GetParent<IDXGIFactory2>())
        {
            using (var factory5 = factory.QueryInterfaceOrNull<IDXGIFactory5>())
                _tearingSupported = factory5?.PresentAllowTearing ?? false;

            _flags = SwapChainFlags.FrameLatencyWaitableObject | (_tearingSupported ? SwapChainFlags.AllowTearing : SwapChainFlags.None);
            var desc = new SwapChainDescription1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Ignore,
                Flags = _flags,
            };

            try
            {
                _swapChain = factory.CreateSwapChainForHwnd(renderer.Device, hwnd, desc);
                IsFlipModel = true;
            }
            catch (SharpGenException ex)
            {
                Log.Warn($"Flip-model swap chain refused ({ex.ResultCode}), falling back to blit model");
                _flags = SwapChainFlags.None;
                _tearingSupported = false;
                desc.SwapEffect = SwapEffect.Discard;
                desc.BufferCount = 1;
                desc.Flags = _flags;
                _swapChain = factory.CreateSwapChainForHwnd(renderer.Device, hwnd, desc);
            }

            factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAll);
        }

        if (IsFlipModel)
        {
            using var swapChain2 = _swapChain.QueryInterface<IDXGISwapChain2>();
            swapChain2.MaximumFrameLatency = 1;
            _waitHandle = swapChain2.FrameLatencyWaitableObject;
        }
        else
        {
            using var dxgiDevice1 = renderer.Device.QueryInterface<IDXGIDevice1>();
            dxgiDevice1.SetMaximumFrameLatency(1);
        }

        _renderTarget = CreateRenderTarget(renderer.Device);
        Log.Info($"Swap chain {width}x{height}, flip model: {IsFlipModel}, tearing: {_tearingSupported}");
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsFlipModel { get; }
    public ID3D11RenderTargetView RenderTarget => _renderTarget;

    /// <summary>Blocks until the swap chain can accept a new frame (about once per refresh).</summary>
    public void WaitForNextFrame(uint timeoutMs)
    {
        if (_waitHandle != IntPtr.Zero)
            Kernel32.WaitForSingleObjectEx(_waitHandle, timeoutMs, true);
    }

    public void Present(bool vsync)
    {
        Result result = vsync
            ? _swapChain.Present(1, PresentFlags.None)
            : _swapChain.Present(0, _tearingSupported ? PresentFlags.AllowTearing : PresentFlags.None);
        result.CheckError();
    }

    public void Resize(ID3D11Device device, int width, int height)
    {
        if (width == Width && height == Height)
            return;
        _renderTarget.Dispose();
        _swapChain.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, _flags).CheckError();
        Width = width;
        Height = height;
        _renderTarget = CreateRenderTarget(device);
    }

    private ID3D11RenderTargetView CreateRenderTarget(ID3D11Device device)
    {
        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        return device.CreateRenderTargetView(backBuffer);
    }

    public void Dispose()
    {
        _renderTarget.Dispose();
        _swapChain.Dispose();
        if (_waitHandle != IntPtr.Zero)
            Kernel32.CloseHandle(_waitHandle);
    }
}
