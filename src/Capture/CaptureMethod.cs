// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Runtime.Versioning;

namespace SuperResScaler.Capture;

/// <summary>How a picture is captured.</summary>
public enum CaptureMethod
{
    /// <summary>Windows.Graphics.Capture when this Windows has it (1903 or later), else the desktop duplication.</summary>
    Auto,

    /// <summary>Windows.Graphics.Capture: a window or a whole display, Windows 10 1903 or later.</summary>
    GraphicsCapture,

    /// <summary>DXGI desktop duplication: a whole display only, from Windows 8 (so on Windows 10 1809).</summary>
    DesktopDuplication,
}

/// <summary>The method a capture will use, and why when it is not the one asked for.</summary>
internal readonly record struct CaptureChoice(CaptureMethod Method, string? Note);

internal static class CaptureSupport
{
    /// <summary>Windows 10 1903: IGraphicsCaptureItemInterop (CreateForWindow, CreateForMonitor).</summary>
    public const int GraphicsCaptureBuild = 18362;

    public static int WindowsBuild => Environment.OSVersion.Version.Build;

    /// <summary>Set to 1, behaves as Windows 10 1809: to try the cabinets' capture on a newer Windows.</summary>
    public const string WithoutGraphicsCaptureVariable = "SUPERRESSCALER_NO_WGC";

    private static readonly bool GraphicsCaptureDisabled = Environment.GetEnvironmentVariable(WithoutGraphicsCaptureVariable) == "1";

    [SupportedOSPlatformGuard("windows10.0.18362.0")]
    public static bool GraphicsCaptureAvailable => !GraphicsCaptureDisabled && OperatingSystem.IsWindowsVersionAtLeast(10, 0, GraphicsCaptureBuild);

    public static string Describe(CaptureMethod method) => method switch
    {
        CaptureMethod.GraphicsCapture => "Windows.Graphics.Capture",
        CaptureMethod.DesktopDuplication => "duplication de bureau",
        _ => "auto",
    };

    /// <summary>
    /// The method for a window or a whole display. A window can only be captured by
    /// Windows.Graphics.Capture: throws when this Windows does not have it.
    /// </summary>
    public static CaptureChoice Choose(CaptureMethod requested, bool wholeDisplay, bool graphicsCaptureAvailable)
    {
        if (!wholeDisplay)
        {
            if (!graphicsCaptureAvailable)
                throw new NotSupportedException("Capturer une fenêtre demande Windows 10 1903 ou plus récent. Choisissez comme source l'écran "
                    + "entier où est le jeu (Capture > Source, ou --display) : il est capturé par duplication de bureau.");
            return new CaptureChoice(CaptureMethod.GraphicsCapture, requested == CaptureMethod.DesktopDuplication
                ? "La duplication de bureau ne capture qu'un écran entier : fenêtre capturée par Windows.Graphics.Capture"
                : null);
        }
        return requested switch
        {
            CaptureMethod.DesktopDuplication => new CaptureChoice(CaptureMethod.DesktopDuplication, null),
            CaptureMethod.GraphicsCapture when !graphicsCaptureAvailable => new CaptureChoice(CaptureMethod.DesktopDuplication,
                "Windows.Graphics.Capture demande Windows 10 1903 : capture par duplication de bureau"),
            CaptureMethod.GraphicsCapture => new CaptureChoice(CaptureMethod.GraphicsCapture, null),
            _ => new CaptureChoice(graphicsCaptureAvailable ? CaptureMethod.GraphicsCapture : CaptureMethod.DesktopDuplication, null),
        };
    }
}
