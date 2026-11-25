using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GestureSign.PieMenu;

internal sealed partial class MenuEditor
{
    private readonly List<UIElement> _sections = [];
    private readonly Stack<PieItem> _branch = new();
    private ListView _menuPicker = null!;
    private Canvas _ring = null!;
    private double _renderedRingWidth;
    private DispatcherTimer? _hidePreviewTools;
    private StackPanel _itemTools = null!;
    private Button _previewButton = null!;
    private Button? _selectedPreviewButton;
    private Border _previewSelectionOutline = null!;
    private bool _refreshingMenus;
    private TextBlock? _selectedMenuLabel;
    private double? _parentAngle;
    private PieItem? _selectedItem;
    private List<PieItem>? Items => _branch.TryPeek(out var parent) ? parent.Children : _menu?.Items;

    private sealed record ItemTool(string Label, string Icon, string? Type);
    private static readonly ItemTool[] ItemTools =
    [
        new("menu-items.submenu.name", "submenu-item", null),
        new("menu-actions.execute-command.name", "command-item", "execute-command"),
        new("menu-actions.simulate-hotkey.name", "hotkey-item", "simulate-hotkey"),
        new("menu-actions.open-file.name", "file-item", "open-file"),
        new("menu-actions.open-uri.name", "uri-item", "open-uri"),
        new("menu-actions.open-settings.name", "settings-item", "open-settings"),
        new("menu-actions.open-menu.name", "redirect-item", "open-menu"),
        new("menu-actions.set-clipboard.name", "clipboard-item", "set-clipboard"),
        new("menu-actions.execute-macro.name", "macro-item", "execute-macro")
    ];

