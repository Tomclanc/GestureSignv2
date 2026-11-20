using GestureSign.Shared;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.WinUI;

internal static class PieMenuComponentService
{
    internal const string VersionId = "0.7.11";
    private const int MaxArchiveSize = 48 * 1024 * 1024;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(4) };
    private static readonly Version MinimumVersion = new(0, 7, 0);
    private static readonly Version CurrentVersion = Version.Parse(VersionId);
    private static readonly string[] RequiredFiles =
    [

        "App.xbf", "KandoWeb/index.html", "KandoWeb/renderer.js",
        "KandoWeb/catalog.json", "KandoWeb/windows-key-codes.json", "KandoWeb/native-icons.json", "KandoWeb/locales/en/translation.json",
        "EditorAssets/editor-strings.json", "EditorAssets/PieMenu.ico", "EditorAssets/example-windows.json"
    ];
    private static string VersionDirectory => Path.Combine(KandoComponentPaths.PieMenuInstallDirectory, VersionId);
    private static string Name => $"GestureSign-PieMenu-{VersionId}-preview-win-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}.zip";

    static PieMenuComponentService() => Client.DefaultRequestHeaders.UserAgent.ParseAdd("GestureSign-PieMenu");

    internal static bool IsCompatible(string? executable) => IsCompatible(executable, MinimumVersion);

    internal static bool IsCurrent(string? executable) =>
        IsCompatible(executable, CurrentVersion) &&
        UiTranslationCatalog.SupportedCultureNames.All(culture =>
            File.Exists(Path.Combine(Path.GetDirectoryName(executable!)!, "KandoWeb", "locales", culture, "translation.json")));

    private static bool IsCompatible(string? executable, Version minimumVersion)
    {
        if (executable is null || !KandoComponentPaths.IsNativePieMenu(executable)) return false;
        try
        {
            var expectedMachine = RuntimeInformation.OSArchitecture switch { Architecture.X64 => 0x8664, Architecture.Arm64 => 0xAA64, _ => 0 };
            return expectedMachine != 0 && KandoExecutableCompatibility.ReadMachine(executable) == expectedMachine &&
                Version.TryParse(FileVersionInfo.GetVersionInfo(executable).FileVersion, out var version) &&
                version >= minimumVersion &&
                HasRuntimeFiles(executable) && RequiredFiles.All(file => File.Exists(Path.Combine(Path.GetDirectoryName(executable)!, file)));
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool HasRuntimeFiles(string executable)
    {
        var stem = Path.Combine(Path.GetDirectoryName(executable)!, Path.GetFileNameWithoutExtension(executable));
        return File.Exists(stem + ".dll") && File.Exists(stem + ".runtimeconfig.json") &&
               File.Exists(stem + ".pri");
    }

    internal static async Task<string?> DownloadAsync(IProgress<double> progress, CancellationToken token)
    {
        if (RuntimeInformation.OSArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("The pie menu supports Windows x64 and ARM64.");
        var release = $"https://github.com/Tomclanc/GestureSignv2/releases/download/pie-menu-v{VersionId}/";
        using var response = await Client.GetAsync(release + "SHA256SUMS.txt", HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var checksum = await response.Content.ReadAsStreamAsync(token);
        var buffer = new byte[4097];
        var count = await checksum.ReadAtLeastAsync(buffer, buffer.Length, false, token);
        if (count == buffer.Length) throw new InvalidDataException("The component checksum file is too large.");
        var entries = Encoding.ASCII.GetString(buffer, 0, count).Split('\n')
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var digest = entries.FirstOrDefault(entry => entry.Length == 2 && entry[1] == Name)?[0];
        if (digest is null || digest.Length != 64 || !digest.All(Uri.IsHexDigit))
            throw new InvalidDataException("The release is missing a valid component SHA-256 digest.");
        Directory.CreateDirectory(KandoComponentPaths.PieMenuInstallDirectory);
        var temporary = Path.Combine(KandoComponentPaths.PieMenuInstallDirectory, $"{Guid.NewGuid():N}.zip");
        try
        {
            var url = new Uri(release + Name);
            await KandoDownloadClient.DownloadAsync(Client, url, temporary, progress, token, maxBytes: MaxArchiveSize);
            await using (var archive = File.OpenRead(temporary))
            {
                if (!Convert.ToHexString(await SHA256.HashDataAsync(archive, token)).Equals(digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded component does not match the release checksum.");
            }
            return await InstallAsync(temporary, token);
        }
        finally { File.Delete(temporary); }
    }

    internal static Task<string> InstallAsync(string archivePath, CancellationToken token = default) =>
        Task.Run(() =>
        {
            var filename = "GestureSignV2.PieMenu.exe";
            var installed = Path.Combine(VersionDirectory, filename);
            Directory.CreateDirectory(KandoComponentPaths.PieMenuInstallDirectory);
            using var lease = KandoComponentPaths.AcquireUpdateLease(Path.Combine(KandoComponentPaths.PieMenuInstallDirectory, ".installing"));
            if (IsCurrent(installed)) return installed;
            var staging = Path.Combine(KandoComponentPaths.PieMenuInstallDirectory, $".install-{Guid.NewGuid():N}");
            try
            {
                if (new FileInfo(archivePath).Length > MaxArchiveSize) throw new InvalidDataException("The component ZIP is too large.");
                using var archive = ZipFile.OpenRead(archivePath);
                if (archive.Entries.Count > 4096 || archive.Entries.Sum(entry => entry.Length) > 128L * 1024 * 1024)
                    throw new InvalidDataException("The component ZIP exceeds its extraction limit.");
                archive.ExtractToDirectory(staging);
                if (!IsCurrent(Path.Combine(staging, filename)))
                    throw new InvalidDataException($"Use the complete Pie Menu {VersionId} ZIP for your Windows architecture.");
                token.ThrowIfCancellationRequested();
                Directory.Move(staging, VersionDirectory);
                return installed;
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
        }, token);
}
