// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Globalization;
using System.Text.RegularExpressions;

namespace SuperResScaler.Native;

/// <summary>A display mode offered by the driver (as listed by EnumDisplaySettings).</summary>
internal readonly record struct DisplayMode(int Width, int Height, int Frequency, int BitsPerPixel, bool Interlaced)
{
    public override string ToString() => $"{Width}x{Height}{(Interlaced ? "i" : "")}@{Frequency}";
}

/// <summary>A requested resolution: "1920x240", "1920x240@60", "1280x480i", "1280x480i@60".</summary>
internal readonly partial record struct ResolutionRequest(int Width, int Height, bool? Interlaced, int? Frequency)
{
    public static bool TryParse(string? text, out ResolutionRequest request)
    {
        request = default;
        var match = text == null ? Match.Empty : ResolutionRegex().Match(text.Trim());
        if (!match.Success)
            return false;
        int width = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int height = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        bool? interlaced = match.Groups[3].Success ? match.Groups[3].Value.Equals("i", StringComparison.OrdinalIgnoreCase) : null;
        int? frequency = match.Groups[4].Success
            ? (int)Math.Round(double.Parse(match.Groups[4].Value.Replace(',', '.'), CultureInfo.InvariantCulture))
            : null;
        if (width <= 0 || height <= 0 || frequency <= 0)
            return false;
        request = new ResolutionRequest(width, height, interlaced, frequency);
        return true;
    }

    /// <summary>Empty, "(inchangée)" or "none": keep the current mode.</summary>
    public static bool IsUnchanged(string? text)
        => string.IsNullOrWhiteSpace(text) || text.Trim().StartsWith('(') || text.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);

    public override string ToString()
        => $"{Width}x{Height}{(Interlaced == true ? "i" : Interlaced == false ? "p" : "")}{(Frequency is int f ? $"@{f}" : "")}";

    [GeneratedRegex(@"^(\d+)\s*[x×]\s*(\d+)\s*([ip])?\s*(?:@\s*(\d+(?:[.,]\d+)?)\s*(?:hz)?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ResolutionRegex();
}

internal static class DisplayModes
{
    private const uint ModeFields = User32.DM_BITSPERPEL | User32.DM_PELSWIDTH | User32.DM_PELSHEIGHT
                                  | User32.DM_DISPLAYFREQUENCY | User32.DM_DISPLAYFLAGS;

    /// <summary>The modes of a monitor ("\\.\DISPLAY1"), without duplicates, sorted.</summary>
    public static List<DisplayMode> List(string device)
    {
        var modes = new HashSet<DisplayMode>();
        var devmode = DEVMODE.Create();
        for (int i = 0; User32.EnumDisplaySettings(device, i, ref devmode); i++)
            modes.Add(ToMode(devmode));
        return modes.OrderBy(m => m.Width).ThenBy(m => m.Height).ThenBy(m => m.Interlaced)
                    .ThenBy(m => m.Frequency).ThenByDescending(m => m.BitsPerPixel).ToList();
    }

    public static DisplayMode? Current(string device)
    {
        var devmode = DEVMODE.Create();
        return User32.EnumDisplaySettings(device, User32.ENUM_CURRENT_SETTINGS, ref devmode) ? ToMode(devmode) : null;
    }

    /// <summary>
    /// The mode that best matches the request: same size, the requested scan type (progressive
    /// first when unspecified), the requested frequency (±1 Hz, for 59.94 Hz modes), otherwise the
    /// current frequency, then 60 Hz; 32 bits per pixel first.
    /// </summary>
    public static DisplayMode? SelectBest(IEnumerable<DisplayMode> modes, ResolutionRequest request, DisplayMode? current)
    {
        int preferredFrequency = request.Frequency ?? current?.Frequency ?? 60;
        return modes
            .Where(m => m.Width == request.Width && m.Height == request.Height)
            .Where(m => request.Interlaced == null || m.Interlaced == request.Interlaced)
            .Where(m => request.Frequency == null || Math.Abs(m.Frequency - request.Frequency.Value) <= 1)
            .OrderBy(m => m.Interlaced)
            .ThenBy(m => Math.Abs(m.Frequency - preferredFrequency))
            .ThenBy(m => Math.Abs(m.Frequency - 60))
            .ThenByDescending(m => m.BitsPerPixel == 32)
            .ThenByDescending(m => m.BitsPerPixel)
            .Cast<DisplayMode?>()
            .FirstOrDefault();
    }

    /// <summary>Applies a mode. <paramref name="flags"/>: CDS_FULLSCREEN (temporary), CDS_TEST (only checks), or 0.</summary>
    public static int Change(string device, DisplayMode mode, uint flags)
    {
        var devmode = DEVMODE.Create();
        devmode.dmFields = ModeFields;
        devmode.dmPelsWidth = (uint)mode.Width;
        devmode.dmPelsHeight = (uint)mode.Height;
        devmode.dmDisplayFrequency = (uint)mode.Frequency;
        devmode.dmBitsPerPel = (uint)mode.BitsPerPixel;
        devmode.dmDisplayFlags = mode.Interlaced ? User32.DM_INTERLACED : 0;
        return User32.ChangeDisplaySettingsEx(device, ref devmode, IntPtr.Zero, flags, IntPtr.Zero);
    }

    public static string DescribeResult(int result) => result switch
    {
        0 => "OK",
        1 => "redémarrage nécessaire",
        -1 => "le pilote a refusé le mode",
        -2 => "mode non supporté",
        -3 => "impossible d'écrire le registre",
        -4 => "paramètres invalides",
        -5 => "paramètres invalides",
        -6 => "écran en double affichage",
        _ => $"erreur {result}",
    };

    private static DisplayMode ToMode(DEVMODE devmode)
        => new((int)devmode.dmPelsWidth, (int)devmode.dmPelsHeight, (int)devmode.dmDisplayFrequency,
               (int)devmode.dmBitsPerPel, (devmode.dmDisplayFlags & User32.DM_INTERLACED) != 0);
}

/// <summary>
/// Switches a monitor to another mode for the duration of a scaling session (like SetResolution.exe,
/// but temporary: CDS_FULLSCREEN, nothing is written to the registry) and puts the desktop's
/// previous mode back when disposed — also when the process exits.
/// </summary>
internal sealed class DisplayModeChange : IDisposable
{
    private static readonly List<DisplayModeChange> Active = new();

    static DisplayModeChange()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreAll();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreAll();
    }

    private readonly string _device;
    private readonly DisplayMode _previous;
    private bool _restored;

    private DisplayModeChange(string device, DisplayMode previous, DisplayMode mode)
    {
        _device = device;
        _previous = previous;
        Mode = mode;
    }

    public DisplayMode Mode { get; }

    /// <summary>
    /// Switches <paramref name="device"/> to the mode closest to <paramref name="request"/>.
    /// Returns null when the monitor is already in that mode. Throws with a readable message otherwise.
    /// </summary>
    public static DisplayModeChange? Apply(string device, ResolutionRequest request)
    {
        DisplayMode current = DisplayModes.Current(device)
            ?? throw new InvalidOperationException($"Mode actuel de {device} illisible");
        DisplayMode mode = DisplayModes.SelectBest(DisplayModes.List(device), request, current)
            ?? throw new InvalidOperationException($"Aucun mode {request} sur {device} (voir --list-modes)");
        if (mode == current)
        {
            Log.Info($"{device} already in {mode}");
            return null;
        }

        int result = DisplayModes.Change(device, mode, User32.CDS_FULLSCREEN);
        if (result != User32.DISP_CHANGE_SUCCESSFUL)
            throw new InvalidOperationException($"Passage de {device} en {mode} impossible : {DisplayModes.DescribeResult(result)}");
        Log.Info($"{device}: {current} -> {mode}");

        var change = new DisplayModeChange(device, current, mode);
        lock (Active)
            Active.Add(change);
        return change;
    }

    /// <summary>Puts the mode the desktop had before back.</summary>
    public void Dispose()
    {
        lock (Active)
        {
            if (_restored)
                return;
            _restored = true;
            Active.Remove(this);
        }

        int result = DisplayModes.Change(_device, _previous, 0);
        if (result != User32.DISP_CHANGE_SUCCESSFUL)
        {
            // Fall back on the mode stored in the registry (the desktop's usual resolution).
            Log.Warn($"Restoring {_device} to {_previous} failed ({DisplayModes.DescribeResult(result)}), using the registry mode");
            result = User32.ChangeDisplaySettingsEx(_device, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        }
        Log.Info($"{_device}: back to {_previous} ({DisplayModes.DescribeResult(result)})");
    }

    private static void RestoreAll()
    {
        DisplayModeChange[] changes;
        lock (Active)
            changes = Active.ToArray();
        foreach (var change in changes)
            change.Dispose();
    }
}
