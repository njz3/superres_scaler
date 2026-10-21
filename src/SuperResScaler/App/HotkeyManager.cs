using SuperResScaler.Native;

namespace SuperResScaler.App;

/// <summary>System-wide hotkeys (RegisterHotKey) delivered to a message-only window on the UI thread.</summary>
internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyManager()
    {
        CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
    }

    /// <summary>Registers e.g. "Alt+Shift+A". Returns an error message, or null on success.</summary>
    public string? Register(string? spec, Action action)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return null; // disabled
        if (!TryParse(spec, out uint modifiers, out uint key))
            return $"Raccourci invalide : {spec}";
        int id = _nextId++;
        if (!User32.RegisterHotKey(Handle, id, modifiers | User32.MOD_NOREPEAT, key))
            return $"Raccourci {spec} déjà utilisé par une autre application";
        _actions[id] = action;
        return null;
    }

    public void UnregisterAll()
    {
        foreach (int id in _actions.Keys)
            User32.UnregisterHotKey(Handle, id);
        _actions.Clear();
    }

    public static bool TryParse(string spec, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        foreach (string rawPart in spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (rawPart.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= User32.MOD_CONTROL; break;
                case "alt": modifiers |= User32.MOD_ALT; break;
                case "shift": modifiers |= User32.MOD_SHIFT; break;
                case "win": case "windows": modifiers |= User32.MOD_WIN; break;
                default:
                    if (key != 0)
                        return false;
                    string part = rawPart.Length == 1 && char.IsDigit(rawPart[0]) ? "D" + rawPart : rawPart;
                    if (!Enum.TryParse(part, true, out Keys parsed))
                        return false;
                    key = (uint)(parsed & Keys.KeyCode);
                    break;
            }
        }
        return key != 0;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == User32.WM_HOTKEY && _actions.TryGetValue((int)m.WParam, out var action))
        {
            try { action(); }
            catch (Exception ex) { Log.Error("Hotkey action failed", ex); }
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
