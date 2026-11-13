// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Globalization;
using System.Text.RegularExpressions;
using SuperResScaler.Capture;
using SuperResScaler.Config;
using SuperResScaler.Native;
using SuperResScaler.VirtualDisplay;

namespace SuperResScaler.App;

/// <summary>Command-line arguments, mainly for launching from an arcade front-end.</summary>
internal sealed partial class CommandLineOptions
{
    public const string Usage = """
        SuperResScaler — mise à l'échelle d'une fenêtre pour les modes super-résolution (1920x240...)

        Sans argument : interface graphique.
          --tray                     démarre l'interface réduite dans la zone de notification

        Mode ligne de commande (pour un front-end) : attend la fenêtre, la met à l'échelle,
        puis quitte quand elle se ferme (ou sur le raccourci démarrer/arrêter).
          --process <jeu.exe>        fenêtre d'un processus
          --title <texte>            fenêtre dont le titre contient ce texte
          --class <classe>           fenêtre de cette classe Win32
          --hwnd <handle>            handle de fenêtre (décimal ou 0x...)
          --foreground               fenêtre au premier plan après le délai
          --wait <s>                 délai max d'attente de la fenêtre (défaut 60)
          --delay <s>                délai avant de démarrer (défaut 1, 3 avec --foreground)
          --persist                  après fermeture, attend une nouvelle fenêtre (lanceur -> jeu)
          --profile <nom>            utilise ce profil de settings.json

        Options (remplacent celles du profil) :
          --shader <nom>             Area, Nearest, SharpBilinear, LinearX_NearestY, Bilinear...
          --mode stretch|aspect      plein écran étiré / au ratio avec bandes noires
                                     (fullscreen_stretch / fullscreen acceptés)
          --aspect <format>          comme ASPECT_RATIO du linuxloader : original (forme de
                                     la fenêtre), 4:3 / 16:9 / 4:3_letterbox (image entière à
                                     sa forme), 4:3_stretch (déformée), 4:3_crop (rognée)
          --crop-position <n>        _crop : partie gardée, -100 gauche/haut … 0 … 100
          --barrel                   _crop : courbe les bords au lieu de les couper
          --barrel-center <%>        milieu non déformé (défaut 60)
          --barrel-radius <%>        rayon du cylindre (défaut 50)
          --mirror-x                 miroir gauche/droite (projecteur derrière un écran)
          --mirror-y                 miroir haut/bas (projecteur par le dessus)
          --display-aspect auto|4:3  forme physique de l'écran (auto : 4:3 en super-résolution)
          --crop g,h,d,b             recadrage manuel en pixels
          --autocrop                 recadrage automatique des bandes noires
          --window                   capture la fenêtre entière (pas seulement la zone client)
          --cursor                   capture le curseur
          --no-vsync                 désactive la synchro verticale
          --display <écran>          capture cet écran entier au lieu de la fenêtre (numéro vu
                                     avec --list-modes, partie du nom du moniteur, ou virtuel
                                     pour l'écran virtuel). Seul : démarre aussitôt ; avec
                                     --process : s'arrête avec le jeu
          --capture auto|wgc|duplication  méthode de capture d'un écran entier : auto =
                                     Windows.Graphics.Capture dès Windows 10 1903, sinon la
                                     duplication de bureau (seule possible en 1809, sans curseur)
          --monitor <écran>          écran de sortie (même format, 0 = automatique)
          --resolution 1920x240@60   mode d'affichage pendant la mise à l'échelle (aussi
                                     1280x480i) ; la résolution d'origine revient à l'arrêt
          --preview [LxH]            fenêtre de prévisualisation au lieu du plein écran

        Écran virtuel de test (pilote Virtual Display Driver, intégré à l'exe), pour essayer les
        super-résolutions sans borne ; il se capture avec --display virtuel :
          --virtual-create           crée l'écran virtuel (demande les droits d'administrateur)
          --virtual-destroy          le détruit (idem) ; ses modes restent enregistrés
          --virtual-modes            liste ses modes
          --virtual-add <mode>       ajoute un mode : 1280x240@60, 1024x384@57.5...
          --virtual-remove <mode>    retire un mode

        Diagnostic :
          --list-modes [n]           liste les modes d'affichage (de l'écran n, sinon de tous)
          --selftest <dossier>       rend une mire avec chaque shader et écrit des PNG + un rapport
          --selftest-overlay         ajoute un test de l'overlay plein écran (~2 s)
        """;

    public bool Help { get; private set; }
    public bool Tray { get; private set; }
    public string? SelfTestDirectory { get; private set; }
    public bool SelfTestOverlay { get; private set; }
    public bool ListModes { get; private set; }
    public int ListModesMonitor { get; private set; }

    /// <summary>--virtual-create, -destroy, -modes, -add, -remove: "create", "destroy", "modes", "add" or "remove".</summary>
    public string? VirtualAction { get; private set; }

    /// <summary>The mode of --virtual-add / --virtual-remove.</summary>
    public VirtualMode VirtualMode { get; private set; }

