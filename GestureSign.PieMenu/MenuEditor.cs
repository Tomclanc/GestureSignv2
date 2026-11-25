using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace GestureSign.PieMenu;

internal sealed partial class MenuEditor
{
    private readonly Microsoft.UI.WindowId _owner;
    private readonly StackPanel _page;
    private readonly MenuDocument _document;
    private readonly MenuEditSession _session;
    private readonly Action<MenuDocument, PieMenu> _preview;
    private readonly Func<Task<string>> _applySavedShortcuts;
    private readonly Func<bool, Task> _setShortcutRecording;
    private readonly UISettings _uiSettings = new();


    private StackPanel _details = null!;
    private StackPanel _itemDetails = null!;
    private TextBlock _status = null!;
    private readonly MenuLanguage _language;
    private readonly MenuThemes _themes = MenuThemes.Load();
    private PieMenu? _menu;

    private static readonly string[] ActionTypes = [
        "simulate-hotkey", "open-file", "open-uri", "execute-command", "set-clipboard", "delay",
        "execute-macro", "focus-window", "send-websocket-message", "send-tcp-message", "open-menu",
        "open-settings", "close-submenu", "close-menu", "inhibit-shortcuts"
    ];

    internal MenuEditor(Microsoft.UI.WindowId owner, StackPanel page, MenuEditSession session,
        Action<MenuDocument, PieMenu> preview, Func<Task<string>> applySavedShortcuts,
        Func<bool, Task> setShortcutRecording, MenuLanguage language)
    {
        _owner = owner;
        _page = page;
        _session = session;
        _document = session.Document;
        _preview = preview;
        _applySavedShortcuts = applySavedShortcuts;
        _setShortcutRecording = setShortcutRecording;
        _language = language;
        _menu = _document.Menus.FirstOrDefault();
        _ = MenuIcons.GetAsync(_iconRoot);
        WarmSystemIcons();
        BuildPage();
    }

    private void ShowDetails(bool refreshPreview = true)
    {
        _details.Children.Clear();
        if (refreshPreview) RefreshRing();
        if (_menu is null) { _details.Children.Add(new TextBlock { Text = L("settings.no-menus") }); return; }
        if (_branch.Count > 0)
        {
            var back = Button("‹  " + L("menu-actions.close-submenu.name"), LeaveSubmenu);
            _details.Children.Add(back);
        }
        _itemDetails = new StackPanel { Spacing = 8 };
        if (_selectedItem is not null)
        {
            _details.Children.Add(_itemDetails);
            ShowItemSettings();
            return;
        }
        var menu = _menu;
        var name = L("native.name");
        var rootIcon = IconPickerButton(RootIcon(menu, 34), menu.SourceIconTheme, menu.SourceIcon,
            (theme, icon) =>
            {
                menu.SourceIcon = icon;
                menu.SourceIconTheme = theme;
                RefreshMenuList();
                ShowDetails();
            });
        rootIcon.HorizontalAlignment = HorizontalAlignment.Center;
        _details.Children.Add(rootIcon);
        var nameBox = Field(null, menu.Name, value =>
        {
            menu.Name = value;
            if (_selectedMenuLabel is not null) _selectedMenuLabel.Text = value;
        }, name);
        _details.Children.Add(Property(name, nameBox));
        var shortcut = L("settings.shortcut-label");
        _details.Children.Add(Property(shortcut, ShortcutPicker(shortcut, menu.Shortcut, global: true,
            value => menu.Shortcut = value, showHeader: false, afterCommit: value => ApplyShortcutAsync(menu, value))));
        _details.Children.Add(MenuBehavior(menu));
        _details.Children.Add(ConditionFields(menu));
        var centerTitle = L("native.inline-settings.center-workflow");
        var center = new StackPanel { Spacing = 8 };
        center.Children.Add(Label(centerTitle, 16));
        var quickKey = L("settings.quick-select-key-label");
        center.Children.Add(Property(quickKey, ShortcutPicker(quickKey, menu.CenterKey, global: false,
            value => menu.CenterKey = value, showHeader: false)));
        center.Children.Add(new TextBlock { Text = L("native.inline-settings.center-workflow-description"), TextWrapping = TextWrapping.Wrap });
        center.Children.Add(WorkflowRows(menu.CenterActions, () => ShowDetails(refreshPreview: false)));
        _details.Children.Add(Surface(center));
        var remove = L("settings.delete-menu");
        _details.Children.Add(Property(remove, Button(L("native.remove"), RemoveMenu)));
    }

