using System.Diagnostics;
using SuperResScaler.App;
using SuperResScaler.Config;
using SuperResScaler.Native;
using SuperResScaler.Rendering;

namespace SuperResScaler.UI;

/// <summary>
/// Main window: pick a window, edit the default options or a per-application profile
/// (changes are saved immediately and applied live), start/stop scaling.
/// </summary>
internal sealed class MainForm : Form
{
    private sealed record ProfileItem(ScalingOptions Options, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ModeItem(ScaleMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record MonitorItem(int Index, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ScalerController _controller;
    private readonly bool _startHidden;
    private readonly NotifyIcon _tray;

    private readonly ComboBox _windowCombo = DropDown(460);
    private readonly Button _refreshButton = Btn("Actualiser");
    private readonly ComboBox _profileCombo = DropDown(300);
    private readonly Button _newProfileButton = Btn("Créer un profil pour cette application");
    private readonly Button _deleteProfileButton = Btn("Supprimer");
    private readonly CheckBox _autoScaleCheck = Check("Démarrer automatiquement quand l'application passe au premier plan");
    private readonly Label _profileMatchLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    private readonly ComboBox _shaderCombo = DropDown(170);
    private readonly ComboBox _modeCombo = DropDown(200);
    private readonly ComboBox _monitorCombo = DropDown(250);
    private readonly TextBox _displayAspectBox = new() { Width = 70 };
    private readonly TextBox _sourceAspectBox = new() { Width = 70 };
    private readonly CheckBox _clientAreaCheck = Check("Zone client uniquement (sans barre de titre ni bordures)");
    private readonly CheckBox _autoCropCheck = Check("Recadrage automatique des bandes noires");
    private readonly CheckBox _cursorCheck = Check("Capturer le curseur");
    private readonly CheckBox _vsyncCheck = Check("VSync");
    private readonly CheckBox _previewCheck = Check("Prévisualisation fenêtrée");
    private readonly NumericUpDown _previewWidth = Number(64, 7680, 70);
    private readonly NumericUpDown _previewHeight = Number(16, 4320, 70);
    private readonly NumericUpDown _cropLeft = Number(0, 4000, 60);
    private readonly NumericUpDown _cropTop = Number(0, 4000, 60);
    private readonly NumericUpDown _cropRight = Number(0, 4000, 60);
    private readonly NumericUpDown _cropBottom = Number(0, 4000, 60);

    private readonly Button _startButton = Btn("Mettre à l'échelle la fenêtre sélectionnée");
    private readonly Button _startDelayedButton = Btn("");
    private readonly Button _stopButton = Btn("Arrêter");
    private readonly Button _folderButton = Btn("Dossier (réglages, shaders, log)");
    private readonly Label _statusLabel = new() { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold) };
    private readonly Label _hotkeyLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    private ScalingOptions _editing;
    private bool _loading;

    public MainForm(ScalerController controller, bool startHidden)
    {
        _controller = controller;
        _startHidden = startHidden;
        _editing = controller.Settings.Defaults;

        SuspendLayout();
        Text = "SuperResScaler";
        Icon = AppIcon.Get();
        // Sizes below are in 96-DPI pixels; WinForms scales them to the monitor DPI.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(8);

        _tray = CreateTrayIcon();
        BuildLayout();
        PopulateLists();
        WireEvents();
        LoadOptionsIntoControls();
        UpdateState();
        ResumeLayout(false);
        PerformLayout();

        var errors = controller.RegisterHotkeys();
        controller.EnableAutoScale();
        var hotkeys = controller.Settings.Hotkeys;
        _hotkeyLabel.Text = $"Raccourcis : {hotkeys.ToggleScaling} = démarrer/arrêter sur la fenêtre active   ·   "
                          + $"{hotkeys.NextShader} = shader suivant   ·   {hotkeys.ResetAutoCrop} = refaire le recadrage auto";
        SetStatus(errors.Count > 0 ? string.Join(" ; ", errors) : controller.Store.LoadError ?? "Prêt");
    }

    // ---------------------------------------------------------------- layout

    private void BuildLayout()
    {
        var root = Grid(1);

        var source = Grid(3);
        source.Controls.Add(Caption("Fenêtre"), 0, 0);
        source.Controls.Add(_windowCombo, 1, 0);
        source.Controls.Add(_refreshButton, 2, 0);
        root.Controls.Add(Group("Source", source));

        var profile = Grid(4);
        profile.Controls.Add(Caption("Réglages"), 0, 0);
        profile.Controls.Add(_profileCombo, 1, 0);
        profile.Controls.Add(_newProfileButton, 2, 0);
        profile.Controls.Add(_deleteProfileButton, 3, 0);
        profile.Controls.Add(_profileMatchLabel, 1, 1);
        profile.SetColumnSpan(_profileMatchLabel, 3);
        profile.Controls.Add(_autoScaleCheck, 1, 2);
        profile.SetColumnSpan(_autoScaleCheck, 3);
        root.Controls.Add(Group("Profil (par défaut ou par application)", profile));

        var render = Grid(4);
        render.Controls.Add(Caption("Shader"), 0, 0);
        render.Controls.Add(_shaderCombo, 1, 0);
        render.Controls.Add(Caption("Écran de sortie"), 2, 0);
        render.Controls.Add(_monitorCombo, 3, 0);
        render.Controls.Add(Caption("Mode"), 0, 1);
        render.Controls.Add(_modeCombo, 1, 1);
        render.Controls.Add(Caption("Ratio de l'écran"), 2, 1);
        render.Controls.Add(Row(_displayAspectBox, Hint("ex. 4:3 pour un CRT en 1920x240")), 3, 1);
        render.Controls.Add(Caption("Ratio de la source"), 2, 2);
        render.Controls.Add(Row(_sourceAspectBox, Hint("auto, 4:3, 16:9… (mode ratio)")), 3, 2);
        var checks = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) };
        checks.Controls.AddRange([_clientAreaCheck, _autoCropCheck, _cursorCheck, _vsyncCheck,
            Row(_previewCheck, _previewWidth, Hint("x"), _previewHeight, Hint("(test sur un écran PC, au lieu du plein écran)"))]);
        render.Controls.Add(checks, 0, 3);
        render.SetColumnSpan(checks, 4);
        root.Controls.Add(Group("Rendu", render));

        root.Controls.Add(Group("Recadrage manuel (pixels)",
            Row(Hint("Gauche"), _cropLeft, Hint("Haut"), _cropTop, Hint("Droite"), _cropRight, Hint("Bas"), _cropBottom)));

        root.Controls.Add(Row(_startButton, _startDelayedButton, _stopButton, _folderButton));
        root.Controls.Add(_statusLabel);
        root.Controls.Add(_hotkeyLabel);
        Controls.Add(root);
    }