    private void BuildPage()
    {
        foreach (var section in _sections) _page.Children.Remove(section);
        _sections.Clear();

        var workspace = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        for (var i = 0; i < 3; i++)
        {
            workspace.ColumnDefinitions.Add(new ColumnDefinition());
            workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        _menuPicker = new ListView { SelectionMode = ListViewSelectionMode.Single };
        _menuPicker.SelectionChanged += (_, _) =>
        {
            if (_refreshingMenus || _menuPicker.SelectedIndex < 0) return;
            LoadMenus(_menuPicker.SelectedIndex, refreshList: false);
        };
        AutomationProperties.SetName(_menuPicker, L("settings.all-menus"));
        var menuColumn = new StackPanel { Spacing = 12 };
        menuColumn.Children.Add(Label(L("settings.all-menus"), 16));
        menuColumn.Children.Add(_menuPicker);
        var addMenu = Button("+  " + L("settings.create-menu-button"), AddMenu);
        addMenu.HorizontalAlignment = HorizontalAlignment.Stretch;
        menuColumn.Children.Add(addMenu);
        var libraryScroll = new ScrollViewer { Content = menuColumn };
        var library = Surface(libraryScroll);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _previewButton = Tool(Symbol.Play, "native.preview", Preview);
        tools.Children.Add(_previewButton);
        tools.Children.Add(Tool(Symbol.Save, "native.save", Save));
        tools.Children.Add(Tool(Symbol.Import, "settings.import-menu", async () => await Import()));
        tools.Children.Add(Tool(Symbol.Share, "settings.export-menu", async () => await Export()));
        var appearance = Tool(Symbol.Setting, "settings.menu-themes-dialog.title");
        var settingsPanel = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        appearance.Click += (_, _) =>
        {
            if (settingsPanel.Content is not null)
            {
                settingsPanel.Content = null;
                return;
            }
            var controls = new StackPanel { Spacing = 6 };
            var closeSettings = Button(L("native.inline-settings.collapse"), () => settingsPanel.Content = null);
            closeSettings.HorizontalAlignment = HorizontalAlignment.Right;
            controls.Children.Add(closeSettings);
            controls.Children.Add(BuildInlineSettings());
            settingsPanel.Content = controls;
            settingsPanel.StartBringIntoView();
        };
        tools.Children.Add(appearance);

        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _ring = new Canvas { Height = 338, FlowDirection = FlowDirection.LeftToRight };
        _ring.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width > 0 && e.NewSize.Width != _renderedRingWidth)
                RefreshRing();
        };
        _itemTools = new StackPanel { Spacing = 8 };
        _itemTools.Children.Add(Label(L("settings.add-menu-items"), 13));
        var itemTools = new VariableSizedWrapGrid { MaximumRowsOrColumns = 9, ItemWidth = 46, ItemHeight = 44,
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var tool in ItemTools)
        {
            var button = Button("", () => AddItem(tool));
            button.Content = RenderIcon(MenuIcons.Kando, tool.Icon + ".svg", "+", 21);
            button.Padding = new Thickness(0);
            button.Width = 40;
            button.Height = 38;
            button.CornerRadius = new CornerRadius(8);
            var title = L(tool.Label);
            AutomationProperties.SetName(button, title);
            ToolTipService.SetToolTip(button, title);
            itemTools.Children.Add(button);
        }
        _itemTools.Children.Add(itemTools);
        var editorColumn = new StackPanel { Spacing = 10 };
        editorColumn.Children.Add(tools);
        editorColumn.Children.Add(_status);
        var stage = Surface(_ring);
        stage.Padding = new Thickness(0);
        stage.Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(40, 42, 127, 186) },
                new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(12, 89, 113, 172) }
            }
        };
        editorColumn.Children.Add(stage);
        editorColumn.Children.Add(Surface(_itemTools));
        var editor = Surface(editorColumn, "CardBackgroundFillColorSecondaryBrush");
        _details = new StackPanel { Spacing = 10 };
        var inspector = Surface(new ScrollViewer { Content = _details, MaxHeight = 590 });

        workspace.Children.Add(library);
        workspace.Children.Add(editor);
        workspace.Children.Add(inspector);
        var wide = false;
        Arrange(false);
        workspace.SizeChanged += (_, e) =>
        {
            var useColumns = e.NewSize.Width >= 810;
            if (useColumns != wide) Arrange(useColumns);
        };
        AddSection(settingsPanel);
        AddSection(workspace);
        RefreshMenuList();
        ShowDetails();

        void Arrange(bool useColumns)
        {
            wide = useColumns;
            libraryScroll.MaxHeight = wide ? 590 : 220;
            workspace.ColumnDefinitions[0].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            workspace.ColumnDefinitions[1].Width = new GridLength(wide ? 2.25 : 1, GridUnitType.Star);
            workspace.ColumnDefinitions[2].Width = wide ? new GridLength(1.5, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(library, wide ? 0 : 1);
            Grid.SetColumn(editor, 1);
            Grid.SetColumn(inspector, wide ? 2 : 1);
            Grid.SetRow(editor, wide ? 0 : 1);
            Grid.SetRow(inspector, wide ? 0 : 2);
        }
    }

    private static Border Surface(UIElement child, string background = "CardBackgroundFillColorDefaultBrush")
    {
        var resources = Application.Current.Resources;
        return new Border
        {
            Child = child, Padding = new Thickness(12), CornerRadius = new CornerRadius(10),
            Background = resources.TryGetValue(background, out var fill) ? fill as Brush : null,
            BorderBrush = resources.TryGetValue("CardStrokeColorDefaultBrush", out var stroke) ? stroke as Brush : null,
            BorderThickness = new Thickness(1)
        };
    }

    private Button Tool(Symbol icon, string key, Action? action = null)
    {
        var label = L(key);
        var button = Button(new SymbolIcon(icon), action);
        button.Width = 36;
        button.Height = 34;
        button.Padding = new Thickness(0);
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        return button;
    }

    private void RefreshRing()
    {
        _hidePreviewTools?.Stop();
        _ring.Children.Clear();
        _selectedPreviewButton = null;
        _previewButton.IsEnabled = _menu is not null;
        _itemTools.Visibility = _menu is null ? Visibility.Collapsed : Visibility.Visible;
        _renderedRingWidth = 0;
        if (_menu is null || _ring.ActualWidth <= 0) return;
        _renderedRingWidth = _ring.ActualWidth;
        _previewSelectionOutline = new Border
        {
            BorderBrush = Brush(99, 181, 237), BorderThickness = new Thickness(2),
            IsHitTestVisible = false
        };
        var size = Math.Min(_ring.ActualWidth, _ring.Height);
        var radius = Math.Max(40, size * .34);
        var nodeSize = Math.Clamp(size * .16, 36, 54);
        var center = new Windows.Foundation.Point(_ring.ActualWidth / 2, _ring.Height / 2);
        var items = Items!;
        var angles = PieLayout.Arrange(items, _parentAngle);
        List<Button>? visibleTools = null;
        var hideTools = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _hidePreviewTools = hideTools;
        hideTools.Tick += (_, _) => ShowTools(null);

        void ShowTools(List<Button>? tools)
        {
            hideTools.Stop();
            if (ReferenceEquals(visibleTools, tools)) return;
            if (visibleTools is not null)
                foreach (var tool in visibleTools) tool.Visibility = Visibility.Collapsed;
            visibleTools = tools;
            if (tools is not null)
                foreach (var tool in tools) tool.Visibility = Visibility.Visible;
        }

        void ScheduleHide(List<Button> tools)
        {
            if (!ReferenceEquals(visibleTools, tools)) return;
            hideTools.Stop();
            hideTools.Start();
        }
        var branch = _branch.TryPeek(out var parent);
        AddNode(branch ? PreviewIcon(parent!, 32) : RootIcon(_menu, 32),
            branch ? parent!.Name : _menu.Name, nodeSize + 16, center.X, center.Y, null);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var angle = angles[i] * Math.PI / 180;
            AddNode(PreviewIcon(item, 24), item.Name, nodeSize,
                center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle), item);
        }
        _ring.Children.Add(_previewSelectionOutline);

        void AddNode(FrameworkElement icon, string label, double diameter, double x, double y, PieItem? item)
        {
            var button = Button(icon);
            var tools = item is null ? null : new List<Button>(3);
            button.Click += (_, _) =>
            {
                ShowTools(tools);
                if (ReferenceEquals(_selectedPreviewButton, button)) return;
                SelectPreviewButton(button);
                _selectedItem = item;
                ShowDetails(refreshPreview: false);
            };
            button.PointerEntered += (_, e) =>
            {
                if (_dragNode is null && e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Touch)
                    ShowTools(tools);
            };
            button.GotFocus += (_, _) =>
            {
                if (button.FocusState == FocusState.Keyboard) ShowTools(tools);
            };
            button.Width = button.Height = diameter;
            button.Padding = new Thickness(0);
            button.CornerRadius = new CornerRadius(diameter / 2);
            AutomationProperties.SetName(button, label);
            if (item is null) ToolTipService.SetToolTip(button, label);
            Canvas.SetLeft(button, x - diameter / 2);
            Canvas.SetTop(button, y - diameter / 2);
            _ring.Children.Add(button);
            if (item is null || tools is null) { if (_selectedItem is null) SelectPreviewButton(button); return; }
            button.PointerExited += (_, _) => ScheduleHide(tools);
            button.LostFocus += (_, _) => ScheduleHide(tools);
            button.CanDrag = true;
            button.AllowDrop = true;
            button.DragStarting += (_, e) =>
            {
                _dragNode = item;
                ShowTools(null);
                e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            };
            button.DropCompleted += (_, _) => _dragNode = null;
            button.DragOver += (_, e) =>
            {
                if (_dragNode is not null && !ReferenceEquals(_dragNode, item))
                    e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            };
            button.Drop += (_, _) =>
            {
                if (_dragNode is not { } dragged || Items is not { } siblings) return;
                var from = siblings.IndexOf(dragged);
                var to = siblings.IndexOf(item);
                if (from < 0 || to < 0 || from == to) return;
                siblings.RemoveAt(from);
                siblings.Insert(to, dragged);
                _dragNode = null;
                _selectedItem = dragged;
                ShowDetails();
            };
            if (ReferenceEquals(item, _selectedItem)) SelectPreviewButton(button);
            double direction = angles[items.IndexOf(item)] * Math.PI / 180;
            AddTool(new SymbolIcon(Symbol.Copy), "settings.duplicate-menu-item", direction - .48, () =>
            {
                var index = items.IndexOf(item);
                var clone = System.Text.Json.JsonSerializer.Deserialize<PieItem>(System.Text.Json.JsonSerializer.Serialize(item))!;
                clone.Angle = null;
                items.Insert(index + 1, clone);
                _selectedItem = clone;
                ShowDetails();
            });
            AddTool(new SymbolIcon(Symbol.Delete), "settings.delete-menu-item", direction + .48, () => RemoveItem(item));
            AddTool(new FontIcon { Glyph = item.Angle.HasValue ? "\uE72E" : "\uE785", FontSize = 15 }, "native.fixed-angle", direction + Math.PI, () =>
            {
                item.Angle = item.Angle.HasValue ? null : angles[items.IndexOf(item)];
                ShowDetails();
            });

            void AddTool(IconElement icon, string key, double bearing, Action action)
            {
                const double toolSize = 28;
                var distance = diameter / 2 + 24;
                var tool = Button(icon, action);
                tool.Padding = new Thickness(0);
                AutomationProperties.SetName(tool, L(key));
                ToolTipService.SetToolTip(tool, L(key));
                tool.Width = tool.Height = toolSize;
                tool.CornerRadius = new CornerRadius(toolSize / 2);
                Canvas.SetLeft(tool, Math.Clamp(x + distance * Math.Sin(bearing) - toolSize / 2, 0, Math.Max(0, _ring.ActualWidth - toolSize)));
                Canvas.SetTop(tool, Math.Clamp(y - distance * Math.Cos(bearing) - toolSize / 2, 0, _ring.Height - toolSize));
                Canvas.SetZIndex(tool, 2);
                tool.Visibility = Visibility.Collapsed;
                tool.PointerEntered += (_, _) => ShowTools(tools);
                tool.PointerExited += (_, _) => ScheduleHide(tools);
                tool.GotFocus += (_, _) => ShowTools(tools);
                tool.LostFocus += (_, _) => ScheduleHide(tools);
                tools.Add(tool);
                _ring.Children.Add(tool);
            }
        }
    }

    private void SelectPreviewButton(Button button)
    {
        _selectedPreviewButton?.ClearValue(Control.BackgroundProperty);
        _selectedPreviewButton = button;
        button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(55, 99, 181, 237));
        _previewSelectionOutline.Width = button.Width;
        _previewSelectionOutline.Height = button.Height;
        _previewSelectionOutline.CornerRadius = button.CornerRadius;
        Canvas.SetLeft(_previewSelectionOutline, Canvas.GetLeft(button));
        Canvas.SetTop(_previewSelectionOutline, Canvas.GetTop(button));
    }

    private void AddSection(UIElement section)
    {
        _sections.Add(section);
        _page.Children.Add(section);
    }

    private FrameworkElement Property(string title, FrameworkElement control)
    {
        if (control is ComboBox or TextBox)
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
        var row = new StackPanel { Spacing = 5 };
        row.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        row.Children.Add(control);
        return row;
    }

    private void LoadMenus(int selected, bool refreshList = true)
    {
        _menu = _document.Menus.Count == 0 ? null : _document.Menus[Math.Clamp(selected, 0, _document.Menus.Count - 1)];
        _branch.Clear();
        _parentAngle = null;
        _selectedItem = null;
        if (refreshList)
            RefreshMenuList();
        else
        {
            _refreshingMenus = true;
            try { _menuPicker.SelectedIndex = selected; }
            finally { _refreshingMenus = false; }
            _selectedMenuLabel = (_menuPicker.SelectedItem as FrameworkElement)?.Tag as TextBlock;
        }
        ShowDetails();
    }

    internal void SelectMenu(string name)
    {
        var index = _document.Menus.FindIndex(menu => string.Equals(menu.Name, name, StringComparison.Ordinal));
        if (index >= 0 && !ReferenceEquals(_document.Menus[index], _menu)) LoadMenus(index, refreshList: false);
    }

    private void RefreshMenuList()
    {
        _refreshingMenus = true;
        try
        {
            _selectedMenuLabel = null;
            _menuPicker.Items.Clear();
            foreach (var menu in _document.Menus)
            {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var icon = RootIcon(menu, 23);
                icon.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(icon);
                var labels = new StackPanel { Spacing = 2 };
                var label = new TextBlock { Text = menu.Name, TextTrimming = TextTrimming.CharacterEllipsis };
                row.Tag = label;
                if (ReferenceEquals(menu, _menu)) _selectedMenuLabel = label;
                labels.Children.Add(label);
                labels.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(menu.Shortcut) ? L("settings.not-bound") : menu.Shortcut,
                    Opacity = .72, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(labels, 1);
                row.Children.Add(labels);
                _menuPicker.Items.Add(row);
            }
            _menuPicker.SelectedIndex = _menu is null ? -1 : _document.Menus.IndexOf(_menu);
        }
        finally { _refreshingMenus = false; }
    }

    private void LeaveSubmenu()
    {
        _selectedItem = _branch.Pop();
        _parentAngle = ParentAngle();
        ShowDetails();
    }

    private double? ParentAngle()
    {
        double? angle = null;
        IReadOnlyList<PieItem> siblings = _menu!.Items;
        foreach (var parent in _branch.Reverse())
        {
            var index = siblings.ToList().IndexOf(parent);
            angle = PieLayout.Normalize(PieLayout.Arrange(siblings, angle)[index] + 180);
            siblings = parent.Children;
        }
        return angle;
    }

    private void EnterSubmenu(PieItem item)
    {
        _branch.Push(item);
        _parentAngle = ParentAngle();
        _selectedItem = null;
        ShowDetails();
    }

    private void AddItem(ItemTool tool)
    {
        if (Items is not { } items) return;
        var item = new PieItem
        {
            Name = L(tool.Label), Submenu = tool.Type is null, Glyph = tool.Type is null ? "▸" : "◇",
            SourceIcon = tool.Icon + ".svg", SourceIconTheme = MenuIcons.Kando
        };
        if (tool.Type is { } type) item.Actions.Add(new(type, type == "execute-macro" ? "[]" : ""));
        items.Add(item);
        _selectedItem = item;
        ShowDetails();
    }

    private void RemoveItem(PieItem item)
    {
        if (Items?.Remove(item) != true) return;
        if (ReferenceEquals(_selectedItem, item)) _selectedItem = null;
        ShowDetails();
    }

    private void MoveItem(PieItem item, int delta)
    {
        if (Items is not { } siblings) return;
        var from = siblings.IndexOf(item);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= siblings.Count) return;
        (siblings[from], siblings[to]) = (siblings[to], siblings[from]);
        ShowDetails();
    }

    private FrameworkElement PreviewIcon(PieItem item, double size)
    {
        if (item.SourceIconTheme is not null && item.SourceIcon is not null)
            return RenderIcon(item.SourceIconTheme, item.SourceIcon, item.Glyph, size);
        if (item.Glyph is "◇" or "▸")
        {
            var tool = item.Submenu ? ItemTools[0] : ItemTools.FirstOrDefault(entry => entry.Type == item.Actions.FirstOrDefault()?.Type);
            if (tool is not null && (item.Submenu || item.Actions.Count > 0))
                return RenderIcon(MenuIcons.Kando, tool.Icon + ".svg", item.Glyph, size);
        }
        return RenderIcon(null, null, item.Glyph, size);
    }

    private static TextBlock Label(string value, double size) =>
        new() { Text = value, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
}
