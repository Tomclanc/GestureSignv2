using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GestureSign.Foundation.Diagnostics;
using GestureSign.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace GestureSign.WinUI;
public sealed partial class MainWindow
{
    private static readonly Lazy<LocalDiagnostics.Writer<StartupDiagnostic>> StartupDiagnostics = new(() =>
        new(LocalDiagnostics.DirectoryFor(KandoComponentPaths.ProductDataDirectory), LocalDiagnostics.StartupFile));
    private int? _startupDiagnosticError;

    private StartupDiagnostic PublishStartupDiagnostic(bool adminEnabled)
    {
        var snapshot = new StartupDiagnostic(DateTimeOffset.UtcNow, adminEnabled, _startupDiagnosticError);
        StartupDiagnostics.Value.Publish(snapshot);
        return snapshot;
    }

    private async Task<bool> ImportArchiveWithPreviewAsync(string file)
    {
        var snapshot = Path.Combine(Path.GetTempPath(), "GestureSign-import-" + Guid.NewGuid().ToString("N") + ".ges");
        try
        {
            File.Copy(file, snapshot);
            var preview = _legacyData.PreviewArchive(snapshot);
            var panel = NewCardPanel(8);
            panel.Children.Add(new TextBlock { Text = F("程序：{0}，动作：{1}，手势：{2}，同名手势绑定：{3}", "Applications: {0}, actions: {1}, gestures: {2}, matching gesture bindings: {3}", preview.Applications, preview.Actions, preview.Gestures, preview.Conflicts), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = T("本次导入会替换存档中包含的配置文件，不会合并。导入前自动备份；写入失败时恢复原配置。", "This import replaces the configuration files included in the archive; it does not merge them. Existing files are backed up and restored if writing fails."), TextWrapping = TextWrapping.Wrap });
            if (preview.IncludesOptions) panel.Children.Add(new TextBlock { Text = T("存档包含全局设置，也将被替换。", "The archive includes global settings, which will also be replaced."), TextWrapping = TextWrapping.Wrap });
            if (!await ConfirmDialogAsync(T("导入预览", "Import preview"), panel, T("备份并导入", "Back up and import"))) return false;
            _legacyData.RestoreArchive(snapshot);
            await ShowInfoDialog(T("导入完成", "Import complete"), T("原配置备份：", "Previous configuration backup: ") + _legacyData.LastImportBackupPath);
            return true;
        }
        catch (Exception ex)
        {
            await ShowInfoDialog(T("导入失败", "Import failed"), ex.Message);
            return false;
        }
        finally { if (File.Exists(snapshot)) File.Delete(snapshot); }
    }

