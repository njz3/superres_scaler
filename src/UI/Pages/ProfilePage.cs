// Copyright (c) Benjamin Maurin (njz3) 2026

using SuperResScaler.App;
using SuperResScaler.Capture;
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
    private sealed record MonitorItem(string Id, string Label) { public override string ToString() => Label; }
    private sealed record CaptureItem(CaptureMethod Method, string Label) { public override string ToString() => Label; }
    private sealed record FrameItem(FrameMode Mode, string Label, string Description) { public override string ToString() => Label; }

    private static readonly FrameItem[] FrameModes =
    [
        new(FrameMode.Margins, "Marges en pixels",
            "Pixels retirés de chaque côté de la source ; une valeur négative ajoute du noir de ce côté (au plus la moitié de l'image)"),
        new(FrameMode.Corners, "Quatre coins",
            "Coins du cadre en % de l'image : à l'intérieur pour zoomer, à l'extérieur (de -50 à 150 %) pour ajouter du noir autour"),
        new(FrameMode.Zoom, "Zoom et centre",
            "100 % = l'image entière ; plus pour zoomer, jusqu'à 50 % pour dézoomer (noir autour) ; centre en % de l'image"),
        new(FrameMode.Rectangle, "Rectangle sur l'image",
            "Tirez un coin du cadre, ou le cadre pour le déplacer, sur l'image de la fenêtre choisie dans « Placement » ou de l'écran source, "
            + "actualisée chaque seconde (ce qui est à l'écran à cet endroit). Mêmes valeurs que « Quatre coins »"),
    ];

    /// <summary>The picture under the rectangle is taken again this often (ms), while it is shown.</summary>
    private const int FramePictureInterval = 1000;

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
    private readonly ToggleSwitch _mirrorX = new();
    private readonly ToggleSwitch _mirrorY = new();
    private readonly ToggleSwitch _interlace = new();
    private readonly ComboBox _displayAspect = UiFactory.Editable(110, "auto", "4:3", "16:9", "16:10", "5:4");
    private readonly ComboBox _monitor = UiFactory.DropDown(420);
    private readonly ComboBox _resolution = UiFactory.Editable(170, UnchangedResolution);
    private readonly ComboBox _placementWindow = UiFactory.DropDown(260);
    private readonly SettingCard _placementCard;

    private readonly ComboBox _sourceDisplay = UiFactory.DropDown(420);
    private readonly ToggleSwitch _clientArea = new();
    private readonly ComboBox _frameMode = UiFactory.DropDown(220);
    private readonly SettingCard _frameCard;
    private readonly SliderField _cropLeft = new("Gauche", -2000, 4000, 0, "px");
    private readonly SliderField _cropTop = new("Haut", -2000, 4000, 0, "px");
    private readonly SliderField _cropRight = new("Droite", -2000, 4000, 0, "px");
    private readonly SliderField _cropBottom = new("Bas", -2000, 4000, 0, "px");
    private readonly SliderField _frameLeft = new("Haut-gauche : X", Geometry.MinFramePercent, Geometry.MaxFramePercent, 1, "%");
    private readonly SliderField _frameTop = new("Haut-gauche : Y", Geometry.MinFramePercent, Geometry.MaxFramePercent, 1, "%");
    private readonly SliderField _frameRight = new("Bas-droit : X", Geometry.MinFramePercent, Geometry.MaxFramePercent, 1, "%");
    private readonly SliderField _frameBottom = new("Bas-droit : Y", Geometry.MinFramePercent, Geometry.MaxFramePercent, 1, "%");
    private readonly SliderField _zoomX = new("Zoom horizontal", Geometry.MinZoom, Geometry.MaxZoom, 0, "%");
    private readonly SliderField _zoomY = new("Zoom vertical", Geometry.MinZoom, Geometry.MaxZoom, 0, "%");
    private readonly ToggleSwitch _zoomLinked = new();
    private readonly SliderField _zoomCenterX = new("Centre : X", Geometry.MinFramePercent, Geometry.MaxFramePercent, 1, "%");
    private readonly SliderField _zoomCenterY = new("Centre : Y", Geometry.MinFramePercent, Geometry.MaxFramePercent, 1, "%");
    private readonly FrameEditor _frameEditor = new();
    private readonly Label _frameValues = UiFactory.Text(string.Empty);
    private readonly Dictionary<FrameMode, Control> _framePanels = new();
    private readonly System.Windows.Forms.Timer _framePictureTimer = new() { Interval = FramePictureInterval };
    private bool _takingFramePicture;
    private string? _framePictureError;
    private readonly ToggleSwitch _autoCrop = new();
    private readonly ToggleSwitch _cursor = new();
    private readonly ComboBox _captureMethod = UiFactory.DropDown(330);
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
        AddCard(Theme.GlyphAspect, "Ratio d'aspect", "4:3 = image entière à sa forme · _stretch = déformée · _crop = rognée", _aspect);
        _cropCard = AddCard(Theme.GlyphCrop, "Rognage (formats _crop)", "Partie gardée : -100 gauche/haut … 0 … 100 droite/bas. Barillet : bords courbés au lieu de coupés",
            _cropPosition, UiFactory.Text("Barillet"), _barrel);
        _cropCard.AddDetail(UiFactory.Row(UiFactory.Text("Milieu non déformé"), _barrelCenter, UiFactory.Text("%   Rayon"), _barrelRadius, UiFactory.Text("%")));
        AddCard(Theme.GlyphMonitor, "Forme de l'écran", "auto = 4:3 pour un mode super-résolution (1920x240…), pixels carrés sinon", _displayAspect);
        AddCard(Theme.GlyphMonitor, "Écran de sortie", "Auto = celui de la fenêtre ; si tout un écran est capturé, un autre que celui-là",
            _monitor, UiFactory.Button("Identifier", (_, _) => DisplayIdentifier.Show()));
        AddCard(Theme.GlyphMonitor, "Résolution", "Mode de l'écran pendant la mise à l'échelle (ex. 1920x240@60) ; la résolution d'origine revient à l'arrêt", _resolution);
        AddCard(Theme.GlyphMirror, "Miroir", "Pour un projecteur : X = derrière un écran (rétroprojection), Y = par le dessus, X et Y = image tournée de 180°",
            UiFactory.Text("X"), _mirrorX, UiFactory.Text("Y"), _mirrorY);
        AddCard(Theme.GlyphSync, "Entrelacement simulé",
            "À chaque rafraîchissement, les lignes paires puis impaires de la source : les traits fins (texte) restent nets mais "
            + "scintillent à 30 Hz. Seulement quand la source a plus de lignes que l'image à l'écran (640x480 sur 1920x240) ; "
            + "sinon, sans effet",
            _interlace);
        _placementCard = AddCard(Theme.GlyphPicture, "Placement", null, _placementWindow);

        AddSection("Capture");
        AddCard(Theme.GlyphMonitor, "Source", "La fenêtre du jeu, ou tout un écran ; la fenêtre ne décide alors que de la fin",
            _sourceDisplay, UiFactory.Button("Identifier", (_, _) => DisplayIdentifier.Show()));
        AddCard(Theme.GlyphWindow, "Zone client uniquement", "Sans la barre de titre ni les bordures", _clientArea);
        _frameCard = AddCard(Theme.GlyphCrop, "Recadrage", null, _frameMode, UiFactory.Button("Réinitialiser", (_, _) => ResetFrame()));
        _framePanels[FrameMode.Margins] = Column(_cropLeft, _cropTop, _cropRight, _cropBottom);
        _framePanels[FrameMode.Corners] = Column(_frameLeft, _frameTop, _frameRight, _frameBottom);
        _framePanels[FrameMode.Zoom] = Column(_zoomX, _zoomY, UiFactory.Row(UiFactory.Text("Même zoom en largeur et en hauteur"), _zoomLinked),
            _zoomCenterX, _zoomCenterY);
        _framePanels[FrameMode.Rectangle] = Column(_frameEditor, _frameValues);
        _frameCard.AddDetail(Column(_framePanels.Values.ToArray()));
        _framePictureTimer.Tick += (_, _) => RefreshFramePicture();
        _framePictureTimer.Start();
        Disposed += (_, _) => _framePictureTimer.Dispose();
        AddCard(Theme.GlyphCrop, "Recadrage automatique des bandes noires", "Retire les bandes d'un jeu 4:3 centré dans une fenêtre plus large", _autoCrop);
        AddCard(Theme.GlyphMouse, "Capturer le curseur", "Pas avec la duplication de bureau", _cursor);
        AddCard(Theme.GlyphSync, "Méthode de capture d'un écran",
            "Auto : Windows.Graphics.Capture dès Windows 10 1903, sinon la duplication de bureau (seule possible en 1809, écran entier). "
            + "Une fenêtre est toujours capturée par Windows.Graphics.Capture",
            _captureMethod);

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
        _frameMode.Items.AddRange(FrameModes);
        _shader.Items.AddRange(ShaderLibrary.List().ToArray<object>());
        bool graphicsCapture = CaptureSupport.GraphicsCaptureAvailable;
        _captureMethod.Items.AddRange(
        [
            new CaptureItem(CaptureMethod.Auto, $"Auto (ici : {CaptureSupport.Describe(graphicsCapture ? CaptureMethod.GraphicsCapture : CaptureMethod.DesktopDuplication)})"),
            new CaptureItem(CaptureMethod.GraphicsCapture, "Windows.Graphics.Capture" + (graphicsCapture ? "" : " (indisponible ici)")),
            new CaptureItem(CaptureMethod.DesktopDuplication, "Duplication de bureau"),
        ]);
        _placementWindow.DropDownWidth = _monitor.DropDownWidth = _sourceDisplay.DropDownWidth = Theme.S(520);
    }

    /// <summary>Lists the displays and selects <paramref name="value"/>, which is kept even if that display is absent today.</summary>
    private static void FillDisplayList(ComboBox combo, string emptyLabel, IReadOnlyList<MonitorInfo> monitors, string value)
    {
        combo.Items.Clear();
        combo.Items.Add(new MonitorItem(string.Empty, emptyLabel));
        foreach (var monitor in monitors)
            combo.Items.Add(new MonitorItem(monitor.Id, monitor.ToString()));
        string id = MonitorInfo.Find(monitors, value)?.Id ?? value;
        MonitorItem? selected = combo.Items.Cast<MonitorItem>().FirstOrDefault(m => m.Id == id);
        if (selected == null)
        {
            selected = new MonitorItem(value, $"Écran absent : {value}");
            combo.Items.Add(selected);
        }
        combo.SelectedItem = selected;
    }

    private void Wire()
    {
        foreach (var text in new Control[] { _name, _process, _title, _aspect, _displayAspect, _resolution })
            text.TextChanged += (_, _) => Changed();
        foreach (var combo in new[] { _shader, _mode, _monitor, _sourceDisplay, _captureMethod, _frameMode })
            combo.SelectedIndexChanged += (_, _) => Changed();
        foreach (var toggle in new[] { _autoScale, _barrel, _mirrorX, _mirrorY, _interlace, _clientArea, _autoCrop, _cursor, _vsync, _preview })
            toggle.CheckedChanged += (_, _) => Changed();
        foreach (var number in new[] { _autoDelay, _cropPosition, _barrelCenter, _barrelRadius, _previewWidth, _previewHeight })
            number.ValueChanged += (_, _) => Changed();

        // The frame: applied live while a slider or the rectangle is dragged, saved when released.
        foreach (var slider in new[] { _cropLeft, _cropTop, _cropRight, _cropBottom, _frameLeft, _frameTop, _frameRight, _frameBottom, _zoomCenterX, _zoomCenterY })
            slider.ValueChanged += save => Changed(save);
        _zoomX.ValueChanged += save =>
        {
            if (_zoomLinked.Checked)
                _zoomY.Value = _zoomX.Value;
            Changed(save);
        };
        _zoomY.ValueChanged += save =>
        {
            if (_zoomLinked.Checked)
                _zoomX.Value = _zoomY.Value;
            Changed(save);
        };
        _zoomLinked.CheckedChanged += (_, _) =>
        {
            if (_zoomLinked.Checked)
                _zoomY.Value = _zoomX.Value;
            Changed();
        };
        _frameEditor.FrameChanged += save =>
        {
            (_frameLeft.Value, _frameTop.Value, _frameRight.Value, _frameBottom.Value) = _frameEditor.Frame;
            Changed(save);
        };

        _resolution.DropDown += (_, _) => FillResolutions();
        _placementWindow.DropDown += (_, _) => RefreshPlacementWindows();
        _placementWindow.SelectedIndexChanged += (_, _) =>
        {
            UpdatePlacement();
            if (_options.FrameMode == FrameMode.Rectangle)
                RefreshFramePicture();
        };
    }

    private static FlowLayoutPanel Column(params Control[] controls)
    {
        var column = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = Padding.Empty,
            BackColor = Theme.CardBackground,
        };
        column.Controls.AddRange(controls);
        return column;
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
        _mirrorX.Checked = o.MirrorX;
        _mirrorY.Checked = o.MirrorY;
        _interlace.Checked = o.SimulatedInterlace;
        _displayAspect.Text = o.DisplayAspect;
        var monitors = MonitorInfo.GetAll();
        FillDisplayList(_monitor, "Auto", monitors, o.Monitor);
        FillDisplayList(_sourceDisplay, "La fenêtre du jeu", monitors, o.SourceDisplay);
        _resolution.Text = ResolutionRequest.IsUnchanged(o.Resolution) ? UnchangedResolution : o.Resolution;
        _clientArea.Checked = o.ClientAreaOnly;
        _frameMode.SelectedItem = FrameModes.FirstOrDefault(m => m.Mode == o.FrameMode) ?? FrameModes[0];
        _cropLeft.Value = o.CropLeft;
        _cropTop.Value = o.CropTop;
        _cropRight.Value = o.CropRight;
        _cropBottom.Value = o.CropBottom;
        _frameLeft.Value = o.FrameLeft;
        _frameTop.Value = o.FrameTop;
        _frameRight.Value = o.FrameRight;
        _frameBottom.Value = o.FrameBottom;
        _zoomX.Value = o.ZoomX;
        _zoomY.Value = o.ZoomY;
        _zoomLinked.Checked = o.ZoomLinked;
        _zoomCenterX.Value = o.ZoomCenterX;
        _zoomCenterY.Value = o.ZoomCenterY;
        _autoCrop.Checked = o.AutoCrop;
        _cursor.Checked = o.CaptureCursor;
        _captureMethod.SelectedItem = _captureMethod.Items.Cast<CaptureItem>().FirstOrDefault(c => c.Method == o.CaptureMethod)
                                      ?? _captureMethod.Items[0];
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
        ShowFrameMode();
    }

    /// <summary>
    /// Copies the settings into the options and applies them. <paramref name="save"/> false while a
    /// slider or the frame is being dragged: applied live, saved when released.
    /// </summary>
    private void Changed(bool save = true)
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
        o.Monitor = (_monitor.SelectedItem as MonitorItem)?.Id ?? string.Empty;
        o.SourceDisplay = (_sourceDisplay.SelectedItem as MonitorItem)?.Id ?? string.Empty;
        o.AspectRatio = ValidatedText(_aspect, t => PictureAspect.TryParse(t, out _), "original") ?? o.AspectRatio;
        o.DisplayAspect = ValidatedText(_displayAspect, t => t.Length == 0 || t.Equals("auto", StringComparison.OrdinalIgnoreCase) || Ratio.TryParse(t, out _), "auto") ?? o.DisplayAspect;
        string? resolution = ValidatedText(_resolution, t => ResolutionRequest.IsUnchanged(t) || ResolutionRequest.TryParse(t, out _), string.Empty);
        if (resolution != null)
            o.Resolution = ResolutionRequest.IsUnchanged(resolution) ? string.Empty : resolution;
        o.CropPosition = (int)_cropPosition.Value;
        o.CropBarrel = _barrel.Checked;
        o.BarrelCenter = (int)_barrelCenter.Value;
        o.BarrelRadius = (int)_barrelRadius.Value;
        o.MirrorX = _mirrorX.Checked;
        o.MirrorY = _mirrorY.Checked;
        o.SimulatedInterlace = _interlace.Checked;
        o.ClientAreaOnly = _clientArea.Checked;
        FrameMode previousFrameMode = o.FrameMode;
        o.FrameMode = (_frameMode.SelectedItem as FrameItem)?.Mode ?? o.FrameMode;
        o.CropLeft = (int)_cropLeft.Value;
        o.CropTop = (int)_cropTop.Value;
        o.CropRight = (int)_cropRight.Value;
        o.CropBottom = (int)_cropBottom.Value;
        o.FrameLeft = _frameLeft.Value;
        o.FrameTop = _frameTop.Value;
        o.FrameRight = _frameRight.Value;
        o.FrameBottom = _frameBottom.Value;
        o.ZoomX = _zoomX.Value;
        o.ZoomY = _zoomY.Value;
        o.ZoomLinked = _zoomLinked.Checked;
        o.ZoomCenterX = _zoomCenterX.Value;
        o.ZoomCenterY = _zoomCenterY.Value;
        o.AutoCrop = _autoCrop.Checked;
        o.CaptureCursor = _cursor.Checked;
        o.CaptureMethod = (_captureMethod.SelectedItem as CaptureItem)?.Method ?? o.CaptureMethod;
        o.VSync = _vsync.Checked;
        o.Preview = _preview.Checked;
        o.PreviewWidth = (int)_previewWidth.Value;
        o.PreviewHeight = (int)_previewHeight.Value;

        Controller.OnOptionsEdited(o, save);
        UpdateFrameEditor();
        if (o.FrameMode != previousFrameMode)
            ShowFrameMode();
        if (!save)
            return; // dragging: the rest follows when released
        UpdateEnabledState();
        UpdatePlacement();
    }

    // ------------------------------------------------------------------ frame

    /// <summary>Shows the settings of the chosen crop mode, and the picture of the source for the rectangle.</summary>
    private void ShowFrameMode()
    {
        var item = _frameMode.SelectedItem as FrameItem ?? FrameModes[0];
        _frameCard.Description = item.Description;
        foreach (var (mode, panel) in _framePanels)
            panel.Visible = mode == item.Mode;
        UpdateFrameEditor();
        if (item.Mode == FrameMode.Rectangle)
            RefreshFramePicture();
    }

    private void UpdateFrameEditor()
    {
        var o = _options;
        _frameEditor.Frame = (o.FrameLeft, o.FrameTop, o.FrameRight, o.FrameBottom);
        _frameValues.Text = $"Haut-gauche ({o.FrameLeft:0.0} %, {o.FrameTop:0.0} %) · bas-droit ({o.FrameRight:0.0} %, {o.FrameBottom:0.0} %)";
    }

    /// <summary>Back to the whole picture, in the current crop mode only.</summary>
    private void ResetFrame()
    {
        switch ((_frameMode.SelectedItem as FrameItem)?.Mode ?? FrameMode.Margins)
        {
            case FrameMode.Margins:
                _cropLeft.Value = _cropTop.Value = _cropRight.Value = _cropBottom.Value = 0;
                break;
            case FrameMode.Corners:
            case FrameMode.Rectangle:
                _frameLeft.Value = _frameTop.Value = 0;
                _frameRight.Value = _frameBottom.Value = 100;
                break;
            case FrameMode.Zoom:
                _zoomX.Value = _zoomY.Value = 100;
                _zoomCenterX.Value = _zoomCenterY.Value = 50;
                break;
        }
        Changed();
    }

    /// <summary>The part of the screen captured: the source display, or the window chosen in "Placement" (before the frame).</summary>
    private Rectangle? SourceScreenArea()
    {
        var o = _options;
        if (MonitorInfo.Find(MonitorInfo.GetAll(), o.SourceDisplay) is { } display)
        {
            RECT bounds = display.Bounds;
            Rectangle region = Geometry.ComputeDisplayRegion(o, bounds.Width, bounds.Height);
            return new Rectangle(bounds.Left + region.X, bounds.Top + region.Y, region.Width, region.Height);
        }
        if (_placementWindow.SelectedItem is WindowInfo window && Dwmapi.TryGetFrameBounds(window.Handle, out RECT frame))
        {
            Rectangle region = Geometry.ComputeSourceRegion(window.Handle, o, frame.Width, frame.Height);
            return new Rectangle(frame.Left + region.X, frame.Top + region.Y, region.Width, region.Height);
        }
        return null;
    }

    /// <summary>
    /// A picture of the source under the rectangle, taken off the UI thread (GDI, what is on screen
    /// there), every second while the rectangle is shown and not dragged; nothing is taken while the
    /// page is hidden or the window minimized. A failed copy (locked session, UAC) keeps the last picture.
    /// </summary>
    private async void RefreshFramePicture()
    {
        if (_takingFramePicture || IsDisposed || !_frameEditor.Visible || _frameEditor.IsDragging
            || FindForm() is not { Visible: true, WindowState: not FormWindowState.Minimized })
            return;
        if (SourceScreenArea() is not { Width: > 0, Height: > 0 } area)
        {
            _frameEditor.SetPicture(null, new Size(4, 3));
            return;
        }
        Size size = ScreenSnapshot.Fit(area.Width, area.Height, _frameEditor.PictureBox);
        _takingFramePicture = true;
        try
        {
            var (picture, error) = await Task.Run<(Bitmap?, string?)>(() =>
            {
                try { return (ScreenSnapshot.Take(area, size), null); }
                catch (Exception ex) { return (null, ex.Message); }
            });
            if (error != null && error != _framePictureError)
                Log.Warn($"Picture of the source for the frame: {error}");
            _framePictureError = error;
            if (IsDisposed)
                picture?.Dispose();
            else if (picture != null)
                _frameEditor.SetPicture(picture, size);
        }
        finally
        {
            _takingFramePicture = false;
        }
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
        _clientArea.Enabled = _placementWindow.Enabled = _options.SourceDisplay.Length == 0;
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
        if (window == null && _options.SourceDisplay.Length > 0)
        {
            Controller.StartDisplay(_options.SourceDisplay, _options);
            return;
        }
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
        return MonitorInfo.Find(monitors, _options.Monitor)
            ?? MonitorInfo.AutoOutput(monitors, MonitorInfo.Find(monitors, _options.SourceDisplay),
                _placementWindow.SelectedItem is WindowInfo window ? MonitorInfo.FromWindow(window.Handle) : null);
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

    /// <summary>Where the picture of the captured display or of the chosen window lands with these settings.</summary>
    private void UpdatePlacement()
    {
        var o = _options;
        Rectangle source;
        if (MonitorInfo.Find(MonitorInfo.GetAll(), o.SourceDisplay) is { } display)
        {
            source = Geometry.ComputeDisplayRegion(o, display.Bounds.Width, display.Bounds.Height);
        }
        else if (_placementWindow.SelectedItem is WindowInfo window && Dwmapi.TryGetFrameBounds(window.Handle, out RECT frame))
        {
            source = Geometry.ComputeSourceRegion(window.Handle, o, frame.Width, frame.Height);
        }
        else
        {
            _placementCard.Description = "Choisissez une fenêtre pour voir où son image sera affichée";
            return;
        }
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
        Size frameSize = placement.Frame?.Size ?? source.Size;
        string framed = placement.Frame is { } f ? $", cadre {f.Width}x{f.Height} en ({f.X},{f.Y}) de la source" : "";
        string cut = placement.Barrel != null ? ", bords courbés"
            : placement.Source.Size != frameSize ? $", cadre rogné à {placement.Source.Width}x{placement.Source.Height}" : "";
        string interlaced = placement.Interlaced ? $", entrelacement simulé ({(placement.Source.Height + 1) / 2} lignes par trame)"
            : o.SimulatedInterlace ? $", entrelacement simulé sans effet ({placement.Source.Height} lignes de source pour {dest.Height:0} à l'écran)"
            : "";
        string mirror = (o.MirrorX, o.MirrorY) switch
        {
            (true, true) => ", miroir X et Y",
            (true, false) => ", miroir X",
            (false, true) => ", miroir Y",
            _ => "",
        };
        _placementCard.Description = $"Source {source.Width}x{source.Height} → écran {output.Width}x{output.Height} (forme {shape:0.###}) : "
                                   + $"image {dest.Width:0}x{dest.Height:0} en ({dest.X:0},{dest.Y:0}), {bars}{framed}{cut}{interlaced}{mirror}"
                                   + (o.AutoCrop ? " — avant recadrage auto" : "");
    }
}
