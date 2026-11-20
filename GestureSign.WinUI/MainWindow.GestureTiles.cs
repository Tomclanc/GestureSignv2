using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace GestureSign.WinUI;

/// <summary>
/// Gesture library in grid and tile view: small selectable tiles (shape + name) and one
/// toolbar for retrain, rename and delete instead of three buttons on every card.
/// </summary>
public sealed partial class MainWindow
{
    private string? _selectedGestureName;
    private int _gestureFingerFilter;      // 0 all, 1, 2, 3 = three or more
    private string _gestureSearch = "";
    private readonly List<Button> _gestureSelectionButtons = new();
    private TextBlock? _gestureSelectionText;
    private readonly List<(Button Tile, string Name)> _gestureTiles = new();

    private LegacyGesture? SelectedGesture
        => _selectedGestureName is null ? null : _legacyData.Gestures.FirstOrDefault(g => string.Equals(g.Name, _selectedGestureName, StringComparison.Ordinal));

    private bool GestureMatchesLibraryFilter(LegacyGesture gesture)
    {
        var fingers = _gestureFingerFilter switch
        {
            1 => gesture.FingerCount == 1,
            2 => gesture.FingerCount == 2,
            3 => gesture.FingerCount >= 3,
            _ => true
        };
        if (!fingers) return false;
        var query = _gestureSearch.Trim();
        return query.Length == 0
               || DisplayName(gesture.Name).Contains(query, StringComparison.OrdinalIgnoreCase)
               || gesture.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private FrameworkElement NewGestureLibraryToolbar(ComboBox viewMode, Action rerender)
    {
        _gestureSelectionButtons.Clear();
        _gestureTiles.Clear();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        void SelectionButton(string text, Func<LegacyGesture, System.Threading.Tasks.Task> run, bool danger = false)
        {
            var button = new Button { Content = text, MinWidth = 64 };
            if (danger && ThemeBrush("SystemFillColorCriticalBrush") is { } critical)
                button.Foreground = critical;
            button.Click += async (_, _) =>
            {
                if (SelectedGesture is { } gesture)
                    await RunUiActionAsync(() => run(gesture));
            };
            _gestureSelectionButtons.Add(button);
            actions.Children.Add(button);
        }
        SelectionButton(L("重训", "Retrain", "重訓", "다시 훈련"), gesture => DrawGestureAsync(gesture));
        SelectionButton(L("改名", "Rename", "重新命名", "이름 변경"), gesture => RenameGestureAsync(gesture));
        SelectionButton(L("删除", "Delete", "刪除", "삭제"), gesture => DeleteGestureAsync(gesture), danger: true);
        _gestureSelectionText = new TextBlock { FontSize = 12, Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
        actions.Children.Add(_gestureSelectionText);

        // Finger filter as a segmented row of toggle buttons.
        var filter = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var toggles = new List<ToggleButton>();
        void FilterButton(int value, string text)
        {
            var toggle = new ToggleButton { Content = text, IsChecked = _gestureFingerFilter == value, MinWidth = 52 };
            toggle.Click += (_, _) =>
            {
                _gestureFingerFilter = value;
                foreach (var other in toggles) other.IsChecked = ReferenceEquals(other, toggle);
                rerender();
            };
            toggles.Add(toggle);
            filter.Children.Add(toggle);
        }
        FilterButton(0, L("全部", "All", "全部", "전체"));
        FilterButton(1, CountText(1, L("指", "finger(s)", "指", "손가락")));
        FilterButton(2, CountText(2, L("指", "finger(s)", "指", "손가락")));
        FilterButton(3, T("3 指及以上", "3+ fingers"));

        var search = new TextBox { PlaceholderText = T("搜索手势", "Search gestures"), Text = _gestureSearch, Width = 200, VerticalAlignment = VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, T("搜索手势", "Search gestures"));
        search.TextChanged += (_, _) =>
        {
            _gestureSearch = search.Text;
            rerender();
        };
        viewMode.ClearValue(ComboBox.HeaderProperty);
        viewMode.MinWidth = 120;
        viewMode.VerticalAlignment = VerticalAlignment.Center;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(viewMode, IntentText("显示方式", "View"));

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(filter);
        tools.Children.Add(search);
        tools.Children.Add(viewMode);

        // Wide: actions left, filters right. Narrow: two rows.
        var bar = new Grid { ColumnSpacing = 16, RowSpacing = 10 };
        bar.Children.Add(actions);
        bar.Children.Add(tools);
        bool? wide = null;
        void Arrange(double width)
        {
            var nextWide = width >= 900;
            if (wide == nextWide) return;
            wide = nextWide;
            bar.ColumnDefinitions.Clear();
            bar.RowDefinitions.Clear();
            if (nextWide)
            {
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }
            else
            {
                bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            Grid.SetColumn(tools, nextWide ? 1 : 0);
            Grid.SetRow(tools, nextWide ? 0 : 1);
            tools.HorizontalAlignment = nextWide ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        bar.SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        Arrange(double.PositiveInfinity);
        // The filters may not fit a very narrow window: let them scroll sideways.
        var scroller = new ScrollViewer
        {
            Content = bar,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollMode = ScrollMode.Disabled
        };
        var card = NewCardPanel(0);
        card.Children.Add(scroller);
        return NewCard(card);
    }

    private void UpdateGestureSelectionToolbar()
    {
        var gesture = SelectedGesture;
        if (gesture is null)
            _selectedGestureName = null;
        foreach (var button in _gestureSelectionButtons)
            button.IsEnabled = gesture is not null;
        if (_gestureSelectionText is not null)
            _gestureSelectionText.Text = gesture is null
                ? T("先选中一个手势", "Select a gesture first")
                : F("已选中：{0}", "Selected: {0}", DisplayName(gesture.Name));
        var accent = ThemeBrush("AccentFillColorDefaultBrush") ?? new SolidColorBrush(Color.FromArgb(255, 76, 194, 255));
        foreach (var (tile, name) in _gestureTiles)
        {
            var selected = string.Equals(name, _selectedGestureName, StringComparison.Ordinal);
            tile.BorderBrush = selected ? accent : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            tile.BorderThickness = new Thickness(selected ? 2 : 1);
            tile.Background = selected
                ? (IsDark ? new SolidColorBrush(Color.FromArgb(255, 47, 74, 98)) : new SolidColorBrush(Color.FromArgb(255, 222, 238, 252)))
                : SubtleBrush();
        }
    }

    /// <summary>Gestures as small tiles; viewMode 1 = grid (small), 2 = tiles (larger).</summary>
    private FrameworkElement NewGestureTileGroup(string title, LegacyGesture[] gestures, int viewMode)
    {
        var panel = NewCardPanel(10);
        panel.Children.Add(new TextBlock
        {
            Text = $"{title}  {CountText(gestures.Length, L("个", "items", "個", "개"))}",
            Style = BodyStrongTextBlockStyle
        });
        if (gestures.Length == 0)
        {
            panel.Children.Add(new TextBlock { Text = L("暂无手势", "No gestures", "暫無手勢", "제스처 없음"), Opacity = .68 });
            return NewCard(panel);
        }

        var used = new HashSet<string>(
            _legacyData.Applications.SelectMany(app => app.Actions).Select(action => action.GestureName),
            StringComparer.OrdinalIgnoreCase);
        var large = viewMode == 2;
        var tileWidth = large ? 156d : 112d;
        var tiles = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        foreach (var gesture in gestures)
            tiles.Children.Add(NewGestureTile(gesture, large, !used.Contains(gesture.Name)));

        var textScale = new Windows.UI.ViewManagement.UISettings().TextScaleFactor;
        var minimumWidth = tileWidth * Math.Max(1, textScale);
        var previousColumns = 0;
        void Arrange(double width)
        {
            var columns = Math.Max(1, (int)Math.Floor((width + 6) / (minimumWidth + 6)));
            if (previousColumns == columns) return;
            previousColumns = columns;
            tiles.ColumnDefinitions.Clear();
            tiles.RowDefinitions.Clear();
            for (var column = 0; column < columns; column++)
                tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var row = 0; row < (gestures.Length + columns - 1) / columns; row++)
                tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < tiles.Children.Count; i++)
            {
                Grid.SetColumn((FrameworkElement)tiles.Children[i], i % columns);
                Grid.SetRow((FrameworkElement)tiles.Children[i], i / columns);
            }
        }
        tiles.SizeChanged += (_, args) => Arrange(args.NewSize.Width);
        Arrange(0);
        panel.Children.Add(tiles);
        return NewCard(panel);
    }

    private Button NewGestureTile(LegacyGesture gesture, bool large, bool unused)
    {
        var name = DisplayName(gesture.Name);
        var content = new Grid { RowSpacing = 4 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var preview = NewGestureCanvas(gesture, large ? 132 : 92, large ? 86 : 58);
        preview.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(preview);
        var label = new TextBlock
        {
            Text = name,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetRow(label, 1);
        content.Children.Add(label);
        // Finger count in the corner; the groups mix 1 and 2 fingers.
        var fingers = new TextBlock
        {
            Text = gesture.FingerCount.ToString(System.Globalization.CultureInfo.CurrentCulture),
            FontSize = 11,
            Opacity = 0.6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -2, 0, 0)
        };
        content.Children.Add(fingers);
        if (unused)
        {
            var dot = new Ellipse
            {
                Width = 6, Height = 6,
                Fill = IsDark ? new SolidColorBrush(Color.FromArgb(255, 107, 113, 128)) : new SolidColorBrush(Color.FromArgb(255, 160, 168, 180)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0)
            };
            ToolTipService.SetToolTip(dot, T("还没有动作使用这个手势", "No action uses this gesture yet"));
            content.Children.Add(dot);
        }

        var tile = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(6, 8, 6, 6),
            CornerRadius = new CornerRadius(8),
            Background = SubtleBrush(),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };
        var fingerText = CountText(gesture.FingerCount, L("指", "finger(s)", "指", "손가락"));
        ToolTipService.SetToolTip(tile, unused ? $"{name} · {fingerText} · {T("还没有动作使用这个手势", "No action uses this gesture yet")}" : $"{name} · {fingerText}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, $"{name} · {fingerText}");
        tile.Click += (_, _) =>
        {
            _selectedGestureName = string.Equals(_selectedGestureName, gesture.Name, StringComparison.Ordinal) ? null : gesture.Name;
            UpdateGestureSelectionToolbar();
        };
        tile.DoubleTapped += async (_, _) =>
        {
            _selectedGestureName = gesture.Name;
            UpdateGestureSelectionToolbar();
            await RunUiActionAsync(() => RenameGestureAsync(gesture));
        };
        var menu = new MenuFlyout();
        void Item(string text, Func<System.Threading.Tasks.Task> run)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += async (_, _) => await RunUiActionAsync(run);
            menu.Items.Add(item);
        }
        Item(L("重训", "Retrain", "重訓", "다시 훈련"), () => DrawGestureAsync(gesture));
        Item(L("改名", "Rename", "重新命名", "이름 변경"), () => RenameGestureAsync(gesture));
        Item(L("删除", "Delete", "刪除", "삭제"), () => DeleteGestureAsync(gesture));
        menu.Opening += (_, _) =>
        {
            _selectedGestureName = gesture.Name;
            UpdateGestureSelectionToolbar();
        };
        tile.ContextFlyout = menu;
        _gestureTiles.Add((tile, gesture.Name));
        return tile;
    }
}
