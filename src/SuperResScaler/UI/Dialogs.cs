using SuperResScaler.Config;
using SuperResScaler.Native;

namespace SuperResScaler.UI;

/// <summary>Base of the small modal dialogs: a padded column of rows, OK / Cancel at the bottom.</summary>
internal abstract class DialogBase : Form
{
    private readonly TableLayoutPanel _layout;
    protected readonly Button OkButton;

    protected DialogBase(string title)
    {
        Text = title;
        Icon = App.AppIcon.Get();
        AutoScaleMode = AutoScaleMode.None;
        Font = Theme.Body;
        BackColor = Theme.PageBackground;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(Theme.S(20));

        _layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
        OkButton = UiFactory.AccentButton("OK", (_, _) => Accept());
        var cancel = UiFactory.Button("Annuler");
        cancel.DialogResult = DialogResult.Cancel;
        CancelButton = cancel;
        AcceptButton = OkButton;
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(16), 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(OkButton);
        Controls.Add(_layout);
        Load += (_, _) =>
        {
            _layout.Controls.Add(buttons);
            _layout.SetColumnSpan(buttons, 2);
        };
    }

    protected void AddRow(string caption, Control control, string? hint = null)
    {
        _layout.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(0, Theme.S(9), Theme.S(12), 0) });
        if (hint == null)
        {
            _layout.Controls.Add(control);
            return;
        }
        var column = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        column.Controls.Add(control);
        column.Controls.Add(new Label { Text = hint, AutoSize = true, Font = Theme.Caption, ForeColor = Theme.SecondaryText, MaximumSize = new Size(Theme.S(460), 0) });
        _layout.Controls.Add(column);
    }

    protected void AddWide(Control control)
    {
        _layout.Controls.Add(control);
        _layout.SetColumnSpan(control, 2);
    }

    /// <summary>Validates and closes with OK; returns false to keep the dialog open.</summary>
    protected abstract bool TryAccept(out string? error);

    private void Accept()
    {
        if (!TryAccept(out string? error))
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>The visible windows of other applications, largest first.</summary>
    protected static List<WindowInfo> RunningWindows()
        => WindowInfo.EnumerateCandidates().OrderByDescending(w => w.ClientArea).ToList();
}

