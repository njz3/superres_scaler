using SuperResScaler.Config;
using SuperResScaler.Native;

namespace SuperResScaler.Engine;

/// <summary>
/// The picture on the output: drawn into <paramref name="Dest"/> (output pixels) from the
/// <paramref name="Source"/> part of the image (source pixels), or bent along <paramref name="Barrel"/>.
/// </summary>
internal sealed record Placement(RectangleF Dest, Rectangle Source, BarrelStrip? Barrel);

/// <summary>
/// CROP_BARREL, ported from linuxloader's blitStretching.c (barrelSetup, barrelArc, barrelSource):
/// the "_crop" formats keep the frame's middle (BarrelCenter of it) exactly as the plain crop shows
/// it, and bend the parts the crop would cut onto the frame's edges, as a picture wrapped on a
/// cylinder seen from the front: a point at the arc s from the middle's edge shows at sin(s / R),
/// scaled so that the picture's edge lands on the frame's; a quarter turn at most. Along the cut
/// axis only, in frames: the crop keeps a share k of the picture, which is 1 / k frames long.
/// </summary>
internal sealed class BarrelStrip
{
    /// <summary>Steps per edge, as linuxloader's quad strip; the middle is one straight piece.</summary>
    public const int StepsPerEdge = 64;

    private readonly double _share;      // k, the share of the picture the crop keeps
    private readonly double _edge;       // each edge's width, in frames
    private readonly double _startLeft;  // where the middle starts in the picture, 0 to 1 (left or top)
    private readonly double _startRight; // where it ends
    private readonly double _arcLeft;    // the picture beyond the middle on each side, in frames
    private readonly double _arcRight;
    private readonly double _angleLeft;  // the turn of the cylinder each side's picture takes, in radians
    private readonly double _angleRight;

    private BarrelStrip(bool horizontal, double share, double kept, double center, double radius)
    {
        Horizontal = horizontal;
        center = Math.Clamp(center, 0, 1);
        radius = Math.Clamp(radius, 0.01, 10);
        double start = (1 - share) * kept; // the crop's window in the picture
        _share = share;
        _edge = (1 - center) / 2;
        _startLeft = start + share * (1 - center) / 2;
        _startRight = start + share * (1 + center) / 2;
        _arcLeft = _startLeft / share;
        _arcRight = (1 - _startRight) / share;
        // An edge with more picture than room bends; with less it only stretches.
        _angleLeft = _arcLeft > _edge ? Math.Min(_arcLeft / radius, Math.PI / 2) : 0;
        _angleRight = _arcRight > _edge ? Math.Min(_arcRight / radius, Math.PI / 2) : 0;

        var points = new List<PointF>();
        for (int i = 0; i <= StepsPerEdge; i++)
            Add(points, _edge * i / StepsPerEdge);
        for (int i = 0; i <= StepsPerEdge; i++)
            Add(points, 1 - _edge + _edge * i / StepsPerEdge);
        Points = points;
    }

    /// <summary>True when the sides are bent (a picture wider than its frame), false for above and below.</summary>
    public bool Horizontal { get; }

    /// <summary>Along the bent axis: X = position in the frame, Y = position in the picture, both 0 to 1.</summary>
    public IReadOnlyList<PointF> Points { get; }

    public static BarrelStrip Create(bool horizontal, double share, double kept, double center, double radius)
        => new(horizontal, share, kept, center, radius);

    /// <summary>The picture position (0 to 1) shown at <paramref name="frame"/> (0 to 1) along the bent axis.</summary>
    public double SourceAt(double frame)
    {
        if (frame < _edge)
            return _startLeft - _share * Arc(_edge - frame, _arcLeft, _angleLeft);
        if (frame > 1 - _edge)
            return _startRight + _share * Arc(frame - (1 - _edge), _arcRight, _angleRight);
        return _startLeft + _share * (frame - _edge);
    }

    /// <summary>x from the middle's edge outward, 0 to the edge's width: the arc of picture shown there.</summary>
    private double Arc(double x, double arc, double angle)
    {
        if (_edge <= 0)
            return 0;
        if (angle < 1e-4)
            return arc * x / _edge;
        return arc * Math.Asin(Math.Sin(angle) * x / _edge) / angle;
    }

    private void Add(List<PointF> points, double frame) => points.Add(new PointF((float)frame, (float)SourceAt(frame)));
}

internal static class Geometry
{
    /// <summary>Highest mode treated as a 15/25 kHz CRT mode by the "auto" display aspect.</summary>
    private const int MaxCrtLines = 600;

