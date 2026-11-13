// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Diagnostics;
using SuperResScaler.Native;

namespace SuperResScaler.VirtualDisplay;

/// <summary>The mode of a real display, noted before the virtual display is created or destroyed.</summary>
internal sealed record KeptMode(string Id, string Name, DisplayMode Mode);

/// <summary>A real display that Windows switched to another mode, and the mode it had before.</summary>
internal sealed record ChangedMode(KeptMode Kept, DisplayMode Now);

/// <summary>
/// Windows keeps one display configuration per set of connected monitors ("PHL + virtual display",
/// "PHL alone") and applies it as soon as a monitor appears or disappears. Creating or destroying the
/// virtual display thus switches the real displays to the mode saved the last time that set of
/// monitors was connected, or to their native mode when there is none: on the cabinets, the PHL came
/// back in 1920x1080 when the virtual display was destroyed. Their modes are noted before, and set
/// back and saved (for the new set of monitors) once Windows has applied its configuration.
/// </summary>
internal static class RealDisplayModes
{
    private const int PollMs = 250;
    private const int StableMs = 1000;
    private const int TimeoutMs = 15_000;

    /// <summary>
    /// The modes of the real displays. A display a scaling session switched temporarily is left out:
    /// its mode is not the desktop's, and the session sets the desktop's back when it ends.
    /// </summary>
    public static IReadOnlyList<KeptMode> Note()
    {
        var kept = new List<KeptMode>();
        foreach (var monitor in MonitorInfo.GetAll().Where(m => !m.IsVirtual && !DisplayModeChange.IsSwitched(m.DeviceName)))
        {
            if (DisplayModes.Current(monitor.DeviceName) is { } mode)
                kept.Add(new KeptMode(monitor.Id, monitor.Name, mode));
        }
        Log.Info($"Real displays before the virtual display change: {Describe(kept)}");
        return kept;
    }

    /// <summary>The noted displays whose mode is not the same any more; those that are gone are left out.</summary>
    public static List<ChangedMode> FindChanged(IEnumerable<KeptMode> kept, IReadOnlyDictionary<string, DisplayMode> now)
        => kept.Where(k => now.TryGetValue(k.Id, out var mode) && !DisplayModes.SameMode(mode, k.Mode))
               .Select(k => new ChangedMode(k, now[k.Id]))
               .ToList();

    /// <summary>The sentence end that tells what was set back.</summary>
    public static string Report(ChangedMode changed, bool done, bool saved, string? error = null)
    {
        string what = $"Windows avait mis {changed.Kept.Name} en {changed.Now}";
        if (!done)
            return $"{what} : retour en {changed.Kept.Mode} impossible ({error})";
        return saved ? $"{what} : remis en {changed.Kept.Mode}" : $"{what} : remis en {changed.Kept.Mode}, mais Windows ne l'a pas enregistré";
    }

    /// <summary>
    /// Waits for Windows to apply the configuration of the new set of monitors (the virtual display
    /// there or gone, then nothing changing for a second), and sets back the modes it changed.
    /// Returns a sentence end for the caller's message, empty when nothing changed.
    /// </summary>
    public static string SetBack(IReadOnlyList<KeptMode> kept, bool virtualPresent)
    {
        if (kept.Count == 0)
            return "";
        var monitors = WaitForDisplays(virtualPresent);
        var now = new Dictionary<string, DisplayMode>(StringComparer.OrdinalIgnoreCase);
        var devices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in monitors.Where(m => !m.IsVirtual))
        {
            if (DisplayModes.Current(monitor.DeviceName) is { } mode && now.TryAdd(monitor.Id, mode))
                devices[monitor.Id] = monitor.DeviceName;
        }

        var changes = FindChanged(kept, now);
        if (changes.Count == 0)
        {
            Log.Info("Real displays kept their modes");
            return "";
        }

        var parts = new List<string>();
        foreach (var changed in changes)
        {
            string device = devices[changed.Kept.Id];
            int result = DisplayModes.Change(device, changed.Kept.Mode, User32.CDS_UPDATEREGISTRY);
            if (result != User32.DISP_CHANGE_SUCCESSFUL)
            {
                string error = DisplayModes.DescribeResult(result);
                Log.Warn($"{changed.Kept.Name} ({device}): Windows set {changed.Now}, back to {changed.Kept.Mode} failed: {error}");
                parts.Add(Report(changed, done: false, saved: false, error));
                continue;
            }

            bool saved = DisplayModes.SameMode(DisplayModes.Saved(device), changed.Kept.Mode);
            if (!saved)
            {
                int error = DisplayModes.SaveActiveConfiguration();
                saved = error == 0 && DisplayModes.SameMode(DisplayModes.Saved(device), changed.Kept.Mode);
                Log.Info($"{device}: mode not saved by ChangeDisplaySettingsEx, SetDisplayConfig: {error}, saved now: {saved}");
            }
            Log.Info($"{changed.Kept.Name} ({device}): Windows set {changed.Now}, back to {changed.Kept.Mode}{(saved ? ", saved" : ", not saved")}");
            parts.Add(Report(changed, done: true, saved));
        }
        return " ; " + string.Join(" ; ", parts);
    }

    /// <summary>The displays once the virtual display is <paramref name="virtualPresent"/> and nothing changed for a second (15 s at most).</summary>
    private static List<MonitorInfo> WaitForDisplays(bool virtualPresent)
    {
        var watch = Stopwatch.StartNew();
        string? last = null;
        long stableSince = 0;
        while (true)
        {
            var monitors = MonitorInfo.GetAll();
            string state = string.Join("|", monitors.Select(m =>
                $"{m.Id}={m.DeviceName}:{m.Bounds.Left},{m.Bounds.Top},{m.Bounds.Width}x{m.Bounds.Height}:{DisplayModes.Current(m.DeviceName)}"));
            if (state != last)
            {
                last = state;
                stableSince = watch.ElapsedMilliseconds;
            }
            else if (monitors.Any(m => m.IsVirtual) == virtualPresent && watch.ElapsedMilliseconds - stableSince >= StableMs)
            {
                return monitors;
            }
            if (watch.ElapsedMilliseconds >= TimeoutMs)
            {
                Log.Warn($"After {TimeoutMs / 1000} s, the virtual display is {(monitors.Any(m => m.IsVirtual) ? "there" : "absent")} "
                         + $"and the displays {(watch.ElapsedMilliseconds - stableSince >= StableMs ? "are settled" : "still change")}");
                return monitors;
            }
            Thread.Sleep(PollMs);
        }
    }

    private static string Describe(IEnumerable<KeptMode> kept)
    {
        string text = string.Join(", ", kept.Select(k => $"{k.Name} {k.Mode}"));
        return text.Length > 0 ? text : "none";
    }
}
