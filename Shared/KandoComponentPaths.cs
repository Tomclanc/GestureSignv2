using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace GestureSign.Shared
{
    internal static class KandoComponentPaths
    {
        private const string ProductFolderName = "GestureSign V2";
        private const uint KfFlagNoPackageRedirection = 0x00010000;
        private const int ErrorInsufficientBuffer = 122;
        private static readonly Guid LocalAppDataFolderId = new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
        private static readonly Guid RoamingAppDataFolderId = new Guid("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D");

        public static string NativeLocalApplicationData => GetNativeKnownFolder(
            LocalAppDataFolderId,
            Environment.SpecialFolder.LocalApplicationData);

        public static string NativeRoamingApplicationData => GetNativeKnownFolder(
            RoamingAppDataFolderId,
            Environment.SpecialFolder.ApplicationData);

        public static string ComponentsRoot => Path.Combine(
            ComponentDataRoot,
            ProductFolderName,
            "Components");

        public static string InstallDirectory => Path.Combine(ComponentsRoot, "Kando");

        public static string RemovedMarkerPath => Path.Combine(ComponentsRoot, "Kando.removed");

        public static string UserDataDirectory => Path.Combine(NativeRoamingApplicationData, "kando");

        private static string ComponentDataRoot
        {
            get
            {
                var packageFamilyName = GetPackageFamilyName();
                return string.IsNullOrWhiteSpace(packageFamilyName)
                    ? NativeLocalApplicationData
                    : Path.Combine(NativeLocalApplicationData, "Packages", packageFamilyName, "LocalState");
            }
        }

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

        private static string GetNativeKnownFolder(Guid folderId, Environment.SpecialFolder fallback)
        {
            IntPtr pathPointer = IntPtr.Zero;
            try
            {
                if (SHGetKnownFolderPath(ref folderId, KfFlagNoPackageRedirection, IntPtr.Zero, out pathPointer) >= 0 &&
                    pathPointer != IntPtr.Zero)
                {
                    var path = Marshal.PtrToStringUni(pathPointer);
                    if (!string.IsNullOrWhiteSpace(path))
                        return path;
                }
            }
            catch
            {
            }
            finally
            {
                if (pathPointer != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pathPointer);
            }

            return Environment.GetFolderPath(fallback);
        }

        private static string? GetPackageFamilyName()
        {
            uint length = 0;
            if (GetCurrentPackageFamilyName(ref length, null) != ErrorInsufficientBuffer || length == 0)
                return null;

            var value = new StringBuilder((int)length);
            return GetCurrentPackageFamilyName(ref length, value) == 0 ? value.ToString() : null;
        }
        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(
            ref Guid rfid,
            uint dwFlags,
            IntPtr hToken,
            out IntPtr ppszPath);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFamilyName(
            ref uint packageFamilyNameLength,
            StringBuilder? packageFamilyName);
    }
}
