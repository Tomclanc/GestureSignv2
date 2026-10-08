using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GestureSign.WinUI;
using GestureSign.Shared;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
void Reject(Action action, string message)
{
    try { action(); }
    catch (Exception error) when (error is InvalidDataException or PlatformNotSupportedException) { checks++; return; }
    throw new Exception(message);
}
string Release(bool draft = false, bool prerelease = false, string tag = "v3.0.0", bool arm = true, string host = "github.com", string? digest = null)
    => JsonSerializer.Serialize(new
    {
        tag_name = tag, draft, prerelease, body = "Release notes",
        assets = new[] { "x64", arm ? "arm64" : "linux" }.Select(architecture => new
        {
            name = $"Kando-win32-{architecture}-{tag.TrimStart('v')}.zip",
            browser_download_url = $"https://{host}/kando-menu/kando/releases/download/{tag}/Kando-win32-{architecture}-{tag.TrimStart('v')}.zip", digest
        })
    });
foreach (var architecture in new[] { Architecture.X64, Architecture.Arm64 })
{
    var release = KandoRelease.Parse(Release(digest: "sha256:" + new string('a', 64)), architecture);
    Check(release.DownloadUri.AbsolutePath.Contains(architecture == Architecture.Arm64 ? "arm64" : "x64"), "Correct ZIP architecture");
    Check(release.Version == new Version(3, 0, 0), "Stable version parsed");
    Check(release.Notes == "Release notes" && release.Sha256 == new string('a', 64), "Notes and checksum retained");
}
Check(KandoRelease.Parse(Release(), Architecture.X64).Sha256 is null, "Older GitHub releases may lack a digest");
Reject(() => KandoRelease.Parse(Release(draft: true), Architecture.X64), "Draft rejected");
Reject(() => KandoRelease.Parse(Release(prerelease: true), Architecture.X64), "Prerelease rejected");
Reject(() => KandoRelease.Parse(Release(tag: "v3.1.0-beta.1"), Architecture.X64), "Prerelease tag rejected even if incorrectly marked stable");
Reject(() => KandoRelease.Parse(Release(arm: false), Architecture.Arm64), "Missing ARM64 must not silently use x64 or an older release");
Reject(() => KandoRelease.Parse(Release(), Architecture.X86), "Unsupported OS architecture rejected");
Reject(() => KandoRelease.Parse(Release(host: "example.com"), Architecture.X64), "Untrusted download rejected");
Reject(() => KandoRelease.Parse(Release(digest: "sha256:wrong"), Architecture.X64), "Malformed digest rejected");

string WebAssets(string tag = "v3.0.0") => string.Join("", new[] { "x64", "arm64" }.Select(a =>
    $"<a href='/kando-menu/kando/releases/download/{tag}/Kando-win32-{a}-{tag.TrimStart('v')}.zip'>Download</a>" +
    $"<clipboard-copy value='sha256:{new string('a',64)}' aria-label='Copy to clipboard digest for Kando-win32-{a}-{tag.TrimStart('v')}.zip'></clipboard-copy>"));
