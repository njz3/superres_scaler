// Copyright (c) Benjamin Maurin (njz3) 2026

using System.ComponentModel;
using System.Drawing.Drawing2D;
using SuperResScaler.Engine;

namespace SuperResScaler.UI;

/// <summary>
/// The frame drawn on a picture of the source (Rectangle crop mode): drag a corner to resize it, or
/// the inside to move it. The canvas spans the frame's whole range, -50 % to 150 % of the picture,
/// black around the picture as on the output; outside the frame is dimmed. Values in % of the picture.
/// </summary>
internal sealed class FrameEditor : Control
{
    private const double Range = Geometry.MaxFramePercent - Geometry.MinFramePercent; // the canvas, in % of the picture

    private Bitmap? _picture;
    private Size _shape = new(4, 3);
    private double _left, _top, _right = 100, _bottom = 100;
    private int _drag = -1; // 0..3 the corners (top-left, top-right, bottom-left, bottom-right), 4 the whole frame
    private PointF _dragStart;
    private (double Left, double Top, double Right, double Bottom) _dragFrame;

    public FrameEditor()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(Theme.S(520), Theme.S(300));
        BackColor = Color.FromArgb(32, 32, 32);
    }

    /// <summary>The frame moved or was resized: false while dragging (apply only), true when released (save).</summary>
    public event Action<bool>? FrameChanged;

    /// <summary>A corner or the frame is being dragged.</summary>
    public bool IsDragging => _drag >= 0;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public (double Left, double Top, double Right, double Bottom) Frame
    {
        get => (_left, _top, _right, _bottom);
        set
        {
            (_left, _top, _right, _bottom) = (Clamp(value.Left), Clamp(value.Top), Clamp(value.Right), Clamp(value.Bottom));
            Invalidate();
        }
    }

    /// <summary>The picture of the source (taken by the caller, owned from now on), or null for a plain placeholder of <paramref name="shape"/>.</summary>
    public void SetPicture(Bitmap? picture, Size shape)
    {
        _picture?.Dispose();
        _picture = picture;
        _shape = shape.Width > 0 && shape.Height > 0 ? shape : new Size(4, 3);
        Invalidate();
    }

    /// <summary>The largest picture size that fits half the canvas each way (the canvas spans twice the picture).</summary>
    public Size PictureBox => new(Math.Max(1, (int)(Width * 100 / Range)), Math.Max(1, (int)(Height * 100 / Range)));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _picture?.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Where the picture (0 % to 100 %) lies on the canvas, at its shape.</summary>
    private RectangleF PictureArea()
    {
        float scale = (float)Math.Min(Width * 100 / Range / _shape.Width, Height * 100 / Range / _shape.Height);
        float width = _shape.Width * scale, height = _shape.Height * scale;
        return new RectangleF((Width - width) / 2, (Height - height) / 2, width, height);
    }

    private PointF ToCanvas(double x, double y)
    {
        var area = PictureArea();
        return new PointF(area.X + (float)(x / 100 * area.Width), area.Y + (float)(y / 100 * area.Height));
    }

    private RectangleF FrameOnCanvas()
    {
        PointF a = ToCanvas(Math.Min(_left, _right), Math.Min(_top, _bottom));
        PointF b = ToCanvas(Math.Max(_left, _right), Math.Max(_top, _bottom));
        return RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y);
    }

    private PointF[] Corners() =>
    [
        ToCanvas(_left, _top),
        ToCanvas(_right, _top),
        ToCanvas(_left, _bottom),
        ToCanvas(_right, _bottom),
    ];

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var area = PictureArea();
        // The frame's range around the picture is black on the output: show it as such.
        var range = RectangleF.FromLTRB(ToCanvas(Geometry.MinFramePercent, Geometry.MinFramePercent).X, ToCanvas(Geometry.MinFramePercent, Geometry.MinFramePercent).Y,
                                        ToCanvas(Geometry.MaxFramePercent, Geometry.MaxFramePercent).X, ToCanvas(Geometry.MaxFramePercent, Geometry.MaxFramePercent).Y);
        using (var black = new SolidBrush(Color.Black))
            g.FillRectangle(black, range);

        if (_picture != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(_picture, area);
        }
        else
        {
            using var fill = new SolidBrush(Color.FromArgb(70, 70, 70));
            g.FillRectangle(fill, area);
            using var grid = new Pen(Color.FromArgb(110, 110, 110));
            for (int i = 1; i < 4; i++)
            {
                g.DrawLine(grid, area.X + area.Width * i / 4, area.Y, area.X + area.Width * i / 4, area.Bottom);
                g.DrawLine(grid, area.X, area.Y + area.Height * i / 4, area.Right, area.Y + area.Height * i / 4);
            }
            TextRenderer.DrawText(g, "Pas d'image de la source", Theme.Caption, Rectangle.Round(area), Color.Gainsboro,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
        using (var outline = new Pen(Color.FromArgb(150, 150, 150)) { DashStyle = DashStyle.Dot })
            g.DrawRectangle(outline, area.X, area.Y, area.Width - 1, area.Height - 1);

        // Dim what the frame leaves out, then draw the frame and its corners.
        var frame = FrameOnCanvas();
        using (var outside = new Region(ClientRectangle))
        {
            outside.Exclude(frame);
            using var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            g.FillRegion(dim, outside);
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(Theme.Accent, Theme.S(2f)))
            g.DrawRectangle(pen, frame.X, frame.Y, frame.Width, frame.Height);
        float handle = Theme.S(9f);
        using var handleFill = new SolidBrush(Color.White);
        using var handlePen = new Pen(Theme.Accent, Theme.S(1.5f));
        foreach (var corner in Corners())
        {
            var box = new RectangleF(corner.X - handle / 2, corner.Y - handle / 2, handle, handle);
            g.FillRectangle(handleFill, box);
            g.DrawRectangle(handlePen, box.X, box.Y, box.Width, box.Height);
        }
    }

    private int HitTest(Point point)
    {
        var corners = Corners();
        float reach = Theme.S(10f);
        for (int i = 0; i < corners.Length; i++)
        {
            if (Math.Abs(point.X - corners[i].X) <= reach && Math.Abs(point.Y - corners[i].Y) <= reach)
                return i;
        }
        return FrameOnCanvas().Contains(point) ? 4 : -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        _drag = HitTest(e.Location);
        _dragStart = e.Location;
        _dragFrame = Frame;
        if (_drag >= 0)
            Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag < 0)
        {
            Cursor = HitTest(e.Location) switch
            {
                0 or 3 => Cursors.SizeNWSE,
                1 or 2 => Cursors.SizeNESW,
                4 => Cursors.SizeAll,
                _ => Cursors.Default,
            };
            return;
        }

        var area = PictureArea();
        double dx = (e.X - _dragStart.X) / area.Width * 100, dy = (e.Y - _dragStart.Y) / area.Height * 100;
        var (left, top, right, bottom) = _dragFrame;
        switch (_drag)
        {
            case 0: left += dx; top += dy; break;
            case 1: right += dx; top += dy; break;
            case 2: left += dx; bottom += dy; break;
            case 3: right += dx; bottom += dy; break;
            default:
                // The whole frame, kept within the range.
                dx = Math.Clamp(dx, Geometry.MinFramePercent - Math.Min(left, right), Geometry.MaxFramePercent - Math.Max(left, right));
                dy = Math.Clamp(dy, Geometry.MinFramePercent - Math.Min(top, bottom), Geometry.MaxFramePercent - Math.Max(top, bottom));
                left += dx; right += dx; top += dy; bottom += dy;
                break;
        }
        Frame = (Snap(left), Snap(top), Snap(right), Snap(bottom));
        FrameChanged?.Invoke(false);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_drag < 0)
            return;
        _drag = -1;
        Capture = false;
        FrameChanged?.Invoke(true);
    }

    private static double Clamp(double percent) => Math.Clamp(percent, Geometry.MinFramePercent, Geometry.MaxFramePercent);

    /// <summary>To a tenth of a percent, as the sliders.</summary>
    private static double Snap(double percent) => Math.Round(Clamp(percent), 1);
}
