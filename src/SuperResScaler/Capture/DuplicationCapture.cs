// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Diagnostics;
using SharpGen.Runtime;
using SuperResScaler.Native;
using Vortice.Direct3D11;
using Vortice.DXGI;
using ResultCode = Vortice.DXGI.ResultCode;

namespace SuperResScaler.Capture;

/// <summary>
/// Captures a whole display with the DXGI desktop duplication (IDXGIOutput1::DuplicateOutput), which
/// Windows 10 1809 has, unlike Windows.Graphics.Capture. It captures what the display shows,
/// overlapping windows included, and never draws the cursor. A mode change or a secure desktop (UAC,
/// lock screen) loses the duplication, and so does a renumbering of the display (the virtual display
/// gets a new \\.\DISPLAYn each time its modes are reloaded): it is then opened again on the display,
/// found again by its stable id. The capture ends only when the display stays absent.
/// The D3D11 device must belong to the graphics card of the display (see <see cref="FindAdapter"/>).
/// </summary>
internal sealed class DuplicationCapture : IFrameSource
{
    /// <summary>Time a display may be absent (renumbering, mode change) before the capture ends.</summary>
    public static readonly TimeSpan MissingGrace = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(250);

    private readonly ID3D11Device _device;
    private readonly string _display;
    private IDXGIOutputDuplication? _duplication;
    private bool _holdingFrame;
    private RECT _bounds;
    private bool _awaitingPicture;
    private int _nudges;
    private long _nudgeAt;
    private long _missingSince;
    private long _nextRetry;
    private string? _lastProblem;
    private volatile bool _closed;

    /// <param name="display">The display: its stable id, number or "virtuel" (see <see cref="MonitorInfo.Find"/>).</param>
    public DuplicationCapture(ID3D11Device device, string display)
    {
        _device = device;
        _display = display;
        if (Open(out _) is { } problem)
            throw new InvalidOperationException($"Duplication de bureau impossible : {problem}");
    }

    public CaptureMethod Method => CaptureMethod.DesktopDuplication;

    public bool IsClosed => _closed;

    /// <summary>The device name the duplication is open on (\\.\DISPLAYn), null while it is lost.</summary>
    public string? DeviceName { get; private set; }

    /// <summary>The duplication never draws the cursor.</summary>
    public void SetCursorCapture(bool enabled) { }

    public CapturedFrame? TryGetLatestFrame()
    {
        if (_duplication == null && !Reopen())
            return null;
        Release(); // frames are given back when disposed, this is only a safety net

        Result result = _duplication!.AcquireNextFrame(0, out OutduplFrameInfo info, out IDXGIResource? resource);
        if (result == ResultCode.WaitTimeout)
        {
            NudgeIfStill();
            return null;
        }
        if (result.Failure || resource == null)
        {
            Lose(result == ResultCode.AccessLost ? "accès perdu (changement de mode, bureau sécurisé…)" : result.ToString());
            return null;
        }
        _holdingFrame = true;

        // No new picture: only the cursor moved, or this is the first frame of the duplication,
        // which is empty (black) until the display is updated.
        if (info.LastPresentTime == 0)
        {
            resource.Dispose();
            Release();
            NudgeIfStill();
            return null;
        }
        _awaitingPicture = false;

        ID3D11Texture2D texture;
        try
        {
            texture = resource.QueryInterface<ID3D11Texture2D>();
        }
        catch
        {
            Release();
            throw;
        }
        finally
        {
            resource.Dispose();
        }
        var description = texture.Description;
        long composed = info.LastPresentTime != 0 ? info.LastPresentTime : Stopwatch.GetTimestamp();
        return new CapturedFrame(texture, (int)description.Width, (int)description.Height, QpcToTime(composed), Release);
    }

    /// <summary>The graphics card the display <paramref name="deviceName"/> (\\.\DISPLAYn) belongs to, or null.</summary>
    public static IDXGIAdapter1? FindAdapter(string deviceName)
    {
        using var output = FindOutput(deviceName, out var adapter);
        return adapter;
    }

