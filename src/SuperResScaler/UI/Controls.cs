using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SuperResScaler.UI;

/// <summary>An entry of the navigation pane: icon, text, accent bar when selected.</summary>
internal sealed class NavItem : Control
{
    private bool _selected;
    private bool _hover;

    public NavItem(string glyph, string text)
    {
        Glyph = glyph;
        Text = text;
        Font = Theme.Body;
        ForeColor = Theme.Text;
        BackColor = Theme.NavBackground;
        Height = Theme.S(40);
        Margin = new Padding(Theme.S(4), Theme.S(1), Theme.S(4), Theme.S(1));
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
    }

    public string Glyph { get; }

    /// <summary>What the item shows (a profile, a page...).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? Target { get; init; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        if (_selected || _hover)
        {
            using var background = new SolidBrush(_selected ? Theme.NavSelected : Theme.NavHover);
            using var path = Theme.RoundedRectangle(new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(5f));
            g.FillPath(background, path);
        }
        if (_selected)
        {
            using var accent = new SolidBrush(Theme.Accent);
            using var bar = Theme.RoundedRectangle(new RectangleF(0, Height / 2f - Theme.S(8f), Theme.S(3f), Theme.S(16f)), Theme.S(1.5f));
            g.FillPath(accent, bar);
        }
        var iconBounds = new Rectangle(Theme.S(10), 0, Theme.S(32), Height);
        TextRenderer.DrawText(g, Glyph, Theme.Icon, iconBounds, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        var textBounds = new Rectangle(Theme.S(50), 0, Width - Theme.S(54), Height);
        TextRenderer.DrawText(g, Text, Font, textBounds, ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// A settings card as in Magpie / Windows 11 settings: icon, title, description on the left,
/// its controls on the right, optionally a second row of controls below.
/// </summary>
internal sealed class SettingCard : Panel
{
    private readonly Label _title;
    private readonly Label _description;
    private readonly FlowLayoutPanel _controls;
    private readonly TableLayoutPanel _layout;

    public SettingCard(string glyph, string title, string? description, params Control[] controls)
    {
        BackColor = Theme.PageBackground;
        Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(16), Theme.S(12));
        Margin = new Padding(0, 0, 0, Theme.S(4));
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        DoubleBuffered = true;
        MinimumSize = new Size(0, Theme.S(68));

        _layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Theme.CardBackground,
            Margin = Padding.Empty,
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(44)));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var icon = new Label
        {
            Text = glyph,
            Font = Theme.Icon,
            ForeColor = Theme.Text,
            AutoSize = false,
            Size = new Size(Theme.S(32), Theme.S(40)),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
            Anchor = AnchorStyles.Left,
        };
        _title = new Label { Text = title, Font = Theme.Body, ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 0, 0, Theme.S(2)) };
        _description = new Label
        {
            Text = description ?? string.Empty,
            Font = Theme.Caption,
            ForeColor = Theme.SecondaryText,
            AutoSize = true,
            Visible = !string.IsNullOrEmpty(description),
            Margin = Padding.Empty,
        };
        var texts = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, Theme.S(12), 0),
            BackColor = Theme.CardBackground,
        };
        texts.Controls.Add(_title);
        texts.Controls.Add(_description);
        texts.SizeChanged += (_, _) => WrapDescription();
        _layout.SizeChanged += (_, _) => WrapDescription();

        _controls = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
            BackColor = Theme.CardBackground,
        };
        foreach (var control in controls)
            AddControl(control);

        _layout.Controls.Add(icon, 0, 0);
        _layout.Controls.Add(texts, 1, 0);
        _layout.Controls.Add(_controls, 2, 0);
        Controls.Add(_layout);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Description
    {
        get => _description.Text;
        set
        {
            _description.Text = value ?? string.Empty;
            _description.Visible = !string.IsNullOrEmpty(value);
            WrapDescription();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color DescriptionColor
    {
        set => _description.ForeColor = value;
    }

    public void AddControl(Control control)
    {
        control.Anchor = AnchorStyles.Left;
        control.Margin = new Padding(Theme.S(6), Theme.S(2), 0, Theme.S(2));
        _controls.Controls.Add(control);
    }

    /// <summary>A second row under the title, spanning the card (e.g. several numeric fields).</summary>
    public void AddDetail(Control detail)
    {
        detail.Margin = new Padding(0, Theme.S(8), 0, 0);
        _layout.Controls.Add(detail, 1, 1);
        _layout.SetColumnSpan(detail, 2);
    }

    /// <summary>Long descriptions wrap instead of pushing the controls out of the card.</summary>
    private void WrapDescription()
    {
        int available = _layout.Width - Theme.S(44) - _controls.Width - Theme.S(24);
        int width = Math.Max(Theme.S(160), available);
        _description.MaximumSize = new Size(width, 0);
        _title.MaximumSize = new Size(width, 0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRectangle(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(6f));
        using var fill = new SolidBrush(Theme.CardBackground);
        using var border = new Pen(Theme.CardBorder);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
    }
}

/// <summary>An on/off switch drawn like the Windows 11 toggle.</summary>
internal sealed class ToggleSwitch : CheckBox
{
    public ToggleSwitch()
    {
        AutoSize = false;
        Size = new Size(Theme.S(44), Theme.S(22));
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.CardBackground);
        var track = new RectangleF(1, 1, Width - 3, Height - 3);
        float radius = track.Height / 2;
        using var path = Theme.RoundedRectangle(track, radius);
        float knob = track.Height - Theme.S(8f);
        if (Checked)
        {
            using var fill = new SolidBrush(Enabled ? Theme.Accent : Theme.DisabledText);
            g.FillPath(fill, path);
            using var knobBrush = new SolidBrush(Color.White);
            g.FillEllipse(knobBrush, track.Right - knob - Theme.S(4f), track.Y + Theme.S(4f), knob, knob);
        }
        else
        {
            var colour = Enabled ? Theme.SecondaryText : Theme.DisabledText;
            using var pen = new Pen(colour, Theme.S(1f));
            g.DrawPath(pen, path);
            using var knobBrush = new SolidBrush(colour);
            float small = knob - Theme.S(2f);
            g.FillEllipse(knobBrush, track.X + Theme.S(5f), track.Y + (track.Height - small) / 2, small, small);
        }
    }
}

/// <summary>Shows a hotkey as Magpie does: one accent chip per key ("Alt", "Shift", "A").</summary>
internal sealed class KeyChips : Control
{
    private string[] _keys = [];

    public KeyChips()
    {
        Font = Theme.BodyStrong;
        Height = Theme.S(34);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Hotkey
    {
        set
        {
            _keys = string.IsNullOrWhiteSpace(value)
                ? ["Aucun"]
                : value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Width = _keys.Sum(ChipWidth) + Math.Max(0, _keys.Length - 1) * Theme.S(6) + 2;
            Invalidate();
        }
    }

    private int ChipWidth(string key) => Math.Max(Theme.S(40), TextRenderer.MeasureText(key, Font).Width + Theme.S(20));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.CardBackground);
        int x = 0;
        foreach (string key in _keys)
        {
            int width = ChipWidth(key);
            var bounds = new Rectangle(x, 0, width, Height - 1);
            using var path = Theme.RoundedRectangle(bounds, Theme.S(4f));
            using var fill = new SolidBrush(key == "Aucun" ? Theme.SecondaryText : Theme.Accent);
            g.FillPath(fill, path);
            TextRenderer.DrawText(g, key, Font, bounds, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x += width + Theme.S(6);
        }
    }
}

internal static class UiFactory
{
    public static Button Button(string text, EventHandler? click = null, string? glyph = null)
    {
        var button = new Button
        {
            Text = glyph == null ? text : $"{text}",
            Font = Theme.Body,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(251, 251, 251),
            ForeColor = Theme.Text,
            Padding = new Padding(Theme.S(10), Theme.S(3), Theme.S(10), Theme.S(3)),
            MinimumSize = new Size(Theme.S(90), Theme.S(32)),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = Theme.CardBorder;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(246, 246, 246);
        if (click != null)
            button.Click += click;
        return button;
    }

    public static Button AccentButton(string text, EventHandler? click = null)
    {
        var button = Button(text, click);
        button.BackColor = Theme.Accent;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = Theme.Accent;
        button.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
        return button;
    }

    /// <summary>A borderless icon button (edit, delete...).</summary>
    public static Button IconButton(string glyph, string tooltip, EventHandler click)
    {
        var button = new Button
        {
            Text = glyph,
            Font = Theme.SmallIcon,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(Theme.S(34), Theme.S(34)),
            BackColor = Theme.CardBackground,
            ForeColor = Theme.Text,
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Theme.NavHover;
        button.Click += click;
        new ToolTip().SetToolTip(button, tooltip);
        return button;
    }

    public static ComboBox DropDown(int width, params object[] items)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(width), Font = Theme.Body };
        combo.Items.AddRange(items);
        return combo;
    }

    public static ComboBox Editable(int width, params object[] items)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = Theme.S(width), Font = Theme.Body };
        combo.Items.AddRange(items);
        return combo;
    }

    public static TextBox TextBox(int width) => new() { Width = Theme.S(width), Font = Theme.Body };

    public static NumericUpDown Number(decimal min, decimal max, int width, int decimals = 0, decimal increment = 1) => new()
    {
        Minimum = min,
        Maximum = max,
        DecimalPlaces = decimals,
        Increment = increment,
        Width = Theme.S(width),
        Font = Theme.Body,
    };

    public static Label Text(string text, Color? colour = null) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.Body,
        ForeColor = colour ?? Theme.SecondaryText,
        Margin = new Padding(Theme.S(4), Theme.S(8), Theme.S(2), 0),
    };

    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.CardBackground };
        foreach (var control in controls)
        {
            control.Margin = new Padding(Theme.S(4), Theme.S(2), Theme.S(4), Theme.S(2));
            row.Controls.Add(control);
        }
        return row;
    }
}
