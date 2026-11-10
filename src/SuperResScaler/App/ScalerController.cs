// Copyright (c) Benjamin Maurin (njz3) 2026

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
        ApplyDisplayNumbers();
    }

    public SettingsStore Store { get; }
    public AppSettings Settings => Store.Settings;

    /// <summary>Starts the profiles marked AutoScale when their window appears.</summary>
    public ProfileWatcher Watcher { get; }

    public bool IsScaling => _session != null;
    public WindowInfo? ScaledWindow { get; private set; }

    /// <summary>What is being scaled, for display (a window or a whole display); null when idle.</summary>
    public string? ScaledLabel { get; private set; }

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

        if (!Launch(hwnd, options))
            return false;

        ActiveOptions = options;
        ScaledWindow = info;
        var display = MonitorInfo.Find(MonitorInfo.GetAll(), options.SourceDisplay);
        ScaledLabel = display == null
            ? $"« {info.Title} » ({info.ProcessName})"
            : $"{display}, jusqu'à la fermeture de « {info.Title} »";
        if (activate)
            User32.SetForegroundWindow(hwnd);
        Report($"Mise à l'échelle de {ScaledLabel} — shader {_session!.ActiveShader}");
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>Scales a whole display, tied to no window: it runs until it is stopped.</summary>
    public bool StartDisplay(string display, ScalingOptions? options = null)
    {
        CancelDelayedScale();
        StopScaling(quiet: true);

        options ??= Settings.Defaults;
        var monitor = MonitorInfo.Find(MonitorInfo.GetAll(), display);
        if (monitor == null)
        {
            Report($"Écran à capturer introuvable : {display}");
            return false;
        }

        var effective = options.Snapshot();
        effective.SourceDisplay = monitor.Id;
        if (!Launch(IntPtr.Zero, effective))
            return false;

        ActiveOptions = options; // edits of these settings still apply live
        ScaledLabel = monitor.ToString();
        Report($"Mise à l'échelle de {ScaledLabel} — shader {_session!.ActiveShader}");
        StateChanged?.Invoke();
        return true;
    }

    private bool Launch(IntPtr window, ScalingOptions options)
    {
        try
        {
            _session = ScalingSession.Start(window, options, OnSessionEnded, message => _ui.Post(_ => Report(message), null));
            return true;
        }
        catch (Exception ex)
        {
            Report($"Échec du démarrage : {ex.Message}");
            return false;
        }
    }

    public void StopScaling() => StopScaling(quiet: false);

    private void StopScaling(bool quiet)
    {
        var session = _session;
        if (session == null)
            return;
        var window = ScaledWindow;
        _session = null;
        ClearState();
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
        StartForeground();
    }

    /// <summary>
    /// The foreground window with its profile. When the defaults capture a display, that display
    /// is scaled for itself: the session must not end with whichever window had the focus.
    /// </summary>
    private void StartForeground()
    {
        IntPtr window = GetForegroundRoot();
        var info = WindowInfo.FromHandle(window);
        var options = info != null ? Settings.ResolveOptions(info) : Settings.Defaults;
        if (options is not Profile && !string.IsNullOrWhiteSpace(options.SourceDisplay))
            StartDisplay(options.SourceDisplay, options);
        else
            StartScaling(window, activate: false);
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
            StartForeground();
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
        ClearState();
        if (reason != null)
            Report(reason);
        StateChanged?.Invoke();
        SessionEnded?.Invoke(window, reason);
    }, null);

    private void ClearState()
    {
        ActiveOptions = null;
        ScaledWindow = null;
        ScaledLabel = null;
    }

    /// <summary>Publishes the display numbers corrected by hand in the settings.</summary>
    public void ApplyDisplayNumbers() => MonitorInfo.SetNumbers(new Dictionary<string, int>(Settings.DisplayNumbers));

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
