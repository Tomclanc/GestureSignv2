using System;
using System.IO;
using GestureSign.PieMenu;
using GestureSign.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    // Settings-page entry points are isolated from navigation. Their legacy
    // bodies remain in the shell temporarily while controls are migrated.
    private UIElement BuildOptionsPageFromService() => BuildOptionsPageCore();
    private sealed record NativeQuickActionsPage(FrameworkElement View, MenuEditor Editor, ToggleSwitch Toggle,
        string Executable, string? Culture);
    private NativeQuickActionsPage? _nativeQuickActionsPage;

    private UIElement BuildQuickActionsPageFromService()
    {
        var options = _legacyData.Options;
        if (_nativeQuickActionsPage is { } cached &&
            string.Equals(cached.Executable, options.KandoExecutablePath, StringComparison.OrdinalIgnoreCase) &&
            cached.Culture == _uiCultureName && cached.Toggle.IsOn == options.KandoEnabled &&
            PieMenuComponentService.IsCompatible(cached.Executable) &&
            !PieMenuComponentService.NeedsManagedRepair(cached.Executable, KandoComponentPaths.PieMenuInstallDirectory))
        {
            ApplyPieMenuSearch(cached.Editor);
            return cached.View;
        }

        _nativeQuickActionsPage = null;
        return BuildQuickActionsPageCore();
    }
    private UIElement BuildTouchPadPageFromService() => BuildTouchPadPageCore();

    private UIElement BuildQuickActionsPageCore()
    {
        var root = NewSection();
        var options = _legacyData.Options;
        var needsRepair = PieMenuComponentService.NeedsManagedRepair(options.KandoExecutablePath, KandoComponentPaths.PieMenuInstallDirectory);
        var native = !needsRepair && PieMenuComponentService.IsCompatible(options.KandoExecutablePath);
        root.Children.Add(MarkSearchTarget(NewPieMenuComponentCard(native, needsRepair, out var enabled), "quick-actions-component"));
        if (needsRepair)
        {
            DispatcherQueue.TryEnqueue(() => _ = RepairPieMenuAsync());
            return root;
        }
        if (native)
        {
            root.Children.Add(BuildPieMenuEditorRegion(options.KandoExecutablePath, enabled, out var editor));
            if (editor is not null)
            {
                _nativeQuickActionsPage = new(root, editor, enabled, options.KandoExecutablePath, _uiCultureName);
                ApplyPieMenuSearch(editor);
            }
            return root;
        }
        var existing = FindKandoExecutablePath(options.KandoExecutablePath);
        if (existing is not null && !KandoComponentPaths.IsNativePieMenu(existing))
        {
            var legacy = NewSection();
            legacy.Children.Add(NewKandoComponentCard());
            legacy.Children.Add(NewKandoPowerToysToggleRow(options.KandoEnabled));
            legacy.Children.Add(NewKandoSettingsHotKeyRow(options.KandoSettingsHotKey));
            legacy.Children.Add(NewKandoOpenSettingsRow());
            root.Children.Add(new Expander { Header = T("环形菜单", "Pie menu"), Content = legacy,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        return root;
    }

    private FrameworkElement BuildPieMenuEditorRegion(string executable, ToggleSwitch enabled, out MenuEditor? menuEditor)
    {
        var editor = NewSection();
        menuEditor = AddPieMenuSettings(editor, executable);
        var controls = new ContentControl
        {
            Content = editor, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var stripes = new Microsoft.UI.Xaml.Shapes.Path
        {
            Stroke = new SolidColorBrush(Microsoft.UI.Colors.Gray), StrokeThickness = 1, Opacity = .24,
            IsHitTestVisible = false
        };
        var region = new Grid();
        region.Children.Add(controls);
        // The overlay must not contribute to the editor's measured size.
        var overlay = new Canvas { IsHitTestVisible = false };
        overlay.Children.Add(stripes);
        region.Children.Add(overlay);

        void DrawStripes()
        {
            double width = region.ActualWidth, height = region.ActualHeight;
            var geometry = new PathGeometry();
            for (double x = 0; x < width + height; x += 18)
            {
                double startY = Math.Max(0, x - width);
                double endY = Math.Min(x, height);
                if (endY <= startY) continue;
                var line = new PathFigure { StartPoint = new Point(x - startY, startY) };
                line.Segments.Add(new LineSegment { Point = new Point(x - endY, endY) });
                geometry.Figures.Add(line);
            }
            stripes.Data = geometry;
        }

        void ApplyEnabledState()
        {
            controls.IsEnabled = enabled.IsOn;
            controls.IsHitTestVisible = enabled.IsOn;
            controls.Opacity = enabled.IsOn ? 1 : .48;
            stripes.Visibility = enabled.IsOn ? Visibility.Collapsed : Visibility.Visible;
            if (!enabled.IsOn) DrawStripes();
        }

        region.SizeChanged += (_, _) => { if (!enabled.IsOn) DrawStripes(); };
        enabled.Toggled += (_, _) => ApplyEnabledState();
        ApplyEnabledState();
        return region;
    }
}
