using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Loadout.Services;

public sealed record ShortcutEntry(string Name, string Kind, string Target, string Arguments, string Source);

public static class ShortcutScannerService
{
    private const int MaxItems = 300;

    public static Task<List<ShortcutEntry>> ScanAsync()
        => Task.Run(() =>
        {
            var results = new List<ShortcutEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirs = new List<string>();

            void AddDir(Environment.SpecialFolder f)
            {
                try
                {
                    string p = Environment.GetFolderPath(f);
                    if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) dirs.Add(p);
                }
                catch { }
            }

            AddDir(Environment.SpecialFolder.Desktop);
            AddDir(Environment.SpecialFolder.CommonDesktopDirectory);
            AddDir(Environment.SpecialFolder.StartMenu);
            AddDir(Environment.SpecialFolder.CommonStartMenu);

            foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // Enumeración segura: IgnoreInaccessible salta carpetas sin permiso
                // (EnumerateFiles es perezoso: el error sale al iterar, no al crear).
                List<string> files = new();
                try
                {
                    var options = new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.System
                    };
                    foreach (var f in Directory.EnumerateFiles(dir, "*.lnk", options))
                        files.Add(f);
                    foreach (var f in Directory.EnumerateFiles(dir, "*.url", options))
                        files.Add(f);
                }
                catch { }

                foreach (var file in files)
                {
                    if (results.Count >= MaxItems) break;
                    try
                    {
                        var entry = ParseFile(file);
                        if (entry == null) continue;
                        if (IsJunk(entry)) continue;
                        string key = entry.Kind + "|" + entry.Target;
                        if (!seen.Add(key)) continue;
                        results.Add(entry);
                    }
                    catch { }
                }
                if (results.Count >= MaxItems) break;
            }

            return results.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        });

    private static bool IsJunk(ShortcutEntry e)
    {
        string n = e.Name.ToLowerInvariant();
        if (n.Contains("uninstall") || n.Contains("desinstalar") || n.Contains("ayuda")
            || n.Contains("help") || n.Contains("leeme") || n.Contains("readme")) return true;
        if (string.IsNullOrWhiteSpace(e.Target)) return true;
        return false;
    }

    private static ShortcutEntry? ParseFile(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            string? url = ParseUrlFile(path);
            if (string.IsNullOrWhiteSpace(url)) return null;
            return new ShortcutEntry(name, "Url", url, string.Empty, path);
        }
        // .lnk vía WScript.Shell (sin NuGet extra, COM del sistema)
        var (target, args) = ResolveLnk(path);
        if (string.IsNullOrWhiteSpace(target)) return null;
        string kind = GuessKind(target);
        return new ShortcutEntry(name, kind, target, args ?? string.Empty, path);
    }

    private static string? ParseUrlFile(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var t = line.Trim();
                if (t.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    return t.Substring(4).Trim();
            }
        }
        catch { }
        return null;
    }

    private static (string? Target, string? Args) ResolveLnk(string lnkPath)
    {
        try
        {
            Type? t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return (null, null);
            dynamic? shell = Activator.CreateInstance(t);
            if (shell == null) return (null, null);
            try
            {
                dynamic sc = shell.CreateShortcut(lnkPath);
                string target = (string)sc.TargetPath;
                string args = (string)sc.Arguments;
                try { Marshal.ReleaseComObject(sc); } catch { }
                return (target?.Trim(), args?.Trim());
            }
            finally { try { Marshal.ReleaseComObject(shell); } catch { } }
        }
        catch { return (null, null); }
    }

    private static string GuessKind(string target)
    {
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "Url";
        if (target.StartsWith("steam://", StringComparison.OrdinalIgnoreCase)) return "Steam";
        try
        {
            if (Directory.Exists(target)) return "Folder";
            if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "Exe";
        }
        catch { }
        return "File";
    }
}
