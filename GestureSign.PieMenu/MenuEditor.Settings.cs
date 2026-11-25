using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using Windows.UI.ViewManagement;

namespace GestureSign.PieMenu;

internal sealed partial class MenuEditor
{
    private PieItem? _dragNode;

    private FrameworkElement BuildInlineSettings()
    {
        var tabs = new Pivot();
        Add(L("settings.menu-themes-dialog.title"), BuildThemeGallery);
        Add(L("settings.general-settings-dialog.advanced-menu-options"), BuildAdvanced);
        Add(L("settings.general-settings-dialog.backup-and-restore"), BuildBackup);
        Add(L("settings.general-settings-dialog.developer-options"), BuildDeveloper);
        tabs.SelectionChanged += (_, _) => LoadSelected();
        LoadSelected();
        return Surface(tabs);

        void Add(string header, Func<FrameworkElement> build) =>
            tabs.Items.Add(new PivotItem { Header = new TextBlock { Text = header, FontSize = 15 }, Tag = build });

        void LoadSelected()
        {
            if (tabs.SelectedItem is PivotItem item && item.Tag is Func<FrameworkElement> build)
                item.Content = new ScrollViewer { Content = build(), MaxHeight = 480 };
        }
    }

    private FrameworkElement BuildThemeGallery()
    {
        var panel = new StackPanel { Spacing = 10 };
        var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var tiles = new Dictionary<string, (Border Outline, Button Button)>();
        var credit = Label("", 12);
        credit.Opacity = .75;
        var paletteHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Right };
        foreach (var id in _themes.Ids)
        {
            var name = _themes.Name(id, _language);
            var preview = PieMenuAssets.Web(Path.Combine("themes", id, "preview.jpg"));
            var picture = new Border { Width = 128, Height = 86, CornerRadius = new CornerRadius(5) };
            if (File.Exists(preview))
            {
                picture.Background = new ImageBrush
                {
                    ImageSource = new BitmapImage(new Uri(preview)),
                    Stretch = Stretch.UniformToFill
                };
            }
            var label = Label(name, 13);
            label.TextAlignment = TextAlignment.Center;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.TextWrapping = TextWrapping.NoWrap;
            var tileContent = new StackPanel { Spacing = 3, Width = picture.Width };
            tileContent.Children.Add(picture);
            tileContent.Children.Add(label);
            var button = Button(tileContent, () =>
            {
                if (_document.WebTheme == id) return;
                _document.WebTheme = id;
                if (!_themes.Palettes(id).Contains(_document.Palette)) _document.Palette = MenuThemes.DefaultPalette;
                RefreshSelection();
            });
            button.Padding = new Thickness(4);
            button.CornerRadius = new CornerRadius(7);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.VerticalAlignment = VerticalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            AutomationProperties.SetName(button, name);
            ToolTipService.SetToolTip(button, name + " · " + ThemeCredit(id));

            var outline = new Border
            {
                Child = button, CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(2), Padding = new Thickness(2),
                Width = 146, Height = 124
            };
            tiles.Add(id, (outline, button));
            cards.Children.Add(outline);
        }
        panel.Children.Add(SettingsRowScroller(cards));
        panel.Children.Add(credit);


        static string ThemeCredit(string id) =>
            id == "purity" ? "Luctarie · CC BY 4.0" : "Simon Schneegans · CC0 1.0";

        void RefreshSelection()
        {
            var accent = new SolidColorBrush(_uiSettings.GetColorValue(UIColorType.Accent));
            foreach (var (id, tile) in tiles)
            {
                var selected = _document.WebTheme == id;
                tile.Outline.BorderBrush = selected ? accent : null;
                AutomationProperties.SetItemStatus(tile.Button, selected
                    ? GestureSign.WinUI.UiTranslationCatalog.TranslateComponent(_language.Tag, "Selected") : "");
            }
            credit.Text = ThemeCredit(_document.WebTheme);
            paletteHost.Content = Choices(null, _themes.Palettes(_document.WebTheme), _document.Palette,
                value => _document.Palette = value, value => _themes.PaletteName(value, _language),
                automationName: L("native.palette"));
        }
        var tuning = _document.Advanced;
        var appearance = new StackPanel { Spacing = 8 };
        appearance.Children.Add(Label(L("native.inline-settings.appearance"), 15));
        appearance.Children.Add(ThemeSettingRow(L("native.palette"), paletteHost));
        appearance.Children.Add(Number(L("native.inline-settings.menu-size"), tuning.ZoomFactor, .5, 4, .1, v => tuning.ZoomFactor = v));
        appearance.Children.Add(ToggleRow(L("settings.menu-themes-dialog.enable-menu-animations"),
            _document.Animations, value => _document.Animations = value));
        appearance.Children.Add(Number(L("native.inline-settings.fade-in"), tuning.FadeInDuration, 0, 1000, 25, v => tuning.FadeInDuration = v));
        appearance.Children.Add(Number(L("native.inline-settings.fade-out"), tuning.FadeOutDuration, 0, 1000, 25, v => tuning.FadeOutDuration = v));
        appearance.Children.Add(ToggleRow(L("settings.menu-themes-dialog.light-dark-mode"),
            tuning.EnableDarkModeForMenuThemes, v => tuning.EnableDarkModeForMenuThemes = v));

