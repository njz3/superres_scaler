using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using SuperResScaler.Engine;
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

    // The CROP_BARREL strip: positions in clip space of the destination viewport, texture coordinates.
    private const string StripVertexShaderSource = """
        struct VSOutput { float4 Position : SV_POSITION; float2 TexCoord : TEXCOORD0; };
        VSOutput main(float2 position : POSITION, float2 texCoord : TEXCOORD0)
        {
            VSOutput o;
            o.Position = float4(position, 0, 1);
            o.TexCoord = texCoord;
            return o;
        }
        """;

    private const int StripVertexSize = 4 * sizeof(float);
    private const int MaxStripVertices = 4 * (BarrelStrip.StepsPerEdge + 1);

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
    private readonly ID3D11VertexShader _stripVertexShader;
    private readonly ID3D11InputLayout _stripLayout;
    private readonly ID3D11Buffer _stripBuffer;
    private readonly ID3D11RasterizerState _rasterizerState;
    private readonly ID3D11SamplerState _pointSampler;
    private readonly ID3D11SamplerState _linearSampler;
    private readonly ID3D11Buffer _constantBuffer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private ID3D11PixelShader? _pixelShader;

    // The captured region (UpdateSource), and the part of it the aspect ratio keeps (_crop formats).
    private ID3D11Texture2D? _capturedTexture;
    private ID3D11ShaderResourceView? _capturedView;
    private ID3D11Texture2D? _croppedTexture;
    private ID3D11ShaderResourceView? _croppedView;
    private Rectangle _croppedFrom;
    private long _capturedVersion;
    private long _croppedVersion = -1;
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
        byte[] stripBytecode = Compile(StripVertexShaderSource, "BarrelStrip", "vs" + _profileSuffix);
        _stripVertexShader = Device.CreateVertexShader(stripBytecode);
        _stripLayout = Device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 2 * sizeof(float), 0),
        ], stripBytecode);
        _stripBuffer = Device.CreateBuffer(new BufferDescription((uint)(MaxStripVertices * StripVertexSize),
            BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        _rasterizerState = Device.CreateRasterizerState(RasterizerDescription.CullNone);
        _pointSampler = Device.CreateSamplerState(SamplerDescription.PointClamp);
        _linearSampler = Device.CreateSamplerState(SamplerDescription.LinearClamp);
        _constantBuffer = Device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<ShaderConstants>(), BindFlags.ConstantBuffer));
    }

    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }

    public string? ShaderName { get; private set; }

    /// <summary>Size of the captured region given to <see cref="UpdateSource"/>.</summary>
    public int CapturedWidth { get; private set; }
    public int CapturedHeight { get; private set; }
    public bool HasSource => _capturedView != null;

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

    /// <summary>Copies the given region of a captured texture: the picture to scale.</summary>
    public void UpdateSource(ID3D11Texture2D texture, int x, int y, int width, int height)
    {
        if (_capturedTexture == null || width != CapturedWidth || height != CapturedHeight)
        {
            _capturedView?.Dispose();
            _capturedTexture?.Dispose();
            _capturedTexture = Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1));
            _capturedView = Device.CreateShaderResourceView(_capturedTexture);
            CapturedWidth = width;
            CapturedHeight = height;
        }
        Context.CopySubresourceRegion(_capturedTexture, 0, 0, 0, 0, texture, 0, new Box(x, y, 0, x + width, y + height, 1));
        _capturedVersion++;
    }

    /// <summary>
    /// The texture the shader reads for <paramref name="part"/> of the captured picture: the
    /// captured one when it is whole, else a copy of the part, so that shaders clamp at its edges.
    /// </summary>
    private ID3D11ShaderResourceView SourceView(Rectangle part)
    {
        if (part == new Rectangle(0, 0, CapturedWidth, CapturedHeight))
            return _capturedView!;
        if (_croppedTexture == null || part.Size != _croppedFrom.Size)
        {
            _croppedView?.Dispose();
            _croppedTexture?.Dispose();
            _croppedTexture = Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)part.Width, (uint)part.Height, 1, 1));
            _croppedView = Device.CreateShaderResourceView(_croppedTexture);
            _croppedVersion = -1;
        }
        if (_croppedVersion != _capturedVersion || part != _croppedFrom)
        {
            Context.CopySubresourceRegion(_croppedTexture, 0, 0, 0, 0, _capturedTexture!, 0,
                new Box(part.Left, part.Top, 0, part.Right, part.Bottom, 1));
            _croppedFrom = part;
            _croppedVersion = _capturedVersion;
        }
        return _croppedView!;
    }

    /// <summary>Clears the target to black and draws the picture where <paramref name="placement"/> says (target pixels).</summary>
    public void Draw(ID3D11RenderTargetView target, Placement placement)
    {
        Context.ClearRenderTargetView(target, new Color4(0f, 0f, 0f, 1f));
        RectangleF dest = placement.Dest;
        Rectangle part = Rectangle.Intersect(placement.Source, new Rectangle(0, 0, CapturedWidth, CapturedHeight));
        if (_capturedView == null || _pixelShader == null || dest.Width < 1 || dest.Height < 1 || part.Width < 1 || part.Height < 1)
            return;
        var sourceView = SourceView(part);

        var constants = new ShaderConstants
        {
            SourceWidth = part.Width,
            SourceHeight = part.Height,
            InvSourceWidth = 1f / part.Width,
            InvSourceHeight = 1f / part.Height,
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
        Context.RSSetState(_rasterizerState);
        Context.PSSetShader(_pixelShader);
        Context.PSSetShaderResource(0, sourceView);
        Context.PSSetSampler(0, _pointSampler);
        Context.PSSetSampler(1, _linearSampler);
        Context.PSSetConstantBuffer(0, _constantBuffer);
        if (placement.Barrel is { } barrel)
        {
            DrawStrip(barrel);
        }
        else
        {
            Context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            Context.IASetInputLayout(null);
            Context.VSSetShader(_vertexShader);
            Context.Draw(3, 0);
        }
        Context.PSSetShaderResource(0, null!);
    }

    /// <summary>
    /// The picture bent along a CROP_BARREL strip, as linuxloader's blitPresentBarrel: one pair of
    /// vertices across the frame per strip point, the texture coordinate following the barrel.
    /// </summary>
    private unsafe void DrawStrip(BarrelStrip barrel)
    {
        var points = barrel.Points;
        int vertexCount = Math.Min(points.Count * 2, MaxStripVertices);
        MappedSubresource mapped = Context.Map(_stripBuffer, MapMode.WriteDiscard);
        try
        {
            var vertices = (float*)mapped.DataPointer;
            for (int i = 0; i < vertexCount / 2; i++)
            {
                float frame = points[i].X, picture = points[i].Y;
                float* v = vertices + i * 8;
                if (barrel.Horizontal) // left to right: top then bottom vertex
                {
                    float x = frame * 2 - 1;
                    v[0] = x; v[1] = 1; v[2] = picture; v[3] = 0;
                    v[4] = x; v[5] = -1; v[6] = picture; v[7] = 1;
                }
                else // top to bottom: left then right vertex
                {
                    float y = 1 - frame * 2;
                    v[0] = -1; v[1] = y; v[2] = 0; v[3] = picture;
                    v[4] = 1; v[5] = y; v[6] = 1; v[7] = picture;
                }
            }
        }
        finally
        {
            Context.Unmap(_stripBuffer, 0);
        }

        Context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        Context.IASetInputLayout(_stripLayout);
        Context.IASetVertexBuffer(0, _stripBuffer, StripVertexSize);
        Context.VSSetShader(_stripVertexShader);
        Context.Draw((uint)vertexCount, 0);
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
    public Bitmap RenderToBitmap(int width, int height, Placement placement)
    {
        var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget);
        using var target = Device.CreateTexture2D(desc);
        using var view = Device.CreateRenderTargetView(target);
        Draw(view, placement);
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
        _croppedView?.Dispose();
        _croppedTexture?.Dispose();
        _capturedView?.Dispose();
        _capturedTexture?.Dispose();
        _pixelShader?.Dispose();
        _stripBuffer.Dispose();
        _stripLayout.Dispose();
        _stripVertexShader.Dispose();
        _rasterizerState.Dispose();
        _constantBuffer.Dispose();
        _linearSampler.Dispose();
        _pointSampler.Dispose();
        _vertexShader.Dispose();
        Context.Dispose();
        Device.Dispose();
    }
}
