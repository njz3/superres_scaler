using System.Globalization;
using System.Text.Json.Serialization;
using SuperResScaler.Native;
using SuperResScaler.Rendering;

namespace SuperResScaler.Config;

public enum ScaleMode
{
    /// <summary>Fill the whole screen (e.g. 320x240 -> 1920x240, i.e. full 4:3 on the CRT).</summary>
    Stretch,

    /// <summary>Respect the source aspect ratio given the physical display aspect ratio (letter/pillarbox).</summary>
    KeepAspect,
}

/// <summary>How a window is captured and scaled.</summary>
public class ScalingOptions
{
    /// <summary>Pixel shader name (file name in the Shaders folder, without extension).</summary>
    public string Shader { get; set; } = ShaderLibrary.DefaultShader;

    public ScaleMode Mode { get; set; } = ScaleMode.Stretch;

    /// <summary>Physical aspect ratio of the screen, e.g. "4:3" for a CRT running 1920x240.</summary>
    public string DisplayAspect { get; set; } = "4:3";

    /// <summary>Aspect ratio of the image, "auto" = from its pixel size (square pixels). KeepAspect mode only.</summary>
    public string SourceAspect { get; set; } = "auto";

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
        SourceAspect = other.SourceAspect;
        ClientAreaOnly = other.ClientAreaOnly;
        CropLeft = other.CropLeft;
        CropTop = other.CropTop;
        CropRight = other.CropRight;
        CropBottom = other.CropBottom;
        AutoCrop = other.AutoCrop;
        CaptureCursor = other.CaptureCursor;
        VSync = other.VSync;
        Monitor = other.Monitor;
        Preview = other.Preview;
        PreviewWidth = other.PreviewWidth;
        PreviewHeight = other.PreviewHeight;
    }
}

/// <summary>Options applied automatically to the windows of one application.</summary>
public sealed class Profile : ScalingOptions
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Executable file name, e.g. "game.exe" (case-insensitive).</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Optional: only windows whose title contains this text.</summary>
    public string? TitleContains { get; set; }

    /// <summary>Start scaling automatically when a matching window comes to the foreground.</summary>
    public bool AutoScale { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrEmpty(Name) ? ProcessName : Name;

    internal bool Matches(WindowInfo window)
    {
        if (!string.Equals(ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase))
            return false;
        return string.IsNullOrEmpty(TitleContains)
            || window.Title.Contains(TitleContains, StringComparison.OrdinalIgnoreCase);
    }
}

public static class AspectRatio
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
