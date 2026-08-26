using GestureSign.Shared;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.WinUI;

internal static class KandoComponentService
{
    private const string SupportedKandoVersion = "2.3.1";
    private static readonly HttpClient Client = CreateClient();

    public static bool IsInstalled
        => KandoComponentPaths.FindExecutable(string.Empty, AppContext.BaseDirectory) is not null;

    public static bool IsDownloaded
        => KandoComponentPaths.FindExecutableUnder(KandoComponentPaths.InstallDirectory) is not null;

    public static async Task PreserveBundledInstallationAsync()
    {
        if (IsDownloaded || File.Exists(KandoComponentPaths.RemovedMarkerPath))
            return;

        var bundledExecutable = KandoComponentPaths.FindBundledExecutable(AppContext.BaseDirectory);
        if (bundledExecutable is null)
            return;

        var sourceDirectory = Path.GetDirectoryName(bundledExecutable);
        if (string.IsNullOrWhiteSpace(sourceDirectory))
            return;

        await Task.Run(() => CopyDirectoryAtomically(sourceDirectory, KandoComponentPaths.InstallDirectory));
    }

    public static async Task DownloadAndInstallAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var asset = ResolveSupportedAsset();
        Directory.CreateDirectory(KandoComponentPaths.ComponentsRoot);
        var archivePath = Path.Combine(KandoComponentPaths.ComponentsRoot, $"Kando-{Guid.NewGuid():N}.zip");
        var stagingRoot = Path.Combine(KandoComponentPaths.ComponentsRoot, $".Kando-{Guid.NewGuid():N}");

        try
        {
            using (var response = await Client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                var totalLength = response.Content.Headers.ContentLength;
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, true);
                var buffer = new byte[1024 * 128];
                long received = 0;
                while (true)
                {
                    var count = await input.ReadAsync(buffer, cancellationToken);
                    if (count == 0)
                        break;
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    received += count;
                    if (totalLength is > 0)
                        progress?.Report(received * 92d / totalLength.Value);
                }
            }

            progress?.Report(94);
            ZipFile.ExtractToDirectory(archivePath, stagingRoot);
            var executable = KandoComponentPaths.FindExecutableUnder(stagingRoot)
                ?? throw new InvalidDataException("下载的 Kando 压缩包中没有找到 kando.exe。");
            var payloadDirectory = Path.GetDirectoryName(executable)
                ?? throw new InvalidDataException("无法确定 Kando 的解压目录。");

            if (!KandoExecutableCompatibility.IsSupportedOnCurrentOperatingSystem(executable, out var reason))
                throw new BadImageFormatException(reason);

            progress?.Report(97);
            InstallExtractedDirectory(payloadDirectory, KandoComponentPaths.InstallDirectory);
            File.WriteAllText(Path.Combine(KandoComponentPaths.InstallDirectory, ".gesturesign-component-version"), asset.TagName);
            if (File.Exists(KandoComponentPaths.RemovedMarkerPath))
                File.Delete(KandoComponentPaths.RemovedMarkerPath);
            progress?.Report(100);
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(stagingRoot);
        }
    }

    public static void Uninstall()
    {
        if (Directory.Exists(KandoComponentPaths.InstallDirectory))
            Directory.Delete(KandoComponentPaths.InstallDirectory, true);
        if (Directory.Exists(KandoComponentPaths.InstallDirectory))
            throw new IOException("Kando 组件目录仍被占用，请关闭 Kando 后重试。");

        Directory.CreateDirectory(KandoComponentPaths.ComponentsRoot);
        File.WriteAllText(KandoComponentPaths.RemovedMarkerPath, DateTime.UtcNow.ToString("O"));
    }

    private static KandoReleaseAsset ResolveSupportedAsset()
    {
        var architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var name = $"Kando-win32-{architecture}-{SupportedKandoVersion}.zip";
        var url = new Uri($"https://github.com/kando-menu/kando/releases/download/v{SupportedKandoVersion}/{name}");
        return new KandoReleaseAsset("v" + SupportedKandoVersion, url);
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

    private sealed record KandoReleaseAsset(string TagName, Uri Url);
}
