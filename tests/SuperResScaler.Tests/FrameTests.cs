// Copyright (c) Benjamin Maurin (njz3) 2026

using SuperResScaler.App;
using SuperResScaler.Config;
using SuperResScaler.Engine;

namespace SuperResScaler.Tests;

/// <summary>The crop modes: the frame chosen in the captured region, inside (zoom in) or beyond it (black, zoom out).</summary>
public class FrameTests
{
    private static Rectangle Frame(ScalingOptions options, int width = 320, int height = 240) => Geometry.ComputeFrame(options, width, height);

    [Fact]
    public void MarginsKeepTheRegionAndAddBlackForNegativeValues()
    {
        Assert.Equal(new Rectangle(0, 0, 320, 240), Frame(new ScalingOptions()));
        // The positive margins cut the region itself (ComputeDisplayRegion), not the frame.
        Assert.Equal(new Rectangle(0, 0, 320, 240), Frame(new ScalingOptions { CropLeft = 10, CropBottom = 20 }));
        Assert.Equal(Rectangle.FromLTRB(-40, 0, 330, 240), Frame(new ScalingOptions { CropLeft = -40, CropRight = -10 }));
        // Half the region at most on each side.
        Assert.Equal(Rectangle.FromLTRB(-160, -120, 320, 240), Frame(new ScalingOptions { CropLeft = -5000, CropTop = -5000 }));
    }

