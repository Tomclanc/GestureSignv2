using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace GestureSign.WinUI.Services;

/// <summary>
/// Finds the executable behind a process file name such as "pulse.exe". A radial menu stores the
/// names of the programs it belongs to, while the shell needs a path to read an icon, so the two
/// are matched here: a running process first, then the registered App Paths entry, then the
/// Windows folder and PATH. Results are cached because the list rebuilds on every refresh.
/// </summary>
internal static class ProgramPathResolver
{
    private static readonly Dictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string? Resolve(string? program)
    {
        if (string.IsNullOrWhiteSpace(program))
            return null;
        var name = program.Trim().Trim('"');
        if (name.Length == 0)
            return null;

        // An entry may already hold a path (or a path from an older configuration).
        if (name.IndexOf(Path.DirectorySeparatorChar) >= 0 || name.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            return File.Exists(name) ? Path.GetFullPath(name) : null;

        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name += ".exe";
        if (Cache.TryGetValue(name, out var cached))
            return cached;
        var found = Find(name);
        Cache[name] = found;
        return found;
    }

    private static string? Find(string name)
    {
        var processName = Path.GetFileNameWithoutExtension(name);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                var path = KandoProcessPath.Read(process);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
        }

        var registered = FromAppPaths(name);
        if (registered is not null)
            return registered;

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var candidate in new[] { Path.Combine(windows, name), Path.Combine(windows, "System32", name) })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;
            string candidate;
            try
            {
                candidate = Path.Combine(directory.Trim(), name);
            }
            catch
            {
                // A malformed PATH entry is skipped rather than failing the whole lookup.
                continue;
            }
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static string? FromAppPaths(string name)
    {
        const string appPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\";
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = root.OpenSubKey(appPaths + name);
                if (key?.GetValue(null) is string path && File.Exists(path))
                    return path;
            }
            catch
            {
                // A redirected or unreadable key only means the icon falls back to a glyph.
            }
        }
        return null;
    }
}
