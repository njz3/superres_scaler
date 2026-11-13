// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Diagnostics;
using System.Globalization;

namespace SuperResScaler.Engine;

/// <summary>
/// What the capture costs while scaling, written to the log every 10 s: captured frames per second,
/// and the age of each frame when the render loop picks it up (from its composition by Windows
/// to its copy into the renderer). Presenting it adds one refresh of the output display with VSync.
/// </summary>
internal sealed class CaptureStatistics
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private long _start = Stopwatch.GetTimestamp();
    private int _frames;
    private double _totalAgeMs;
    private double _maxAgeMs;

    /// <summary>The last report, null before the first one.</summary>
    public string? LastReport { get; private set; }

    /// <summary>Current time in the clock of Direct3D11CaptureFrame.SystemRelativeTime (QueryPerformanceCounter).</summary>
    public static TimeSpan Now() => TimeSpan.FromTicks((long)(Stopwatch.GetTimestamp() * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency)));

    /// <summary>Counts a frame picked up <paramref name="age"/> after it was composed.</summary>
    public void Add(TimeSpan age)
    {
        double ms = Math.Max(0, age.TotalMilliseconds);
        _frames++;
        _totalAgeMs += ms;
        _maxAgeMs = Math.Max(_maxAgeMs, ms);
    }

    /// <summary>Returns the report once per <see cref="Interval"/> (null otherwise) and starts a new period.</summary>
    public string? TryReport(long now)
    {
        var elapsed = Stopwatch.GetElapsedTime(_start, now);
        if (elapsed < Interval)
            return null;
        LastReport = Format(_frames, elapsed, _frames > 0 ? _totalAgeMs / _frames : 0, _maxAgeMs);
        _start = now;
        _frames = 0;
        _totalAgeMs = 0;
        _maxAgeMs = 0;
        return LastReport;
    }

    public static string Format(int frames, TimeSpan elapsed, double averageAgeMs, double maxAgeMs)
        => string.Format(CultureInfo.InvariantCulture, "Capture: {0:0.0} frames/s, frame age when picked up {1:0.0} ms on average, {2:0.0} ms at most",
            frames / elapsed.TotalSeconds, averageAgeMs, maxAgeMs);
}
