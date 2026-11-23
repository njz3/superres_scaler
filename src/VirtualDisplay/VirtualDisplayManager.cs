// Copyright (c) Benjamin Maurin (njz3) 2026

using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using SuperResScaler.Native;

namespace SuperResScaler.VirtualDisplay;

/// <summary>What is known about the virtual display.</summary>
internal sealed record VirtualDisplayStatus(bool DriverAvailable, bool DeviceInstalled, MonitorInfo? Monitor, int Problem = 0)
{
    /// <summary>Device Manager's code 43: Windows stopped the device after its driver crashed too often.</summary>
    public const int StoppedProblem = 43;

    public string Describe()
    {
        if (Monitor != null)
            return $"Actif : {Monitor}";
        if (DeviceInstalled && Problem == StoppedProblem)
            return "Arrêté par Windows (code 43) : son pilote a été relancé trop souvent (relectures des modes rapprochées). "
                   + "Détruisez-le puis recréez-le, ou redémarrez le PC";
        if (DeviceInstalled && Problem != 0)
            return $"Créé, mais en erreur (code {Problem} du Gestionnaire de périphériques) : détruisez-le et recréez-le";
        if (DeviceInstalled)
            return "Créé, mais Windows ne l'affiche pas : activez-le dans Paramètres > Affichage, ou détruisez-le et recréez-le";
        return DriverAvailable ? "Absent" : "Absent, et cet exe a été compilé sans le pilote d'écran virtuel";
    }
}

/// <summary>
/// The virtual test display, made by Virtual Display Driver (MIT, embedded in the exe, see
/// <see cref="EmbeddedDriver"/>): a monitor with no hardware whose modes are chosen in
/// vdd_settings.xml. Creating and destroying it need administrator rights (SuperResScaler is
/// started again elevated for that); changing its modes does not.
/// </summary>
internal static class VirtualDisplayManager
{
    public const string HardwareId = @"Root\MttVDD";

    /// <summary>Folder where the driver reads vdd_settings.xml (its default).</summary>
    public const string SettingsFolder = @"C:\VirtualDisplayDriver";

    /// <summary>Hidden argument of the elevated helper: --virtual-display-admin create|destroy result-file.</summary>
    public const string AdminArgument = "--virtual-display-admin";

    private const string PipeName = "MTTVirtualDisplayPipe";

    public static string SettingsPath => Path.Combine(SettingsFolder, "vdd_settings.xml");

    public static VirtualDisplayStatus GetStatus()
    {
        bool installed;
        int problem = 0;
        try
        {
            var problems = SetupApi.FindProblems(SetupApi.DisplayClass, HardwareId);
            installed = problems.Count > 0;
            problem = problems.FirstOrDefault(p => p != 0);
        }
        catch (Exception ex)
        {
            Log.Warn($"Virtual display lookup: {ex.Message}");
            installed = false;
        }
        var monitor = MonitorInfo.GetAll().FirstOrDefault(m => m.IsVirtual);
        return new VirtualDisplayStatus(EmbeddedDriver.IsAvailable, installed || monitor != null, monitor, problem);
    }

    public static VirtualDisplaySettings LoadSettings() => VirtualDisplaySettings.Load(SettingsPath);

    /// <summary>
    /// Creates the virtual display (UAC prompt). Its modes are those of vdd_settings.xml, written first if missing.
    /// The real displays keep their modes (see <see cref="RealDisplayModes"/>).
    /// </summary>
    public static string Create()
    {
        if (GetStatus() is { DeviceInstalled: true } status)
            return $"L'écran virtuel existe déjà ({status.Describe()})";
        if (!EmbeddedDriver.IsAvailable)
            throw new InvalidOperationException("Cet exe a été compilé sans le pilote d'écran virtuel");
        if (!File.Exists(SettingsPath))
            VirtualDisplaySettings.CreateDefault().Save(SettingsPath);
        var kept = RealDisplayModes.Note();
        string message = RunElevated("create");
        return message + RealDisplayModes.SetBack(kept, virtualPresent: true);
    }

    /// <summary>
    /// Destroys the virtual display (UAC prompt). vdd_settings.xml stays, for the next creation.
    /// The real displays keep their modes (see <see cref="RealDisplayModes"/>).
    /// </summary>
    public static string Destroy()
    {
        if (!GetStatus().DeviceInstalled)
            return "Il n'y a pas d'écran virtuel";
        var kept = RealDisplayModes.Note();
        string message = RunElevated("destroy");
        return message + RealDisplayModes.SetBack(kept, virtualPresent: false);
    }

    /// <summary>Adds modes and has the driver read them again. Returns what was done.</summary>
    public static string AddModes(IEnumerable<VirtualMode> modes)
    {
        var settings = LoadSettings();
        var list = modes.ToList();
        int added = settings.Add(list);
        if (added == 0)
            return list.Count == 1 ? $"Le mode {list[0]} existe déjà" : "Ces modes existent déjà";
        settings.Save(SettingsPath);
        string what = list.Count == 1 ? $"Mode {list[0]} ajouté" : $"{added} mode(s) ajouté(s)";
        return $"{what}{Reload()}";
    }