foreach (var arch in new[] { Architecture.X64, Architecture.Arm64 })
{
    var parsed = KandoReleaseClient.ParseAssets("v3.0.0", WebAssets(), arch);
    Check(parsed.Sha256 == new string('a',64), "Website asset preserves matching checksum");
    Check(parsed.DownloadUri.AbsolutePath.Contains(arch == Architecture.X64 ? "win32-x64" : "win32-arm64"), "Website selects exact architecture");
}
Reject(() => KandoReleaseClient.ParseAssets("v3.0.0", WebAssets("v2.3.1"), Architecture.X64), "Website never substitutes an older asset");
Reject(() => KandoReleaseClient.ParseAssets("v3.0.0", WebAssets().Replace("/kando-menu", "https://evil.example/kando-menu"), Architecture.X64), "Website rejects untrusted asset hosts");
Reject(() => KandoReleaseClient.ParseAssets("v3.0.0-beta.1", WebAssets("v3.0.0-beta.1"), Architecture.X64), "Website rejects beta versions");
Reject(() => KandoReleaseClient.ParseAssets("v3.0.0", WebAssets().Replace("sha256:", "wrong:"), Architecture.X64), "Website checksum validation cannot be bypassed");
var atom = "<feed xmlns='http://www.w3.org/2005/Atom'><entry><link href='https://github.com/kando-menu/kando/releases/tag/v4.0.0-beta.1'/><content>Beta notes</content></entry><entry><link href='https://github.com/kando-menu/kando/releases/tag/v3.0.0'/><content type='html'>&lt;p&gt;Stable notes&lt;/p&gt;</content></entry></feed>";
Check(KandoReleaseClient.ParseNotes(atom, new Uri("https://github.com/kando-menu/kando/releases/tag/v3.0.0")) == "Stable notes", "Notes match stable tag rather than newest beta feed entry");
foreach (var status in new[] { System.Net.HttpStatusCode.Forbidden, System.Net.HttpStatusCode.TooManyRequests, System.Net.HttpStatusCode.BadGateway })
{
    int apiCalls = 0, webCalls = 0;
    var time = DateTimeOffset.UtcNow;
    var handler = new ReleaseHandler(request => {
        if (request.RequestUri!.Host == "api.github.com") { apiCalls++; return new System.Net.Http.HttpResponseMessage(status); }
        webCalls++;
        var result = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { RequestMessage = request };
        if (request.RequestUri.AbsolutePath.EndsWith("/latest"))
        { result.RequestMessage = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get,"https://github.com/kando-menu/kando/releases/tag/v3.0.0"); result.Content = new System.Net.Http.StringContent("latest page"); }
        else result.Content = new System.Net.Http.StringContent(request.RequestUri.AbsolutePath.EndsWith(".atom") ? atom : WebAssets());
        return result;
    });
    using var http = new System.Net.Http.HttpClient(handler);
    var lookup = new KandoReleaseClient(http, Architecture.Arm64, () => time);
    var found = await lookup.GetLatestAsync();
    Check(found.TagName == "v3.0.0" && found.Notes == "Stable notes", "API limit/unavailability uses latest stable website and notes");
    await lookup.GetLatestAsync();
    Check(apiCalls == 1 && webCalls == 3, "Check-then-update reuses short cache instead of repeating requests");
    time += TimeSpan.FromMinutes(6);
    await lookup.GetLatestAsync();
    Check(apiCalls == 2, "Expired cache refreshes latest stable metadata");
}
using (var http = new System.Net.Http.HttpClient(new ReleaseHandler(r => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Forbidden))))
{
    try { await new KandoReleaseClient(http, Architecture.X64).GetLatestAsync(); throw new Exception("Expected unavailable lookup"); }
    catch (KandoReleaseLookupException error) { Check(error.RateLimited, "Both sources unavailable reports structured rate-limit error for localization"); }
}
using (var http = new System.Net.Http.HttpClient(new ReleaseHandler(r => throw new Exception("Canceled caller made a request"))))
{
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    try { await new KandoReleaseClient(http, Architecture.X64).GetLatestAsync(canceled.Token); throw new Exception("Expected caller cancellation"); }
    catch (OperationCanceledException) { checks++; }
}
var root = Path.Combine(Path.GetTempPath(), "GestureSign-KandoTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var leasePath = Path.Combine(root, "lease", "Kando.updating");
    Check(!KandoComponentPaths.IsUpdateLeaseActive(leasePath), "No lease means normal launch");
    using (KandoComponentPaths.AcquireUpdateLease(leasePath))
    {
        Check(KandoComponentPaths.IsUpdateLeaseActive(leasePath), "Active updater prevents daemon launches");
        try
        {
            using var second = KandoComponentPaths.AcquireUpdateLease(leasePath);
            throw new Exception("Expected competing update rejection");
        }
        catch (IOException) { checks++; }
    }
    Check(!KandoComponentPaths.IsUpdateLeaseActive(leasePath) && !File.Exists(leasePath), "Lease release unblocks launches and removes marker");
    Directory.CreateDirectory(Path.GetDirectoryName(leasePath)!);
    File.WriteAllText(leasePath, "stale");
    Check(!KandoComponentPaths.IsUpdateLeaseActive(leasePath), "A stale marker cannot permanently disable Kando");
    Check(!KandoComponentService.IsManagedExecutable(Path.Combine(root, "external", "kando.exe")), "External executable is never managed");
    var metadata = Path.Combine(root, "metadata");
    Directory.CreateDirectory(Path.Combine(metadata, "resources"));
    var exe = Path.Combine(metadata, "kando.exe");
    File.WriteAllText(exe, "placeholder executable");
    var package = Encoding.UTF8.GetBytes("{\"name\":\"kando\",\"version\":\"3.0.0\"}");
    var header = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { files = new Dictionary<string, object> { ["package.json"] = new { offset = "0", size = package.Length } } }));
    var padded = (header.Length + 3) / 4 * 4;
    using (var writer = new BinaryWriter(File.Create(Path.Combine(metadata, "resources", "app.asar"))))
    {
        writer.Write(4U); writer.Write((uint)(8 + padded)); writer.Write((uint)(4 + padded)); writer.Write((uint)header.Length);
        writer.Write(header); writer.Write(new byte[padded - header.Length]); writer.Write(package);
    }
    Check(KandoRelease.ReadInstalledVersion(exe) == "3.0.0", "Actual Electron ASAR version, not Electron executable version");
    File.WriteAllText(Path.Combine(metadata, "resources", "app.asar"), "bad ASAR");
    File.WriteAllText(Path.Combine(metadata, ".gesturesign-component-version"), "v2.3.1");
    Check(KandoRelease.ReadInstalledVersion(exe) == "v2.3.1", "Version marker fallback");
    Check(KandoRelease.ReadInstalledVersion(Path.Combine(root, "missing.exe")) is null, "Missing executable has no version");

    (string Payload, string Destination, string Settings) Fixture(string name)
    {
        var directory = Path.Combine(root, name);
        var payload = Path.Combine(directory, "payload");
        var destination = Path.Combine(directory, "Kando");
        var settings = Path.Combine(directory, "settings");
        Directory.CreateDirectory(payload); Directory.CreateDirectory(destination); Directory.CreateDirectory(settings);
        File.WriteAllText(Path.Combine(payload, "kando.exe"), "new");
        File.WriteAllText(Path.Combine(destination, "kando.exe"), "old");
        Directory.CreateDirectory(Path.Combine(destination, "portable-config"));
        File.WriteAllText(Path.Combine(destination, "portable-config", "menus.json"), "portable menus");
        File.WriteAllText(Path.Combine(settings, "menus.json"), "custom menus");
        File.WriteAllText(Path.Combine(settings, "config.json"), "custom settings");
        return (payload, destination, settings);
    }
    var success = Fixture("success");
    var events = new List<string>();
    await KandoInstallation.ReplaceAsync(success.Payload, success.Destination, success.Settings,
        () => { events.Add("stop old"); Check(Directory.Exists(success.Payload), "Download exists before shutdown"); return Task.CompletedTask; },
        () => { events.Add("start new"); Check(File.ReadAllText(Path.Combine(success.Destination, "kando.exe")) == "new", "New application activated before startup"); return Task.CompletedTask; }, null, null);
    Check(events.SequenceEqual(new[] { "stop old", "start new" }), "Stop only after staging, then start");
    Check(File.ReadAllText(Path.Combine(success.Settings, "menus.json")) == "custom menus", "Successful update preserves menus");
    Check(File.ReadAllText(Path.Combine(success.Settings, "config.json")) == "custom settings", "Successful update preserves settings");
    Check(!Directory.GetDirectories(Path.GetDirectoryName(success.Destination)!).Any(p => Path.GetFileName(p).StartsWith("Kando.backup-") || Path.GetFileName(p).StartsWith("Kando.settings-")), "Successful update removes backups");

    var failed = Fixture("startup-failure");
    events.Clear();
    try
    {
        await KandoInstallation.ReplaceAsync(failed.Payload, failed.Destination, failed.Settings,
            () => { events.Add("stop old"); return Task.CompletedTask; },
            () =>
            {
                File.WriteAllText(Path.Combine(failed.Settings, "config.json"), "new-format migrated settings");
                File.WriteAllText(Path.Combine(failed.Settings, "new-file.json"), "migration");
                throw new IOException("Simulated new version startup failure");
            },
            () => { events.Add("stop new"); return Task.CompletedTask; },
            () => { events.Add("restore old"); Check(File.ReadAllText(Path.Combine(failed.Destination, "kando.exe")) == "old", "Restore executable before restarting"); return Task.CompletedTask; });
        throw new Exception("Expected startup failure");
    }
    catch (IOException) { checks++; }
    Check(events.SequenceEqual(new[] { "stop old", "stop new", "restore old" }), "Rollback process order");
    Check(File.ReadAllText(Path.Combine(failed.Settings, "config.json")) == "custom settings", "Rollback restores pre-migration settings");
    Check(!File.Exists(Path.Combine(failed.Settings, "new-file.json")), "Rollback removes new-version settings files");
    Check(File.ReadAllText(Path.Combine(failed.Destination, "portable-config", "menus.json")) == "portable menus", "Rollback retains portable settings");

    var blocked = Fixture("replacement-failure");
    Directory.Delete(blocked.Payload, true);
    var restored = false;
    try
    {
        await KandoInstallation.ReplaceAsync(blocked.Payload, blocked.Destination, blocked.Settings, null, null, null,
            () => { restored = true; return Task.CompletedTask; });
        throw new Exception("Expected replacement failure");
    }
    catch (DirectoryNotFoundException) { checks++; }
    Check(restored && File.ReadAllText(Path.Combine(blocked.Destination, "kando.exe")) == "old", "Failed directory replacement restores old installation");
    Check(File.ReadAllText(Path.Combine(blocked.Settings, "config.json")) == "custom settings", "Replacement failure keeps settings");

    var fresh = Fixture("fresh-install-failure");
    Directory.Delete(fresh.Destination, true);
    Directory.Delete(fresh.Settings, true);
    try
    {
        await KandoInstallation.ReplaceAsync(fresh.Payload, fresh.Destination, fresh.Settings, null,
            () =>
            {
                Directory.CreateDirectory(fresh.Settings);
                File.WriteAllText(Path.Combine(fresh.Settings, "config.json"), "new settings");
                throw new IOException("Fresh installation failed to start");
            }, null, null);
        throw new Exception("Expected fresh startup failure");
    }
    catch (IOException) { checks++; }
    Check(!Directory.Exists(fresh.Destination) && !Directory.Exists(fresh.Settings), "Failed fresh installation restores absent application/settings state");

    var recovery = Fixture("rollback-failure");
    try
    {
        await KandoInstallation.ReplaceAsync(recovery.Payload, recovery.Destination, recovery.Settings, null,
            () => throw new IOException("New startup failed"), () => throw new IOException("Cannot stop new app"), null);
        throw new Exception("Expected recovery failure");
    }
    catch (AggregateException error)
    {
        Check(error.InnerExceptions.Count == 2, "Rollback reports original and recovery errors");
        Check(Directory.GetDirectories(Path.GetDirectoryName(recovery.Destination)!, "Kando.backup-*").Length == 1, "Unrecoverable failure keeps old application backup");
        Check(Directory.GetDirectories(Path.GetDirectoryName(recovery.Destination)!, "Kando.settings-*").Length == 1, "Unrecoverable failure keeps original settings backup");
    }
    if (args.Contains("--live-release"))
    {
        using var forcedClient = new System.Net.Http.HttpClient(new ForceApiLimitHandler(new System.Net.Http.HttpClientHandler())) { Timeout = TimeSpan.FromSeconds(45) };
        forcedClient.DefaultRequestHeaders.UserAgent.ParseAdd("GestureSign-Kando-Release-Test");
        var live = await new KandoReleaseClient(forcedClient, Architecture.X64).GetLatestAsync();
        Check(live.Sha256 is not null && !string.IsNullOrWhiteSpace(live.Notes), "Live rate-limit fallback preserves official digest and release notes");
        Check(live.DownloadUri.Host == "github.com", "Live latest stable GitHub release resolved");
        Console.WriteLine($"Live stable release: {live.TagName}, {live.DownloadUri}");
    }
    var inspectIndex = Array.IndexOf(args, "--inspect");
    if (inspectIndex >= 0)
    {
        var actual = KandoRelease.ReadInstalledVersion(args[inspectIndex + 1]);
        Check(actual == "3.0.0", "Read actual official Kando ZIP metadata");
        Console.WriteLine($"Official ZIP application version: {actual}");
    }
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"Kando update checks passed: {checks}");

await DownloadClientChecks.Run(Check);

sealed class ReleaseHandler(Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> respond) : System.Net.Http.HttpMessageHandler
{
    protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
}
sealed class ForceApiLimitHandler(System.Net.Http.HttpMessageHandler inner) : System.Net.Http.DelegatingHandler(inner)
{
    protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken token)
        => request.RequestUri!.Host == "api.github.com"
            ? Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Forbidden))
            : base.SendAsync(request, token);
}
