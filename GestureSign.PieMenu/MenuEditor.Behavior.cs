using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GestureSign.PieMenu;

internal sealed partial class MenuEditor
{
    private Expander MenuBehavior(PieMenu menu)
    {
        var x = PositionField(L("native.fixed-x"), menu.FixedX, value => menu.FixedX = value);
        var y = PositionField(L("native.fixed-y"), menu.FixedY, value => menu.FixedY = value);
        var picker = Button(L("settings.fixed-position-tooltip"), async () =>
        {
            try { await PickPositionAsync(x, y); }
            catch (Exception error) { SetStatus(error.Message); }
        });
        var coordinates = new StackPanel
        {
            Spacing = 8,
            Visibility = menu.HasFixedPosition ? Visibility.Visible : Visibility.Collapsed
        };
        coordinates.Children.Add(x);
        coordinates.Children.Add(y);
        coordinates.Children.Add(picker);
        var fixedPosition = BehaviorToggle("settings.fixed-position-mode", menu.HasFixedPosition, enabled =>
        {
            menu.UseFixedPosition = enabled;
            coordinates.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        });
        return PropertySection(L("settings.menu-behavior"), fixedPosition,
            BehaviorToggle("settings.anchored-mode", menu.Anchored, value => menu.Anchored = value),
            BehaviorToggle("settings.hover-mode", menu.Hover, value => menu.Hover = value), coordinates);
    }

    private NumberBox PositionField(string label, double? value, Action<double> update)
    {
        var field = new NumberBox
        {
            Header = label, Minimum = 0, Maximum = 1, SmallChange = .05, LargeChange = .25,
            Value = Math.Clamp(value ?? .5, 0, 1), HorizontalAlignment = HorizontalAlignment.Stretch,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        AutomationProperties.SetName(field, label);
        field.ValueChanged += (_, args) =>
        {
            if (double.IsFinite(args.NewValue)) update(Math.Clamp(args.NewValue, 0, 1));
        };
        return field;
    }

    private async Task PickPositionAsync(NumberBox x, NumberBox y)
    {
        var title = L("settings.fixed-position-tooltip");
        var dialog = new ContentDialog
        {
            Title = title, XamlRoot = _page.XamlRoot,
            CloseButtonText = L("settings.cancel"),
            PrimaryButtonText = L("settings.fixed-position-picker.confirm")
        };
        var map = new Grid
        {
            FlowDirection = FlowDirection.LeftToRight, RowSpacing = 6, ColumnSpacing = 6, Height = 150
        };
        for (var i = 0; i < 3; i++)
        {
            map.RowDefinitions.Add(new RowDefinition());
            map.ColumnDefinitions.Add(new ColumnDefinition());
        }
        var chosenX = double.IsFinite(x.Value) ? x.Value : .5;
        var chosenY = double.IsFinite(y.Value) ? y.Value : .5;
        var position = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center };
        var screen = new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        Grid.SetColumnSpan(screen, 3);
        Grid.SetRowSpan(screen, 3);
        map.Children.Add(screen);
        screen.Tapped += (_, args) =>
        {
            var point = args.GetPosition(screen);
            chosenX = Math.Clamp(Math.Round(point.X / screen.ActualWidth, 2), 0, 1);
            chosenY = Math.Clamp(Math.Round(point.Y / screen.ActualHeight, 2), 0, 1);
            UpdateLabel();
        };
        UpdateLabel();
        AddPreset(0, 0, "settings.fixed-position-picker.top-left-preset-tooltip");
        AddPreset(2, 0, "settings.fixed-position-picker.top-right-preset-tooltip");
        AddPreset(1, 1, "settings.fixed-position-picker.centered-preset-tooltip");
        AddPreset(0, 2, "settings.fixed-position-picker.bottom-left-preset-tooltip");
        AddPreset(2, 2, "settings.fixed-position-picker.bottom-right-preset-tooltip");
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(Surface(map));
        content.Children.Add(position);
        dialog.Content = content;
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            x.Value = chosenX;
            y.Value = chosenY;
        }

        void UpdateLabel() => position.Text = $"X: {chosenX:0.##}   Y: {chosenY:0.##}";

        void AddPreset(int column, int row, string key)
        {
            var label = L(key);
            var point = Button("+", () =>
            {
                chosenX = column / 2d;
                chosenY = row / 2d;
                UpdateLabel();
            });
            point.HorizontalAlignment = HorizontalAlignment.Center;
            point.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(point, label);
            ToolTipService.SetToolTip(point, label);
            Grid.SetColumn(point, column);
            Grid.SetRow(point, row);
            map.Children.Add(point);
        }
    }

    private Expander InteractionModes() => PropertySection(L("settings.general-settings-dialog.interaction-modes"),
        BehaviorToggle("settings.general-settings-dialog.enable-marking-mode", _document.Marking, value => _document.Marking = value),
        BehaviorToggle("settings.general-settings-dialog.enable-turbo-mode", _document.Turbo, value => _document.Turbo = value),
        BehaviorToggle("settings.general-settings-dialog.require-click-for-hover-mode", _document.ConfirmHover, value => _document.ConfirmHover = value),
        BehaviorToggle("settings.general-settings-dialog.trigger-center-click-on-key-release", _document.CloseOnCenterRelease, value => _document.CloseOnCenterRelease = value),
        BehaviorToggle("settings.general-settings-dialog.move-pointer-to-menu-center", _document.WarpPointer, value => _document.WarpPointer = value),
        MouseButtonBehavior("settings.general-settings-dialog.right-mouse-button-behavior", _document.RightButton, "close", value => _document.RightButton = value),
        MouseButtonBehavior("settings.general-settings-dialog.middle-mouse-button-behavior", _document.MiddleButton, "select", value => _document.MiddleButton = value));

    private FrameworkElement BehaviorToggle(string key, bool value, Action<bool> update)
    {
        var label = L(key);
        var toggle = Toggle(null, value, update, label);
        ToolTipService.SetToolTip(toggle, L(key + "-info"));
        return Property(label, toggle);
    }

    private FrameworkElement MouseButtonBehavior(string key, string value, string fallback, Action<string> update)
    {
        string[] behavior = ["select", "nothing", "close", "select-parent"];
        var label = L(key);
        var choices = Choices(null, behavior, Array.IndexOf(behavior, value) >= 0 ? value : fallback, update, mode => mode switch
        {
            "select" => L("settings.general-settings-dialog.select-item"),
            "nothing" => L("settings.general-settings-dialog.do-nothing"),
            "close" => L("settings.general-settings-dialog.close-menu"),
            _ => L("settings.general-settings-dialog.select-parent")
        }, automationName: label);
        ToolTipService.SetToolTip(choices, L(key + "-info"));
        return Property(label, choices);
    }
}
