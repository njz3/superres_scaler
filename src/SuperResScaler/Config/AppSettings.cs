using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SuperResScaler.Native;

namespace SuperResScaler.Config;

public sealed class HotkeySettings
{
    /// <summary>Start scaling the foreground window, or stop the current scaling.</summary>
    public string ToggleScaling { get; set; } = "Alt+Shift+A";

    /// <summary>Switch to the next shader (saved in the active profile).</summary>
    public string NextShader { get; set; } = "Alt+Shift+S";

    /// <summary>Forget the black bars detected so far and detect again.</summary>
    public string ResetAutoCrop { get; set; } = "Alt+Shift+C";
}

public sealed class AppSettings
{
    /// <summary>Options used for windows that have no profile.</summary>
    public ScalingOptions Defaults { get; set; } = new();

    public List<Profile> Profiles { get; set; } = new();

    public HotkeySettings Hotkeys { get; set; } = new();

    /// <summary>Delay of the "scale the foreground window in N s" button.</summary>
    public int ScaleDelaySeconds { get; set; } = 3;

    /// <summary>Profiles marked AutoScale start automatically when their window gets the focus.</summary>
    public bool AutoScaleEnabled { get; set; } = true;

    public bool StartInTray { get; set; }

    /// <summary>Most specific matching profile (with a title filter first), or null.</summary>
    internal Profile? FindProfile(WindowInfo window)
        => Profiles.Where(p => p.Matches(window))
                   .OrderByDescending(p => !string.IsNullOrEmpty(p.TitleContains))
                   .FirstOrDefault();

    internal Profile? FindProfileByName(string name)
        => Profiles.FirstOrDefault(p => string.Equals(p.DisplayName, name, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Options for a window: its profile, or the defaults.</summary>
    internal ScalingOptions ResolveOptions(WindowInfo window) => FindProfile(window) ?? Defaults;
}

/// <summary>Loads and saves settings.json (next to the executable when writable, else in %AppData%).</summary>
public sealed class SettingsStore
{
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep "Alt+Shift+A" and accents readable
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private SettingsStore(string directory, AppSettings settings)
    {
        Directory = directory;
        Settings = settings;
    }

    public string Directory { get; }
    public string FilePath => Path.Combine(Directory, FileName);
    public AppSettings Settings { get; }

    /// <summary>Error encountered while reading the file (defaults are used in that case).</summary>
    public string? LoadError { get; private set; }

    public static SettingsStore Load()
    {
        string directory = ChooseDirectory();
        string path = Path.Combine(directory, FileName);
        AppSettings settings = new();
        string? error = null;
        if (File.Exists(path))
        {
            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                error = $"settings.json illisible, valeurs par défaut utilisées : {ex.Message}";
            }
        }
        var store = new SettingsStore(directory, settings) { LoadError = error };
        if (!File.Exists(path))
            store.Save();
        return store;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Settings, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }

    private static string ChooseDirectory()
    {
        string exeDirectory = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDirectory, FileName)) || IsWritable(exeDirectory))
            return exeDirectory;
        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SuperResScaler");
        System.IO.Directory.CreateDirectory(appData);
        return appData;
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            string probe = Path.Combine(directory, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
