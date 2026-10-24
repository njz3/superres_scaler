// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SuperResScaler.App;

/// <summary>Small generated icon (window and tray): a 4:3 frame with a horizontal stretch arrow.</summary>
internal static class AppIcon
{
    private static Icon? _icon;

    public static Icon Get() => _icon ??= Create();

    private static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(255, 32, 48, 96));
            g.FillRectangle(background, 1, 4, 30, 24);
            using var frame = new Pen(Color.FromArgb(255, 120, 200, 255), 2);
            g.DrawRectangle(frame, 2, 5, 28, 22);
            using var arrow = new Pen(Color.White, 3) { StartCap = LineCap.ArrowAnchor, EndCap = LineCap.ArrowAnchor };
            g.DrawLine(arrow, 6, 16, 26, 16);
        }
        IntPtr handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }
}