    /// <summary>
    /// Physical shape of the output. "auto": a mode of at most 600 lines more than twice as wide as
    /// high (1280x240, 1920x240, 2560x240, 1920x480i…) is a super-resolution CRT mode, hence 4:3;
    /// anything else has square pixels (its shape is its resolution).
    /// </summary>
    public static double ResolveDisplayAspect(string? setting, int outputWidth, int outputHeight)
    {
        bool auto = string.IsNullOrWhiteSpace(setting) || setting.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase);
        if (!auto && Ratio.TryParse(setting, out double ratio))
            return ratio;
        double pixelShape = (double)outputWidth / outputHeight;
        return outputHeight <= MaxCrtLines && pixelShape > 2 ? 4.0 / 3.0 : pixelShape;
    }

    /// <summary>Where the picture goes on the output (see <see cref="ComputePlacement"/>).</summary>
    public static RectangleF ComputeDestination(ScalingOptions options, int sourceWidth, int sourceHeight, int outputWidth, int outputHeight)
        => ComputePlacement(options, sourceWidth, sourceHeight, outputWidth, outputHeight).Dest;

    /// <summary>
    /// Where the picture goes on the output, as linuxloader's blitPresent:
    /// <list type="number">
    /// <item>the frame (the AspectRatio's shape, or the window's for "original") fills the screen
    /// (Stretch) or keeps its shape on the physical screen with black bars (KeepAspect). The
    /// physical shape matters on a super-resolution mode: a 1920x240 4:3 CRT has pixels 6 times
    /// taller than wide;</item>
    /// <item>the picture in the frame: whole at its own shape (letterbox, as shares of the frame so
    /// that a stretched frame still shows the picture at its true shape on a screen of the frame's
    /// shape), deformed to fill it (stretch), or cut to the frame's shape (crop, the kept part placed
    /// by CropPosition) — or, with CropBarrel, whole with the cut parts bent onto the edges.</item>
    /// </list>
    /// </summary>
    public static Placement ComputePlacement(ScalingOptions options, int sourceWidth, int sourceHeight, int outputWidth, int outputHeight)
    {
        var whole = new Rectangle(0, 0, Math.Max(0, sourceWidth), Math.Max(0, sourceHeight));
        if (sourceWidth <= 0 || sourceHeight <= 0 || outputWidth <= 0 || outputHeight <= 0)
            return new Placement(new RectangleF(0, 0, outputWidth, outputHeight), whole, null);

        if (!PictureAspect.TryParse(options.AspectRatio, out var aspect))
            aspect = PictureAspect.Original;
        double pictureShape = (double)sourceWidth / sourceHeight;
        double frameShape = aspect.Kind == PictureAspectKind.Original ? pictureShape : aspect.Ratio;
        bool sameShape = Math.Abs(pictureShape - frameShape) <= frameShape * 1e-3;

        double x = 0, y = 0, width = outputWidth, height = outputHeight;
        if (options.Mode == ScaleMode.KeepAspect)
        {
            // Physical width of one output pixel relative to its height.
            double pixelAspect = ResolveDisplayAspect(options.DisplayAspect, outputWidth, outputHeight) / ((double)outputWidth / outputHeight);
            width = frameShape * outputHeight / pixelAspect;
            if (width > outputWidth)
            {
                width = outputWidth;
                height = outputWidth * pixelAspect / frameShape;
            }
            x = (outputWidth - width) / 2;
            y = (outputHeight - height) / 2;
        }

        Rectangle source = whole;
        BarrelStrip? barrel = null;
        if (!sameShape && aspect.Kind == PictureAspectKind.Letterbox)
        {
            if (pictureShape > frameShape)
            {
                double pictureHeight = height * frameShape / pictureShape;
                y += (height - pictureHeight) / 2;
                height = pictureHeight;
            }
            else
            {
                double pictureWidth = width * pictureShape / frameShape;
                x += (width - pictureWidth) / 2;
                width = pictureWidth;
            }
        }
        else if (!sameShape && aspect.Kind == PictureAspectKind.Crop)
        {
            double kept = (Math.Clamp(options.CropPosition, -100, 100) + 100) / 200.0; // 0 left or top, 1 right or bottom
            bool horizontal = pictureShape > frameShape; // too wide: the sides go; too tall: above and below
            if (options.CropBarrel)
            {
                double share = horizontal ? frameShape / pictureShape : pictureShape / frameShape;
                barrel = BarrelStrip.Create(horizontal, share, kept, options.BarrelCenter / 100.0, options.BarrelRadius / 100.0);
            }
            else if (horizontal)
            {
                int keptWidth = (int)Math.Round(sourceHeight * frameShape);
                source = new Rectangle((int)Math.Round((sourceWidth - keptWidth) * kept), 0, keptWidth, sourceHeight);
            }
            else
            {
                int keptHeight = (int)Math.Round(sourceWidth / frameShape);
                source = new Rectangle(0, (int)Math.Round((sourceHeight - keptHeight) * kept), sourceWidth, keptHeight);
            }
        }

        // Whole pixels, edges rounded independently so that bars on both sides stay even.
        float left = (float)Math.Round(x), top = (float)Math.Round(y);
        var dest = RectangleF.FromLTRB(left, top, (float)Math.Round(x + width), (float)Math.Round(y + height));
        return new Placement(dest, source, barrel);
    }

    /// <summary>
    /// Region of the captured texture to scale: the client area (or whole window) minus the
    /// manual crop margins, clamped to the captured content.
    /// The capture origin is the window's visible frame (DWM extended frame bounds).
    /// </summary>
    public static Rectangle ComputeSourceRegion(IntPtr hwnd, ScalingOptions options, int contentWidth, int contentHeight)
    {
        if (!Dwmapi.TryGetFrameBounds(hwnd, out RECT frame))
            return Rectangle.Empty;

        RECT area = frame;
        if (options.ClientAreaOnly && User32.GetClientRect(hwnd, out RECT client))
        {
            var origin = new POINT();
            User32.ClientToScreen(hwnd, ref origin);
            area = new RECT(origin.X, origin.Y, origin.X + client.Width, origin.Y + client.Height);
        }

        int left = area.Left - frame.Left + Math.Max(0, options.CropLeft);
        int top = area.Top - frame.Top + Math.Max(0, options.CropTop);
        int right = area.Right - frame.Left - Math.Max(0, options.CropRight);
        int bottom = area.Bottom - frame.Top - Math.Max(0, options.CropBottom);

        left = Math.Clamp(left, 0, contentWidth);
        top = Math.Clamp(top, 0, contentHeight);
        right = Math.Clamp(right, left, contentWidth);
        bottom = Math.Clamp(bottom, top, contentHeight);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}
