using System;
using System.Threading.Tasks;
using GestureSign.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private bool _kandoComponentBusy;

    private async Task CheckKandoUpdateAsync(Button button)
    {
        if (_kandoComponentBusy) return;
        _kandoComponentBusy = true;
        button.IsEnabled = false;
        var originalContent = button.Content;
        try
        {
            button.Content = T("检查中…", "Checking…");
            var release = await KandoComponentService.GetLatestReleaseAsync();
            var executable = FindKandoExecutablePath(_legacyData.Options.KandoExecutablePath);
            var managed = KandoComponentService.IsManagedExecutable(executable);
            var current = KandoRelease.ReadInstalledVersion(executable);
            var installed = KandoRelease.ParseVersion(current);
            var available = installed is null || release.Version > installed;
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = F("当前版本：{0}\n最新正式版：{1}", "Current version: {0}\nLatest stable release: {1}",
                    current ?? T("版本未知", "Unknown version"), release.TagName),
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = !managed
                    ? T("这是外部 Kando，请使用原安装方式更新。通过下方链接下载时，请按原安装位置和配置方式升级。GestureSign 不会覆盖此安装。", "This is an external Kando installation. Update through its original installation method. If downloading below, use its existing installation location and configuration. GestureSign will not overwrite this installation.")
                    : available
                        ? T("更新会保留菜单、个人设置和手势绑定。下载完成后会短暂关闭 Kando，并恢复此前的运行状态；失败时恢复旧版。", "The update keeps menus, personal settings and gesture bindings. After downloading, Kando will close briefly and return to its previous running state. If the update fails, the previous version is restored.")
                        : T("当前已是最新正式版，或版本高于最新正式版。", "Your version is the latest stable release or newer."),
                TextWrapping = TextWrapping.Wrap
            });
            var releaseLink = new HyperlinkButton { Content = T("查看发布说明和下载", "View release notes and downloads"), NavigateUri = release.ReleaseUri };
            panel.Children.Add(releaseLink);
            panel.Children.Add(new TextBlock { Text = T("更新说明", "Release notes"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(new ScrollViewer
            {
                MaxHeight = 260,
                Content = new TextBlock { Text = string.IsNullOrWhiteSpace(release.Notes) ? T("未提供更新说明。", "No release notes were provided.") : release.Notes, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
            });
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = T("Kando 更新", "Kando update"),
                Content = panel,
                CloseButtonText = T("关闭", "Close"),
                PrimaryButtonText = managed && available ? T("立即更新", "Update now") : "",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await UpdateManagedKandoAsync(button);
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = true;
            _kandoComponentBusy = false;
        }
    }

    private async Task UpdateManagedKandoAsync(Button button)
    {
        // Recheck the active path after the dialog, before modifying anything.
        if (!KandoComponentService.IsManagedExecutable(FindKandoExecutablePath(_legacyData.Options.KandoExecutablePath)))
            throw new InvalidOperationException(T("当前 Kando 不是由 GestureSign 管理的组件。", "The active Kando installation is not managed by GestureSign."));
        var options = _legacyData.Options;
        var wasRunning = false;
        IDisposable? updateLease = null;
        async Task StopAsync() => await Kando.StopAndWaitAsync(options);
        async Task RestartAsync()
        {
            if (!wasRunning) return;
            using var process = StartKandoProcess(options, string.Empty);
            if (process is null) throw new InvalidOperationException(T("Kando 启动失败。", "Kando could not start."));
            await Task.Delay(2000);
            if (!IsKandoRunning(options))
                throw new InvalidOperationException(T("Kando 在启动后退出。", "Kando exited after starting."));
        }
        try
        {
            var progress = new Progress<double>(value => button.Content = F("更新中 {0:0}%", "Updating {0:0}%", value));
            await KandoComponentService.DownloadAndInstallAsync(progress,
                beforeReplace: async () =>
                {
                    if (!KandoComponentService.IsManagedExecutable(FindKandoExecutablePath(_legacyData.Options.KandoExecutablePath)))
                        throw new InvalidOperationException(T("当前 Kando 不是由 GestureSign 管理的组件。", "The active Kando installation is not managed by GestureSign."));
                    // The lease blocks daemon launches without changing settings.
                    // Windows releases it even if this process crashes.
                    updateLease = KandoComponentPaths.AcquireUpdateLease(KandoComponentPaths.UpdateLeasePath);
                    wasRunning = IsKandoRunning(options);
                    await StopAsync();
                },
                validate: RestartAsync,
                stop: StopAsync,
                restore: RestartAsync);
        }
        catch (Exception error) when (error is not AggregateException and not KandoReleaseLookupException)
        {
            throw new InvalidOperationException(T("Kando 更新失败，旧版程序和设置已保留或恢复。", "The Kando update failed. The previous application and settings were kept or restored.") + "\n\n" + error.Message, error);
        }
        finally
        {
            updateLease?.Dispose();
            ShowSelectedPage();
        }
        await ShowInfoDialog(T("Kando 已更新", "Kando updated"), T("已安装最新正式版，菜单、设置和手势绑定已保留。", "The latest stable release is installed. Menus, settings and gesture bindings were kept."));
    }
}
