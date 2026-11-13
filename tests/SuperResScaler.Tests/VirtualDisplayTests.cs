// Copyright (c) Benjamin Maurin (njz3) 2026

using SuperResScaler.App;
using SuperResScaler.Capture;
using SuperResScaler.Native;
using SuperResScaler.VirtualDisplay;

namespace SuperResScaler.Tests;

public class VirtualDisplayTests
{
    /// <summary>The vdd_settings.xml shipped with Virtual Display Driver 25.7.23 (shortened).</summary>
    private const string DriverDefaultXml = """
        <?xml version='1.0' encoding='utf-8'?>
        <vdd_settings>
            <monitors>
                <count>1</count>
            </monitors>
            <gpu>
                <friendlyname>default</friendlyname>
            </gpu>
        	<global>
        		<!--These are global refreshrates, any you add in here, will be replicated to all resolutions-->
        		<g_refresh_rate>60</g_refresh_rate>
        		<g_refresh_rate>90</g_refresh_rate>
        		<g_refresh_rate>120</g_refresh_rate>
        	</global>
            <resolutions>
                <resolution>
                    <width>800</width>
                    <height>600</height>
                    <refresh_rate>30</refresh_rate>
                </resolution>
                <resolution>
                    <width>1920</width>
                    <height>1080</height>
                    <refresh_rate>30</refresh_rate>
                </resolution>
            </resolutions>
            <options>
        		<CustomEdid>false</CustomEdid>
        		<HardwareCursor>true</HardwareCursor>
        		<logging>false</logging>
        	</options>
        </vdd_settings>
        """;

