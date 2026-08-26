using System;
using System.IO;
using System.Linq;

namespace GestureSign.Shared
{
    internal static class KandoComponentPaths
    {
        private const string ProductFolderName = "GestureSign V2";

        public static string ComponentsRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductFolderName,
            "Components");

        public static string InstallDirectory => Path.Combine(ComponentsRoot, "Kando");

        public static string RemovedMarkerPath => Path.Combine(ComponentsRoot, "Kando.removed");

        public static string? FindExecutable(string configuredPath, string baseDirectory)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
                return configuredPath;

            var componentExecutable = FindExecutableUnder(InstallDirectory);
            if (componentExecutable != null)
                return componentExecutable;

            if (File.Exists(RemovedMarkerPath))
                return null;

            return BundledCandidates(baseDirectory).FirstOrDefault(File.Exists);
        }

        public static string? FindBundledExecutable(string baseDirectory)
            => BundledCandidates(baseDirectory).FirstOrDefault(File.Exists);

        public static string? FindExecutableUnder(string directory)
        {
            if (!Directory.Exists(directory))
                return null;

            foreach (var name in new[] { "kando.exe", "Kando.exe" })
            {
                var direct = Path.Combine(directory, name);
                if (File.Exists(direct))
                    return direct;
            }

            try
            {
                return Directory.EnumerateFiles(directory, "*.exe", SearchOption.AllDirectories)
                    .FirstOrDefault(path => string.Equals(Path.GetFileName(path), "kando.exe", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static string[] BundledCandidates(string baseDirectory)
        {
            var parentDirectory = Path.GetFullPath(Path.Combine(baseDirectory, ".."));
            return
            [
                Path.Combine(baseDirectory, "Kando", "kando.exe"),
                Path.Combine(baseDirectory, "Kando", "Kando.exe"),
                Path.Combine(baseDirectory, "Kando", "Kando-win32-x64", "kando.exe"),
                Path.Combine(baseDirectory, "Kando", "Kando-win32-x64", "Kando.exe"),
                Path.Combine(baseDirectory, "Kando", "Kando-win32-arm64", "kando.exe"),
                Path.Combine(baseDirectory, "Kando", "Kando-win32-arm64", "Kando.exe"),
                Path.Combine(baseDirectory, "kando.exe"),
                Path.Combine(baseDirectory, "Kando.exe"),
                Path.Combine(parentDirectory, "Kando", "kando.exe"),
                Path.Combine(parentDirectory, "Kando", "Kando.exe"),
                Path.Combine(parentDirectory, "Kando", "Kando-win32-x64", "kando.exe"),
                Path.Combine(parentDirectory, "Kando", "Kando-win32-arm64", "kando.exe")
            ];
        }
    }
}
