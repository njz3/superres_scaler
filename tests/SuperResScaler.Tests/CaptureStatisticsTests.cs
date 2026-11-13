// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Diagnostics;
using SuperResScaler.Engine;

namespace SuperResScaler.Tests;

public class CaptureStatisticsTests
{
    [Fact]
    public void FormatsTheReport()
        => Assert.Equal("Capture: 60.0 source frames/s, 58.0 taken (2.0 never shown), 58.0 presents/s, frame age when taken 8.2 ms on average, 16.7 ms at most",
            CaptureStatistics.Format(600, 580, 580, TimeSpan.FromSeconds(10), 8.2, 16.7));

    [Fact]
    public void ReportsOncePerPeriod()
    {
        var statistics = new CaptureStatistics();
        statistics.Add(TimeSpan.FromMilliseconds(5));
        statistics.Add(TimeSpan.FromMilliseconds(15), sourceFrames: 2); // one frame replaced before being taken
        statistics.Add(TimeSpan.FromMilliseconds(-1), sourceFrames: 0); // clocks a hair apart: age 0; at least 1 frame
        statistics.AddPresent();
        statistics.AddPresent();

        Assert.Null(statistics.TryReport(Stopwatch.GetTimestamp()));
        Assert.Null(statistics.LastReport);

        long later = Stopwatch.GetTimestamp() + 10 * Stopwatch.Frequency;
        string? report = statistics.TryReport(later);
        Assert.NotNull(report);
        // 4 source frames, 3 taken, 2 presents in about 10 s.
        Assert.Contains("0.4 source frames/s, 0.3 taken (0.1 never shown), 0.2 presents/s", report);
        Assert.Contains("6.7 ms on average, 15.0 ms at most", report);
        Assert.Equal(report, statistics.LastReport);

        // A new period starts: nothing more until it is over.
        Assert.Null(statistics.TryReport(later + Stopwatch.Frequency));
    }

    [Fact]
    public void UsesTheClockOfTheCapturedFrames()
    {
        // Direct3D11CaptureFrame.SystemRelativeTime counts QueryPerformanceCounter time in TimeSpan ticks.
        var before = TimeSpan.FromTicks((long)(Stopwatch.GetTimestamp() * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency)));
        var now = CaptureStatistics.Now();
        Assert.InRange((now - before).TotalMilliseconds, 0, 1000);
    }
}