    private async Task ExportDiagnosticsAsync()
    {
        try
        {
            await FlushPendingOptionUpdatesAsync();
            var source = LegacyDataStore.Load();
            var panel = NewCardPanel(8);
            panel.Children.Add(new TextBlock
            {
                Text = T("诊断包包含系统信息和你勾选的文件。配置及应用日志可能含应用路径、窗口标题或命令。输入诊断只含设备状态和事件次数。ZIP 仅保存在本机。", "The package includes system information and the files you select. Configuration and app logs may contain application paths, window titles, or commands. Input diagnostics contain registration status and event counts. The ZIP stays on this device."),
                TextWrapping = TextWrapping.Wrap
            });
            var selected = new List<(string Path, string Name, bool IsLog, CheckBox Check)>();
            if (Directory.Exists(source.LocalPath))
            {
                foreach (var path in Directory.EnumerateFiles(source.LocalPath, "*.log").OrderBy(Path.GetFileName).Take(20))
                    AddFile(path, Path.GetFileName(path), Path.GetFileName(path), isLog: true);
            }

            panel.Children.Add(new TextBlock { Text = T("配置文件", "Configuration files"), Style = BodyStrongTextBlockStyle });
            AddFile(source.ConfigPath, "config/GestureSign.config", T("全局设置 (GestureSign.config)", "Global settings (GestureSign.config)"));
            AddFile(source.ActionsPath, "config/Actions.gsa", T("动作和应用规则 (Actions.gsa)", "Actions and app rules (Actions.gsa)"));
            AddFile(source.GesturesPath, "config/Gestures.gest", T("手势定义 (Gestures.gest)", "Gesture definitions (Gestures.gest)"));
            AddFile(Path.Combine(KandoComponentPaths.PieMenuConfigurationDirectory, "menus.json"), "config/menus.json",
                T("快捷操作菜单 (menus.json)", "Quick Actions menus (menus.json)"));

            panel.Children.Add(new TextBlock { Text = T("系统输入诊断", "System input diagnostics"), Style = BodyStrongTextBlockStyle });
            panel.Children.Add(new TextBlock
            {
                Text = T("请先复现问题，再点预览。计数从 GestureSign 后台启动时累积，不包含输入文字、键码或指针坐标。", "Reproduce the problem before previewing. Counts are totals observed by GestureSign since its background process started. Typed text, key codes and pointer positions are excluded."),
                TextWrapping = TextWrapping.Wrap
            });
            var devices = new[]
            {
                AddDevice("TouchpadInput", "touchpad", T("触控板", "Touchpad")),
                AddDevice("TouchscreenInput", "touchscreen", T("触摸屏", "Touchscreen")),
                AddDevice("MouseInput", "mouse", T("鼠标", "Mouse")),
                AddDevice("KeyboardInput", "keyboard", T("键盘", "Keyboard"))
            };
            var structured = new CheckBox { Content = T("诊断包内容", "Diagnostic package contents") + " (JSON · MCP)" };
            panel.Children.Add(structured);
            if (!await ConfirmDialogAsync(T("诊断包内容", "Diagnostic package contents"), panel, T("预览", "Preview"))) return;

            var files = new Dictionary<string, string>
            {
                ["system.txt"] = $"Version: {AppVersion}\nOS: {Environment.OSVersion}\nArchitecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}\nUTC: {DateTime.UtcNow:O}"
            };
            var unavailableFiles = new List<string>();
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var item in selected.Where(item => item.Check.IsChecked == true))
            {
                try
                {
                    files[item.Name] = RedactDiagnosticPath(await ReadDiagnosticFileAsync(item.Path, item.IsLog), profile);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    unavailableFiles.Add($"{item.Name}: {(error is InvalidDataException ? "size limit exceeded" : "file unavailable")}");
                }
            }
            if (unavailableFiles.Count > 0)
                files["collection.txt"] = string.Join("\n", unavailableFiles);
            if (structured.IsChecked == true)
            {
                var adminEnabled = await Task.Run(IsAdminStartupEnabled);
                files["diagnostics/gesturesign-health.json"] = await LocalDiagnostics.ReportJsonAsync(
                    KandoComponentPaths.ProductDataDirectory, startupSnapshot: PublishStartupDiagnostic(adminEnabled));
            }

            var requestedDevices = devices.Where(item => item.Check.IsChecked == true).ToArray();
            if (requestedDevices.Length > 0)
            {
                var snapshot = await RequestDiagnosticInputHealthAsync(source.LocalPath);
                foreach (var device in requestedDevices)
                    files[$"input/{device.Name}.json"] = ProjectInputDiagnostics(snapshot, device.Key);
            }

            var preview = NewDiagnosticPreview(files);
            if (!await ConfirmDialogAsync(T("检查诊断内容", "Review diagnostic contents"), preview, T("导出", "Export"))) return;
            var destination = await PickSaveFileAsync("GestureSign-diagnostics.zip", ".zip");
            if (destination == null) return;
            var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
                    foreach (var item in files) { using var writer = new StreamWriter(zip.CreateEntry(item.Key).Open()); await writer.WriteAsync(item.Value); }
                File.Move(temp, destination, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            await ShowInfoDialog(T("导出完成", "Export complete"), destination);

            void AddFile(string? path, string name, string label, bool isLog = false)
            {
                var check = new CheckBox { Content = label, IsChecked = false, IsEnabled = File.Exists(path) };
                panel.Children.Add(check);
                if (path is not null) selected.Add((path, name, isLog, check));
            }

            (string Key, string Name, CheckBox Check) AddDevice(string key, string name, string label)
            {
                var check = new CheckBox { Content = label, IsChecked = false };
                panel.Children.Add(check);
                return (key, name, check);
            }
        }
        catch (Exception ex) { await ShowInfoDialog(T("导出失败", "Export failed"), ex.Message); }
    }

