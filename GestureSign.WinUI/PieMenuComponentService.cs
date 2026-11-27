using GestureSign.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.WinUI;

internal static class PieMenuComponentService
{
    internal const string VersionId = "0.7.13";
    private const string PayloadInventory = "payload-files.txt";
    private const int MaxArchiveSize = 48 * 1024 * 1024;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(4) };
    private static readonly Version MinimumVersion = new(0, 7, 0);
    private static readonly Version CurrentVersion = Version.Parse(VersionId);
    private static readonly string[] RequiredFiles =
    [
        "App.xbf", "KandoWeb/index.html", "KandoWeb/renderer.js",
        "KandoWeb/catalog.json", "KandoWeb/windows-key-codes.json", "KandoWeb/locales/en/translation.json",
        "KandoWeb/renderer.css", "KandoWeb/material-symbols-rounded.woff2", "KandoWeb/SimpleIcons.woff2",
        "Microsoft.Web.WebView2.Core.dll", "WinRT.Runtime.dll",
        "EditorAssets/editor-strings.json", "EditorAssets/PieMenu.ico", "EditorAssets/example-windows.json"
    ];
    private static string Name => $"GestureSign-PieMenu-{VersionId}-win-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}.zip";

    static PieMenuComponentService() => Client.DefaultRequestHeaders.UserAgent.ParseAdd("GestureSign-PieMenu");

    internal static bool IsCompatible(string? executable) => IsCompatible(executable, MinimumVersion);

    internal static bool NeedsManagedRepair(string? executable, string installRoot)
    {
        var directory = ManagedVersionDirectory(executable, installRoot);
        if (directory is null) return false;

        var version = Version.Parse(Path.GetFileName(directory));
        return version <= CurrentVersion && (version == CurrentVersion ? !IsCurrent(executable) : !IsCompatible(executable));
    }

    internal static string? ManagedVersionDirectory(string? executable, string installRoot)
    {
        if (string.IsNullOrWhiteSpace(executable)) return null;
        try
        {
            var fullPath = Path.GetFullPath(executable);
            var directory = Path.GetDirectoryName(fullPath);
            if (directory is null ||
                !string.Equals(Path.GetDirectoryName(directory), Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot)), StringComparison.OrdinalIgnoreCase) ||
                !Version.TryParse(Path.GetFileName(directory), out _))
                return null;

            var filename = Path.GetFileName(fullPath);
            return filename.Equals("GestureSignV2.PieMenu.exe", StringComparison.OrdinalIgnoreCase) ||
                filename.Equals("GestureSign.PieMenu.exe", StringComparison.OrdinalIgnoreCase)
                ? directory : null;
        }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (PathTooLongException) { return null; }
    }

    internal static async Task UninstallAsync(string executable, string installRoot, Func<Task> beforeRemove)
    {
        var directory = ManagedVersionDirectory(executable, installRoot)
            ?? throw new InvalidOperationException("Only the managed Pie Menu component can be uninstalled.");
        if (IsReparsePoint(installRoot) || IsReparsePoint(directory))
            throw new IOException("The Pie Menu installation has been redirected.");
        using var lease = KandoComponentPaths.AcquireUpdateLease(Path.Combine(installRoot, ".installing"));

        await beforeRemove();
        await Task.Run(() =>
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        });
    }

    private static bool IsReparsePoint(string directory) =>
        Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0;

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
                HasRuntimeFiles(executable) && HasRequiredAssets(Path.GetDirectoryName(executable)!);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    private static bool HasRequiredAssets(string directory)
    {
        if (!RequiredFiles.All(file => File.Exists(Path.Combine(directory, file))) ||
            !HasSharedAssemblies(directory) ||
            (!File.Exists(Path.Combine(directory, "KandoWeb", "native-icons.zip")) &&
             !File.Exists(Path.Combine(directory, "KandoWeb", "native-icons.json"))))
            return false;

        // New packages list every delivered file. Older releases use the fixed requirements above.
        var inventory = Path.Combine(directory, PayloadInventory);
        if (!File.Exists(inventory)) return true;
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        foreach (var entry in File.ReadLines(inventory))
        {
            if (string.IsNullOrWhiteSpace(entry) || Path.IsPathRooted(entry)) return false;
            var file = Path.GetFullPath(entry, prefix);
            if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
                return false;
        }
        return true;
    }

    private static bool HasSharedAssemblies(string directory) =>
        (File.Exists(Path.Combine(directory, "Microsoft.Windows.SDK.NET.dll")) &&
         File.Exists(Path.Combine(directory, "Microsoft.WinUI.dll"))) ||
        (File.Exists(Path.Combine(directory, "shared-assemblies.json")) &&
         File.Exists(Path.Combine(directory, "shared-assemblies.zip")));

    private static void ValidateSharedAssemblyFallback(string directory)
    {
        var manifest = Path.Combine(directory, "shared-assemblies.json");
        if (!File.Exists(manifest)) return;
        var checksums = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifest))
            ?? throw new InvalidDataException("Missing Pie Menu assembly checksums.");
        using var archive = ZipFile.OpenRead(Path.Combine(directory, "shared-assemblies.zip"));
        var required = new[] { "Microsoft.Windows.SDK.NET.dll", "Microsoft.WinUI.dll" };
        if (!required.All(checksums.ContainsKey))
            throw new InvalidDataException("Missing Pie Menu assembly checksums.");
        foreach (var (filename, checksum) in checksums)
        {
            var entry = archive.GetEntry(filename);
            if (entry is null || entry.Length is <= 0 or > 32 * 1024 * 1024)
                throw new InvalidDataException($"Missing fallback for {filename}.");
            using var stream = entry.Open();
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(checksum, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The fallback for {filename} failed verification.");
        }
    }

    private static bool HasRuntimeFiles(string executable)
    {
        var stem = Path.Combine(Path.GetDirectoryName(executable)!, Path.GetFileNameWithoutExtension(executable));
        return File.Exists(stem + ".dll") && File.Exists(stem + ".runtimeconfig.json") && File.Exists(stem + ".deps.json") &&
               File.Exists(stem + ".pri");
    }

    internal static Task<string?> DownloadAsync(IProgress<double> progress, CancellationToken token, Func<Task>? beforeReplace = null) =>
        DownloadIntoDirectoryAsync(KandoComponentPaths.PieMenuInstallDirectory, progress, token, beforeReplace);

    internal static async Task<string?> DownloadIntoDirectoryAsync(string installRoot, IProgress<double> progress, CancellationToken token, Func<Task>? beforeReplace = null)
    {
        if (RuntimeInformation.OSArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("The pie menu supports Windows x64 and ARM64.");
        var release = $"https://github.com/Tomclanc/GestureSignv2/releases/download/v18.4.15/";
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
        Directory.CreateDirectory(installRoot);
        var temporary = Path.Combine(installRoot, $"{Guid.NewGuid():N}.zip");
        try
        {
            var url = new Uri(release + Name);
            await KandoDownloadClient.DownloadAsync(Client, url, temporary, progress, token, maxBytes: MaxArchiveSize);
            await using (var archive = File.OpenRead(temporary))
            {
                if (!Convert.ToHexString(await SHA256.HashDataAsync(archive, token)).Equals(digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded component does not match the release checksum.");
            }
            return await InstallInDirectoryAsync(temporary, installRoot, token, beforeReplace);
        }
        finally { File.Delete(temporary); }
    }

    internal static Task<string> InstallAsync(string archivePath, CancellationToken token = default,
        Func<Task>? beforeReplace = null, Func<Task>? restore = null) =>
        InstallInDirectoryAsync(archivePath, KandoComponentPaths.PieMenuInstallDirectory, token,
            beforeReplace, replaceExisting: true, restore: restore);

    internal static Task<string> InstallInDirectoryAsync(string archivePath, string installRoot,
        CancellationToken token = default, Func<Task>? beforeReplace = null,
        bool replaceExisting = false, Func<Task>? restore = null) =>
        Task.Run(async () =>
        {
            var filename = "GestureSignV2.PieMenu.exe";
            var versionDirectory = Path.Combine(installRoot, VersionId);
            var installed = Path.Combine(versionDirectory, filename);
            Directory.CreateDirectory(installRoot);
            using var lease = KandoComponentPaths.AcquireUpdateLease(Path.Combine(installRoot, ".installing"));
            if (!replaceExisting && IsCurrent(installed)) return installed;
            var staging = Path.Combine(installRoot, $".install-{Guid.NewGuid():N}");
            try
            {
                if (new FileInfo(archivePath).Length > MaxArchiveSize) throw new InvalidDataException("The component ZIP is too large.");
                using var archive = ZipFile.OpenRead(archivePath);
                if (archive.Entries.Count > 4096 || archive.Entries.Sum(entry => entry.Length) > 128L * 1024 * 1024)
                    throw new InvalidDataException("The component ZIP exceeds its extraction limit.");
                archive.ExtractToDirectory(staging);
                if (!IsCurrent(Path.Combine(staging, filename)))
                    throw new InvalidDataException($"Use the complete Pie Menu {VersionId} ZIP for your Windows architecture.");
                ValidateSharedAssemblyFallback(staging);
                token.ThrowIfCancellationRequested();
                await KandoInstallation.ReplaceAsync(staging, versionDirectory, userData: null,
                    beforeReplace, validate: null, stop: null, restore: restore);
                return installed;
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
        }, token);
}