    [Theory]
    [InlineData("1280x240", 1280, 240, 60)]
    [InlineData("1280x240@60", 1280, 240, 60)]
    [InlineData("1024x384@57.5", 1024, 384, 57.5)]
    [InlineData(" 1024 x 384 @ 57,5 Hz ", 1024, 384, 57.5)]
    [InlineData("512×384@57.5hz", 512, 384, 57.5)]
    [InlineData("320x240@59.94", 320, 240, 59.94)]
    public void ParsesModes(string text, int width, int height, double frequency)
    {
        Assert.True(VirtualMode.TryParse(text, out var mode));
        Assert.Equal(new VirtualMode(width, height, frequency), mode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1280")]
    [InlineData("12x34")]
    [InlineData("1280x240@0")]
    [InlineData("1280x240@2000")]
    [InlineData("1280x240i")]
    [InlineData("abc")]
    public void RejectsInvalidModes(string? text) => Assert.False(VirtualMode.TryParse(text, out _));

    [Fact]
    public void FormatsModesForTheFileAndTheScreen()
    {
        Assert.Equal("1024x384@57.5", new VirtualMode(1024, 384, 57.5).ToString());
        Assert.Equal("1280x240@60", new VirtualMode(1280, 240, 60).ToString());
        Assert.Equal("59.94", new VirtualMode(320, 240, 59.94).FrequencyText);
        Assert.True(new VirtualMode(1024, 384, 57.5).SameAs(new VirtualMode(1024, 384, 57.5001)));
        Assert.False(new VirtualMode(1024, 384, 57.5).SameAs(new VirtualMode(1024, 384, 58)));
    }

    [Fact]
    public void ReadsTheDriversOwnFileWithItsGlobalFrequencies()
    {
        var settings = VirtualDisplaySettings.Parse(DriverDefaultXml);

        Assert.Equal([new VirtualMode(800, 600, 30), new VirtualMode(1920, 1080, 30)], settings.Modes);
        Assert.Equal([60.0, 90.0, 120.0], settings.GlobalRefreshRates);
        // Each size at its own frequency and at each global one, as the driver does.
        var effective = settings.EffectiveModes().ToList();
        Assert.Equal(8, effective.Count);
        Assert.Contains(new VirtualMode(800, 600, 120), effective);
    }

    [Fact]
    public void DefaultSettingsHaveTheCabinetModesOnly()
    {
        var settings = VirtualDisplaySettings.CreateDefault();

        Assert.Equal(VirtualDisplaySettings.CabinetModes, settings.Modes);
        Assert.Empty(settings.GlobalRefreshRates);
        Assert.Equal(new VirtualMode(640, 480, 60), settings.Modes[0]);
        Assert.Contains(new VirtualMode(1024, 384, 57.5), settings.Modes);
        Assert.Equal(settings.Modes, settings.EffectiveModes());
    }

    [Fact]
    public void RewritesOnlyTheResolutions()
    {
        var settings = VirtualDisplaySettings.Parse(DriverDefaultXml);
        Assert.Equal(1, settings.Add([new VirtualMode(1024, 384, 57.5), new VirtualMode(800, 600, 30)]));
        Assert.True(settings.Remove(new VirtualMode(1920, 1080, 30)));
        Assert.False(settings.Remove(new VirtualMode(1920, 1080, 30)));

        string xml = settings.ToXml();
        var again = VirtualDisplaySettings.Parse(xml);

        Assert.Equal([new VirtualMode(800, 600, 30), new VirtualMode(1024, 384, 57.5)], again.Modes);
        Assert.Equal([60.0, 90.0, 120.0], again.GlobalRefreshRates);
        Assert.Contains("<refresh_rate>57.5</refresh_rate>", xml);
        Assert.Contains("<HardwareCursor>true</HardwareCursor>", xml);
        Assert.Contains("<count>1</count>", xml);
        Assert.Contains("These are global refreshrates", xml);
        Assert.DoesNotContain("1080", xml);
    }

    [Fact]
    public void SavesAndLoadsAFile()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SuperResScaler.Tests." + Path.GetRandomFileName());
        try
        {
            string path = Path.Combine(folder, "sub", "vdd_settings.xml");
            Assert.Equal(VirtualDisplaySettings.CabinetModes, VirtualDisplaySettings.Load(path).Modes); // missing: defaults

            var settings = VirtualDisplaySettings.CreateDefault();
            settings.Add([new VirtualMode(2560, 240, 60)]);
            settings.Save(path);

            Assert.Equal(VirtualDisplaySettings.CabinetModes.Count + 1, VirtualDisplaySettings.Load(path).Modes.Count);
            Assert.False(File.Exists(path + ".tmp"));
            Assert.StartsWith("<?xml", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void EmbedsTheSignedDriverUnchanged()
    {
        // The catalog signs the INF's exact bytes: a line-ending conversion by git would break the installation.
        Assert.True(EmbeddedDriver.IsAvailable);
        foreach (var (name, hash) in EmbeddedDriver.Files)
            Assert.Equal(hash, EmbeddedDriver.Hash(name));
        Assert.StartsWith("MIT License", EmbeddedDriver.License);
        Assert.Contains("Copyright (c) 2024 Virtual Display", EmbeddedDriver.License);

        string folder = Path.Combine(Path.GetTempPath(), "SuperResScaler.Tests." + Path.GetRandomFileName());
        try
        {
            string inf = EmbeddedDriver.ExtractTo(folder);
            Assert.Equal(Path.Combine(folder, "MttVDD.inf"), inf);
            Assert.Equal(["LICENSE.txt", "mttvdd.cat", "MttVDD.dll", "MttVDD.inf"],
                Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));
            Assert.Equal(EmbeddedDriver.Files["MttVDD.inf"], Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(inf))));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    [Theory]
    [InlineData(640, 480, 149, 112)]   // 4:3
    [InlineData(1920, 1080, 199, 112)] // 16:9
    [InlineData(1280, 240, 149, 112)]  // super-resolution modes: 4:3, as on the tube
    [InlineData(1920, 240, 149, 112)]
    [InlineData(1024, 384, 149, 112)]
    [InlineData(2560, 1080, 200, 84)]  // 21:9 keeps its proportions
    [InlineData(0, 480, 0, 0)]
    public void FitsTheThumbnailInItsBox(int width, int height, int expectedWidth, int expectedHeight)
        => Assert.Equal(new Size(expectedWidth, expectedHeight), ScreenSnapshot.Fit(width, height, new Size(200, 112)));

    [Fact]
    public void ExplainsAVirtualDisplayStoppedByWindows()
    {
        Assert.Equal("Absent", new VirtualDisplayStatus(true, false, null).Describe());
        Assert.Contains("code 43", new VirtualDisplayStatus(true, true, null, VirtualDisplayStatus.StoppedProblem).Describe());
        Assert.Contains("Détruisez-le puis recréez-le", new VirtualDisplayStatus(true, true, null, 43).Describe());
        Assert.Contains("code 10", new VirtualDisplayStatus(true, true, null, 10).Describe());
        Assert.StartsWith("Créé, mais Windows ne l'affiche pas", new VirtualDisplayStatus(true, true, null).Describe());
    }

    private static MonitorInfo Display(int number, string name, string id)
        => new(number, $@"\\.\DISPLAY{number}", name, id, new RECT(0, 0, 640, 480), number == 1, IntPtr.Zero);

    [Fact]
    public void FindsTheVirtualDisplay()
    {
        var real = Display(1, "DELL U2415", @"\\?\DISPLAY#DEL40F4#5&1&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}");
        var virtualDisplay = Display(6, "VDD by MTT", @"\\?\DISPLAY#MTT1337#1&28a6823a&0&UID256#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}");
        List<MonitorInfo> all = [real, virtualDisplay];

        Assert.False(real.IsVirtual);
        Assert.True(virtualDisplay.IsVirtual);
        Assert.Same(virtualDisplay, MonitorInfo.Find(all, "virtuel"));
        Assert.Same(virtualDisplay, MonitorInfo.Find(all, "Virtual"));
        Assert.Same(virtualDisplay, MonitorInfo.Find(all, "6"));
        Assert.Null(MonitorInfo.Find([real], "virtuel"));
        Assert.Equal("VDD by MTT (virtuel) — 640x480 — Écran 6", virtualDisplay.ToString());
        Assert.Equal("DELL U2415 — 640x480 (principal) — Écran 1", real.ToString());
    }

    [Fact]
    public void ParsesTheVirtualDisplayCommands()
    {
        var add = CommandLineOptions.Parse(["--virtual-add", "1024x384@57.5"]);
        Assert.Equal("add", add.VirtualAction);
        Assert.Equal(new VirtualMode(1024, 384, 57.5), add.VirtualMode);
        Assert.False(add.IsHeadless);

        Assert.Equal("remove", CommandLineOptions.Parse(["--virtual-remove", "1280x240"]).VirtualAction);
        Assert.Equal("create", CommandLineOptions.Parse(["--virtual-create"]).VirtualAction);
        Assert.Equal("destroy", CommandLineOptions.Parse(["--virtual-destroy"]).VirtualAction);
        Assert.Equal("modes", CommandLineOptions.Parse(["--virtual-modes"]).VirtualAction);
        Assert.Equal(("create", @"C:\t\r.txt"), CommandLineOptions.Parse(["--virtual-display-admin", "create", @"C:\t\r.txt"]).VirtualAdmin);

        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--virtual-add", "12x34"]));
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--virtual-create", "--virtual-destroy"]));
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--virtual-add"]));

        var display = CommandLineOptions.Parse(["--display", "virtuel"]);
        Assert.True(display.DisplayOnly);
        Assert.Equal("virtuel", display.Display);
    }

    [Fact]
    public void FindsTheRealDisplaysWindowsSwitched()
    {
        const string Phl = @"\\?\DISPLAY#PHLC0D1#4&2b5f1b5&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        const string Dell = @"\\?\DISPLAY#DEL40F4#5&1&UID4354#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        const string Gone = @"\\?\DISPLAY#GSM5B7F#5&1&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        var vga = new DisplayMode(640, 480, 60, 32, false);
        var fullHd = new DisplayMode(1920, 1080, 60, 32, false);
        KeptMode phl = new(Phl, "PHL 243V7", vga), dell = new(Dell, "DELL U2415", fullHd), gone = new(Gone, "LG", vga);
        var now = new Dictionary<string, DisplayMode>(StringComparer.OrdinalIgnoreCase)
        {
            [Phl.ToUpperInvariant()] = fullHd,             // switched by Windows: set back
            [Dell] = fullHd with { Frequency = 59 },       // 59.94 Hz shown as 59: the same mode
        };

        var changed = Assert.Single(RealDisplayModes.FindChanged([phl, dell, gone], now));
        Assert.Equal(new ChangedMode(phl, fullHd), changed);

        Assert.Equal("Windows avait mis PHL 243V7 en 1920x1080@60 : remis en 640x480@60",
            RealDisplayModes.Report(changed, done: true, saved: true));
        Assert.EndsWith("mais Windows ne l'a pas enregistré", RealDisplayModes.Report(changed, done: true, saved: false));
        Assert.Equal("Windows avait mis PHL 243V7 en 1920x1080@60 : retour en 640x480@60 impossible (mode non supporté)",
            RealDisplayModes.Report(changed, done: false, saved: false, "mode non supporté"));
    }

    [Fact]
    public void ComparesModesAsWindowsShowsThem()
    {
        var mode = new DisplayMode(1280, 480, 60, 32, true);
        Assert.True(DisplayModes.SameMode(mode, mode with { BitsPerPixel = 16, Frequency = 59 }));
        Assert.False(DisplayModes.SameMode(mode, mode with { Interlaced = false }));
        Assert.False(DisplayModes.SameMode(mode, mode with { Frequency = 57 }));
        Assert.False(DisplayModes.SameMode(mode, null));
    }
}