    private static TableLayoutPanel Grid(int columns) => new()
    {
        ColumnCount = columns,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Fill,
    };

    private static GroupBox Group(string title, Control content)
    {
        var box = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Padding = new Padding(8) };
        content.Dock = DockStyle.Fill;
        box.Controls.Add(content);
        return box;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        row.Controls.AddRange(controls);
        return row;
    }

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };
    private static Label Hint(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 3), ForeColor = SystemColors.GrayText };
    private static ComboBox DropDown(int width) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true };
    private static Button Btn(string text) => new() { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 2, 6, 2) };
    private static NumericUpDown Number(int min, int max, int width) => new() { Minimum = min, Maximum = max, Width = width };

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Afficher", null, (_, _) => ShowFromTray());
        menu.Items.Add("Arrêter la mise à l'échelle", null, (_, _) => _controller.StopScaling());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => Close());
        var tray = new NotifyIcon { Icon = AppIcon.Get(), Text = "SuperResScaler", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowFromTray();
        return tray;
    }

    // ---------------------------------------------------------------- data

    private void PopulateLists()
    {
        _modeCombo.Items.AddRange([
            new ModeItem(ScaleMode.Stretch, "Étirer (remplir tout l'écran)"),
            new ModeItem(ScaleMode.KeepAspect, "Conserver le ratio d'aspect"),
        ]);

        _monitorCombo.Items.Add(new MonitorItem(0, "Auto (écran de la fenêtre source)"));
        foreach (var monitor in MonitorInfo.GetAll())
            _monitorCombo.Items.Add(new MonitorItem(monitor.Index, monitor.ToString()));

        _shaderCombo.Items.AddRange(ShaderLibrary.List().ToArray<object>());
        _startDelayedButton.Text = $"Fenêtre active dans {_controller.Settings.ScaleDelaySeconds} s";

        RefreshProfiles(_editing);
        RefreshWindows();
    }

    private void RefreshWindows()
    {
        IntPtr selected = (_windowCombo.SelectedItem as WindowInfo)?.Handle ?? IntPtr.Zero;
        _windowCombo.BeginUpdate();
        _windowCombo.Items.Clear();
        foreach (var window in WindowInfo.EnumerateCandidates())
        {
            int index = _windowCombo.Items.Add(window);
            if (window.Handle == selected)
                _windowCombo.SelectedIndex = index;
        }
        _windowCombo.EndUpdate();
    }

    private void RefreshProfiles(ScalingOptions select)
    {
        _loading = true;
        _profileCombo.Items.Clear();
        _profileCombo.Items.Add(new ProfileItem(_controller.Settings.Defaults, "(Par défaut — fenêtres sans profil)"));
        foreach (var profile in _controller.Settings.Profiles)
            _profileCombo.Items.Add(new ProfileItem(profile, $"Profil : {profile.DisplayName}"));
        _profileCombo.SelectedItem = _profileCombo.Items.Cast<ProfileItem>().FirstOrDefault(i => ReferenceEquals(i.Options, select))
                                     ?? _profileCombo.Items[0];
        _loading = false;
        _editing = ((ProfileItem)_profileCombo.SelectedItem!).Options;
    }

    private void LoadOptionsIntoControls()
    {
        _loading = true;
        var o = _editing;
        if (!_shaderCombo.Items.Contains(o.Shader))
            _shaderCombo.Items.Add(o.Shader);
        _shaderCombo.SelectedItem = o.Shader;
        _modeCombo.SelectedItem = _modeCombo.Items.Cast<ModeItem>().First(m => m.Mode == o.Mode);
        _monitorCombo.SelectedItem = _monitorCombo.Items.Cast<MonitorItem>().FirstOrDefault(m => m.Index == o.Monitor) ?? _monitorCombo.Items[0];
        _displayAspectBox.Text = o.DisplayAspect;
        _sourceAspectBox.Text = o.SourceAspect;
        _clientAreaCheck.Checked = o.ClientAreaOnly;
        _autoCropCheck.Checked = o.AutoCrop;
        _cursorCheck.Checked = o.CaptureCursor;
        _vsyncCheck.Checked = o.VSync;
        _previewCheck.Checked = o.Preview;
        _previewWidth.Value = Math.Clamp(o.PreviewWidth, (int)_previewWidth.Minimum, (int)_previewWidth.Maximum);
        _previewHeight.Value = Math.Clamp(o.PreviewHeight, (int)_previewHeight.Minimum, (int)_previewHeight.Maximum);
        _cropLeft.Value = Math.Clamp(o.CropLeft, 0, 4000);
        _cropTop.Value = Math.Clamp(o.CropTop, 0, 4000);
        _cropRight.Value = Math.Clamp(o.CropRight, 0, 4000);
        _cropBottom.Value = Math.Clamp(o.CropBottom, 0, 4000);

        var profile = o as Profile;
        _autoScaleCheck.Enabled = profile != null;
        _autoScaleCheck.Checked = profile?.AutoScale ?? false;
        _deleteProfileButton.Enabled = profile != null;
        _profileMatchLabel.Text = profile == null
            ? "Utilisés pour toute fenêtre qui n'a pas de profil."
            : $"S'applique aux fenêtres de {profile.ProcessName}" + (string.IsNullOrEmpty(profile.TitleContains) ? "" : $" dont le titre contient « {profile.TitleContains} »");
        _displayAspectBox.BackColor = _sourceAspectBox.BackColor = SystemColors.Window;
        _loading = false;
    }

    private void SaveControlsIntoOptions()
    {
        if (_loading)
            return;
        var o = _editing;
        o.Shader = _shaderCombo.SelectedItem as string ?? o.Shader;
        o.Mode = (_modeCombo.SelectedItem as ModeItem)?.Mode ?? o.Mode;
        o.Monitor = (_monitorCombo.SelectedItem as MonitorItem)?.Index ?? 0;
        if (AspectRatio.TryParse(_displayAspectBox.Text, out _))
            o.DisplayAspect = _displayAspectBox.Text.Trim();
        if (AspectRatio.IsAuto(_sourceAspectBox.Text) || AspectRatio.TryParse(_sourceAspectBox.Text, out _))
            o.SourceAspect = string.IsNullOrWhiteSpace(_sourceAspectBox.Text) ? "auto" : _sourceAspectBox.Text.Trim();
        _displayAspectBox.BackColor = AspectRatio.TryParse(_displayAspectBox.Text, out _) ? SystemColors.Window : Color.MistyRose;
        _sourceAspectBox.BackColor = AspectRatio.IsAuto(_sourceAspectBox.Text) || AspectRatio.TryParse(_sourceAspectBox.Text, out _)
            ? SystemColors.Window : Color.MistyRose;
        o.ClientAreaOnly = _clientAreaCheck.Checked;
        o.AutoCrop = _autoCropCheck.Checked;
        o.CaptureCursor = _cursorCheck.Checked;
        o.VSync = _vsyncCheck.Checked;
        o.Preview = _previewCheck.Checked;
        o.PreviewWidth = (int)_previewWidth.Value;
        o.PreviewHeight = (int)_previewHeight.Value;
        o.CropLeft = (int)_cropLeft.Value;
        o.CropTop = (int)_cropTop.Value;
        o.CropRight = (int)_cropRight.Value;
        o.CropBottom = (int)_cropBottom.Value;
        if (o is Profile profile)
            profile.AutoScale = _autoScaleCheck.Checked;
        _controller.OnOptionsEdited(o);
    }

    // ---------------------------------------------------------------- events

    private void WireEvents()
    {
        _refreshButton.Click += (_, _) => RefreshWindows();
        _windowCombo.DropDown += (_, _) => RefreshWindows();
        _windowCombo.SelectedIndexChanged += (_, _) => SelectProfileForWindow();
        _profileCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _profileCombo.SelectedItem is not ProfileItem item)
                return;
            _editing = item.Options;
            LoadOptionsIntoControls();
        };
        _newProfileButton.Click += (_, _) => CreateProfile();
        _deleteProfileButton.Click += (_, _) => DeleteProfile();

        foreach (var combo in new[] { _shaderCombo, _modeCombo, _monitorCombo })
            combo.SelectedIndexChanged += (_, _) => SaveControlsIntoOptions();
        foreach (var box in new[] { _displayAspectBox, _sourceAspectBox })
            box.TextChanged += (_, _) => SaveControlsIntoOptions();
        foreach (var check in new[] { _clientAreaCheck, _autoCropCheck, _cursorCheck, _vsyncCheck, _previewCheck, _autoScaleCheck })
            check.CheckedChanged += (_, _) => SaveControlsIntoOptions();
        foreach (var number in new[] { _previewWidth, _previewHeight, _cropLeft, _cropTop, _cropRight, _cropBottom })
            number.ValueChanged += (_, _) => SaveControlsIntoOptions();

        _startButton.Click += (_, _) =>
        {
            if (_windowCombo.SelectedItem is WindowInfo window)
                _controller.StartScaling(window.Handle, _editing);
            else
                SetStatus("Choisissez d'abord une fenêtre dans la liste");
        };
        _startDelayedButton.Click += (_, _) => _controller.ScaleForegroundAfterDelay(_controller.Settings.ScaleDelaySeconds);
        _stopButton.Click += (_, _) => _controller.StopScaling();
        _folderButton.Click += (_, _) => Process.Start(new ProcessStartInfo(_controller.Store.Directory) { UseShellExecute = true });

        _controller.StateChanged += UpdateState;
        _controller.Message += SetStatus;
        _controller.OptionsChanged += options =>
        {
            if (ReferenceEquals(options, _editing))
                LoadOptionsIntoControls();
        };
    }

    private void SelectProfileForWindow()
    {
        if (_windowCombo.SelectedItem is not WindowInfo window)
            return;
        ScalingOptions options = _controller.Settings.ResolveOptions(window);
        if (!ReferenceEquals(options, _editing))
        {
            RefreshProfiles(options);
            LoadOptionsIntoControls();
        }
    }

    private void CreateProfile()
    {
        if (_windowCombo.SelectedItem is not WindowInfo window || string.IsNullOrEmpty(window.ProcessName))
        {
            SetStatus("Choisissez d'abord la fenêtre de l'application");
            return;
        }
        var settings = _controller.Settings;
        var profile = settings.Profiles.FirstOrDefault(p => string.Equals(p.ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
        {
            profile = new Profile { Name = window.ProcessName, ProcessName = window.ProcessName };
            profile.CopyFrom(_editing);
            settings.Profiles.Add(profile);
            _controller.Store.Save();
            SetStatus($"Profil créé pour {window.ProcessName}");
        }
        RefreshProfiles(profile);
        LoadOptionsIntoControls();
    }

    private void DeleteProfile()
    {
        if (_editing is not Profile profile)
            return;
        if (MessageBox.Show(this, $"Supprimer le profil {profile.DisplayName} ?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _controller.Settings.Profiles.Remove(profile);
        _controller.Store.Save();
        RefreshProfiles(_controller.Settings.Defaults);
        LoadOptionsIntoControls();
    }

    private void UpdateState()
    {
        _stopButton.Enabled = _controller.IsScaling;
        _tray.Text = _controller.ScaledWindow is { } window
            ? Truncate($"SuperResScaler — {window.Title}", 63)
            : "SuperResScaler";
    }

    private void SetStatus(string message) => _statusLabel.Text = message;

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    // ---------------------------------------------------------------- tray

    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden && !IsHandleCreated)
        {
            CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
            Hide();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _controller.StopScaling();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosed(e);
    }
}
