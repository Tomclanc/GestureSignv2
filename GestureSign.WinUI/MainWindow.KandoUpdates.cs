using System;
using System.Threading.Tasks;
using GestureSign.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private bool _kandoComponentBusy;

    private string KandoText(string chinese, string english)
        => UiTranslationCatalog.TranslateComponent(ResolveUiCultureName(_uiCultureName), english) is var translated && translated != english ? translated : T(chinese, english);

    private string KandoFormat(string chinese, string english, params object?[] values)
        => string.Format(KandoText(chinese, english), values);

    private async Task RunKandoActionAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error)
        {
            LogException(error);
            var details = KandoText("详细原因请查看日志。", "See the log for details.");
            foreach (var cause in FlattenKandoErrors(error))
            {
                if (cause is UnauthorizedAccessException || cause is System.ComponentModel.Win32Exception { NativeErrorCode: 5 })
                {
                    details = KandoText("访问被拒绝，请关闭 Kando 后重试。", "Access denied. Close Kando and try again.") + "\n\n" + details;
                    break;
                }
                if (cause is System.Net.Http.HttpRequestException or OperationCanceledException)
                {
                    details = KandoText("连接失败，请稍后重试。", "Connection failed. Try again later.") + "\n\n" + details;
                    break;
                }
            }
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = KandoText("Kando 更新", "Kando update"),
                Content = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = KandoText("查看日志", "View logs"),
                CloseButtonText = KandoText("关闭", "Close"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_legacyData.LocalPath) { UseShellExecute = true });
        }
    }

    private static System.Collections.Generic.IEnumerable<Exception> FlattenKandoErrors(Exception error)
    {
        yield return error;
        if (error is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                foreach (var cause in FlattenKandoErrors(inner)) yield return cause;
        }
        else if (error.InnerException is { } inner)
            foreach (var cause in FlattenKandoErrors(inner)) yield return cause;
    }

    private async Task CheckKandoUpdateAsync(Button button)
    {
        if (_kandoComponentBusy) return;
        _kandoComponentBusy = true;
        button.IsEnabled = false;
        var originalContent = button.Content;
        try
        {
            button.Content = KandoText("检查中…", "Checking…");
            var release = await KandoComponentService.GetLatestReleaseAsync();
            var executable = FindKandoExecutablePath(_legacyData.Options.KandoExecutablePath);
            var managed = KandoComponentService.IsManagedExecutable(executable);
            var current = KandoRelease.ReadInstalledVersion(executable);
            var installed = KandoRelease.ParseVersion(current);
            var available = installed is null || release.Version > installed;
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = KandoFormat("当前版本：{0}\n最新正式版：{1}", "Current version: {0}\nLatest stable release: {1}",
                    current ?? KandoText("版本未知", "Unknown version"), release.TagName),
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = !managed
                    ? KandoText("这是外部 Kando，请使用原安装方式更新。通过下方链接下载时，请按原安装位置和配置方式升级。GestureSign 不会覆盖此安装。", "This is an external Kando installation. Update through its original installation method. If downloading below, use its existing installation location and configuration. GestureSign will not overwrite this installation.")
                    : available
                        ? KandoText("更新会保留菜单、个人设置和手势绑定。下载完成后会短暂关闭 Kando，并恢复此前的运行状态；失败时恢复旧版。", "The update keeps menus, personal settings and gesture bindings. After downloading, Kando will close briefly and return to its previous running state. If the update fails, the previous version is restored.")
                        : KandoText("当前已是最新正式版，或版本高于最新正式版。", "Your version is the latest stable release or newer."),
                TextWrapping = TextWrapping.Wrap
            });
            var releaseLink = new HyperlinkButton { Content = KandoText("查看发布说明和下载", "View release notes and downloads"), NavigateUri = release.ReleaseUri };
            panel.Children.Add(releaseLink);
            panel.Children.Add(new TextBlock { Text = KandoText("官方更新说明（原文）", "Official release notes (original language)"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(new ScrollViewer
            {
                MaxHeight = 260,
                Content = new TextBlock { Text = string.IsNullOrWhiteSpace(release.Notes) ? KandoText("未提供更新说明。", "No release notes were provided.") : System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(release.Notes, @"\[([^\]]+)\]\((https?://[^)]+)\)", "$1 ($2)"), @"\*\*([^*]+)\*\*", "$1"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
            });
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = KandoText("Kando 更新", "Kando update"),
                Content = panel,
                CloseButtonText = KandoText("关闭", "Close"),
                PrimaryButtonText = managed && available ? KandoText("立即更新", "Update now") : "",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await UpdateManagedKandoAsync(button, release);
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = true;
            _kandoComponentBusy = false;
        }
    }

    private async Task UpdateManagedKandoAsync(Button button, KandoRelease release)
    {
        // Recheck the active path after the dialog, before modifying anything.
        if (!KandoComponentService.IsManagedExecutable(FindKandoExecutablePath(_legacyData.Options.KandoExecutablePath)))
            throw new InvalidOperationException(KandoText("当前 Kando 不是由 GestureSign 管理的组件。", "The active Kando installation is not managed by GestureSign."));
        var options = _legacyData.Options;
        var wasRunning = false;
        IDisposable? updateLease = null;
        async Task StopAsync() => await Kando.StopAndWaitAsync(options, message => LogException(new InvalidOperationException(message)));
        async Task RestartAsync()
        {
            if (!wasRunning) return;
            using var process = StartKandoProcess(options, string.Empty);
            if (process is null) throw new InvalidOperationException(KandoText("Kando 启动失败。", "Kando could not start."));
            await Task.Delay(2000);
            if (!IsKandoRunning(options))
                throw new InvalidOperationException(KandoText("Kando 在启动后退出。", "Kando exited after starting."));
        }
        try
        {
            var progress = new Progress<double>(value => button.Content = KandoFormat("更新中 {0:0}%", "Updating {0:0}%", value));
            await KandoComponentService.DownloadAndInstallAsync(progress,
                beforeReplace: async () =>
                {
                    if (!KandoComponentService.IsManagedExecutable(FindKandoExecutablePath(_legacyData.Options.KandoExecutablePath)))
                        throw new InvalidOperationException(KandoText("当前 Kando 不是由 GestureSign 管理的组件。", "The active Kando installation is not managed by GestureSign."));
                    // The lease blocks daemon launches without changing settings.
                    // Windows releases it even if this process crashes.
                    updateLease = KandoComponentPaths.AcquireUpdateLease(KandoComponentPaths.UpdateLeasePath);
                    wasRunning = IsKandoRunning(options);
                    await StopAsync();
                },
                validate: RestartAsync,
                stop: StopAsync,
                restore: RestartAsync, release: release);
        }
        catch (Exception error) when (error is not AggregateException)
        {
            throw new InvalidOperationException(KandoText("Kando 更新失败，旧版程序和设置已保留或恢复。", "The Kando update failed. The previous application and settings were kept or restored."), error);
        }
        finally
        {
            updateLease?.Dispose();
            ShowSelectedPage();
        }
        await ShowInfoDialog(KandoText("Kando 已更新", "Kando updated"), KandoText("已安装最新正式版，菜单、设置和手势绑定已保留。", "The latest stable release is installed. Menus, settings and gesture bindings were kept."));
    }
}
