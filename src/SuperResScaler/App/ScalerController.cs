using SuperResScaler.Config;
using SuperResScaler.Engine;
using SuperResScaler.Native;
using SuperResScaler.Rendering;

namespace SuperResScaler.App;

/// <summary>
/// Application logic shared by the main window and the command-line mode: starts/stops the
/// scaling session, hotkeys, delayed start and automatic start of profiles. UI thread only.
/// </summary>
internal sealed class ScalerController : IDisposable
{
    private const int AutoScaleDelayMs = 1000;

    private readonly SynchronizationContext _ui;
    private readonly HotkeyManager _hotkeys = new();
    private readonly User32.WinEventProc _foregroundProc;
    private readonly System.Windows.Forms.Timer _autoScaleTimer = new() { Interval = AutoScaleDelayMs };
    private System.Windows.Forms.Timer? _delayTimer;
    private IntPtr _foregroundHook;
    private IntPtr _autoScaleCandidate;
    private int _delayRemaining;
    private ScalingSession? _session;

    public ScalerController(SettingsStore store)
    {
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("ScalerController must be created on the UI thread.");
        Store = store;
        _foregroundProc = OnForegroundChanged;
        _autoScaleTimer.Tick += OnAutoScaleTimer;
    }

    public SettingsStore Store { get; }
    public AppSettings Settings => Store.Settings;

    public bool IsScaling => _session != null;
    public WindowInfo? ScaledWindow { get; private set; }

    /// <summary>The settings object (profile or defaults) the running session was started from.</summary>
    public ScalingOptions? ActiveOptions { get; private set; }

    public event Action? StateChanged;
    public event Action<string>? Message;

    /// <summary>A hotkey modified these options (the UI should reload them).</summary>
    public event Action<ScalingOptions>? OptionsChanged;

    /// <summary>The session ended; the argument is the reason (null when stopped on request).</summary>
    public event Action<string?>? SessionEnded;

    /// <summary>Registers the configured hotkeys. Returns the problems encountered.</summary>
    public List<string> RegisterHotkeys(Action? toggleAction = null)
    {
        _hotkeys.UnregisterAll();
        var errors = new List<string>();
        void Add(string? spec, Action action)
        {
            if (_hotkeys.Register(spec, action) is { } error)
            {
                errors.Add(error);
                Log.Warn(error);
            }
        }
        Add(Settings.Hotkeys.ToggleScaling, toggleAction ?? ToggleForeground);
        Add(Settings.Hotkeys.NextShader, NextShader);
        Add(Settings.Hotkeys.ResetAutoCrop, () => _session?.ResetAutoCrop());
        return errors;
    }

    /// <summary>
    /// Starts scaling a window with the given options, or with its profile / the defaults.
    /// <paramref name="activate"/> gives the keyboard focus back to the window.
    /// </summary>
    public bool StartScaling(IntPtr hwnd, ScalingOptions? options = null, bool activate = true)
    {
        CancelDelayedScale();
        StopScaling(quiet: true);

        var info = WindowInfo.FromHandle(hwnd);
        if (info == null || hwnd == User32.GetShellWindow())
        {
            Report("Aucune fenêtre valide à mettre à l'échelle");
            return false;
        }
        if (info.ProcessId == Environment.ProcessId)
        {
            Report("Activez la fenêtre du jeu, pas celle de SuperResScaler");
            return false;
        }

        options ??= Settings.ResolveOptions(info);
        if (User32.IsIconic(hwnd))
            User32.ShowWindow(hwnd, User32.SW_RESTORE);

        try
        {
            _session = ScalingSession.Start(hwnd, options, OnSessionEnded, message => _ui.Post(_ => Report(message), null));
        }
        catch (Exception ex)
        {
            Report($"Échec du démarrage : {ex.Message}");
            return false;
        }

        ActiveOptions = options;
        ScaledWindow = info;
        if (activate)
            User32.SetForegroundWindow(hwnd);
        Report($"Mise à l'échelle de « {info.Title} » — shader {_session.ActiveShader}");
        StateChanged?.Invoke();
        return true;
    }

    public void StopScaling() => StopScaling(quiet: false);

    private void StopScaling(bool quiet)
    {
        var session = _session;
        if (session == null)
            return;
        _session = null;
        ActiveOptions = null;
        ScaledWindow = null;
        session.Stop();
        if (!quiet)
            Report("Mise à l'échelle arrêtée");
        StateChanged?.Invoke();
        SessionEnded?.Invoke(null);
    }

