using System.Text;

namespace SuperResScaler.Native;

/// <summary>A top-level window that can be used as a scaling source.</summary>
internal sealed record WindowInfo(IntPtr Handle, string Title, string ClassName, string ProcessName, uint ProcessId)
{
    public override string ToString() => $"{Title}  —  {ProcessName}";

    public static WindowInfo? FromHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !User32.IsWindow(hwnd))
            return null;
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        return new WindowInfo(hwnd, GetTitle(hwnd), GetClassName(hwnd), GetProcessName(pid), pid);
    }

    /// <summary>Lists the visible, non-tool top-level windows of other processes, in Z order.</summary>
    public static List<WindowInfo> EnumerateCandidates(bool includeOwnProcess = false)
    {
        var result = new List<WindowInfo>();
        uint ownPid = (uint)Environment.ProcessId;
        IntPtr shell = User32.GetShellWindow();

        User32.EnumWindows((hwnd, _) =>
        {
            if (hwnd == shell || !IsCandidate(hwnd))
                return true;
            User32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid && !includeOwnProcess)
                return true;
            result.Add(new WindowInfo(hwnd, GetTitle(hwnd), GetClassName(hwnd), GetProcessName(pid), pid));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static bool IsCandidate(IntPtr hwnd)
    {
        if (!User32.IsWindowVisible(hwnd) || Dwmapi.IsCloaked(hwnd))
            return false;
        if (User32.GetWindowTextLength(hwnd) == 0)
            return false;
        uint exStyle = (uint)User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
        if ((exStyle & User32.WS_EX_TOOLWINDOW) != 0)
            return false;
        if (User32.GetWindow(hwnd, User32.GW_OWNER) != IntPtr.Zero)
            return false;
        return User32.GetWindowRect(hwnd, out var rect) && rect.Width > 0 && rect.Height > 0;
    }

    public static string GetTitle(IntPtr hwnd)
    {
        int length = User32.GetWindowTextLength(hwnd);
        if (length <= 0)
            return string.Empty;
        var sb = new StringBuilder(length + 1);
        User32.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        User32.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Executable file name of a process (e.g. "game.exe"), or an empty string.</summary>
    public static string GetProcessName(uint pid)
    {
        IntPtr process = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (Kernel32.QueryFullProcessImageName(process, 0, sb, ref size))
                    return Path.GetFileName(sb.ToString());
            }
            finally
            {
                Kernel32.CloseHandle(process);
            }
        }

        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName + ".exe";
        }
        catch
        {
            return string.Empty;
        }
    }
}
