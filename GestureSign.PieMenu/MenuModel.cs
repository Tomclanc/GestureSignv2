using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GestureSign.PieMenu;

internal sealed class MenuDocument
{
    public MenuTuning Advanced { get; set; } = new();
    public List<PieMenu> Menus { get; set; } = [];
    public string Palette { get; set; } = "Kando Default";
    public string WebTheme { get; set; } = "default";
    public string DarkWebTheme { get; set; } = "default";
    public string DarkPalette { get; set; } = "Dark Night";
    // Keep the legacy field so older component versions can still read exported documents.
    public string Language { get; set; } = "system";
    public bool Animations { get; set; } = true;
    public bool Marking { get; set; } = true;
    public bool Turbo { get; set; } = true;
    public bool ConfirmHover { get; set; }
    public bool CloseOnCenterRelease { get; set; }
    public bool WarpPointer { get; set; } = true;
    public string RightButton { get; set; } = "close";
    public string MiddleButton { get; set; } = "select";
}

internal sealed record MenuTuning
{
    public double ZoomFactor { get; set; } = 1;
    public double FadeInDuration { get; set; } = 75;
    public double FadeOutDuration { get; set; } = 100;
    public bool EnablePointerReactiveEffects { get; set; } = true;
    public bool EnableSelectionWedges { get; set; }
    public bool UnderlineQuickSelectKey { get; set; } = true;
    public bool DrawQuickSelectKey { get; set; }
    public bool EnableDarkModeForMenuThemes { get; set; }
    public double MaxSelectionRadius { get; set; }
    public double CenterDeadZone { get; set; } = 50;
    public double MinParentDistance { get; set; } = 150;
    public double DragThreshold { get; set; } = 15;
    public double GestureMinStrokeLength { get; set; } = 150;
    public double GestureMinStrokeAngle { get; set; } = 20;
    public double GestureJitterThreshold { get; set; } = 10;
    public double GesturePauseTimeout { get; set; } = 100;
    public double FixedStrokeLength { get; set; }
}

internal sealed class PieMenu
{
    public string Name { get; set; } = "Quick actions";
    public string Shortcut { get; set; } = "";
    public MenuConditions? Conditions { get; set; }
    public string Glyph { get; set; } = "✿";
    public string? SourceIcon { get; set; }
    public string? SourceIconTheme { get; set; }
    public bool Hover { get; set; }
    public bool Anchored { get; set; }
    public bool? UseFixedPosition { get; set; }
    public double? FixedX { get; set; }
    public double? FixedY { get; set; }
    internal bool HasFixedPosition => UseFixedPosition ?? (FixedX.HasValue && FixedY.HasValue);
    public string CenterKey { get; set; } = "Backspace";
    public List<PieAction> CenterActions { get; set; } = [];
    public List<PieItem> Items { get; set; } = [];
}

internal sealed class PieItem
{
    public string Name { get; set; } = "New item";
    public string Glyph { get; set; } = "◇";
    public string? SourceIcon { get; set; }
    public string? SourceIconTheme { get; set; }
    public double? Angle { get; set; }
    public string Key { get; set; } = "";
    public bool Submenu { get; set; }
    public List<PieItem> Children { get; set; } = [];
    public List<PieAction> Actions { get; set; } = [];
}

internal sealed record PieAction(string Type, string Value);

internal static class MenuStorage
{
    internal static string PathName { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GestureSign V2", "PieMenu", "menus.json");

    internal static void UseDirectory(string directory) =>
        PathName = Path.Combine(Path.GetFullPath(directory), "menus.json");
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        MaxDepth = 96,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static MenuDocument Load(MenuLanguage language)
    {
        if (File.Exists(PathName)) return Read(PathName);
        var example = PieMenuAssets.Editor("example-windows.json");
        if (File.Exists(example))
        {
            var menus = KandoImport.Read(example);
            void Translate(PieItem item)
            {
                item.Name = language.Text(item.Name);
                item.Children.ForEach(Translate);
            }
            foreach (var menu in menus)
            {
                menu.Name = language.Text(menu.Name);
                menu.Items.ForEach(Translate);
            }
            return new MenuDocument { Menus = menus };
        }
        return new MenuDocument
        {
            Menus =
            [
                new PieMenu
                {
                    Items =
                    [
                        Item("Files", "▣", "open-file", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                        Item("Calculator", "＋", "open-uri", "calculator:"),
                        new PieItem
                        {
                            Name = "Edit", Glyph = "✎", Submenu = true,
                            Children =
                            [
                                Item("Copy", "□", "simulate-hotkey", "Control+C"),
                                Item("Paste", "▤", "simulate-hotkey", "Control+V"),
                                Item("Undo", "↶", "simulate-hotkey", "Control+Z")
                            ]
                        },
                        Item("Settings", "⚙", "open-settings", "")
                    ]
                }
            ]
        };
    }

    private static PieItem Item(string name, string glyph, string type, string value) =>
        new() { Name = name, Glyph = glyph, Actions = [new(type, value)] };

    internal static MenuDocument Read(string path) =>
        JsonSerializer.Deserialize<MenuDocument>(ReadBounded(path), Format)
        ?? throw new InvalidDataException("The menu document is empty.");

    internal static string ReadBounded(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 2 * 1024 * 1024)
            throw new InvalidDataException("Menu files must be smaller than 2 MB.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static MenuDocument Clone(MenuDocument document) =>
        JsonSerializer.Deserialize<MenuDocument>(JsonSerializer.SerializeToUtf8Bytes(document, Format), Format)!;

    private sealed record MenuBackup(string Kind, MenuDocument Document);

    internal static void SaveBackup(MenuDocument document, string path, bool menus)
    {
        var payload = new MenuDocument();
        if (menus) payload.Menus = document.Menus;
        else CopyPreferences(document, payload);
        File.WriteAllText(path, JsonSerializer.Serialize(new MenuBackup(menus ? "menus" : "settings", payload), Format));
    }

    internal static string SettingsJson(MenuDocument document)
    {
        var payload = new MenuDocument();
        CopyPreferences(document, payload);
        return JsonSerializer.Serialize(payload, Format);
    }

    internal static MenuDocument ReadBackup(string path, bool menus)
    {
        var backup = JsonSerializer.Deserialize<MenuBackup>(ReadBounded(path), Format);
        if (backup?.Kind != (menus ? "menus" : "settings") || backup.Document is null || backup.Document.Menus is null)
            throw new InvalidDataException("Choose a matching PieMenu backup file.");
        return backup.Document;
    }

    internal static void CopyPreferences(MenuDocument source, MenuDocument target)
    {
        var incoming = source.Advanced ?? new();
        target.Advanced = incoming with { };
        target.Palette = source.Palette;
        target.WebTheme = source.WebTheme;
        target.DarkWebTheme = source.DarkWebTheme;
        target.DarkPalette = source.DarkPalette;
        target.Language = source.Language;
        target.Animations = source.Animations;
        target.Marking = source.Marking;
        target.Turbo = source.Turbo;
        target.ConfirmHover = source.ConfirmHover;
        target.CloseOnCenterRelease = source.CloseOnCenterRelease;
        target.WarpPointer = source.WarpPointer;
        target.RightButton = source.RightButton;
        target.MiddleButton = source.MiddleButton;
    }

    internal static string Save(MenuDocument document, string? target = null)
    {
        target ??= PathName;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var contents = JsonSerializer.Serialize(document, Format);
            File.WriteAllText(temporary, contents);
            File.Move(temporary, target, overwrite: true);
            return contents;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
