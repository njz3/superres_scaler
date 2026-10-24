using SuperResScaler.App;
using SuperResScaler.Config;
using SuperResScaler.UI.Pages;

namespace SuperResScaler.UI;

/// <summary>
/// Main window laid out like Magpie: navigation pane (Accueil, Profils with Par défaut and one
/// entry per game, Nouveau profil, then À propos and Paramètres) and the selected page.
/// Minimizing or closing sends it to the tray, where the profiles are watched: the scaling
/// starts by itself when a game window matches a profile.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly ScalerController _controller;
    private readonly bool _startHidden;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _trayWatch;
    private readonly ToolStripMenuItem _trayStop;
    private readonly FlowLayoutPanel _navTop;
    private readonly FlowLayoutPanel _navBottom;
    private readonly Panel _content;
    private readonly HomePage _home;
    private readonly SettingsPage _settings;
    private readonly AboutPage _about;
    private Page? _current;
    private bool _exiting;
    private bool _trayHintShown;

    public MainForm(ScalerController controller, bool startHidden)
    {
        _controller = controller;
        _startHidden = startHidden;
        Theme.Dpi = DeviceDpi;

        Text = "SuperResScaler";
        Icon = AppIcon.Get();
        AutoScaleMode = AutoScaleMode.None;
        Font = Theme.Body;
        BackColor = Theme.PageBackground;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(Theme.S(1120), Theme.S(780));
        MinimumSize = new Size(Theme.S(860), Theme.S(560));

        _content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.PageBackground };
        var nav = new Panel { Dock = DockStyle.Left, Width = Theme.S(272), BackColor = Theme.NavBackground, Padding = new Padding(Theme.S(4), Theme.S(8), Theme.S(4), Theme.S(8)) };
        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = Theme.S(44),
            WrapContents = false,
            BackColor = Theme.NavBackground,
            Padding = new Padding(Theme.S(14), Theme.S(10), 0, 0),
        };
        header.Controls.Add(new PictureBox
        {
            Image = new Bitmap(AppIcon.Get().ToBitmap(), Theme.S(20), Theme.S(20)),
            Size = new Size(Theme.S(20), Theme.S(20)),
            Margin = new Padding(0, Theme.S(1), Theme.S(10), 0),
        });
        header.Controls.Add(new Label { Text = "SuperResScaler", Font = Theme.AppTitle, ForeColor = Theme.Text, AutoSize = true, Margin = Padding.Empty });
        _navTop = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.NavBackground };
        _navBottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Theme.NavBackground };
        _navTop.SizeChanged += (_, _) => FitNavItems(_navTop);
        _navBottom.SizeChanged += (_, _) => FitNavItems(_navBottom);
        nav.Controls.Add(_navTop);
        nav.Controls.Add(_navBottom);
        nav.Controls.Add(header);
        Controls.Add(_content);
        Controls.Add(nav);

        _home = new HomePage(controller);
        _settings = new SettingsPage(controller);
        _settings.WatchingSettingsChanged += UpdateWatching;
        _about = new AboutPage(controller);

        _trayWatch = new ToolStripMenuItem("Surveiller les profils", null, (_, _) => ToggleWatching()) { CheckOnClick = false };
        _trayStop = new ToolStripMenuItem("Arrêter la mise à l'échelle", null, (_, _) => _controller.StopScaling());
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Ouvrir SuperResScaler", null, (_, _) => ShowFromTray()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(_trayWatch);
        menu.Items.Add(_trayStop);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => Exit());
        menu.Opening += (_, _) => { _trayWatch.Checked = _controller.Settings.AutoScaleEnabled; _trayStop.Enabled = _controller.IsScaling; };
        _tray = new NotifyIcon { Icon = AppIcon.Get(), Text = "SuperResScaler", ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };

        BuildNavigation(_home);
        Navigate(_home, FindNavItem(_home));

        controller.StateChanged += OnControllerStateChanged;
        controller.Message += _ => _current?.OnStateChanged();
        controller.Watcher.StateChanged += OnControllerStateChanged;
        controller.OptionsChanged += options =>
        {
            if (_current is ProfilePage page && ReferenceEquals(page.Options, options))
                page.OnShown();
        };
        foreach (string error in controller.RegisterHotkeys())
            controller.Report(error);
        if (controller.Store.LoadError != null)
            controller.Report(controller.Store.LoadError);
    }

    // ------------------------------------------------------------------ navigation

    private void BuildNavigation(object? select)
    {
        foreach (Control control in _navTop.Controls.Cast<Control>().Concat(_navBottom.Controls.Cast<Control>()).ToList())
            control.Dispose();
        _navTop.Controls.Clear();
        _navBottom.Controls.Clear();

        AddNav(_navTop, new NavItem(Theme.GlyphHome, "Accueil") { Target = _home });
        _navTop.Controls.Add(new Label
        {
            Text = "Profils",
            Font = Theme.Body,
            ForeColor = Theme.SecondaryText,
            AutoSize = true,
            Margin = new Padding(Theme.S(18), Theme.S(14), 0, Theme.S(6)),
        });
        AddNav(_navTop, new NavItem(Theme.GlyphLayers, "Par défaut") { Target = _controller.Settings.Defaults });
        foreach (var profile in _controller.Settings.Profiles)
            AddNav(_navTop, new NavItem(Theme.GlyphGame, profile.DisplayName) { Target = profile });
        AddNav(_navTop, new NavItem(Theme.GlyphAdd, "Nouveau profil") { Target = "new" });

        AddNav(_navBottom, new NavItem(Theme.GlyphInfo, "À propos") { Target = _about });
        AddNav(_navBottom, new NavItem(Theme.GlyphSettings, "Paramètres") { Target = _settings });
        FitNavItems(_navTop);
        FitNavItems(_navBottom);

        if (FindNavItem(select) is { } item)
            item.Selected = true;
    }

    private void AddNav(FlowLayoutPanel panel, NavItem item)
    {
        item.Click += (_, _) => OnNavClick(item);
        panel.Controls.Add(item);
    }

    private static void FitNavItems(FlowLayoutPanel panel)
    {
        int width = panel.ClientSize.Width - Theme.S(10);
        foreach (var item in panel.Controls.OfType<NavItem>())
            item.Width = Math.Max(Theme.S(120), width);
    }

    private NavItem? FindNavItem(object? target)
        => _navTop.Controls.OfType<NavItem>().Concat(_navBottom.Controls.OfType<NavItem>())
                  .FirstOrDefault(i => ReferenceEquals(i.Target, target));

    private void OnNavClick(NavItem item)
    {
        switch (item.Target)
        {
            case "new":
                CreateProfile();
                break;
            case Page page:
                Navigate(page, item);
                break;
            case ScalingOptions options:
                ShowProfile(options);
                break;
        }
    }

    private void Navigate(Page page, NavItem? item)
    {
        if (_current is ProfilePage old && old != page)
            old.Dispose();
        _content.SuspendLayout();
        _content.Controls.Clear();
        _content.AutoScrollPosition = Point.Empty;
        _content.Controls.Add(page);
        _content.ResumeLayout();
        _current = page;
        page.OnShown();
        foreach (var nav in _navTop.Controls.OfType<NavItem>().Concat(_navBottom.Controls.OfType<NavItem>()))
            nav.Selected = nav == item;
    }

    private void ShowProfile(ScalingOptions options)
    {
        var page = new ProfilePage(_controller, options);
        page.Renamed += profile =>
        {
            if (FindNavItem(profile) is { } item)
            {
                item.Text = profile.DisplayName;
                item.Invalidate();
            }
        };
        page.Deleted += DeleteProfile;
        page.Duplicated += DuplicateProfile;
        Navigate(page, FindNavItem(options));
    }

    // ------------------------------------------------------------------ profiles

    private void CreateProfile()
    {
        using var dialog = new NewProfileDialog(_controller.Settings);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is not { } profile)
            return;
        AddProfile(profile);
    }

    private void DuplicateProfile(Profile source)
    {
        var copy = source.Clone();
        string baseName = $"{source.DisplayName} (copie)";
        copy.Name = baseName;
        for (int i = 2; _controller.Settings.Profiles.Any(p => string.Equals(p.DisplayName, copy.Name, StringComparison.OrdinalIgnoreCase)); i++)
            copy.Name = $"{baseName} {i}";
        AddProfile(copy);
    }

    private void AddProfile(Profile profile)
    {
        _controller.Settings.Profiles.Add(profile);
        _controller.Store.Save();
        _controller.Report($"Profil {profile.DisplayName} créé ({profile.ProcessName})");
        BuildNavigation(profile);
        ShowProfile(profile);
    }

    private void DeleteProfile(Profile profile)
    {
        _controller.Settings.Profiles.Remove(profile);
        _controller.Store.Save();
        _controller.Report($"Profil {profile.DisplayName} supprimé");
        var defaults = _controller.Settings.Defaults;
        BuildNavigation(defaults);
        ShowProfile(defaults);
    }

    // ------------------------------------------------------------------ state, tray, watching

    private void OnControllerStateChanged()
    {
        _current?.OnStateChanged();
        string text = _controller.ScaledWindow is { } window
            ? $"SuperResScaler — {window.Title}"
            : _controller.Watcher.Active ? "SuperResScaler — surveillance des profils" : "SuperResScaler";
        _tray.Text = text.Length <= 63 ? text : text[..62] + "…";
    }

    /// <summary>The profiles are watched while the window is in the tray or minimized (or always, if set so).</summary>
    private void UpdateWatching()
    {
        _controller.Watcher.Active = !Visible || WindowState == FormWindowState.Minimized || _controller.Settings.WatchWhileVisible;
        OnControllerStateChanged();
    }

    private void ToggleWatching()
    {
        _controller.Settings.AutoScaleEnabled = !_controller.Settings.AutoScaleEnabled;
        _controller.Store.Save();
        _controller.Watcher.Restart();
        _controller.Report(_controller.Settings.AutoScaleEnabled ? "Surveillance des profils activée" : "Surveillance des profils désactivée");
        if (_current == _home || _current == _settings)
            _current.OnShown();
        OnControllerStateChanged();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden && !IsHandleCreated)
        {
            // Started in the tray: no Load / VisibleChanged until the window is first shown.
            CreateHandle();
            value = false;
            base.SetVisibleCore(false);
            UpdateWatching();
            return;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        UpdateWatching();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateWatching();
        if (Visible)
            _current?.OnShown();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && Visible)
            HideToTray();
    }

    private void HideToTray()
    {
        Hide();
        UpdateWatching();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _tray.ShowBalloonTip(3000, "SuperResScaler",
                _controller.Settings.AutoScaleEnabled
                    ? "Toujours actif dans la zone de notification : les profils sont surveillés."
                    : "Toujours actif dans la zone de notification.",
                ToolTipIcon.Info);
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        UpdateWatching();
    }

    private void Exit()
    {
        _exiting = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing && _controller.Settings.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _controller.Watcher.Active = false;
        _controller.StopScaling();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosed(e);
    }
}
