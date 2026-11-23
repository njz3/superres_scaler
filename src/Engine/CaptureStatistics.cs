// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Diagnostics;
using System.Globalization;

namespace SuperResScaler.Engine;

/// <summary>
/// What the capture costs while scaling, written to the log every 10 s:
/// <list type="bullet">
/// <item>source frames per second: what the captured window or display produced (the game's rate);</item>
/// <item>frames taken: those the render loop received; fewer than the source means frames were replaced
/// by a newer one before being taken, and were never shown;</item>
/// <item>presents: frames sent to the output;</item>
/// <item>the age of each frame when taken, from its composition by Windows to its copy into the renderer.</item>
/// </list>
/// The output then shows the latest frame presented at its next refresh.
/// </summary>
internal sealed class CaptureStatistics
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private long _start = Stopwatch.GetTimestamp();
    private int _sourceFrames;
    private int _frames;
    private int _presents;
    private double _totalAgeMs;
    private double _maxAgeMs;

    /// <summary>The last report, null before the first one.</summary>
    public string? LastReport { get; private set; }

    /// <summary>Current time in the clock of Direct3D11CaptureFrame.SystemRelativeTime (QueryPerformanceCounter).</summary>
    public static TimeSpan Now() => TimeSpan.FromTicks((long)(Stopwatch.GetTimestamp() * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency)));

    /// <summary>Counts a frame taken <paramref name="age"/> after it was composed, standing for <paramref name="sourceFrames"/> frames of the source.</summary>
    public void Add(TimeSpan age, int sourceFrames = 1)
    {
        double ms = Math.Max(0, age.TotalMilliseconds);
        _frames++;
        _sourceFrames += Math.Max(1, sourceFrames);
        _totalAgeMs += ms;
        _maxAgeMs = Math.Max(_maxAgeMs, ms);
    }

    /// <summary>Counts a frame sent to the output.</summary>
    public void AddPresent() => _presents++;

    /// <summary>Returns the report once per <see cref="Interval"/> (null otherwise) and starts a new period.</summary>
    public string? TryReport(long now)
    {
        var elapsed = Stopwatch.GetElapsedTime(_start, now);
        if (elapsed < Interval)
            return null;
        LastReport = Format(_sourceFrames, _frames, _presents, elapsed, _frames > 0 ? _totalAgeMs / _frames : 0, _maxAgeMs);
        _start = now;
        _sourceFrames = 0;
        _frames = 0;
        _presents = 0;
        _totalAgeMs = 0;
        _maxAgeMs = 0;
        return LastReport;
    }

    public static string Format(int sourceFrames, int frames, int presents, TimeSpan elapsed, double averageAgeMs, double maxAgeMs)
    {
        double seconds = elapsed.TotalSeconds;
        return string.Format(CultureInfo.InvariantCulture,
            "Capture: {0:0.0} source frames/s, {1:0.0} taken ({2:0.0} never shown), {3:0.0} presents/s, frame age when taken {4:0.0} ms on average, {5:0.0} ms at most",
            sourceFrames / seconds, frames / seconds, (sourceFrames - frames) / seconds, presents / seconds, averageAgeMs, maxAgeMs);
    }
}
