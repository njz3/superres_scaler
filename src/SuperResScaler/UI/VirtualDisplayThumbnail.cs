// Copyright (c) Benjamin Maurin (njz3) 2026

using SuperResScaler.Capture;
using SuperResScaler.Native;

namespace SuperResScaler.UI;

/// <summary>
/// Small picture of the virtual test display, in the top-right corner of the pages, refreshed every
/// half second while the main window is shown (nothing is captured while it is hidden or minimized).
/// Hidden when there is no virtual display. A click opens its page.
/// </summary>
internal sealed class VirtualDisplayThumbnail : Control
{
    public const int RefreshInterval = 500;
    private const int BoxWidth = 200;
    private const int BoxHeight = 112;

    private readonly Form _owner;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = RefreshInterval };
    private readonly ToolTip _toolTip = new();
    private Bitmap? _picture;
    private string _caption = string.Empty;
    private bool _capturing;
    private string? _lastError;

    public VirtualDisplayThumbnail(Form owner)
    {
        _owner = owner;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        Size = new Size(Theme.S(BoxWidth), Theme.S(BoxHeight));
        Cursor = Cursors.Hand;
        Visible = false;
        _timer.Tick += (_, _) => RefreshNow();
        _timer.Start();
    }

    /// <summary>A virtual display was found at the last refresh (the thumbnail is then visible).</summary>
    public bool IsActive { get; private set; }

    /// <summary>Time the last picture took (GDI copy and shrink), for the log and the tests.</summary>
    public TimeSpan LastCaptureTime { get; private set; }

    private sealed record Snapshot(MonitorInfo? Monitor, Bitmap? Picture, TimeSpan Time, string? Error);

    /// <summary>Takes a new picture now (off the UI thread), unless one is already being taken.</summary>
    public async void RefreshNow()
    {
        if (_capturing || IsDisposed || !_owner.Visible || _owner.WindowState == FormWindowState.Minimized)
            return;
        _capturing = true;
        try
        {
            var box = new Size(Width - 2, Height - 2); // inside the border
            var snapshot = await Task.Run(() => Take(box));
            if (IsDisposed)
                snapshot.Picture?.Dispose();
            else
                Apply(snapshot);
        }
        catch (Exception ex)
        {
            Log.Warn($"Virtual display thumbnail: {ex.Message}");
        }
        finally
        {
            _capturing = false;
        }
    }

    private static Snapshot Take(Size box)
    {
        var monitor = MonitorInfo.GetAll().FirstOrDefault(m => m.IsVirtual);
        if (monitor == null)
            return new Snapshot(null, null, TimeSpan.Zero, null);
        var bounds = Rectangle.FromLTRB(monitor.Bounds.Left, monitor.Bounds.Top, monitor.Bounds.Right, monitor.Bounds.Bottom);
        var size = ScreenSnapshot.Fit(bounds.Width, bounds.Height, box);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var picture = ScreenSnapshot.Take(bounds, size);
            return new Snapshot(monitor, picture, System.Diagnostics.Stopwatch.GetElapsedTime(start), null);
        }
        catch (Exception ex)
        {
            // A locked session or a secure desktop (UAC) cannot be copied: the last picture stays.
            return new Snapshot(monitor, null, TimeSpan.Zero, ex.Message);
        }
    }

    private void Apply(Snapshot snapshot)
    {
        bool active = snapshot.Monitor != null;
        if (snapshot.Picture != null || !active)
        {
            _picture?.Dispose();
            _picture = snapshot.Picture;
            LastCaptureTime = snapshot.Time;
        }
        if (snapshot.Error != null && snapshot.Error != _lastError)
            Log.Warn($"Virtual display thumbnail: {snapshot.Error}");
        _lastError = snapshot.Error;

        if (snapshot.Monitor is { } monitor)
        {
            _caption = $"Écran virtuel — {monitor.Bounds.Width}x{monitor.Bounds.Height}";
            _toolTip.SetToolTip(this, $"{monitor.Name} (Écran {monitor.Number}), rafraîchi toutes les 0,5 s. Cliquez pour ouvrir sa page.");
        }
        if (active != IsActive)
        {
            IsActive = active;
            Visible = active; // the page's title row makes room for it, or takes it back
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var box = ClientRectangle;
        g.Clear(Color.Black);
        if (_picture != null)
        {
            var image = new Rectangle((box.Width - _picture.Width) / 2, (box.Height - _picture.Height) / 2, _picture.Width, _picture.Height);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.DrawImage(_picture, image);
        }
        else
        {
            TextRenderer.DrawText(g, "Pas d'image", Theme.Caption, box, Theme.DisabledText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        int bandHeight = Theme.Caption.Height + Theme.S(4);
        var band = new Rectangle(1, box.Height - 1 - bandHeight, box.Width - 2, bandHeight);
        using (var shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            g.FillRectangle(shade, band);
        band.Inflate(-Theme.S(6), 0);
        TextRenderer.DrawText(g, _caption, Theme.Caption, band, Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        using var border = new Pen(Theme.CardBorder);
        g.DrawRectangle(border, 0, 0, box.Width - 1, box.Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _toolTip.Dispose();
            _picture?.Dispose();
            _picture = null;
        }
        base.Dispose(disposing);
    }
}
