// Copyright (c) Benjamin Maurin (njz3) 2026

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
    public float MirrorSignX, MirrorSignY; // read by the vertex shaders only
    private readonly float _padding0, _padding1;
}

/// <summary>
/// Owns the D3D11 device and draws the cropped source image into a destination rectangle
/// with the selected pixel shader (single full-screen-triangle pass).
/// </summary>
internal sealed class Renderer : IDisposable
{
    // The mirror flips the geometry, not the texture coordinates: the drawing itself is mirrored,
    // so that it holds for an off-centre crop or barrel, and every pixel shader works unchanged.
    private const string VertexShaderSource = """
        cbuffer Constants : register(b0) { float2 MirrorSign : packoffset(c3); };
        struct VSOutput { float4 Position : SV_POSITION; float2 TexCoord : TEXCOORD0; };
        VSOutput main(uint id : SV_VertexID)
        {
            VSOutput o;
            o.TexCoord = float2((id << 1) & 2, id & 2);
            o.Position = float4((o.TexCoord * float2(2, -2) + float2(-1, 1)) * MirrorSign, 0, 1);
            return o;
        }
        """;

    // The CROP_BARREL strip: positions in clip space of the destination viewport, texture coordinates.
    private const string StripVertexShaderSource = """
        cbuffer Constants : register(b0) { float2 MirrorSign : packoffset(c3); };
        struct VSOutput { float4 Position : SV_POSITION; float2 TexCoord : TEXCOORD0; };
        VSOutput main(float2 position : POSITION, float2 texCoord : TEXCOORD0)
        {
            VSOutput o;
            o.Position = float4(position * MirrorSign, 0, 1);
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

    // Simulated interlacing: one line in two of the source (FIELD 0 = even, 1 = odd), half as high.
    private const string FieldPixelShaderSource = """
        Texture2D<float4> Source : register(t0);
        float4 main(float4 position : SV_POSITION) : SV_TARGET
        {
            uint width, height;
            Source.GetDimensions(width, height);
            int y = min(int(position.y) * 2 + FIELD, int(height) - 1);
            return Source.Load(int3(int(position.x), y, 0));
        }
        """;

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

    // The captured region (UpdateSource); the frame when it goes beyond the region (black around);
    // the part the shader reads when it is not all of one of them (frame inside the region, _crop formats).
    private ID3D11Texture2D? _capturedTexture;
    private ID3D11ShaderResourceView? _capturedView;
    private ID3D11Texture2D? _paddedTexture;
    private ID3D11ShaderResourceView? _paddedView;
    private ID3D11RenderTargetView? _paddedTarget;
    private Rectangle _paddedFrame;
    private System.Drawing.Size _paddedRegion;
    private long _paddedFromVersion = -1;
    private long _paddedVersion;
    private ID3D11Texture2D? _croppedTexture;
    private ID3D11ShaderResourceView? _croppedView;
    private Rectangle _croppedFrom;
    private ID3D11Texture2D? _croppedSource;
    private long _capturedVersion;
    private long _croppedVersion = -1;

    // Simulated interlacing: the field the shader reads instead of the picture.
    private readonly ID3D11PixelShader[] _fieldShaders;
    private ID3D11Texture2D? _fieldTexture;
    private ID3D11ShaderResourceView? _fieldView;
    private ID3D11RenderTargetView? _fieldTarget;
    private uint _frameCount;

    /// <param name="adapter">
    /// The graphics card to render with, null for the default one. The desktop duplication needs the
    /// card of the captured display.
    /// </param>
    public Renderer(IDXGIAdapter? adapter = null)
    {
        FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];
        const DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport;

        Result result = D3D11.D3D11CreateDevice(adapter, adapter != null ? DriverType.Unknown : DriverType.Hardware, flags, levels,
            out ID3D11Device? device, out FeatureLevel level, out ID3D11DeviceContext? context);
        if (result.Failure && adapter != null)
        {
            Log.Warn($"D3D11 device on {adapter.Description.Description} unavailable ({result}), using the default graphics card");
            result = D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, levels, out device, out level, out context);
        }
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
            dxgiDevice.GetAdapter(out IDXGIAdapter used).CheckError();
            using (used)
                Log.Info($"D3D11 device: {used.Description.Description}, feature level {level}");
        }
        GpuPriority.Raise(Device);

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
        _fieldShaders =
        [
            Device.CreatePixelShader(Compile("#define FIELD 0\n" + FieldPixelShaderSource, "EvenField", "ps" + _profileSuffix)),
            Device.CreatePixelShader(Compile("#define FIELD 1\n" + FieldPixelShaderSource, "OddField", "ps" + _profileSuffix)),
        ];
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
    /// The texture the shader reads for <paramref name="part"/> of <paramref name="frame"/> (frame
    /// pixels; the frame in region pixels): the captured or padded texture when the part is all of
    /// it, else a copy of the part, so that shaders clamp at its edges. A frame inside the region is
    /// read from the captured texture; one going beyond it from a copy with black around.
    /// </summary>
    private ID3D11ShaderResourceView SourceView(Rectangle frame, Rectangle part)
    {
        var region = new Rectangle(0, 0, CapturedWidth, CapturedHeight);
        if (region.Contains(frame))
        {
            part.Offset(frame.Location);
            return part == region ? _capturedView! : CroppedView(_capturedTexture!, _capturedVersion, part);
        }
        UpdatePadded(frame);
        return part == new Rectangle(Point.Empty, frame.Size) ? _paddedView! : CroppedView(_paddedTexture!, _paddedVersion, part);
    }

    /// <summary>The frame with black where it goes beyond the captured region; the black is drawn when the layout changes.</summary>
    private void UpdatePadded(Rectangle frame)
    {
        if (_paddedTexture == null || frame.Size != _paddedFrame.Size)
        {
            _paddedTarget?.Dispose();
            _paddedView?.Dispose();
            _paddedTexture?.Dispose();
            _paddedTexture = Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)frame.Width, (uint)frame.Height, 1, 1,
                BindFlags.ShaderResource | BindFlags.RenderTarget));
            _paddedView = Device.CreateShaderResourceView(_paddedTexture);
            _paddedTarget = Device.CreateRenderTargetView(_paddedTexture);
            _paddedFrame = Rectangle.Empty;
        }
        var regionSize = new System.Drawing.Size(CapturedWidth, CapturedHeight);
        bool layout = frame != _paddedFrame || regionSize != _paddedRegion;
        if (!layout && _paddedFromVersion == _capturedVersion)
            return;
        if (layout)
            Context.ClearRenderTargetView(_paddedTarget!, new Color4(0f, 0f, 0f, 1f));
        var visible = Rectangle.Intersect(frame, new Rectangle(Point.Empty, regionSize));
        if (visible.Width > 0 && visible.Height > 0)
        {
            Context.CopySubresourceRegion(_paddedTexture, 0, (uint)(visible.X - frame.X), (uint)(visible.Y - frame.Y), 0, _capturedTexture!, 0,
                new Box(visible.Left, visible.Top, 0, visible.Right, visible.Bottom, 1));
        }
        _paddedFrame = frame;
        _paddedRegion = regionSize;
        _paddedFromVersion = _capturedVersion;
        _paddedVersion++;
    }

    /// <summary>A copy of <paramref name="part"/> of <paramref name="source"/>, made again when the source's content changed.</summary>
    private ID3D11ShaderResourceView CroppedView(ID3D11Texture2D source, long version, Rectangle part)
    {
        if (_croppedTexture == null || part.Size != _croppedFrom.Size)
        {
            _croppedView?.Dispose();
            _croppedTexture?.Dispose();
            _croppedTexture = Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)part.Width, (uint)part.Height, 1, 1));
            _croppedView = Device.CreateShaderResourceView(_croppedTexture);
            _croppedVersion = -1;
        }
        if (_croppedVersion != version || part != _croppedFrom || !ReferenceEquals(source, _croppedSource))
        {
            Context.CopySubresourceRegion(_croppedTexture, 0, 0, 0, 0, source, 0,
                new Box(part.Left, part.Top, 0, part.Right, part.Bottom, 1));
            _croppedFrom = part;
            _croppedVersion = version;
            _croppedSource = source;
        }
        return _croppedView!;
    }

    /// <summary>
    /// One line in two of <paramref name="source"/> (<paramref name="size"/> pixels): the even ones
    /// for <paramref name="field"/> 0, the odd ones for 1, into a texture half as high (rounded up).
    /// </summary>
    private ID3D11ShaderResourceView FieldView(ID3D11ShaderResourceView source, System.Drawing.Size size, int field)
    {
        int height = (size.Height + 1) / 2;
        if (_fieldTexture == null || _fieldTexture.Description.Width != size.Width || _fieldTexture.Description.Height != height)
        {
            _fieldTarget?.Dispose();
            _fieldView?.Dispose();
            _fieldTexture?.Dispose();
            _fieldTexture = Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)size.Width, (uint)height, 1, 1,
                BindFlags.ShaderResource | BindFlags.RenderTarget));
            _fieldView = Device.CreateShaderResourceView(_fieldTexture);
            _fieldTarget = Device.CreateRenderTargetView(_fieldTexture);
        }
        Context.OMSetRenderTargets(_fieldTarget!);
        Context.RSSetViewport(new Viewport(0, 0, size.Width, height));
        Context.RSSetState(_rasterizerState);
        Context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        Context.IASetInputLayout(null);
        Context.VSSetShader(_vertexShader);
        Context.PSSetShader(_fieldShaders[field & 1]);
        Context.PSSetShaderResource(0, source);
        Context.Draw(3, 0);
        Context.PSSetShaderResource(0, null!);
        return _fieldView!;
    }

    /// <summary>
    /// Clears the target to black and draws the picture where <paramref name="placement"/> says (target
    /// pixels). <paramref name="field"/>: which lines an interlaced placement shows (0 even, 1 odd).
    /// </summary>
    public void Draw(ID3D11RenderTargetView target, Placement placement, int field = 0)
    {
        Context.ClearRenderTargetView(target, new Color4(0f, 0f, 0f, 1f));
        RectangleF dest = placement.Dest;
        Rectangle frame = placement.Frame ?? new Rectangle(0, 0, CapturedWidth, CapturedHeight);
        Rectangle part = Rectangle.Intersect(placement.Source, new Rectangle(Point.Empty, frame.Size));
        if (_capturedView == null || _pixelShader == null || dest.Width < 1 || dest.Height < 1 || part.Width < 1 || part.Height < 1)
            return;
        var sourceView = SourceView(frame, part);
        int sourceHeight = part.Height;
        if (placement.Interlaced && part.Height >= 2)
        {
            // The vertex shader mirrors with the constants: unmirrored for the field.
            var straight = new ShaderConstants { MirrorSignX = 1f, MirrorSignY = 1f };
            Context.UpdateSubresource(in straight, _constantBuffer);
            Context.VSSetConstantBuffer(0, _constantBuffer);
            sourceView = FieldView(sourceView, part.Size, field);
            sourceHeight = (part.Height + 1) / 2;
        }

        var constants = new ShaderConstants
        {
            SourceWidth = part.Width,
            SourceHeight = sourceHeight,
            InvSourceWidth = 1f / part.Width,
            InvSourceHeight = 1f / sourceHeight,
            OutputWidth = dest.Width,
            OutputHeight = dest.Height,
            InvOutputWidth = 1f / dest.Width,
            InvOutputHeight = 1f / dest.Height,
            Time = (float)_clock.Elapsed.TotalSeconds,
            FrameCount = _frameCount++,
            OutputX = dest.X,
            OutputY = dest.Y,
            MirrorSignX = placement.MirrorX ? -1f : 1f,
            MirrorSignY = placement.MirrorY ? -1f : 1f,
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
        Context.VSSetConstantBuffer(0, _constantBuffer);
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
    public Bitmap RenderToBitmap(int width, int height, Placement placement, int field = 0)
    {
        var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget);
        using var target = Device.CreateTexture2D(desc);
        using var view = Device.CreateRenderTargetView(target);
        Draw(view, placement, field);
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
        _paddedTarget?.Dispose();
        _paddedView?.Dispose();
        _paddedTexture?.Dispose();
        _fieldTarget?.Dispose();
        _fieldView?.Dispose();
        _fieldTexture?.Dispose();
        foreach (var shader in _fieldShaders)
            shader.Dispose();
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
