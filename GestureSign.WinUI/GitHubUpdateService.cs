using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.WinUI;

internal sealed record GitHubReleaseInfo(Version Version, string TagName, Uri ReleaseUri);

internal static class GitHubUpdateService
{
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
