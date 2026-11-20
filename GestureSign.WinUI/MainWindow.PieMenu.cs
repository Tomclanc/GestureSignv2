using GestureSign.PieMenu;
using GestureSign.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PieMenuItem = GestureSign.PieMenu.PieMenu;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private MenuEditSession? _pieDraft;
    private string? _pieDraftExecutable;
    private bool _pieInstalling;
    private readonly SemaphoreSlim _pieRecordingLock = new(1, 1);

    internal void OpenQuickActions()
    {
        Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(item => (string?)item.Tag == "quickActions");
        if (_currentNavigationPage != "quickActions") ShowPage("quickActions");
    }

    private FrameworkElement NewPieMenuComponentCard(bool installed, out ToggleSwitch enabled)
    {
        var options = _legacyData.Options;
        enabled = NewKandoQuickActionsToggle(options.KandoEnabled);
        enabled.Header = T("环形菜单", "Pie menu");
        if (installed && PieMenuComponentService.IsCurrent(options.KandoExecutablePath))
            return enabled;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var download = NewPillButton(installed ? T("更新组件", "Update component") :
            T("下载组件", "Download component"), true);
        var import = NewPillButton(T("从 ZIP 安装", "Install from ZIP"), false);
        download.Click += async (_, _) => await RunUiActionAsync(() => InstallPieComponentAsync(download, import, true));
        import.Click += async (_, _) => await RunUiActionAsync(() => InstallPieComponentAsync(download, import, false));
        buttons.Children.Add(download);
        buttons.Children.Add(import);
        if (installed)
        {
            var settings = new StackPanel { Spacing = 8 };
            settings.Children.Add(enabled);
            settings.Children.Add(NewPowerToysSettingCard("\uE9D9",
                T("环形菜单组件更新", "Pie menu component update"),
                T("更新后使用 GestureSign 主托盘图标。", "The update uses the main GestureSign tray icon."), buttons));
            return settings;
        }
        return NewPowerToysSettingCard("\uE9D9", T("环形菜单 · 可选组件", "Pie menu · optional component"),
            T("需要时再下载。也可直接选择对应架构的预览 ZIP。",
                "Download it when needed, or install the preview ZIP for your architecture."), buttons);
    }

    private async Task InstallPieComponentAsync(Button download, Button import, bool fromGitHub)
    {
        if (_pieInstalling) return;
        _pieInstalling = true;
        var label = download.Content;
        download.IsEnabled = import.IsEnabled = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            string? executable;
            if (fromGitHub)
            {
                var progress = new Progress<double>(value => download.Content = T("下载中", "Downloading") + $" {value:0}%");
                executable = await PieMenuComponentService.DownloadAsync(progress, timeout.Token);
                if (executable is null)
                {
                    await ShowInfoDialog(T("下载尚未开放", "Download not available yet"),
                        T("GitHub 上还没有这个版本的组件。可以先用「从 ZIP 安装」。",
                            "This component version is not on GitHub yet. You can install the local ZIP."));
                    return;
                }
            }
            else
            {
                var zip = await PickOpenFileAsync([".zip"]);
                if (zip is null) return;
                executable = await PieMenuComponentService.InstallAsync(zip, timeout.Token);
            }

            await FlushPendingOptionUpdatesAsync();
            var old = LegacyDataStore.Load().Options;
            if (!string.Equals(old.KandoExecutablePath, executable, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (old.KandoEnabled)
                        await UpdateOptionAndWaitAsync("KandoEnabled", "False");
                    if (KandoComponentPaths.IsNativePieMenu(old.KandoExecutablePath))
                        await Kando.StopNativeAndWaitAsync(old);
                    else if (old.KandoEnabled)
                        StopKandoProcesses(old);
                    await UpdateOptionAndWaitAsync("KandoExecutablePath", executable);
                }
                finally
                {
                    if (old.KandoEnabled)
                        await EnableKandoQuickActionsAsync();
                }
            }
            ShowSelectedPage();
        }
        catch (Exception error) when (fromGitHub && error is HttpRequestException or OperationCanceledException)
        {
            LogException(error);
            await ShowInfoDialog(T("暂时无法下载", "Download unavailable"),
                T("无法从 GitHub 下载。可稍后重试，或选「从 ZIP 安装」。",
                    "The GitHub download is unavailable. Try again later or choose Install from ZIP."));
        }
        finally
        {
            download.Content = label;
            download.IsEnabled = import.IsEnabled = true;
            _pieInstalling = false;
        }
    }

    private MenuEditor? AddPieMenuSettings(StackPanel root, string executable)
    {
        try
        {
            PieMenuAssets.Root = Path.GetDirectoryName(executable)!;
            MenuStorage.UseDirectory(KandoComponentPaths.PieMenuConfigurationDirectory);
            var language = new MenuLanguage(ResolveUiCultureName(_uiCultureName));
            if (_pieDraft is null || !string.Equals(_pieDraftExecutable, executable, StringComparison.OrdinalIgnoreCase))
            {
                var previous = Path.Combine(KandoComponentPaths.NativeLocalApplicationData, "GestureSign V2", "PieMenu", "menus.json");
                var document = !File.Exists(MenuStorage.PathName) && File.Exists(previous) ? MenuStorage.Read(previous) : MenuStorage.Load(language);
                _pieDraft = new MenuEditSession(document);
                _pieDraftExecutable = executable;
            }
            return new MenuEditor(AppWindow.Id, root, _pieDraft, PreviewPieMenu,
                ReloadPieShortcutsAsync, SetPieRecordingAsync, language)
            {
                DeveloperCommand = command =>
                {
                    if (!StartKando(_legacyData.Options, "--developer " + command))
                        LogWinUiDaemonMessage("Failed to send PieMenu developer command.");
                }
            };

        }
        catch (Exception error)
        {
            LogException(error);
            root.Children.Add(new InfoBar { IsOpen = true, Severity = InfoBarSeverity.Error, Message = error.Message });
            return null;
        }
    }

    private void ApplyPieMenuSearch(MenuEditor editor)
    {
        if (_searchTarget is not { Page: "quickActions", MenuName: { } name }) return;
        editor.SelectMenu(name);
        _searchTarget = null;
    }

    private void RefreshPieMenuHostConfiguration()
    {
        var options = _legacyData.Options;
        if (options.KandoEnabled && KandoComponentPaths.IsNativePieMenu(options.KandoExecutablePath))
            StartKando(options, "--background");
    }

    private async Task<string> ReloadPieShortcutsAsync()
    {
        if (!_legacyData.Options.KandoEnabled) return "";
        var id = Guid.NewGuid().ToString("N");
        var statusFile = Path.Combine(KandoComponentPaths.PieMenuConfigurationDirectory, $".result-{id}.txt");
        var failure = T("菜单已保存，后台未确认重新绑定快捷键。", "Menus were saved, but the runtime did not confirm the shortcut reload.");
        try
        {
            using var request = StartKandoProcess(_legacyData.Options, $"--reload --result {id}");
            if (request is null) return failure;
            for (var attempt = 0; attempt < 60; attempt++)
            {
                if (File.Exists(statusFile)) return await File.ReadAllTextAsync(statusFile);
                await Task.Delay(100);
            }
            return failure;
        }
        finally { File.Delete(statusFile); }
    }

    private void PreviewPieMenu(MenuDocument document, PieMenuItem menu)
    {
        var path = Path.Combine(KandoComponentPaths.PieMenuConfigurationDirectory, $".preview-{Guid.NewGuid():N}.json");
        try
        {
            MenuStorage.Save(document, path);
            if (!StartKando(_legacyData.Options, $"--preview-file {QuoteArgument(path)} --preview-menu {document.Menus.IndexOf(menu).ToString(CultureInfo.InvariantCulture)}"))
                throw new InvalidOperationException(T("无法启动环形菜单预览。", "Could not start the pie menu preview."));
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    private async Task SetPieRecordingAsync(bool recording)
    {
        await _pieRecordingLock.WaitAsync();
        try
        {
            var options = _legacyData.Options;
            if (!options.KandoEnabled || !KandoComponentPaths.IsNativePieMenu(options.KandoExecutablePath)) return;
            using var process = StartKandoProcess(options, recording ? "--recording on" : "--recording off");
            if (process is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await process.WaitForExitAsync(timeout.Token);
            }
        }
        finally { _pieRecordingLock.Release(); }
    }
}
