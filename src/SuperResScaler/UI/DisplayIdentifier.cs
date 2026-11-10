// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Drawing.Text;
using SuperResScaler.Engine;
using SuperResScaler.Native;

namespace SuperResScaler.UI;

/// <summary>Shows each display's number on it for a few seconds, like Identify in Windows' settings.</summary>
internal static class DisplayIdentifier
{
    private const int DurationMs = 4000;
    private static readonly List<Form> OpenForms = new();
    private static System.Windows.Forms.Timer? _timer;

    public static void Show()
    {
        CloseAll();
        foreach (var monitor in MonitorInfo.GetAll())
        {
            var form = new IdentifyForm(monitor);
            form.Show();
            OpenForms.Add(form);
        }
        _timer = new System.Windows.Forms.Timer { Interval = DurationMs };
        _timer.Tick += (_, _) => CloseAll();
        _timer.Start();
    }

    private static void CloseAll()
    {
        _timer?.Dispose();
        _timer = null;
        foreach (var form in OpenForms)
            form.Dispose();
        OpenForms.Clear();
    }

    private sealed class IdentifyForm : Form
    {
        private const int WS_EX_TOPMOST = 0x8;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int MaxHeight = 360;

        private readonly MonitorInfo _monitor;
        private readonly Rectangle _target;
        private readonly float _stretch;

        public IdentifyForm(MonitorInfo monitor)
        {
            _monitor = monitor;
            RECT b = monitor.Bounds;
            // On a super-resolution mode the pixels are far taller than wide: widen the text to compensate.
            double pixelAspect = Geometry.ResolveDisplayAspect("auto", b.Width, b.Height) / ((double)b.Width / b.Height);
            _stretch = (float)(1 / pixelAspect);
            int height = Math.Min(b.Height * 7 / 10, MaxHeight);
            int width = Math.Min(b.Width, (int)(height * 1.3f * _stretch));
            _target = new Rectangle(b.Left + (b.Width - width) / 2, b.Top + (b.Height - height) / 2, width, height);

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Accent;
            DoubleBuffered = true;
            Bounds = _target;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = _target; // the window may have been resized on reaching a display of another DPI
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform(_stretch, 1);
            float width = ClientSize.Width / _stretch, height = ClientSize.Height;
            using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var numberFont = new Font(Theme.PageTitle.FontFamily, height * 0.55f, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString(_monitor.Number.ToString(), numberFont, Brushes.White, new RectangleF(0, 0, width, height * 0.76f), centered);

            // On one line, however long the monitor's name.
            string caption = $"{_monitor.Name} — {_monitor.Bounds.Width}x{_monitor.Bounds.Height}";
            float captionSize = height * 0.09f;
            using (var measure = new Font(Theme.PageTitle.FontFamily, captionSize, FontStyle.Regular, GraphicsUnit.Pixel))
                captionSize *= Math.Min(1, width * 0.94f / g.MeasureString(caption, measure).Width);
            using var captionFont = new Font(Theme.PageTitle.FontFamily, captionSize, FontStyle.Regular, GraphicsUnit.Pixel);
            g.DrawString(caption, captionFont, Brushes.White, new RectangleF(0, height * 0.76f, width, height * 0.2f), centered);
        }
    }
}
