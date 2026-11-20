using System;
using System.Linq;
using GestureSign.Foundation.Intent;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace GestureSign.WinUI;
public sealed partial class MainWindow
{
    private readonly GestureSign.Foundation.Intent.AccessibilitySettings _accessibility = LoadAccessibility();
    private readonly AccessibilitySettingsView _contrast = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, ControlSize> _controlSizes = new();
    private sealed class ControlSize { public double MinHeight; public double FontSize; public Thickness Padding; }
    private static GestureSign.Foundation.Intent.AccessibilitySettings LoadAccessibility() { GestureSign.Foundation.Intent.AccessibilitySettings.Reload(); return GestureSign.Foundation.Intent.AccessibilitySettings.Current; }
    private bool HighContrast { get { try { return _contrast.Settings.HighContrast; } catch (System.Runtime.InteropServices.COMException) { return false; } } }
    private void InitializeAccessibilityEvents()
    {
        bool previous = HighContrast;
        void RefreshContrast()
        {
            if (previous == HighContrast) return;
            previous = HighContrast;
            ApplyMicaDimmingOverlay();
            ShowSelectedPage();
        }
        // Some unpackaged Windows configurations cannot subscribe to this WinRT event.
        // Recheck on activation as a fallback; accessibility must never prevent startup.
        Activated += (_, _) => RefreshContrast();
        Windows.Foundation.TypedEventHandler<Windows.UI.ViewManagement.AccessibilitySettings, object> changed = (_, _) => DispatcherQueue.TryEnqueue(RefreshContrast);
        try
        {
            _contrast.Settings.HighContrastChanged += changed;
            Closed += (_, _) => { try { _contrast.Settings.HighContrastChanged -= changed; } catch (System.Runtime.InteropServices.COMException) { } };
        }
        catch (System.Runtime.InteropServices.COMException) { }
    }
    private SolidColorBrush SystemBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is SolidColorBrush brush) return brush;
        var colors = new UISettings();
        return new SolidColorBrush(colors.GetColorValue(key.Contains("Background") ? UIColorType.Background : UIColorType.Foreground));
    }
    private void ShowPage(string tag, bool recordHistory = true)
    {
        ShowPageCore(tag, recordHistory);
        if (tag == "quickActions" && _nativeQuickActionsPage is { View: var quickActions })
        {
            quickActions.Loaded -= QuickActionsAccessibilityLoaded;
            quickActions.Loaded += QuickActionsAccessibilityLoaded;
            if (quickActions.IsLoaded) ApplyAccessibleTree(quickActions);
            return;
        }
        foreach (var page in PageHost.Children.OfType<FrameworkElement>()) page.Loaded += (_, _) => ApplyAccessibleTree(Root);
        DispatcherQueue.TryEnqueue(() => ApplyAccessibleTree(Root));
    }
    private void QuickActionsAccessibilityLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement page) ApplyAccessibleTree(page);
    }

    private void ApplyAccessibleTree(DependencyObject root)
    {
        // Capture sizes before changing inherited font values; toggling never compounds scaling.
        void Capture(DependencyObject node)
        {
            if (node is Control c) _controlSizes.GetValue(c, x => new ControlSize { MinHeight = x.MinHeight, FontSize = x.FontSize, Padding = x.Padding });
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Capture(VisualTreeHelper.GetChild(node, i));
        }
        void Apply(DependencyObject node)
        {
            if (node is Control c)
            {
                c.UseSystemFocusVisuals = true;
                if (c is Button or ToggleSwitch or ComboBox or TextBox or CheckBox or NavigationViewItem)
                {
                    var original = _controlSizes.GetValue(c, x => new ControlSize { MinHeight = x.MinHeight, FontSize = x.FontSize, Padding = x.Padding });
                    c.MinHeight = _accessibility.LargeControls ? Math.Max(original.MinHeight, 56) : original.MinHeight;
                    c.FontSize = _accessibility.LargeControls ? Math.Max(original.FontSize, 18) : original.FontSize;
                    if (c is Button or ComboBox or TextBox or NavigationViewItem)
                        c.Padding = _accessibility.LargeControls ? new Thickness(Math.Max(original.Padding.Left, 16), Math.Max(original.Padding.Top, 10), Math.Max(original.Padding.Right, 16), Math.Max(original.Padding.Bottom, 10)) : original.Padding;
                }
            }
            if (node is TextBlock text) { text.IsTextScaleFactorEnabled = true; if (HighContrast) text.Opacity = 1; }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Apply(VisualTreeHelper.GetChild(node, i));
        }
        Capture(root);
        Apply(root);
        Navigation.OpenPaneLength = _accessibility.LargeControls ? 280 : 248;
        UpdateGlobalSearchLayout();
    }
    private FrameworkElement NewAccessibilitySettings()
    {
        var panel = NewCardPanel(10);
        panel.Loaded += (_, _) => ApplyAccessibleTree(Root);
        var large = new ToggleSwitch { Header = IntentText("大控件（含左侧导航）", "Large controls (including navigation)"), IsOn = _accessibility.LargeControls };
        large.Toggled += (_, _) => { _accessibility.LargeControls = large.IsOn; _accessibility.Save(); ApplyAccessibleTree(Root); };
        var numbers = new ToggleSwitch { Header = IntentText("手势预览显示轨迹编号", "Number traces in gesture previews"), IsOn = _accessibility.NumberTrails };
        numbers.Toggled += (_, _) => { _accessibility.NumberTrails = numbers.IsOn; _accessibility.Save(); };
        var edge = new Slider { Header = IntentText("触控板边缘宽度（每边百分比）", "Touchpad edge width (% per edge)"), Minimum = 3, Maximum = 20, StepFrequency = 1, Value = Math.Clamp(_accessibility.TouchpadEdgePercent, 3, 20), MinWidth = 180 };
        var edgeValue = new TextBlock { Text = $"{edge.Value:0}%" };
        AutomationProperties.SetName(edge, IntentText("触控板边缘宽度百分比", "Touchpad edge width percent"));
        edge.ValueChanged += (_, e) => { _accessibility.TouchpadEdgePercent = (int)e.NewValue; edgeValue.Text = $"{e.NewValue:0}%"; _accessibility.Save(); };
        panel.Children.Add(large); panel.Children.Add(numbers); panel.Children.Add(edge); panel.Children.Add(edgeValue);
        panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Text = IntentText("大控件增大导航、按钮文字和点击区域，立即生效；系统缩放会在此基础上叠加。跟随 Windows 文字大小和高对比度。Tab 切换控件，空格切换选项，方向键调整滑块。轨迹编号在下次打开预览时生效。加宽边缘会缩小中间绘制区域。", "Large controls enlarge navigation, control text and hit targets immediately; Windows scaling applies on top. Follows Windows text size and contrast themes. Tab moves focus, Space toggles options, arrows adjust sliders. Trace numbers update when reopening previews. Wider edges reduce the central drawing area.") });
        var windows = NewPillButton(IntentText("打开 Windows 无障碍设置", "Open Windows accessibility settings"), false);
        windows.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:easeofaccess"));
        panel.Children.Add(windows);
        return new Expander { Header = IntentText("无障碍：显示与操作", "Accessibility: display and interaction"), Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }
    private FrameworkElement NewIntentReviewScope()
    {
        var panel = NewCardPanel(8);
        foreach (var title in new[] {
            IntentText("两指自由绘制：始终判断", "Two-finger drawings: always reviewed"),
            IntentText("三／四指自由绘制：始终判断", "Three/four-finger drawings: always reviewed"),
            IntentText("自定义绘制轨迹（含单指）：始终判断", "Custom drawn paths (including one finger): always reviewed"),
            IntentText("开启 AI评分或 AI 否决后，以上轨迹绑定的所有动作均参与判断，不限于智能关闭。AI评分不拦截；AI 否决根据判断阻止动作。", "AI scoring and AI veto cover every action bound to these drawings, not just Smart Close. Scoring never blocks; veto can prevent an action.") })
            panel.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        return new Expander { Header = IntentText("AI 判断范围", "AI review scope"), Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }
    private void AddAccessibleTraceMarker(Canvas canvas, Microsoft.UI.Xaml.Shapes.Polyline line, int index)
    {
        if (HighContrast) line.Stroke = SystemBrush("SystemControlForegroundBaseHighBrush");
        if (line.Points.Count == 0) return;
        if (_accessibility.NumberTrails)
        {
            var number = new TextBlock { Text = (index + 1).ToString(), FontSize = 12 };
            Canvas.SetLeft(number, Math.Max(0, line.Points[0].X - 10)); Canvas.SetTop(number, Math.Max(0, line.Points[0].Y - 14));
            canvas.Children.Add(number);
        }
        AddGestureArrowHead(canvas, line.Points.ToArray(), line.Stroke, 2);
    }
    private sealed class AccessibilitySettingsView { public Windows.UI.ViewManagement.AccessibilitySettings Settings { get; } = new(); }
}
