using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace GestureSign.WinUI;

internal sealed class KandoReleaseLookupException(bool rateLimited, Exception inner)
    : IOException("Could not check the latest Kando release. The installed version has not been changed.", inner)
{
    public bool RateLimited { get; } = rateLimited;
}

internal sealed class KandoReleaseClient(HttpClient client, Architecture architecture, Func<DateTimeOffset>? clock = null)
{
    private const string Repository = "https://github.com/kando-menu/kando";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private KandoRelease? cached;
    private DateTimeOffset checkedAt;

    public async Task<KandoRelease> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var elapsed = now() - checkedAt;
            if (cached is not null && elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromMinutes(5)) return cached;
            KandoRelease release;
            bool rateLimited = false;
            try
            {
                using var apiTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                apiTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                using var response = await client.GetAsync("https://api.github.com/repos/kando-menu/kando/releases/latest", apiTimeout.Token);
                rateLimited = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests;
                response.EnsureSuccessStatusCode();
                release = KandoRelease.Parse(await response.Content.ReadAsStringAsync(apiTimeout.Token), architecture);
            }
            catch (Exception error) when (error is HttpRequestException || (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                try { release = await FromOfficialWebsiteAsync(cancellationToken); }
                catch (Exception fallbackError) when (!cancellationToken.IsCancellationRequested && fallbackError is HttpRequestException or OperationCanceledException or InvalidDataException)
                { throw new KandoReleaseLookupException(rateLimited, fallbackError); }
            }
            cached = release;
            checkedAt = now();
            return release;
        }
        finally { gate.Release(); }
    }

    private async Task<KandoRelease> FromOfficialWebsiteAsync(CancellationToken token)
    {
        // /latest redirects to GitHub's latest stable release, not the newest beta.
        using var request = new HttpRequestMessage(HttpMethod.Get, Repository + "/releases/latest");
        request.Headers.Accept.ParseAdd("text/html");
        using var response = await client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        var uri = response.RequestMessage?.RequestUri;
        if (uri is null || uri.Scheme != "https" || uri.Host != "github.com" ||
            !uri.AbsolutePath.StartsWith("/kando-menu/kando/releases/tag/", StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected Kando release redirect.");
        var tag = Uri.UnescapeDataString(uri.AbsolutePath.Substring("/kando-menu/kando/releases/tag/".Length));
        if (KandoRelease.ParseVersion(tag) is null) throw new InvalidDataException("Invalid stable Kando version.");
        using var assetRequest = new HttpRequestMessage(HttpMethod.Get, Repository + "/releases/expanded_assets/" + Uri.EscapeDataString(tag));
        assetRequest.Headers.Accept.ParseAdd("text/html");
        using var assets = await client.SendAsync(assetRequest, token);
        assets.EnsureSuccessStatusCode();
        var release = ParseAssets(tag, await assets.Content.ReadAsStringAsync(token), architecture);
        try
        {
            using var notesTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            notesTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var notesRequest = new HttpRequestMessage(HttpMethod.Get, Repository + "/releases.atom");
            notesRequest.Headers.Accept.ParseAdd("application/atom+xml");
            using var notesResponse = await client.SendAsync(notesRequest, notesTimeout.Token);
            notesResponse.EnsureSuccessStatusCode();
            release = release with { Notes = ParseNotes(await notesResponse.Content.ReadAsStringAsync(notesTimeout.Token), release.ReleaseUri) };
        }
        catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or System.Xml.XmlException or OperationCanceledException) { }
        return release;
    }

    internal static KandoRelease ParseAssets(string tag, string html, Architecture architecture)
    {
        var platform = architecture switch { Architecture.X64 => "x64", Architecture.Arm64 => "arm64", _ => throw new PlatformNotSupportedException() };
        if (KandoRelease.ParseVersion(tag) is null) throw new InvalidDataException("Invalid stable Kando version.");
        var name = $"Kando-win32-{platform}-{tag.TrimStart('v', 'V')}.zip";
        var expected = new Uri(Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + name);
        var found = Regex.Matches(html, @"<a\b[^>]*>", RegexOptions.IgnoreCase)
            .Select(m => Attribute(m.Value, "href")).Any(href =>
                Uri.TryCreate(new Uri(Repository), href, out var link) && link == expected);
        if (!found) throw new InvalidDataException("The latest stable Kando release has no matching Windows ZIP.");
        string? digest = null;
        foreach (Match copy in Regex.Matches(html, @"<clipboard-copy\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (Attribute(copy.Value, "aria-label") == "Copy to clipboard digest for " + name)
            {
                digest = Attribute(copy.Value, "value");
                break;
            }
        }
        // Reuse the same architecture, version, URL and digest validation as the API.
        return KandoRelease.Parse(JsonSerializer.Serialize(new {
            tag_name = tag, draft = false, prerelease = false, body = "",
            assets = new[] { new { name, browser_download_url = expected.AbsoluteUri, digest } }
        }), architecture);
    }

    private static string Attribute(string html, string name)
    {
        var match = Regex.Match(html, @"\b" + Regex.Escape(name) + "\\s*=\\s*([\"'])(.*?)\\1", RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[2].Value) : "";
    }

    internal static string ParseNotes(string xml, Uri releaseUri)
    {
        XNamespace ns = "http://www.w3.org/2005/Atom";
        var entry = XDocument.Parse(xml).Root?.Elements(ns + "entry").FirstOrDefault(e =>
            e.Elements(ns + "link").Any(l => (string?)l.Attribute("href") == releaseUri.AbsoluteUri));
        var html = entry?.Element(ns + "content")?.Value ?? "";
        html = Regex.Replace(html, @"<br\s*/?>|</p>|</li>", "\n", RegexOptions.IgnoreCase);
        return WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", "")).Trim();
    }
}