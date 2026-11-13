// Copyright (c) Benjamin Maurin (njz3) 2026

using SuperResScaler.App;
using SuperResScaler.Capture;
using SuperResScaler.Config;

namespace SuperResScaler.Tests;

public class CaptureMethodTests
{
    [Theory]
    // Windows 10 1903 or later: Windows.Graphics.Capture unless the duplication is asked for.
    [InlineData(CaptureMethod.Auto, true, CaptureMethod.GraphicsCapture, false)]
    [InlineData(CaptureMethod.GraphicsCapture, true, CaptureMethod.GraphicsCapture, false)]
    [InlineData(CaptureMethod.DesktopDuplication, true, CaptureMethod.DesktopDuplication, false)]
    // Windows 10 1809 (the cabinets): always the duplication, with a note when something else was asked for.
    [InlineData(CaptureMethod.Auto, false, CaptureMethod.DesktopDuplication, false)]
    [InlineData(CaptureMethod.GraphicsCapture, false, CaptureMethod.DesktopDuplication, true)]
    [InlineData(CaptureMethod.DesktopDuplication, false, CaptureMethod.DesktopDuplication, false)]
    public void ChoosesTheMethodOfAWholeDisplay(CaptureMethod requested, bool graphicsCapture, CaptureMethod expected, bool note)
    {
        var choice = CaptureSupport.Choose(requested, wholeDisplay: true, graphicsCapture);
        Assert.Equal(expected, choice.Method);
        Assert.Equal(note, choice.Note != null);
    }

    [Fact]
    public void CapturesAWindowWithWindowsGraphicsCaptureOnly()
    {
        Assert.Equal(new CaptureChoice(CaptureMethod.GraphicsCapture, null), CaptureSupport.Choose(CaptureMethod.Auto, wholeDisplay: false, true));
        var duplication = CaptureSupport.Choose(CaptureMethod.DesktopDuplication, wholeDisplay: false, true);
        Assert.Equal(CaptureMethod.GraphicsCapture, duplication.Method);
        Assert.Contains("écran entier", duplication.Note);

        var error = Assert.Throws<NotSupportedException>(() => CaptureSupport.Choose(CaptureMethod.Auto, wholeDisplay: false, false));
        Assert.Contains("1903", error.Message);
    }

    [Fact]
    public void ThisWindowsHasWindowsGraphicsCapture()
    {
        // The development PC runs Windows 11; the cabinets' 1809 is build 17763.
        Assert.True(CaptureSupport.WindowsBuild >= CaptureSupport.GraphicsCaptureBuild);
        Assert.True(CaptureSupport.GraphicsCaptureAvailable);
    }

    [Theory]
    [InlineData("auto", CaptureMethod.Auto)]
    [InlineData("wgc", CaptureMethod.GraphicsCapture)]
    [InlineData("Duplication", CaptureMethod.DesktopDuplication)]
    [InlineData("dda", CaptureMethod.DesktopDuplication)]
    public void ParsesTheCaptureOption(string value, CaptureMethod expected)
    {
        var cli = CommandLineOptions.Parse(["--display", "virtuel", "--capture", value]);
        var options = new ScalingOptions { CaptureMethod = CaptureMethod.GraphicsCapture };
        cli.ApplyOverrides(options);
        Assert.Equal(expected, options.CaptureMethod);
    }

    [Fact]
    public void RejectsAnUnknownCaptureMethod()
        => Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--capture", "bitblt"]));

    [Theory]
    [InlineData(2560, 1280)]
    [InlineData(1920, 1280)]
    [InlineData(640, 560)] // the cabinets' 640x480: a 1280-wide preview did not fit (Racecab1, 13/11)
    public void FitsTheSelfTestPreviewOnTheDisplay(int displayWidth, int previewWidth)
    {
        var monitor = new SuperResScaler.Native.MonitorInfo(1, @"\\.\DISPLAY1", "PHL", "id", new SuperResScaler.Native.RECT(0, 0, displayWidth, 480), true, IntPtr.Zero);
        Assert.Equal(previewWidth, SelfTest.PreviewWidth(monitor));
    }

    [Fact]
    public void CopiesTheCaptureMethod()
    {
        var options = new ScalingOptions { CaptureMethod = CaptureMethod.DesktopDuplication };
        Assert.Equal(CaptureMethod.DesktopDuplication, options.Snapshot().CaptureMethod);
        Assert.Equal(CaptureMethod.Auto, new ScalingOptions().CaptureMethod);
    }
}
