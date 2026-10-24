using System.Globalization;
using System.Text.Json.Serialization;
using SuperResScaler.Native;
using SuperResScaler.Rendering;

namespace SuperResScaler.Config;

/// <summary>How the picture's frame is placed on the screen (linuxloader's DISPLAY_MODE).</summary>
public enum ScaleMode
{
    /// <summary>The frame fills the whole screen (FULLSCREEN_STRETCH), e.g. 320x240 -> 1920x240 = full 4:3 on the CRT.</summary>
    Stretch,

    /// <summary>The frame keeps its shape on the physical screen, with black bars (FULLSCREEN).</summary>
    KeepAspect,
}

/// <summary>How a window is captured and scaled.</summary>
public class ScalingOptions
{
    /// <summary>Pixel shader name (file name in the Shaders folder, without extension).</summary>
    public string Shader { get; set; } = ShaderLibrary.DefaultShader;

    public ScaleMode Mode { get; set; } = ScaleMode.Stretch;

    /// <summary>
    /// Physical shape of the screen: "auto" (4:3 for a super-resolution mode such as 1920x240,
    /// square pixels otherwise) or a ratio such as "4:3". Used by KeepAspect.
    /// </summary>
    public string DisplayAspect { get; set; } = "auto";

    /// <summary>
    /// Shape of the frame and how the picture goes in it, as linuxloader's ASPECT_RATIO:
    /// "original" (the window's shape), "4:3" / "16:9" / any ratio (the picture whole inside at its
    /// own shape, black bars; "_letterbox" is the same), "4:3_stretch" (the picture fills the frame,
    /// deformed) or "4:3_crop" (it fills the frame at its shape, the overflow cut, see CropPosition).
    /// </summary>
    public string AspectRatio { get; set; } = "original";

    /// <summary>The part kept by the "_crop" formats: -100 left or top, 0 the middle, 100 right or bottom.</summary>
    public int CropPosition { get; set; }

    /// <summary>"_crop" formats: bend the parts the crop would cut onto the frame's edges (a cylinder seen from the front).</summary>
    public bool CropBarrel { get; set; }

    /// <summary>The frame's middle shown undistorted by the barrel, in % of the frame (100 = plain crop).</summary>
    public int BarrelCenter { get; set; } = 60;

    /// <summary>The barrel's radius in % of the frame: small bends hard at the edge, large compresses evenly.</summary>
    public int BarrelRadius { get; set; } = 50;

    /// <summary>Capture only the client area (no title bar or borders).</summary>
    public bool ClientAreaOnly { get; set; } = true;

    public int CropLeft { get; set; }
    public int CropTop { get; set; }
    public int CropRight { get; set; }
    public int CropBottom { get; set; }

    /// <summary>Detect and remove black bars (e.g. a 4:3 game pillarboxed in a wide window).</summary>
    public bool AutoCrop { get; set; }

    public bool CaptureCursor { get; set; }

    public bool VSync { get; set; } = true;

    /// <summary>Output monitor: 0 = the monitor of the source window, otherwise 1..N.</summary>
    public int Monitor { get; set; }

    /// <summary>
    /// Display mode set on the output monitor while scaling, e.g. "1920x240@60" or "1280x480i";
    /// empty = unchanged. The desktop's previous mode comes back when the scaling stops.
    /// </summary>
    public string Resolution { get; set; } = string.Empty;

    /// <summary>Render into a normal window instead of a full-screen overlay (for testing on a desktop PC).</summary>
    public bool Preview { get; set; }

    public int PreviewWidth { get; set; } = 1920;
    public int PreviewHeight { get; set; } = 240;

    public ScalingOptions Snapshot()
    {
        var copy = new ScalingOptions();
        copy.CopyFrom(this);
        return copy;
    }

    public void CopyFrom(ScalingOptions other)
    {
        Shader = other.Shader;
        Mode = other.Mode;
        DisplayAspect = other.DisplayAspect;
        AspectRatio = other.AspectRatio;
        CropPosition = other.CropPosition;
        CropBarrel = other.CropBarrel;
        BarrelCenter = other.BarrelCenter;
        BarrelRadius = other.BarrelRadius;
        ClientAreaOnly = other.ClientAreaOnly;
        CropLeft = other.CropLeft;
        CropTop = other.CropTop;
        CropRight = other.CropRight;
        CropBottom = other.CropBottom;
        AutoCrop = other.AutoCrop;
        CaptureCursor = other.CaptureCursor;
        VSync = other.VSync;
        Monitor = other.Monitor;
        Resolution = other.Resolution;
        Preview = other.Preview;
        PreviewWidth = other.PreviewWidth;
        PreviewHeight = other.PreviewHeight;
    }
}