    private void ShowItemSettings()
    {
        _itemDetails.Children.Clear();
        if (_selectedItem is { } item)
        {
            var label = L("native.label");
            var name = Field(null, item.Name, value => item.Name = value, label);
            _itemDetails.Children.Add(Property(label, name));
            _itemDetails.Children.Add(Property(L("native.item-icon"), IconPickerButton(PreviewIcon(item, 28), item.SourceIconTheme, item.SourceIcon, (theme, icon) =>
            {
                item.SourceIcon = icon;
                item.SourceIconTheme = theme;
                RefreshRing();
                ShowItemSettings();
            })));
            var angle = L("native.fixed-angle");
            _itemDetails.Children.Add(Property(angle, Field(null, FullNumber(item.Angle), value => { item.Angle = ParseNumber(value); RefreshRing(); }, angle)));
            var quickKey = L("settings.quick-select-key-label");
            _itemDetails.Children.Add(Property(quickKey, ShortcutPicker(quickKey, item.Key, global: false,
                value => item.Key = value, showHeader: false)));
            if (item.Submenu || item.Children.Count > 0)
                _itemDetails.Children.Add(Property(L("menu-items.submenu.name"), Button(L("settings.edit-submenu"), () => EnterSubmenu(item))));
            if (!item.Submenu)
                _itemDetails.Children.Add(PropertySection(L("settings.workflow-editor.select-workflow-name"),
                    WorkflowRows(item.Actions, ShowItemSettings)));
        }
    }

    private StackPanel WorkflowRows(System.Collections.Generic.List<PieAction> actions, Action refresh)
    {
        var rows = new StackPanel { Spacing = 8 };
        for (int i = 0; i < actions.Count; i++)
        {
            int index = i;
            var action = actions[index];
            var type = Choices(L("native.action"), ActionTypes.Append(action.Type).Distinct().ToArray(), action.Type,
                value => { actions[index] = action with { Type = value }; refresh(); },
                value => Array.IndexOf(ActionTypes, value) >= 0 ? L($"menu-actions.{value}.name") : value);
            rows.Children.Add(type);
            void Move(int delta)
            {
                int target = index + delta;
                if (target < 0 || target >= actions.Count) return;
                (actions[index], actions[target]) = (actions[target], actions[index]);
                refresh();
            }
            rows.Children.Add(Row(Button("↑", () => Move(-1)), Button("↓", () => Move(1)),
                Button(L("native.remove"), () => { actions.RemoveAt(index); refresh(); })));
            if (action.Type == "simulate-hotkey")
                rows.Children.Add(ShortcutPicker(L("menu-actions.simulate-hotkey.hotkey"), action.Value, global: true,
                    value => actions[index] = actions[index] with { Value = value }, physical: true));
            else if (action.Type is not ("close-menu" or "close-submenu" or "open-settings" or "inhibit-shortcuts"))
                rows.Children.Add(Field(L("native.action-value"), action.Value,
                    value => actions[index] = actions[index] with { Value = value }));
        }
        rows.Children.Add(Button("+  " + L("settings.workflow-editor.add-action"),
            () => { actions.Add(new("close-menu", "")); refresh(); }));
        return rows;
    }

