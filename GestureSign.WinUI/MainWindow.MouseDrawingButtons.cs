using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace GestureSign.WinUI;
public sealed partial class MainWindow
{
    private FrameworkElement NewMouseDrawingButtons(int configured)
    {
        var mask = configured;
        var root = new StackPanel { Spacing = 12 };
        var master = new ToggleSwitch { IsOn = mask != 0, OnContent = IntentText("开", "On"), OffContent = IntentText("关", "Off") };
        bool changing = false;
        void Save() {
            UpdateOptionAndReload("DrawingButton", (master.IsOn ? mask : 0).ToString(CultureInfo.InvariantCulture));
            UpdateOptionAndReload("MouseGesturesDisabledByUser", master.IsOn ? "False" : "True");
        }
        if (mask == 0) mask = 2097152;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        ToggleSwitch? rightToggle = null;
        foreach (var (name, bit) in new[] { (IntentText("左键", "Left"), 1048576), (IntentText("中键", "Middle"), 4194304), (IntentText("右键", "Right"), 2097152) })
        {
            var toggle = new ToggleSwitch { VerticalAlignment = VerticalAlignment.Center, IsOn = (mask & bit) != 0, OnContent = IntentText("开", "On"), OffContent = IntentText("关", "Off") };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, name);
            toggle.Toggled += (_, _) => {
                mask = toggle.IsOn ? mask | bit : mask & ~bit;
                changing = true; master.IsOn = mask != 0; changing = false; Save();
            };
            if (bit == 2097152) rightToggle = toggle;
            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
            buttonRow.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            buttonRow.Children.Add(toggle);
            panel.Children.Add(buttonRow);
        }
        master.Toggled += (_, _) => { if (!changing) { if (master.IsOn && mask == 0) rightToggle!.IsOn = true; Save(); } };

        root.Children.Add(MarkSearchTarget(NewSettingRow(IntentText("启用鼠标手势", "Enable mouse gestures"), null, master), "mouse-gestures"));
        root.Children.Add(MarkSearchTarget(NewSettingRow(IntentText("鼠标手势启动键", "Mouse gesture start buttons"), IntentText("可同时开启多个按键，最先按下的启动键控制本次绘制。", "Enable multiple buttons; the first pressed start button controls the drawing."), panel), "mouse-gesture-buttons"));
        return root;
    }
}
