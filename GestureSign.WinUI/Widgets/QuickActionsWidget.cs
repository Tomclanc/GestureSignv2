using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Microsoft.Windows.Widgets.Providers;
using Windows.System.UserProfile;

namespace GestureSign.WinUI;

internal sealed class QuickActionsWidget(AutoResetEvent changed) : IWidgetProvider
{
    private const string Definition = "QuickActions";
    private const string Template = """
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.5",
          "rtl": "${rtl}",
          "body": [
            { "type": "TextBlock", "text": "${heading}", "size": "medium", "weight": "bolder", "wrap": true },
            { "type": "TextBlock", "text": "${description}", "isSubtle": true, "wrap": true, "maxLines": 4 }
          ],
          "actions": [
            { "type": "Action.OpenUrl", "title": "${action}", "url": "${url}" }
          ]
        }
        """;

    private readonly object _sync = new();
    private readonly HashSet<string> _pins = [];

    internal bool HasPins
    {
        get { lock (_sync) return _pins.Count != 0; }
    }

    public void CreateWidget(WidgetContext context) => Publish(context);

    public void Activate(WidgetContext context) => Publish(context);

    public void OnWidgetContextChanged(WidgetContextChangedArgs args) => Publish(args.WidgetContext);

    public void DeleteWidget(string widgetId, string customState)
    {
        lock (_sync)
        {
            _pins.Remove(widgetId);
            changed.Set();
        }
    }

    public void Deactivate(string widgetId) { }

    public void OnActionInvoked(WidgetActionInvokedArgs args) { }

    private void Publish(WidgetContext context)
    {
        if (context.DefinitionId != Definition) return;

        lock (_sync)
        {
            _pins.Add(context.Id);
            changed.Set();
            var culture = UiTranslationCatalog.ResolveCultureName(GlobalizationPreferences.Languages[0]);
            string Translate(string text) => UiTranslationCatalog.Translate(culture, text);
            var data = new
            {
                rtl = UiTranslationCatalog.IsRightToLeft(culture),
                heading = Translate("Pie menu"),
                description = Translate("Open the radial menu with dedicated shortcuts."),
                action = Translate("Quick Actions"),
                url = App.QuickActionsUri
            };
            WidgetManager.GetDefault().UpdateWidget(new WidgetUpdateRequestOptions(context.Id)
            {
                Template = Template,
                Data = JsonSerializer.Serialize(data)
            });
        }
    }
}
