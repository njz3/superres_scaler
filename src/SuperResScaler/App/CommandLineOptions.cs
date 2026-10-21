using System.Globalization;
using System.Text.RegularExpressions;
using SuperResScaler.Config;
using SuperResScaler.Native;

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
          --mode stretch|aspect      étirer en plein écran / conserver le ratio
          --display-aspect 4:3       ratio physique de l'écran
          --source-aspect auto|16:9  ratio de l'image source (mode aspect)
          --crop g,h,d,b             recadrage manuel en pixels
          --autocrop                 recadrage automatique des bandes noires
          --window                   capture la fenêtre entière (pas seulement la zone client)
          --cursor                   capture le curseur
          --no-vsync                 désactive la synchro verticale
          --monitor <n>              écran de sortie (1..N, 0 = celui de la fenêtre)
          --preview [LxH]            fenêtre de prévisualisation au lieu du plein écran

        Diagnostic :
          --selftest <dossier>       rend une mire avec chaque shader et écrit des PNG + un rapport
          --selftest-overlay         ajoute un test de l'overlay plein écran (~2 s)
        """;

    public bool Help { get; private set; }
    public bool Tray { get; private set; }
    public string? SelfTestDirectory { get; private set; }
    public bool SelfTestOverlay { get; private set; }

    public string? Process { get; private set; }
    public string? Title { get; private set; }
    public string? ClassName { get; private set; }
    public IntPtr Hwnd { get; private set; }
    public bool Foreground { get; private set; }
    public double WaitSeconds { get; private set; } = 60;
    public double? DelaySeconds { get; private set; }
    public bool Persist { get; private set; }
    public string? Profile { get; private set; }

    private readonly List<Action<ScalingOptions>> _overrides = new();

    public bool IsHeadless => Process != null || Title != null || ClassName != null || Hwnd != IntPtr.Zero || Foreground;

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
                    ScaleMode mode = v switch
                    {
                        "stretch" or "fill" => ScaleMode.Stretch,
                        "aspect" or "keepaspect" => ScaleMode.KeepAspect,
                        _ => throw new ArgumentException($"Mode inconnu : {v} (stretch ou aspect)"),
                    };
                    o._overrides.Add(s => s.Mode = mode);
                    break;
                }
                case "--display-aspect":
                {
                    string v = Value();
                    if (!AspectRatio.TryParse(v, out _))
                        throw new ArgumentException($"Ratio invalide : {v}");
                    o._overrides.Add(s => s.DisplayAspect = v);
                    break;
                }
                case "--source-aspect":
                {
                    string v = Value();
                    if (!AspectRatio.IsAuto(v) && !AspectRatio.TryParse(v, out _))
                        throw new ArgumentException($"Ratio invalide : {v}");
                    o._overrides.Add(s => s.SourceAspect = v);
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
                case "--monitor": { int m = (int)ParseDouble(arg, Value()); o._overrides.Add(s => s.Monitor = m); break; }
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

    private static double ParseDouble(string name, string value)
        => double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double result) && result >= 0
            ? result
            : throw new ArgumentException($"Nombre invalide pour {name} : {value}");

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
