using System;
using System.Linq;
using GestureSign.Foundation.RadialMenus;
using GestureSign.Foundation.Security;
using GestureSign.Foundation.Steps;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    /// <summary>
    /// Earlier versions stored AI API keys as plain text. On start-up, encrypt the global key
    /// and any key typed into an "Ask AI" step (library actions and gesture actions).
    /// </summary>
    private void ProtectPlainSecrets()
    {
        try
        {
            var globalKey = _legacyData.Options.AiApiKey;
            if (!string.IsNullOrEmpty(globalKey) && !SecretStore.IsProtected(globalKey))
                UpdateOptionAndReload("AiApiKey", SecretStore.Protect(globalKey));

            var radialPath = RadialMenuConfig.PathIn(_legacyData.RoamingPath);
            var radial = RadialMenuConfig.Load(radialPath);
            if (radial is not null && StepSecrets.ProtectPlain(radial))
                radial.Save(radialPath);

            var changed = false;
            foreach (var command in _legacyData.Applications.SelectMany(app => app.Actions).SelectMany(action => action.Commands)
                         .Where(command => string.Equals(command.PluginClass, StepsPluginClass, StringComparison.Ordinal)).ToList())
            {
                var updated = StepSecrets.ProtectPlainJson(command.Settings);
                if (updated is null)
                    continue;
                _legacyData.UpdateCommand(command, command.Name, command.PluginClass, updated, command.IsEnabled);
                changed = true;
            }
            if (changed)
                _legacyData = LegacyDataStore.Load();
        }
        catch (Exception ex)
        {
            LogException(ex);
        }
    }
}
