using GestureSign.Foundation.Intent;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.WinUI.Services;

internal sealed class IntentComponentService
{
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _client;
    private readonly IntentComponentAsset? _asset;
    public IntentComponentService(HttpClient? client = null, IntentComponentAsset? asset = null) { _client = client ?? Client; _asset = asset; }
    private readonly SemaphoreSlim _requests = new(1, 1);
    private Process? _host;
    public static string InstallDirectory => IntentComponentLocation.Resolve(AppContext.BaseDirectory);
    public static string Executable => Path.Combine(InstallDirectory, "Runtime", "GestureSign.IntentDlc.exe");
    public bool Installed
    {
        get
        {
            try { var marker = IntentFiles.Read<IntentComponentManifest>(Path.Combine(InstallDirectory, "component.json")); return File.Exists(Executable) && marker?.Protocol == 2 && marker.Version == IntentComponentPackage.ComponentVersion && marker.Architecture == Architecture; }
            catch { return false; }
        }
    }
    public static string Architecture => RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
    public IntentComponentAsset Asset => _asset ?? (IntentFiles.Read<IntentComponentAsset[]>(Path.Combine(AppContext.BaseDirectory, "Assets", "intent-dlc.catalog.json")) ?? [])
        .FirstOrDefault(a => a.Architecture == Architecture) ?? throw new InvalidDataException("此版本尚未提供匹配的组件下载目录。");

    public async Task DownloadAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        IntentComponentLocation.CheckWritable(InstallDirectory);
        var asset = Asset;
        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/Tomclanc/GestureSignv2/releases/download/", StringComparison.Ordinal)) throw new InvalidDataException("组件下载地址无效。");
        var temp = Path.Combine(Path.GetTempPath(), "IntentDlc-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromMinutes(10));
            using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) throw new IOException("这个组件版本尚未上传到 GitHub Releases，可先选择“导入组件包”使用配套离线包。");
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            {
                var buffer = new byte[131072]; long received = 0;
                while (true)
                {
                    int count = await input.ReadAsync(buffer, timeout.Token); if (count == 0) break;
                    received += count; if (received > asset.Bytes) throw new InvalidDataException("组件下载大小异常。");
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token); progress.Report(received * 90d / asset.Bytes);
                }
            }
            await InstallAsync(temp, cancellationToken); progress.Report(100);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public async Task InstallAsync(string archivePath, CancellationToken cancellationToken)
    {
        IntentComponentLocation.CheckWritable(InstallDirectory);
        var asset = Asset; await StopAsync();
        await Task.Run(() => IntentComponentPackage.Install(archivePath, asset, InstallDirectory, cancellationToken), cancellationToken);
    }
    public async Task UninstallAsync()
    {
        await StopAsync();
        // Only the fixed component payload is removed; local samples/model live elsewhere.
        await Task.Run(() => { if (Directory.Exists(InstallDirectory)) Directory.Delete(InstallDirectory, true); });
    }
    public async Task<IntentHostResponse> SendAsync(IntentHostRequest request, bool start = true)
    {
        await _requests.WaitAsync();
        try
        {
            try { return await ExchangeAsync(request, 1000); }
            catch (HostUnavailableException) when (start)
            {
                if (!Installed) throw new InvalidOperationException("请先下载或导入学习组件。");
                if (_host == null || _host.HasExited)
                {
                    using var daemon = Process.GetProcessesByName("GestureSign").FirstOrDefault(p => p.SessionId == Process.GetCurrentProcess().SessionId)
                        ?? throw new InvalidOperationException("请先启动 GestureSign 手势后台。");
                    var info = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(Executable)! };
                    info.ArgumentList.Add("--serve"); info.ArgumentList.Add("--daemon-pid"); info.ArgumentList.Add(daemon.Id.ToString());
                    _host?.Dispose(); _host = Process.Start(info);
                }
                return await ExchangeAsync(request, 5000);
            }
        }
        finally { _requests.Release(); }
    }
    private static async Task<IntentHostResponse> ExchangeAsync(IntentHostRequest request, int milliseconds)
    {
        using var timeout = new CancellationTokenSource(milliseconds);
        using var pipe = new NamedPipeClientStream(".", IntentFiles.PipeName + ".control", PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await pipe.ConnectAsync(timeout.Token); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { throw new HostUnavailableException(ex); }
        using var reader = new StreamReader(pipe, leaveOpen: true); using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token);
        var line = await reader.ReadLineAsync(timeout.Token);
        if (line == null || line.Length > 1_000_000) throw new InvalidDataException("学习组件响应无效。");
        var response = JsonSerializer.Deserialize<IntentHostResponse>(line) ?? throw new InvalidDataException("学习组件未返回状态。");
        if (response.Protocol != 2) throw new InvalidDataException("请更新学习组件。");
        if (response.Error != null) throw new InvalidOperationException(response.Error);
        return response;
    }
    private sealed class HostUnavailableException(Exception inner) : IOException("学习引擎未连接。", inner);
    private async Task StopAsync()
    {
        try { await SendAsync(new("stop"), start: false); } catch (IOException) { } catch (OperationCanceledException) { }
        // A prior UI instance may own the process; the lock file is the authoritative lifetime check.
        for (int i = 0; i < 30; i++)
        {
            try { Directory.CreateDirectory(IntentFiles.Root); using var file = new FileStream(Path.Combine(IntentFiles.Root, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); return; }
            catch (IOException) { await Task.Delay(100); }
        }
        throw new IOException("学习组件正在退出，请稍后重试。");
    }
}
