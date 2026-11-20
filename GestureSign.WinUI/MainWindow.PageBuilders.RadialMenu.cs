using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    // Grid cell (row, column) of each radial slot in a 3x3 overview; slot 0 is
    // up and slots advance clockwise, matching the daemon's on-screen layout.
    private static readonly (int Row, int Column)[] RadialMenuCells =
        [(0, 1), (0, 2), (1, 2), (2, 2), (2, 1), (2, 0), (1, 0), (0, 0)];

    private FrameworkElement NewRadialMenuCard()
    {
        var panel = NewCardPanel(12);
        panel.Children.Add(new TextBlock
        {
            Text = L("中键轮盘", "Middle-button radial menu", "中鍵輪盤", "가운데 버튼 원형 메뉴"),
            Style = BodyStrongTextBlockStyle
        });
        panel.Children.Add(new TextBlock
        {
            Text = L(
                "按住中键拖动弹出轮盘。点击格子进行设置，命令名称会显示在轮盘上；要按程序区分，在程序分组里使用这些触发方式。",
                "Hold the middle button and drag to open the wheel. Click a slot to set it; the command name is shown on the wheel. For per-app wheels, use these triggers in an app group.",
                "按住中鍵拖動彈出輪盤。點擊格子進行設定，命令名稱會顯示在輪盤上；要依程式區分，在程式分組中使用這些觸發方式。",
                "가운데 버튼을 누른 채 끌면 원형 메뉴가 열립니다. 칸을 클릭해 설정하면 명령 이름이 메뉴에 표시됩니다. 앱별 메뉴는 앱 그룹에서 이 트리거를 사용하세요."),
            TextWrapping = TextWrapping.Wrap
        });

        var rings = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        rings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var ring = 0; ring < 2; ring++)
        {
            var grid = NewRadialMenuRingGrid(outer: ring == 1);
            Grid.SetColumn(grid, ring);
            rings.Children.Add(grid);
        }
        panel.Children.Add(rings);
        return NewCard(panel);
    }

    private Grid NewRadialMenuRingGrid(bool outer)
    {
        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        for (var i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var center = new TextBlock
        {
            Text = outer ? L("外圈", "Outer ring", "外圈", "바깥쪽 링") : L("内圈", "Inner ring", "內圈", "안쪽 링"),
            Style = BodyStrongTextBlockStyle,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(center, 1);
        Grid.SetColumn(center, 1);
        grid.Children.Add(center);

        for (var slot = 0; slot < 8; slot++)
        {
            var index = FirstRadialMenuIndex + (outer ? 8 : 0) + slot;
            var button = NewRadialMenuSlotButton(index);
            Grid.SetRow(button, RadialMenuCells[slot].Row);
            Grid.SetColumn(button, RadialMenuCells[slot].Column);
            grid.Children.Add(button);
        }
        return grid;
    }

    private Button NewRadialMenuSlotButton(int index)
    {
        var gestureName = RadialMenuGestureName(index);
        var action = GetGlobalTouchPadAction(gestureName);
        var direction = new TextBlock
        {
            Text = RadialMenuDirectionName(index),
            FontSize = 12,
            Opacity = 0.68,
            TextAlignment = TextAlignment.Center
        };
        var label = new TextBlock
        {
            Text = action is null ? L("未设置", "Not set", "未設定", "설정 안 됨") : action.Name,
            Opacity = action is null || !action.IsEnabled ? 0.5 : 1,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        var stack = NewCardPanel(2);
        stack.Children.Add(direction);
        stack.Children.Add(label);

        var button = new Button
        {
            Content = stack,
            Background = TouchPadMiniZoneBrush(action),
            BorderBrush = BorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            MinHeight = 54,
            Padding = new Thickness(6, 4, 6, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        ToolTipService.SetToolTip(button, RadialMenuDisplayName(index));
        button.Click += async (_, _) => await RunUiActionAsync(() => ConfigureTouchPadEdgeCommandAsync(gestureName, RadialMenuDisplayName(index)));
        return button;
    }
}
