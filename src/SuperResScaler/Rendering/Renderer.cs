using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace SuperResScaler.Rendering;

internal sealed class ShaderCompileException(string shader, string message) : Exception($"{shader} : {message}");

/// <summary>Layout of the "Constants" cbuffer declared in Shaders/Common.hlsli.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ShaderConstants
{
    public float SourceWidth, SourceHeight, InvSourceWidth, InvSourceHeight;
    public float OutputWidth, OutputHeight, InvOutputWidth, InvOutputHeight;
    public float Time;
    public uint FrameCount;
    public float OutputX, OutputY;
}

/// <summary>
/// Owns the D3D11 device and draws the cropped source image into a destination rectangle
/// with the selected pixel shader (single full-screen-triangle pass).
/// </summary>
internal sealed class Renderer : IDisposable
{
    private const string VertexShaderSource = """
        struct VSOutput { float4 Position : SV_POSITION; float2 TexCoord : TEXCOORD0; };
        VSOutput main(uint id : SV_VertexID)
        {
            VSOutput o;
            o.TexCoord = float2((id << 1) & 2, id & 2);
            o.Position = float4(o.TexCoord * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }
        """;

    // Used when the Shaders folder is missing or the selected shader does not compile.
    private const string FallbackPixelShaderSource = """
        Texture2D<float4> Source : register(t0);
        cbuffer Constants : register(b0) { float2 SourceSize; float2 InvSourceSize; float2 OutputSize; float2 InvOutputSize; };
        float4 main(float4 position : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
        {
            int2 p = clamp(int2(floor(uv * SourceSize)), int2(0, 0), int2(SourceSize) - 1);
            return Source.Load(int3(p, 0));
        }
        """;

    public const string FallbackShaderName = "Nearest (intégré)";

    private readonly string _profileSuffix;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11SamplerState _pointSampler;
    private readonly ID3D11SamplerState _linearSampler;
    private readonly ID3D11Buffer _constantBuffer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private ID3D11PixelShader? _pixelShader;
    private ID3D11Texture2D? _sourceTexture;
    private ID3D11ShaderResourceView? _sourceView;
    private uint _frameCount;

    public Renderer()
    {
        FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];
        const DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport;

