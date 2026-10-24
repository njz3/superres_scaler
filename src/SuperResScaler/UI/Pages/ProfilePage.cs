using SuperResScaler.App;
using SuperResScaler.Config;
using SuperResScaler.Engine;
using SuperResScaler.Native;
using SuperResScaler.Rendering;

namespace SuperResScaler.UI.Pages;

/// <summary>
/// The settings of the defaults or of one game profile: which windows it applies to (process,
/// title, automatic start), rendering, capture, advanced options. Every change is saved at once
/// and applied live when this profile is the one being used.
/// </summary>
internal sealed class ProfilePage : Page
{
    private sealed record ModeItem(ScaleMode Mode, string Label) { public override string ToString() => Label; }
    private sealed record MonitorItem(int Index, string Label) { public override string ToString() => Label; }

    private const string UnchangedResolution = "(inchangée)";

    private readonly ScalingOptions _options;
    private readonly Profile? _profile;

    private readonly TextBox _name = UiFactory.TextBox(260);
    private readonly TextBox _process = UiFactory.TextBox(260);
    private readonly TextBox _title = UiFactory.TextBox(260);
    private readonly ToggleSwitch _autoScale = new();
    private readonly NumericUpDown _autoDelay = UiFactory.Number(0, 60, 60, 1, 0.5m);
    private readonly SettingCard? _matchCard;

    private readonly ComboBox _shader = UiFactory.DropDown(200);
    private readonly ComboBox _mode = UiFactory.DropDown(250);
    private readonly ComboBox _aspect = UiFactory.Editable(200, "original", "4:3", "16:9", "4:3_stretch", "16:9_stretch", "4:3_crop", "16:9_crop", "5:4", "8:7");
    private readonly NumericUpDown _cropPosition = UiFactory.Number(-100, 100, 66);
    private readonly ToggleSwitch _barrel = new();
    private readonly NumericUpDown _barrelCenter = UiFactory.Number(0, 100, 60);
    private readonly NumericUpDown _barrelRadius = UiFactory.Number(1, 1000, 66);
    private readonly SettingCard _cropCard;
    private readonly ComboBox _displayAspect = UiFactory.Editable(110, "auto", "4:3", "16:9", "16:10", "5:4");
    private readonly ComboBox _monitor = UiFactory.DropDown(260);
    private readonly ComboBox _resolution = UiFactory.Editable(170, UnchangedResolution);
    private readonly ComboBox _placementWindow = UiFactory.DropDown(260);
    private readonly SettingCard _placementCard;

    private readonly ToggleSwitch _clientArea = new();
    private readonly NumericUpDown _cropLeft = UiFactory.Number(0, 4000, 66);
    private readonly NumericUpDown _cropTop = UiFactory.Number(0, 4000, 66);
    private readonly NumericUpDown _cropRight = UiFactory.Number(0, 4000, 66);
    private readonly NumericUpDown _cropBottom = UiFactory.Number(0, 4000, 66);
    private readonly ToggleSwitch _autoCrop = new();
    private readonly ToggleSwitch _cursor = new();
    private readonly ToggleSwitch _vsync = new();
    private readonly ToggleSwitch _preview = new();
    private readonly NumericUpDown _previewWidth = UiFactory.Number(64, 7680, 76);
    private readonly NumericUpDown _previewHeight = UiFactory.Number(16, 4320, 70);

