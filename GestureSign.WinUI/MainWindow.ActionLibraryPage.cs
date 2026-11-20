using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GestureSign.Foundation.RadialMenus;
using GestureSign.Foundation.Steps;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GestureSign.WinUI;

/// <summary>
/// "Action library" page: every reusable action, grouped by category. The
/// radial menu, gestures and edge swipes run these actions by reference.
/// </summary>
public sealed partial class MainWindow
{
    private Grid? _actionLibraryCategoryHost;
    private Grid? _actionLibraryListHost;
    private string? _actionLibraryCategory;
    private string _actionLibraryFilter = "";

    private UIElement BuildActionLibraryPage()
    {
        EnsureRadialConfig();
        var root = NewSection();

        var toolbar = new Grid { ColumnSpacing = 8 };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var create = new Button { Content = T("新建动作", "New action"), MinHeight = 32 };
        if (ThemeStyle("AccentButtonStyle") is { } accentStyle) create.Style = accentStyle;
        create.Click += async (_, _) => await RunUiActionAsync(async () =>
        {
            if (await EditRadialActionAsync(null) is null) return;
            RefreshActionLibraryViews();
        });
        toolbar.Children.Add(create);
        var filter = new TextBox { PlaceholderText = T("筛选", "Filter"), Text = _actionLibraryFilter, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Right };
        filter.TextChanged += (_, _) =>
        {
            _actionLibraryFilter = filter.Text;
            RefreshActionLibraryList();
        };
        Grid.SetColumn(filter, 1);
        toolbar.Children.Add(filter);
        root.Children.Add(toolbar);

        var columns = new Grid { ColumnSpacing = 16 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _actionLibraryCategoryHost = new Grid();
        _actionLibraryListHost = new Grid();
        Grid.SetColumn(_actionLibraryListHost, 1);
        columns.Children.Add(_actionLibraryCategoryHost);
        columns.Children.Add(_actionLibraryListHost);
        root.Children.Add(NewCard(columns));

        RefreshActionLibraryCategories();
        RefreshActionLibraryList();
        return root;
    }

    /// <summary>Refreshes every view that shows library actions (library page and radial page).</summary>
    private void RefreshActionLibraryViews()
    {
        RefreshActionLibraryCategories();
        RefreshActionLibraryList();
        RefreshRadialWheel();
        RefreshRadialLibrary();
    }

    private static string CategoryOf(RadialMenuAction action) => (action.Category ?? "").Trim();

    private void RefreshActionLibraryCategories()
    {
        if (_actionLibraryCategoryHost is null || _radialConfig is null)
            return;
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single };
        void Add(string? id, string text, int count)
        {
            var row = new Grid { Tag = id ?? "\0all", Padding = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis });
            var number = new TextBlock { Text = count.ToString(System.Globalization.CultureInfo.CurrentCulture), Opacity = 0.55 };
            Grid.SetColumn(number, 1);
            row.Children.Add(number);
            list.Items.Add(row);
            if (string.Equals(id, _actionLibraryCategory, StringComparison.Ordinal))
                list.SelectedItem = row;
        }
        var actions = _radialConfig.Actions;
        Add(null, T("全部", "All"), actions.Count);
        foreach (var group in actions.Where(a => CategoryOf(a).Length > 0).GroupBy(CategoryOf).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            Add(group.Key, group.Key, group.Count());
        var uncategorized = actions.Count(a => CategoryOf(a).Length == 0);
        if (uncategorized > 0 && uncategorized < actions.Count)
            Add("", T("未分类", "Uncategorized"), uncategorized);
        if (list.SelectedItem is null && list.Items.Count > 0)
        {
            _actionLibraryCategory = null;
            list.SelectedIndex = 0;
        }
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not FrameworkElement { Tag: string tag }) return;
            _actionLibraryCategory = tag == "\0all" ? null : tag;
            RefreshActionLibraryList();
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = T("分类", "Category"), Style = BodyStrongTextBlockStyle });
        panel.Children.Add(list);
        _actionLibraryCategoryHost.Children.Clear();
        _actionLibraryCategoryHost.Children.Add(panel);
    }

    private string ActionKindText(RadialMenuAction action)
    {
        var command = action.Commands.FirstOrDefault();
        if (command is null)
            return "";
        if (string.Equals(command.PluginClass, StepsPluginClass, StringComparison.Ordinal))
            return F("{0} 个步骤", "{0} steps", CountSteps(ParseStepProgram(command.CommandSettings).Steps));
        return PluginName(command.PluginClass ?? "");
    }

    private static int CountSteps(IEnumerable<Step>? steps)
        => (steps ?? Enumerable.Empty<Step>()).Sum(s => 1 + CountSteps(s.Body) + CountSteps(s.Else));

    private void RefreshActionLibraryList()
    {
        if (_actionLibraryListHost is null || _radialConfig is null)
            return;
        var config = _radialConfig;
        var query = _actionLibraryFilter.Trim();
        var actions = config.Actions
            .Where(a => _actionLibraryCategory is null || string.Equals(CategoryOf(a), _actionLibraryCategory, StringComparison.CurrentCultureIgnoreCase))
            .Where(a => query.Length == 0
                        || (a.Name ?? "").Contains(query, StringComparison.CurrentCultureIgnoreCase)
                        || (a.Description ?? "").Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        async Task Edit(RadialMenuAction action)
        {
            if (await EditRadialActionAsync(action) is null) return;
            RefreshActionLibraryViews();
        }

        var list = new ListView { SelectionMode = ListViewSelectionMode.Single };
        foreach (var action in actions)
        {
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6), Tag = action, Opacity = action.IsEnabled ? 1 : 0.5 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Glyph(action.Icon is { Length: 1 } glyph ? glyph : "", 20);
            if (ThemeBrush("AccentTextFillColorPrimaryBrush") is { } accent) icon.Foreground = accent;
            icon.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(icon);
            var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(action.Name) ? "?" : action.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var detail = string.IsNullOrWhiteSpace(action.Description) ? ActionKindText(action) : action.Description!;
            if (detail.Length > 0)
                texts.Children.Add(new TextBlock { Text = detail.Replace("\n", " "), FontSize = 12, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);
            var side = new TextBlock { Text = CategoryOf(action), FontSize = 12, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(side, 2);
            row.Children.Add(side);

            var current = action;
            row.DoubleTapped += async (_, _) => await RunUiActionAsync(() => Edit(current));
            var menu = new MenuFlyout();
            void Item(string text, Func<Task> onClick)
            {
                var item = new MenuFlyoutItem { Text = text };
                item.Click += async (_, _) => await RunUiActionAsync(onClick);
                menu.Items.Add(item);
            }
            Item(L("编辑", "Edit", "編輯", "편집"), () => Edit(current));
            Item(T("创建副本", "Duplicate"), () =>
            {
                var copy = System.Text.Json.JsonSerializer.Deserialize<RadialMenuAction>(System.Text.Json.JsonSerializer.Serialize(current))!;
                copy.Id = RadialMenuConfig.NewId();
                copy.Name = F("{0} 副本", "{0} copy", current.Name);
                config.Actions.Add(copy);
                SaveRadialConfig();
                RefreshActionLibraryViews();
                return Task.CompletedTask;
            });
            Item(current.IsEnabled ? L("停用", "Disable", "停用", "사용 안 함") : L("启用", "Enable", "啟用", "사용"), () =>
            {
                current.IsEnabled = !current.IsEnabled;
                SaveRadialConfig();
                RefreshActionLibraryViews();
                return Task.CompletedTask;
            });
            menu.Items.Add(new MenuFlyoutSeparator());
            Item(L("删除", "Delete", "刪除", "삭제"), async () =>
            {
                if (!await ConfirmDialogAsync(DeleteConfirmationTitle(),
                        F("确定删除动作 {0}？", "Delete action {0}?", current.Name), DeleteButtonText()))
                    return;
                config.Actions.Remove(current);
                config.Normalize(); // drops wheel slots that referenced it
                SaveRadialConfig();
                RefreshActionLibraryViews();
            });
            row.ContextFlyout = menu;
            list.Items.Add(row);
        }

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = T("动作库", "Action library"), Style = BodyStrongTextBlockStyle });
        if (actions.Count == 0)
            panel.Children.Add(new TextBlock { Text = T("还没有动作。点「新建动作」从模板开始。", "No actions yet. Choose New action to start from a template."), Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        else
            panel.Children.Add(new TextBlock { Text = T("双击编辑，右键可创建副本、停用或删除。轮盘、手势和边缘交互都可以使用这些动作。", "Double-click to edit; right-click to duplicate, disable or delete. The radial menu, gestures and edge swipes can all use these actions."), Opacity = 0.6, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(list);
        _actionLibraryListHost.Children.Clear();
        _actionLibraryListHost.Children.Add(panel);
    }
}
