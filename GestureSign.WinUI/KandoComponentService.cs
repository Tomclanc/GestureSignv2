using GestureSign.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.WinUI;

internal static class KandoComponentService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly SemaphoreSlim InstallationLock = new(1, 1);

    public static bool IsManagedExecutable(string? executable)
        => executable is not null && PathsEqual(executable,
            KandoComponentPaths.FindExecutableUnder(KandoComponentPaths.InstallDirectory) ?? "");

    private static readonly KandoReleaseClient ReleaseClient = new(Client, RuntimeInformation.OSArchitecture);

    public static async Task<KandoRelease> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try { return await ReleaseClient.GetLatestAsync(timeout.Token); }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        { throw new KandoReleaseLookupException(false, error); }
    }
    public static bool IsInstalled
        => KandoComponentPaths.FindExecutable(string.Empty, AppContext.BaseDirectory) is not null;

    public static bool IsDownloaded
        => KandoComponentPaths.FindExecutableUnder(KandoComponentPaths.InstallDirectory) is not null;

    public static bool HasPersistentUserData
        => File.Exists(Path.Combine(KandoComponentPaths.UserDataDirectory, "config.json")) ||
           File.Exists(Path.Combine(KandoComponentPaths.UserDataDirectory, "menus.json"));

    public static async Task PreserveBundledInstallationAsync(bool preserveLegacyInstallation)
    {
        await Task.Run(MigrateLegacyStoreData);
        preserveLegacyInstallation |= HasPersistentUserData;

        if (IsDownloaded || File.Exists(KandoComponentPaths.RemovedMarkerPath) || !preserveLegacyInstallation)
            return;

        var bundledExecutable = KandoComponentPaths.FindBundledExecutable(AppContext.BaseDirectory);
        if (bundledExecutable is not null)
        {
            var sourceDirectory = Path.GetDirectoryName(bundledExecutable);
            if (!string.IsNullOrWhiteSpace(sourceDirectory))
                await Task.Run(() => CopyDirectoryAtomically(sourceDirectory, KandoComponentPaths.InstallDirectory));
            return;
        }

        var migrationArchive = Path.Combine(AppContext.BaseDirectory, "KandoMigrationPayload.zip");
        if (File.Exists(migrationArchive))
            await Task.Run(() => InstallFromArchive(migrationArchive));
    }

    public static async Task DownloadAndInstallAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        Func<Task>? beforeReplace = null,
        Func<Task>? validate = null,
        Func<Task>? stop = null,
        Func<Task>? restore = null, KandoRelease? release = null)
    {
        await InstallationLock.WaitAsync(cancellationToken);
        try
        {
            await DownloadAndInstallCoreAsync(progress, cancellationToken, beforeReplace, validate, stop, restore, release);
        }
        finally { InstallationLock.Release(); }
    }

    private static async Task DownloadAndInstallCoreAsync(IProgress<double>? progress, CancellationToken cancellationToken,
        Func<Task>? beforeReplace, Func<Task>? validate, Func<Task>? stop, Func<Task>? restore, KandoRelease? release)
    {
        var asset = release ?? await GetLatestReleaseAsync(cancellationToken);
        Directory.CreateDirectory(KandoComponentPaths.ComponentsRoot);
        var archivePath = Path.Combine(KandoComponentPaths.ComponentsRoot, $"Kando-{Guid.NewGuid():N}.zip");
        var stagingRoot = Path.Combine(KandoComponentPaths.ComponentsRoot, $".Kando-{Guid.NewGuid():N}");

        try
        {
            await DownloadArchiveWithRetryAsync(asset.DownloadUri, archivePath, progress, cancellationToken);
            if (asset.Sha256 is not null)
            {
                await using var archive = File.OpenRead(archivePath);
                var digest = Convert.ToHexString(await SHA256.HashDataAsync(archive, cancellationToken));
                if (!digest.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Kando download checksum verification failed. Please retry.");
            }
            progress?.Report(94);
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, stagingRoot), cancellationToken);
            var executable = KandoComponentPaths.FindExecutableUnder(stagingRoot)
                ?? throw new InvalidDataException("下载的 Kando 压缩包中没有找到 kando.exe。");
            var payloadDirectory = Path.GetDirectoryName(executable)
                ?? throw new InvalidDataException("无法确定 Kando 的解压目录。");

            if (!KandoExecutableCompatibility.IsSupportedOnCurrentOperatingSystem(executable, out var reason))
                throw new BadImageFormatException(reason);

            if (KandoRelease.ParseVersion(KandoRelease.ReadInstalledVersion(executable)) != asset.Version)
                throw new InvalidDataException("The downloaded Kando application version does not match the release.");
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(payloadDirectory, ".gesturesign-component-version"), asset.TagName);
            var userData = PreservePortableSettings(payloadDirectory, false);

            progress?.Report(97);
            await KandoInstallation.ReplaceAsync(payloadDirectory, KandoComponentPaths.InstallDirectory, userData,
                async () =>
                {
                    if (beforeReplace is not null) await beforeReplace();
                    PreservePortableSettings(payloadDirectory, true);
                }, async () =>
                {
                    if (validate is not null) await validate();
                    if (File.Exists(KandoComponentPaths.RemovedMarkerPath))
                        File.Delete(KandoComponentPaths.RemovedMarkerPath);
                }, stop, restore);
            progress?.Report(100);
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(stagingRoot);
        }
    }

    private static Task DownloadArchiveWithRetryAsync(Uri url, string archivePath, IProgress<double>? progress, CancellationToken cancellationToken)
        => KandoDownloadClient.DownloadAsync(Client, url, archivePath, progress, cancellationToken);

    public static void Uninstall()
    {
        if (Directory.Exists(KandoComponentPaths.InstallDirectory))
            Directory.Delete(KandoComponentPaths.InstallDirectory, true);
        if (Directory.Exists(KandoComponentPaths.InstallDirectory))
            throw new IOException("Kando 组件目录仍被占用，请关闭 Kando 后重试。");

        Directory.CreateDirectory(KandoComponentPaths.ComponentsRoot);
        File.WriteAllText(KandoComponentPaths.RemovedMarkerPath, DateTime.UtcNow.ToString("O"));
    }

    private static void MigrateLegacyStoreData()
    {
        TryMigrate(MigrateLegacyRemovedMarker);
        TryMigrate(MigrateLegacyDownloadedInstallation);
        TryMigrate(MigrateLegacyUserData);
    }

    private static void TryMigrate(Action migration)
    {
        try
        {
            migration();
        }
        catch
        {
            // A locked or inaccessible legacy directory must not prevent startup
            // or the optional Kando component from being restored.
        }
    }

    private static void MigrateLegacyRemovedMarker()
    {
        if (File.Exists(KandoComponentPaths.RemovedMarkerPath) || IsDownloaded)
            return;

        foreach (var sourceDirectory in EnumerateLegacyComponentRoots())
        {
            var sourceMarker = Path.Combine(sourceDirectory, "Kando.removed");
            if (!File.Exists(sourceMarker))
                continue;

            Directory.CreateDirectory(KandoComponentPaths.ComponentsRoot);
            File.Copy(sourceMarker, KandoComponentPaths.RemovedMarkerPath, false);
            return;
        }
    }

    private static void MigrateLegacyDownloadedInstallation()
    {
        if (IsDownloaded || File.Exists(KandoComponentPaths.RemovedMarkerPath))
            return;

        foreach (var componentRoot in EnumerateLegacyComponentRoots())
        {
            var source = Path.Combine(componentRoot, "Kando");
            if (PathsEqual(source, KandoComponentPaths.InstallDirectory) ||
                KandoComponentPaths.FindExecutableUnder(source) is null)
            {
                continue;
            }

            CopyDirectoryAtomically(source, KandoComponentPaths.InstallDirectory);
            return;
        }
    }

    private static void MigrateLegacyUserData()
    {
        var destination = KandoComponentPaths.UserDataDirectory;
        foreach (var source in EnumerateLegacyKandoUserDataDirectories())
        {
            if (!Directory.Exists(source) || PathsEqual(source, destination))
                continue;

            MergePersistentUserData(source, destination);
        }
    }

    private static IEnumerable<string> EnumerateLegacyComponentRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var localDataRoot in EnumerateLegacyLocalDataRoots())
        {
            var path = Path.Combine(localDataRoot, "GestureSign V2", "Components");
            if (seen.Add(NormalizePath(path)))
                yield return path;
        }
    }

    private static IEnumerable<string> EnumerateLegacyKandoUserDataDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var redirectedRoaming = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "kando");
        if (seen.Add(NormalizePath(redirectedRoaming)))
            yield return redirectedRoaming;

        foreach (var packageDirectory in EnumerateStorePackageDirectories())
        {
            foreach (var relativePath in new[]
                     {
                         Path.Combine("LocalCache", "Roaming", "kando"),
                         Path.Combine("RoamingState", "kando"),
                         Path.Combine("AppData", "Roaming", "kando"),
                         Path.Combine("LocalCache", "Local", "kando"),
                         Path.Combine("LocalState", "kando")
                     })
            {
                var path = Path.Combine(packageDirectory, relativePath);
                if (seen.Add(NormalizePath(path)))
                    yield return path;
            }
        }
    }

    private static IEnumerable<string> EnumerateLegacyLocalDataRoots()
    {
        yield return KandoComponentPaths.NativeLocalApplicationData;
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var packageDirectory in EnumerateStorePackageDirectories())
        {
            yield return Path.Combine(packageDirectory, "LocalCache", "Local");
            yield return Path.Combine(packageDirectory, "LocalState");
            yield return Path.Combine(packageDirectory, "AppData", "Local");
        }
    }

    private static IEnumerable<string> EnumerateStorePackageDirectories()
    {
        var packagesRoot = Path.Combine(KandoComponentPaths.NativeLocalApplicationData, "Packages");
        if (!Directory.Exists(packagesRoot))
            yield break;

        IEnumerable<string> packageDirectories;
        try
        {
            packageDirectories = Directory.EnumerateDirectories(
                packagesRoot,
                "TomClancy.GestureSignV2_*").ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var packageDirectory in packageDirectories)
            yield return packageDirectory;
    }

    private static void MergePersistentUserData(string sourceDirectory, string destinationDirectory)
    {
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(file).Equals("ipc-info.json", StringComparison.OrdinalIgnoreCase))
                    continue;
                CopyFileIfMissing(file, Path.Combine(destinationDirectory, Path.GetFileName(file)));
            }

            foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(directory).Equals("session", StringComparison.OrdinalIgnoreCase))
                    continue;
                CopyDirectoryIfMissing(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
            }
        }
        catch
        {
            // A locked cache or an inaccessible stale package directory must not
            // prevent Kando itself from being restored after a Store update.
        }
    }

    private static void CopyDirectoryIfMissing(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
            CopyFileIfMissing(file, Path.Combine(destinationDirectory, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
            CopyDirectoryIfMissing(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
    }

    private static void CopyFileIfMissing(string source, string destination)
    {
        if (File.Exists(destination))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, false);
    }

    private static bool PathsEqual(string left, string right)
        => NormalizePath(left).Equals(NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path;
        }
    }

    private static string PreservePortableSettings(string payload, bool copy)
    {
        var install = KandoComponentPaths.InstallDirectory;
        var mode = Path.Combine(install, "portableMode.json");
        if (!File.Exists(mode)) return KandoComponentPaths.UserDataDirectory;
        using var document = JsonDocument.Parse(File.ReadAllText(mode));
        var configured = document.RootElement.TryGetProperty("configDirectory", out var property)
            ? property.GetString() ?? "." : ".";
        var data = Path.GetFullPath(Path.Combine(install, configured));
        if (copy) File.Copy(mode, Path.Combine(payload, "portableMode.json"), true);
        var relative = Path.GetRelativePath(install, data);
        if (relative == ".")
        {
            // Configuration at the program root must not overwrite new binaries.
            foreach (var name in new[] { "config.json", "menus.json" })
                if (copy && File.Exists(Path.Combine(install, name))) File.Copy(Path.Combine(install, name), Path.Combine(payload, name), true);
            return KandoComponentPaths.UserDataDirectory;
        }
        if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            if (copy && Directory.Exists(data)) KandoInstallation.CopyDirectory(data, Path.Combine(payload, relative));
            // The old program backup contains this portable settings directory.
            return KandoComponentPaths.UserDataDirectory;
        }
        var componentRelative = Path.GetRelativePath(data, KandoComponentPaths.ComponentsRoot);
        if (componentRelative == "." || (!Path.IsPathRooted(componentRelative) && componentRelative != ".." &&
            !componentRelative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new InvalidDataException("Kando portable settings must not contain the component installation directory.");
        return data;
    }

    private static void InstallFromArchive(string archivePath)
    {
        var stagingRoot = Path.Combine(KandoComponentPaths.ComponentsRoot, $".Kando-migrate-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(KandoComponentPaths.ComponentsRoot);
            ZipFile.ExtractToDirectory(archivePath, stagingRoot);
            var executable = KandoComponentPaths.FindExecutableUnder(stagingRoot)
                ?? throw new InvalidDataException("Kando migration payload does not contain kando.exe.");
            var payloadDirectory = Path.GetDirectoryName(executable)
                ?? throw new InvalidDataException("Unable to resolve the Kando migration directory.");
            if (!KandoExecutableCompatibility.IsSupportedOnCurrentOperatingSystem(executable, out var reason))
                throw new BadImageFormatException(reason);
            InstallExtractedDirectory(payloadDirectory, KandoComponentPaths.InstallDirectory);
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }
    private static void CopyDirectoryAtomically(string sourceDirectory, string destinationDirectory)
    {
        var stagingDirectory = destinationDirectory + ".migrate-" + Guid.NewGuid().ToString("N");
        try
        {
            CopyDirectory(sourceDirectory, stagingDirectory);
            InstallExtractedDirectory(stagingDirectory, destinationDirectory);
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static void InstallExtractedDirectory(string sourceDirectory, string destinationDirectory)
    {
        var backupDirectory = destinationDirectory + ".backup-" + Guid.NewGuid().ToString("N");
        var hadExisting = Directory.Exists(destinationDirectory);
        try
        {
            if (hadExisting)
                Directory.Move(destinationDirectory, backupDirectory);
            Directory.Move(sourceDirectory, destinationDirectory);
            TryDeleteDirectory(backupDirectory);
        }
        catch
        {
            if (!Directory.Exists(destinationDirectory) && Directory.Exists(backupDirectory))
                Directory.Move(backupDirectory, destinationDirectory);
            throw;
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GestureSign-V2-Kando-Component");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

}