    public static string RemoveMode(VirtualMode mode)
    {
        var settings = LoadSettings();
        if (!settings.Remove(mode))
            return $"Le mode {mode} n'existe pas";
        if (settings.Modes.Count == 0)
            throw new InvalidOperationException("L'écran virtuel doit garder au moins un mode");
        settings.Save(SettingsPath);
        return $"Mode {mode} retiré{Reload()}";
    }

    /// <summary>The modes of the file and, when the display exists, those Windows offers for it.</summary>
    public static string DescribeModes()
    {
        var text = new StringBuilder();
        var settings = LoadSettings();
        text.AppendLine(File.Exists(SettingsPath) ? $"Modes de {SettingsPath} :" : "Modes par défaut (fichier pas encore écrit) :");
        foreach (var mode in settings.Modes)
            text.AppendLine($"  {mode}");
        if (settings.GlobalRefreshRates.Count > 0)
            text.AppendLine($"  + fréquences globales, ajoutées à chaque taille : {string.Join(", ", settings.GlobalRefreshRates)} Hz");

        var status = GetStatus();
        text.AppendLine().AppendLine($"Écran virtuel : {status.Describe()}");
        if (status.Monitor is { } monitor)
        {
            DisplayMode? current = DisplayModes.Current(monitor.DeviceName);
            foreach (var mode in DisplayModes.List(monitor.DeviceName).Where(m => m.BitsPerPixel == 32))
                text.AppendLine($"  {(mode == current ? "*" : " ")} {mode}");
            text.AppendLine("Windows arrondit les fréquences : 57.5 Hz y apparaît en 58.");
        }
        return text.ToString();
    }

    /// <summary>
    /// Asks the running driver to read vdd_settings.xml again. Returns a sentence end for the caller's message.
    /// The driver reloads by stopping its host process, which Windows counts as a crash and restarts:
    /// the display comes back as another \\.\DISPLAYn, and a capture of it ends. Measured on the
    /// development PC: after five reloads within about two minutes, Windows stopped the device (code 43).
    /// </summary>
    public static string Reload()
    {
        if (!GetStatus().DeviceInstalled)
            return " ; pris en compte à la création de l'écran virtuel";
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            pipe.Connect(3000);
            byte[] command = Encoding.Unicode.GetBytes("RELOAD_DRIVER");
            pipe.Write(command, 0, command.Length);
            pipe.Flush();
            return " ; l'écran virtuel a relu ses modes";
        }
        catch (Exception ex)
        {
            Log.Warn($"Virtual display reload: {ex.Message}");
            return $" ; le pilote n'a pas pu les relire ({ex.Message}) : détruisez puis recréez l'écran virtuel";
        }
    }

    /// <summary>Starts SuperResScaler again elevated for <paramref name="action"/> and returns its result.</summary>
    private static string RunElevated(string action)
    {
        string resultFile = Path.Combine(Path.GetTempPath(), $"SuperResScaler-virtual-{Guid.NewGuid():N}.txt");
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Chemin de SuperResScaler inconnu"))
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add(AdminArgument);
        start.ArgumentList.Add(action);
        start.ArgumentList.Add(resultFile);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Démarrage impossible");
            if (!process.WaitForExit(120_000))
                throw new TimeoutException("Pas de réponse après 2 minutes");
            string message = File.Exists(resultFile) ? File.ReadAllText(resultFile).Trim() : "";
            if (process.ExitCode != 0)
                throw new InvalidOperationException(message.Length > 0 ? message : $"Échec (code {process.ExitCode})");
            return message;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Droits d'administrateur refusés : rien n'a changé");
        }
        finally
        {
            try { File.Delete(resultFile); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>The elevated helper: creates or destroys the device, writes a sentence to the result file.</summary>
    public static int RunAdmin(string action, string resultFile)
    {
        string message;
        int code;
        try
        {
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                throw new UnauthorizedAccessException("Droits d'administrateur nécessaires");
            switch (action)
            {
                case "create":
                    message = CreateDevice();
                    break;
                case "destroy":
                    int removed = SetupApi.RemoveDevices(SetupApi.DisplayClass, HardwareId);
                    message = removed > 0 ? "Écran virtuel détruit" : "Il n'y avait pas d'écran virtuel";
                    break;
                default:
                    throw new ArgumentException($"Action inconnue : {action}");
            }
            code = 0;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            code = 1;
        }
        Log.Info($"Virtual display {action} (elevated): {message}");
        try { File.WriteAllText(resultFile, message); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return code;
    }

    /// <summary>
    /// Elevated: writes the embedded driver to a new temporary folder (checked), creates the device
    /// with it, then deletes the folder: Windows keeps its own copy in its driver store.
    /// </summary>
    private static string CreateDevice()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"SuperResScaler-VirtualDisplayDriver-{Guid.NewGuid():N}");
        try
        {
            string inf = EmbeddedDriver.ExtractTo(folder);
            bool reboot = SetupApi.CreateRootDevice(SetupApi.DisplayClass, "Display", HardwareId, inf);
            return reboot ? "Écran virtuel créé ; Windows demande un redémarrage" : "Écran virtuel créé";
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Delete {folder}: {ex.Message}"); }
        }
    }
}
