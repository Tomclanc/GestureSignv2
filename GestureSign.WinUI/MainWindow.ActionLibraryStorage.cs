using System;
using GestureSign.Foundation.RadialMenus;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private RadialMenuConfig? _radialConfig;
    private string RadialConfigPath => RadialMenuConfig.PathIn(_legacyData.RoamingPath);

    private RadialMenuConfig LoadRadialConfigForEditing()
    {
        try { return RadialMenuConfig.Load(RadialConfigPath) ?? new RadialMenuConfig(); }
        catch (Exception error)
        {
            LogException(error);
            return new RadialMenuConfig();
        }
    }

    private void SaveRadialConfig()
    {
        if (_radialConfig is null) return;
        try { _radialConfig.Save(RadialConfigPath); }
        catch (Exception error) { _ = ShowInfoDialog(T("保存失败", "Save failed"), error.Message); }
    }
}
