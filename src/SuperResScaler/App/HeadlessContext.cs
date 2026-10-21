using System.Diagnostics;
using SuperResScaler.Config;
using SuperResScaler.Native;

namespace SuperResScaler.App;

/// <summary>
/// Command-line mode: waits for the target window, scales it, and exits when it closes
/// (or, with --persist, goes back to waiting). Only a tray icon is shown.
/// </summary>
internal sealed class HeadlessContext : ApplicationContext
{
    public const int ExitOk = 0;
    public const int ExitNoWindow = 2;
    public const int ExitStartFailed = 3;

    private readonly ScalerController _controller;
    private readonly CommandLineOptions _cli;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly NotifyIcon _tray;
    private Stopwatch _waiting = Stopwatch.StartNew();
    private Stopwatch? _delay;
    private IntPtr _target;
    private bool _hasScaled;
    private bool _exiting;

    public HeadlessContext(ScalerController controller, CommandLineOptions cli)
    {
        _controller = controller;
        _cli = cli;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Arrêter et quitter", null, (_, _) => Exit(ExitOk));
        _tray = new NotifyIcon
        {
            Icon = AppIcon.Get(),
            Text = "SuperResScaler (ligne de commande)",
            ContextMenuStrip = menu,
            Visible = true,
        };

        controller.RegisterHotkeys(toggleAction: () => Exit(ExitOk));
        controller.SessionEnded += OnSessionEnded;
        _timer.Tick += OnTick;
        _timer.Start();
        Log.Info($"Command-line mode: waiting up to {cli.WaitSeconds} s for the target window");
    }

    public int ExitCode { get; private set; }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_cli.Foreground && _target == IntPtr.Zero)
        {
            _target = FindTarget();
            if (_target == IntPtr.Zero)
            {
                if (_waiting.Elapsed.TotalSeconds > _cli.WaitSeconds)
                {
                    Log.Warn("No matching window found");
                    Exit(_hasScaled ? ExitOk : ExitNoWindow);
                }
                return;
            }
            Log.Info($"Target window found: {WindowInfo.FromHandle(_target)}");
        }

        _delay ??= Stopwatch.StartNew();
        if (_delay.Elapsed.TotalSeconds < _cli.EffectiveDelay)
            return;
        _timer.Stop();

        if (_cli.Foreground)
            _target = User32.GetAncestor(User32.GetForegroundWindow(), User32.GA_ROOT);

        var window = WindowInfo.FromHandle(_target);
        if (window != null && _controller.StartScaling(_target, ResolveOptions(window), activate: true))
            _hasScaled = true;
        else if (_cli.Persist && !_cli.Foreground)
            RestartWaiting();
        else
            Exit(ExitStartFailed);
    }

    private IntPtr FindTarget()
        => WindowInfo.EnumerateCandidates().FirstOrDefault(_cli.Matches)?.Handle ?? IntPtr.Zero;

    private ScalingOptions ResolveOptions(WindowInfo window)
    {
        ScalingOptions baseOptions = _controller.Settings.ResolveOptions(window);
        if (_cli.Profile != null)
        {
            if (_controller.Settings.FindProfileByName(_cli.Profile) is { } profile)
                baseOptions = profile;
            else
                Log.Warn($"Profile '{_cli.Profile}' not found, using {(baseOptions is Profile p ? p.DisplayName : "defaults")}");
        }
        var options = baseOptions.Snapshot();
        _cli.ApplyOverrides(options);
        return options;
    }

    private void OnSessionEnded(string? reason)
    {
        if (_exiting)
            return;
        if (reason != null && _cli.Persist && !_cli.Foreground)
        {
            Log.Info("Window closed, waiting for a new one (--persist)");
            RestartWaiting();
            return;
        }
        Exit(ExitOk);
    }

    private void RestartWaiting()
    {
        _target = IntPtr.Zero;
        _delay = null;
        _waiting = Stopwatch.StartNew();
        _timer.Start();
    }

    private void Exit(int code)
    {
        if (_exiting)
            return;
        _exiting = true;
        ExitCode = code;
        _timer.Stop();
        _controller.StopScaling();
        _tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