    /// <summary>The DXGI output of the display <paramref name="deviceName"/> and its graphics card, or null.</summary>
    private static IDXGIOutput1? FindOutput(string deviceName, out IDXGIAdapter1? adapter)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint a = 0; factory.EnumAdapters1(a, out IDXGIAdapter1? candidate).Success; a++)
        {
            for (uint o = 0; candidate!.EnumOutputs(o, out IDXGIOutput? output).Success; o++)
            {
                using (output)
                {
                    if (string.Equals(output!.Description.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        adapter = candidate;
                        return output.QueryInterface<IDXGIOutput1>();
                    }
                }
            }
            candidate.Dispose();
        }
        adapter = null;
        return null;
    }

    /// <summary>Opens the duplication; returns null, or what prevents it (and whether the display is absent).</summary>
    private string? Open(out bool missing)
    {
        missing = false;
        var monitor = MonitorInfo.Find(MonitorInfo.GetAll(), _display);
        if (monitor == null)
        {
            missing = true;
            return $"écran {_display} absent";
        }
        using var output = FindOutput(monitor.DeviceName, out var adapter);
        using (adapter)
        {
            if (output == null)
            {
                missing = true;
                return $"{monitor.DeviceName} absent des sorties DXGI";
            }
            try
            {
                _duplication = output.DuplicateOutput(_device);
            }
            catch (SharpGenException ex)
            {
                return Explain(ex.ResultCode, adapter!.Description.Description);
            }
        }

        DeviceName = monitor.DeviceName;
        _bounds = monitor.Bounds;
        _awaitingPicture = true;
        _nudges = 0;
        _nudgeAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 10;
        var description = _duplication.Description;
        Log.Info($"Capture started (desktop duplication): {monitor}, {description.ModeDescription.Width}x{description.ModeDescription.Height}"
                 + $", rotation {description.Rotation}{(description.DesktopImageInSystemMemory ? ", image in system memory" : "")}");
        if (description.Rotation is not (ModeRotation.Identity or ModeRotation.Unspecified))
            Log.Warn("Desktop duplication: rotated display, the picture is captured unrotated");
        return null;
    }

    private static string Explain(Result result, string adapter)
    {
        if (result == ResultCode.Unsupported || result.Code == unchecked((int)0x80070057)) // E_INVALIDARG
            return $"cet écran est déjà dupliqué par SuperResScaler, ou ne peut pas l'être avec la carte graphique du rendu ({adapter}) : {result.Code:X8}";
        if (result == ResultCode.NotCurrentlyAvailable)
            return "trop de captures de cet écran en même temps";
        if (result.Code == unchecked((int)0x80070005)) // E_ACCESSDENIED
            return "accès refusé (bureau sécurisé : UAC, écran de verrouillage…)";
        if (result == ResultCode.SessionDisconnected)
            return "session déconnectée (bureau à distance)";
        return result.ToString();
    }

    /// <summary>Opens the duplication again, at most every 250 ms; the capture ends once the display stays absent.</summary>
    private bool Reopen()
    {
        long now = Stopwatch.GetTimestamp();
        if (now < _nextRetry)
            return false;
        _nextRetry = now + (long)(RetryInterval.TotalSeconds * Stopwatch.Frequency);

        string? problem = Open(out bool missing);
        if (problem == null)
        {
            Log.Info($"Desktop duplication open again on {DeviceName}");
            _missingSince = 0;
            _lastProblem = null;
            return true;
        }
        if (problem != _lastProblem)
            Log.Info($"Desktop duplication: {problem}");
        _lastProblem = problem;
        if (!missing)
            _missingSince = 0;
        else if (_missingSince == 0)
            _missingSince = now;
        else if (Stopwatch.GetElapsedTime(_missingSince, now) > MissingGrace)
            _closed = true;
        return false;
    }

    /// <summary>
    /// The picture only comes with an update of the display: a still one (a paused game, the
    /// self-test's pattern) is asked to repaint the windows over it, three times at most.
    /// </summary>
    private void NudgeIfStill()
    {
        long now = Stopwatch.GetTimestamp();
        if (!_awaitingPicture || _nudges >= 3 || now < _nudgeAt)
            return;
        _nudges++;
        _nudgeAt = now + Stopwatch.Frequency / 2;
        var area = _bounds;
        User32.RedrawWindow(IntPtr.Zero, ref area, IntPtr.Zero, User32.RDW_INVALIDATE | User32.RDW_ERASE | User32.RDW_FRAME | User32.RDW_ALLCHILDREN);
    }

    private void Lose(string reason)
    {
        Release();
        _duplication?.Dispose();
        _duplication = null;
        if (reason != _lastProblem)
            Log.Info($"Desktop duplication of {DeviceName}: {reason}, opening it again");
        _lastProblem = reason;
        DeviceName = null;
    }

    private void Release()
    {
        if (!_holdingFrame)
            return;
        _holdingFrame = false;
        _duplication?.ReleaseFrame();
    }

    /// <summary>A QueryPerformanceCounter value in the clock of <see cref="CapturedFrame.ComposedAt"/>.</summary>
    private static TimeSpan QpcToTime(long qpc) => TimeSpan.FromTicks((long)(qpc * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency)));

    public void Dispose()
    {
        Release();
        _duplication?.Dispose();
        _duplication = null;
    }
}
