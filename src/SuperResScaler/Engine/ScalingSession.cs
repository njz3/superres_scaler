// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using SuperResScaler.Capture;
using SuperResScaler.Config;
using SuperResScaler.Native;
using SuperResScaler.Rendering;

namespace SuperResScaler.Engine;

/// <summary>
/// One scaling run: captures a window and presents it scaled on a monitor. Everything
/// (overlay window, D3D11 device, capture session) lives on a dedicated render thread.
/// </summary>
internal sealed class ScalingSession
{
    /// <summary>Captured frames between two black-bar analyses.</summary>
    private const int AutoCropInterval = 30;

    /// <summary>Loop iterations between two top-most re-assertions / display size checks.</summary>
    private const int TopMostInterval = 120;
    private const int DisplayCheckInterval = 60;

    private readonly IntPtr _source;
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly ManualResetEventSlim _started = new();
    private readonly BlackBarDetector _blackBars = new();
    private readonly Action<ScalingSession, string?> _onEnded;
    private readonly Action<string>? _onMessage;
    private ScalingOptions _options;
    private Exception? _startError;
    private volatile bool _stopRequested;

    // Owned by the render thread.
    private Renderer? _renderer;
    private OverlayWindow? _overlay;
    private SwapChainPresenter? _presenter;
    private WindowCapture? _capture;
    private DisplayModeChange? _displayChange;
    private string? _monitorDevice;
    private Rectangle _outputBounds;
    private Rectangle _lastRegion;
    private long _capturedFrames;

