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
/// "Radial menu" page: application list on the left, the
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
        var switches = NewRadialMenuSwitchesBar();

        // Three cards, laid out like the Quick Actions editor: applications on the left,
        // the wheel in the middle, the action library on the right. A narrow window stacks
        // them in one column. Only widths and positions change on resize - the definitions
        // are created once and never cleared, which is what used to crash XAML mid-layout.
        var workspace = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        for (var index = 0; index < 3; index++)
        {
            workspace.ColumnDefinitions.Add(new ColumnDefinition());
            workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        _radialMenuListHost = new Grid();
        _radialWheelHost = new Grid();
        _radialLibraryHost = new Grid();
        _radialListCard = NewCard(_radialMenuListHost, new Thickness(14));
        _radialWheelCard = NewCard(_radialWheelHost, new Thickness(14));
        _radialLibraryCard = NewCard(_radialLibraryHost, new Thickness(14));
        workspace.Children.Add(_radialListCard);
        workspace.Children.Add(_radialWheelCard);
        workspace.Children.Add(_radialLibraryCard);

        _radialWorkspace = workspace;
        _radialSwitches = switches;
        _radialLayoutMode = -1;
        _radialLayoutQueued = false;
        ApplyRadialPageLayout(double.PositiveInfinity);
        // The width is only known once the page is measured; rewriting the layout while the
        // grid is laying out itself crashes XAML, so the re-layout is queued.
        workspace.SizeChanged += (_, e) => QueueRadialPageLayout(e.NewSize.Width);
        root.Loaded += (_, _) => QueueRadialPageLayout(workspace.ActualWidth);

        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(switches);
        content.Children.Add(workspace);
        root.Children.Add(content);

        RefreshRadialPage();
        return root;
    }

    private int _radialLayoutMode = -1;
    private Grid? _radialWorkspace;
    private Border? _radialListCard;
    private Border? _radialWheelCard;
    private Border? _radialLibraryCard;
    private StackPanel? _radialSwitches;
    private double _radialPendingWidth;
    private bool _radialLayoutQueued;

    /// <summary>
    /// Re-applies the columns for <paramref name="width"/> after the current layout pass.
    /// XAML fails fast (0xc000027b in Microsoft.UI.Xaml.dll) when SizeChanged rewrites the
    /// definitions of the grid that raised it, which is what made the page vanish at random.
    /// </summary>
    private void QueueRadialPageLayout(double width)
    {
        if (double.IsNaN(width) || width <= 0)
            return;
        _radialPendingWidth = width;
        if (_radialLayoutQueued)
            return;
        _radialLayoutQueued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _radialLayoutQueued = false;
            if (_radialWorkspace is null || _radialSwitches is null)
                return;
            ApplyRadialPageLayout(_radialPendingWidth);
        });
    }

    private void ApplyRadialPageLayout(double width)
    {
        var workspace = _radialWorkspace;
        if (workspace is null || double.IsNaN(width) || width <= 0)
            return;
        _radialSwitches!.Orientation = width >= 420 ? Orientation.Horizontal : Orientation.Vertical;
        // Wide: applications | wheel | library, in the same proportions as the Quick Actions
        // editor. Narrow: one column, library last.
        var wide = width >= 810;
        if (_radialLayoutMode == (wide ? 1 : 0))
            return;
        if (_radialListCard is null || _radialWheelCard is null || _radialLibraryCard is null)
            return;
        _radialLayoutMode = wide ? 1 : 0;
        workspace.ColumnDefinitions[0].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        workspace.ColumnDefinitions[1].Width = new GridLength(wide ? 2.25 : 1, GridUnitType.Star);
        workspace.ColumnDefinitions[2].Width = wide ? new GridLength(1.5, GridUnitType.Star) : new GridLength(0);
        Place(_radialListCard, 0, wide ? 0 : 1);
        Place(_radialWheelCard, wide ? 0 : 1, 1);
        Place(_radialLibraryCard, wide ? 0 : 2, wide ? 2 : 1);

        static void Place(FrameworkElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, 1);
        }
    }

    /// <summary>Compact icon button for the wheel toolbar; the caption lives in a tooltip.</summary>
    private Button NewRadialToolButton(Symbol symbol, string label)
    {
        var button = new Button
        {
            Content = new SymbolIcon(symbol),
            Width = 36,
            Height = 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        return button;
    }

    /// <summary>Enable / outer ring / third ring as one compact row (a column when very narrow).</summary>
    private StackPanel NewRadialMenuSwitchesBar()
    {
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
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(enabledSwitch, L("环形菜单", "Radial menu", "環形選單", "원형 메뉴"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(outerSwitch, L("外圈", "Outer ring", "外圈", "바깥쪽 링"));
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 28 };
        void Pair(string label, ToggleSwitch toggle)
        {
            // No On/Off text: the label sits beside the switch instead.
            toggle.OnContent = string.Empty;
            toggle.OffContent = string.Empty;
            toggle.MinWidth = 0;
            var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            pair.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            pair.Children.Add(toggle);
            bar.Children.Add(pair);
        }
        Pair(L("启用", "Enable", "啟用", "사용"), enabledSwitch);
        Pair(L("外圈", "Outer ring", "外圈", "바깥쪽 링"), outerSwitch);
        Pair(T("第三圈", "Third ring"), thirdSwitch);
        return bar;
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

    /// <summary>Submenus opened in the wheel editor, from the selected top-level menu down.</summary>
    private readonly List<string> _radialSubmenuPath = new();

    /// <summary>The menu selected in the list on the left (never a submenu).</summary>
    private RadialMenuDefinition TopRadialMenu()
    {
        var config = _radialConfig!;
        return config.Menus.FirstOrDefault(m => m.Id == _radialSelectedMenuId && m.Scope != RadialMenuScope.Submenu) ?? config.GlobalMenu;
    }

    /// <summary>The menu shown in the wheel editor: the open submenu, or the top-level menu.</summary>
    private RadialMenuDefinition SelectedRadialMenu()
    {
        var config = _radialConfig!;
        while (_radialSubmenuPath.Count > 0)
        {
            var submenu = config.FindMenuById(_radialSubmenuPath[^1]);
            if (submenu is { Scope: RadialMenuScope.Submenu })
                return submenu;
            _radialSubmenuPath.RemoveAt(_radialSubmenuPath.Count - 1);
        }
        return TopRadialMenu();
    }

    /// <summary>Removes submenus that no slot refers to any more (repeated for nested ones).</summary>
    private static void PruneOrphanSubmenus(RadialMenuConfig config)
    {
        while (true)
        {
            var referenced = config.Menus
                .SelectMany(m => m.Slots.Values)
                .Select(value => RadialMenuConfig.TryGetSubmenuId(value, out var id) ? id : null)
                .Where(id => id is not null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var orphans = config.Menus.Where(m => m.Scope == RadialMenuScope.Submenu && !referenced.Contains(m.Id)).ToList();
            if (orphans.Count == 0)
                return;
            foreach (var orphan in orphans)
                config.Menus.Remove(orphan);
        }
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
        RadialMenuScope.Submenu => string.IsNullOrWhiteSpace(menu.Name) ? T("子菜单", "Submenu") : menu.Name,
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
            .Where(m => m.Scope != RadialMenuScope.Submenu)
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
            if (menu == TopRadialMenu())
                list.SelectedItem = row;
        }
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is FrameworkElement { Tag: string id } && id != _radialSelectedMenuId)
            {
                _radialSelectedMenuId = id;
                _radialSubmenuPath.Clear();
                RefreshRadialWheel();
            }
        };

        var add = NewPillButton(L("添加", "Add", "新增", "추가"), false);
        add.Click += async (_, _) => await RunUiActionAsync(AddRadialApplicationMenuAsync);
        var delete = NewPillButton(L("删除", "Delete", "刪除", "삭제"), false);
        delete.Click += async (_, _) => await RunUiActionAsync(DeleteSelectedRadialMenuAsync);
        var buttons = new Grid { ColumnSpacing = 8 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        add.HorizontalAlignment = HorizontalAlignment.Stretch;
        delete.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (add.Content is string caption)
            add.Content = "+  " + caption;
        buttons.Children.Add(add);
        Grid.SetColumn(delete, 1);
        buttons.Children.Add(delete);

        // Title, list and buttons share the card like the Quick Actions menu list: the list
        // takes the leftover height and scrolls inside it, the buttons stay at the bottom.
        var panel = new Grid { RowSpacing = 8 };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock { Text = L("程序", "Applications", "程式", "프로그램"), Style = BodyStrongTextBlockStyle };
        Grid.SetRow(title, 0);
        panel.Children.Add(title);
        Grid.SetRow(list, 1);
        panel.Children.Add(list);
        Grid.SetRow(buttons, 2);
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
        var menu = TopRadialMenu();
        if (menu.Scope != RadialMenuScope.Application)
            return;
        if (!await ConfirmDialogAsync(DeleteConfirmationTitle(), RadialMenuTitle(menu), DeleteButtonText()))
            return;
        _radialConfig!.Menus.Remove(menu);
        PruneOrphanSubmenus(_radialConfig);
        _radialSelectedMenuId = null;
        _radialSubmenuPath.Clear();
        SaveRadialConfig();
        RefreshRadialPage();
    }
}
