using SuperResScaler.Config;
using SuperResScaler.Engine;
using SuperResScaler.Native;
using SuperResScaler.Rendering;

namespace SuperResScaler.App;

/// <summary>
/// Application logic shared by the main window and the command-line mode: starts/stops the
/// scaling session, hotkeys, delayed start and the profile watcher. UI thread only.
/// </summary>
internal sealed class ScalerController : IDisposable
{
    private const int HistorySize = 50;

    private readonly SynchronizationContext _ui;
    private readonly HotkeyManager _hotkeys = new();
    private readonly List<(DateTime Time, string Text)> _history = new();
    private System.Windows.Forms.Timer? _delayTimer;
    private int _delayRemaining;
    private ScalingSession? _session;

    public ScalerController(SettingsStore store)
    {
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("ScalerController must be created on the UI thread.");
        Store = store;
        Watcher = new ProfileWatcher(this);
    }

    public SettingsStore Store { get; }
    public AppSettings Settings => Store.Settings;

    /// <summary>Starts the profiles marked AutoScale when their window appears.</summary>
    public ProfileWatcher Watcher { get; }

    public bool IsScaling => _session != null;
    public WindowInfo? ScaledWindow { get; private set; }

    /// <summary>The settings object (profile or defaults) the running session was started from.</summary>
    public ScalingOptions? ActiveOptions { get; private set; }

    /// <summary>The latest messages, oldest first.</summary>
    public IReadOnlyList<(DateTime Time, string Text)> History => _history;

    public event Action? StateChanged;
    public event Action<string>? Message;

    /// <summary>A hotkey modified these options (the UI should reload them).</summary>
    public event Action<ScalingOptions>? OptionsChanged;

    /// <summary>The session of this window ended; the reason is null when it was stopped on request.</summary>
    public event Action<WindowInfo?, string?>? SessionEnded;

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
        var window = ScaledWindow;
        _session = null;
        ActiveOptions = null;
        ScaledWindow = null;
        session.Stop();
        if (!quiet)
            Report("Mise à l'échelle arrêtée");
        StateChanged?.Invoke();
        SessionEnded?.Invoke(window, null);
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

    private static IntPtr GetForegroundRoot() => User32.GetAncestor(User32.GetForegroundWindow(), User32.GA_ROOT);

    private void OnSessionEnded(ScalingSession session, string? reason) => _ui.Post(_ =>
    {
        if (_session != session)
            return; // already stopped from here
        var window = ScaledWindow;
        _session = null;
        ActiveOptions = null;
        ScaledWindow = null;
        if (reason != null)
            Report(reason);
        StateChanged?.Invoke();
        SessionEnded?.Invoke(window, reason);
    }, null);

    public void Report(string message)
    {
        Log.Info(message);
        _history.Add((DateTime.Now, message));
        if (_history.Count > HistorySize)
            _history.RemoveAt(0);
        Message?.Invoke(message);
    }

    public void Dispose()
    {
        CancelDelayedScale();
        Watcher.Dispose();
        StopScaling(quiet: true);
        _hotkeys.Dispose();
    }
}