    private ScalingSession(IntPtr source, ScalingOptions options, Action<ScalingSession, string?> onEnded, Action<string>? onMessage)
    {
        _source = source;
        _options = options.Snapshot();
        _onEnded = onEnded;
        _onMessage = onMessage;
        _thread = new Thread(Run) { IsBackground = true, Name = "SuperResScaler render", Priority = ThreadPriority.AboveNormal };
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    public IntPtr SourceWindow => _source;

    /// <summary>Screen rectangle the image is presented in (client area of the preview window).</summary>
    public Rectangle OutputBounds => _outputBounds;

    /// <summary>Shader actually in use (may be the built-in fallback).</summary>
    public string? ActiveShader { get; private set; }

    /// <summary>
    /// Starts scaling <paramref name="source"/>. Throws if the capture or the output cannot be set up.
    /// <paramref name="onEnded"/> is called on the render thread with the reason (null when stopped on request).
    /// </summary>
    public static ScalingSession Start(IntPtr source, ScalingOptions options,
        Action<ScalingSession, string?> onEnded, Action<string>? onMessage = null)
    {
        var session = new ScalingSession(source, options, onEnded, onMessage);
        session._thread.Start();
        session._started.Wait();
        if (session._startError != null)
        {
            session._thread.Join();
            ExceptionDispatchInfo.Capture(session._startError).Throw();
        }
        return session;
    }

    public void Stop()
    {
        _stopRequested = true;
        if (Thread.CurrentThread != _thread)
            _thread.Join(5000);
    }

    /// <summary>Applies new options live (monitor and preview changes need a restart).</summary>
    public void UpdateOptions(ScalingOptions options)
    {
        var snapshot = options.Snapshot();
        _commands.Enqueue(() => ApplyOptions(snapshot));
    }

    public void ResetAutoCrop() => _commands.Enqueue(() =>
    {
        _blackBars.Reset();
        _onMessage?.Invoke("Recadrage automatique réinitialisé");
    });

    private void Run()
    {
        try
        {
            Initialize();
        }
        catch (Exception ex)
        {
            Log.Error("Scaling start failed", ex);
            _startError = ex;
            Cleanup();
            _started.Set();
            return;
        }
        _started.Set();

        string? reason;
        try
        {
            reason = Loop();
        }
        catch (Exception ex)
        {
            Log.Error("Scaling loop failed", ex);
            reason = "Erreur : " + ex.Message;
        }
        finally
        {
            Cleanup();
        }
        Log.Info($"Scaling ended: {reason ?? "stopped"}");
        _onEnded(this, reason);
    }

    private void Initialize()
    {
        var info = WindowInfo.FromHandle(_source) ?? throw new InvalidOperationException("La fenêtre source n'existe plus.");
        Log.Info($"Scaling '{info.Title}' ({info.ProcessName}), shader {_options.Shader}, mode {_options.Mode}, preview {_options.Preview}");

        _renderer = new Renderer();
        _renderer.LoadShaderOrFallback(_options.Shader);
        ActiveShader = _renderer.ShaderName;
        if (ActiveShader == Renderer.FallbackShaderName)
            _onMessage?.Invoke($"Shader « {_options.Shader} » indisponible, nearest intégré utilisé (voir le log)");

        // The output monitor is chosen once: a mode change must not make it move to another one.
        _monitorDevice = PickMonitor().DeviceName;
        ApplyResolution();

        _outputBounds = ResolveOutputBounds();
        _overlay = new OverlayWindow(_outputBounds, _options.Preview);
        _presenter = new SwapChainPresenter(_renderer, _overlay.Handle, _outputBounds.Width, _outputBounds.Height);
        _capture = new WindowCapture(_renderer.Device, _source, _options.CaptureCursor);
        Log.Info($"Output {_outputBounds}");
    }

    private string? Loop()
    {
        var renderer = _renderer!;
        var overlay = _overlay!;
        var presenter = _presenter!;
        var capture = _capture!;
        bool hasImage = false;
        long iteration = 0;

        while (!_stopRequested)
        {
            OverlayWindow.PumpMessages();
            if (overlay.CloseRequested)
                return "Fenêtre de prévisualisation fermée";
            while (_commands.TryDequeue(out var command))
                command();

            if (!User32.IsWindow(_source) || capture.IsClosed)
                return "La fenêtre source a été fermée";

            if (User32.IsIconic(_source))
            {
                // Minimized windows produce no frames: show the desktop until it is restored.
                if (overlay.IsVisible)
                    overlay.Hide();
                Thread.Sleep(50);
                continue;
            }

            iteration++;
            if (!overlay.IsPreview && iteration % DisplayCheckInterval == 0)
                CheckDisplayBounds();

            // Every successful wait must be followed by a Present (the waitable object is a semaphore).
            presenter.WaitForNextFrame(100);

            using (var frame = capture.TryGetLatestFrame())
            {
                if (frame != null && UpdateSource(frame))
                    hasImage = true;
            }

            var placement = Geometry.ComputePlacement(_options, renderer.CapturedWidth, renderer.CapturedHeight, presenter.Width, presenter.Height);
            renderer.Draw(presenter.RenderTarget, placement);
            presenter.Present(_options.VSync);

            if (hasImage && !overlay.IsVisible)
                overlay.Show();
            else if (!overlay.IsPreview && iteration % TopMostInterval == 0)
                overlay.BringToTop();
        }
        return null;
    }

    private bool UpdateSource(CapturedFrame frame)
    {
        var renderer = _renderer!;
        Rectangle region = Geometry.ComputeSourceRegion(_source, _options, frame.ContentWidth, frame.ContentHeight);
        if (region.Width <= 0 || region.Height <= 0)
            return false;

        if (_options.AutoCrop)
        {
            if (_capturedFrames % AutoCropInterval == 0)
                _blackBars.Analyze(renderer.ReadPixels(frame.Texture, region.X, region.Y, region.Width, region.Height), region.Width, region.Height);
            region = _blackBars.Apply(region);
        }
        _capturedFrames++;

        if (region != _lastRegion)
        {
            Log.Info($"Source region {region.X},{region.Y} {region.Width}x{region.Height} (captured {frame.ContentWidth}x{frame.ContentHeight})");
            _lastRegion = region;
        }
        renderer.UpdateSource(frame.Texture, region.X, region.Y, region.Width, region.Height);
        return true;
    }

    private void ApplyOptions(ScalingOptions options)
    {
        var previous = _options;
        _options = options;

        if (!string.Equals(previous.Shader, options.Shader, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _renderer!.LoadShader(options.Shader);
                ActiveShader = _renderer.ShaderName;
                _onMessage?.Invoke($"Shader : {ActiveShader}");
            }
            catch (Exception ex)
            {
                Log.Error($"Shader '{options.Shader}' failed to load", ex);
                _onMessage?.Invoke($"Erreur shader {options.Shader} : {ex.Message}");
            }
        }
        if (previous.CaptureCursor != options.CaptureCursor)
            _capture!.SetCursorCapture(options.CaptureCursor);
        if (!string.Equals(previous.Resolution, options.Resolution, StringComparison.OrdinalIgnoreCase))
            _onMessage?.Invoke("Nouvelle résolution : appliquée au prochain démarrage de la mise à l'échelle");
        if (previous.AutoCrop != options.AutoCrop || previous.ClientAreaOnly != options.ClientAreaOnly
            || previous.CropLeft != options.CropLeft || previous.CropTop != options.CropTop
            || previous.CropRight != options.CropRight || previous.CropBottom != options.CropBottom)
            _blackBars.Reset();
    }

    /// <summary>Switches the output monitor to the configured mode (full-screen output only).</summary>
    private void ApplyResolution()
    {
        if (ResolutionRequest.IsUnchanged(_options.Resolution))
            return;
        if (_options.Preview)
        {
            Log.Info($"Preview: resolution {_options.Resolution} not applied");
            return;
        }
        if (!ResolutionRequest.TryParse(_options.Resolution, out var request))
        {
            _onMessage?.Invoke($"Résolution invalide : {_options.Resolution} (ex. 1920x240@60)");
            return;
        }
        try
        {
            _displayChange = DisplayModeChange.Apply(_monitorDevice!, request);
            if (_displayChange != null)
                _onMessage?.Invoke($"Écran en {_displayChange.Mode}, la résolution d'origine reviendra à l'arrêt");
        }
        catch (Exception ex)
        {
            // Keep scaling at the current resolution rather than leaving the cabinet without a picture.
            Log.Error("Display mode change failed", ex);
            _onMessage?.Invoke(ex.Message);
        }
    }

    private Rectangle ResolveOutputBounds()
    {
        RECT monitor = (MonitorInfo.GetAll().FirstOrDefault(m => m.DeviceName == _monitorDevice) ?? PickMonitor()).Bounds;
        if (_options.Preview)
            return new Rectangle(monitor.Left + 40, monitor.Top + 40, Math.Max(64, _options.PreviewWidth), Math.Max(16, _options.PreviewHeight));
        return Rectangle.FromLTRB(monitor.Left, monitor.Top, monitor.Right, monitor.Bottom);
    }

    private MonitorInfo PickMonitor()
    {
        if (_options.Monitor > 0)
        {
            var all = MonitorInfo.GetAll();
            if (_options.Monitor <= all.Count)
                return all[_options.Monitor - 1];
            Log.Warn($"Monitor {_options.Monitor} not found, using the source window's monitor");
        }
        return MonitorInfo.FromWindow(_source);
    }

    /// <summary>Follows display mode changes (e.g. switching between 1920x240 and 2560x240).</summary>
    private void CheckDisplayBounds()
    {
        var bounds = ResolveOutputBounds();
        if (bounds == _outputBounds)
            return;
        Log.Info($"Output bounds changed from {_outputBounds} to {bounds}");
        _outputBounds = bounds;
        _overlay!.SetBounds(bounds);
        _renderer!.Context.ClearState(); // the back buffer must not be bound while resizing
        _presenter!.Resize(_renderer.Device, bounds.Width, bounds.Height);
    }

    private void Cleanup()
    {
        TryDispose(_capture);
        TryDispose(_presenter);
        TryDispose(_renderer);
        TryDispose(_overlay);
        OverlayWindow.PumpMessages();
        // Last, once the overlay is gone: the desktop's resolution from before the scaling.
        TryDispose(_displayChange);
        _capture = null;
        _presenter = null;
        _renderer = null;
        _overlay = null;
        _displayChange = null;
    }

    private static void TryDispose(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"Dispose {disposable?.GetType().Name}: {ex.Message}");
        }
    }
}
