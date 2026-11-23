// Copyright (c) Benjamin Maurin (njz3) 2026

using SuperResScaler.App;
using SuperResScaler.Config;
using SuperResScaler.Native;
using SuperResScaler.UI;
using SuperResScaler.VirtualDisplay;

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
        // The runtime too: built for .NET 8, the exe runs on a newer one when .NET 8 is missing (RollForward).
        Log.Info($"SuperResScaler {typeof(Program).Assembly.GetName().Version} started on .NET {Environment.Version}, "
                 + $"Windows {Environment.OSVersion.Version}, arguments: {string.Join(' ', args)}");
        if (store.LoadError != null)
            Log.Warn(store.LoadError);

        if (cli.VirtualAdmin is var (adminAction, resultFile))
            return VirtualDisplayManager.RunAdmin(adminAction, resultFile);
        if (cli.VirtualAction != null)
            return RunVirtualAction(cli);

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

    /// <summary>--virtual-create, -destroy, -modes, -add, -remove: prints the result, 0 on success.</summary>
    private static int RunVirtualAction(CommandLineOptions cli)
    {
        try
        {
            string text = cli.VirtualAction switch
            {
                "create" => VirtualDisplayManager.Create(),
                "destroy" => VirtualDisplayManager.Destroy(),
                "add" => VirtualDisplayManager.AddModes([cli.VirtualMode]),
                "remove" => VirtualDisplayManager.RemoveMode(cli.VirtualMode),
                _ => VirtualDisplayManager.DescribeModes(),
            };
            Log.Info($"Virtual display {cli.VirtualAction}: {text}");
            ShowText(text, error: false);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"Virtual display {cli.VirtualAction}: {ex.Message}");
            ShowText(ex.Message, error: true);
            return 1;
        }
    }

    /// <summary>The display modes of one display (by its number) or of all of them, current mode marked.</summary>
    private static string DescribeModes(int monitorNumber)
    {
        var text = new System.Text.StringBuilder();
        foreach (var monitor in MonitorInfo.GetAll().Where(m => monitorNumber <= 0 || m.Number == monitorNumber))
        {
            DisplayMode? current = DisplayModes.Current(monitor.DeviceName);
            text.AppendLine($"{monitor} — actuel : {current?.ToString() ?? "?"}");
            foreach (var mode in DisplayModes.List(monitor.DeviceName).Where(m => m.BitsPerPixel == 32))
                text.AppendLine($"  {(mode == current ? "*" : " ")} {mode}");
        }
        return text.Length > 0 ? text.ToString() : $"Écran {monitorNumber} introuvable";
    }

    /// <summary>Prints to the parent console when started from one, otherwise shows a message box.</summary>
    private static void ShowText(string text, bool error)
    {
        // Redirected output first: attaching to the console would bypass the redirection. Written by a
        // writer of its own: setting Console.OutputEncoding sets the console's code page, which .NET 8
        // refuses with an IOException when there is no console (a redirected WinExe).
        var kind = Kernel32.GetStandardOutput();
        if (kind != Kernel32.StandardOutput.None)
        {
            var stream = Console.OpenStandardOutput(); // before looking at the parent's console
            int? console = kind == Kernel32.StandardOutput.Pipe ? Kernel32.ParentConsoleCodePage() : null;
            using var output = new StreamWriter(stream, RedirectedEncoding(kind, console, (int)Kernel32.GetACP()));
            output.WriteLine();
            output.WriteLine(text);
            return;
        }
        if (Kernel32.AttachConsole(Kernel32.ATTACH_PARENT_PROCESS))
        {
            Console.WriteLine();
            Console.WriteLine(text);
            return;
        }
        MessageBox.Show(text, "SuperResScaler", MessageBoxButtons.OK, error ? MessageBoxIcon.Error : MessageBoxIcon.Information);
    }

    /// <summary>
    /// The encoding a redirected output is read with. A new file: UTF-8 with its BOM, which Notepad and
    /// Windows PowerShell's Get-Content recognize (without it, they read « Écran » as « Ã‰cran »). The end
    /// of a file: UTF-8 without a BOM in the middle. A pipe: the reader decodes with its console's code
    /// page (<paramref name="consoleCodePage"/>), or the ANSI one when it has no console, as Windows
    /// PowerShell over WinRM does.
    /// </summary>
    internal static System.Text.Encoding RedirectedEncoding(Kernel32.StandardOutput kind, int? consoleCodePage, int ansiCodePage)
    {
        if (kind == Kernel32.StandardOutput.NewFile)
            return new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        if (kind != Kernel32.StandardOutput.Pipe)
            return new System.Text.UTF8Encoding(false);
        int codePage = consoleCodePage ?? ansiCodePage;
        if (codePage == 65001)
            return new System.Text.UTF8Encoding(false);
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            var encoding = (System.Text.Encoding)System.Text.Encoding.GetEncoding(codePage).Clone();
            encoding.EncoderFallback = new System.Text.EncoderReplacementFallback("?");
            return encoding;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new System.Text.UTF8Encoding(false);
        }
    }
}
