// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SuperResScaler.UI;

/// <summary>Colours, fonts and icon glyphs of the Magpie-like (Windows 11 settings) look.</summary>
internal static class Theme
{
    public static readonly Color NavBackground = Color.FromArgb(243, 243, 243);
    public static readonly Color PageBackground = Color.FromArgb(249, 249, 249);
    public static readonly Color CardBackground = Color.White;
    public static readonly Color CardBorder = Color.FromArgb(229, 229, 229);
    public static readonly Color NavSelected = Color.FromArgb(234, 234, 234);
    public static readonly Color NavHover = Color.FromArgb(237, 237, 237);
    public static readonly Color Accent = Color.FromArgb(0, 95, 184);
    public static readonly Color AccentHover = Color.FromArgb(25, 117, 197);
    public static readonly Color Text = Color.FromArgb(26, 26, 26);
    public static readonly Color SecondaryText = Color.FromArgb(96, 96, 96);
    public static readonly Color DisabledText = Color.FromArgb(160, 160, 160);
    public static readonly Color Success = Color.FromArgb(15, 123, 15);

    private static readonly HashSet<string> InstalledFonts =
        new InstalledFontCollection().Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Pick(params string[] names) => names.FirstOrDefault(InstalledFonts.Contains) ?? names[^1];

    private static readonly string TextFamily = Pick("Segoe UI Variable Text", "Segoe UI");
    private static readonly string DisplayFamily = Pick("Segoe UI Variable Display", "Segoe UI");

    /// <summary>Windows 11 icon font, or its Windows 10 predecessor (same code points).</summary>
    public static readonly string IconFamily = Pick("Segoe Fluent Icons", "Segoe MDL2 Assets");

    public static readonly Font Body = new(TextFamily, 10f);
    public static readonly Font BodyStrong = new(TextFamily, 10f, FontStyle.Bold);
    public static readonly Font Caption = new(TextFamily, 8.75f);
    public static readonly Font Section = new(TextFamily, 10.5f, FontStyle.Bold);
    public static readonly Font PageTitle = new(DisplayFamily, 21f, FontStyle.Bold);
    public static readonly Font AppTitle = new(TextFamily, 10f);
    public static readonly Font Icon = new(IconFamily, 13f);
    public static readonly Font SmallIcon = new(IconFamily, 10f);

    // Segoe MDL2 Assets / Segoe Fluent Icons code points.
    public const string GlyphHome = "";
    public const string GlyphSettings = "";
    public const string GlyphInfo = "";
    public const string GlyphAdd = "";
    public const string GlyphLayers = "";
    public const string GlyphGame = "";
    public const string GlyphFullScreen = "";
    public const string GlyphTimer = "";
    public const string GlyphWindow = "";
    public const string GlyphEye = "";
    public const string GlyphEdit = "";
    public const string GlyphDelete = "";
    public const string GlyphCopy = "";
    public const string GlyphFolder = "";
    public const string GlyphMonitor = "";
    public const string GlyphCrop = "";
    public const string GlyphPicture = "";
    public const string GlyphSearch = "";
    public const string GlyphRename = "";
    public const string GlyphColor = "";
    public const string GlyphAspect = "";
    public const string GlyphTag = "";
    public const string GlyphApp = "";
    public const string GlyphMouse = "";
    public const string GlyphSync = "";
    public const string GlyphPreview = "";
    public const string GlyphHistory = "";
    public const string GlyphKeyboard = "";
    public const string GlyphTray = "";
    public const string GlyphPlay = "";
    public const string GlyphStop = "";
    public const string GlyphRefresh = "";

    public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>DPI of the main window; sizes below are given in 96-DPI pixels and scaled with <see cref="S"/>.</summary>
    public static int Dpi { get; set; } = 96;

    public static int S(int pixels) => (int)Math.Round(pixels * Dpi / 96.0);
    public static float S(float pixels) => pixels * Dpi / 96f;
}
