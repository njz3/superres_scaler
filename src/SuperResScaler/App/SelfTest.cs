using System.Diagnostics;
using SuperResScaler.Capture;
using SuperResScaler.Config;
using SuperResScaler.Engine;
using SuperResScaler.Native;
using SuperResScaler.Rendering;

namespace SuperResScaler.App;

/// <summary>
/// End-to-end check without a game: shows a test pattern window, captures it with
/// Windows.Graphics.Capture, renders it with every shader at 1920x240 and verifies the pixels.
/// Writes PNG files and selftest.txt into the output folder.
/// </summary>
internal static class SelfTest
{
    private const int ColourTolerance = 10;

    public static int Run(string outputDirectory, bool includeOverlay)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var report = new List<string>();
        int failures = 0;
        void Check(bool ok, string description)
        {
            report.Add($"{(ok ? "PASS" : "FAIL")}  {description}");
            if (!ok)
                failures++;
        }

        using var form = new TestPatternForm();
        form.Show();
        Application.DoEvents();

        Exception? error = null;
        IntPtr hwnd = form.Handle;
        var worker = new Thread(() =>
        {
            try
            {
                RenderTests(hwnd, outputDirectory, Check);
                SessionTest(hwnd, outputDirectory, preview: true, Check);
                if (includeOverlay)
                    SessionTest(hwnd, outputDirectory, preview: false, Check);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        while (worker.IsAlive)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }

        if (error != null)
        {
            report.Add($"ERROR {error}");
            failures++;
        }
        report.Add(failures == 0 ? "ALL TESTS PASSED" : $"{failures} FAILURE(S)");
        File.WriteAllLines(Path.Combine(outputDirectory, "selftest.txt"), report);
        foreach (string line in report)
            Log.Info("selftest: " + line);
        return failures == 0 ? 0 : 1;
    }

    private static void RenderTests(IntPtr hwnd, string outputDirectory, Action<bool, string> check)
    {
        using var renderer = new Renderer();
        using var capture = new WindowCapture(renderer.Device, hwnd, captureCursor: false);
        using var frame = WaitForFrame(capture);
        check(frame != null, "Windows.Graphics.Capture delivers a frame of the test window");
        if (frame == null)
            return;

        using (var raw = Renderer.ToBitmap(renderer.ReadPixels(frame.Texture, 0, 0, frame.ContentWidth, frame.ContentHeight), frame.ContentWidth, frame.ContentHeight))
            raw.Save(Path.Combine(outputDirectory, "capture_raw.png"));

        var options = new ScalingOptions();
        Rectangle region = Geometry.ComputeSourceRegion(hwnd, options, frame.ContentWidth, frame.ContentHeight);
        check(region.Size == new Size(TestPatternForm.ClientWidth, TestPatternForm.ClientHeight),
            $"client area located in the capture: {region} (window captured as {frame.ContentWidth}x{frame.ContentHeight})");

        var detector = new BlackBarDetector();
        detector.Analyze(renderer.ReadPixels(frame.Texture, region.X, region.Y, region.Width, region.Height), region.Width, region.Height);
        Rectangle content = detector.Apply(region);
        check(content.Width == TestPatternForm.ContentWidth && content.Height == TestPatternForm.ClientHeight
              && content.X == region.X + TestPatternForm.ContentX,
            $"black bars detected: content {content.Width}x{content.Height} at +{content.X - region.X}");

        foreach (string shader in ShaderLibrary.List())
        {
            try
            {
                renderer.LoadShader(shader);
            }
            catch (Exception ex)
            {
                check(false, $"{shader}: compiles ({ex.Message})");
                continue;
            }
            renderer.UpdateSource(frame.Texture, content.X, content.Y, content.Width, content.Height);
            using var output = renderer.RenderToBitmap(1920, 240, new RectangleF(0, 0, 1920, 240));
            output.Save(Path.Combine(outputDirectory, $"{shader}_1920x240.png"));
            check(VerifyPattern(output, new Rectangle(0, 0, 1920, 240), out string detail), $"{shader}: 320x240 -> 1920x240 pattern {detail}");
        }

        renderer.LoadShader(ShaderLibrary.DefaultShader);
        renderer.UpdateSource(frame.Texture, content.X, content.Y, content.Width, content.Height);
        using (var output = renderer.RenderToBitmap(1280, 240, new RectangleF(0, 0, 1280, 240)))
        {
            output.Save(Path.Combine(outputDirectory, "Area_1280x240.png"));
            check(VerifyPattern(output, new Rectangle(0, 0, 1280, 240), out string detail), $"Area: 320x240 -> 1280x240 pattern {detail}");
        }

        var aspect = new ScalingOptions { Mode = ScaleMode.KeepAspect, DisplayAspect = "4:3", SourceAspect = "16:9" };
        RectangleF letterbox = Geometry.ComputeDestination(aspect, 320, 240, 1920, 240);
        check(letterbox == new RectangleF(0, 30, 1920, 180), $"16:9 source on a 4:3 1920x240 screen is letterboxed: {letterbox}");
        using (var output = renderer.RenderToBitmap(1920, 240, letterbox))
            output.Save(Path.Combine(outputDirectory, "Area_1920x240_16x9_letterbox.png"));

        var pillarbox = new ScalingOptions { Mode = ScaleMode.KeepAspect, DisplayAspect = "4:3", SourceAspect = "auto" };
        RectangleF fit = Geometry.ComputeDestination(pillarbox, 320, 240, 1920, 240);
        check(fit == new RectangleF(0, 0, 1920, 240), $"4:3 source on a 4:3 1920x240 screen fills it: {fit}");
    }

    /// <summary>Runs a real scaling session (preview window or full-screen overlay) and checks the screen.</summary>
    private static void SessionTest(IntPtr hwnd, string outputDirectory, bool preview, Action<bool, string> check)
    {
        string name = preview ? "preview" : "overlay";
        var options = new ScalingOptions { Preview = preview, PreviewWidth = 1280, PreviewHeight = 240, AutoCrop = true };
        string? endReason = "not ended";
        var ended = new ManualResetEventSlim();
        var session = ScalingSession.Start(hwnd, options, (_, reason) => { endReason = reason; ended.Set(); });

        Thread.Sleep(1500);
        Rectangle bounds = session.OutputBounds;
        using (var screen = new Bitmap(bounds.Width, bounds.Height))
        {
            using (var g = Graphics.FromImage(screen))
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            screen.Save(Path.Combine(outputDirectory, $"session_{name}_screen.png"));
            check(VerifyPattern(screen, new Rectangle(Point.Empty, bounds.Size), out string detail),
                $"{name} session: pattern visible on screen at {bounds} {detail}");
        }

        var stopwatch = Stopwatch.StartNew();
        session.Stop();
        ended.Wait(3000);
        check(ended.IsSet && endReason == null, $"{name} session stops cleanly in {stopwatch.ElapsedMilliseconds} ms (reason: {endReason ?? "none"})");
    }

    private static CapturedFrame? WaitForFrame(WindowCapture capture)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (capture.TryGetLatestFrame() is { } frame)
                return frame;
            Thread.Sleep(10);
        }
        return null;
    }

    /// <summary>Checks the colour bars (top half) and the alternating 1-pixel lines (bottom half).</summary>
    private static bool VerifyPattern(Bitmap image, Rectangle area, out string detail)
    {
        double scaleX = area.Width / (double)TestPatternForm.ContentWidth;
        double scaleY = area.Height / (double)TestPatternForm.ClientHeight;
        var errors = new List<string>();

        for (int i = 0; i < TestPatternForm.Bars.Length; i++)
        {
            int x = area.X + (int)((i * TestPatternForm.BarWidth + TestPatternForm.BarWidth / 2.0) * scaleX);
            int y = area.Y + (int)(60 * scaleY);
            Color actual = image.GetPixel(x, y);
            if (!Near(actual, TestPatternForm.Bars[i]))
                errors.Add($"bar {i} at ({x},{y}) = {Describe(actual)}");
        }

        if (scaleY == 1.0)
        {
            int x = area.X + area.Width / 2;
            for (int row = TestPatternForm.LinesTop; row < TestPatternForm.ClientHeight; row += 7)
            {
                Color expected = TestPatternForm.LineColour(row);
                Color actual = image.GetPixel(x, area.Y + row);
                if (!Near(actual, expected))
                    errors.Add($"line {row} = {Describe(actual)}");
            }
        }

        detail = errors.Count == 0 ? "OK" : string.Join("; ", errors.Take(4));
        return errors.Count == 0;
    }

    private static bool Near(Color a, Color b)
        => Math.Abs(a.R - b.R) <= ColourTolerance && Math.Abs(a.G - b.G) <= ColourTolerance && Math.Abs(a.B - b.B) <= ColourTolerance;

    private static string Describe(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>
    /// 400x240 client area: a 320x240 pattern between two 40-pixel black bars (like a 4:3 game
    /// pillarboxed in a wider window). Top half: colour bars. Bottom half: alternating 1-pixel lines.
    /// </summary>
    private sealed class TestPatternForm : Form
    {
        public const int ClientWidth = 400;
        public const int ClientHeight = 240;
        public const int ContentX = 40;
        public const int ContentWidth = 320;
        public const int BarWidth = 40;
        public const int LinesTop = 120;

        public static readonly Color[] Bars =
        [
            Color.FromArgb(255, 255, 255), Color.FromArgb(255, 255, 0), Color.FromArgb(0, 255, 255), Color.FromArgb(0, 255, 0),
            Color.FromArgb(255, 0, 255), Color.FromArgb(255, 0, 0), Color.FromArgb(0, 0, 255), Color.FromArgb(128, 128, 128),
        ];

        public TestPatternForm()
        {
            Text = "SuperResScaler — mire de test";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(200, 200);
            ClientSize = new Size(ClientWidth, ClientHeight);
            BackColor = Color.Black;
            DoubleBuffered = true;
        }

        public static Color LineColour(int row) => row % 2 == 0 ? Color.FromArgb(255, 255, 255) : Color.FromArgb(255, 0, 0);

        protected override bool ShowWithoutActivation => true;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Black);
            for (int i = 0; i < Bars.Length; i++)
            {
                using var brush = new SolidBrush(Bars[i]);
                g.FillRectangle(brush, ContentX + i * BarWidth, 0, BarWidth, LinesTop);
            }
            for (int row = LinesTop; row < ClientHeight; row++)
            {
                using var brush = new SolidBrush(LineColour(row));
                g.FillRectangle(brush, ContentX, row, ContentWidth, 1);
            }
        }
    }
}
