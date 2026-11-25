using GestureSign.Foundation.Intent;
using GestureSign.WinUI.Services;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "GestureSign-PackageTest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
IntentComponentAsset Create(string name, string? unsafeName = null, string arch = "x64", int protocol = 2, string backend = "Hardware")
{
    var path = Path.Combine(root, name + ".zip");
    using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
    {
        using (var writer = new StreamWriter(zip.CreateEntry("component.json").Open())) writer.Write(JsonSerializer.Serialize(new IntentComponentManifest(protocol, IntentComponentPackage.ComponentVersion, arch, backend)));
        var exe = new byte[128]; exe[0] = 0x4d; exe[1] = 0x5a; exe[0x3c] = 64; exe[64] = 0x50; exe[65] = 0x45; exe[68] = 0x64; exe[69] = 0x86;
        using (var output = zip.CreateEntry("Runtime/GestureSign.IntentDlc.exe").Open()) output.Write(exe);
        if (unsafeName != null) { using var writer = new StreamWriter(zip.CreateEntry(unsafeName).Open()); writer.Write("bad"); }
    }
    using var input = File.OpenRead(path);
    return new("x64", IntentComponentPackage.ComponentVersion, name + ".zip", input.Length, Convert.ToHexString(SHA256.HashData(input)), "https://github.com/Tomclanc/GestureSignv2/releases/download/v18.2.9/test.zip");
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
Reject(Create("wrong-backend", backend: "Cpu"), "CPU manifest accepted for a hardware catalog entry.");
Reject(valid with { Backend = "Other" }, "Unknown backend accepted.");
Check(JsonSerializer.Deserialize<IntentComponentManifest>("{\"Protocol\":2,\"Version\":\"18.3.1\",\"Architecture\":\"x64\"}")?.Backend == "Hardware", "Legacy hardware manifest lost compatibility.");
Check(IntentComponentPackage.IsCompatible(new(2, "18.3.1", "x64"), "x64"), "Existing engine no longer resumes after application upgrade.");
Check(!IntentComponentPackage.IsCompatible(new(2, "18.3.1", "arm64"), "x64") &&
    !IntentComponentPackage.IsCompatible(new(1, "18.3.1", "x64"), "x64") &&
    !IntentComponentPackage.IsCompatible(new(2, "18.3.0", "x64"), "x64"), "Incompatible installed engine accepted.");
bool canceled = false; try { IntentComponentPackage.Install(Path.Combine(root, valid.FileName), valid, target, new CancellationToken(true)); } catch (OperationCanceledException) { canceled = true; }
Check(canceled && File.Exists(Path.Combine(target, "old-marker")), "Canceled install replaced payload.");
IntentComponentPackage.Install(Path.Combine(root, valid.FileName), valid, target);
Check(!File.Exists(Path.Combine(target, "old-marker")) && !Directory.EnumerateDirectories(root, ".Intent-*").Any(), "Atomic replacement left stale payload/staging.");
var cpu = Create("cpu", backend: "Cpu") with { Backend = "Cpu" };
File.WriteAllText(Path.Combine(target, "Runtime", "onnxruntime.dll"), "old hardware dependency");
IntentComponentPackage.Install(Path.Combine(root, cpu.FileName), cpu, target);
Check(!File.Exists(Path.Combine(target, "Runtime", "onnxruntime.dll")) && IntentFiles.Read<IntentComponentManifest>(Path.Combine(target, "component.json"))?.Backend == "Cpu", "Switching to CPU retained old hardware payload.");
using var missingHttp = new HttpClient(new FakeHandler(HttpStatusCode.NotFound, []));
var service = new IntentComponentService(() => null, missingHttp, valid); string? error = null;
try { await service.DownloadAsync(new Progress<double>(), CancellationToken.None); } catch (IOException ex) { error = ex.Message; }
Check(error?.Contains("GitHub Releases") == true, "Unpublished download did not offer offline import.");
using var oversizedHttp = new HttpClient(new FakeHandler(HttpStatusCode.OK, new byte[4096]));
service = new IntentComponentService(() => null, oversizedHttp, valid with { Bytes = 1 }); error = null;
try { await service.DownloadAsync(new Progress<double>(), CancellationToken.None); } catch (InvalidDataException ex) { error = ex.Message; }
Check(error != null, "Oversized download accepted.");
Check(!Directory.EnumerateDirectories(root, ".Intent-*").Any(), "Failed validation left temporary payloads.");
Console.WriteLine($"PASS: {checks} component download / hash / architecture / traversal / cancel / atomic installation checks.");

sealed class FakeHandler(HttpStatusCode code, byte[] data) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(code) { Content = new ByteArrayContent(data) });
}
