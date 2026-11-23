// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SuperResScaler.VirtualDisplay;

/// <summary>
/// Virtual Display Driver, embedded in the exe as it was released (see VirtualDisplayDriver\SOURCE.txt).
/// Its catalog signs the exact bytes of the INF: the files are checked against their SHA-256 before
/// each installation, and written to a temporary folder that is deleted once Windows has copied them.
/// </summary>
internal static class EmbeddedDriver
{
    public const string Version = "25.7.23";
    public const string InfName = "MttVDD.inf";
    private const string ResourcePrefix = "VirtualDisplayDriver.";
    private const string LicenseName = "LICENSE.txt";

    /// <summary>The driver files and their SHA-256 (the ones of the release).</summary>
    public static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>
    {
        ["MttVDD.dll"] = "C9CA837F57A98FBD43BC416A7F535A95843626E7759EAF85CF0CD7CE334DBB05",
        ["MttVDD.inf"] = "550D211FE481E74DFE3F9D724ED78BE48B3A9113405965D683D9373E8D672F5D",
        ["mttvdd.cat"] = "08A0093FC9B2E32B287A6F8A77CA4DE0A31830D29FC33D2B13A918DC859468F6",
    };

    private static Assembly Assembly => typeof(EmbeddedDriver).Assembly;

    /// <summary>True when the exe was built with the driver.</summary>
    public static bool IsAvailable => Files.Keys.All(name => Assembly.GetManifestResourceInfo(ResourcePrefix + name) != null);

    /// <summary>The driver's MIT license notice.</summary>
    public static string License => Encoding.UTF8.GetString(Read(LicenseName));

    public static byte[] Read(string name)
    {
        using var stream = Assembly.GetManifestResourceStream(ResourcePrefix + name)
            ?? throw new FileNotFoundException($"Pilote d'écran virtuel absent de l'exe : {name}");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The SHA-256 of an embedded file, in upper-case hexadecimal.</summary>
    public static string Hash(string name) => Convert.ToHexString(SHA256.HashData(Read(name)));

    /// <summary>Writes the driver files to <paramref name="folder"/> after checking them; returns the INF's path.</summary>
    public static string ExtractTo(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var (name, expected) in Files)
        {
            byte[] bytes = Read(name);
            string actual = Convert.ToHexString(SHA256.HashData(bytes));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Pilote d'écran virtuel altéré : {name} (SHA-256 {actual})");
            File.WriteAllBytes(Path.Combine(folder, name), bytes);
        }
        File.WriteAllBytes(Path.Combine(folder, LicenseName), Read(LicenseName));
        return Path.Combine(folder, InfName);
    }
}
