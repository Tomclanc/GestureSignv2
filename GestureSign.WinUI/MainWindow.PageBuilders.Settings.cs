using Microsoft.UI.Xaml;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    // Settings-page entry points are isolated from navigation. Their legacy
    // bodies remain in the shell temporarily while controls are migrated.
    private UIElement BuildOptionsPageFromService() => BuildOptionsPageCore();
    private UIElement BuildQuickActionsPageFromService() => BuildQuickActionsPageCore();
    private UIElement BuildTouchPadPageFromService() => BuildTouchPadPageCore();

    private UIElement BuildQuickActionsPageCore()
    {
        var root = NewSection();
        var options = _legacyData.Options;
        var installed = KandoComponentService.IsInstalled;
        root.Children.Add(NewKandoComponentCard());
        root.Children.Add(NewKandoPowerToysPreviewCard());
        if (installed)
        {
            root.Children.Add(NewKandoPowerToysToggleRow(options.KandoEnabled));
            root.Children.Add(NewKandoSettingsHotKeyRow(options.KandoSettingsHotKey));
            root.Children.Add(NewKandoOpenSettingsRow());
        }
        return root;
    }
}