    public ProfilePage(ScalerController controller, ScalingOptions options)
        : base(controller, (options as Profile)?.DisplayName ?? "Par défaut")
    {
        _options = options;
        _profile = options as Profile;

        if (_profile != null)
        {
            AddSection("Application", "Les fenêtres auxquelles ce profil s'applique, comme les filtres de jeux de BackForceFeeder.");
            AddCard(Theme.GlyphRename, "Nom", null, _name);
            AddCard(Theme.GlyphApp, "Processus", "Ex. game.exe — jokers * et ?, alternatives séparées par |",
                _process, UiFactory.Button("Choisir…", (_, _) => PickWindow()));
            AddCard(Theme.GlyphTag, "Titre de fenêtre", "Facultatif : texte contenu dans le titre (ou celui d'une fenêtre enfant), ou motif * ? |", _title);
            AddCard(Theme.GlyphEye, "Mise à l'échelle automatique",
                "Démarre dès qu'une fenêtre correspond, quand SuperResScaler surveille les profils (zone de notification)",
                UiFactory.Text("après"), _autoDelay, UiFactory.Text("s"), _autoScale);
            _matchCard = AddCard(Theme.GlyphSearch, "Fenêtres correspondantes", null,
                UiFactory.Button("Actualiser", (_, _) => RefreshMatches()),
                UiFactory.AccentButton("Mettre à l'échelle", (_, _) => ScaleMatchingWindow()));
        }
        else
        {
            AddCard(Theme.GlyphLayers, "Réglages par défaut", "Utilisés pour toute fenêtre qui n'a pas de profil (raccourci, fenêtre précise, ligne de commande).");
        }

        AddSection("Rendu");
        AddCard(Theme.GlyphColor, "Shader", "Filtre de mise à l'échelle (dossier Shaders)", _shader);
        AddCard(Theme.GlyphFullScreen, "Affichage", "Le cadre remplit l'écran, ou garde sa forme avec des bandes noires", _mode);
        AddCard(Theme.GlyphAspect, "Ratio d'aspect", "Comme le linuxloader : 4:3 = image entière à sa forme · _stretch = déformée · _crop = rognée", _aspect);
        _cropCard = AddCard(Theme.GlyphCrop, "Rognage (formats _crop)", "Partie gardée : -100 gauche/haut … 0 … 100 droite/bas. Barillet : bords courbés au lieu de coupés",
            _cropPosition, UiFactory.Text("Barillet"), _barrel);
        _cropCard.AddDetail(UiFactory.Row(UiFactory.Text("Milieu non déformé"), _barrelCenter, UiFactory.Text("%   Rayon"), _barrelRadius, UiFactory.Text("%")));
        AddCard(Theme.GlyphMonitor, "Forme de l'écran", "auto = 4:3 pour un mode super-résolution (1920x240…), pixels carrés sinon", _displayAspect);
        AddCard(Theme.GlyphMonitor, "Écran de sortie", null, _monitor);
        AddCard(Theme.GlyphMonitor, "Résolution", "Mode de l'écran pendant la mise à l'échelle (ex. 1920x240@60) ; la résolution d'origine revient à l'arrêt", _resolution);
        _placementCard = AddCard(Theme.GlyphPicture, "Placement", null, _placementWindow);

        AddSection("Capture");
        AddCard(Theme.GlyphWindow, "Zone client uniquement", "Sans la barre de titre ni les bordures", _clientArea);
        AddCard(Theme.GlyphCrop, "Recadrage manuel", "Pixels retirés de chaque côté",
            UiFactory.Text("G"), _cropLeft, UiFactory.Text("H"), _cropTop, UiFactory.Text("D"), _cropRight, UiFactory.Text("B"), _cropBottom);
        AddCard(Theme.GlyphCrop, "Recadrage automatique des bandes noires", "Retire les bandes d'un jeu 4:3 centré dans une fenêtre plus large", _autoCrop);
        AddCard(Theme.GlyphMouse, "Capturer le curseur", null, _cursor);

        AddSection("Avancé");
        AddCard(Theme.GlyphSync, "VSync", "Désactivée : latence minimale, déchirement possible", _vsync);
        AddCard(Theme.GlyphPreview, "Prévisualisation fenêtrée", "Pour tester sur un écran de PC : une fenêtre de cette taille au lieu du plein écran",
            _previewWidth, UiFactory.Text("x"), _previewHeight, _preview);

        if (_profile != null)
        {
            AddSection("Profil");
            AddCard(Theme.GlyphCopy, "Dupliquer", "Crée un nouveau profil avec ces réglages",
                UiFactory.Button("Dupliquer", (_, _) => Duplicated?.Invoke(_profile)));
            AddCard(Theme.GlyphDelete, "Supprimer", "Supprime ce profil",
                UiFactory.Button("Supprimer", (_, _) => Delete()));
        }

        PopulateLists();
        Wire();
    }

    /// <summary>The profile's name changed (the navigation pane shows it).</summary>
    public event Action<Profile>? Renamed;
    public event Action<Profile>? Deleted;
    public event Action<Profile>? Duplicated;

    public ScalingOptions Options => _options;