/// <summary>Options applied automatically to the windows of one application.</summary>
public sealed class Profile : ScalingOptions
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Executable(s), as BackForceFeeder's process filter: "game.exe", wildcards "game*.exe",
    /// alternatives "game.exe|game_dx11.exe". Case-insensitive, ".exe" optional.
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// Optional window title filter: wildcards and "|" alternatives; a text without wildcard only
    /// has to be contained in the title. Child windows' titles count too. Empty = any window.
    /// </summary>
    public string? WindowTitle { get; set; }

    /// <summary>Start scaling as soon as a matching window exists (while the profiles are watched).</summary>
    public bool AutoScale { get; set; } = true;

    /// <summary>Seconds a matching window must exist before scaling (games often resize right after starting).</summary>
    public double AutoScaleDelay { get; set; } = 1;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrEmpty(Name) ? ProcessName : Name;

    [JsonIgnore]
    internal bool HasTitleFilter => !string.IsNullOrWhiteSpace(WindowTitle);

    /// <summary>The process matches, and the window's title (or one of its children's) when a filter is set.</summary>
    internal bool Matches(WindowInfo window, bool includeChildren = false)
    {
        if (!WindowMatcher.MatchesProcess(ProcessName, window.ProcessName))
            return false;
        if (WindowMatcher.MatchesTitle(WindowTitle, window.Title))
            return true;
        return includeChildren && WindowInfo.GetChildTitles(window.Handle).Any(t => WindowMatcher.MatchesTitle(WindowTitle, t));
    }

    public Profile Clone()
    {
        var copy = new Profile { Name = Name, ProcessName = ProcessName, WindowTitle = WindowTitle, AutoScale = AutoScale, AutoScaleDelay = AutoScaleDelay };
        copy.CopyFrom(this);
        return copy;
    }
}

/// <summary>Process and window title filters, as BackForceFeeder's ProcessAnalyzer.</summary>
public static class WindowMatcher
{
    private static readonly char[] Wildcards = ['*', '?'];

    /// <summary>"game.exe", "game*", "a.exe|b.exe" against an executable name; an empty filter matches nothing.</summary>
    public static bool MatchesProcess(string? filter, string processName)
    {
        if (string.IsNullOrWhiteSpace(filter) || string.IsNullOrEmpty(processName))
            return false;
        string name = WithoutExe(processName);
        return Alternatives(filter).Any(pattern => Matches(WithoutExe(pattern), name, containsWhenPlain: false));
    }

    /// <summary>Wildcards and "|" alternatives; a plain text is searched in the title. An empty filter matches any title.</summary>
    public static bool MatchesTitle(string? filter, string title)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;
        return Alternatives(filter).Any(pattern => Matches(pattern, title, containsWhenPlain: true));
    }

    private static IEnumerable<string> Alternatives(string filter)
        => filter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string WithoutExe(string name)
    {
        name = Path.GetFileName(name.Trim());
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static bool Matches(string pattern, string text, bool containsWhenPlain)
    {
        if (pattern.IndexOfAny(Wildcards) < 0)
        {
            return containsWhenPlain
                ? text.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                : text.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }
        string regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(text, regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }
}

/// <summary>How the picture goes in its frame (linuxloader's ASPECT_RATIO families).</summary>
public enum PictureAspectKind
{
    /// <summary>ORIGINAL: the frame has the captured window's shape (square pixels).</summary>
    Original,

    /// <summary>4:3, 16:9, 4:3_LETTERBOX: the picture whole inside the frame at its own shape, black bars.</summary>
    Letterbox,

    /// <summary>4:3_STRETCH: the picture fills the frame, deformed.</summary>
    Stretch,

    /// <summary>4:3_CROP: the picture fills the frame at its own shape, the overflow cut (or bent, CropBarrel).</summary>
    Crop,
}

/// <summary>Parsed <see cref="ScalingOptions.AspectRatio"/>.</summary>
public readonly record struct PictureAspect(PictureAspectKind Kind, double Ratio)
{
    public static readonly PictureAspect Original = new(PictureAspectKind.Original, 0);

    private static readonly (string Suffix, PictureAspectKind Kind)[] Suffixes =
    [
        ("letterbox", PictureAspectKind.Letterbox),
        ("stretch", PictureAspectKind.Stretch),
        ("crop", PictureAspectKind.Crop),
    ];

    /// <summary>Parses "original", "4:3", "4:3_letterbox", "4:3_stretch", "4:3_crop" (any ratio, any case, '_', '-' or ' ').</summary>
    public static bool TryParse(string? text, out PictureAspect aspect)
    {
        aspect = Original;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        string value = text.Trim();
        if (value.Equals("original", StringComparison.OrdinalIgnoreCase) || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return true;

        var kind = PictureAspectKind.Letterbox;
        foreach (var (suffix, suffixKind) in Suffixes)
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                kind = suffixKind;
                value = value[..^suffix.Length].TrimEnd(' ', '_', '-');
                break;
            }
        }
        if (!SuperResScaler.Config.Ratio.TryParse(value, out double ratio))
            return false;
        aspect = new PictureAspect(kind, ratio);
        return true;
    }
}

public static class Ratio
{
    /// <summary>Parses "4:3", "16/9", "1.333" (or "1,333").</summary>
    public static bool TryParse(string? text, out double ratio)
    {
        ratio = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        text = text.Trim().Replace(',', '.');
        int separator = text.IndexOfAny([':', '/', 'x']);
        if (separator > 0)
        {
            if (double.TryParse(text[..separator], NumberStyles.Float, CultureInfo.InvariantCulture, out double w)
                && double.TryParse(text[(separator + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double h)
                && w > 0 && h > 0)
            {
                ratio = w / h;
                return true;
            }
            return false;
        }
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out ratio) && ratio > 0;
    }

    public static bool IsAuto(string? text) => string.IsNullOrWhiteSpace(text) || text.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase);
}
