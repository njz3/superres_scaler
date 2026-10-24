// Copyright (c) Benjamin Maurin (njz3) 2026

namespace SuperResScaler;

/// <summary>Minimal thread-safe file logger (SuperResScaler.log next to the settings).</summary>
internal static class Log
{
    private const long MaxSize = 1_000_000;
    private static readonly object Sync = new();
    private static string? _path;

    public static void Initialize(string directory)
    {
        _path = Path.Combine(directory, "SuperResScaler.log");
        try
        {
            var file = new FileInfo(_path);
            if (file.Exists && file.Length > MaxSize)
                file.Delete();
        }
        catch
        {
            // Logging must never break the application.
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex == null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        System.Diagnostics.Debug.WriteLine(line);
        if (_path == null)
            return;
        lock (Sync)
        {
            try { File.AppendAllText(_path, line + Environment.NewLine); }
            catch { /* ignored */ }
        }
    }
}
