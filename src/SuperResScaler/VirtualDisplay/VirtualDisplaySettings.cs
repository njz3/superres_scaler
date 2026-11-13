// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SuperResScaler.VirtualDisplay;

/// <summary>A mode of the virtual display: "1280x240@60", "1024x384@57.5" (frequency in Hz, decimals allowed).</summary>
internal readonly partial record struct VirtualMode(int Width, int Height, double Frequency)
{
    public const double DefaultFrequency = 60;

    /// <summary>The frequency as written in vdd_settings.xml and shown: "60", "57.5".</summary>
    public string FrequencyText => Frequency.ToString("0.###", CultureInfo.InvariantCulture);

    public override string ToString() => $"{Width}x{Height}@{FrequencyText}";

    /// <summary>Same size and frequency (to the thousandth of a hertz).</summary>
    public bool SameAs(VirtualMode other)
        => Width == other.Width && Height == other.Height && Math.Abs(Frequency - other.Frequency) < 0.0005;

    /// <summary>Parses "1280x240", "1280x240@60", "1024x384@57.5", "1024 x 384 @ 57,5 Hz" (60 Hz when absent).</summary>
    public static bool TryParse(string? text, out VirtualMode mode)
    {
        mode = default;
        var match = text == null ? Match.Empty : ModeRegex().Match(text.Trim());
        if (!match.Success)
            return false;
        int width = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int height = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        double frequency = match.Groups[3].Success
            ? double.Parse(match.Groups[3].Value.Replace(',', '.'), CultureInfo.InvariantCulture)
            : DefaultFrequency;
        if (!IsValid(width, height, frequency))
            return false;
        mode = new VirtualMode(width, height, Math.Round(frequency, 3));
        return true;
    }

    public static bool IsValid(int width, int height, double frequency)
        => width is >= 64 and <= 16384 && height is >= 64 and <= 16384 && frequency is >= 1 and <= 1000;

    [GeneratedRegex(@"^(\d+)\s*[x×]\s*(\d+)\s*(?:@\s*(\d+(?:[.,]\d+)?)\s*(?:hz)?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ModeRegex();
}

/// <summary>
/// vdd_settings.xml of Virtual Display Driver: the modes offered by the virtual display. Only the
/// resolutions are rewritten; the other settings of the file are kept as they are.
/// </summary>
internal sealed class VirtualDisplaySettings
{
    /// <summary>The modes of the cabinets' C:\Racecab\Utils\SetCRT*.bat, the 24 kHz ones at 57.5 Hz only.</summary>
    public static readonly IReadOnlyList<VirtualMode> CabinetModes =
    [
        new(640, 480, 60), new(320, 240, 60), new(1280, 240, 60), new(1920, 240, 60), new(1280, 480, 60),
        new(512, 384, 57.5), new(1024, 384, 57.5), new(1024, 768, 60), new(1280, 960, 60), new(1920, 1080, 60),
    ];

    private readonly XDocument _document;

    private VirtualDisplaySettings(XDocument document)
    {
        _document = document;
        Modes = ReadModes(document);
        GlobalRefreshRates = document.Root?.Element("global")?.Elements("g_refresh_rate")
            .Select(e => double.TryParse(e.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) ? rate : 0)
            .Where(rate => rate > 0)
            .ToList() ?? [];
    }

    /// <summary>The modes of the file, in its order. The first one is the mode a new virtual display starts in.</summary>
    public List<VirtualMode> Modes { get; }

    /// <summary>Frequencies that the driver adds to every size (none in the files SuperResScaler writes).</summary>
    public IReadOnlyList<double> GlobalRefreshRates { get; }

    /// <summary>Every mode the driver offers: each mode, plus each size at each global frequency.</summary>
    public IEnumerable<VirtualMode> EffectiveModes()
    {
        var all = new List<VirtualMode>(Modes);
        foreach (double rate in GlobalRefreshRates)
            foreach (var (width, height) in Modes.Select(m => (m.Width, m.Height)).Distinct())
                all.Add(new VirtualMode(width, height, rate));
        return all.Where((mode, index) => !all.Take(index).Any(mode.SameAs));
    }

    /// <summary>Adds the modes that are not there yet; returns how many were added.</summary>
    public int Add(IEnumerable<VirtualMode> modes)
    {
        int added = 0;
        foreach (var mode in modes)
        {
            if (Modes.Any(mode.SameAs))
                continue;
            Modes.Add(mode);
            added++;
        }
        return added;
    }

    /// <summary>Removes a mode; false if it was not there.</summary>
    public bool Remove(VirtualMode mode) => Modes.RemoveAll(mode.SameAs) > 0;

    public static VirtualDisplaySettings Parse(string xml) => new(XDocument.Parse(xml, LoadOptions.PreserveWhitespace));

    /// <summary>The settings of the file, or the default ones when it does not exist yet.</summary>
    public static VirtualDisplaySettings Load(string path)
        => File.Exists(path) ? Parse(File.ReadAllText(path)) : CreateDefault();

    /// <summary>One virtual display with the cabinet modes, no global frequency, no logging.</summary>
    public static VirtualDisplaySettings CreateDefault()
    {
        var settings = Parse(DefaultXml);
        settings.Add(CabinetModes);
        return settings;
    }

    public string ToXml()
    {
        var root = _document.Root ?? throw new InvalidOperationException("vdd_settings.xml sans élément racine");
        var resolutions = root.Element("resolutions");
        if (resolutions == null)
        {
            resolutions = new XElement("resolutions");
            root.Add(resolutions);
        }
        resolutions.RemoveNodes();
        foreach (var mode in Modes)
        {
            resolutions.Add(new XText("\n        "));
            resolutions.Add(new XElement("resolution",
                new XElement("width", mode.Width.ToString(CultureInfo.InvariantCulture)),
                new XElement("height", mode.Height.ToString(CultureInfo.InvariantCulture)),
                new XElement("refresh_rate", mode.FrequencyText)));
        }
        resolutions.Add(new XText("\n    "));

        var writerSettings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false, Indent = false };
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, writerSettings))
            _document.Save(writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Writes the file (and its folder), replacing it at once.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, ToXml(), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    private static List<VirtualMode> ReadModes(XDocument document)
    {
        var modes = new List<VirtualMode>();
        foreach (var resolution in document.Root?.Element("resolutions")?.Elements("resolution") ?? [])
        {
            if (!int.TryParse(resolution.Element("width")?.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
                || !int.TryParse(resolution.Element("height")?.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
                continue;
            double frequency = double.TryParse(resolution.Element("refresh_rate")?.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double rate)
                ? rate
                : 30; // the driver's default when the frequency is missing
            var mode = new VirtualMode(width, height, Math.Round(frequency, 3));
            if (!modes.Any(mode.SameAs))
                modes.Add(mode);
        }
        return modes;
    }

    private const string DefaultXml = """
        <?xml version='1.0' encoding='utf-8'?>
        <!-- Virtual Display Driver settings written by SuperResScaler: a test display with the cabinet modes. -->
        <vdd_settings>
            <monitors>
                <count>1</count>
            </monitors>
            <gpu>
                <friendlyname>default</friendlyname>
            </gpu>
            <global>
                <!-- No global frequency: the driver would add each of them to every size. -->
            </global>
            <resolutions>
            </resolutions>
            <options>
                <CustomEdid>false</CustomEdid>
                <PreventSpoof>false</PreventSpoof>
                <EdidCeaOverride>false</EdidCeaOverride>
                <HardwareCursor>true</HardwareCursor>
                <SDR10bit>false</SDR10bit>
                <HDRPlus>false</HDRPlus>
                <logging>false</logging>
                <debuglogging>false</debuglogging>
            </options>
        </vdd_settings>
        """;
}
