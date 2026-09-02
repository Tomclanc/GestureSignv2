using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private enum TouchPadEdgeMarker { None, Horizontal, Vertical }
    private sealed record TouchPadEdgeZone(string Title, TouchPadEdgeMarker Marker, IReadOnlyList<TouchPadEdgeAction> Actions);
    private sealed record TouchPadEdgeAction(string Title, string GestureName);

    private UIElement BuildTouchPadPageCore()
    {
        var root = NewSection();
        root.Children.Add(NewSettingsGroup(L("边缘识别", "Edge Recognition", "邊緣辨識", "エッジ認識", "가장자리 인식"),
        [
            NewToggleRow(L("启用触控板手势", "Enable touchpad gestures", "啟用觸控板手勢", "タッチパッドジェスチャを有効にする", "터치패드 제스처 사용"), _legacyData.Options.RegisterTouchPad, "RegisterTouchPad"),
            NewToggleRow(L("优先使用 Windows 触控板系统手势", "Prefer Windows touchpad gestures", "優先使用 Windows 觸控板系統手勢", "Windows のタッチパッドシステムジェスチャを優先", "Windows 터치패드 시스템 제스처 우선 사용"), _legacyData.Options.PreferWindowsTouchPadGestures, "PreferWindowsTouchPadGestures")
        ]));

        root.Children.Add(NewTouchPadMapCard());
        root.Children.Add(NewTouchScreenMapCard());
        return root;
    }

    private FrameworkElement NewTouchPadMapCard()
    {
        var panel = NewCardPanel(14);
        panel.Children.Add(new TextBlock
        {
            Text = L("触控板边缘", "Touchpad Edges", "觸控板邊緣", "タッチパッドのエッジ", "터치패드 가장자리"),
            Style = BodyStrongTextBlockStyle
        });

        var map = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 12,
            MinHeight = 700
        };
        map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
        map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        map.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) });
        map.RowDefinitions.Add(new RowDefinition { Height = new GridLength(270) });
        map.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) });

        var edges = TouchPadEdges();
        var top = NewTouchPadZone(edges[0]);
        var bottom = NewTouchPadZone(edges[1]);
        var left = NewTouchPadZone(edges[2]);
        var right = NewTouchPadZone(edges[3]);

        Grid.SetColumn(top, 1);
        map.Children.Add(top);

        Grid.SetRow(left, 1);
        map.Children.Add(left);

        var center = NewTouchPadCenter();
        Grid.SetColumn(center, 1);
        Grid.SetRow(center, 1);
        map.Children.Add(center);

        Grid.SetColumn(right, 2);
        Grid.SetRow(right, 1);
        map.Children.Add(right);

        Grid.SetColumn(bottom, 1);
        Grid.SetRow(bottom, 2);
        map.Children.Add(bottom);

        var cornerCells = new[]
        {
            (Column: 0, Row: 0),
            (Column: 2, Row: 0),
            (Column: 0, Row: 2),
            (Column: 2, Row: 2)
        };
        foreach (var cell in cornerCells)
        {
            var corner = NewTouchPadMapFiller();
            Grid.SetColumn(corner, cell.Column);
            Grid.SetRow(corner, cell.Row);
            map.Children.Add(corner);
        }

        panel.Children.Add(new Border
        {
            Background = TouchPadSurfaceBrush(),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = map
        });

        return NewCard(panel, new Thickness(14));
    }

    private FrameworkElement NewTouchScreenMapCard()
    {
        var panel = NewCardPanel(14);
        panel.Children.Add(new TextBlock
        {
            Text = L("触摸屏边缘", "Touchscreen Edges", "觸控螢幕邊緣", "タッチスクリーンのエッジ", "터치스크린 가장자리"),
            Style = BodyStrongTextBlockStyle
        });

        var map = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 12,
            MinHeight = 700
        };
        map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
        map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        map.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) });
        map.RowDefinitions.Add(new RowDefinition { Height = new GridLength(270) });
        map.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) });

        var edges = TouchScreenEdges();
        var top = NewTouchPadZone(edges[0]);
        var bottom = NewTouchPadZone(edges[1]);
        var left = NewTouchPadZone(edges[2]);
        var right = NewTouchPadZone(edges[3]);

        Grid.SetColumn(top, 1);
        map.Children.Add(top);

        Grid.SetRow(left, 1);
        map.Children.Add(left);

        var center = NewTouchScreenCenter();
        Grid.SetColumn(center, 1);
        Grid.SetRow(center, 1);
        map.Children.Add(center);

        Grid.SetColumn(right, 2);
        Grid.SetRow(right, 1);
        map.Children.Add(right);

        Grid.SetColumn(bottom, 1);
        Grid.SetRow(bottom, 2);
        map.Children.Add(bottom);

        var cornerCells = new[]
        {
            (Column: 0, Row: 0),
            (Column: 2, Row: 0),
            (Column: 0, Row: 2),
            (Column: 2, Row: 2)
        };
        foreach (var cell in cornerCells)
        {
            var corner = NewTouchPadMapFiller();
            Grid.SetColumn(corner, cell.Column);
            Grid.SetRow(corner, cell.Row);
            map.Children.Add(corner);
        }

        panel.Children.Add(new Border
        {
            Background = TouchPadSurfaceBrush(),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = map
        });

        return NewCard(panel, new Thickness(14));
    }

    private FrameworkElement NewTouchPadZone(TouchPadEdgeZone zone)
    {
        var isHorizontalZone = zone.Marker == TouchPadEdgeMarker.Horizontal;
        var orderedActions = OrderedTouchPadEdgeActions(zone, isHorizontalZone);
        var content = NewCardPanel(8);
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(new TextBlock
        {
            Text = zone.Title,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Style = BodyStrongTextBlockStyle
        });

        if (isHorizontalZone)
        {
            var actions = new Grid
            {
                ColumnSpacing = 10,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            for (var index = 0; index < orderedActions.Count; index++)
            {
                actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var button = NewTouchPadGestureButton(orderedActions[index]);
                Grid.SetColumn(button, index);
                actions.Children.Add(button);
            }

            content.Children.Add(actions);
        }
        else
        {
            foreach (var item in orderedActions)
                content.Children.Add(NewTouchPadGestureButton(item));
        }

        return new Border
        {
            Background = TouchPadZoneBrush(zone.Actions.Any(item => GetGlobalTouchPadAction(item.GestureName)?.IsEnabled == true)),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = content
        };
    }

    private static IReadOnlyList<TouchPadEdgeAction> OrderedTouchPadEdgeActions(TouchPadEdgeZone zone, bool isHorizontalZone)
    {
        if (zone.Actions.Count != 3)
            return zone.Actions;

        var tap = zone.Actions.FirstOrDefault(action => action.GestureName.Count(ch => ch == '.') == 1);
        if (tap is null)
            return zone.Actions;

        var first = zone.Actions.FirstOrDefault(action =>
            action != tap &&
            action.GestureName.EndsWith(isHorizontalZone ? ".Left" : ".Up", StringComparison.OrdinalIgnoreCase));
        var last = zone.Actions.FirstOrDefault(action =>
            action != tap &&
            action.GestureName.EndsWith(isHorizontalZone ? ".Right" : ".Down", StringComparison.OrdinalIgnoreCase));

        return first is not null && last is not null
            ? new[] { first, tap, last }
            : zone.Actions;
    }

    private Button NewTouchPadGestureButton(TouchPadEdgeAction item)
    {
        var action = GetGlobalTouchPadAction(item.GestureName);
        var command = action?.Commands.FirstOrDefault();
        var title = new TextBlock
        {
            Text = item.Title,
            Style = ResourceStyle("BodyTextBlockStyle"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap
        };
        var summary = new TextBlock
        {
            Text = TouchPadCommandSummary(action, command),
            FontSize = 12,
            Opacity = action?.IsEnabled == false ? 0.5 : 0.68,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2
        };

        var stack = NewCardPanel(2);
        stack.HorizontalAlignment = HorizontalAlignment.Stretch;
        stack.Children.Add(title);
        stack.Children.Add(summary);

        var button = new Button
        {
            Content = stack,
            Background = TouchPadMiniZoneBrush(action),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            MinHeight = 58,
            Padding = new Thickness(8, 5, 8, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        button.Click += async (_, _) => await RunUiActionAsync(() => ConfigureTouchPadEdgeCommandAsync(item.GestureName, item.Title));
        return button;
    }

    private FrameworkElement NewTouchPadCenter()
    {
        var panel = NewCardPanel(8);
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(new FontIcon
        {
            Glyph = "\uE815",
            FontSize = 26,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = L("触控板", "Touchpad", "觸控板", "タッチパッド", "터치패드"),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Style = ResourceStyle("SubtitleTextBlockStyle")
        });

        return new Border
        {
            Background = TouchPadCenterBrush(),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = panel
        };
    }

    private FrameworkElement NewTouchScreenCenter()
    {
        var panel = NewCardPanel(8);
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(new FontIcon
        {
            Glyph = "\uE815",
            FontSize = 26,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = L("触摸屏", "Touchscreen", "觸控螢幕", "タッチスクリーン", "터치스크린"),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Style = ResourceStyle("SubtitleTextBlockStyle")
        });

        return new Border
        {
            Background = TouchPadCenterBrush(),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = panel
        };
    }

    private FrameworkElement NewTouchPadMapFiller()
    {
        return new Border
        {
            Background = TouchPadSurfaceBrush(),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
    }

    private async Task ConfigureTouchPadEdgeCommandAsync(string gestureName, string title)
    {
        var existingAction = GetGlobalTouchPadAction(gestureName);
        var existingCommand = existingAction?.Commands.FirstOrDefault();
        var name = new TextBox
        {
            PlaceholderText = "命令名称",
            Text = existingCommand?.Name ?? "发送快捷键"
        };
        var selectedPluginIndex = existingCommand is null ? 0 : PluginIndex(existingCommand.PluginClass);
        var plugin = new ComboBox
        {
            Margin = new Thickness(0, 8, 0, 0)
        };
        AddPluginItems(plugin);
        // WinUI can coerce SelectedIndex back to -1 when it is assigned before
        // the ComboBox has any items. Populate the list first so the editor and
        // the persisted command always start from the same plugin selection.
        plugin.SelectedIndex = selectedPluginIndex;
        var pluginDescription = NewPluginDescriptionTextBlock();
        var pluginClass = new TextBox
        {
            PlaceholderText = "自定义插件类名",
            Text = existingCommand?.PluginClass ?? PluginClassFromIndex(plugin.SelectedIndex),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var settings = new TextBox
        {
            Text = existingCommand?.Settings ?? "",
            PlaceholderText = "命令设置 JSON，可留空",
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MinHeight = 80
        };
        var hotkey = NewHotKeyRecorderWithClear(settings, settings.Text);
        var appPicker = NewCommandAppPicker(plugin, pluginClass, settings);
        var typedSettings = NewTypedCommandSettingsEditor(pluginClass, settings, enableEdgeContinuousVolume: true);
        var continuousVolumeHint = new TextBlock
        {
            Text = L(
                "四条边缘均支持音量调节：左右边缘上下滑动，上下边缘左右滑动。可选择连续滑条式或每次滑动只触发一次；连续模式建议设置 2%–5%。",
                "All four edges support volume control: slide vertically on the left or right edge, or horizontally on the top or bottom edge. Choose slider-like continuous control or one action per swipe; 2%–5% is recommended for continuous mode.",
                "四條邊緣均支援音量調節：左右邊緣上下滑動，上下邊緣左右滑動。可選擇連續滑桿式或每次滑動只觸發一次；連續模式建議設定 2%–5%。",
                "4 辺すべてで音量を調整できます。左右端では上下、上下端では左右にスライドします。連続スライダー式または 1 スワイプ 1 回を選択でき、連続時は 2%～5% を推奨します。",
                "네 가장자리 모두에서 볼륨을 조절할 수 있습니다. 왼쪽/오른쪽 가장자리에서는 세로로, 위/아래 가장자리에서는 가로로 밉니다. 연속 슬라이더 방식 또는 스와이프당 한 번을 선택할 수 있으며 연속 모드는 2%~5%를 권장합니다."),
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var continuousScrollHint = new TextBlock
        {
            Text = L(
                "滚动映射会按边缘方向自动选择轴：左右边缘上下滑动控制纵向滚动条，上下边缘左右滑动控制横向滚动条。滚动量为 1 格并随移动距离连续触发。",
                "Scroll mapping selects the axis from the edge: vertical sliding on the left or right edge controls vertical scrolling; horizontal sliding on the top or bottom edge controls horizontal scrolling. It repeats in one-notch steps as you move.",
                "捲動映射會依邊緣方向自動選擇軸：左右邊緣上下滑動控制縱向捲軸，上下邊緣左右滑動控制橫向捲軸。每次捲動 1 格並隨移動距離連續觸發。",
                "スクロール軸は端の方向から自動選択されます。左右端の上下スライドは縦スクロール、上下端の左右スライドは横スクロールを操作し、移動距離に応じて 1 ノッチずつ連続実行します。",
                "스크롤 축은 가장자리 방향에 따라 자동 선택됩니다. 왼쪽/오른쪽 가장자리의 세로 밀기는 세로 스크롤을, 위/아래 가장자리의 가로 밀기는 가로 스크롤을 1칸씩 연속 실행합니다."),
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var enabled = new CheckBox
        {
            Content = "启用这个边缘",
            IsChecked = existingAction?.IsEnabled ?? true,
            Margin = new Thickness(0, 8, 0, 0)
        };
        // Keep the user's explicit operation choice independently from the
        // controls. WinUI may update the ComboBox and the hidden class editor
        // at different times while a ContentDialog is closing.
        var selectedPluginClassValue = existingCommand?.PluginClass ?? PluginClassFromIndex(plugin.SelectedIndex);

        string SelectedPluginClass()
        {
            return string.IsNullOrWhiteSpace(selectedPluginClassValue)
                ? pluginClass.Text.Trim()
                : selectedPluginClassValue;
        }

        void UpdateEditor(bool resetSettings)
        {
            var selectedClass = PluginClassFromIndex(plugin.SelectedIndex);
            if (!string.IsNullOrWhiteSpace(selectedClass))
            {
                selectedPluginClassValue = selectedClass;
                pluginClass.Text = selectedClass;
            }
            else
            {
                selectedPluginClassValue = "";
            }

            if (resetSettings)
            {
                var edgeScrollSettings = pluginClass.Text.Contains("MouseActions", StringComparison.OrdinalIgnoreCase)
                    ? EdgeScrollSettingsJson(gestureName)
                    : null;
                settings.Text = edgeScrollSettings
                    ?? (pluginClass.Text.Contains("Volume", StringComparison.OrdinalIgnoreCase)
                        ? VolumeSettingsJson(0, 4, continuousEdge: true)
                        : PluginSettingsTemplate(pluginClass.Text));
                UpdateDefaultCommandName(name, pluginClass.Text);
            }

            UpdateCommandEditorVisibility(pluginClass.Text, pluginClass, hotkey, settings, appPicker);
            UpdatePluginDescription(pluginDescription, pluginClass.Text);
            UpdateTypedCommandSettingsEditor(typedSettings, pluginClass.Text, settings.Text);
            continuousVolumeHint.Visibility = pluginClass.Text.Contains("Volume", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
            continuousScrollHint.Visibility = pluginClass.Text.Contains("MouseActions", StringComparison.OrdinalIgnoreCase) &&
                                              EdgeScrollSettingsJson(gestureName) is not null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        plugin.SelectionChanged += (_, _) => UpdateEditor(true);

        var panel = NewCardPanel(8);
        panel.Children.Add(name);
        panel.Children.Add(plugin);
        panel.Children.Add(pluginDescription);
        panel.Children.Add(pluginClass);
        panel.Children.Add(hotkey);
        panel.Children.Add(appPicker);
        panel.Children.Add(typedSettings);
        panel.Children.Add(continuousVolumeHint);
        panel.Children.Add(continuousScrollHint);
        panel.Children.Add(settings);
        panel.Children.Add(enabled);
        UpdateEditor(false);

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = $"编辑{title}",
            Content = NewDialogScrollContent(panel),
            PrimaryButtonText = "保存",
            SecondaryButtonText = existingAction is null ? "" : "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            await DeleteTouchPadEdgeAsync(gestureName, title, confirm: false);
            return;
        }

        if (result != ContentDialogResult.Primary)
            return;

        // SelectionChanged is not guaranteed to finish propagating before a
        // ContentDialog's primary result is delivered. Re-read the visible app
        // picker and materialize its target into the command at commit time.
        CommitSelectedAppCommandChoice(appPicker, plugin, pluginClass, settings);

        // Persist the visible ComboBox selection, not the hidden editor field.
        // This prevents a stale HotKey class/settings pair from being written
        // after the user changes the operation type.
        var pluginClassValue = SelectedPluginClass();
        if (string.IsNullOrWhiteSpace(pluginClassValue))
        {
            await ShowInfoDialog("插件类名为空", "请选择一个操作，或填写自定义插件类名。");
            return;
        }

        var globalApp = EnsureGlobalApplication();
        var action = globalApp.Actions.FirstOrDefault(item => string.Equals(item.GestureName, gestureName, StringComparison.OrdinalIgnoreCase));
        if (action is null)
        {
            _legacyData.AddAction(globalApp, title, gestureName);
            _legacyData = LegacyDataStore.Load();
            globalApp = EnsureGlobalApplication();
            action = globalApp.Actions.FirstOrDefault(item => string.Equals(item.GestureName, gestureName, StringComparison.OrdinalIgnoreCase));
        }

        if (action is null)
        {
            await ShowInfoDialog("保存失败", "没有找到可写入的全局动作。");
            return;
        }

        var command = action.Commands.FirstOrDefault();
        if (command is null)
            _legacyData.AddCommand(action, name.Text, pluginClassValue, settings.Text);
        else
            _legacyData.UpdateCommand(command, name.Text, pluginClassValue, settings.Text, true);

        _legacyData = LegacyDataStore.Load();
        action = GetGlobalTouchPadAction(gestureName);
        if (action is not null)
            _legacyData.SetEnabled(action.Source, enabled.IsChecked ?? true);

        ReloadActionDataOnly();
        await NotifyDaemonAsync(DaemonCommand.LoadApplications);
    }

    private async Task ToggleTouchPadEdgeAsync(string gestureName)
    {
        var action = GetGlobalTouchPadAction(gestureName);
        if (action is null)
            return;

        _legacyData.SetEnabled(action.Source, !action.IsEnabled);
        ReloadActionDataOnly();
        await NotifyDaemonAsync(DaemonCommand.LoadApplications);
    }

    private async Task DeleteTouchPadEdgeAsync(string gestureName, string title, bool confirm = true)
    {
        var globalApp = GetGlobalApplication();
        var action = globalApp?.Actions.FirstOrDefault(item => string.Equals(item.GestureName, gestureName, StringComparison.OrdinalIgnoreCase));
        if (globalApp is null || action is null)
            return;

        if (confirm && !await ConfirmDialogAsync(
                L("清空边缘动作", "Clear edge action", "清空邊緣動作", "エッジアクションをクリア", "가장자리 동작 지우기"),
                string.Format(
                    CultureInfo.CurrentCulture,
                    L("确定清空 {0}？", "Clear {0}?", "確定清空 {0}？", "{0}をクリアしますか？", "{0} 항목을 지우시겠습니까?"),
                    title),
                L("清空", "Clear", "清空", "クリア", "지우기")))
            return;

        _legacyData.DeleteAction(globalApp, action);
        ReloadActionDataOnly();
        await NotifyDaemonAsync(DaemonCommand.LoadApplications);
    }

    private LegacyApplication EnsureGlobalApplication()
    {
        var globalApp = GetGlobalApplication();
        if (globalApp is not null)
            return globalApp;

        _legacyData.EnsureGlobalApplication();
        _legacyData = LegacyDataStore.Load();
        return GetGlobalApplication()
            ?? throw new InvalidOperationException("无法创建全局动作配置。");
    }

    private LegacyApplication? GetGlobalApplication()
        => _legacyData.Applications.FirstOrDefault(app => app.Type == "全局");

    private LegacyAction? GetGlobalTouchPadAction(string gestureName)
        => GetGlobalApplication()?.Actions.FirstOrDefault(action => string.Equals(action.GestureName, gestureName, StringComparison.OrdinalIgnoreCase));

    private string TouchPadCommandSummary(LegacyAction? action, LegacyCommand? command)
    {
        if (action is null)
            return L("未设置", "Not set", "未設定", "未設定", "설정 안 됨");

        if (command is null)
            return action.IsEnabled
                ? L("未设置命令", "No command", "未設定命令", "コマンド未設定", "명령 없음")
                : L("已停用", "Disabled", "已停用", "無効", "사용 안 함");

        var hotKey = HotKeyDisplayText(command.Settings);
        if (!string.IsNullOrWhiteSpace(hotKey))
            return action.IsEnabled ? hotKey : $"{hotKey} · {L("已停用", "Disabled", "已停用", "無効", "사용 안 함")}";

        if (!action.IsEnabled)
            return $"{PluginName(command.PluginClass)} · {L("已停用", "Disabled", "已停用", "無効", "사용 안 함")}";

        return $"{PluginName(command.PluginClass)} · {(command.IsEnabled ? L("启用", "Enabled", "啟用", "有効", "사용") : L("停用", "Disabled", "停用", "無効", "사용 안 함"))}";
    }

    private SolidColorBrush TouchPadSurfaceBrush()
    {
        return IsDark
            ? new SolidColorBrush(Color.FromArgb(255, 61, 64, 67))
            : new SolidColorBrush(Color.FromArgb(255, 224, 234, 242));
    }

    private SolidColorBrush TouchPadCenterBrush()
    {
        return IsDark
            ? new SolidColorBrush(Color.FromArgb(255, 72, 76, 79))
            : new SolidColorBrush(Color.FromArgb(255, 238, 244, 249));
    }

    private SolidColorBrush TouchPadZoneBrush(bool hasEnabledAction)
    {
        if (hasEnabledAction)
        {
            return IsDark
                ? new SolidColorBrush(Color.FromArgb(255, 38, 61, 80))
                : new SolidColorBrush(Color.FromArgb(255, 216, 235, 250));
        }

        return IsDark
            ? new SolidColorBrush(Color.FromArgb(255, 39, 40, 42))
            : new SolidColorBrush(Color.FromArgb(255, 246, 249, 252));
    }

    private SolidColorBrush TouchPadMiniZoneBrush(LegacyAction? action)
    {
        if (action?.IsEnabled == true)
        {
            return IsDark
                ? new SolidColorBrush(Color.FromArgb(255, 52, 77, 98))
                : new SolidColorBrush(Color.FromArgb(255, 226, 241, 252));
        }

        return SubtleBrush();
    }

    private IReadOnlyList<TouchPadEdgeZone> TouchPadEdges()
        =>
        [
            new(L("上边缘", "Top Edge", "上邊緣", "上エッジ", "위쪽 가장자리"), TouchPadEdgeMarker.Horizontal,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchPadEdgeTopGesture),
                new(L("左滑", "Swipe Left", "左滑", "左へスワイプ", "왼쪽으로 스와이프"), TouchPadEdgeTopLeftGesture),
                new(L("右滑", "Swipe Right", "右滑", "右へスワイプ", "오른쪽으로 스와이프"), TouchPadEdgeTopRightGesture)
            ]),
            new(L("下边缘", "Bottom Edge", "下邊緣", "下エッジ", "아래쪽 가장자리"), TouchPadEdgeMarker.Horizontal,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchPadEdgeBottomGesture),
                new(L("左滑", "Swipe Left", "左滑", "左へスワイプ", "왼쪽으로 스와이프"), TouchPadEdgeBottomLeftGesture),
                new(L("右滑", "Swipe Right", "右滑", "右へスワイプ", "오른쪽으로 스와이프"), TouchPadEdgeBottomRightGesture)
            ]),
            new(L("左边缘", "Left Edge", "左邊緣", "左エッジ", "왼쪽 가장자리"), TouchPadEdgeMarker.None,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchPadEdgeLeftGesture),
                new(L("上滑", "Swipe Up", "上滑", "上へスワイプ", "위로 스와이프"), TouchPadEdgeLeftUpGesture),
                new(L("下滑", "Swipe Down", "下滑", "下へスワイプ", "아래로 스와이프"), TouchPadEdgeLeftDownGesture)
            ]),
            new(L("右边缘", "Right Edge", "右邊緣", "右エッジ", "오른쪽 가장자리"), TouchPadEdgeMarker.None,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchPadEdgeRightGesture),
                new(L("上滑", "Swipe Up", "上滑", "上へスワイプ", "위로 스와이프"), TouchPadEdgeRightUpGesture),
                new(L("下滑", "Swipe Down", "下滑", "下へスワイプ", "아래로 스와이프"), TouchPadEdgeRightDownGesture)
            ])
        ];

    private IReadOnlyList<TouchPadEdgeZone> TouchScreenEdges()
        =>
        [
            new(L("上边缘", "Top Edge", "上邊緣", "上エッジ", "위쪽 가장자리"), TouchPadEdgeMarker.Horizontal,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchScreenEdgeTopGesture),
                new(L("左滑", "Swipe Left", "左滑", "左へスワイプ", "왼쪽으로 스와이프"), TouchScreenEdgeTopLeftGesture),
                new(L("右滑", "Swipe Right", "右滑", "右へスワイプ", "오른쪽으로 스와이프"), TouchScreenEdgeTopRightGesture)
            ]),
            new(L("下边缘", "Bottom Edge", "下邊緣", "下エッジ", "아래쪽 가장자리"), TouchPadEdgeMarker.Horizontal,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchScreenEdgeBottomGesture),
                new(L("左滑", "Swipe Left", "左滑", "左へスワイプ", "왼쪽으로 스와이프"), TouchScreenEdgeBottomLeftGesture),
                new(L("右滑", "Swipe Right", "右滑", "右へスワイプ", "오른쪽으로 스와이프"), TouchScreenEdgeBottomRightGesture)
            ]),
            new(L("左边缘", "Left Edge", "左邊緣", "左エッジ", "왼쪽 가장자리"), TouchPadEdgeMarker.None,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchScreenEdgeLeftGesture),
                new(L("上滑", "Swipe Up", "上滑", "上へスワイプ", "위로 스와이프"), TouchScreenEdgeLeftUpGesture),
                new(L("下滑", "Swipe Down", "下滑", "下へスワイプ", "아래로 스와이프"), TouchScreenEdgeLeftDownGesture)
            ]),
            new(L("右边缘", "Right Edge", "右邊緣", "右エッジ", "오른쪽 가장자리"), TouchPadEdgeMarker.None,
            [
                new(L("点击", "Tap", "點擊", "タップ", "탭"), TouchScreenEdgeRightGesture),
                new(L("上滑", "Swipe Up", "上滑", "上へスワイプ", "위로 스와이프"), TouchScreenEdgeRightUpGesture),
                new(L("下滑", "Swipe Down", "下滑", "下へスワイプ", "아래로 스와이프"), TouchScreenEdgeRightDownGesture)
            ])
        ];

    private FrameworkElement NewTouchPadEdgeMarker(TouchPadEdgeMarker marker)
    {
        var isHorizontal = marker == TouchPadEdgeMarker.Horizontal;
        return new Border
        {
            Width = isHorizontal ? 34 : 4,
            Height = isHorizontal ? 4 : 34,
            CornerRadius = new CornerRadius(2),
            Background = IsDark
                ? new SolidColorBrush(Color.FromArgb(210, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(210, 24, 32, 38)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        };
    }

}

