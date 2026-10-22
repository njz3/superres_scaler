using SuperResScaler.App;
using SuperResScaler.Config;
using SuperResScaler.Native;
using SuperResScaler.UI;

namespace SuperResScaler;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        CommandLineOptions cli;
        try
        {
            cli = CommandLineOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            ShowText(ex.Message + Environment.NewLine + Environment.NewLine + CommandLineOptions.Usage, error: true);
            return 1;
        }
        if (cli.Help)
        {
            ShowText(CommandLineOptions.Usage, error: false);
            return 0;
        }
        if (cli.ListModes)
        {
            ShowText(DescribeModes(cli.ListModesMonitor), error: false);
            return 0;
        }

        var store = SettingsStore.Load();
        Log.Initialize(store.Directory);
        Log.Info($"SuperResScaler {typeof(Program).Assembly.GetName().Version} started, arguments: {string.Join(' ', args)}");
        if (store.LoadError != null)
            Log.Warn(store.LoadError);

        if (cli.SelfTestDirectory != null)
            return SelfTest.Run(cli.SelfTestDirectory, cli.SelfTestOverlay);

        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        using var controller = new ScalerController(store);
        if (cli.IsHeadless)
        {
            using var context = new HeadlessContext(controller, cli);
            Application.Run(context);
            return context.ExitCode;
        }

        Application.Run(new MainForm(controller, cli.Tray || store.Settings.StartInTray));
        return 0;
    }

    /// <summary>The display modes of one monitor (1..N) or of all of them, current mode marked.</summary>
    private static string DescribeModes(int monitorIndex)
    {
        var text = new System.Text.StringBuilder();
        foreach (var monitor in MonitorInfo.GetAll().Where(m => monitorIndex <= 0 || m.Index == monitorIndex))
        {
            DisplayMode? current = DisplayModes.Current(monitor.DeviceName);
            text.AppendLine($"Écran {monitor} — actuel : {current?.ToString() ?? "?"}");
            foreach (var mode in DisplayModes.List(monitor.DeviceName).Where(m => m.BitsPerPixel == 32))
                text.AppendLine($"  {(mode == current ? "*" : " ")} {mode}");
        }
        return text.Length > 0 ? text.ToString() : $"Écran {monitorIndex} introuvable";
    }

    /// <summary>Prints to the parent console when started from one, otherwise shows a message box.</summary>
    private static void ShowText(string text, bool error)
    {
        // Redirected output first (as UTF-8): attaching to the console would bypass the redirection.
        if (Kernel32.IsOutputRedirected())
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        if (Kernel32.IsOutputRedirected() || Kernel32.AttachConsole(Kernel32.ATTACH_PARENT_PROCESS))
        {
            Console.WriteLine();
            Console.WriteLine(text);
            return;
        }
        MessageBox.Show(text, "SuperResScaler", MessageBoxButtons.OK, error ? MessageBoxIcon.Error : MessageBoxIcon.Information);
    }
}
