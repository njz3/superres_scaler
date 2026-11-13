// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SuperResScaler.Native;

/// <summary>
/// A display of the desktop. <paramref name="Id"/> is stable (the monitor's device path), so that a
/// profile survives a renumbering. <paramref name="Number"/> is what the user sees: Windows has no
/// API for the number of Settings > Display, so it is the driver's \\.\DISPLAYn unless the user
/// corrected it (Paramètres, see <see cref="SetNumbers"/>).
/// </summary>
internal sealed partial record MonitorInfo(int Number, string DeviceName, string FriendlyName, string Id, RECT Bounds, bool IsPrimary, IntPtr Handle)
{
    /// <summary>EDID manufacturer and product of Virtual Display Driver's monitor, found in its device path.</summary>
    private const string VirtualMonitorMarker = "MTT1337";

    private static volatile IReadOnlyDictionary<string, int> _numbers = new Dictionary<string, int>();

    /// <summary>The n of \\.\DISPLAYn.</summary>
    public int DriverNumber => ParseDriverNumber(DeviceName);

    public string Name => FriendlyName.Length > 0 ? FriendlyName : DeviceName.Replace(@"\\.\", "");

    /// <summary>The test display of Virtual Display Driver (see VirtualDisplayManager).</summary>
    public bool IsVirtual => Id.Contains(VirtualMonitorMarker, StringComparison.OrdinalIgnoreCase);

    public override string ToString()
        => $"{Name}{(IsVirtual ? " (virtuel)" : "")} — {Bounds.Width}x{Bounds.Height}{(IsPrimary ? " (principal)" : "")} — Écran {Number}";

    /// <summary>The numbers the user set by hand, by <see cref="Id"/>.</summary>
    public static void SetNumbers(IReadOnlyDictionary<string, int> numbers) => _numbers = numbers;

    /// <summary>All displays of the desktop, by number.</summary>
    public static List<MonitorInfo> GetAll()
    {
        var handles = new List<IntPtr>();
        User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            handles.Add(monitor);
            return true;
        }, IntPtr.Zero);

        var targets = ReadTargets();
        var result = new List<MonitorInfo>();
        foreach (IntPtr handle in handles)
        {
            if (TryGet(handle, targets, out var info))
                result.Add(info);
        }
        return result.OrderBy(m => m.Number).ThenBy(m => m.DeviceName, StringComparer.Ordinal).ToList();
    }

    /// <summary>The display that contains most of the given window.</summary>
    public static MonitorInfo FromWindow(IntPtr hwnd)
    {
        IntPtr handle = User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST);
        return TryGet(handle, ReadTargets(), out var info) ? info : GetAll().First();
    }

    /// <summary>
    /// A display from its stable id, its number ("2"), its device name, "virtuel" for the virtual
    /// test display, or a part of its monitor name ("DELL", as Hikaruloader's SCREEN_n_DISPLAY);
    /// null if empty or absent.
    /// </summary>
    public static MonitorInfo? Find(IEnumerable<MonitorInfo> monitors, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;
        reference = reference.Trim();
        var all = monitors as IReadOnlyCollection<MonitorInfo> ?? monitors.ToList();
        if (all.FirstOrDefault(m => string.Equals(m.Id, reference, StringComparison.OrdinalIgnoreCase)) is { } byId)
            return byId;
        if (reference.Equals("virtuel", StringComparison.OrdinalIgnoreCase) || reference.Equals("virtual", StringComparison.OrdinalIgnoreCase))
            return all.FirstOrDefault(m => m.IsVirtual);
        if (int.TryParse(reference, out int number))
            return all.FirstOrDefault(m => m.Number == number);
        return all.FirstOrDefault(m => string.Equals(m.DeviceName, reference, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(m => m.FriendlyName.Contains(reference, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The output when none is chosen: when a whole display is captured, another one (the primary
    /// first), or that same one if it is alone; otherwise the window's display, or the primary.
    /// </summary>
    public static MonitorInfo AutoOutput(IReadOnlyList<MonitorInfo> monitors, MonitorInfo? captured, MonitorInfo? windowMonitor)
    {
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        if (captured == null)
            return windowMonitor ?? primary;
        if (primary.Id != captured.Id)
            return primary;
        return monitors.FirstOrDefault(m => m.Id != captured.Id) ?? captured;
    }

    public static int ParseDriverNumber(string deviceName)
    {
        var match = DeviceNumberRegex().Match(deviceName);
        return match.Success && int.TryParse(match.Groups[1].Value, out int number) ? number : 0;
    }

    private static bool TryGet(IntPtr handle, Dictionary<string, (string Friendly, string Path)> targets, out MonitorInfo info)
    {
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = string.Empty };
        if (!User32.GetMonitorInfo(handle, ref mi))
        {
            info = null!;
            return false;
        }
        targets.TryGetValue(mi.szDevice, out var target);
        string id = string.IsNullOrEmpty(target.Path) ? mi.szDevice : target.Path;
        int number = _numbers.TryGetValue(id, out int chosen) ? chosen : ParseDriverNumber(mi.szDevice);
        string name = string.IsNullOrEmpty(target.Friendly) ? ReadDriverName(mi.szDevice) : target.Friendly;
        info = new MonitorInfo(number, mi.szDevice, name, id, mi.rcMonitor, (mi.dwFlags & User32.MONITORINFOF_PRIMARY) != 0, handle);
        return true;
    }

    /// <summary>
    /// The monitor's name in its driver. A laptop panel's EDID has no name: only the driver's
    /// ("Dell INSPIRON AUO9F91 Display") exists, and the display configuration does not give it.
    /// </summary>
    private static string ReadDriverName(string deviceName)
    {
        var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint index = 0; User32.EnumDisplayDevices(deviceName, index, ref device, 0); index++)
        {
            if ((device.StateFlags & User32.DISPLAY_DEVICE_ACTIVE) != 0)
                return device.DeviceString;
        }
        return string.Empty;
    }

    /// <summary>The monitor (EDID name, device path) behind each \\.\DISPLAYn, from the display configuration.</summary>
    private static Dictionary<string, (string Friendly, string Path)> ReadTargets()
    {
        var result = new Dictionary<string, (string Friendly, string Path)>(StringComparer.OrdinalIgnoreCase);
        if (User32.GetDisplayConfigBufferSizes(User32.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0)
            return result;
        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
        if (User32.QueryDisplayConfig(User32.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
            return result;

        foreach (var path in paths.Take((int)pathCount))
        {
            var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME { viewGdiDeviceName = string.Empty };
            source.header.type = User32.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
            source.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
            source.header.adapterId = path.sourceAdapterId;
            source.header.id = path.sourceId;
            if (User32.DisplayConfigGetSourceName(ref source) != 0)
                continue;

            var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME { monitorFriendlyDeviceName = string.Empty, monitorDevicePath = string.Empty };
            target.header.type = User32.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
            target.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
            target.header.adapterId = path.targetAdapterId;
            target.header.id = path.targetId;
            if (User32.DisplayConfigGetTargetName(ref target) == 0)
                result.TryAdd(source.viewGdiDeviceName, (target.monitorFriendlyDeviceName, target.monitorDevicePath));
        }
        return result;
    }

    [GeneratedRegex(@"DISPLAY(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DeviceNumberRegex();
}
