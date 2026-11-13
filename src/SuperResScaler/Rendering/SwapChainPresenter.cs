// Copyright (c) Benjamin Maurin (njz3) 2026

using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SuperResScaler.Rendering;

/// <summary>
/// Flip-model swap chain on the overlay window. Frames are presented as soon as they are drawn,
/// with a sync interval of 0: the display shows the latest one at its next refresh, without
/// tearing (unless VSync is off), and presenting never waits for the output's refresh. So the
/// render loop follows the captured picture's rate, not the output's: when two independent
/// 60 Hz clocks drift, no frame is lost waiting for the wrong one. Falls back to the legacy blit
/// model if the flip model is refused.
/// </summary>
internal sealed class SwapChainPresenter : IDisposable
{
    /// <summary>One shown, one waiting for the refresh, one to draw in: presenting never blocks on a free buffer.</summary>
    private const uint FlipBufferCount = 3;

    private readonly IDXGISwapChain1 _swapChain;
    private readonly bool _tearingSupported;
    private readonly SwapChainFlags _flags;
    private ID3D11RenderTargetView _renderTarget;

    public SwapChainPresenter(Renderer renderer, IntPtr hwnd, int width, int height)
    {
        Width = width;
        Height = height;

        using var dxgiDevice = renderer.Device.QueryInterface<IDXGIDevice1>();
        // At most one frame queued for the GPU: the CPU never runs ahead of it.
        dxgiDevice.SetMaximumFrameLatency(1);
        dxgiDevice.GetAdapter(out IDXGIAdapter adapter).CheckError();
        using (adapter)
        using (var factory = adapter.GetParent<IDXGIFactory2>())
        {
            using (var factory5 = factory.QueryInterfaceOrNull<IDXGIFactory5>())
                _tearingSupported = factory5?.PresentAllowTearing ?? false;

            _flags = _tearingSupported ? SwapChainFlags.AllowTearing : SwapChainFlags.None;
            var desc = new SwapChainDescription1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = FlipBufferCount,
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

        _renderTarget = CreateRenderTarget(renderer.Device);
        Log.Info($"Swap chain {width}x{height}, flip model: {IsFlipModel} ({(IsFlipModel ? FlipBufferCount : 1)} buffers), tearing: {_tearingSupported}");
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsFlipModel { get; }
    public ID3D11RenderTargetView RenderTarget => _renderTarget;

    /// <summary>
    /// Presents the drawn frame without waiting for the output's refresh. With VSync, the latest
    /// frame is shown at the next refresh (no tearing); without, at once (tearing allowed).
    /// </summary>
    public void Present(bool vsync)
    {
        var flags = !vsync && _tearingSupported ? PresentFlags.AllowTearing : PresentFlags.None;
        _swapChain.Present(0, flags).CheckError();
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
    }
}
