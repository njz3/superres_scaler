using System.Text;
using System.Text.RegularExpressions;

namespace SuperResScaler.Rendering;

/// <summary>
/// Pixel shaders are plain .hlsl files in the "Shaders" folder next to the executable.
/// Drop a new file there and it shows up in the list (see Shaders/README.md for the contract).
/// </summary>
internal static partial class ShaderLibrary
{
    public const string DefaultShader = "Area";
    public const string Extension = ".hlsl";

    public static string Directory { get; set; } = Path.Combine(AppContext.BaseDirectory, "Shaders");

    /// <summary>Shader names (file names without extension), sorted.</summary>
    public static List<string> List()
    {
        if (!System.IO.Directory.Exists(Directory))
            return new List<string>();
        return System.IO.Directory.EnumerateFiles(Directory, "*" + Extension)
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? FindPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        string path = Path.Combine(Directory, name + Extension);
        return File.Exists(path) ? path : null;
    }

    /// <summary>The shader that follows <paramref name="current"/> in the list (wraps around).</summary>
    public static string Next(string current)
    {
        var names = List();
        if (names.Count == 0)
            return current;
        int index = names.FindIndex(n => n.Equals(current, StringComparison.OrdinalIgnoreCase));
        return names[(index + 1) % names.Count];
    }

    /// <summary>Reads a shader and inlines its #include "file" directives (relative to the shader).</summary>
    public static string LoadSource(string path) => LoadSource(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static string LoadSource(string path, HashSet<string> visited)
    {
        string fullPath = Path.GetFullPath(path);
        if (!visited.Add(fullPath))
            return string.Empty; // already included (acts like #pragma once)

        var sb = new StringBuilder();
        sb.Append("#line 1 \"").Append(Path.GetFileName(fullPath)).AppendLine("\"");
        int lineNumber = 0;
        foreach (string line in File.ReadLines(fullPath))
        {
            lineNumber++;
            var match = IncludeRegex().Match(line);
            if (match.Success)
            {
                string includePath = Path.Combine(Path.GetDirectoryName(fullPath)!, match.Groups[1].Value);
                sb.Append(LoadSource(includePath, visited));
                sb.Append("#line ").Append(lineNumber + 1).Append(" \"").Append(Path.GetFileName(fullPath)).AppendLine("\"");
            }
            else
            {
                sb.AppendLine(line);
            }
        }
        return sb.ToString();
    }

    [GeneratedRegex("^\\s*#\\s*include\\s+\"([^\"]+)\"")]
    private static partial Regex IncludeRegex();
}