/// <summary>"Nouveau profil", as Magpie's: name, the application's window, settings copied from.</summary>
internal sealed class NewProfileDialog : DialogBase
{
    private sealed record Source(ScalingOptions Options, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly TextBox _name = UiFactory.TextBox(320);
    private readonly ComboBox _window = UiFactory.DropDown(420);
    private readonly TextBox _process = UiFactory.TextBox(320);
    private readonly TextBox _title = UiFactory.TextBox(320);
    private readonly ComboBox _copyFrom = UiFactory.DropDown(320);
    private readonly AppSettings _settings;
    private string _suggestedName = string.Empty;

    public NewProfileDialog(AppSettings settings, WindowInfo? preselect = null) : base("Nouveau profil")
    {
        _settings = settings;
        _window.Items.Add("(aucune — saisir le processus à la main)");
        foreach (var window in RunningWindows())
            _window.Items.Add(window);
        _window.SelectedIndexChanged += (_, _) => OnWindowSelected();
        _window.DropDownWidth = Theme.S(560);

        _copyFrom.Items.Add(new Source(settings.Defaults, "Par défaut"));
        foreach (var profile in settings.Profiles)
            _copyFrom.Items.Add(new Source(profile, profile.DisplayName));
        _copyFrom.SelectedIndex = 0;

        AddRow("Fenêtre", _window, "Choisissez la fenêtre du jeu en cours d'exécution : son processus est repris.");
        AddRow("Processus", _process, "Ex. game.exe — jokers * et ?, alternatives séparées par | (comme BackForceFeeder).");
        AddRow("Titre de fenêtre", _title, "Facultatif : texte contenu dans le titre, ou motif avec * ? et |.");
        AddRow("Nom", _name);
        AddRow("Copier depuis", _copyFrom);

        _window.SelectedIndex = preselect == null ? 0 : Math.Max(0, _window.Items.IndexOf(_window.Items.OfType<WindowInfo>().FirstOrDefault(w => w.Handle == preselect.Handle)));
    }

    public Profile? Result { get; private set; }

    private void OnWindowSelected()
    {
        if (_window.SelectedItem is not WindowInfo window)
            return;
        _process.Text = window.ProcessName;
        if (string.IsNullOrWhiteSpace(_name.Text) || _name.Text == _suggestedName)
        {
            _suggestedName = string.IsNullOrWhiteSpace(window.Title) ? Path.GetFileNameWithoutExtension(window.ProcessName) : window.Title;
            _name.Text = _suggestedName;
        }
    }

    protected override bool TryAccept(out string? error)
    {
        error = null;
        string process = _process.Text.Trim();
        if (process.Length == 0)
        {
            error = "Indiquez le processus du jeu (ex. game.exe), ou choisissez sa fenêtre.";
            return false;
        }
        string name = string.IsNullOrWhiteSpace(_name.Text) ? Path.GetFileNameWithoutExtension(process) : _name.Text.Trim();
        if (_settings.Profiles.Any(p => string.Equals(p.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"Un profil s'appelle déjà « {name} ».";
            return false;
        }
        var profile = new Profile
        {
            Name = name,
            ProcessName = process,
            WindowTitle = string.IsNullOrWhiteSpace(_title.Text) ? null : _title.Text.Trim(),
        };
        profile.CopyFrom(((Source)_copyFrom.SelectedItem!).Options);
        Result = profile;
        return true;
    }
}

/// <summary>Picks one of the running windows (to fill a profile's process and title).</summary>
internal sealed class WindowPickerDialog : DialogBase
{
    private readonly ListBox _list = new() { Width = Theme.S(560), Height = Theme.S(300), Font = Theme.Body, IntegralHeight = false };

    public WindowPickerDialog() : base("Choisir une fenêtre")
    {
        foreach (var window in RunningWindows())
            _list.Items.Add(window);
        if (_list.Items.Count > 0)
            _list.SelectedIndex = 0;
        _list.DoubleClick += (_, _) => OkButton.PerformClick();
        AddWide(new Label { Text = "Fenêtres ouvertes (le jeu doit être lancé) :", AutoSize = true });
        AddWide(_list);
    }

    public WindowInfo? Result { get; private set; }

    protected override bool TryAccept(out string? error)
    {
        Result = _list.SelectedItem as WindowInfo;
        error = Result == null ? "Choisissez une fenêtre." : null;
        return Result != null;
    }
}

/// <summary>Records a new hotkey: the user presses the combination.</summary>
internal sealed class HotkeyDialog : DialogBase
{
    private readonly KeyChips _chips = new();
    private readonly Label _hint = new() { AutoSize = true, ForeColor = Theme.SecondaryText, Text = "Appuyez sur la combinaison (au moins une touche Ctrl, Alt, Shift ou Win)." };

    public HotkeyDialog(string current) : base("Raccourci")
    {
        Result = current;
        _chips.Hotkey = current;
        KeyPreview = true;
        AddWide(_hint);
        AddWide(_chips);
        AddWide(UiFactory.Button("Désactiver ce raccourci", (_, _) => { Result = string.Empty; _chips.Hotkey = string.Empty; }));
    }

    public string Result { get; private set; }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        bool modifierOnly = key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None;
        bool hasModifier = (keyData & (Keys.Control | Keys.Alt | Keys.Shift)) != 0 || IsWinDown();
        if (!modifierOnly && hasModifier)
        {
            var parts = new List<string>();
            if ((keyData & Keys.Control) != 0) parts.Add("Ctrl");
            if ((keyData & Keys.Alt) != 0) parts.Add("Alt");
            if ((keyData & Keys.Shift) != 0) parts.Add("Shift");
            if (IsWinDown()) parts.Add("Win");
            parts.Add(key is >= Keys.D0 and <= Keys.D9 ? ((int)key - (int)Keys.D0).ToString() : key.ToString());
            Result = string.Join("+", parts);
            _chips.Hotkey = Result;
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static bool IsWinDown() => (Control.ModifierKeys & Keys.LWin) != 0 || GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    protected override bool TryAccept(out string? error)
    {
        error = null;
        if (Result.Length > 0 && !App.HotkeyManager.TryParse(Result, out _, out _))
        {
            error = $"Raccourci invalide : {Result}";
            return false;
        }
        return true;
    }
}
