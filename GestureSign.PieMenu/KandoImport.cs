using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GestureSign.PieMenu;

internal static class KandoImport
{
    internal static List<PieMenu> Read(string path)
    {
        using var json = JsonDocument.Parse(MenuStorage.ReadBounded(path), new JsonDocumentOptions
        {
            AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 96
        });
        var menus = Children(json.RootElement, "menus");
        if (Node(json.RootElement, "root").ValueKind == JsonValueKind.Object) menus = [json.RootElement];
        var imported = menus.Select(menu =>
        {
            var root = Node(menu, "root");
            var fixedPosition = Node(menu, "fixedMenuPosition");
            var fixedMode = Node(menu, "useFixedPosition");
            var centered = Flag(menu, "centered");
            var conditions = Node(menu, "conditions");
            var area = Node(conditions, "screenArea");
            return new PieMenu
            {
                Name = Text(root, "name", "Imported menu"),
                Shortcut = Text(menu, "shortcut"),
                Conditions = conditions.ValueKind == JsonValueKind.Object ? new MenuConditions
                {
                    AppName = Text(conditions, "appName"), WindowName = Text(conditions, "windowName"),
                    XMin = Number(area, "xMin"), XMax = Number(area, "xMax"),
                    YMin = Number(area, "yMin"), YMax = Number(area, "yMax")
                } : null,
                Glyph = Glyph(root, "✿"),
                SourceIcon = OptionalText(root, "icon"),
                SourceIconTheme = OptionalText(root, "iconTheme"),
                Hover = Flag(menu, "hoverMode"),
                Anchored = Flag(menu, "anchored"),
                UseFixedPosition = fixedMode.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? fixedMode.GetBoolean() : centered,
                FixedX = Number(fixedPosition, "x") ?? (centered ? 0.5 : null),
                FixedY = Number(fixedPosition, "y") ?? (centered ? 0.5 : null),
                CenterKey = Text(Node(root, "activateWorkflow"), "quickSelectKey", "Backspace"),
                CenterActions = Children(Node(root, "activateWorkflow"), "actions").Select(action => ReadAction(action)).ToList(),
                Items = Children(root, "children").Select(ReadItem).ToList()
            };
        }).ToList();
        if (imported.Count == 0)
            throw new InvalidDataException("No Kando menus were found in this file.");
        return imported;
    }

    private static PieItem ReadItem(JsonElement item)
    {
        var type = Text(item, "type");
        var workflow = Node(item, "selectWorkflow");
        var actions = Children(workflow, "actions").Select(action => ReadAction(action)).ToList();
        if (actions.Count == 0 && type is not ("submenu" or "button" or "root"))
        {
            var data = Node(item, "data");
            actions.Add(ReadAction(data, type));
        }
        return new PieItem
        {
            Name = Text(item, "name", "Item"),
            Key = Text(workflow, "quickSelectKey", Text(Node(item, "openWorkflow"), "quickSelectKey", Text(item, "quickSelectKey"))),
            Glyph = Glyph(item, type == "submenu" ? "▸" : "◇"),
            SourceIcon = OptionalText(item, "icon"),
            SourceIconTheme = OptionalText(item, "iconTheme"),
            Angle = Number(item, "angle"),
            Submenu = type == "submenu",
            Actions = actions,
            Children = Children(item, "children").Select(ReadItem).ToList()
        };
    }

    private static string Glyph(JsonElement item, string fallback)
    {
        var icon = Text(item, "icon");
        if (icon.Length is > 0 and <= 5 && icon.Any(c => !char.IsAscii(c))) return icon;
        return icon switch
        {
            "folder" or "folder_open" => "\uE838", "settings" => "\uE713",
            "content_copy" => "\uE8C8", "content_paste" => "\uE77F",
            "undo" => "\uE7A7", "calculate" => "\uE8EF", "terminal" => "\uE756",
            "language" or "link" => "\uE774", "close" => "\uE711",
            "apps" => "\uE80A", "public" => "\uE774", "assignment" => "\uE77F",
            "select_window" => "\uE737", "folder_special" => "\uE838",
            "play_circle" => "▷", "arrow_forward" => "→", "arrow_back" => "←",
            _ => fallback
        };
    }

    private static PieAction ReadAction(JsonElement data, string? legacy = null)
    {
        var type = legacy ?? Text(data, "type");
        return type switch
        {
            "hotkey" or "simulate-hotkey" => new("simulate-hotkey", Text(data, "hotkey")),
            "file" or "open-file" => new("open-file", Text(data, "path", Text(data, "file"))),
            "uri" or "open-uri" => new("open-uri", Text(data, "uri")),
            "command" or "execute-command" => new("execute-command", Text(data, "command")),
            "text" or "set-clipboard" => new("set-clipboard", Text(data, "text")),
            "delay" => new(type, Number(data, "duration")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"),
            "open-menu" => new(type, Text(data, "menu")),
            "macro" or "execute-macro" => new("execute-macro", Node(data, "macro").ValueKind == JsonValueKind.Array ? Node(data, "macro").GetRawText() : "[]"),
            "open-settings" or "close-menu" or "close-submenu" or "inhibit-shortcuts" => new(type, ""),
            _ => new(type, data.ValueKind == JsonValueKind.Undefined ? "" : data.GetRawText())
        };
    }

    private static JsonElement Node(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var node) ? node : default;
    private static string Text(JsonElement value, string name, string fallback = "") =>
        Node(value, name) is { ValueKind: JsonValueKind.String } node ? node.GetString() ?? fallback : fallback;
    private static string? OptionalText(JsonElement value, string name)
    {
        var text = Text(value, name);
        return text.Length == 0 ? null : text;
    }
    private static double? Number(JsonElement value, string name) =>
        Node(value, name) is { ValueKind: JsonValueKind.Number } node && node.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
    private static bool Flag(JsonElement value, string name) => Node(value, name).ValueKind == JsonValueKind.True;
    private static IEnumerable<JsonElement> Children(JsonElement value, string name) =>
        Node(value, name) is { ValueKind: JsonValueKind.Array } node ? node.EnumerateArray().ToArray() : [];
}
