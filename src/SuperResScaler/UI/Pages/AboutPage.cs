// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Diagnostics;
using System.Reflection;
using SuperResScaler.App;
using SuperResScaler.VirtualDisplay;

namespace SuperResScaler.UI.Pages;

internal sealed class AboutPage : Page
{
    private const string RepositoryUrl = "https://github.com/njz3/superres_scaler";

    public AboutPage(ScalerController controller) : base(controller, "À propos")
    {
        string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        int plus = version.IndexOf('+');
        string display = plus > 0 && version.Length > plus + 8 ? $"{version[..plus]} ({version.Substring(plus + 1, 7)})" : version;
        string copyright = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

        AddCard(Theme.GlyphFullScreen, $"SuperResScaler {display}",
            "Affiche une fenêtre de jeu en plein écran par un shader, pour les modes super-résolution des écrans CRT (1280x240, 1920x240…).");
        AddCard(Theme.GlyphInfo, "Copyright", copyright);
        AddCard(Theme.GlyphInfo, "Code source", RepositoryUrl,
            UiFactory.Button("Ouvrir", (_, _) => Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true })));

        AddSection("Remerciements");
        AddCard(Theme.GlyphWindow, "Magpie", "Principe de la capture et de l'overlay, organisation de l'interface (github.com/Blinue/Magpie)");
        AddCard(Theme.GlyphAspect, "linuxloader", "Formats d'aspect : original, 4:3, 16:9, _stretch, _crop, recadrage en barillet");
        AddCard(Theme.GlyphEye, "BackForceFeeder", "Détection des jeux par nom de processus et titre de fenêtre");
        AddCard(Theme.GlyphColor, "Vortice.Windows", "Accès à Direct3D 11 depuis .NET (licence MIT)");
        AddCard(Theme.GlyphVirtualDisplay, $"Virtual Display Driver {EmbeddedDriver.Version}",
            "Pilote de l'écran virtuel, intégré à l'exe sans modification. Copyright (c) 2024 Virtual Display, licence MIT (github.com/VirtualDrivers/Virtual-Display-Driver)",
            UiFactory.Button("Licence", (_, _) => ShowDriverLicense()));
    }

    private void ShowDriverLicense()
    {
        string text;
        try { text = EmbeddedDriver.License; }
        catch (Exception ex) { text = ex.Message; }
        MessageBox.Show(FindForm(), text, "Virtual Display Driver — licence", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