    private static Expander PropertySection(string title, params UIElement[] controls)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var control in controls) panel.Children.Add(control);
        return new Expander { Header = title, Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }

    private void AddMenu()
    {
        _document.Menus.Add(new PieMenu
        {
            Name = UniqueName(L("native.new-menu")), SourceIcon = "logo-medium.svg", SourceIconTheme = MenuIcons.Kando
        });
        LoadMenus(_document.Menus.Count - 1);
    }

    private string UniqueName(string name)
    {
        var candidate = name;
        var n = 2;
        while (_document.Menus.Any(m => string.Equals(m.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            candidate = name + " (" + n++ + ")";
        return candidate;
    }

    private void RemoveMenu()
    {
        if (_menu is null) return;
        var index = _document.Menus.IndexOf(_menu);
        _document.Menus.Remove(_menu);
        LoadMenus(index);
    }

    private void MoveAction(PieItem item, int from, int delta)
    {
        var to = from + delta;
        if (to < 0 || to >= item.Actions.Count) return;
        (item.Actions[from], item.Actions[to]) = (item.Actions[to], item.Actions[from]);
        ShowItemSettings();
    }

    internal void SetStatus(string text)
    {
        _status.Text = text;
        _status.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private string L(string key) => _language.Text(key);
    private async void Save()
    {
        try
        {
            var invalid = _document.Menus.FirstOrDefault(menu => menu.Conditions is { } condition &&
                (!MenuConditions.IsValidPattern(condition.AppName) || !MenuConditions.IsValidPattern(condition.WindowName)));
            if (invalid is not null)
            {
                SetStatus($"{invalid.Name}: {L("native.condition-invalid")}");
                return;
            }
            var warnings = await _session.SaveAllAsync(_applySavedShortcuts);
            RefreshMenuList();
            SetStatus(warnings.Length > 0 ? warnings : L("native.saved"));
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private async Task ApplyShortcutAsync(PieMenu menu, string shortcut)
    {
        var message = await _session.SaveShortcutAsync(menu, shortcut, _language, _applySavedShortcuts);
        if (message.Length == 0)
            message = shortcut.Length == 0 ? L("native.shortcut-cleared") : string.Format(L("native.shortcut-saved"), shortcut);
        RefreshMenuList();
        SetStatus(message);
    }

    private void Preview()
    {
        try { if (_menu is not null) _preview(_document, _menu); }
        catch (Exception error) { SetStatus(error.Message); }
    }

    private async Task Import()
    {
        try
        {
            var picker = new FileOpenPicker(_owner);
            picker.FileTypeFilter.Add(".json"); picker.FileTypeFilter.Add(".json5");
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            var menus = KandoImport.Read(file.Path);
            foreach (var menu in menus)
            {
                menu.Name = UniqueName(menu.Name);
                _document.Menus.Add(menu);
            }
            LoadMenus(_document.Menus.Count - menus.Count);
            SetStatus(string.Format(CultureInfo.GetCultureInfo(_language.Tag), L("native.imported"), menus.Count));
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private async Task Export()
    {
        try
        {
            var picker = new FileSavePicker(_owner) { SuggestedFileName = "pie-menus", DefaultFileExtension = ".json" };
            picker.FileTypeChoices.Add(L("native.configuration"), [".json"]);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            MenuStorage.Save(_document, file.Path);
            SetStatus(string.Format(L("native.exported"), file.Path));
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private static string FullNumber(double? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
    private static double? ParseNumber(string value) => double.TryParse(value, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : null;
    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromArgb(255, r, g, b));
    private Button Button(object content, Action? action = null)
    {
        var button = new Button { Content = content };
        if (action is not null) button.Click += (_, _) => action();
        ContentPresenter? attachedPresenter = null;
        button.Loaded += (_, _) =>
        {
            if (VisualTreeHelper.GetChildrenCount(button) == 0 ||
                VisualTreeHelper.GetChild(button, 0) is not ContentPresenter presenter ||
                ReferenceEquals(presenter, attachedPresenter)) return;
            attachedPresenter = presenter;
            var states = VisualStateManager.GetVisualStateGroups(presenter)
                .FirstOrDefault(group => group.Name == "CommonStates");
            if (states is null) return;

            // Match the quick hover and slower release timing of Kando's editor.
            var transition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(100) };
            if (_uiSettings.AnimationsEnabled) presenter.BackgroundTransition = transition;
            states.CurrentStateChanging += (_, args) =>
            {
                var state = args.NewState?.Name;
                var animate = _uiSettings.AnimationsEnabled;
                transition.Duration = TimeSpan.FromMilliseconds(state is "PointerOver" or "Pressed" ? 100 : 300);
                presenter.BackgroundTransition = animate ? transition : null;
                presenter.Translation = animate && state == "Pressed" && !ReferenceEquals(button, _selectedPreviewButton)
                    ? Vector3.UnitY : Vector3.Zero;
            };
        };
        return button;
    }
    private static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    private static TextBox Field(string? header, string value, Action<string> update, string? automationName = null)
    {
        var field = new TextBox { Header = header, Text = value, MinWidth = 140, MaxWidth = 360 };
        if (automationName is not null) AutomationProperties.SetName(field, automationName);
        field.TextChanged += (_, _) => update(field.Text);
        return field;
    }
    private ToggleSwitch Toggle(string? header, bool value, Action<bool> update, string? automationName = null)
    {
        var field = new ToggleSwitch { Header = header, IsOn = value, OnContent = L("native.on"), OffContent = L("native.off") };
        if (automationName is not null) AutomationProperties.SetName(field, automationName);
        field.Toggled += (_, _) => update(field.IsOn);
        return field;
    }
    private static ComboBox Choices(string? header, string[] values, string selected, Action<string> update,
        Func<string, string>? displayName = null, string? automationName = null)
    {
        var options = values.Select(value => new MenuLanguage.Option(value, displayName?.Invoke(value) ?? value)).ToArray();
        var field = new ComboBox
        {
            Header = header, ItemsSource = options, DisplayMemberPath = nameof(MenuLanguage.Option.Name),
            SelectedItem = options.FirstOrDefault(option => option.Id == selected),
            MinWidth = 140, MaxWidth = 330
        };
        if (automationName is not null) AutomationProperties.SetName(field, automationName);
        field.SelectionChanged += (_, _) => { if (field.SelectedItem is MenuLanguage.Option option) update(option.Id); };
        return field;
    }
}
