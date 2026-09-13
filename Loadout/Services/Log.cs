using System;
using System.IO;
using System.Text;

namespace Loadout.Services;

/// <summary>
/// Log rotativo a disco (%LocalAppData%\Loadout\logs\loadout.log).
/// Thread-safe y best-effort: nunca lanza excepciones ni rompe la app.
/// Archivo que supera ~1,5 MB se vuelca a loadout.bak.log y se reinicia.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1_500_000;
    private static readonly object Gate = new();
    private static string _file = string.Empty;

    public static string FilePath
    {
        get
        {
            if (_file.Length > 0) return _file;
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Loadout", "logs");
            try { Directory.CreateDirectory(dir); } catch { }
            _file = Path.Combine(dir, "loadout.log");
            return _file;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
    {
        string line = ex == null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}";
        Write("ERROR", line);
    }

    public static void Error(string message, string detail)
        => Write("ERROR", $"{message} :: {detail}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                RotateIfNeeded();
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch { /* el log nunca debe romper la app */ }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var fi = new FileInfo(FilePath);
            if (!fi.Exists || fi.Length <= MaxBytes) return;
            string bak = Path.Combine(fi.DirectoryName!, "loadout.bak.log");
            try { File.Copy(fi.FullName, bak, true); } catch { }
            try { fi.Delete(); } catch { }
        }
        catch { }
    }
}