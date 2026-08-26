using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.WinUI;

internal sealed record GitHubReleaseInfo(Version Version, string TagName, Uri ReleaseUri);

internal static class GitHubUpdateService
{
    private const string ReleaseDownloadBaseUrl = "https://github.com/Tomclanc/GestureSignv2/releases/download";
    private const string LatestReleaseUrl = "https://github.com/Tomclanc/GestureSignv2/releases/latest";
    private static readonly HttpClient Client = CreateClient();

    public static async Task<GitHubReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var releaseUri = response.RequestMessage?.RequestUri;
        var tagName = releaseUri?.Segments[^1].Trim('/').Trim() ?? string.Empty;
        if (!TryParseVersion(tagName, out var version) || releaseUri is null)
            throw new InvalidOperationException("GitHub returned an invalid release response.");

        return new GitHubReleaseInfo(version, tagName, releaseUri);
    }

    public static Uri GetAssetUri(GitHubReleaseInfo release, string assetName)
        => new($"{ReleaseDownloadBaseUrl}/{Uri.EscapeDataString(release.TagName)}/{Uri.EscapeDataString(assetName)}");

    public static async Task DownloadAssetAsync(
        Uri assetUri,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(assetUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var totalLength = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);
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
                progress?.Report(received * 100d / totalLength.Value);
        }

        await output.FlushAsync(cancellationToken);
        if (totalLength is > 0 && received != totalLength.Value)
            throw new InvalidDataException($"Downloaded size mismatch. Expected {totalLength.Value}, received {received}.");
    }

    public static void ValidateDownloadedAsset(string path, bool msi)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < 1024 * 1024)
            throw new InvalidDataException("The downloaded update package is incomplete.");

        if (msi)
        {
            Span<byte> expected = stackalloc byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
            Span<byte> actual = stackalloc byte[expected.Length];
            using var stream = File.OpenRead(path);
            if (stream.Read(actual) != expected.Length || !actual.SequenceEqual(expected))
                throw new InvalidDataException("The downloaded file is not a valid MSI package.");
            return;
        }

        using var archive = ZipFile.OpenRead(path);
        foreach (var required in new[] { "GestureSign.WinUI.exe", "Backend/GestureSign.exe" })
        {
            if (archive.GetEntry(required) is null)
                throw new InvalidDataException($"The portable update is missing {required}.");
        }
    }

    public static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().TrimStart('v', 'V');
        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
            normalized = normalized[..suffixIndex];
        return Version.TryParse(normalized, out version!);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GestureSign-V2-UpdateChecker");
        return client;
    }
}
