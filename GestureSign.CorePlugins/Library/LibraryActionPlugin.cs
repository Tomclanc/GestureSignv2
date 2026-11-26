using System;
using System.Linq;
using System.Text.Json;
using GestureSign.Common.Applications;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;

namespace GestureSign.CorePlugins.Library
{
    /// <summary>
    /// Runs an action from the action library (RadialMenus.json), so gestures,
    /// edge swipes and the radial menu can share one action definition.
    /// Settings: {"ActionId":"..."}.
    /// </summary>
    public class LibraryActionPlugin : IPlugin
    {
        public const string ClassName = "GestureSign.CorePlugins.Library.LibraryActionPlugin";
        private const int MaxDepth = 4;

        [ThreadStatic]
        private static int _depth;

        private string _actionId = "";

        public string Name => "Library action";
        public string Category => "Common";
        public string Description => _actionId;
        public bool IsAction => true;
        public object GUI => null;
        public bool ActivateWindowDefault => true;
        public object Icon => IconSource.Command;
        public IHostControl HostControl { get; set; }

        public void Initialize()
        {
        }

        public bool Gestured(PointInfo actionPoint)
        {
            var config = ApplicationManager.Instance.LoadRadialMenuConfig();
            var action = config?.Actions.FirstOrDefault(a => string.Equals(a.Id, _actionId, StringComparison.Ordinal));
            if (action == null)
            {
                Logging.LogMessage($"Library action not found: {_actionId}");
                return false;
            }
            if (!action.IsEnabled)
                return false;
            if (_depth >= MaxDepth)
            {
                Logging.LogMessage($"Library action nesting is too deep: {action.Name}");
                return false;
            }

            _depth++;
            try
            {
                var ok = true;
                foreach (var command in action.Commands.Where(c => c.IsEnabled))
                {
                    var plugin = PluginManager.Instance.FindPluginByClassAndFilename(command.PluginClass, command.PluginFilename);
                    if (plugin == null)
                    {
                        Logging.LogMessage($"Library action {action.Name}: plugin not found: {command.PluginClass}");
                        ok = false;
                        continue;
                    }
                    plugin.Plugin.Deserialize(command.CommandSettings ?? "");
                    ok &= plugin.Plugin.Gestured(actionPoint);
                }
                return ok;
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
                return false;
            }
            finally
            {
                _depth--;
            }
        }

        public bool Deserialize(string serializedData)
        {
            _actionId = "";
            if (string.IsNullOrWhiteSpace(serializedData))
                return true;
            try
            {
                using var document = JsonDocument.Parse(serializedData);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("ActionId", out var id))
                    _actionId = id.GetString() ?? "";
                return true;
            }
            catch (JsonException)
            {
                _actionId = serializedData.Trim();
                return true;
            }
        }

        public string Serialize() => JsonSerializer.Serialize(new { ActionId = _actionId });
    }
}
