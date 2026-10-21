using SuperResScaler.Config;
using SuperResScaler.Native;

namespace SuperResScaler.Engine;

internal static class Geometry
{
    /// <summary>
    /// Where the source image goes on the output. Stretch fills everything; KeepAspect fits the
    /// source aspect ratio on a screen whose physical shape is DisplayAspect (so a 1920x240
    /// "4:3" CRT has pixels 6 times taller than wide).
    /// </summary>
    public static RectangleF ComputeDestination(ScalingOptions options, int sourceWidth, int sourceHeight, int outputWidth, int outputHeight)
    {
        var full = new RectangleF(0, 0, outputWidth, outputHeight);
        if (options.Mode == ScaleMode.Stretch || sourceWidth <= 0 || sourceHeight <= 0)
            return full;

        if (!AspectRatio.TryParse(options.DisplayAspect, out double displayAspect))
            displayAspect = (double)outputWidth / outputHeight;
        double sourceAspect = AspectRatio.IsAuto(options.SourceAspect) || !AspectRatio.TryParse(options.SourceAspect, out double parsed)
            ? (double)sourceWidth / sourceHeight
            : parsed;

        // Physical width of one output pixel relative to its height.
        double pixelAspect = displayAspect / ((double)outputWidth / outputHeight);

        double width = sourceAspect * outputHeight / pixelAspect;
        double height = outputHeight;
        if (width > outputWidth)
        {
            width = outputWidth;
            height = outputWidth * pixelAspect / sourceAspect;
        }
        float x = (float)Math.Round((outputWidth - width) / 2);
        float y = (float)Math.Round((outputHeight - height) / 2);
        return new RectangleF(x, y, (float)Math.Round(width), (float)Math.Round(height));
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
