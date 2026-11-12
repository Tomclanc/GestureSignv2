using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private UIElement BuildIgnoredGestureCard()
    {
        var panel = NewCardPanel(10);
        panel.Children.Add(new TextBlock { Text = T("手势忽略", "Ignored gestures"), FontSize = 20 });
        panel.Children.Add(new TextBlock { Text = T("仅阻止所选手势执行动作，其他手势仍然可用；不保证还原系统原生输入。", "Block actions for the selected gesture while keeping other gestures available. Native input is not restored."), TextWrapping = TextWrapping.Wrap });
        var add = NewPillButton(T("添加手势忽略", "Add ignored gesture"), false);
        add.Click += async (_, _) => await EditIgnoredGestureAsync(null);
        panel.Children.Add(add);
        foreach (var app in _legacyData.Applications.Where(a => a.Type == "忽略" && !string.IsNullOrWhiteSpace(a.IgnoredGesture)))
        {
            var scope = app.MatchUsing == 4 ? T("所有程序", "All applications") : MatchUsingText(app.MatchUsing) + ": " + app.MatchString;
            panel.Children.Add(NewTableRow([app.IsEnabled ? T("开", "On") : T("关", "Off"), BuiltInGestureDisplayName(app.IgnoredGesture), scope], false,
                NewInlineButtonsWithContext(
                    (T("编辑", "Edit"), async _ => await EditIgnoredGestureAsync(app)),
                    (app.IsEnabled ? T("停用", "Disable") : T("启用", "Enable"), async button => { await ToggleApplicationEnabledAsync(app, button); ReloadData(); }),
                    (T("删除", "Delete"), async _ => await DeleteApplicationAsync(app)))));
        }
        return NewCard(panel, new Thickness(14));
    }

    private async Task EditIgnoredGestureAsync(LegacyApplication? app)
    {
        var panel = NewCardPanel(10);
        var gestures = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var edgeNames = from prefix in new[] { "TouchPadEdge", "TouchScreenEdge" }
                        from edge in new[] { "Top", "Bottom", "Left", "Right" }
                        from suffix in edge is "Top" or "Bottom" ? new[] { "", ".Left", ".Right" } : new[] { "", ".Up", ".Down" }
                        select prefix + "." + edge + suffix;
        var names = _legacyData.Gestures.Select(g => g.Name)
            .Concat(_legacyData.Applications.SelectMany(a => a.Actions).Select(a => a.GestureName))
            .Concat(edgeNames).Append(app?.IgnoredGesture ?? "")
            .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToArray();
        foreach (var name in names) gestures.Items.Add(new ComboBoxItem { Content = BuiltInGestureDisplayName(name), Tag = name });
        gestures.SelectedIndex = app == null ? 0 : Math.Max(0, Array.FindIndex(names, n => string.Equals(n, app.IgnoredGesture, StringComparison.OrdinalIgnoreCase)));
        var all = new CheckBox { Content = T("所有程序", "All applications"), IsChecked = app == null || app.MatchUsing == 4 };
        var match = new TextBox { Header = T("匹配文本", "Match Text"), Text = app?.MatchString ?? "" };
        var matchUsing = new ComboBox { Header = T("匹配方式", "Match by"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var text in new[] { T("窗口标题", "Window Title"), T("可执行文件", "Executable"), T("窗口类", "Window Class") }) matchUsing.Items.Add(text);
        matchUsing.SelectedIndex = app?.MatchUsing switch { 1 => 0, 0 => 2, _ => 1 };
        var regex = new CheckBox { Content = T("使用正则匹配", "Use regular expressions"), IsChecked = app?.IsRegEx ?? false };
        var enabled = new CheckBox { Content = T("启用", "Enable"), IsChecked = app?.IsEnabled ?? true };
        void ScopeChanged() { match.IsEnabled = matchUsing.IsEnabled = regex.IsEnabled = all.IsChecked != true; }
        all.Checked += (_, _) => ScopeChanged(); all.Unchecked += (_, _) => ScopeChanged(); ScopeChanged();
        panel.Children.Add(gestures); panel.Children.Add(all); panel.Children.Add(matchUsing); panel.Children.Add(match); panel.Children.Add(regex); panel.Children.Add(enabled);
        if (!await ConfirmDialogAsync(T("手势忽略", "Ignored gestures"), panel, T("保存", "Save"))) return;
        if (gestures.SelectedItem is not ComboBoxItem selected) return;
        if (all.IsChecked != true && string.IsNullOrWhiteSpace(match.Text))
        {
            await ShowInfoDialog(T("匹配文本", "Match Text"), T("请选择所有程序，或填写程序匹配文本。", "Select all applications or enter application match text."));
            return;
        }
        if (regex.IsChecked == true && all.IsChecked != true)
        {
            try { _ = new System.Text.RegularExpressions.Regex(match.Text, System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)); }
            catch (ArgumentException) { await ShowInfoDialog(T("正则", "Regex"), T("正则表达式无效。", "Invalid regular expression.")); return; }
        }
        _legacyData.SaveIgnoredGesture(app, (string)selected.Tag, all.IsChecked == true ? 4 : matchUsing.SelectedIndex switch { 0 => 1, 2 => 0, _ => 2 }, match.Text, regex.IsChecked == true, enabled.IsChecked == true);
        ReloadData();
    }
}