    private void PopulateLists()
    {
        _mode.Items.AddRange([new ModeItem(ScaleMode.Stretch, "Plein écran étiré"), new ModeItem(ScaleMode.KeepAspect, "Plein écran au ratio (bandes noires)")]);
        _monitor.Items.Add(new MonitorItem(0, "Auto (écran de la fenêtre)"));
        foreach (var monitor in MonitorInfo.GetAll())
            _monitor.Items.Add(new MonitorItem(monitor.Index, monitor.ToString()));
        _shader.Items.AddRange(ShaderLibrary.List().ToArray<object>());
        _placementWindow.DropDownWidth = Theme.S(520);
    }

    private void Wire()
    {
        foreach (var text in new Control[] { _name, _process, _title, _aspect, _displayAspect, _resolution })
            text.TextChanged += (_, _) => Changed();
        foreach (var combo in new[] { _shader, _mode, _monitor })
            combo.SelectedIndexChanged += (_, _) => Changed();
        foreach (var toggle in new[] { _autoScale, _barrel, _clientArea, _autoCrop, _cursor, _vsync, _preview })
            toggle.CheckedChanged += (_, _) => Changed();
        foreach (var number in new[] { _autoDelay, _cropPosition, _barrelCenter, _barrelRadius, _cropLeft, _cropTop, _cropRight, _cropBottom, _previewWidth, _previewHeight })
            number.ValueChanged += (_, _) => Changed();
        _resolution.DropDown += (_, _) => FillResolutions();
        _placementWindow.DropDown += (_, _) => RefreshPlacementWindows();
        _placementWindow.SelectedIndexChanged += (_, _) => UpdatePlacement();
    }

    public override void OnShown()
    {
        Loading = true;
        var o = _options;
        if (_profile != null)
        {
            _name.Text = _profile.Name;
            _process.Text = _profile.ProcessName;
            _title.Text = _profile.WindowTitle ?? string.Empty;
            _autoScale.Checked = _profile.AutoScale;
            _autoDelay.Value = (decimal)Math.Clamp(_profile.AutoScaleDelay, 0, 60);
        }
        if (!_shader.Items.Contains(o.Shader))
            _shader.Items.Add(o.Shader);
        _shader.SelectedItem = o.Shader;
        _mode.SelectedItem = _mode.Items.Cast<ModeItem>().First(m => m.Mode == o.Mode);
        _aspect.Text = o.AspectRatio;
        _cropPosition.Value = Math.Clamp(o.CropPosition, -100, 100);
        _barrel.Checked = o.CropBarrel;
        _barrelCenter.Value = Math.Clamp(o.BarrelCenter, 0, 100);
        _barrelRadius.Value = Math.Clamp(o.BarrelRadius, 1, 1000);
        _displayAspect.Text = o.DisplayAspect;
        _monitor.SelectedItem = _monitor.Items.Cast<MonitorItem>().FirstOrDefault(m => m.Index == o.Monitor) ?? _monitor.Items[0];
        _resolution.Text = ResolutionRequest.IsUnchanged(o.Resolution) ? UnchangedResolution : o.Resolution;
        _clientArea.Checked = o.ClientAreaOnly;
        _cropLeft.Value = Math.Clamp(o.CropLeft, 0, 4000);
        _cropTop.Value = Math.Clamp(o.CropTop, 0, 4000);
        _cropRight.Value = Math.Clamp(o.CropRight, 0, 4000);
        _cropBottom.Value = Math.Clamp(o.CropBottom, 0, 4000);
        _autoCrop.Checked = o.AutoCrop;
        _cursor.Checked = o.CaptureCursor;
        _vsync.Checked = o.VSync;
        _preview.Checked = o.Preview;
        _previewWidth.Value = Math.Clamp(o.PreviewWidth, 64, 7680);
        _previewHeight.Value = Math.Clamp(o.PreviewHeight, 16, 4320);
        foreach (var combo in new[] { _aspect, _displayAspect, _resolution })
            combo.BackColor = SystemColors.Window;
        Loading = false;

        RefreshMatches();
        RefreshPlacementWindows();
        UpdateEnabledState();
    }

