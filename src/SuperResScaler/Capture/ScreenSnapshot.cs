// Copyright (c) Benjamin Maurin (njz3) 2026

using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SuperResScaler.Native;

namespace SuperResScaler.Capture;

/// <summary>
/// A small copy of a part of the desktop, shrunk by GDI while it is copied, for thumbnails taken a
/// few times a second at most. Not for the scaling itself, which uses Windows.Graphics.Capture.
/// </summary>
internal static class ScreenSnapshot
{
    /// <summary>Wider than this, a display is taken for a super-resolution mode (1280x240, 1024x384...).</summary>
    public const double SuperResolutionAspect = 2.5;

    /// <summary>
    /// Size of the picture of a <paramref name="width"/> x <paramref name="height"/> display in a box:
    /// its own proportions, except super-resolution modes, shown at 4:3 as on the cabinet's tube.
    /// </summary>
    public static Size Fit(int width, int height, Size box)
    {
        if (width <= 0 || height <= 0 || box.Width <= 0 || box.Height <= 0)
            return Size.Empty;
        double aspect = width / (double)height;
        if (aspect > SuperResolutionAspect)
            aspect = 4.0 / 3.0;
        return aspect >= box.Width / (double)box.Height
            ? new Size(box.Width, Math.Max(1, (int)Math.Round(box.Width / aspect)))
            : new Size(Math.Max(1, (int)Math.Round(box.Height * aspect)), box.Height);
    }

    /// <summary>Copies <paramref name="source"/> (screen pixels) shrunk to <paramref name="size"/>.</summary>
    public static Bitmap Take(Rectangle source, Size size)
    {
        var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            IntPtr destination = graphics.GetHdc();
            try
            {
                IntPtr screen = User32.GetDC(IntPtr.Zero);
                if (screen == IntPtr.Zero)
                    throw new Win32Exception("GetDC");
                try
                {
                    // HALFTONE averages the source pixels: small text stays readable, lines do not flicker.
                    Gdi32.SetStretchBltMode(destination, Gdi32.HALFTONE);
                    Gdi32.SetBrushOrgEx(destination, 0, 0, IntPtr.Zero);
                    if (!Gdi32.StretchBlt(destination, 0, 0, size.Width, size.Height,
                            screen, source.X, source.Y, source.Width, source.Height, Gdi32.SRCCOPY))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                finally
                {
                    User32.ReleaseDC(IntPtr.Zero, screen);
                }
            }
            finally
            {
                graphics.ReleaseHdc(destination);
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
