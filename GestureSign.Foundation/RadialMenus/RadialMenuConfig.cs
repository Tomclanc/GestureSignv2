#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GestureSign.Foundation.RadialMenus;

/// <summary>
/// Middle-button radial menu configuration (RadialMenus.json in the data folder).
/// Actions live once in <see cref="Actions"/>; menus reference them by id from
/// their slots, so one action can appear in many menus and editing it updates
/// every menu. A per-application menu inherits each empty slot from the global menu.
/// </summary>
public sealed class RadialMenuConfig
{
    public const string FileName = "RadialMenus.json";
    public const int SlotsPerRing = 8;
    public const int MaxRings = 3;

    /// <summary>Slot value prefix for a folder slot: "menu:&lt;submenu id&gt;".</summary>
    public const string SubmenuPrefix = "menu:";
    private static readonly string[] RingNames = { "Inner", "Outer", "Third" };

    public int Version { get; set; } = 1;
    public List<RadialMenuAction> Actions { get; set; } = new();
    public List<RadialMenuDefinition> Menus { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Slot key such as "Inner.0" or "Outer.7"; slot 0 points up, clockwise.</summary>
    public static string SlotKey(bool outer, int index) => SlotKey(outer ? 1 : 0, index);

    /// <summary>Slot key of ring 0 (inner), 1 (outer) or 2 (third): "Inner.0", "Outer.3", "Third.7".</summary>
    public static string SlotKey(int ring, int index) => RingNames[Math.Clamp(ring, 0, MaxRings - 1)] + "." + index;

    /// <summary>Parses a slot key; returns false for anything else.</summary>
    public static bool TryParseSlotKey(string key, out int ring, out int index)
    {
        ring = -1;
        index = -1;
        var dot = key?.IndexOf('.') ?? -1;
        if (dot <= 0)
            return false;
        ring = Array.FindIndex(RingNames, n => string.Equals(n, key.Substring(0, dot), StringComparison.OrdinalIgnoreCase));
        return ring >= 0 && int.TryParse(key.Substring(dot + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out index)
               && index >= 0 && index < SlotsPerRing;
    }

    public static string PathIn(string dataFolder) => Path.Combine(dataFolder, FileName);

    public static RadialMenuConfig Load(string path)
    {
        if (!File.Exists(path))
            return null;
        var config = JsonSerializer.Deserialize<RadialMenuConfig>(File.ReadAllText(path), JsonOptions) ?? new RadialMenuConfig();
        config.Normalize();
        return config;
    }

    public void Save(string path)
    {
        Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        // Write to a temporary file first so a crash never leaves half a file.
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    public RadialMenuConfig Clone()
        => JsonSerializer.Deserialize<RadialMenuConfig>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions);

    public void Normalize()
    {
        Actions ??= new();
        Menus ??= new();
        foreach (var action in Actions)
        {
            if (string.IsNullOrWhiteSpace(action.Id)) action.Id = NewId();
            action.Commands ??= new();
        }
        foreach (var menu in Menus)
        {
            if (string.IsNullOrWhiteSpace(menu.Id)) menu.Id = NewId();
            menu.Slots ??= new(StringComparer.OrdinalIgnoreCase);
            if (menu.Slots.Comparer != StringComparer.OrdinalIgnoreCase)
                menu.Slots = new Dictionary<string, string>(menu.Slots, StringComparer.OrdinalIgnoreCase);
            // Drop references to deleted actions and submenus.
            foreach (var key in menu.Slots.Where(pair => !IsValidSlotValue(pair.Value)).Select(pair => pair.Key).ToList())
                menu.Slots.Remove(key);
        }
        if (!Menus.Any(m => m.Scope == RadialMenuScope.Global))
            Menus.Insert(0, new RadialMenuDefinition { Id = NewId(), Scope = RadialMenuScope.Global });
    }

    public static string NewId() => Guid.NewGuid().ToString("N");

    public static string SubmenuRef(string submenuId) => SubmenuPrefix + submenuId;

    /// <summary>True when a slot value points to a submenu ("menu:&lt;id&gt;").</summary>
    public static bool TryGetSubmenuId(string slotValue, out string submenuId)
    {
        submenuId = null;
        if (string.IsNullOrEmpty(slotValue) || !slotValue.StartsWith(SubmenuPrefix, StringComparison.Ordinal))
            return false;
        submenuId = slotValue.Substring(SubmenuPrefix.Length);
        return submenuId.Length > 0;
    }

    public RadialMenuDefinition FindMenuById(string id)
        => string.IsNullOrEmpty(id) ? null : Menus.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    private bool IsValidSlotValue(string value)
        => !string.IsNullOrEmpty(value) &&
           (TryGetSubmenuId(value, out var submenuId)
               ? FindMenuById(submenuId)?.Scope == RadialMenuScope.Submenu
               : FindAction(value) != null);

    /// <summary>The value a slot shows: its own, or for a top-level menu the global menu's.</summary>
    private string EffectiveSlotValue(RadialMenuDefinition menu, string key)
    {
        if (menu != null && menu.Slots.TryGetValue(key, out var own))
            return own;
        var global = GlobalMenu;
        if (menu != global && menu?.Scope != RadialMenuScope.Submenu && global.Slots.TryGetValue(key, out var inherited))
            return inherited;
        return null;
    }

    /// <summary>Folder slots of a menu (same inheritance as actions), keyed like <see cref="ResolveSlots(RadialMenuDefinition, int)"/>.</summary>
    public Dictionary<string, RadialMenuDefinition> ResolveSubmenus(RadialMenuDefinition menu, int ringCount)
    {
        var result = new Dictionary<string, RadialMenuDefinition>(StringComparer.OrdinalIgnoreCase);
        for (var ring = 0; ring < Math.Clamp(ringCount, 1, MaxRings); ring++)
            for (var index = 0; index < SlotsPerRing; index++)
            {
                var key = SlotKey(ring, index);
                if (TryGetSubmenuId(EffectiveSlotValue(menu, key), out var id) && FindMenuById(id) is { Scope: RadialMenuScope.Submenu } submenu)
                    result[key] = submenu;
            }
        return result;
    }

    public RadialMenuAction FindAction(string id)
        => string.IsNullOrEmpty(id) ? null : Actions.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    public RadialMenuDefinition GlobalMenu => Menus.First(m => m.Scope == RadialMenuScope.Global);

    /// <summary>
    /// The menu that applies to a window, chosen from the window's process file
    /// name and class name. Returns the global menu when nothing more specific matches.
    /// </summary>
    public RadialMenuDefinition FindMenu(string processFileName, string className)
    {
        if (IsTaskbarClass(className))
        {
            var taskbar = Menus.FirstOrDefault(m => m.Scope == RadialMenuScope.Taskbar);
            if (taskbar != null) return taskbar;
        }
        if (IsDesktopClass(className))
        {
            var desktop = Menus.FirstOrDefault(m => m.Scope == RadialMenuScope.Desktop);
            if (desktop != null) return desktop;
        }
        if (!string.IsNullOrWhiteSpace(processFileName))
        {
            var app = Menus.FirstOrDefault(m => m.Scope == RadialMenuScope.Application && m.MatchesProcess(processFileName));
            if (app != null) return app;
        }
        return GlobalMenu;
    }

    /// <summary>Effective slots of a menu: its own slots, with empty ones taken from the global menu.</summary>
    public Dictionary<string, RadialMenuAction> ResolveSlots(RadialMenuDefinition menu, bool includeOuterRing)
        => ResolveSlots(menu, includeOuterRing ? 2 : 1);

    /// <summary>Effective slots of the first <paramref name="ringCount"/> rings (1 to 3).</summary>
    public Dictionary<string, RadialMenuAction> ResolveSlots(RadialMenuDefinition menu, int ringCount)
    {
        var result = new Dictionary<string, RadialMenuAction>(StringComparer.OrdinalIgnoreCase);
        for (var ring = 0; ring < Math.Clamp(ringCount, 1, MaxRings); ring++)
        {
            for (var index = 0; index < SlotsPerRing; index++)
            {
                var key = SlotKey(ring, index);
                // A slot of the menu itself (action or folder) hides the global one.
                var value = EffectiveSlotValue(menu, key);
                var action = TryGetSubmenuId(value, out _) ? null : FindAction(value);
                if (action != null && action.IsEnabled)
                    result[key] = action;
            }
        }
        return result;
    }

    public static bool IsTaskbarClass(string className)
        => className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    public static bool IsDesktopClass(string className)
        => className is "Progman" or "WorkerW";
}

public enum RadialMenuScope
{
    Global,
    Taskbar,
    Desktop,
    Application,
    /// <summary>A folder opened from a slot of another menu; never matched to a window.</summary>
    Submenu
}

public sealed class RadialMenuDefinition
{
    public string Id { get; set; }
    public RadialMenuScope Scope { get; set; } = RadialMenuScope.Application;

    /// <summary>Display name, e.g. "Chrome".</summary>
    public string Name { get; set; }

    /// <summary>Process file names separated by ';', e.g. "anki.exe;pythonw.exe".</summary>
    public string Processes { get; set; }

    /// <summary>Full path of the program, used for its icon in the settings list.</summary>
    public string IconPath { get; set; }

    /// <summary>When set, the middle button keeps its native behavior in this application.</summary>
    public bool Disabled { get; set; }

    /// <summary>Slot key ("Inner.0".."Outer.7") to action id.</summary>
    public Dictionary<string, string> Slots { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool MatchesProcess(string processFileName)
        => !string.IsNullOrWhiteSpace(Processes) &&
           Processes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Any(p => string.Equals(p, processFileName, StringComparison.OrdinalIgnoreCase));
}

public sealed class RadialMenuAction
{
    public string Id { get; set; }

    /// <summary>Text shown on the wheel.</summary>
    public string Name { get; set; }

    /// <summary>Optional description shown in the library.</summary>
    public string Description { get; set; }

    /// <summary>Library category (free text, e.g. "文本"); empty means uncategorized.</summary>
    public string Category { get; set; }

    /// <summary>Icon: a Segoe Fluent Icons glyph (one character) or an image/exe path; empty uses the command's icon.</summary>
    public string Icon { get; set; }

    /// <summary>Id of the template this action was created from, if any.</summary>
    public string TemplateId { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Activate the target window before running; null uses each command's default.</summary>
    public bool? ActivateWindow { get; set; }

    /// <summary>Commands run in order; same format as gesture action commands.</summary>
    public List<RadialMenuCommand> Commands { get; set; } = new();
}

public sealed class RadialMenuCommand
{
    public string Name { get; set; }
    public string PluginClass { get; set; }
    public string PluginFilename { get; set; }
    public string CommandSettings { get; set; }
    public bool IsEnabled { get; set; } = true;
}
