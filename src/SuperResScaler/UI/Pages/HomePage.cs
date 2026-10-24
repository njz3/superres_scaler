using SuperResScaler.App;
using SuperResScaler.Config;
using SuperResScaler.Native;

namespace SuperResScaler.UI.Pages;

/// <summary>Accueil: state, profile watching, activation (hotkey, delayed, chosen window), recent activity.</summary>
internal sealed class HomePage : Page
{
    private readonly SettingCard _stateCard;
    private readonly Button _stopButton;
    private readonly SettingCard _watchCard;
    private readonly ToggleSwitch _watchToggle = new();
    private readonly KeyChips _toggleChips = new();
    private readonly NumericUpDown _delay = UiFactory.Number(1, 30, 60);
    private readonly ComboBox _windows = UiFactory.DropDown(320);
    private readonly ListBox _history = new()
    {
        Font = Theme.Caption,
        BorderStyle = BorderStyle.None,
        Height = Theme.S(170),
        IntegralHeight = false,
        SelectionMode = SelectionMode.None,
        BackColor = Theme.CardBackground,
    };

    public HomePage(ScalerController controller) : base(controller, "Accueil")
    {
        AddSection("État");
        _stopButton = UiFactory.Button("Arrêter", (_, _) => Controller.StopScaling());
        _stateCard = AddCard(Theme.GlyphFullScreen, "Mise à l'échelle", null, _stopButton);
        _watchToggle.CheckedChanged += (_, _) =>
        {
            if (Loading)
                return;
            Controller.Settings.AutoScaleEnabled = _watchToggle.Checked;
            Controller.Store.Save();
            Controller.Watcher.Restart();
            OnStateChanged();
        };
        _watchCard = AddCard(Theme.GlyphEye, "Surveillance des profils", null, _watchToggle);

        AddSection("Activation", "Appuyez sur le raccourci pour mettre à l'échelle la fenêtre au premier plan, ou pour arrêter.");
        AddCard(Theme.GlyphKeyboard, "Raccourci de mise à l'échelle", "Fenêtre au premier plan, avec son profil (ou les réglages par défaut)",
            _toggleChips, UiFactory.IconButton(Theme.GlyphEdit, "Modifier le raccourci", (_, _) => EditHotkey()));
        _delay.ValueChanged += (_, _) =>
        {
            if (Loading)
                return;
            Controller.Settings.ScaleDelaySeconds = (int)_delay.Value;
            Controller.Store.Save();
        };
        AddCard(Theme.GlyphTimer, "Mise à l'échelle différée", "Met à l'échelle la fenêtre au premier plan après ce délai : lancez, puis cliquez sur le jeu",
            _delay, UiFactory.Text("s"), UiFactory.Button("Démarrer", (_, _) => Controller.ScaleForegroundAfterDelay((int)_delay.Value)));
        _windows.DropDown += (_, _) => RefreshWindows();
        _windows.DropDownWidth = Theme.S(520);
        AddCard(Theme.GlyphWindow, "Fenêtre précise", "Choisissez une fenêtre ouverte ; son profil s'applique s'il existe",
            _windows, UiFactory.AccentButton("Mettre à l'échelle", (_, _) => ScaleChosenWindow()));

        AddSection("Activité récente");
        var historyCard = AddCard(Theme.GlyphHistory, "Journal", "Détections, démarrages et arrêts (le détail est dans SuperResScaler.log)");
        historyCard.AddDetail(_history);
        _history.Dock = DockStyle.Fill;
    }

    public override void OnShown()
    {
        Loading = true;
        _toggleChips.Hotkey = Controller.Settings.Hotkeys.ToggleScaling;
        _delay.Value = Math.Clamp(Controller.Settings.ScaleDelaySeconds, 1, 30);
        _watchToggle.Checked = Controller.Settings.AutoScaleEnabled;
        Loading = false;
        RefreshWindows();
        RefreshHistory();
        OnStateChanged();
    }

    public override void OnStateChanged()
    {
        var settings = Controller.Settings;
        if (Controller.ScaledWindow is { } window)
        {
            string profile = Controller.ActiveOptions is Profile p ? $"profil {p.DisplayName}" : "réglages par défaut";
            _stateCard.Description = $"Active : « {window.Title} » ({window.ProcessName}), {profile}";
            _stateCard.DescriptionColor = Theme.Success;
        }
        else
        {
            _stateCard.Description = "Inactive";
            _stateCard.DescriptionColor = Theme.SecondaryText;
        }
        _stopButton.Enabled = Controller.IsScaling;

        int automatic = settings.Profiles.Count(p => p.AutoScale);
        string state = !settings.AutoScaleEnabled ? "Désactivée"
            : Controller.Watcher.Active ? "En cours"
            : settings.WatchWhileVisible ? "En pause" : "Démarre quand SuperResScaler est réduit dans la zone de notification";
        string pending = Controller.Watcher.Pending is { } found ? $" — « {found.Window.Title} » détectée (profil {found.Profile.DisplayName})" : "";
        _watchCard.Description = $"Démarre la mise à l'échelle dès qu'une fenêtre correspond à un profil automatique ({automatic} profil(s)). {state}{pending}";
        RefreshHistory();
    }

    private void RefreshHistory()
    {
        _history.BeginUpdate();
        _history.Items.Clear();
        foreach (var (time, text) in Controller.History.Reverse().Take(30))
            _history.Items.Add($"{time:HH:mm:ss}  {text}");
        _history.EndUpdate();
    }

    private void RefreshWindows()
    {
        IntPtr selected = (_windows.SelectedItem as WindowInfo)?.Handle ?? IntPtr.Zero;
        _windows.BeginUpdate();
        _windows.Items.Clear();
        foreach (var window in WindowInfo.EnumerateCandidates())
        {
            int index = _windows.Items.Add(window);
            if (window.Handle == selected)
                _windows.SelectedIndex = index;
        }
        _windows.EndUpdate();
    }

    private void ScaleChosenWindow()
    {
        if (_windows.SelectedItem is WindowInfo window)
            Controller.StartScaling(window.Handle);
        else
            Controller.Report("Choisissez d'abord une fenêtre");
    }

    private void EditHotkey()
    {
        using var dialog = new HotkeyDialog(Controller.Settings.Hotkeys.ToggleScaling);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            return;
        Controller.Settings.Hotkeys.ToggleScaling = dialog.Result;
        Controller.Store.Save();
        foreach (string error in Controller.RegisterHotkeys())
            Controller.Report(error);
        _toggleChips.Hotkey = dialog.Result;
    }
}
