// Copyright (c) Benjamin Maurin (njz3) 2026

using SuperResScaler.Config;
using SuperResScaler.Engine;

namespace SuperResScaler.Tests;

public class GeometryTests
{
    [Theory]
    [InlineData("auto", 1920, 240, 4.0 / 3)]     // super-resolution CRT modes
    [InlineData("auto", 2560, 240, 4.0 / 3)]
    [InlineData("auto", 1280, 240, 4.0 / 3)]
    [InlineData("auto", 1920, 480, 4.0 / 3)]     // interlaced super-resolution
    [InlineData("", 1920, 240, 4.0 / 3)]
    [InlineData("auto", 640, 480, 4.0 / 3)]      // square pixels
    [InlineData("auto", 1920, 1080, 16.0 / 9)]
    [InlineData("auto", 5120, 1440, 32.0 / 9)]   // super-ultrawide LCD, not a CRT mode
    [InlineData("16:9", 1920, 240, 16.0 / 9)]    // explicit shape wins
    public void DisplayAspect(string setting, int width, int height, double expected)
        => Assert.Equal(expected, Geometry.ResolveDisplayAspect(setting, width, height), 6);

    [Theory]
    // Stretch (linuxloader FULLSCREEN_STRETCH): the frame fills the screen.
    [InlineData("Stretch", "original", "auto", 320, 240, 1920, 240, 0, 0, 1920, 240)]
    [InlineData("Stretch", "original", "auto", 1280, 720, 1920, 240, 0, 0, 1920, 240)]
    [InlineData("Stretch", "16:9_STRETCH", "auto", 640, 480, 1920, 240, 0, 0, 1920, 240)]
    [InlineData("Stretch", "4:3_crop", "auto", 1280, 720, 1920, 240, 0, 0, 1920, 240)]
    // Stretch with 4:3 / 16:9 (= _LETTERBOX): the picture keeps its shape inside the stretched frame.
    [InlineData("Stretch", "16:9", "auto", 640, 480, 1920, 240, 240, 0, 1440, 240)]
    [InlineData("Stretch", "4:3_LETTERBOX", "auto", 1280, 720, 1920, 240, 0, 30, 1920, 180)]
    [InlineData("Stretch", "4:3", "auto", 640, 480, 1920, 240, 0, 0, 1920, 240)]
    // KeepAspect (FULLSCREEN) on a 4:3 super-resolution CRT.
    [InlineData("KeepAspect", "original", "auto", 640, 480, 1920, 240, 0, 0, 1920, 240)]
    [InlineData("KeepAspect", "original", "auto", 1280, 720, 1920, 240, 0, 30, 1920, 180)]
    [InlineData("KeepAspect", "original", "auto", 256, 224, 2560, 240, 183, 0, 2194, 240)]
    [InlineData("KeepAspect", "4:3", "auto", 256, 224, 2560, 240, 183, 0, 2194, 240)]
    [InlineData("KeepAspect", "4:3_stretch", "auto", 256, 224, 2560, 240, 0, 0, 2560, 240)]
    [InlineData("KeepAspect", "16:9", "auto", 640, 480, 1920, 240, 240, 30, 1440, 180)]
    [InlineData("KeepAspect", "16:9_stretch", "auto", 640, 480, 1920, 240, 0, 30, 1920, 180)]
    [InlineData("KeepAspect", "16:9_crop", "auto", 640, 480, 1920, 240, 0, 30, 1920, 180)]
    [InlineData("KeepAspect", "4:3 letterbox", "auto", 1280, 720, 1920, 240, 0, 30, 1920, 180)]
    [InlineData("KeepAspect", "4:3_crop", "auto", 1280, 720, 1920, 240, 0, 0, 1920, 240)]
    [InlineData("KeepAspect", "original", "16:9", 640, 480, 1920, 240, 240, 0, 1440, 240)]
    // KeepAspect on a square-pixel LCD.
    [InlineData("KeepAspect", "original", "auto", 640, 480, 1920, 1080, 240, 0, 1440, 1080)]
    [InlineData("KeepAspect", "4:3_stretch", "auto", 256, 224, 1920, 1080, 240, 0, 1440, 1080)]
    [InlineData("KeepAspect", "4:3", "auto", 256, 224, 1920, 1080, 343, 0, 1234, 1080)]
    public void Destination(string mode, string aspect, string display, int sourceWidth, int sourceHeight,
        int outputWidth, int outputHeight, int x, int y, int width, int height)
    {
        var options = new ScalingOptions
        {
            Mode = Enum.Parse<ScaleMode>(mode),
            AspectRatio = aspect,
            DisplayAspect = display,
        };
        var dest = Geometry.ComputeDestination(options, sourceWidth, sourceHeight, outputWidth, outputHeight);
        Assert.Equal(new RectangleF(x, y, width, height), dest);
    }

