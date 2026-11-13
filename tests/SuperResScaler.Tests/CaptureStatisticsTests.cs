// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Diagnostics;
using SuperResScaler.Engine;

namespace SuperResScaler.Tests;

public class CaptureStatisticsTests
{
    [Fact]
    public void FormatsTheReport()
        => Assert.Equal("Capture: 60.0 frames/s, frame age when picked up 8.2 ms on average, 16.7 ms at most",
            CaptureStatistics.Format(600, TimeSpan.FromSeconds(10), 8.2, 16.7));

    [Fact]
    public void ReportsOncePerPeriod()
    {
        var statistics = new CaptureStatistics();
        statistics.Add(TimeSpan.FromMilliseconds(5));
        statistics.Add(TimeSpan.FromMilliseconds(15));
        statistics.Add(TimeSpan.FromMilliseconds(-1)); // clocks a hair apart: counted as 0

        Assert.Null(statistics.TryReport(Stopwatch.GetTimestamp()));
        Assert.Null(statistics.LastReport);

        long later = Stopwatch.GetTimestamp() + 11 * Stopwatch.Frequency;
        string? report = statistics.TryReport(later);
        Assert.NotNull(report);
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
