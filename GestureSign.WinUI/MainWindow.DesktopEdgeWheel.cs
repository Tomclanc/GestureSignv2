using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace GestureSign.WinUI;

/// <summary>
/// "Desktop edge mouse wheel": a screen diagram beside a 4 x 2 table (edge x scroll
/// direction), a clear button per cell and presets that fill a whole edge.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string[] DesktopEdges = { "Left", "Right", "Top", "Bottom" };
    private static readonly string[] DesktopWheelDirections = { "WheelUp", "WheelDown" };
    private string _desktopEdgeSelected = "Bottom";

    private static string DesktopEdgeGesture(string edge, string direction) => "DesktopEdge." + edge + "." + direction;

    private string DesktopEdgeTitle(string edge) => edge switch
    {
        "Left" => T("左边缘", "Left edge"),
        "Right" => T("右边缘", "Right edge"),
        "Top" => T("上边缘", "Top edge"),
        _ => T("下边缘", "Bottom edge")
    };

    private string DesktopWheelTitle(string direction)
        => direction == "WheelUp" ? T("向上滚动", "Scroll up") : T("向下滚动", "Scroll down");

    private bool DesktopEdgeHasAction(string edge)
        => DesktopWheelDirections.Any(direction => GetGlobalTouchPadAction(DesktopEdgeGesture(edge, direction)) is not null);

    /// <summary>What a cell shows: the command's own name, else its hotkey, else the generic summary.</summary>
    private string DesktopEdgeCellText(LegacyAction action)
    {
        var command = action.Commands.FirstOrDefault();
        if (command is not null && !string.IsNullOrWhiteSpace(command.Name))
            return action.IsEnabled ? command.Name : command.Name + " · " + L("已停用", "Disabled", "已停用", "사용 안 함");
        return TouchPadCommandSummary(action, command);
    }

    private FrameworkElement NewDesktopEdgeWheelCard()
    {
        var panel = NewCardPanel(14);
        var accent = ThemeBrush("AccentFillColorDefaultBrush") ?? new SolidColorBrush(Color.FromArgb(255, 76, 194, 255));
        var muted = IsDark ? new SolidColorBrush(Color.FromArgb(255, 91, 97, 112)) : new SolidColorBrush(Color.FromArgb(255, 176, 184, 196));
        var selectedRow = IsDark ? new SolidColorBrush(Color.FromArgb(255, 52, 58, 72)) : new SolidColorBrush(Color.FromArgb(255, 229, 236, 245));

        var setCount = DesktopEdges.Sum(edge => DesktopWheelDirections.Count(direction => GetGlobalTouchPadAction(DesktopEdgeGesture(edge, direction)) is not null));
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 4 };
        titles.Children.Add(new TextBlock { Text = T("桌面边缘鼠标滚轮", "Desktop edge mouse wheel"), Style = BodyStrongTextBlockStyle });
        titles.Children.Add(new TextBlock
        {
            Text = T("指针停在显示器外侧边缘时滚动。未配置时保持原有滚动；相邻显示器内部边界不触发。", "Scroll at an outer display edge. Unassigned actions keep normal scrolling; internal monitor boundaries are excluded."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            FontSize = 13
        });
        header.Children.Add(titles);
        var count = new Border
        {
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, 10, 3),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = F("已设置 {0} / {1}", "Set: {0} of {1}", setCount, 8), FontSize = 12, Opacity = 0.8 }
        };
        Grid.SetColumn(count, 1);
        header.Children.Add(count);
        panel.Children.Add(header);

        // Rows of the table, kept so a click on the diagram can highlight one.
        var rows = new Dictionary<string, Grid>(StringComparer.Ordinal);
        void SelectEdge(string edge)
        {
            _desktopEdgeSelected = edge;
            foreach (var pair in rows)
                pair.Value.Background = pair.Key == edge ? selectedRow : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        // ----- diagram: a screen with four clickable edges ----------------
        var diagram = new Canvas { Width = 240, Height = 178, HorizontalAlignment = HorizontalAlignment.Center };
        diagram.Children.Add(Placed(new Border
        {
            Width = 200, Height = 124,
            BorderBrush = BorderBrush(), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Background = IsDark ? new SolidColorBrush(Color.FromArgb(255, 27, 31, 42)) : new SolidColorBrush(Color.FromArgb(255, 245, 247, 250)),
            IsHitTestVisible = false
        }, 20, 20));
        diagram.Children.Add(Placed(new Rectangle { Width = 24, Height = 10, Fill = muted, IsHitTestVisible = false }, 108, 160));
        diagram.Children.Add(Placed(new Rectangle { Width = 68, Height = 5, RadiusX = 2.5, RadiusY = 2.5, Fill = muted, IsHitTestVisible = false }, 86, 170));
        void EdgeBar(string edge, double left, double top, double width, double height)
        {
            var bar = new Button
            {
                Width = width, Height = height, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0,
                CornerRadius = new CornerRadius(5),
                Background = DesktopEdgeHasAction(edge) ? accent : muted,
                BorderThickness = new Thickness(edge == _desktopEdgeSelected ? 2 : 0),
                BorderBrush = ThemeBrush("TextFillColorPrimaryBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.White)
            };
            ToolTipService.SetToolTip(bar, DesktopEdgeTitle(edge));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bar, DesktopEdgeTitle(edge));
            bar.Click += (_, _) =>
            {
                SelectEdge(edge);
                foreach (var child in diagram.Children.OfType<Button>())
                    child.BorderThickness = new Thickness(ReferenceEquals(child, bar) ? 2 : 0);
            };
            diagram.Children.Add(Placed(bar, left, top));
        }
        EdgeBar("Top", 34, 4, 172, 10);
        EdgeBar("Bottom", 34, 148, 172, 10);
        EdgeBar("Left", 4, 34, 10, 96);
        EdgeBar("Right", 226, 34, 10, 96);

        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center };
        legend.Children.Add(LegendItem(accent, T("已设置", "Set")));
        legend.Children.Add(LegendItem(muted, L("未设置", "Not set", "未設定", "설정 안 됨")));
        var diagramPanel = new StackPanel { Spacing = 8 };
        diagramPanel.Children.Add(diagram);
        diagramPanel.Children.Add(legend);

        // ----- table: one row per edge, one column per direction -----------
        var table = new StackPanel { Spacing = 4 };
        var head = NewDesktopEdgeRow();
        for (var i = 0; i < DesktopWheelDirections.Length; i++)
        {
            var label = new TextBlock { Text = DesktopWheelTitle(DesktopWheelDirections[i]), FontSize = 12, Opacity = 0.72 };
            Grid.SetColumn(label, i + 1);
            head.Children.Add(label);
        }
        table.Children.Add(head);
        foreach (var edge in DesktopEdges)
        {
            var row = NewDesktopEdgeRow();
            row.Padding = new Thickness(8, 5, 8, 5);
            row.CornerRadius = new CornerRadius(8);
            row.Background = edge == _desktopEdgeSelected ? selectedRow : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            row.Children.Add(new TextBlock { Text = DesktopEdgeTitle(edge), VerticalAlignment = VerticalAlignment.Center });
            for (var i = 0; i < DesktopWheelDirections.Length; i++)
            {
                var cell = NewDesktopEdgeCell(edge, DesktopWheelDirections[i]);
                Grid.SetColumn(cell, i + 1);
                row.Children.Add(cell);
            }
            rows[edge] = row;
            table.Children.Add(row);
        }

        // Wide: diagram beside the table. Narrow: diagram above it.
        var body = new Grid { ColumnSpacing = 28, RowSpacing = 16 };
        body.Children.Add(diagramPanel);
        body.Children.Add(table);
        bool? wide = null;
        void Arrange(double width)
        {
            var nextWide = width >= 640;
            if (wide == nextWide) return;
            wide = nextWide;
            body.ColumnDefinitions.Clear();
            body.RowDefinitions.Clear();
            if (nextWide)
            {
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            else
            {
                body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            Grid.SetColumn(table, nextWide ? 1 : 0);
            Grid.SetRow(table, nextWide ? 0 : 1);
        }
        body.SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        Arrange(double.PositiveInfinity);
        panel.Children.Add(body);

        // ----- presets ------------------------------------------------------
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        presets.Children.Add(new TextBlock { Text = T("常用预设", "Presets"), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.72, FontSize = 13 });
        const string volume = "GestureSign.CorePlugins.Volume.VolumePlugin";
        const string brightness = "GestureSign.CorePlugins.ScreenBrightness.ScreenBrightnessPlugin";
        const string hotkey = "GestureSign.CorePlugins.HotKey.HotKeyPlugin";
        static string Keys(bool win, bool ctrl, int key)
            => "{\"Windows\":" + (win ? "true" : "false") + ",\"Control\":" + (ctrl ? "true" : "false") + ",\"Shift\":false,\"Alt\":false,\"KeyCode\":[" + key + "],\"SendByKeybdEvent\":false}";
        void Preset(string label, string edge, (string Name, string Plugin, string Settings) up, (string Name, string Plugin, string Settings) down)
        {
            var button = new Button { Content = label, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 4, 12, 5) };
            button.Click += async (_, _) => await RunUiActionAsync(() => ApplyDesktopEdgePresetAsync(edge, label, up, down));
            presets.Children.Add(button);
        }
        Preset(T("下边缘：调音量", "Bottom edge: volume"), "Bottom",
            (T("音量 +", "Volume up"), volume, VolumeSettingsJson(0, 4)), (T("音量 -", "Volume down"), volume, VolumeSettingsJson(1, 4)));
        Preset(T("上边缘：调亮度", "Top edge: brightness"), "Top",
            (T("亮度 +", "Brightness up"), brightness, BrightnessSettingsJson(0, 5)), (T("亮度 -", "Brightness down"), brightness, BrightnessSettingsJson(1, 5)));
        Preset(T("左边缘：切换虚拟桌面", "Left edge: virtual desktops"), "Left",
            (T("上一个虚拟桌面", "Previous desktop"), hotkey, Keys(true, true, 37)), (T("下一个虚拟桌面", "Next desktop"), hotkey, Keys(true, true, 39)));
        Preset(T("右边缘：切换标签页", "Right edge: tabs"), "Right",
            (T("上一个标签页", "Previous tab"), hotkey, Keys(false, true, 33)), (T("下一个标签页", "Next tab"), hotkey, Keys(false, true, 34)));
        panel.Children.Add(new Border { Height = 1, Background = BorderBrush(), Opacity = 0.7 });
        panel.Children.Add(new ScrollViewer
        {
            Content = presets,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled
        });
        return NewCard(panel);

        static FrameworkElement Placed(FrameworkElement element, double left, double top)
        {
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
            return element;
        }

        static FrameworkElement LegendItem(Brush brush, string text)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            item.Children.Add(new Rectangle { Width = 14, Height = 6, RadiusX = 3, RadiusY = 3, Fill = brush, VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(new TextBlock { Text = text, FontSize = 12, Opacity = 0.72 });
            return item;
        }
    }

    private static Grid NewDesktopEdgeRow()
    {
        var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(8, 0, 8, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return row;
    }

    /// <summary>A set cell: its action name and a clear button. An empty cell: a quiet "Not set" button.</summary>
    private FrameworkElement NewDesktopEdgeCell(string edge, string direction)
    {
        var gestureName = DesktopEdgeGesture(edge, direction);
        var title = DesktopEdgeTitle(edge) + " · " + DesktopWheelTitle(direction);
        var action = GetGlobalTouchPadAction(gestureName);
        var open = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            MinHeight = 40,
            Padding = new Thickness(12, 4, 8, 4),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = BorderBrush()
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open, title);
        open.Click += async (_, _) =>
        {
            _desktopEdgeSelected = edge;
            await RunUiActionAsync(() => ConfigureTouchPadEdgeCommandAsync(gestureName, title));
        };
        if (action is null)
        {
            open.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            open.Content = new TextBlock { Text = L("未设置", "Not set", "未設定", "설정 안 됨"), Opacity = 0.6, FontSize = 13 };
            return open;
        }

        var text = DesktopEdgeCellText(action);
        open.Background = TouchPadMiniZoneBrush(action);
        open.Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        ToolTipService.SetToolTip(open, text);
        var clear = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 12 },
            Width = 32, Height = 32, Padding = new Thickness(0), MinWidth = 0,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        var clearText = L("清除", "Clear", "清除", "지우기");
        ToolTipService.SetToolTip(clear, clearText);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(clear, clearText + " " + title);
        clear.Click += async (_, _) =>
        {
            _desktopEdgeSelected = edge;
            await RunUiActionAsync(() => DeleteTouchPadEdgeAsync(gestureName, title, confirm: false));
        };
        var cell = new Grid();
        cell.Children.Add(open);
        cell.Children.Add(clear);
        clear.HorizontalAlignment = HorizontalAlignment.Right;
        open.Padding = new Thickness(12, 4, 40, 4);
        return cell;
    }

    /// <summary>Fills both directions of one edge; asks first when it would replace existing settings.</summary>
    private async Task ApplyDesktopEdgePresetAsync(string edge, string label,
        (string Name, string Plugin, string Settings) up, (string Name, string Plugin, string Settings) down)
    {
        if (DesktopEdgeHasAction(edge) && !await ConfirmDialogAsync(
                T("常用预设", "Presets"),
                F("{0} 已有设置，要替换吗？", "{0} already has actions. Replace them?", DesktopEdgeTitle(edge)),
                T("替换", "Replace")))
            return;
        _desktopEdgeSelected = edge;
        foreach (var (direction, command) in new[] { ("WheelUp", up), ("WheelDown", down) })
        {
            var gestureName = DesktopEdgeGesture(edge, direction);
            var globalApp = EnsureGlobalApplication();
            var action = globalApp.Actions.FirstOrDefault(item => string.Equals(item.GestureName, gestureName, StringComparison.OrdinalIgnoreCase));
            if (action is null)
            {
                _legacyData.AddAction(globalApp, DesktopEdgeTitle(edge) + " · " + DesktopWheelTitle(direction), gestureName);
                _legacyData = LegacyDataStore.Load();
                action = GetGlobalTouchPadAction(gestureName);
            }
            if (action is null)
                continue;
            var existing = action.Commands.FirstOrDefault();
            if (existing is null)
                _legacyData.AddCommand(action, command.Name, command.Plugin, command.Settings);
            else
                _legacyData.UpdateCommand(existing, command.Name, command.Plugin, command.Settings, true);
            _legacyData = LegacyDataStore.Load();
            action = GetGlobalTouchPadAction(gestureName);
            if (action is not null && !action.IsEnabled)
                _legacyData.SetEnabled(action.Source, true);
        }
        ReloadActionDataOnly();
        await NotifyDaemonAsync(DaemonCommand.LoadApplications);
    }
}
