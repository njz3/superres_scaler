using SuperResScaler.Config;
using SuperResScaler.Native;

namespace SuperResScaler.App;

/// <summary>
/// Watches the running processes and their windows, as BackForceFeeder does, and starts scaling
/// when a window matches a profile marked AutoScale: same process/title filters (wildcards, "|"
/// alternatives, child window titles). The window must exist for the profile's delay first, so
/// that a game that resizes or goes borderless right after starting is captured in its final
/// state. UI thread only (WinForms timer).
/// </summary>
internal sealed class ProfileWatcher : IDisposable
{
    /// <summary>Windows smaller than this (splash screens, launchers' tool windows) are ignored.</summary>
    private const int MinClientArea = 160 * 120;

    private readonly ScalerController _controller;
    private readonly System.Windows.Forms.Timer _timer = new();
    private IntPtr _candidate;
    private Profile? _candidateProfile;
    private DateTime _candidateSince;
    private IntPtr _suppressed;
    private bool _active;

    public ProfileWatcher(ScalerController controller)
    {
        _controller = controller;
        _timer.Tick += (_, _) => Scan();
        controller.SessionEnded += OnSessionEnded;
    }

    /// <summary>Scanning on (the main window is in the tray or minimized, or WatchWhileVisible).</summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value)
                return;
            _active = value;
            _candidate = IntPtr.Zero;
            Log.Info($"Profile watching {(value ? "on" : "off")}");
            Restart();
        }
    }

    /// <summary>The profile and window waiting for their delay, for display.</summary>
    public (Profile Profile, WindowInfo Window)? Pending { get; private set; }

    public event Action? StateChanged;

    /// <summary>Applies a new scan interval or enables/disables the scanning.</summary>
    public void Restart()
    {
        _timer.Stop();
        _timer.Interval = Math.Max(250, _controller.Settings.WatchIntervalMs);
        if (_active && _controller.Settings.AutoScaleEnabled)
        {
            _timer.Start();
            Scan();
        }
        else
        {
            SetPending(null);
        }
    }

    /// <summary>The best window to scale for the auto-scale profiles, or null.</summary>
    public static (Profile Profile, WindowInfo Window)? FindMatch(AppSettings settings, IEnumerable<WindowInfo> windows, IntPtr ignore = default)
    {
        (Profile Profile, WindowInfo Window)? best = null;
        int bestArea = 0;
        foreach (var window in windows)
        {
            if (window.Handle == ignore || User32.IsIconic(window.Handle))
                continue;
            var profile = settings.FindProfile(window, includeChildren: true);
            if (profile is not { AutoScale: true })
                continue;
            int area = window.ClientArea;
            if (area < MinClientArea || area <= bestArea)
                continue;
            best = (profile, window);
            bestArea = area;
        }
        return best;
    }

    private void Scan()
    {
        if (!_active || !_controller.Settings.AutoScaleEnabled)
            return;
        var windows = WindowInfo.EnumerateCandidates();

        // A window the user stopped by hand is left alone until it goes away.
        if (_suppressed != IntPtr.Zero && !windows.Any(w => w.Handle == _suppressed))
            _suppressed = IntPtr.Zero;
        if (_controller.IsScaling)
        {
            SetPending(null);
            return;
        }

        var match = FindMatch(_controller.Settings, windows, _suppressed);
        if (match is not { } found)
        {
            SetPending(null);
            return;
        }
        if (found.Window.Handle != _candidate || !ReferenceEquals(found.Profile, _candidateProfile))
        {
            _candidate = found.Window.Handle;
            _candidateProfile = found.Profile;
            _candidateSince = DateTime.UtcNow;
            SetPending(found);
            _controller.Report($"« {found.Window.Title} » ({found.Window.ProcessName}) correspond au profil {found.Profile.DisplayName}");
        }
        if ((DateTime.UtcNow - _candidateSince).TotalSeconds < Math.Max(0, found.Profile.AutoScaleDelay))
            return;

        _candidate = IntPtr.Zero;
        SetPending(null);
        if (!_controller.StartScaling(found.Window.Handle, found.Profile, activate: false))
            _suppressed = found.Window.Handle; // do not retry a failing window every second
    }

    private void OnSessionEnded(WindowInfo? window, string? reason)
    {
        // Stopped on request while the game still runs: do not start it again behind the user's back.
        if (reason == null && window != null && User32.IsWindow(window.Handle))
            _suppressed = window.Handle;
    }

    private void SetPending((Profile Profile, WindowInfo Window)? pending)
    {
        if (pending == null && Pending == null)
            return;
        Pending = pending;
        if (pending == null)
        {
            _candidate = IntPtr.Zero;
            _candidateProfile = null;
        }
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        _timer.Dispose();
        _controller.SessionEnded -= OnSessionEnded;
    }
}
