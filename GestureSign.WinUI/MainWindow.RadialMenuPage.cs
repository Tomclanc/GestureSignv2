using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GestureSign.Foundation.RadialMenus;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace GestureSign.WinUI;

/// <summary>
/// "Middle-button radial menu" page: application list on the left, the
/// selected application's wheel in the middle and the shared action library
/// on the right. Slots reference library actions, so an action can be placed
/// on many wheels; empty slots of an application wheel inherit the global wheel.
/// </summary>
public sealed partial class MainWindow
{
    private const double WheelSize = 500;
    private const double WheelDeadZone = 36;

    /// <summary>Ring edges in the settings preview, from the dead zone outwards, for 1 to 3 rings.</summary>
    private static double[] WheelRadii(int rings) => rings switch
    {
        <= 1 => new[] { WheelDeadZone, 140d },
        2 => new[] { WheelDeadZone, 125d, 215d },
        _ => new[] { WheelDeadZone, 112d, 180d, 244d }
    };

    private RadialMenuConfig? _radialConfig;
    private string? _radialSelectedMenuId;
    private string? _radialCopiedActionId;
    private Dictionary<string, string>? _radialCopiedSlots;
    private string _radialLibraryFilter = "";

    private Grid? _radialMenuListHost;
    private Grid? _radialWheelHost;
    private Grid? _radialLibraryHost;

    private string RadialConfigPath => RadialMenuConfig.PathIn(_legacyData.RoamingPath);

