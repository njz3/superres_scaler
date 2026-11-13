// Copyright (c) Benjamin Maurin (njz3) 2026

using SuperResScaler.App;

namespace SuperResScaler.UI.Pages;

/// <summary>A page of the main window: big title, then sections of setting cards (Magpie layout).</summary>
internal abstract class Page : UserControl
{
    private readonly TableLayoutPanel _stack;
    private readonly Label _title;

    protected Page(ScalerController controller, string title)
    {
        Controller = controller;
        AutoScaleMode = AutoScaleMode.None;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Theme.PageBackground;
        Font = Theme.Body;
        Padding = new Padding(Theme.S(40), Theme.S(24), Theme.S(40), Theme.S(32));

        _stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            BackColor = Theme.PageBackground,
        };
        _stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _title = new Label
        {
            Text = title,
            Font = Theme.PageTitle,
            ForeColor = Theme.Text,
            AutoSize = true,
            Margin = Padding.Empty,
        };
        // Title on the left, the corner (virtual display thumbnail) on the right when there is one.
        _header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.PageBackground,
            Margin = new Padding(0, 0, 0, Theme.S(12)),
        };
        _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _header.Controls.Add(_title, 0, 0);
        _stack.Controls.Add(_header);
        Controls.Add(_stack);
    }

    protected ScalerController Controller { get; }

    protected string PageTitle
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    /// <summary>
    /// Puts <paramref name="control"/> (the virtual display's thumbnail) in the top-right corner,
    /// beside the title; it takes room only while it is visible. Null gives it back, so that it is
    /// not disposed with the page.
    /// </summary>
    public void SetCorner(Control? control)
    {
        if (_corner != null && _corner != control && _corner.Parent == _header)
            _header.Controls.Remove(_corner);
        _corner = control;
        if (control != null && control.Parent != _header)
        {
            control.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            control.Margin = new Padding(Theme.S(16), 0, 0, 0);
            _header.Controls.Add(control, 1, 0);
        }
    }

    private readonly TableLayoutPanel _header;
    private Control? _corner;

    /// <summary>Called each time the page is shown, to refresh what may have changed meanwhile.</summary>
    public virtual void OnShown() { }

    /// <summary>Called when the controller state changes (scaling started/stopped, watcher...).</summary>
    public virtual void OnStateChanged() { }

    protected void AddSection(string title, string? description = null)
    {
        var header = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, Theme.S(18), 0, Theme.S(6)),
            BackColor = Theme.PageBackground,
        };
        header.Controls.Add(new Label { Text = title, Font = Theme.Section, ForeColor = Theme.Text, AutoSize = true, Margin = Padding.Empty });
        if (description != null)
        {
            header.Controls.Add(new Label
            {
                Text = description,
                Font = Theme.Body,
                ForeColor = Theme.SecondaryText,
                AutoSize = true,
                MaximumSize = new Size(Theme.S(900), 0),
                Margin = new Padding(0, Theme.S(2), 0, 0),
            });
        }
        _stack.Controls.Add(header);
    }

    protected SettingCard AddCard(string glyph, string title, string? description, params Control[] controls)
    {
        var card = new SettingCard(glyph, title, description, controls);
        _stack.Controls.Add(card);
        return card;
    }

    protected void AddControl(Control control)
    {
        control.Dock = DockStyle.Top;
        _stack.Controls.Add(control);
    }

    /// <summary>Runs <paramref name="apply"/> unless the page is being filled from the settings.</summary>
    protected bool Loading { get; set; }
}