    private void Changed()
    {
        if (Loading)
            return;
        var o = _options;
        if (_profile != null)
        {
            string oldName = _profile.DisplayName;
            _profile.Name = _name.Text.Trim();
            _profile.ProcessName = _process.Text.Trim();
            _profile.WindowTitle = string.IsNullOrWhiteSpace(_title.Text) ? null : _title.Text.Trim();
            _profile.AutoScale = _autoScale.Checked;
            _profile.AutoScaleDelay = (double)_autoDelay.Value;
            if (_profile.DisplayName != oldName)
            {
                PageTitle = _profile.DisplayName;
                Renamed?.Invoke(_profile);
            }
        }

        o.Shader = _shader.SelectedItem as string ?? o.Shader;
        o.Mode = (_mode.SelectedItem as ModeItem)?.Mode ?? o.Mode;
        o.Monitor = (_monitor.SelectedItem as MonitorItem)?.Index ?? 0;
        o.AspectRatio = ValidatedText(_aspect, t => PictureAspect.TryParse(t, out _), "original") ?? o.AspectRatio;
        o.DisplayAspect = ValidatedText(_displayAspect, t => t.Length == 0 || t.Equals("auto", StringComparison.OrdinalIgnoreCase) || Ratio.TryParse(t, out _), "auto") ?? o.DisplayAspect;
        string? resolution = ValidatedText(_resolution, t => ResolutionRequest.IsUnchanged(t) || ResolutionRequest.TryParse(t, out _), string.Empty);
        if (resolution != null)
            o.Resolution = ResolutionRequest.IsUnchanged(resolution) ? string.Empty : resolution;
        o.CropPosition = (int)_cropPosition.Value;
        o.CropBarrel = _barrel.Checked;
        o.BarrelCenter = (int)_barrelCenter.Value;
        o.BarrelRadius = (int)_barrelRadius.Value;
        o.ClientAreaOnly = _clientArea.Checked;
        o.CropLeft = (int)_cropLeft.Value;
        o.CropTop = (int)_cropTop.Value;
        o.CropRight = (int)_cropRight.Value;
        o.CropBottom = (int)_cropBottom.Value;
        o.AutoCrop = _autoCrop.Checked;
        o.CaptureCursor = _cursor.Checked;
        o.VSync = _vsync.Checked;
        o.Preview = _preview.Checked;
        o.PreviewWidth = (int)_previewWidth.Value;
        o.PreviewHeight = (int)_previewHeight.Value;

        Controller.OnOptionsEdited(o);
        UpdateEnabledState();
        UpdatePlacement();
    }

    /// <summary>The combo's text when valid (empty → <paramref name="empty"/>), else null and a red background.</summary>
    private static string? ValidatedText(ComboBox combo, Func<string, bool> isValid, string empty)
    {
        string text = combo.Text.Trim();
        bool valid = isValid(text);
        combo.BackColor = valid ? SystemColors.Window : Color.MistyRose;
        return valid ? (text.Length == 0 ? empty : text) : null;
    }

    private void UpdateEnabledState()
    {
        bool crop = PictureAspect.TryParse(_options.AspectRatio, out var aspect) && aspect.Kind == PictureAspectKind.Crop;
        _cropPosition.Enabled = _barrel.Enabled = crop;
        _barrelCenter.Enabled = _barrelRadius.Enabled = crop && _options.CropBarrel;
        _previewWidth.Enabled = _previewHeight.Enabled = _options.Preview;
        _autoDelay.Enabled = _profile?.AutoScale ?? false;
    }

    // ------------------------------------------------------------------ windows

    private List<WindowInfo> MatchingWindows()
        => _profile == null ? new List<WindowInfo>()
            : WindowInfo.EnumerateCandidates().Where(w => _profile.Matches(w, includeChildren: true)).OrderByDescending(w => w.ClientArea).ToList();

    private void RefreshMatches()
    {
        if (_matchCard == null)
            return;
        var matches = MatchingWindows();
        _matchCard.Description = matches.Count == 0
            ? "Aucune fenêtre ouverte ne correspond (lancez le jeu, puis Actualiser)"
            : string.Join("\n", matches.Take(3).Select(w => $"« {w.Title} » — {w.ProcessName}"));
        _matchCard.DescriptionColor = matches.Count == 0 ? Theme.SecondaryText : Theme.Success;
    }

    private void ScaleMatchingWindow()
    {
        var window = MatchingWindows().FirstOrDefault();
        if (window == null)
        {
            RefreshMatches();
            Controller.Report($"Aucune fenêtre ne correspond au profil {_profile?.DisplayName}");
            return;
        }
        Controller.StartScaling(window.Handle, _options);
    }

