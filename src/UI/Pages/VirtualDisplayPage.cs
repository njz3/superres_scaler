// Copyright (c) Benjamin Maurin (njz3) 2026

using SuperResScaler.App;
using SuperResScaler.Native;
using SuperResScaler.VirtualDisplay;

namespace SuperResScaler.UI.Pages;

/// <summary>Écran virtuel: creates and destroys the virtual test display, edits its modes, scales it to a real display.</summary>
internal sealed class VirtualDisplayPage : Page
{
    private readonly SettingCard _stateCard;
    private readonly Button _create;
    private readonly Button _destroy;
    private readonly Button _scale;
    private readonly ComboBox _currentMode = UiFactory.DropDown(200);
    private readonly Button _applyMode;
    private readonly NumericUpDown _width = UiFactory.Number(64, 16384, 90);
    private readonly NumericUpDown _height = UiFactory.Number(64, 16384, 90);
    private readonly NumericUpDown _frequency = UiFactory.Number(1, 1000, 90, 2, 0.5m);
    private readonly SettingCard _globalsCard;
    private readonly TableLayoutPanel _modes = new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1,
        GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        BackColor = Theme.PageBackground,
        Margin = Padding.Empty,
    };
    private VirtualDisplayStatus? _status;
    private bool _busy;

    public VirtualDisplayPage(ScalerController controller) : base(controller, "Écran virtuel")
    {
        AddSection("Écran virtuel",
            "Un écran sans matériel, avec les modes de votre choix, pour essayer les super-résolutions sans borne : un jeu ou SetResolution y basculent comme sur un vrai écran. "
            + "Il est invisible : mettez-le à l'échelle pour le voir. Créer et détruire demandent les droits d'administrateur (confirmation de Windows), pas les modes. "
            + "Pilote Virtual Display Driver (licence MIT), intégré à l'exe.");
        _create = UiFactory.AccentButton("Créer", async (_, _) => await RunAsync(VirtualDisplayManager.Create, refreshDelay: 2000));
        _destroy = UiFactory.Button("Détruire", async (_, _) => await RunAsync(VirtualDisplayManager.Destroy, refreshDelay: 1500));
        _stateCard = AddCard(Theme.GlyphVirtualDisplay, "État", null,
            UiFactory.Button("Identifier", (_, _) => DisplayIdentifier.Show()), _destroy, _create);

        AddSection("Mise à l'échelle",
            "L'écran virtuel est capturé et affiché sur un autre écran, avec le shader, le ratio d'aspect, le miroir, etc. "
            + "Pour un jeu, choisissez l'écran virtuel comme source dans son profil (Capture > Source), ou --display virtuel.");
        _scale = UiFactory.AccentButton("Mettre à l'échelle", (_, _) => ScaleVirtualDisplay());
        AddCard(Theme.GlyphFullScreen, "Afficher l'écran virtuel", "Avec les réglages par défaut, sur leur écran de sortie (Auto : un autre écran), jusqu'à l'arrêt", _scale);
        _applyMode = UiFactory.Button("Appliquer", (_, _) => ApplyCurrentMode());
        AddCard(Theme.GlyphMonitor, "Résolution de l'écran virtuel", "Comme SetResolution : le mode est posé et enregistré par Windows", _currentMode, _applyMode);

        AddSection("Modes",
            $"Enregistrés dans {VirtualDisplayManager.SettingsPath} ; le pilote les relit aussitôt. Le premier est celui d'un écran virtuel qui vient d'être créé. "
            + "Windows arrondit les fréquences : 57,5 Hz s'y affiche 58.");
        var addCard = AddCard(Theme.GlyphAdd, "Ajouter un mode", "Largeur, hauteur et fréquence (décimales acceptées, comme 57,5 Hz)",
            UiFactory.Button("Ajouter", async (_, _) => await AddModeAsync()));
        addCard.AddDetail(UiFactory.Row(UiFactory.Text("Largeur"), _width, UiFactory.Text("Hauteur"), _height, UiFactory.Text("Fréquence (Hz)"), _frequency));
        AddCard(Theme.GlyphGame, "Modes des bornes", "320x240, 640x480, 1280x240, 1920x240, 1280x480, 1024x768, 1280x960, 1920x1080 à 60 Hz ; 512x384 et 1024x384 à 57,5 Hz",
            UiFactory.Button("Ajouter", async (_, _) => await RunAsync(() => VirtualDisplayManager.AddModes(VirtualDisplaySettings.CabinetModes), refreshDelay: 800)));
        _globalsCard = AddCard(Theme.GlyphInfo, "Fréquences globales", null);
        _globalsCard.Visible = false;
        AddControl(_modes);
        _modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _width.Value = 1280;
        _height.Value = 240;
        _frequency.Value = 60;
    }

    public override void OnShown() => RefreshAll();

    public override void OnStateChanged() => UpdateButtons();

    private void RefreshAll()
    {
        try
        {
            _status = VirtualDisplayManager.GetStatus();
            _stateCard.Description = _status.Describe();
            _stateCard.DescriptionColor = _status.Monitor != null ? Theme.Success : Theme.SecondaryText;
            RefreshCurrentModes();
            RebuildModes(VirtualDisplayManager.LoadSettings());
        }
        catch (Exception ex)
        {
            Log.Warn($"Virtual display page: {ex.Message}");
            _stateCard.Description = $"Erreur : {ex.Message}";
            _stateCard.DescriptionColor = Color.Firebrick;
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool installed = _status?.DeviceInstalled == true;
        bool visible = _status?.Monitor != null;
        // Only the button that applies is shown: a disabled accent button still looks clickable.
        _create.Visible = !installed;
        _destroy.Visible = installed;
        _create.Enabled = !_busy && _status?.DriverAvailable == true;
        _destroy.Enabled = !_busy;
        _scale.Enabled = !_busy && visible;
        _applyMode.Enabled = !_busy && visible && _currentMode.SelectedItem is DisplayMode;
        _currentMode.Enabled = visible;
    }

    private void RefreshCurrentModes()
    {
        _currentMode.BeginUpdate();
        _currentMode.Items.Clear();
        if (_status?.Monitor is { } monitor)
        {
            DisplayMode? current = DisplayModes.Current(monitor.DeviceName);
            foreach (var mode in DisplayModes.List(monitor.DeviceName).Where(m => m.BitsPerPixel == 32))
            {
                int index = _currentMode.Items.Add(mode);
                if (mode == current)
                    _currentMode.SelectedIndex = index;
            }
        }
        _currentMode.EndUpdate();
    }

    /// <summary>One card per mode of vdd_settings.xml, with a button to remove it.</summary>
    private void RebuildModes(VirtualDisplaySettings settings)
    {
        foreach (Control card in _modes.Controls.Cast<Control>().ToList())
            card.Dispose();
        _modes.Controls.Clear();

        for (int i = 0; i < settings.Modes.Count; i++)
        {
            var mode = settings.Modes[i];
            var notes = new List<string>();
            if (i == 0)
                notes.Add("mode de démarrage");
            if (Math.Abs(mode.Frequency - Math.Round(mode.Frequency)) > 0.0005)
                notes.Add($"{Math.Round(mode.Frequency, MidpointRounding.AwayFromZero)} Hz dans Windows");
            var remove = UiFactory.IconButton(Theme.GlyphDelete, $"Retirer {mode}", async (_, _) =>
                await RunAsync(() => VirtualDisplayManager.RemoveMode(mode), refreshDelay: 800));
            remove.Enabled = settings.Modes.Count > 1;
            _modes.Controls.Add(new SettingCard(Theme.GlyphMonitor, $"{mode.Width} x {mode.Height} à {mode.FrequencyText.Replace('.', ',')} Hz",
                notes.Count > 0 ? string.Join(", ", notes) : null, remove));
        }

        _globalsCard.Visible = settings.GlobalRefreshRates.Count > 0;
        _globalsCard.Description = $"Le fichier ajoute aussi {string.Join(", ", settings.GlobalRefreshRates)} Hz à chaque taille : retirez les <g_refresh_rate> de {VirtualDisplayManager.SettingsPath} pour ne garder que ces modes.";
    }

    private async Task AddModeAsync()
    {
        int width = (int)_width.Value, height = (int)_height.Value;
        double frequency = Math.Round((double)_frequency.Value, 2);
        if (!VirtualMode.IsValid(width, height, frequency))
        {
            Controller.Report("Mode invalide");
            return;
        }
        await RunAsync(() => VirtualDisplayManager.AddModes([new VirtualMode(width, height, frequency)]), refreshDelay: 800);
    }

    private void ApplyCurrentMode()
    {
        if (_status?.Monitor is not { } monitor || _currentMode.SelectedItem is not DisplayMode mode)
            return;
        int result = DisplayModes.Change(monitor.DeviceName, mode, User32.CDS_UPDATEREGISTRY);
        Controller.Report(result == User32.DISP_CHANGE_SUCCESSFUL
            ? $"Écran virtuel en {mode}"
            : $"Écran virtuel : {mode} refusé ({DisplayModes.DescribeResult(result)})");
        RefreshAll();
    }

    private void ScaleVirtualDisplay()
    {
        var monitor = VirtualDisplayManager.GetStatus().Monitor;
        if (monitor == null)
            Controller.Report("Créez d'abord l'écran virtuel");
        else
            Controller.StartDisplay(monitor.Id);
    }

    /// <summary>Runs an action off the UI thread, reports its result, then refreshes the page once the display has settled.</summary>
    private async Task RunAsync(Func<string> action, int refreshDelay)
    {
        if (_busy)
            return;
        _busy = true;
        UpdateButtons();
        try
        {
            Controller.Report(await Task.Run(action));
        }
        catch (Exception ex)
        {
            Log.Warn($"Virtual display: {ex.Message}");
            Controller.Report(ex.Message);
        }
        finally
        {
            _busy = false;
        }
        RefreshAll();
        await Task.Delay(refreshDelay);
        if (!IsDisposed)
            RefreshAll();
    }
}