    private UIElement BuildRadialMenuPage()
    {
        _radialConfig = LoadRadialConfigForEditing();
        var root = NewSection();
        root.Children.Add(NewRadialMenuSwitchesCard());

        var columns = new Grid { ColumnSpacing = 16 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        _radialMenuListHost = new Grid();
        _radialWheelHost = new Grid();
        _radialLibraryHost = new Grid();
        Grid.SetColumn(_radialWheelHost, 1);
        Grid.SetColumn(_radialLibraryHost, 2);
        columns.Children.Add(_radialMenuListHost);
        columns.Children.Add(_radialWheelHost);
        columns.Children.Add(_radialLibraryHost);
        root.Children.Add(NewCard(columns));

        RefreshRadialPage();
        return root;
    }

    private FrameworkElement NewRadialMenuSwitchesCard()
    {
        var panel = NewCardPanel(8);
        var options = _legacyData.Options;
        var enabledSwitch = NewRadialMenuToggle(options.RadialMenuEnabled);
        var outerSwitch = NewRadialMenuToggle(options.RadialMenuOuterRing);
        var thirdSwitch = NewRadialMenuToggle(options.RadialMenuThirdRing);
        thirdSwitch.IsEnabled = options.RadialMenuOuterRing;
        enabledSwitch.Toggled += (_, _) => UpdateOptionAndReload("RadialMenuEnabled", enabledSwitch.IsOn ? "True" : "False");
        outerSwitch.Toggled += (_, _) =>
        {
            UpdateOptionAndReload("RadialMenuOuterRing", outerSwitch.IsOn ? "True" : "False");
            thirdSwitch.IsEnabled = outerSwitch.IsOn;
            _radialRingCount = !outerSwitch.IsOn ? 1 : thirdSwitch.IsOn ? 3 : 2;
            RefreshRadialWheel();
        };
        thirdSwitch.Toggled += (_, _) =>
        {
            UpdateOptionAndReload("RadialMenuThirdRing", thirdSwitch.IsOn ? "True" : "False");
            _radialRingCount = !outerSwitch.IsOn ? 1 : thirdSwitch.IsOn ? 3 : 2;
            RefreshRadialWheel();
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(thirdSwitch, T("第三圈", "Third ring"));
        _radialRingCount = !options.RadialMenuOuterRing ? 1 : options.RadialMenuThirdRing ? 3 : 2;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(enabledSwitch, L("中键轮盘", "Middle-button radial menu", "中鍵輪盤", "가운데 버튼 원형 메뉴"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(outerSwitch, L("外圈", "Outer ring", "外圈", "바깥쪽 링"));
        panel.Children.Add(NewSettingRow(L("启用", "Enable", "啟用", "사용"), null, enabledSwitch));
        panel.Children.Add(NewSettingRow(L("外圈", "Outer ring", "外圈", "바깥쪽 링"), null, outerSwitch));
        panel.Children.Add(NewSettingRow(T("第三圈", "Third ring"), null, thirdSwitch));
        return NewCard(panel);
    }

    private int _radialRingCount = 2;

    private void RefreshRadialPage()
    {
        RefreshRadialMenuList();
        RefreshRadialWheel();
        RefreshRadialLibrary();
    }

    // ----- persistence --------------------------------------------------

    private RadialMenuConfig LoadRadialConfigForEditing()
    {
        try
        {
            var loaded = RadialMenuConfig.Load(RadialConfigPath);
            if (loaded != null)
                return loaded;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        var config = new RadialMenuConfig();
        if (MigrateLegacyRadialSlots(config))
            SaveRadialConfig(config);
        config.Normalize();
        return config;
    }

    private void SaveRadialConfig(RadialMenuConfig? config = null)
    {
        config ??= _radialConfig;
        if (config is null)
            return;
        try
        {
            config.Save(RadialConfigPath);
        }
        catch (Exception ex)
        {
            _ = ShowInfoDialog(T("保存失败", "Save failed"), ex.Message);
        }
    }

    /// <summary>
    /// Once RadialMenus.json exists, the slots of the first radial menu version
    /// (gesture actions named "RadialMenu.Inner.N"/"RadialMenu.Outer.N") have been
    /// copied into it and are no longer used; remove them from the action list.
    /// </summary>
    private void RemoveMigratedRadialSlotActions()
    {
        try
        {
            if (!File.Exists(RadialConfigPath))
                return;
            var stale = _legacyData.Applications
                .SelectMany(app => app.Actions.Where(action => RadialMenuIndex(action.GestureName) > 0).Select(action => (app, action)))
                .ToList();
            if (stale.Count == 0)
                return;
            foreach (var (app, action) in stale)
                _legacyData.DeleteAction(app, action);
            _legacyData = LegacyDataStore.Load();
            _ = NotifyDaemonAsync(DaemonCommand.LoadApplications);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    /// <summary>
    /// Moves slots configured with the earlier built-in triggers
    /// ("RadialMenu.Inner.0" actions in gesture groups) into the new model.
    /// </summary>
    private bool MigrateLegacyRadialSlots(RadialMenuConfig config)
    {
        var migrated = false;
        config.Normalize();
        foreach (var app in _legacyData.Applications)
        {
            var legacySlots = app.Actions.Where(a => RadialMenuIndex(a.GestureName) > 0 && a.Commands.Count > 0).ToList();
            if (legacySlots.Count == 0)
                continue;

            RadialMenuDefinition? menu;
            if (app.Type == "全局")
                menu = config.GlobalMenu;
            else if (app.MatchUsing == 2 && !string.IsNullOrWhiteSpace(app.MatchString))
            {
                menu = new RadialMenuDefinition { Id = RadialMenuConfig.NewId(), Scope = RadialMenuScope.Application, Name = app.Name, Processes = app.MatchString };
                config.Menus.Add(menu);
            }
            else
                continue;

            foreach (var legacy in legacySlots)
            {
                var action = new RadialMenuAction
                {
                    Id = RadialMenuConfig.NewId(),
                    Name = RadialMenuSlotLabel(legacy),
                    IsEnabled = legacy.IsEnabled,
                    Commands = legacy.Commands.Select(c => new RadialMenuCommand { Name = c.Name, PluginClass = c.PluginClass, CommandSettings = c.Settings, IsEnabled = true }).ToList()
                };
                config.Actions.Add(action);
                var index = RadialMenuIndex(legacy.GestureName) - FirstRadialMenuIndex;
                menu.Slots[RadialMenuConfig.SlotKey(index >= 8, index % 8)] = action.Id;
                migrated = true;
            }
        }
        return migrated;
    }

    private RadialMenuDefinition SelectedRadialMenu()
    {
        var config = _radialConfig!;
        return config.Menus.FirstOrDefault(m => m.Id == _radialSelectedMenuId) ?? config.GlobalMenu;
    }

    /// <summary>Taskbar and desktop entries always exist so they can be configured.</summary>
    private RadialMenuDefinition EnsureSpecialMenu(RadialMenuScope scope)
    {
        var config = _radialConfig!;
        var menu = config.Menus.FirstOrDefault(m => m.Scope == scope);
        if (menu is null)
        {
            menu = new RadialMenuDefinition { Id = RadialMenuConfig.NewId(), Scope = scope };
            config.Menus.Add(menu);
        }
        return menu;
    }

    private string RadialMenuTitle(RadialMenuDefinition menu) => menu.Scope switch
    {
        RadialMenuScope.Global => L("全局", "Global", "全域", "전역"),
        RadialMenuScope.Taskbar => T("任务栏", "Taskbar"),
        RadialMenuScope.Desktop => T("桌面", "Desktop"),
        _ => string.IsNullOrWhiteSpace(menu.Name) ? menu.Processes ?? "" : menu.Name
    };

    // ----- application list (left) --------------------------------------

    private void RefreshRadialMenuList()
    {
        if (_radialMenuListHost is null || _radialConfig is null)
            return;
        EnsureSpecialMenu(RadialMenuScope.Taskbar);
        EnsureSpecialMenu(RadialMenuScope.Desktop);
        var menus = _radialConfig.Menus
            .OrderBy(m => m.Scope)
            .ThenBy(m => RadialMenuTitle(m), StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 520 };
        foreach (var menu in menus)
        {
            var glyph = menu.Scope switch
            {
                RadialMenuScope.Global => "",
                RadialMenuScope.Taskbar => "",
                RadialMenuScope.Desktop => "",
                _ => ""
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Tag = menu.Id, Padding = new Thickness(0, 4, 0, 4) };
            row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = RadialMenuTitle(menu), TextTrimming = TextTrimming.CharacterEllipsis });
            var subtitle = menu.Scope == RadialMenuScope.Application ? menu.Processes : null;
            if (!string.IsNullOrWhiteSpace(subtitle))
                text.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(text);
            row.Opacity = menu.Disabled ? 0.5 : 1;
            list.Items.Add(row);
            if (menu == SelectedRadialMenu())
                list.SelectedItem = row;
        }
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is FrameworkElement { Tag: string id } && id != _radialSelectedMenuId)
            {
                _radialSelectedMenuId = id;
                RefreshRadialWheel();
            }
        };

        var add = NewPillButton(L("添加", "Add", "新增", "추가"), false);
        add.Click += async (_, _) => await RunUiActionAsync(AddRadialApplicationMenuAsync);
        var delete = NewPillButton(L("删除", "Delete", "刪除", "삭제"), false);
        delete.Click += async (_, _) => await RunUiActionAsync(DeleteSelectedRadialMenuAsync);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(add);
        buttons.Children.Add(delete);

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = L("程序", "Applications", "程式", "프로그램"), Style = BodyStrongTextBlockStyle });
        panel.Children.Add(list);
        panel.Children.Add(buttons);
        _radialMenuListHost.Children.Clear();
        _radialMenuListHost.Children.Add(panel);
    }

    private async Task AddRadialApplicationMenuAsync()
    {
        var processes = new List<RunningProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.ProcessName))
                        continue;
                    var fileName = GetProcessFileName(process);
                    processes.Add(new RunningProcessInfo(process.ProcessName, string.IsNullOrWhiteSpace(fileName) ? process.ProcessName + ".exe" : fileName));
                }
                catch
                {
                    // Protected processes are skipped.
                }
            }
        }
        processes = processes.DistinctBy(p => p.FileName, StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = L("选择运行中的程序", "Select a running app", "選擇執行中的程式", "실행 중인 프로그램 선택") };
        foreach (var process in processes)
            combo.Items.Add($"{process.Name} ({process.FileName})");
        var name = new TextBox { Header = L("名称", "Name", "名稱", "이름") };
        var match = new TextBox { Header = "exe" };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0 || combo.SelectedIndex >= processes.Count) return;
            name.Text = processes[combo.SelectedIndex].Name;
            match.Text = processes[combo.SelectedIndex].FileName;
        };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(combo);
        panel.Children.Add(name);
        panel.Children.Add(match);

        if (!await ConfirmDialogAsync(L("添加", "Add", "新增", "추가"), panel, L("保存", "Save", "儲存", "저장"),
                () => !string.IsNullOrWhiteSpace(match.Text)))
            return;

        var processesText = string.Join(";", match.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p : p + ".exe"));
        var existing = _radialConfig!.Menus.FirstOrDefault(m => m.Scope == RadialMenuScope.Application &&
                                                               string.Equals(m.Processes, processesText, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new RadialMenuDefinition
            {
                Id = RadialMenuConfig.NewId(),
                Scope = RadialMenuScope.Application,
                Name = string.IsNullOrWhiteSpace(name.Text) ? System.IO.Path.GetFileNameWithoutExtension(processesText.Split(';')[0]) : name.Text.Trim(),
                Processes = processesText
            };
            _radialConfig.Menus.Add(existing);
            SaveRadialConfig();
        }
        _radialSelectedMenuId = existing.Id;
        RefreshRadialPage();
    }

    private async Task DeleteSelectedRadialMenuAsync()
    {
        var menu = SelectedRadialMenu();
        if (menu.Scope != RadialMenuScope.Application)
            return;
        if (!await ConfirmDialogAsync(DeleteConfirmationTitle(), RadialMenuTitle(menu), DeleteButtonText()))
            return;
        _radialConfig!.Menus.Remove(menu);
        _radialSelectedMenuId = null;
        SaveRadialConfig();
        RefreshRadialPage();
    }
}
