// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Drawing;
using System.Text.Json;
using SuperResScaler.App;
using SuperResScaler.Config;
using SuperResScaler.Engine;
using SuperResScaler.Native;

namespace SuperResScaler.Tests;

public class DisplaySelectionTests
{
    private static MonitorInfo Display(int number, string name, string id, bool primary = false, int? driverNumber = null)
        => new(number, $@"\\.\DISPLAY{driverNumber ?? number}", name, id, new RECT(0, 0, 1920, 1080), primary, IntPtr.Zero);

    private static readonly MonitorInfo Dell = Display(1, "DELL U2415", "path-a", primary: true);
    private static readonly MonitorInfo Crt = Display(2, "CRT arcade", "path-b");
    private static readonly List<MonitorInfo> Two = [Dell, Crt];

    [Theory]
    [InlineData("path-b", "path-b")]
    [InlineData("PATH-B", "path-b")]
    [InlineData("2", "path-b")]
    [InlineData(" 1 ", "path-a")]
    [InlineData(@"\\.\DISPLAY2", "path-b")]
    [InlineData("crt", "path-b")]              // a part of the monitor's name
    [InlineData("dell", "path-a")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("3", null)]
    [InlineData("LG", null)]
    public void FindAcceptsAnyReference(string? reference, string? expectedId)
        => Assert.Equal(expectedId, MonitorInfo.Find(Two, reference)?.Id);

    [Fact]
    public void FindUsesTheCorrectedNumber()
    {
        var renumbered = Display(5, "CRT arcade", "path-b", driverNumber: 2);
        Assert.Equal(2, renumbered.DriverNumber);
        Assert.Same(renumbered, MonitorInfo.Find([Dell, renumbered], "5"));
        Assert.Null(MonitorInfo.Find([Dell, renumbered], "2"));
    }

    [Fact]
    public void AutoOutputWithoutDisplayCaptureIsTheWindowsDisplayOrThePrimary()
    {
        Assert.Same(Crt, MonitorInfo.AutoOutput(Two, null, Crt));
        Assert.Same(Dell, MonitorInfo.AutoOutput(Two, null, null));
    }

    [Fact]
    public void AutoOutputAvoidsTheCapturedDisplay()
    {
        Assert.Same(Dell, MonitorInfo.AutoOutput(Two, Crt, Crt));
        Assert.Same(Crt, MonitorInfo.AutoOutput(Two, Dell, Dell));
        Assert.Same(Dell, MonitorInfo.AutoOutput([Dell], Dell, null)); // alone: no other choice
    }

    [Theory]
    [InlineData(@"\\.\DISPLAY1", 1)]
    [InlineData(@"\\.\DISPLAY12", 12)]
    [InlineData("display3", 3)]
    [InlineData(@"\\.\DISPLAY", 0)]
    [InlineData("", 0)]
    public void DriverNumber(string deviceName, int expected)
        => Assert.Equal(expected, MonitorInfo.ParseDriverNumber(deviceName));

    [Fact]
    public void LabelShowsNameSizeAndPrimaryThenNumber()
    {
        Assert.Equal("DELL U2415 — 1920x1080 (principal) — Écran 1", Dell.ToString());
        Assert.Equal("CRT arcade — 1920x1080 — Écran 2", Crt.ToString());
        Assert.Equal("DISPLAY3", Display(3, "", "x").Name); // no monitor name known
    }

    [Theory]
    [InlineData("{\"Monitor\":2}", "2")]        // settings.json of the versions that stored a number
    [InlineData("{\"Monitor\":0}", "")]
    [InlineData("{\"Monitor\":\"path-b\"}", "path-b")]
    [InlineData("{}", "")]
    public void MonitorIsReadFromOldAndNewSettings(string json, string expected)
        => Assert.Equal(expected, JsonSerializer.Deserialize<ScalingOptions>(json)!.Monitor);

    [Fact]
    public void MonitorSurvivesSaving()
    {
        string json = JsonSerializer.Serialize(new ScalingOptions { Monitor = "path-b", SourceDisplay = "path-a" });
        var read = JsonSerializer.Deserialize<ScalingOptions>(json)!;
        Assert.Equal("path-b", read.Monitor);
        Assert.Equal("path-a", read.SourceDisplay);
    }

    [Fact]
    public void SnapshotKeepsTheDisplays()
    {
        var copy = new ScalingOptions { Monitor = "path-b", SourceDisplay = "path-a" }.Snapshot();
        Assert.Equal("path-b", copy.Monitor);
        Assert.Equal("path-a", copy.SourceDisplay);
    }

    [Fact]
    public void DisplayRegionIsTheDisplayMinusTheCrop()
    {
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), Geometry.ComputeDisplayRegion(new ScalingOptions(), 1920, 1080));
        var crop = new ScalingOptions { CropLeft = 10, CropTop = 20, CropRight = 30, CropBottom = 40 };
        Assert.Equal(Rectangle.FromLTRB(10, 20, 1890, 1040), Geometry.ComputeDisplayRegion(crop, 1920, 1080));
    }

    [Fact]
    public void DisplayRegionIsEmptyWhenTheCropTakesEverything()
        => Assert.Equal(0, Geometry.ComputeDisplayRegion(new ScalingOptions { CropLeft = 5000 }, 1920, 1080).Width);

    [Fact]
    public void DisplayAloneStartsWithoutAWindow()
    {
        var cli = CommandLineOptions.Parse(["--display", "2"]);
        Assert.True(cli.IsHeadless);
        Assert.True(cli.DisplayOnly);
        var options = new ScalingOptions();
        cli.ApplyOverrides(options);
        Assert.Equal("2", options.SourceDisplay);
    }

    [Fact]
    public void DisplayWithAProcessWaitsForIt()
    {
        var cli = CommandLineOptions.Parse(["--display", "DELL", "--process", "game.exe"]);
        Assert.True(cli.IsHeadless);
        Assert.False(cli.DisplayOnly);
    }

    [Fact]
    public void NoArgumentMeansTheInterface()
        => Assert.False(CommandLineOptions.Parse([]).IsHeadless);

    [Theory]
    [InlineData("0", "")]
    [InlineData("2", "2")]
    [InlineData("DELL", "DELL")]
    public void MonitorArgument(string value, string expected)
    {
        var options = new ScalingOptions { Monitor = "before" };
        CommandLineOptions.Parse(["--monitor", value]).ApplyOverrides(options);
        Assert.Equal(expected, options.Monitor);
    }
}
