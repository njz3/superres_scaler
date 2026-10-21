using System.Runtime.InteropServices;
using System.Text;

namespace SuperResScaler.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;

    public RECT(int left, int top, int right, int bottom)
    {
        Left = left; Top = top; Right = right; Bottom = bottom;
    }

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;

    public override readonly string ToString() => $"({Left},{Top}) {Width}x{Height}";
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X, Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public POINT pt;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MONITORINFOEX
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string szDevice;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WNDCLASSEX
{
    public int cbSize;
    public uint style;
    public IntPtr lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public IntPtr hInstance;
    public IntPtr hIcon;
    public IntPtr hCursor;
    public IntPtr hbrBackground;
    public string? lpszMenuName;
    public string lpszClassName;
    public IntPtr hIconSm;
}

internal delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

internal static class User32
{
    private const string Dll = "user32.dll";

    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_HOTKEY = 0x0312;

    public const int HTTRANSPARENT = -1;
    public const int MA_NOACTIVATE = 3;

    public const uint WS_POPUP = 0x80000000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_SYSMENU = 0x00080000;
    public const uint WS_MINIMIZEBOX = 0x00020000;

    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;
    public const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const uint WS_VISIBLE = 0x10000000;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_RESTORE = 9;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const uint LWA_ALPHA = 0x2;
    public const uint PM_REMOVE = 0x1;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint MONITORINFOF_PRIMARY = 1;
    public const uint GA_ROOT = 2;
    public const uint GW_OWNER = 4;
    public static readonly IntPtr IDC_ARROW = new(32512);

    public const uint MOD_ALT = 0x1;
    public const uint MOD_CONTROL = 0x2;
    public const uint MOD_SHIFT = 0x4;
    public const uint MOD_WIN = 0x8;
    public const uint MOD_NOREPEAT = 0x4000;

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);
    public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport(Dll)] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport(Dll)] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport(Dll)] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport(Dll)] public static extern IntPtr GetForegroundWindow();
    [DllImport(Dll)] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport(Dll)] public static extern IntPtr GetTopWindow(IntPtr hWnd);
    [DllImport(Dll)] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport(Dll)] public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport(Dll)] public static extern IntPtr GetShellWindow();
    [DllImport(Dll, CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport(Dll, CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport(Dll, CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport(Dll)] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport(Dll, EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport(Dll)] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport(Dll)] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport(Dll)] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport(Dll)] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
    [DllImport(Dll)] public static extern bool AdjustWindowRectEx(ref RECT rect, uint style, bool menu, uint exStyle);

    [DllImport(Dll)] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport(Dll, CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport(Dll)] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport(Dll, SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport(Dll)] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true)] public static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport(Dll)] public static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport(Dll)] public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport(Dll)] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport(Dll)] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport(Dll)] public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);
    [DllImport(Dll)] public static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);

    [DllImport(Dll)] public static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax, uint remove);
    [DllImport(Dll)] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport(Dll)] public static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport(Dll)]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback,
        uint processId, uint threadId, uint flags);
    [DllImport(Dll)] public static extern bool UnhookWinEvent(IntPtr hook);
}

internal static class Kernel32
{
    private const string Dll = "kernel32.dll";

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint WAIT_OBJECT_0 = 0;
    public const int ATTACH_PARENT_PROCESS = -1;

    [DllImport(Dll)] public static extern uint WaitForSingleObjectEx(IntPtr handle, uint milliseconds, bool alertable);
    [DllImport(Dll)] public static extern bool CloseHandle(IntPtr handle);
    [DllImport(Dll, CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport(Dll, SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    [DllImport(Dll)] public static extern bool AttachConsole(int processId);
}

internal static class Dwmapi
{
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAKED = 14;

    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    /// <summary>Visible bounds of a window (without the invisible resize borders), in screen pixels.</summary>
    public static bool TryGetFrameBounds(IntPtr hwnd, out RECT rect)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) == 0)
            return true;
        return User32.GetWindowRect(hwnd, out rect);
    }

    public static bool IsCloaked(IntPtr hwnd)
        => DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
}
