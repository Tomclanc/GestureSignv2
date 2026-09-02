using Microsoft.UI.Xaml;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private UIElement BuildGesturesPageFromService() => BuildGesturesPageCore();
    private UIElement BuildIgnoredPageFromService() => BuildIgnoredPageCore();
}