    private FrameworkElement NewDiagnosticPreview(Dictionary<string, string> files)
    {
        var names = new ComboBox
        {
            ItemsSource = files.Keys.ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(names, T("选择要预览的文件", "Preview file"));
        var text = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 320
        };
        names.SelectionChanged += (_, _) =>
        {
            if (names.SelectedItem is string name) text.Text = files[name];
        };
        names.SelectedIndex = 0;
        var panel = NewCardPanel(8);
        panel.Children.Add(names);
        panel.Children.Add(text);
        return panel;
    }

    private static async Task<string> ReadDiagnosticFileAsync(string path, bool tail)
    {
        const int logLimit = 1024 * 1024;
        const int configurationLimit = 2 * 1024 * 1024;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        if (!tail && length > configurationLimit) throw new InvalidDataException();
        var bytes = new byte[(int)Math.Min(length, tail ? logLimit : configurationLimit)];
        if (length > bytes.Length) stream.Seek(length - bytes.Length, SeekOrigin.Begin);
        await stream.ReadExactlyAsync(bytes);
        using var reader = new StreamReader(new MemoryStream(bytes, writable: false));
        if (length > bytes.Length) await reader.ReadLineAsync();
        return await reader.ReadToEndAsync();
    }

    private static string RedactDiagnosticPath(string text, string profile)
    {
        if (string.IsNullOrWhiteSpace(profile)) return text;
        var jsonPath = JsonEncodedText.Encode(profile, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString();
        var xmlPath = System.Security.SecurityElement.Escape(profile) ?? profile;
        return text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
            .Replace(jsonPath, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
            .Replace(xmlPath, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonElement> RequestDiagnosticInputHealthAsync(string directory)
    {
        var requestedAt = DateTimeOffset.UtcNow;
        if (!await SendDaemonCommandAsync(DaemonCommand.InputHealth)) return default;
        var path = Path.Combine(directory, "Input-health.json");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 64 * 1024) return default;
                using var document = await JsonDocument.ParseAsync(stream);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("Reason", out var reason) && reason.ValueKind == JsonValueKind.String &&
                    reason.GetString() == "SupportRequest" &&
                    root.TryGetProperty("RecordedUtc", out var utc) && utc.ValueKind == JsonValueKind.String &&
                    utc.TryGetDateTimeOffset(out var recordedAt) && recordedAt >= requestedAt)
                    return root.Clone();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { return default; }
            catch (JsonException) { }
            await Task.Delay(100);
        }
        return default;
    }

    private static string ProjectInputDiagnostics(JsonElement snapshot, string key)
    {
        var fields = key switch
        {
            "MouseInput" => new[] { "HookInstalled", "Moves", "ButtonDown", "ButtonUp", "Wheel" },
            "KeyboardInput" => new[] { "HookInstalled", "KeyDown", "KeyUp" },
            _ => new[] { "RawInputUsage", "RegistrationExpected", "Registered", "Reports", "FramesDelivered" }
        };
        var report = new Dictionary<string, object> { ["Source"] = "GestureSign", ["CountersSince"] = "daemon startup" };
        if (snapshot.ValueKind == JsonValueKind.Object &&
            snapshot.TryGetProperty(key, out var device) && device.ValueKind == JsonValueKind.Object)
        {
            report["Status"] = "available";
            report["RecordedUtc"] = snapshot.GetProperty("RecordedUtc").GetDateTimeOffset();
            foreach (var field in fields)
            {
                if (device.TryGetProperty(field, out var value) &&
                    value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)
                    report[field] = value;
            }
        }
        else report["Status"] = "no fresh per-device report from the background process";
        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }
}
