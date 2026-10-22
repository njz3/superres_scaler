using SuperResScaler.Native;

namespace SuperResScaler.Tests;

public class DisplayModeTests
{
    // Modes as a CRT Emudriver / Switchres setup could list them.
    private static readonly DisplayMode[] CrtModes =
    [
        new(1920, 240, 60, 32, false),
        new(1920, 240, 59, 32, false),
        new(1920, 240, 60, 16, false),
        new(1280, 240, 60, 32, false),
        new(1280, 480, 60, 32, true),
        new(1280, 480, 30, 32, false),
        new(640, 480, 60, 32, false),
        new(640, 480, 60, 32, true),
        new(1920, 1080, 60, 32, false),
    ];

    [Theory]
    [InlineData("1920x240", 1920, 240, null, null)]
    [InlineData("1920x240@60", 1920, 240, null, 60)]
    [InlineData("1280x480i", 1280, 480, true, null)]
    [InlineData("1280x480p@30", 1280, 480, false, 30)]
    [InlineData(" 2560 x 240 @ 59.94Hz ", 2560, 240, null, 60)]
    public void ParsesResolution(string text, int width, int height, bool? interlaced, int? frequency)
    {
        Assert.True(ResolutionRequest.TryParse(text, out var request));
        Assert.Equal(new ResolutionRequest(width, height, interlaced, frequency), request);
    }

    [Theory]
    [InlineData("1920")]
    [InlineData("x240")]
    [InlineData("1920x240@")]
    [InlineData("0x240")]
    public void RejectsInvalidResolution(string text) => Assert.False(ResolutionRequest.TryParse(text, out _));

    [Theory]
    [InlineData("")]
    [InlineData("(inchangée)")]
    [InlineData("none")]
    public void RecognisesUnchanged(string text) => Assert.True(ResolutionRequest.IsUnchanged(text));

    [Theory]
    [InlineData("1920x240", "1920x240@60")]      // current frequency (60) and 32 bits preferred
    [InlineData("1920x240@59", "1920x240@59")]
    [InlineData("1280x480", "1280x480@30")]      // progressive before interlaced when unspecified
    [InlineData("1280x480i", "1280x480i@60")]
    [InlineData("640x480@60", "640x480@60")]
    [InlineData("1280x240@59.94", "1280x240@60")] // ±1 Hz for 59.94 Hz modes
    public void SelectsMode(string request, string expected)
    {
        Assert.True(ResolutionRequest.TryParse(request, out var parsed));
        var current = new DisplayMode(1920, 1080, 60, 32, false);
        var mode = DisplayModes.SelectBest(CrtModes, parsed, current);
        Assert.NotNull(mode);
        Assert.Equal(expected, mode.Value.ToString());
        Assert.Equal(32, mode.Value.BitsPerPixel);
    }

    [Theory]
    [InlineData("2560x240")]
    [InlineData("1920x240@50")]
    [InlineData("1920x240i")]
    public void NoModeForUnavailableResolution(string request)
    {
        Assert.True(ResolutionRequest.TryParse(request, out var parsed));
        Assert.Null(DisplayModes.SelectBest(CrtModes, parsed, null));
    }
}
