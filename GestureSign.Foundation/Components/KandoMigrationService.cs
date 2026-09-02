using System.IO;

namespace GestureSign.Foundation.Components;

public static class KandoMigrationService
{
    public static string? FindLegacyInstallation(IEnumerable<string> candidates, string destination, bool destinationInstalled, bool removedMarkerExists, Func<string, bool> isValid)
    {
        if (destinationInstalled || removedMarkerExists) return null;
        foreach (var candidate in candidates)
            if (!PathsEqual(candidate, destination) && Directory.Exists(candidate) && isValid(candidate)) return candidate;
        return null;
    }

    public static void MergePersistentUserData(string source, string destination)
    {
        if (!Directory.Exists(source) || PathsEqual(source, destination)) return;
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) if (!Path.GetFileName(file).Equals("ipc-info.json", StringComparison.OrdinalIgnoreCase)) CopyFileIfMissing(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(source)) if (!Path.GetFileName(dir).Equals("session", StringComparison.OrdinalIgnoreCase)) CopyDirectoryIfMissing(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static void CopyDirectoryIfMissing(string source, string destination) { Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source)) CopyFileIfMissing(file, Path.Combine(destination, Path.GetFileName(file))); foreach (var dir in Directory.EnumerateDirectories(source)) CopyDirectoryIfMissing(dir, Path.Combine(destination, Path.GetFileName(dir))); }
    private static void CopyFileIfMissing(string source, string destination) { if (!File.Exists(destination)) { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination); } }
    private static bool PathsEqual(string left, string right) => string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}