    /// <summary>Hotkey: stop the current scaling, or scale the foreground window.</summary>
    public void ToggleForeground()
    {
        if (IsScaling)
        {
            StopScaling();
            return;
        }
        StartScaling(GetForegroundRoot(), activate: false);
    }

    /// <summary>Gives the user a few seconds to activate the game window, then scales it.</summary>
    public void ScaleForegroundAfterDelay(int seconds)
    {
        CancelDelayedScale();
        _delayRemaining = Math.Max(1, seconds);
        Report($"Activez la fenêtre à mettre à l'échelle… {_delayRemaining}");
        _delayTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _delayTimer.Tick += (_, _) =>
        {
            if (--_delayRemaining > 0)
            {
                Report($"Activez la fenêtre à mettre à l'échelle… {_delayRemaining}");
                return;
            }
            CancelDelayedScale();
            StartScaling(GetForegroundRoot(), activate: false);
        };
        _delayTimer.Start();
    }

    private void CancelDelayedScale()
    {
        _delayTimer?.Dispose();
        _delayTimer = null;
    }

    /// <summary>The user edited these options: persist them and apply them live if they are in use.</summary>
    public void OnOptionsEdited(ScalingOptions edited)
    {
        Store.Save();
        if (_session != null && ReferenceEquals(edited, ActiveOptions))
            _session.UpdateOptions(edited);
    }

    private void NextShader()
    {
        if (_session == null || ActiveOptions == null)
            return;
        ActiveOptions.Shader = ShaderLibrary.Next(ActiveOptions.Shader);
        Store.Save();
        _session.UpdateOptions(ActiveOptions);
        OptionsChanged?.Invoke(ActiveOptions);
    }

    /// <summary>Starts profiles marked AutoScale when their window comes to the foreground.</summary>
    public void EnableAutoScale()
    {
        if (_foregroundHook != IntPtr.Zero)
            return;
        _foregroundHook = User32.SetWinEventHook(User32.EVENT_SYSTEM_FOREGROUND, User32.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundProc, 0, 0, User32.WINEVENT_OUTOFCONTEXT | User32.WINEVENT_SKIPOWNPROCESS);
        ConsiderAutoScale(User32.GetForegroundWindow());
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        => ConsiderAutoScale(hwnd);

    private void ConsiderAutoScale(IntPtr hwnd)
    {
        if (!Settings.AutoScaleEnabled || IsScaling || hwnd == IntPtr.Zero)
            return;
        IntPtr root = User32.GetAncestor(hwnd, User32.GA_ROOT);
        var info = WindowInfo.FromHandle(root);
        if (info == null || Settings.FindProfile(info) is not { AutoScale: true })
            return;
        // Wait a little: games often resize or go borderless right after showing their window.
        _autoScaleCandidate = root;
        _autoScaleTimer.Stop();
        _autoScaleTimer.Start();
    }

    private void OnAutoScaleTimer(object? sender, EventArgs e)
    {
        _autoScaleTimer.Stop();
        IntPtr candidate = _autoScaleCandidate;
        _autoScaleCandidate = IntPtr.Zero;
        if (IsScaling || candidate == IntPtr.Zero || GetForegroundRoot() != candidate)
            return;
        StartScaling(candidate, activate: false);
    }

    private static IntPtr GetForegroundRoot() => User32.GetAncestor(User32.GetForegroundWindow(), User32.GA_ROOT);

    private void OnSessionEnded(ScalingSession session, string? reason) => _ui.Post(_ =>
    {
        if (_session != session)
            return; // already stopped from here
        _session = null;
        ActiveOptions = null;
        ScaledWindow = null;
        if (reason != null)
            Report(reason);
        StateChanged?.Invoke();
        SessionEnded?.Invoke(reason);
    }, null);

    private void Report(string message)
    {
        Log.Info(message);
        Message?.Invoke(message);
    }

    public void Dispose()
    {
        CancelDelayedScale();
        StopScaling(quiet: true);
        if (_foregroundHook != IntPtr.Zero)
            User32.UnhookWinEvent(_foregroundHook);
        _autoScaleTimer.Dispose();
        _hotkeys.Dispose();
    }
}
