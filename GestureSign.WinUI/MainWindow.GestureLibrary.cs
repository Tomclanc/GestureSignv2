using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private FrameworkElement NewGestureGroup(string title, LegacyGesture[] gestures, int viewMode)
    {
        var panel = NewCardPanel(10);
        panel.Children.Add(new TextBlock
        {
            Text = $"{title}  {CountText(gestures.Length, L("个", "items", "個", "개"))}",
            Style = BodyStrongTextBlockStyle
        });
        var cards = new Grid { ColumnSpacing = 12, RowSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var gesture in gestures)
            cards.Children.Add(NewGestureLibraryCard(gesture, viewMode));

        // Use the measured group width, including DPI and text scaling, to wrap cards.
        var textScale = new Windows.UI.ViewManagement.UISettings().TextScaleFactor;
        var minimumWidth = Math.Max(viewMode == 1 ? 250 : 300, (viewMode == 1 ? 230 : 270) * textScale);
        int previousColumns = 0;
        void Arrange(double width)
        {
            int columns = viewMode == 0 ? 1 : Math.Max(1, (int)Math.Floor((width + 12) / (minimumWidth + 12)));
            columns = Math.Min(columns, Math.Max(1, gestures.Length));
            if (previousColumns == columns) return;
            previousColumns = columns;
            cards.ColumnDefinitions.Clear();
            cards.RowDefinitions.Clear();
            for (int column = 0; column < columns; column++)
                cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int row = 0; row < (gestures.Length + columns - 1) / columns; row++)
                cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < cards.Children.Count; i++)
            {
                Grid.SetColumn((FrameworkElement)cards.Children[i], i % columns);
                Grid.SetRow((FrameworkElement)cards.Children[i], i / columns);
            }
        }
        cards.SizeChanged += (_, args) => Arrange(args.NewSize.Width);
        Arrange(0);
        if (gestures.Length == 0)
            panel.Children.Add(new TextBlock { Text = L("暂无手势", "No gestures", "暫無手勢", "제스처 없음"), Opacity = .68 });
        else
            panel.Children.Add(cards);
        return NewCard(panel);
    }

    private FrameworkElement NewGestureLibraryCard(LegacyGesture gesture, int viewMode)
    {
        var description = NewCardPanel(6);
        description.Children.Add(new TextBlock
        {
            Text = DisplayName(gesture.Name), TextWrapping = TextWrapping.Wrap,
            Style = BodyStrongTextBlockStyle,
            TextAlignment = viewMode == 0 ? TextAlignment.Left : TextAlignment.Center
        });
        description.Children.Add(new TextBlock
        {
            Text = CountText(gesture.FingerCount, L("指", "finger(s)", "指", "손가락")),
            Opacity = .68, TextAlignment = viewMode == 0 ? TextAlignment.Left : TextAlignment.Center
        });
        var buttons = NewInlineButtons(
            (L("重训", "Retrain", "重訓", "다시 훈련"), async () => await DrawGestureAsync(gesture)),
            (L("改名", "Rename", "重新命名", "이름 변경"), async () => await RenameGestureAsync(gesture)),
            (L("删除", "Delete", "刪除", "삭제"), async () => await DeleteGestureAsync(gesture)));
        var preview = NewGestureCanvas(gesture, viewMode == 0 ? 100 : viewMode == 1 ? 124 : 180, viewMode == 0 ? 64 : viewMode == 1 ? 60 : 108);
        var content = new Grid { ColumnSpacing = 16, RowSpacing = 10 };
        content.Children.Add(preview);
        content.Children.Add(description);
        content.Children.Add(buttons);
        bool? horizontal = null;
        void Arrange(double width)
        {
            bool nextHorizontal = viewMode == 0 && width >= 600;
            if (horizontal == nextHorizontal) return;
            horizontal = nextHorizontal;
            content.ColumnDefinitions.Clear();
            content.RowDefinitions.Clear();
            if (nextHorizontal)
            {
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition());
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }
            else
                for (int i = 0; i < 3; i++) content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < content.Children.Count; i++)
            {
                var child = (FrameworkElement)content.Children[i];
                Grid.SetColumn(child, nextHorizontal ? i : 0);
                Grid.SetRow(child, nextHorizontal ? 0 : i);
                child.VerticalAlignment = VerticalAlignment.Center;
            }
            preview.HorizontalAlignment = nextHorizontal ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        }
        content.SizeChanged += (_, args) => Arrange(args.NewSize.Width);
        Arrange(0);
        return new Border
        {
            Background = SubtleBrush(), BorderBrush = BorderBrush(), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Child = content,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }
}