    [Fact]
    public void MarginsOnlyCutTheRegionInTheMarginsMode()
    {
        var margins = new ScalingOptions { CropLeft = 10, CropTop = 20, CropRight = 30, CropBottom = 40 };
        Assert.Equal(Rectangle.FromLTRB(10, 20, 1890, 1040), Geometry.ComputeDisplayRegion(margins, 1920, 1080));
        var negative = new ScalingOptions { CropLeft = -10, CropTop = -20 };
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), Geometry.ComputeDisplayRegion(negative, 1920, 1080));
        var corners = margins.Snapshot();
        corners.FrameMode = FrameMode.Corners;
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), Geometry.ComputeDisplayRegion(corners, 1920, 1080));
    }

    [Theory]
    [InlineData(FrameMode.Corners)]
    [InlineData(FrameMode.Rectangle)]
    public void CornersAreInPercentOfThePicture(FrameMode mode)
    {
        var options = new ScalingOptions { FrameMode = mode, FrameLeft = 25, FrameTop = 25, FrameRight = 75, FrameBottom = 75 };
        Assert.Equal(Rectangle.FromLTRB(80, 60, 240, 180), Frame(options));
        options.FrameLeft = options.FrameTop = -50;
        options.FrameRight = options.FrameBottom = 150;
        Assert.Equal(Rectangle.FromLTRB(-160, -120, 480, 360), Frame(options));
        // Out of range values are held at -50 % and 150 %.
        options.FrameLeft = -400;
        options.FrameRight = 900;
        Assert.Equal(Rectangle.FromLTRB(-160, -120, 480, 360), Frame(options));
    }

    [Fact]
    public void CornersMayBeGivenInAnyOrder()
    {
        var options = new ScalingOptions { FrameMode = FrameMode.Corners, FrameLeft = 75, FrameTop = 100, FrameRight = 25, FrameBottom = 0 };
        Assert.Equal(Rectangle.FromLTRB(80, 0, 240, 240), Frame(options));
        options.FrameRight = 75; // no width: one pixel
        Assert.Equal(new Rectangle(240, 0, 1, 240), Frame(options));
    }

    [Fact]
    public void ZoomIsAroundItsCentre()
    {
        var options = new ScalingOptions { FrameMode = FrameMode.Zoom };
        Assert.Equal(new Rectangle(0, 0, 320, 240), Frame(options));
        options.ZoomX = options.ZoomY = 200;
        Assert.Equal(Rectangle.FromLTRB(80, 60, 240, 180), Frame(options));
        options.ZoomX = options.ZoomY = 50;
        Assert.Equal(Rectangle.FromLTRB(-160, -120, 480, 360), Frame(options));
        options.ZoomX = 200;
        options.ZoomY = 100;
        options.ZoomCenterX = 25;
        Assert.Equal(Rectangle.FromLTRB(0, 0, 160, 240), Frame(options));
        // Below 50 % the zoom is held: the frame never exceeds twice the picture.
        options.ZoomX = options.ZoomY = 10;
        options.ZoomCenterX = 50;
        Assert.Equal(Rectangle.FromLTRB(-160, -120, 480, 360), Frame(options));
    }

    [Fact]
    public void PlacementScalesTheFrame()
    {
        var whole = Geometry.ComputePlacement(new ScalingOptions(), 320, 240, 1920, 240);
        Assert.Null(whole.Frame);

        // Zoomed out to 50 %: a 640x480 frame, still 4:3, fills the 4:3 CRT; the picture is its middle.
        var zoomOut = new ScalingOptions { FrameMode = FrameMode.Zoom, ZoomX = 50, ZoomY = 50, Mode = ScaleMode.KeepAspect };
        var placement = Geometry.ComputePlacement(zoomOut, 320, 240, 1920, 240);
        Assert.Equal(Rectangle.FromLTRB(-160, -120, 480, 360), placement.Frame);
        Assert.Equal(new Rectangle(0, 0, 640, 480), placement.Source);
        Assert.Equal(new RectangleF(0, 0, 1920, 240), placement.Dest);

        // The frame's shape decides the bars: the left half of a 4:3 picture is 2:3 on the 4:3 tube.
        var half = new ScalingOptions { FrameMode = FrameMode.Corners, FrameRight = 50, Mode = ScaleMode.KeepAspect };
        placement = Geometry.ComputePlacement(half, 320, 240, 1920, 240);
        Assert.Equal(new Rectangle(0, 0, 160, 240), placement.Frame);
        Assert.Equal(new RectangleF(480, 0, 960, 240), placement.Dest);
    }

    [Fact]
    public void FrameSettingsAreCopied()
    {
        var options = new ScalingOptions
        {
            FrameMode = FrameMode.Zoom, FrameLeft = 1, FrameTop = 2, FrameRight = 3, FrameBottom = 4,
            ZoomX = 150, ZoomY = 175, ZoomLinked = false, ZoomCenterX = 40, ZoomCenterY = 60, CropLeft = -5,
        };
        var copy = options.Snapshot();
        Assert.Equal((FrameMode.Zoom, 1.0, 2.0, 3.0, 4.0), (copy.FrameMode, copy.FrameLeft, copy.FrameTop, copy.FrameRight, copy.FrameBottom));
        Assert.Equal((150.0, 175.0, false, 40.0, 60.0, -5), (copy.ZoomX, copy.ZoomY, copy.ZoomLinked, copy.ZoomCenterX, copy.ZoomCenterY, copy.CropLeft));
    }

    private static ScalingOptions Parsed(params string[] args)
    {
        var options = new ScalingOptions();
        CommandLineOptions.Parse(["--process", "game.exe", .. args]).ApplyOverrides(options);
        return options;
    }

    [Fact]
    public void FrameIsParsedFromTheCommandLine()
    {
        var crop = Parsed("--crop", "-10,0,5,0");
        Assert.Equal((FrameMode.Margins, -10, 5), (crop.FrameMode, crop.CropLeft, crop.CropRight));

        var frame = Parsed("--frame", "-25,10.5,125,90");
        Assert.Equal((FrameMode.Corners, -25.0, 10.5, 125.0, 90.0), (frame.FrameMode, frame.FrameLeft, frame.FrameTop, frame.FrameRight, frame.FrameBottom));

        var zoom = Parsed("--zoom", "200");
        Assert.Equal((FrameMode.Zoom, 200.0, 200.0, true), (zoom.FrameMode, zoom.ZoomX, zoom.ZoomY, zoom.ZoomLinked));

        var both = Parsed("--zoom", "150,120", "--zoom-center", "40,60");
        Assert.Equal((FrameMode.Zoom, 150.0, 120.0, false, 40.0, 60.0),
            (both.FrameMode, both.ZoomX, both.ZoomY, both.ZoomLinked, both.ZoomCenterX, both.ZoomCenterY));
    }

    [Fact]
    public void SimulatedInterlaceReachesThePlacement()
    {
        Assert.False(Geometry.ComputePlacement(new ScalingOptions(), 640, 480, 1920, 240).Interlaced);
        var options = new ScalingOptions { SimulatedInterlace = true };
        var placement = Geometry.ComputePlacement(options, 640, 480, 1920, 240);
        Assert.True(placement.Interlaced);
        Assert.Equal(new RectangleF(0, 0, 1920, 240), placement.Dest); // same place: only the lines read change
        Assert.False(Geometry.ComputePlacement(options, 640, 1, 1920, 240).Interlaced); // a single line has no fields
    }

    [Theory]
    [InlineData(640, 480, 1920, 240, "Stretch", "original", true)]   // 480 lines on 240: two fields of 240
    [InlineData(400, 300, 1920, 240, "Stretch", "original", true)]   // 300 on 240
    [InlineData(320, 240, 1920, 240, "Stretch", "original", false)]  // as many lines: all of them fit
    [InlineData(640, 480, 1920, 1080, "Stretch", "original", false)] // fewer lines than the screen
    [InlineData(640, 480, 1920, 480, "Stretch", "original", false)]
    [InlineData(640, 480, 1920, 600, "KeepAspect", "16:9", true)]    // letterboxed on the 4:3 tube: the picture is 450 lines high
    [InlineData(320, 240, 640, 480, "Stretch", "4:3_crop", false)]
    public void SimulatedInterlaceNeedsMoreSourceLinesThanTheScreenShows(int sourceWidth, int sourceHeight, int outputWidth, int outputHeight,
                                                                          string mode, string aspect, bool expected)
    {
        var options = new ScalingOptions { SimulatedInterlace = true, Mode = Enum.Parse<ScaleMode>(mode), AspectRatio = aspect };
        var placement = Geometry.ComputePlacement(options, sourceWidth, sourceHeight, outputWidth, outputHeight);
        Assert.Equal(expected, placement.Interlaced);
        Assert.Equal(expected, placement.Source.Height > placement.Dest.Height);
    }

    [Fact]
    public void SimulatedInterlaceIsCopiedAndParsed()
    {
        var options = new ScalingOptions { SimulatedInterlace = true };
        Assert.True(options.Snapshot().SimulatedInterlace);
        Assert.True(Parsed("--interlace").SimulatedInterlace);
        Assert.False(Parsed().SimulatedInterlace);
    }

    [Theory]
    [InlineData("--frame", "0,0,100")]
    [InlineData("--frame", "0,0,100,200")]
    [InlineData("--zoom", "20")]
    [InlineData("--zoom", "100,100,100")]
    [InlineData("--zoom-center", "50")]
    [InlineData("--crop", "1,2,3,x")]
    public void RejectsInvalidFrames(string option, string value)
        => Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--process", "game.exe", option, value]));
}
