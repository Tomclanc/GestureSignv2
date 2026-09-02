using System.IO;

namespace GestureSign.Foundation.Configuration;

public static class ConfigurationMigrationService
{
    public static int CopyMissingTree(string legacyPath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(legacyPath) || string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(legacyPath)) return 0;
        var copied = 0;
        Directory.CreateDirectory(targetPath);
        foreach (var file in Directory.EnumerateFiles(legacyPath, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(targetPath, Path.GetRelativePath(legacyPath, file));
            if (File.Exists(destination)) continue;
            try { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination); copied++; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return copied;
    }
}