    /// <summary>The elevated helper's action and result file (hidden argument).</summary>
    public (string Action, string ResultFile)? VirtualAdmin { get; private set; }

    public string? Process { get; private set; }
    public string? Title { get; private set; }
    public string? ClassName { get; private set; }
    public IntPtr Hwnd { get; private set; }
    public bool Foreground { get; private set; }

    /// <summary>The display to capture (--display).</summary>
    public string? Display { get; private set; }
    public double WaitSeconds { get; private set; } = 60;
    public double? DelaySeconds { get; private set; }
    public bool Persist { get; private set; }
    public string? Profile { get; private set; }

    private readonly List<Action<ScalingOptions>> _overrides = new();

    public bool IsHeadless => TargetsWindow || Display != null;

    /// <summary>A display is captured with no window to wait for.</summary>
    public bool DisplayOnly => Display != null && !TargetsWindow;

    private bool TargetsWindow => Process != null || Title != null || ClassName != null || Hwnd != IntPtr.Zero || Foreground;

    public double EffectiveDelay => DelaySeconds ?? (Foreground ? 3 : 1);

    public void ApplyOverrides(ScalingOptions options)
    {
        foreach (var apply in _overrides)
            apply(options);
    }

    public bool Matches(WindowInfo window)
    {
        if (Hwnd != IntPtr.Zero && window.Handle != Hwnd)
            return false;
        if (Process != null && !string.Equals(Path.GetFileNameWithoutExtension(window.ProcessName),
                Path.GetFileNameWithoutExtension(Process), StringComparison.OrdinalIgnoreCase))
            return false;
        if (Title != null && !window.Title.Contains(Title, StringComparison.OrdinalIgnoreCase))
            return false;
        if (ClassName != null && !string.Equals(window.ClassName, ClassName, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    /// <summary>Parses the arguments. Throws <see cref="ArgumentException"/> with a readable message.</summary>
    public static CommandLineOptions Parse(string[] args)
    {
        var o = new CommandLineOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string Value()
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"Valeur manquante après {arg}");
                return args[++i];
            }

            switch (arg.ToLowerInvariant())
            {
                case "-h": case "--help": case "/?": o.Help = true; break;
                case "--tray": o.Tray = true; break;
                case "--selftest": o.SelfTestDirectory = Value(); break;
                case "--selftest-overlay": o.SelfTestOverlay = true; break;

                case "--process": o.Process = Value(); break;
                case "--title": o.Title = Value(); break;
                case "--class": o.ClassName = Value(); break;
                case "--hwnd": o.Hwnd = ParseHandle(Value()); break;
                case "--foreground": o.Foreground = true; break;
                case "--wait": o.WaitSeconds = ParseDouble(arg, Value()); break;
                case "--delay": o.DelaySeconds = ParseDouble(arg, Value()); break;
                case "--persist": o.Persist = true; break;
                case "--profile": o.Profile = Value(); break;

                case "--shader": { string v = Value(); o._overrides.Add(s => s.Shader = v); break; }
                case "--mode":
                {
                    string v = Value().ToLowerInvariant();
                    // linuxloader's DISPLAY_MODE names are accepted too.
                    ScaleMode mode = v switch
                    {
                        "stretch" or "fill" or "fullscreen_stretch" => ScaleMode.Stretch,
                        "aspect" or "keepaspect" or "fit" or "fullscreen" => ScaleMode.KeepAspect,
                        _ => throw new ArgumentException($"Mode inconnu : {v} (stretch ou aspect)"),
                    };
                    o._overrides.Add(s => s.Mode = mode);
                    break;
                }
                case "--display-aspect":
                {
                    string v = Value();
                    if (!v.Equals("auto", StringComparison.OrdinalIgnoreCase) && !Ratio.TryParse(v, out _))
                        throw new ArgumentException($"Ratio d'écran invalide : {v} (auto, 4:3, 16:9…)");
                    o._overrides.Add(s => s.DisplayAspect = v);
                    break;
                }
                case "--aspect":
                {
                    string v = Value();
                    if (!PictureAspect.TryParse(v, out _))
                        throw new ArgumentException($"Ratio d'aspect invalide : {v} (original, 4:3, 16:9, 4:3_letterbox…)");
                    o._overrides.Add(s => s.AspectRatio = v);
                    break;
                }
                case "--crop-position":
                {
                    int position = (int)ParseSigned(arg, Value(), -100, 100);
                    o._overrides.Add(s => s.CropPosition = position);
                    break;
                }
                case "--barrel": o._overrides.Add(s => s.CropBarrel = true); break;
                case "--mirror-x": o._overrides.Add(s => s.MirrorX = true); break;
                case "--mirror-y": o._overrides.Add(s => s.MirrorY = true); break;
                case "--barrel-center":
                {
                    int center = (int)ParseSigned(arg, Value(), 0, 100);
                    o._overrides.Add(s => s.BarrelCenter = center);
                    break;
                }
                case "--barrel-radius":
                {
                    int radius = (int)ParseSigned(arg, Value(), 1, 1000);
                    o._overrides.Add(s => s.BarrelRadius = radius);
                    break;
                }
                case "--crop":
                {
                    int[] c = ParseCrop(Value());
                    o._overrides.Add(s => { s.CropLeft = c[0]; s.CropTop = c[1]; s.CropRight = c[2]; s.CropBottom = c[3]; });
                    break;
                }
                case "--autocrop": o._overrides.Add(s => s.AutoCrop = true); break;
                case "--window": o._overrides.Add(s => s.ClientAreaOnly = false); break;
                case "--cursor": o._overrides.Add(s => s.CaptureCursor = true); break;
                case "--no-vsync": o._overrides.Add(s => s.VSync = false); break;
                case "--capture":
                {
                    string v = Value().Trim().ToLowerInvariant();
                    CaptureMethod method = v switch
                    {
                        "auto" => CaptureMethod.Auto,
                        "wgc" or "graphicscapture" => CaptureMethod.GraphicsCapture,
                        "duplication" or "dda" or "desktopduplication" => CaptureMethod.DesktopDuplication,
                        _ => throw new ArgumentException($"Méthode de capture inconnue : {v} (auto, wgc ou duplication)"),
                    };
                    o._overrides.Add(s => s.CaptureMethod = method);
                    break;
                }
                case "--monitor":
                {
                    string v = Value().Trim();
                    o._overrides.Add(s => s.Monitor = v == "0" ? string.Empty : v);
                    break;
                }
                case "--display":
                {
                    string v = Value().Trim();
                    o.Display = v;
                    o._overrides.Add(s => s.SourceDisplay = v);
                    break;
                }
                case "--resolution":
                case "--res":
                {
                    string v = Value();
                    if (!ResolutionRequest.IsUnchanged(v) && !ResolutionRequest.TryParse(v, out _))
                        throw new ArgumentException($"Résolution invalide : {v} (ex. 1920x240, 1920x240@60, 1280x480i)");
                    o._overrides.Add(s => s.Resolution = v);
                    break;
                }
                case "--virtual-create": o.SetVirtualAction(arg, "create"); break;
                case "--virtual-destroy": o.SetVirtualAction(arg, "destroy"); break;
                case "--virtual-modes": o.SetVirtualAction(arg, "modes"); break;
                case "--virtual-add":
                case "--virtual-remove":
                {
                    string v = Value();
                    if (!VirtualMode.TryParse(v, out var mode))
                        throw new ArgumentException($"Mode invalide pour {arg} : {v} (ex. 1280x240@60, 1024x384@57.5)");
                    o.SetVirtualAction(arg, arg.EndsWith("add", StringComparison.OrdinalIgnoreCase) ? "add" : "remove");
                    o.VirtualMode = mode;
                    break;
                }
                case VirtualDisplayManager.AdminArgument:
                {
                    string action = Value();
                    o.VirtualAdmin = (action, Value());
                    break;
                }
                case "--list-modes":
                    o.ListModes = true;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int listMonitor))
                    {
                        o.ListModesMonitor = listMonitor;
                        i++;
                    }
                    break;
                case "--preview":
                {
                    int? w = null, h = null;
                    if (i + 1 < args.Length && SizeRegex().Match(args[i + 1]) is { Success: true } size)
                    {
                        w = int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture);
                        h = int.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture);
                        i++;
                    }
                    o._overrides.Add(s =>
                    {
                        s.Preview = true;
                        s.PreviewWidth = w ?? s.PreviewWidth;
                        s.PreviewHeight = h ?? s.PreviewHeight;
                    });
                    break;
                }
                default:
                    throw new ArgumentException($"Argument inconnu : {arg}");
            }
        }
        return o;
    }

    private void SetVirtualAction(string argument, string action)
    {
        if (VirtualAction != null)
            throw new ArgumentException($"Une seule action d'écran virtuel à la fois ({argument})");
        VirtualAction = action;
    }

    private static double ParseDouble(string name, string value)
        => double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double result) && result >= 0
            ? result
            : throw new ArgumentException($"Nombre invalide pour {name} : {value}");

    private static double ParseSigned(string name, string value, double min, double max)
        => double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double result) && result >= min && result <= max
            ? result
            : throw new ArgumentException($"Valeur invalide pour {name} : {value} ({min} à {max})");

    private static IntPtr ParseHandle(string value)
    {
        bool hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (long.TryParse(hex ? value[2..] : value, hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out long handle))
            return new IntPtr(handle);
        throw new ArgumentException($"Handle invalide : {value}");
    }

    private static int[] ParseCrop(string value)
    {
        var parts = value.Split(',', ';');
        if (parts.Length != 4 || !parts.All(p => int.TryParse(p.Trim(), out int n) && n >= 0))
            throw new ArgumentException($"Recadrage invalide : {value} (attendu gauche,haut,droite,bas)");
        return parts.Select(p => int.Parse(p.Trim(), CultureInfo.InvariantCulture)).ToArray();
    }

    [GeneratedRegex("^(\\d+)x(\\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();
}