        Result result = D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, levels,
            out ID3D11Device? device, out FeatureLevel level, out ID3D11DeviceContext? context);
        if (result.Failure)
        {
            Log.Warn($"Hardware D3D11 device unavailable ({result}), using WARP");
            D3D11.D3D11CreateDevice(null, DriverType.Warp, flags, levels, out device, out level, out context).CheckError();
        }
        Device = device!;
        Context = context!;
        _profileSuffix = level >= FeatureLevel.Level_11_0 ? "_5_0" : "_4_0";

        // Windows.Graphics.Capture also uses the device from its own threads.
        using (var multithread = Context.QueryInterface<ID3D11Multithread>())
            multithread.SetMultithreadProtected(true);

        using (var dxgiDevice = Device.QueryInterface<IDXGIDevice>())
        {
            dxgiDevice.GetAdapter(out IDXGIAdapter adapter).CheckError();
            using (adapter)
                Log.Info($"D3D11 device: {adapter.Description.Description}, feature level {level}");
        }

        _vertexShader = Device.CreateVertexShader(Compile(VertexShaderSource, "FullscreenTriangle", "vs" + _profileSuffix));
        _pointSampler = Device.CreateSamplerState(SamplerDescription.PointClamp);
        _linearSampler = Device.CreateSamplerState(SamplerDescription.LinearClamp);
        _constantBuffer = Device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<ShaderConstants>(), BindFlags.ConstantBuffer));
    }

    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }

    public string? ShaderName { get; private set; }

    public int SourceWidth { get; private set; }
    public int SourceHeight { get; private set; }
    public bool HasSource => _sourceView != null;

    /// <summary>Compiles Shaders/&lt;name&gt;.hlsl. Throws on error and keeps the current shader.</summary>
    public void LoadShader(string name)
    {
        string path = ShaderLibrary.FindPath(name) ?? throw new FileNotFoundException($"Shader introuvable : {name}");
        string source = ShaderLibrary.LoadSource(path);
        var shader = Device.CreatePixelShader(Compile(source, Path.GetFileName(path), "ps" + _profileSuffix));
        _pixelShader?.Dispose();
        _pixelShader = shader;
        ShaderName = name;
    }

    /// <summary>Loads <paramref name="name"/>, or the built-in nearest-neighbour shader if that fails.</summary>
    public void LoadShaderOrFallback(string name)
    {
        try
        {
            LoadShader(name);
        }
        catch (Exception ex)
        {
            Log.Error($"Shader '{name}' unavailable, using built-in fallback", ex);
            _pixelShader?.Dispose();
            _pixelShader = Device.CreatePixelShader(Compile(FallbackPixelShaderSource, "Fallback", "ps" + _profileSuffix));
            ShaderName = FallbackShaderName;
        }
    }

    private static byte[] Compile(string source, string sourceName, string profile)
    {
        Result result = Compiler.Compile(source, "main", sourceName, profile, out Blob? blob, out Blob? errors);
        try
        {
            if (result.Failure || blob == null)
                throw new ShaderCompileException(sourceName, errors?.AsString()?.Trim() ?? result.ToString());
            return blob.AsBytes();
        }
        finally
        {
            blob?.Dispose();
            errors?.Dispose();
        }
    }

    /// <summary>Copies the given region of a captured texture into the shader's source texture.</summary>
    public void UpdateSource(ID3D11Texture2D texture, int x, int y, int width, int height)
    {
        if (_sourceTexture == null || width != SourceWidth || height != SourceHeight)
        {
            _sourceView?.Dispose();
            _sourceTexture?.Dispose();
            _sourceTexture = Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1));
            _sourceView = Device.CreateShaderResourceView(_sourceTexture);
            SourceWidth = width;
            SourceHeight = height;
        }
        Context.CopySubresourceRegion(_sourceTexture, 0, 0, 0, 0, texture, 0, new Box(x, y, 0, x + width, y + height, 1));
    }

    /// <summary>Clears the target to black and draws the source into <paramref name="dest"/> (target pixels).</summary>
    public void Draw(ID3D11RenderTargetView target, RectangleF dest)
    {
        Context.ClearRenderTargetView(target, new Color4(0f, 0f, 0f, 1f));
        if (_sourceView == null || _pixelShader == null || dest.Width < 1 || dest.Height < 1)
            return;

        var constants = new ShaderConstants
        {
            SourceWidth = SourceWidth,
            SourceHeight = SourceHeight,
            InvSourceWidth = 1f / SourceWidth,
            InvSourceHeight = 1f / SourceHeight,
            OutputWidth = dest.Width,
            OutputHeight = dest.Height,
            InvOutputWidth = 1f / dest.Width,
            InvOutputHeight = 1f / dest.Height,
            Time = (float)_clock.Elapsed.TotalSeconds,
            FrameCount = _frameCount++,
            OutputX = dest.X,
            OutputY = dest.Y,
        };
        Context.UpdateSubresource(in constants, _constantBuffer);

        Context.OMSetRenderTargets(target);
        Context.RSSetViewport(new Viewport(dest.X, dest.Y, dest.Width, dest.Height));
        Context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        Context.IASetInputLayout(null);
        Context.VSSetShader(_vertexShader);
        Context.PSSetShader(_pixelShader);
        Context.PSSetShaderResource(0, _sourceView);
        Context.PSSetSampler(0, _pointSampler);
        Context.PSSetSampler(1, _linearSampler);
        Context.PSSetConstantBuffer(0, _constantBuffer);
        Context.Draw(3, 0);
        Context.PSSetShaderResource(0, null!);
    }

    /// <summary>Synchronously copies a texture region to CPU memory as tightly packed BGRA rows.</summary>
    public byte[] ReadPixels(ID3D11Texture2D texture, int x, int y, int width, int height)
    {
        var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read);
        using var staging = Device.CreateTexture2D(desc);
        Context.CopySubresourceRegion(staging, 0, 0, 0, 0, texture, 0, new Box(x, y, 0, x + width, y + height, 1));

        MappedSubresource mapped = Context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var pixels = new byte[width * height * 4];
            for (int row = 0; row < height; row++)
                Marshal.Copy(mapped.DataPointer + row * (nint)mapped.RowPitch, pixels, row * width * 4, width * 4);
            return pixels;
        }
        finally
        {
            Context.Unmap(staging, 0);
        }
    }

    /// <summary>Renders into an off-screen target and returns the result (used by the self-test).</summary>
    public Bitmap RenderToBitmap(int width, int height, RectangleF dest)
    {
        var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget);
        using var target = Device.CreateTexture2D(desc);
        using var view = Device.CreateRenderTargetView(target);
        Draw(view, dest);
        return ToBitmap(ReadPixels(target, 0, 0, width, height), width, height);
    }

    public static Bitmap ToBitmap(byte[] bgra, int width, int height)
    {
        for (int i = 3; i < bgra.Length; i += 4)
            bgra[i] = 255; // captured windows often carry a meaningless alpha channel

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int row = 0; row < height; row++)
                Marshal.Copy(bgra, row * width * 4, data.Scan0 + row * data.Stride, width * 4);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    public void Dispose()
    {
        Context.ClearState();
        Context.Flush();
        _sourceView?.Dispose();
        _sourceTexture?.Dispose();
        _pixelShader?.Dispose();
        _constantBuffer.Dispose();
        _linearSampler.Dispose();
        _pointSampler.Dispose();
        _vertexShader.Dispose();
        Context.Dispose();
        Device.Dispose();
    }
}
