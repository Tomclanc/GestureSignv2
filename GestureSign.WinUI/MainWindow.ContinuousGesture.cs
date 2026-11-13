using System;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private FrameworkElement NewContinuousGestureEditor(TextBox json)
    {
        var panel = NewCardPanel(8);
        var enabled = new CheckBox { Content = T("连续滑动（无需松手，重复执行动作）", "Continuous sliding (repeat without lifting)"), IsChecked = !string.IsNullOrWhiteSpace(json.Text) };
        var fingers = new ComboBox { Header = T("手指数", "Finger count"), HorizontalAlignment = HorizontalAlignment.Stretch };
        for (var count = 1; count <= 10; count++) fingers.Items.Add(count);
        fingers.SelectedIndex = 1;
        var direction = new ComboBox { Header = T("滑动方向", "Sliding direction"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var label in new[] { T("左", "Left"), T("右", "Right"), T("上", "Up"), T("下", "Down") }) direction.Items.Add(label);
        direction.SelectedIndex = 0;
        try
        {
            if (JsonNode.Parse(string.IsNullOrWhiteSpace(json.Text) ? "null" : json.Text) is JsonObject value)
            {
                fingers.SelectedIndex = Math.Clamp(value["ContactCount"]?.GetValue<int>() ?? 2, 1, 10) - 1;
                var raw = value["Gesture"]?.ToString();
                direction.SelectedIndex = raw switch { "Right" or "2" => 1, "Up" or "4" => 2, "Down" or "8" => 3, _ => 0 };
            }
        }
        catch (System.Text.Json.JsonException) { }
        var options = NewCardPanel(8);
        options.Children.Add(fingers); options.Children.Add(direction);
        options.Children.Add(new TextBlock { Text = T("按所选指数量和方向滑动，每移动一段距离就执行一次当前动作的命令，无需绘制图案。", "Slide with the selected finger count and direction. Each movement step runs this action's commands again; no drawn pattern is required."), TextWrapping = TextWrapping.Wrap });
        void Update()
        {
            options.Visibility = enabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            json.Text = enabled.IsChecked == true
                ? new JsonObject { ["ContactCount"] = fingers.SelectedIndex + 1, ["Gesture"] = 1 << direction.SelectedIndex }.ToJsonString()
                : "";
        }
        enabled.Checked += (_, _) => Update(); enabled.Unchecked += (_, _) => Update();
        fingers.SelectionChanged += (_, _) => Update(); direction.SelectionChanged += (_, _) => Update();
        options.Visibility = enabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        panel.Children.Add(enabled); panel.Children.Add(options);
        return panel;
    }
}
