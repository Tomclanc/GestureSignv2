using GestureSign.Foundation.Intent;
using GestureSign.WinUI.Services;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "GestureSign-PackageTest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
IntentComponentAsset Create(string name, string? unsafeName = null, string arch = "x64", int protocol = 2)
{
    var path = Path.Combine(root, name + ".zip");
    using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
    {
        using (var writer = new StreamWriter(zip.CreateEntry("component.json").Open())) writer.Write(JsonSerializer.Serialize(new IntentComponentManifest(protocol, "0.3.0", arch)));
        var exe = new byte[128]; exe[0] = 0x4d; exe[1] = 0x5a; exe[0x3c] = 64; exe[64] = 0x50; exe[65] = 0x45; exe[68] = 0x64; exe[69] = 0x86;
        using (var output = zip.CreateEntry("Runtime/GestureSign.IntentDlc.exe").Open()) output.Write(exe);
        if (unsafeName != null) { using var writer = new StreamWriter(zip.CreateEntry(unsafeName).Open()); writer.Write("bad"); }
    }
    using var input = File.OpenRead(path);
    return new("x64", "0.3.0", name + ".zip", input.Length, Convert.ToHexString(SHA256.HashData(input)), "https://github.com/Tomclanc/GestureSignv2/releases/download/intent-dlc-v0.3.0/test.zip");
}
var valid = Create("valid"); var target = Path.Combine(root, "component");
IntentComponentPackage.Install(Path.Combine(root, valid.FileName), valid, target);
Check(File.Exists(Path.Combine(target, "Runtime/GestureSign.IntentDlc.exe")), "Valid archive did not install.");
File.WriteAllText(Path.Combine(target, "old-marker"), "preserve on failure");
void Reject(IntentComponentAsset asset, string reason)
{
    bool rejected = false;
    try { IntentComponentPackage.Install(Path.Combine(root, asset.FileName), asset, target); } catch (InvalidDataException) { rejected = true; }
    Check(rejected && File.Exists(Path.Combine(target, "old-marker")), reason);
}
Reject(valid with { Sha256 = new string('0', 64) }, "Bad hash replaced installed payload.");
Reject(Create("traversal", "../../escape.txt"), "Path traversal accepted.");
Check(!File.Exists(Path.Combine(root, "escape.txt")), "Archive escaped staging.");
Reject(Create("wrong-architecture", arch: "arm64"), "Wrong architecture accepted.");
Reject(Create("wrong-protocol", protocol: 1), "Old standalone UI component accepted.");
bool canceled = false; try { IntentComponentPackage.Install(Path.Combine(root, valid.FileName), valid, target, new CancellationToken(true)); } catch (OperationCanceledException) { canceled = true; }
Check(canceled && File.Exists(Path.Combine(target, "old-marker")), "Canceled install replaced payload.");
IntentComponentPackage.Install(Path.Combine(root, valid.FileName), valid, target);
Check(!File.Exists(Path.Combine(target, "old-marker")) && !Directory.EnumerateDirectories(root, ".Intent-*").Any(), "Atomic replacement left stale payload/staging.");
using var missingHttp = new HttpClient(new FakeHandler(HttpStatusCode.NotFound, []));
var service = new IntentComponentService(missingHttp, valid); string? error = null;
try { await service.DownloadAsync(new Progress<double>(), CancellationToken.None); } catch (IOException ex) { error = ex.Message; }
Check(error?.Contains("GitHub Releases") == true, "Unpublished download did not offer offline import.");
using var oversizedHttp = new HttpClient(new FakeHandler(HttpStatusCode.OK, new byte[4096]));
service = new IntentComponentService(oversizedHttp, valid with { Bytes = 1 }); error = null;
try { await service.DownloadAsync(new Progress<double>(), CancellationToken.None); } catch (InvalidDataException ex) { error = ex.Message; }
Check(error != null, "Oversized download accepted.");
Check(!Directory.EnumerateDirectories(root, ".Intent-*").Any(), "Failed validation left temporary payloads.");
Console.WriteLine($"PASS: {checks} component download / hash / architecture / traversal / cancel / atomic installation checks.");

sealed class FakeHandler(HttpStatusCode code, byte[] data) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(code) { Content = new ByteArrayContent(data) });
}