    [Theory]
    [InlineData("original", PictureAspectKind.Original, 0)]
    [InlineData("", PictureAspectKind.Original, 0)]
    [InlineData("auto", PictureAspectKind.Original, 0)]
    [InlineData("4:3", PictureAspectKind.Letterbox, 4.0 / 3)]
    [InlineData("16/9", PictureAspectKind.Letterbox, 16.0 / 9)]
    [InlineData("4:3 letterbox", PictureAspectKind.Letterbox, 4.0 / 3)]
    [InlineData("4:3_LETTERBOX", PictureAspectKind.Letterbox, 4.0 / 3)]
    [InlineData("16:9-letterbox", PictureAspectKind.Letterbox, 16.0 / 9)]
    [InlineData("4:3_STRETCH", PictureAspectKind.Stretch, 4.0 / 3)]
    [InlineData("16:9 stretch", PictureAspectKind.Stretch, 16.0 / 9)]
    [InlineData("4:3_CROP", PictureAspectKind.Crop, 4.0 / 3)]
    [InlineData("16:9-crop", PictureAspectKind.Crop, 16.0 / 9)]
    public void ParsesAspect(string text, PictureAspectKind kind, double ratio)
    {
        Assert.True(PictureAspect.TryParse(text, out var aspect));
        Assert.Equal(kind, aspect.Kind);
        Assert.Equal(ratio, aspect.Ratio, 6);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("letterbox")]
    [InlineData("crop")]
    [InlineData("4:0")]
    public void RejectsInvalidAspect(string text) => Assert.False(PictureAspect.TryParse(text, out _));

    [Theory]
    // 16:9 picture in a 4:3 frame: the sides go, 960 of the 1280 columns kept (as linuxloader).
    [InlineData("4:3_crop", 0, 1280, 720, 160, 0, 960, 720)]
    [InlineData("4:3_crop", -100, 1280, 720, 0, 0, 960, 720)]
    [InlineData("4:3_crop", 100, 1280, 720, 320, 0, 960, 720)]
    // 4:3 picture in a 16:9 frame: above and below go, 360 of the 480 rows kept.
    [InlineData("16:9_crop", 0, 640, 480, 0, 60, 640, 360)]
    [InlineData("16:9_crop", -100, 640, 480, 0, 0, 640, 360)]
    [InlineData("16:9_crop", 100, 640, 480, 0, 120, 640, 360)]
    // Same shape: nothing to cut.
    [InlineData("4:3_crop", 0, 640, 480, 0, 0, 640, 480)]
    public void CropKeepsPartOfThePicture(string aspect, int position, int sourceWidth, int sourceHeight, int x, int y, int width, int height)
    {
        var options = new ScalingOptions { Mode = ScaleMode.KeepAspect, AspectRatio = aspect, CropPosition = position };
        var placement = Geometry.ComputePlacement(options, sourceWidth, sourceHeight, 1920, 240);
        Assert.Equal(new Rectangle(x, y, width, height), placement.Source);
        Assert.Null(placement.Barrel);
    }

    [Fact]
    public void BarrelKeepsTheWholePicture()
    {
        var options = new ScalingOptions { Mode = ScaleMode.KeepAspect, AspectRatio = "4:3_crop", CropBarrel = true };
        var placement = Geometry.ComputePlacement(options, 1280, 720, 1920, 240);
        Assert.Equal(new Rectangle(0, 0, 1280, 720), placement.Source);
        Assert.Equal(new RectangleF(0, 0, 1920, 240), placement.Dest);
        Assert.NotNull(placement.Barrel);
        Assert.True(placement.Barrel.Horizontal);

        var sameShape = Geometry.ComputePlacement(options, 640, 480, 1920, 240);
        Assert.Null(sameShape.Barrel); // 4:3 in 4:3: nothing to bend
    }

    [Fact]
    public void BarrelMatchesLinuxloader()
    {
        // k = 0.75 (a 16:9 picture in 4:3), crop centred, middle 60 %, radius 50 %.
        var barrel = BarrelStrip.Create(horizontal: true, share: 0.75, kept: 0.5, center: 0.6, radius: 0.5);
        Assert.Equal(0, barrel.SourceAt(0), 6);        // the picture's edge on the frame's edge
        Assert.Equal(1, barrel.SourceAt(1), 6);
        Assert.Equal(0.5, barrel.SourceAt(0.5), 6);
        Assert.Equal(0.275, barrel.SourceAt(0.2), 6);  // the middle is the plain crop: start + k (1 - c) / 2
        Assert.Equal(0.725, barrel.SourceAt(0.8), 6);
        // Edge: L = 0.275 / 0.75, t = L / R, s = L asin(sin(t) x / D) / t at x = 0.1, D = 0.2.
        Assert.Equal(0.14704, barrel.SourceAt(0.1), 4);
        var points = barrel.Points;
        Assert.Equal(2 * (BarrelStrip.StepsPerEdge + 1), points.Count);
        for (int i = 1; i < points.Count; i++)
            Assert.True(points[i].Y >= points[i - 1].Y, "the strip must not fold over");
    }

    [Fact]
    public void BarrelWithFullMiddleIsThePlainCrop()
    {
        var barrel = BarrelStrip.Create(horizontal: true, share: 0.75, kept: 0.5, center: 1, radius: 0.5);
        Assert.Equal(0.125, barrel.SourceAt(0), 6);
        Assert.Equal(0.875, barrel.SourceAt(1), 6);
    }

    [Fact]
    public void BarrelFollowsCropPosition()
    {
        // Kept part on the left: nothing left of the middle to bend, the right edge holds the rest.
        var barrel = BarrelStrip.Create(horizontal: true, share: 0.75, kept: 0, center: 0.6, radius: 0.5);
        Assert.Equal(0, barrel.SourceAt(0), 6);
        Assert.Equal(0.15, barrel.SourceAt(0.2), 6);
        Assert.Equal(0.6, barrel.SourceAt(0.8), 6);
        Assert.Equal(1, barrel.SourceAt(1), 6);
    }
}
