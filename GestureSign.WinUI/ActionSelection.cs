using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace GestureSign.WinUI;

internal static class ActionSelection
{
    public static LegacyAction? Resolve(IReadOnlyList<LegacyApplication> applications,
        LegacyApplication owner, LegacyAction action)
    {
        var live = applications.SelectMany(app => app.Actions)
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Source, action.Source));
        if (live is not null) return live;

        // Match within the original application, never by an action name alone
        // or by its list position (which can change after insertions/deletions).
        var owners = applications.Where(app =>
            app.Name == owner.Name && app.Group == owner.Group && app.Type == owner.Type &&
            app.MatchUsing == owner.MatchUsing && app.MatchString == owner.MatchString &&
            app.IsRegEx == owner.IsRegEx).ToArray();
        if (owners.Length != 1) return null;
        var matches = owners[0].Actions.Where(candidate =>
            JsonNode.DeepEquals(candidate.Source, action.Source)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