        var darkPalette = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Right };
        var darkPaletteLabel = L("native.inline-settings.dark-palette");
        void RefreshDarkPalette() => darkPalette.Content = Choices(null, _themes.Palettes(_document.DarkWebTheme),
            _document.DarkPalette, value => _document.DarkPalette = value, value => _themes.PaletteName(value, _language),
            automationName: darkPaletteLabel);
        var darkThemeLabel = L("native.inline-settings.dark-theme");
        appearance.Children.Add(ThemeSettingRow(darkThemeLabel, Choices(null, _themes.Ids, _document.DarkWebTheme, id =>
        {
            _document.DarkWebTheme = id;
            if (!_themes.Palettes(id).Contains(_document.DarkPalette)) _document.DarkPalette = MenuThemes.DefaultPalette;
            RefreshDarkPalette();
        }, id => _themes.Name(id, _language), automationName: darkThemeLabel)));
        appearance.Children.Add(ThemeSettingRow(darkPaletteLabel, darkPalette));
        RefreshDarkPalette();

        var interaction = new StackPanel { Spacing = 8 };
        interaction.Children.Add(Label(L("native.inline-settings.pointer-shortcuts"), 15));
        interaction.Children.Add(ToggleRow(L("settings.menu-themes-dialog.enable-pointer-reactive-effects"),
            tuning.EnablePointerReactiveEffects, v => tuning.EnablePointerReactiveEffects = v));
        interaction.Children.Add(ToggleRow(L("settings.menu-themes-dialog.selection-wedges"),
            tuning.EnableSelectionWedges, v => tuning.EnableSelectionWedges = v));
        interaction.Children.Add(ToggleRow(L("settings.menu-themes-dialog.underline-quick-select-key"),
            tuning.UnderlineQuickSelectKey, v => tuning.UnderlineQuickSelectKey = v));
        interaction.Children.Add(ToggleRow(L("settings.menu-themes-dialog.draw-quick-select-key"),
            tuning.DrawQuickSelectKey, v => tuning.DrawQuickSelectKey = v));
        interaction.Children.Add(InteractionModes());

        var options = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        options.ColumnDefinitions.Add(new ColumnDefinition());
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        options.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        options.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var appearanceCard = Surface(appearance);
        var interactionCard = Surface(interaction);
        appearanceCard.VerticalAlignment = interactionCard.VerticalAlignment = VerticalAlignment.Top;
        options.Children.Add(appearanceCard);
        options.Children.Add(interactionCard);
        Grid.SetRow(interactionCard, 1);
        options.SizeChanged += (_, e) =>
        {
            var wide = e.NewSize.Width >= 670;
            var column = wide ? 1 : 0;
            if (Grid.GetColumn(interactionCard) == column) return;
            options.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(interactionCard, column);
            Grid.SetRow(interactionCard, wide ? 0 : 1);
        };
        panel.Children.Add(options);

        FrameworkElement Number(string label, double value, double min, double max, double step, Action<double> change)
        {
            var box = new NumberBox { Value = value, Minimum = min, Maximum = max,
                SmallChange = step, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Width = 116 };
            box.ValueChanged += (_, e) => { if (double.IsFinite(e.NewValue)) change(e.NewValue); };
            AutomationProperties.SetName(box, label);
            return ThemeSettingRow(label, box);
        }

        FrameworkElement ToggleRow(string label, bool value, Action<bool> change) =>
            ThemeSettingRow(label, Toggle(null, value, change, label));

        panel.Children.Add(new HyperlinkButton
        {
            Content = L("native.inline-settings.theme-credits"),
            NavigateUri = new Uri("https://github.com/kando-menu/kando/tree/6df468a66695ac504afebfff2c581a42296511b4/assets/menu-themes")
        });
        RefreshSelection();
        return panel;
    }

    private static Grid ThemeSettingRow(string title, FrameworkElement control)
    {
        var row = new Grid { ColumnSpacing = 12, MinHeight = 34 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        control.MaxWidth = 174;
        if (control is ToggleSwitch toggle) toggle.MinWidth = 0;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private FrameworkElement BuildAdvanced()
    {
        var panel = new StackPanel { Spacing = 6 };
        var advanced = _document.Advanced;
        Add("max-selection-radius", advanced.MaxSelectionRadius, 9999, 50, v => _document.Advanced.MaxSelectionRadius = v);
        Add("center-click-zone-radius", advanced.CenterDeadZone, 999, 50, v => _document.Advanced.CenterDeadZone = v);
        Add("minimum-submenu-distance", advanced.MinParentDistance, 999, 50, v => _document.Advanced.MinParentDistance = v);
        Add("movement-threshold", advanced.DragThreshold, 999, 10, v => _document.Advanced.DragThreshold = v);
        Add("minimum-gesture-length", advanced.GestureMinStrokeLength, 999, 50, v => _document.Advanced.GestureMinStrokeLength = v);
        Add("minimum-gesture-angle", advanced.GestureMinStrokeAngle, 30, 1, v => _document.Advanced.GestureMinStrokeAngle = v);
        Add("gesture-jitter-threshold", advanced.GestureJitterThreshold, 50, 1, v => _document.Advanced.GestureJitterThreshold = v);
        Add("gesture-pause-timeout", advanced.GesturePauseTimeout, 999, 50, v => _document.Advanced.GesturePauseTimeout = v);
        Add("fixed-stroke-length", advanced.FixedStrokeLength, 999, 10, v => _document.Advanced.FixedStrokeLength = v);
        return panel;

        void Add(string key, double value, double max, double step, Action<double> change)
        {
            string label = L("settings.general-settings-dialog." + key);
            var field = new NumberBox
            {
                Value = Math.Clamp(value, 0, max), Minimum = 0, Maximum = max,
                SmallChange = step, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                Width = 128, HorizontalAlignment = HorizontalAlignment.Right
            };
            ToolTipService.SetToolTip(field, L("settings.general-settings-dialog." + key + "-info"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(field, label);
            field.ValueChanged += (_, e) => { if (double.IsFinite(e.NewValue)) change(e.NewValue); };
            var row = new Grid { ColumnSpacing = 15 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(field, 1);
            row.Children.Add(field);
            panel.Children.Add(row);
        }
    }
    private FrameworkElement BuildBackup()
    {
        return SettingsButtonRow(
            Button(L("native.inline-settings.back-up-menus"), async () => await TransferBackup(true, false)),
            Button(L("native.inline-settings.restore-menus"), async () => await TransferBackup(true, true)),
            Button(L("native.inline-settings.back-up-settings"), async () => await TransferBackup(false, false)),
            Button(L("native.inline-settings.restore-settings"), async () => await TransferBackup(false, true)),
            Button(L("native.inline-settings.open-config-folder"), () =>
            {
                var dir = Path.GetDirectoryName(MenuStorage.PathName)!;
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
            }));
    }

    private async Task TransferBackup(bool menus, bool restore)
    {
        try
        {
            if (restore)
            {
                var picker = new FileOpenPicker(_owner);
                picker.FileTypeFilter.Add(".json");
                var file = await picker.PickSingleFileAsync();
                if (file is null) return;
                var loaded = MenuStorage.ReadBackup(file.Path, menus);
                if (menus)
                {
                    _document.Menus = loaded.Menus;
                    LoadMenus(0);
                }
                else MenuStorage.CopyPreferences(loaded, _document);
                SetStatus(L("native.inline-settings.restored"));
                return;
            }
            var save = new FileSavePicker(_owner) { SuggestedFileName = menus ? "pie-menu-backup" : "pie-settings-backup", DefaultFileExtension = ".json" };
            save.FileTypeChoices.Add("JSON", [".json"]);
            var destination = await save.PickSaveFileAsync();
            if (destination is not null)
                MenuStorage.SaveBackup(_document, destination.Path, menus);
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    internal Action<string>? DeveloperCommand { get; set; }

    private FrameworkElement BuildDeveloper()
    {
        var inspect = Button(L("settings.general-settings-dialog.settings-window-dev-tools"));
        inspect.Click += (_, _) =>
        {
            var details = new TextBox
            {
                Text = MenuStorage.SettingsJson(_document),
                IsReadOnly = true, AcceptsReturn = true, MaxHeight = 300, MinWidth = 280
            };
            new Flyout { Content = details }.ShowAt(inspect);
        };
        return SettingsButtonRow(
            Button(L("settings.general-settings-dialog.reload-menu-theme"), () => DeveloperCommand?.Invoke("menu-theme")),
            Button(L("settings.general-settings-dialog.reload-sound-theme"), () => DeveloperCommand?.Invoke("sound-theme")),
            Button(L("settings.general-settings-dialog.menu-window-dev-tools"), () => DeveloperCommand?.Invoke("inspect-menu")),
            inspect);
    }

    private static ScrollViewer SettingsButtonRow(params UIElement[] buttons) => SettingsRowScroller(Row(buttons));

    private static ScrollViewer SettingsRowScroller(UIElement content) => new()
    {
        Content = content,
        HorizontalScrollMode = ScrollMode.Enabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollMode = ScrollMode.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
}
