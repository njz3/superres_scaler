// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Runtime.InteropServices;

namespace SuperResScaler.Native;

internal sealed record MonitorInfo(int Index, string DeviceName, RECT Bounds, bool IsPrimary)
{
    public override string ToString()
        => $"{Index}: {DeviceName.Replace(@"\\.\", "")} {Bounds.Width}x{Bounds.Height}{(IsPrimary ? " (principal)" : "")}";

    /// <summary>All monitors, numbered from 1 in enumeration order.</summary>
    public static List<MonitorInfo> GetAll()
    {
        var handles = new List<IntPtr>();
        User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            handles.Add(monitor);
            return true;
        }, IntPtr.Zero);

        var result = new List<MonitorInfo>();
        foreach (IntPtr handle in handles)
        {
            if (TryGet(handle, result.Count + 1, out var info))
                result.Add(info);
        }
        return result;
    }

    /// <summary>The monitor that contains most of the given window.</summary>
    public static MonitorInfo FromWindow(IntPtr hwnd)
    {
        IntPtr handle = User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST);
        var all = GetAll();
        if (TryGet(handle, 0, out var info))
        {
            var match = all.FirstOrDefault(m => m.DeviceName == info.DeviceName);
            return match ?? info;
        }
        return all.First();
    }

    private static bool TryGet(IntPtr handle, int index, out MonitorInfo info)
    {
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = string.Empty };
        if (!User32.GetMonitorInfo(handle, ref mi))
        {
            info = null!;
            return false;
        }
        info = new MonitorInfo(index, mi.szDevice, mi.rcMonitor, (mi.dwFlags & User32.MONITORINFOF_PRIMARY) != 0);
        return true;
    }
}