    private void PickWindow()
    {
        using var dialog = new WindowPickerDialog();
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK || dialog.Result is not { } window)
            return;
        _process.Text = window.ProcessName;
        if (string.IsNullOrWhiteSpace(_name.Text))
            _name.Text = window.Title;
    }

    private void Delete()
    {
        if (_profile == null)
            return;
        if (MessageBox.Show(FindForm(), $"Supprimer le profil « {_profile.DisplayName} » ?", "SuperResScaler",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            Deleted?.Invoke(_profile);
    }

    // ------------------------------------------------------------------ resolution and placement

    private MonitorInfo TargetMonitor()
    {
        var monitors = MonitorInfo.GetAll();
        if (_options.Monitor > 0 && _options.Monitor <= monitors.Count)
            return monitors[_options.Monitor - 1];
        if (_placementWindow.SelectedItem is WindowInfo window)
            return MonitorInfo.FromWindow(window.Handle);
        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    /// <summary>The 32-bit modes of the output monitor, like SetResolution LIST.</summary>
    private void FillResolutions()
    {
        string text = _resolution.Text;
        _resolution.BeginUpdate();
        _resolution.Items.Clear();
        _resolution.Items.Add(UnchangedResolution);
        foreach (var mode in DisplayModes.List(TargetMonitor().DeviceName).Where(m => m.BitsPerPixel == 32).Reverse())
            _resolution.Items.Add(mode.ToString());
        _resolution.EndUpdate();
        _resolution.Text = text;
    }

    private void RefreshPlacementWindows()
    {
        IntPtr selected = (_placementWindow.SelectedItem as WindowInfo)?.Handle ?? IntPtr.Zero;
        var windows = WindowInfo.EnumerateCandidates();
        if (selected == IntPtr.Zero && _profile != null)
            selected = windows.Where(w => _profile.Matches(w, includeChildren: true)).OrderByDescending(w => w.ClientArea).FirstOrDefault()?.Handle ?? IntPtr.Zero;
        _placementWindow.BeginUpdate();
        _placementWindow.Items.Clear();
        foreach (var window in windows)
        {
            int index = _placementWindow.Items.Add(window);
            if (window.Handle == selected)
                _placementWindow.SelectedIndex = index;
        }
        _placementWindow.EndUpdate();
        UpdatePlacement();
    }

    /// <summary>Where the chosen window's picture lands with these settings.</summary>
    private void UpdatePlacement()
    {
        if (_placementWindow.SelectedItem is not WindowInfo window || !Dwmapi.TryGetFrameBounds(window.Handle, out RECT frame))
        {
            _placementCard.Description = "Choisissez une fenêtre pour voir où son image sera affichée";
            return;
        }
        var o = _options;
        Rectangle source = Geometry.ComputeSourceRegion(window.Handle, o, frame.Width, frame.Height);
        Size output;
        if (o.Preview)
            output = new Size(o.PreviewWidth, o.PreviewHeight);
        else if (ResolutionRequest.TryParse(o.Resolution, out var resolution))
            output = new Size(resolution.Width, resolution.Height);
        else
            output = new Size(TargetMonitor().Bounds.Width, TargetMonitor().Bounds.Height);
        if (source.Width <= 0 || source.Height <= 0 || output.Width <= 0 || output.Height <= 0)
        {
            _placementCard.Description = "Fenêtre vide après recadrage";
            return;
        }

        var placement = Geometry.ComputePlacement(o, source.Width, source.Height, output.Width, output.Height);
        RectangleF dest = placement.Dest;
        double shape = Geometry.ResolveDisplayAspect(o.DisplayAspect, output.Width, output.Height);
        string bars = dest.Width < output.Width && dest.Height < output.Height ? "bandes noires tout autour"
            : dest.Width < output.Width ? "bandes noires à gauche et à droite"
            : dest.Height < output.Height ? "bandes noires en haut et en bas"
            : "plein écran";
        string cut = placement.Barrel != null ? ", bords courbés"
            : placement.Source.Size != source.Size ? $", source rognée à {placement.Source.Width}x{placement.Source.Height}" : "";
        _placementCard.Description = $"Source {source.Width}x{source.Height} → écran {output.Width}x{output.Height} (forme {shape:0.###}) : "
                                   + $"image {dest.Width:0}x{dest.Height:0} en ({dest.X:0},{dest.Y:0}), {bars}{cut}"
                                   + (o.AutoCrop ? " — avant recadrage auto" : "");
    }
}
