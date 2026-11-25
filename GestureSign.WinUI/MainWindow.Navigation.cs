using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    /// <summary>
    /// In minimal mode the back and menu buttons float over the top-left corner of the
    /// content; move the content down so they do not cover the page title.
    /// </summary>
    private void Navigation_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        NavigationContentRoot.Margin = args.DisplayMode == NavigationViewDisplayMode.Minimal
            ? new Thickness(0, 40, 0, 0)
            : new Thickness(0);
    }
}
