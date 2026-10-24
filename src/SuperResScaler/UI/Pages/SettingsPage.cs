// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Diagnostics;
using SuperResScaler.App;
using SuperResScaler.Config;
using SuperResScaler.Rendering;

namespace SuperResScaler.UI.Pages;

/// <summary>Paramètres: start-up and tray, profile watching, hotkeys, files.</summary>
internal sealed class SettingsPage : Page
{
    private readonly ToggleSwitch _startInTray = new();
    private readonly ToggleSwitch _closeToTray = new();
    private readonly ToggleSwitch _watch = new();
    private readonly ToggleSwitch _watchWhileVisible = new();
    private readonly NumericUpDown _interval = UiFactory.Number(250, 10000, 80, 0, 250);
    private readonly KeyChips _toggleChips = new();
    private readonly KeyChips _shaderChips = new();
    private readonly KeyChips _autoCropChips = new();
    private readonly SettingCard _hotkeyStatus;

    public SettingsPage(ScalerController controller) : base(controller, "Paramètres")
    {
        AddSection("Démarrage et zone de notification");
        AddCard(Theme.GlyphPlay, "Démarrer dans la zone de notification", "Au lancement, la fenêtre reste cachée et les profils sont surveillés (équivaut à --tray)", _startInTray);
        AddCard(Theme.GlyphTray, "Le bouton Fermer réduit dans la zone de notification", "Pour quitter : clic droit sur l'icône, Quitter", _closeToTray);

        AddSection("Surveillance des profils",
            "Comme BackForceFeeder : les processus et les titres de fenêtres sont comparés aux profils, et la mise à l'échelle démarre quand l'un d'eux correspond.");
        AddCard(Theme.GlyphEye, "Surveiller les profils", "Pour les profils dont la mise à l'échelle automatique est activée", _watch);
        AddCard(Theme.GlyphWindow, "Surveiller aussi quand cette fenêtre est affichée", "Par défaut, la surveillance ne tourne que lorsque SuperResScaler est réduit", _watchWhileVisible);
        AddCard(Theme.GlyphTimer, "Intervalle entre deux recherches", "En millisecondes", _interval);

        AddSection("Raccourcis");
        AddCard(Theme.GlyphKeyboard, "Démarrer / arrêter la mise à l'échelle", "Fenêtre au premier plan",
            _toggleChips, UiFactory.IconButton(Theme.GlyphEdit, "Modifier", (_, _) => EditHotkey(h => h.ToggleScaling, (h, v) => h.ToggleScaling = v, _toggleChips)));
        AddCard(Theme.GlyphColor, "Shader suivant", "Pendant la mise à l'échelle ; enregistré dans le profil en cours",
            _shaderChips, UiFactory.IconButton(Theme.GlyphEdit, "Modifier", (_, _) => EditHotkey(h => h.NextShader, (h, v) => h.NextShader = v, _shaderChips)));
        AddCard(Theme.GlyphCrop, "Refaire le recadrage automatique", "Oublie les bandes noires détectées",
            _autoCropChips, UiFactory.IconButton(Theme.GlyphEdit, "Modifier", (_, _) => EditHotkey(h => h.ResetAutoCrop, (h, v) => h.ResetAutoCrop = v, _autoCropChips)));
        _hotkeyStatus = AddCard(Theme.GlyphInfo, "État des raccourcis", null);

        AddSection("Fichiers");
        AddCard(Theme.GlyphFolder, "Réglages et journal", controller.Store.FilePath,
            UiFactory.Button("Ouvrir le dossier", (_, _) => Open(controller.Store.Directory)));
        AddCard(Theme.GlyphColor, "Shaders", ShaderLibrary.Directory,
            UiFactory.Button("Ouvrir le dossier", (_, _) => Open(ShaderLibrary.Directory)));

        _startInTray.CheckedChanged += (_, _) => Save(s => s.StartInTray = _startInTray.Checked);
        _closeToTray.CheckedChanged += (_, _) => Save(s => s.CloseToTray = _closeToTray.Checked);
        _watch.CheckedChanged += (_, _) => Save(s => s.AutoScaleEnabled = _watch.Checked, watcher: true);
        _watchWhileVisible.CheckedChanged += (_, _) => Save(s => s.WatchWhileVisible = _watchWhileVisible.Checked, watcher: true);
        _interval.ValueChanged += (_, _) => Save(s => s.WatchIntervalMs = (int)_interval.Value, watcher: true);
    }

    /// <summary>Raised when a setting that decides whether the watcher runs changed.</summary>
    public event Action? WatchingSettingsChanged;

    public override void OnShown()
    {
        Loading = true;
        var s = Controller.Settings;
        _startInTray.Checked = s.StartInTray;
        _closeToTray.Checked = s.CloseToTray;
        _watch.Checked = s.AutoScaleEnabled;
        _watchWhileVisible.Checked = s.WatchWhileVisible;
        _interval.Value = Math.Clamp(s.WatchIntervalMs, 250, 10000);
        _toggleChips.Hotkey = s.Hotkeys.ToggleScaling;
        _shaderChips.Hotkey = s.Hotkeys.NextShader;
        _autoCropChips.Hotkey = s.Hotkeys.ResetAutoCrop;
        Loading = false;
    }

    private void Save(Action<AppSettings> apply, bool watcher = false)
    {
        if (Loading)
            return;
        apply(Controller.Settings);
        Controller.Store.Save();
        if (watcher)
        {
            Controller.Watcher.Restart();
            WatchingSettingsChanged?.Invoke();
        }
    }

    private void EditHotkey(Func<HotkeySettings, string> get, Action<HotkeySettings, string> set, KeyChips chips)
    {
        using var dialog = new HotkeyDialog(get(Controller.Settings.Hotkeys));
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            return;
        set(Controller.Settings.Hotkeys, dialog.Result);
        Controller.Store.Save();
        chips.Hotkey = dialog.Result;
        var errors = Controller.RegisterHotkeys();
        _hotkeyStatus.Description = errors.Count == 0 ? "Tous les raccourcis sont actifs" : string.Join(" ; ", errors);
        _hotkeyStatus.DescriptionColor = errors.Count == 0 ? Theme.Success : Color.Firebrick;
    }

    private static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn($"Open {path}: {ex.Message}"); }
    }
}
