using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using SuperResScaler.Native;

namespace SuperResScaler.Engine;

/// <summary>
/// The window the scaled image is presented in. In full-screen mode it is a top-most,
/// click-through, never-activated layered popup covering the monitor, so keyboard/gamepad
/// focus stays on the game underneath. In preview mode it is a small captioned window.
/// It must be created and pumped on the render thread.
/// </summary>
internal sealed class OverlayWindow : IDisposable
{
    private const string ClassName = "SuperResScaler.Overlay";

    private static readonly WndProcDelegate WndProcInstance = WndProc;
    private static readonly ConcurrentDictionary<IntPtr, OverlayWindow> Windows = new();
    private static readonly object RegisterLock = new();
    private static bool _registered;

    public OverlayWindow(Rectangle clientBounds, bool preview)
    {
        RegisterClass();
        IsPreview = preview;

        var rect = new RECT(clientBounds.Left, clientBounds.Top, clientBounds.Right, clientBounds.Bottom);
        uint style, exStyle;
        if (preview)
        {
            style = User32.WS_CAPTION | User32.WS_SYSMENU | User32.WS_MINIMIZEBOX;
            exStyle = User32.WS_EX_TOPMOST | User32.WS_EX_NOACTIVATE;
            User32.AdjustWindowRectEx(ref rect, style, false, exStyle);
        }
        else
        {
            style = User32.WS_POPUP;
            exStyle = User32.WS_EX_TOPMOST | User32.WS_EX_NOACTIVATE | User32.WS_EX_LAYERED
                    | User32.WS_EX_TRANSPARENT | User32.WS_EX_TOOLWINDOW;
        }

        Handle = User32.CreateWindowEx(exStyle, ClassName, "SuperResScaler", style,
            rect.Left, rect.Top, rect.Width, rect.Height, IntPtr.Zero, IntPtr.Zero, Kernel32.GetModuleHandle(null), IntPtr.Zero);
        if (Handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx");
        Windows[Handle] = this;

        // Fully opaque layered window: click-through, and still eligible for independent flip.
        if (!preview)
            User32.SetLayeredWindowAttributes(Handle, 0, 255, User32.LWA_ALPHA);
    }

    public IntPtr Handle { get; }
    public bool IsPreview { get; }

    /// <summary>The user closed the preview window.</summary>
    public bool CloseRequested { get; private set; }

    public bool IsVisible => User32.IsWindowVisible(Handle);

    public void Show()
    {
        User32.ShowWindow(Handle, User32.SW_SHOWNOACTIVATE);
        BringToTop();
    }

    public void Hide() => User32.ShowWindow(Handle, User32.SW_HIDE);

    /// <summary>Re-asserts the top-most position (a top-most game window may have been activated above us).</summary>
    public void BringToTop()
        => User32.SetWindowPos(Handle, User32.HWND_TOPMOST, 0, 0, 0, 0, User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);

    /// <summary>Moves/resizes a full-screen overlay (e.g. after a display mode change).</summary>
    public void SetBounds(Rectangle bounds)
        => User32.SetWindowPos(Handle, User32.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, User32.SWP_NOACTIVATE);

    /// <summary>Dispatches the pending messages of the calling thread.</summary>
    public static void PumpMessages()
    {
        while (User32.PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, User32.PM_REMOVE))
        {
            User32.TranslateMessage(ref msg);
            User32.DispatchMessage(ref msg);
        }
    }

    private static void RegisterClass()
    {
        lock (RegisterLock)
        {
            if (_registered)
                return;
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcInstance),
                hInstance = Kernel32.GetModuleHandle(null),
                hCursor = User32.LoadCursor(IntPtr.Zero, User32.IDC_ARROW),
                lpszClassName = ClassName,
            };
            if (User32.RegisterClassEx(ref wc) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx");
            _registered = true;
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        Windows.TryGetValue(hwnd, out var window);
        switch (msg)
        {
            case User32.WM_NCHITTEST when window is { IsPreview: false }:
                return User32.HTTRANSPARENT;
            case User32.WM_MOUSEACTIVATE:
                return User32.MA_NOACTIVATE;
            case User32.WM_ERASEBKGND:
                return 1;
            case User32.WM_CLOSE:
                if (window != null)
                    window.CloseRequested = true;
                return IntPtr.Zero;
        }
        return User32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        Windows.TryRemove(Handle, out _);
        User32.DestroyWindow(Handle);
    }
}
