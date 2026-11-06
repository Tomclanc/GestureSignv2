using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace GestureSign.WinUI;
public sealed partial class MainWindow
{
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
            var panel = NewCardPanel(8);
            panel.Children.Add(new TextBlock { Text = T("仅导出版本、系统架构和你选择的日志，不包含动作配置。日志可能包含窗口标题、应用路径或命令内容；用户名路径会替换，但其他敏感内容仍需检查。文件只保存在本机，不会自动上传。", "Exports version, OS architecture and selected logs, without action configuration. Logs may contain window titles, application paths or command content. User-profile paths are redacted; review other sensitive content. Files stay local and are never uploaded automatically."), TextWrapping = TextWrapping.Wrap });
            var selected = new System.Collections.Generic.List<(string Path, CheckBox Check)>();
            if (Directory.Exists(_legacyData.LocalPath))
            foreach (var path in Directory.EnumerateFiles(_legacyData.LocalPath, "*.log").Take(20))
            {
                var check = new CheckBox { Content = Path.GetFileName(path), IsChecked = false };
                panel.Children.Add(check); selected.Add((path, check));
            }
            if (!await ConfirmDialogAsync(T("诊断包内容", "Diagnostic package contents"), panel, T("预览", "Preview"))) return;
            await NotifyDaemonAsync(DaemonCommand.InputHealth);
            var files = new System.Collections.Generic.Dictionary<string, string>();
            files["system.txt"] = $"Version: {AppVersion}\nOS: {Environment.OSVersion}\nArchitecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}\nUTC: {DateTime.UtcNow:O}";
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var item in selected.Where(item => item.Check.IsChecked == true))
            {
                // Bounded snapshots keep export responsive even after long logging sessions.
                using var stream = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 1024 * 1024) stream.Seek(-1024 * 1024, SeekOrigin.End);
                using var reader = new StreamReader(stream);
                var text = await reader.ReadToEndAsync();
                if (!string.IsNullOrEmpty(profile)) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
                files[Path.GetFileName(item.Path)] = text;
            }
            var content = new TextBox { Text = string.Join("\n\n", files.Select(item => "--- " + item.Key + " ---\n" + item.Value)), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 320 };
            if (!await ConfirmDialogAsync(T("检查诊断内容", "Review diagnostic contents"), content, T("导出", "Export"))) return;
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
        }
        catch (Exception ex) { await ShowInfoDialog(T("导出失败", "Export failed"), ex.Message); }
    }
}
