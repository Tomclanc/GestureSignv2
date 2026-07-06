using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Xml.Linq;

namespace GestureSign.WinUI;

internal sealed class LegacyDataStore
{
    private const string ActionsFileName = "Actions.gsa";
    private const string GesturesFileName = "Gestures.gest";
    private const string ConfigFileName = "GestureSign.config";
    private const string ApplicationDataFolderName = "GestureSign V2";
    private const string DataLocationFileName = "DataLocation.txt";
    private const string OneDriveDataLocationValue = "OneDrive";
    private static readonly string[] BrowserExecutableAliases =
    [
        "MicrosoftEdge",
        "msedge",
        "msedge.exe",
        "msedgewebview2",
        "MicrosoftEdgeCP",
        "firefox",
        "chrome",
        "iexplore"
    ];

    private readonly JsonArray _actionsRoot;
    private readonly JsonArray _gesturesRoot;
    private readonly string _defaultsPath;

    public IReadOnlyList<LegacyApplication> Applications { get; private init; } = [];
    public IReadOnlyList<LegacyGesture> Gestures { get; private init; } = [];
    public LegacyOptions Options { get; private init; } = new();
    public string DataSource { get; private init; } = "Defaults";
    public string RoamingPath { get; private init; } = "";
    public string LocalPath { get; private init; } = "";
    public string? ActionsPath { get; private init; }
    public string? GesturesPath { get; private init; }
    public string? ConfigPath { get; private init; }
    public bool OneDriveSyncEnabled { get; private init; }
    public string? OneDriveSyncPath { get; private init; }

    private LegacyDataStore(JsonArray actionsRoot, JsonArray gesturesRoot, string defaultsPath)
    {
        _actionsRoot = actionsRoot;
        _gesturesRoot = gesturesRoot;
        _defaultsPath = defaultsPath;
    }

    public static LegacyDataStore Load()
    {
        var applicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var defaultRoamingPath = Path.Combine(applicationDataPath, ApplicationDataFolderName);
        var oneDrivePath = GetOneDriveApplicationDataPath();
        var oneDriveSyncEnabled = IsOneDriveSyncEnabled();
        var roamingPath = oneDriveSyncEnabled && !string.IsNullOrWhiteSpace(oneDrivePath)
            ? oneDrivePath
            : defaultRoamingPath;
        var legacyRoamingPath = Path.Combine(applicationDataPath, "GestureSign");
        EnsureRoamingDataMigrated(legacyRoamingPath, roamingPath);
        if (!string.Equals(roamingPath, defaultRoamingPath, StringComparison.OrdinalIgnoreCase))
            EnsureRoamingDataMigrated(defaultRoamingPath, roamingPath);
        var portablePath = Path.Combine(AppContext.BaseDirectory, "AppData");
        var defaultsPath = FirstExistingDirectory(
            Path.Combine(AppContext.BaseDirectory, "Defaults"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "GestureSign.ControlPanel", "Defaults")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "GestureSign.ControlPanel", "Defaults")))
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "GestureSign.ControlPanel", "Defaults"));

        var actionsPath = FirstExisting(
            Path.Combine(roamingPath, ActionsFileName),
            Path.Combine(portablePath, ActionsFileName),
            Path.Combine(defaultsPath, ActionsFileName));

        var gesturesPath = FirstExisting(
            Path.Combine(roamingPath, GesturesFileName),
            Path.Combine(portablePath, GesturesFileName),
            Path.Combine(defaultsPath, GesturesFileName));

        var configPath = FirstExisting(
            Path.Combine(roamingPath, ConfigFileName),
            Path.Combine(portablePath, ConfigFileName));

        var actionsRoot = LoadJsonArray(actionsPath);
        var gesturesRoot = LoadJsonArray(gesturesPath);
        var changedActions = NormalizeBuiltInApplications(actionsRoot);
        changedActions |= NormalizeMatchUsingValues(actionsRoot);
        changedActions |= EnsureSettingsWindowIgnored(actionsRoot);
        changedActions |= NormalizeEdgeCommandPlugins(actionsRoot);
        var changedGestures = NormalizeGestureNames(gesturesRoot, actionsRoot, out var changedGestureActions);
        var actionsFromDefaults = IsDefaultsPath(actionsPath);
        var gesturesFromDefaults = IsDefaultsPath(gesturesPath);
        if ((changedActions || changedGestureActions) && actionsPath is not null && !actionsFromDefaults)
            SaveJsonArray(actionsPath, actionsRoot);
        if (changedGestures && gesturesPath is not null && !gesturesFromDefaults)
            SaveJsonArray(gesturesPath, gesturesRoot);

        return new LegacyDataStore(actionsRoot, gesturesRoot, defaultsPath)
        {
            Applications = LoadApplications(actionsRoot),
            Gestures = LoadGestures(gesturesRoot),
            Options = LoadOptions(configPath),
            RoamingPath = roamingPath,
            LocalPath = LocalApplicationDataPath(),
            ActionsPath = actionsPath,
            GesturesPath = gesturesPath,
            ConfigPath = configPath,
            OneDriveSyncEnabled = oneDriveSyncEnabled,
            OneDriveSyncPath = oneDrivePath,
            DataSource = actionsPath is null || actionsFromDefaults
                ? "默认配置"
                : "用户配置"
        };
    }

    public static bool CanUseOneDriveSync() => !string.IsNullOrWhiteSpace(GetOneDriveApplicationDataPath());

    public static bool IsOneDriveSyncEnabled()
    {
        try
        {
            var path = Path.Combine(LocalApplicationDataPath(), DataLocationFileName);
            return File.Exists(path)
                && string.Equals(File.ReadAllText(path).Trim(), OneDriveDataLocationValue, StringComparison.OrdinalIgnoreCase)
                && CanUseOneDriveSync();
        }
        catch
        {
            return false;
        }
    }

    public void SetOneDriveSyncEnabled(bool enabled)
    {
        var targetPath = enabled
            ? GetOneDriveApplicationDataPath() ?? throw new InvalidOperationException("未检测到 OneDrive 文件夹。")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationDataFolderName);

        CopyConfigurationFiles(targetPath, ActionsPath, GesturesPath, ConfigPath);
        WriteDataLocation(enabled);
    }

    public static string? GetOneDriveApplicationDataPath()
    {
        var root = GetOneDriveRootPath();
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, "Apps", ApplicationDataFolderName);
    }

    private static string? GetOneDriveRootPath()
    {
        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var path = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                return path;
        }

        return null;
    }

    private static string LocalApplicationDataPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationDataFolderName);

    private static void WriteDataLocation(bool oneDrive)
    {
        var localPath = LocalApplicationDataPath();
        Directory.CreateDirectory(localPath);
        var path = Path.Combine(localPath, DataLocationFileName);
        if (oneDrive)
            File.WriteAllText(path, OneDriveDataLocationValue);
        else if (File.Exists(path))
            File.Delete(path);
    }

    private static void CopyConfigurationFiles(string targetPath, string? actionsPath, string? gesturesPath, string? configPath)
    {
        Directory.CreateDirectory(targetPath);
        CopyConfigurationFile(actionsPath, Path.Combine(targetPath, ActionsFileName));
        CopyConfigurationFile(gesturesPath, Path.Combine(targetPath, GesturesFileName));
        CopyConfigurationFile(configPath, Path.Combine(targetPath, ConfigFileName));
    }

    private static void CopyConfigurationFile(string? sourcePath, string destinationPath)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            File.Copy(sourcePath, destinationPath, true);
    }

    private static void EnsureRoamingDataMigrated(string legacyPath, string targetPath)
    {
        if (!Directory.Exists(legacyPath))
            return;

        try
        {
            Directory.CreateDirectory(targetPath);
            foreach (var sourcePath in Directory.EnumerateFiles(legacyPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(legacyPath, sourcePath);
                var destinationPath = Path.Combine(targetPath, relativePath);
                if (File.Exists(destinationPath))
                    continue;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    File.Copy(sourcePath, destinationPath, false);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public string CreateBackup()
    {
        var backupRoot = Path.Combine(LocalPath, "Backup");
        Directory.CreateDirectory(backupRoot);

        var backupPath = Path.Combine(backupRoot, $"GestureSign-{DateTime.Now:yyyyMMdd-HHmmss}.ges");
        using var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create);
        AddIfExists(archive, ActionsPath, ActionsFileName);
        AddIfExists(archive, GesturesPath, GesturesFileName);
        AddIfExists(archive, ConfigPath, ConfigFileName);
        return backupPath;
    }

    public void AddUserApplication(string name, int matchUsing, string matchString, string group, bool isRegex)
    {
        _actionsRoot.Add(new JsonObject
        {
            ["$type"] = "GestureSign.Common.Applications.UserApp, GestureSign.Common",
            ["LimitNumberOfFingers"] = 2,
            ["BlockTouchInputThreshold"] = 1,
            ["Name"] = name,
            ["MatchUsing"] = matchUsing,
            ["MatchString"] = matchString,
            ["IsRegEx"] = isRegex,
            ["Group"] = group,
            ["Actions"] = new JsonArray()
        });
        SaveActions();
    }

    public void AddIgnoredApplication(string name, int matchUsing, string matchString, bool isRegex)
    {
        _actionsRoot.Add(new JsonObject
        {
            ["$type"] = "GestureSign.Common.Applications.IgnoredApp, GestureSign.Common",
            ["Name"] = name,
            ["MatchUsing"] = matchUsing,
            ["MatchString"] = matchString,
            ["IsRegEx"] = isRegex,
            ["IsEnabled"] = true
        });
        SaveActions();
    }

    public void EnsureGlobalApplication()
    {
        if (_actionsRoot.OfType<JsonObject>().Any(app => app.StringValue("$type", "").Contains("GlobalApp", StringComparison.OrdinalIgnoreCase)))
            return;

        _actionsRoot.Insert(0, new JsonObject
        {
            ["$type"] = "GestureSign.Common.Applications.GlobalApp, GestureSign.Common",
            ["Name"] = "(全局动作)",
            ["MatchUsing"] = 4,
            ["IsEnabled"] = true,
            ["Actions"] = new JsonArray()
        });
        SaveActions();
    }

    public void DeleteApplication(LegacyApplication application)
    {
        _actionsRoot.Remove(application.Source);
        SaveActions();
    }

    public void UpdateApplication(LegacyApplication application, string name, int matchUsing, string matchString, string group, bool isRegex, bool isEnabled, int limitNumberOfFingers, int blockTouchInputThreshold)
    {
        application.Source["Name"] = name;
        application.Source["MatchUsing"] = matchUsing;
        application.Source["MatchString"] = matchString;
        application.Source["IsRegEx"] = isRegex;
        application.Source["IsEnabled"] = isEnabled;
        if (application.Type != "忽略")
        {
            application.Source["Group"] = group;
            application.Source["LimitNumberOfFingers"] = limitNumberOfFingers;
            application.Source["BlockTouchInputThreshold"] = blockTouchInputThreshold;
        }
        SaveActions();
    }

    public void AddAction(LegacyApplication application, string name, string gestureName)
    {
        var actions = GetOrCreateArray(application.Source, "Actions");
        actions.Add(new JsonObject
        {
            ["Name"] = name,
            ["GestureName"] = gestureName,
            ["Condition"] = "",
            ["IsEnabled"] = true,
            ["Commands"] = new JsonArray()
        });
        SaveActions();
    }

    public void UpdateAction(LegacyAction action, string name, string gestureName, string condition, bool isEnabled, bool activateWindow, int mouseHotkey, int ignoredDevices, string hotkeyJson, string continuousGestureJson)
    {
        action.Source["Name"] = name;
        action.Source["GestureName"] = gestureName;
        action.Source["Condition"] = condition;
        action.Source["IsEnabled"] = isEnabled;
        action.Source["ActivateWindow"] = activateWindow;
        action.Source["MouseHotkey"] = mouseHotkey;
        action.Source["IgnoredDevices"] = ignoredDevices;
        action.Source["Hotkey"] = ParseJsonObjectOrNull(hotkeyJson);
        action.Source["ContinuousGesture"] = ParseJsonObjectOrNull(continuousGestureJson);
        SaveActions();
    }

    public void DeleteAction(LegacyApplication application, LegacyAction action)
    {
        if (application.Source["Actions"] is JsonArray actions)
        {
            actions.Remove(action.Source);
            SaveActions();
        }
    }

    public void AddCommand(LegacyAction action, string name, string pluginClass, string settings)
    {
        var commands = GetOrCreateArray(action.Source, "Commands");
        commands.Clear();
        commands.Add(new JsonObject
        {
            ["CommandSettings"] = settings,
            ["Name"] = name,
            ["PluginClass"] = pluginClass,
            ["PluginFilename"] = "GestureSign.CorePlugins.dll",
            ["IsEnabled"] = true
        });
        SaveActions();
    }

    public void NormalizeSingleCommandPerAction(bool preferLast = false)
    {
        foreach (var action in _actionsRoot.OfType<JsonObject>()
                     .SelectMany(app => app["Actions"] as JsonArray ?? [])
                     .OfType<JsonObject>())
        {
            if (action["Commands"] is not JsonArray commands || commands.Count <= 1)
                continue;

            var selected = (preferLast ? commands.LastOrDefault() : commands.FirstOrDefault())?.DeepClone();
            commands.Clear();
            if (selected is not null)
                commands.Add(selected);
        }
        SaveActions();
    }

    public void UpdateCommand(LegacyCommand command, string name, string pluginClass, string settings, bool isEnabled)
    {
        command.Source["Name"] = name;
        command.Source["PluginClass"] = pluginClass;
        command.Source["PluginFilename"] = "GestureSign.CorePlugins.dll";
        command.Source["CommandSettings"] = settings;
        command.Source["IsEnabled"] = isEnabled;
        SaveActions();
    }

    public void DeleteCommand(LegacyAction action, LegacyCommand command)
    {
        if (action.Source["Commands"] is JsonArray commands)
        {
            commands.Remove(command.Source);
            SaveActions();
        }
    }

    public void SetEnabled(JsonObject source, bool enabled)
    {
        source["IsEnabled"] = enabled;
        SaveActions();
    }

    public void ImportActions(string path)
    {
        var incoming = LoadJsonArray(path);
        foreach (var item in incoming.Select(node => node?.DeepClone()).OfType<JsonObject>())
            _actionsRoot.Add(item);
        SaveActions();
    }

    public void ImportGestures(string path)
    {
        var existingNames = _gesturesRoot.OfType<JsonObject>()
            .Select(node => node.StringValue("Name", ""))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var incoming = LoadJsonArray(path);
        foreach (var item in incoming.Select(node => node?.DeepClone()).OfType<JsonObject>())
        {
            var name = item.StringValue("Name", "");
            if (!existingNames.Contains(name))
                _gesturesRoot.Add(item);
        }
        SaveGestures();
    }

    public void AddGesture(string name, int fingerCount, string direction)
    {
        _gesturesRoot.Add(new JsonObject
        {
            ["Name"] = GetUniqueGestureName(name),
            ["PointPatterns"] = new JsonArray(new JsonObject
            {
                ["Points"] = CreateGesturePoints(Math.Clamp(fingerCount, 1, 5), direction)
            })
        });
        SaveGestures();
    }

    public void AddGestureFromPoints(string name, int fingerCount, IReadOnlyList<(double X, double Y)> points)
    {
        _gesturesRoot.Add(new JsonObject
        {
            ["Name"] = GetUniqueGestureName(name),
            ["PointPatterns"] = new JsonArray(new JsonObject
            {
                ["Points"] = CreateGesturePoints(Math.Clamp(fingerCount, 1, 5), points)
            })
        });
        SaveGestures();
    }

    public void AddGestureFromPointPatterns(string name, IReadOnlyList<IReadOnlyList<(double X, double Y)>> pointPatterns)
    {
        var points = new JsonArray();
        foreach (var pattern in pointPatterns)
        {
            var line = new JsonArray();
            foreach (var point in pattern)
                line.Add($"{Math.Round(point.X)}, {Math.Round(point.Y)}");
            points.Add(line);
        }

        _gesturesRoot.Add(new JsonObject
        {
            ["Name"] = GetUniqueGestureName(name),
            ["PointPatterns"] = new JsonArray(new JsonObject { ["Points"] = points })
        });
        SaveGestures();
    }

    public void UpdateGesturePoints(LegacyGesture gesture, int fingerCount, IReadOnlyList<(double X, double Y)> points)
    {
        gesture.Source["PointPatterns"] = new JsonArray(new JsonObject
        {
            ["Points"] = CreateGesturePoints(Math.Clamp(fingerCount, 1, 5), points)
        });
        SaveGestures();
    }

    public void UpdateGesturePointPatterns(LegacyGesture gesture, IReadOnlyList<IReadOnlyList<(double X, double Y)>> pointPatterns)
    {
        var points = new JsonArray();
        foreach (var pattern in pointPatterns)
        {
            var line = new JsonArray();
            foreach (var point in pattern)
                line.Add($"{Math.Round(point.X)}, {Math.Round(point.Y)}");
            points.Add(line);
        }

        gesture.Source["PointPatterns"] = new JsonArray(new JsonObject { ["Points"] = points });
        SaveGestures();
    }

    public void RenameGesture(LegacyGesture gesture, string name)
    {
        var oldName = gesture.Name;
        var newName = GetUniqueGestureName(name, oldName);
        gesture.Source["Name"] = newName;

        foreach (var action in _actionsRoot.OfType<JsonObject>()
                     .SelectMany(app => app["Actions"] as JsonArray ?? [])
                     .OfType<JsonObject>()
                     .Where(action => string.Equals(action.StringValue("GestureName", ""), oldName, StringComparison.OrdinalIgnoreCase)))
            action["GestureName"] = newName;

        SaveGestures();
        SaveActions();
    }

    public void DeleteGesture(LegacyGesture gesture)
    {
        _gesturesRoot.Remove(gesture.Source);
        SaveGestures();
    }

    public string ExportActions(string path)
    {
        File.WriteAllText(path, _actionsRoot.ToJsonString(JsonOptions()));
        return path;
    }

    public string ExportGestures(string path)
    {
        File.WriteAllText(path, _gesturesRoot.ToJsonString(JsonOptions()));
        return path;
    }

    public void RestoreArchive(string archivePath)
    {
        Directory.CreateDirectory(RoamingPath);
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries.Where(entry => entry.Name is ActionsFileName or GesturesFileName or ConfigFileName))
        {
            var destination = Path.Combine(RoamingPath, entry.Name);
            entry.ExtractToFile(destination, true);
        }
    }

    public void UpdateOption(string key, string value)
    {
        Directory.CreateDirectory(RoamingPath);
        var configPath = Path.Combine(RoamingPath, ConfigFileName);
        RetryConfigFileAccess(() =>
        {
            var document = LoadConfigDocument(configPath);

            var appSettings = document.Root?.Element("appSettings");
            if (appSettings is null)
            {
                appSettings = new XElement("appSettings");
                document.Root?.Add(appSettings);
            }

            var item = appSettings.Elements("add").FirstOrDefault(node => string.Equals(node.Attribute("key")?.Value, key, StringComparison.OrdinalIgnoreCase));
            if (item is null)
                appSettings.Add(new XElement("add", new XAttribute("key", key), new XAttribute("value", value)));
            else
                item.SetAttributeValue("value", value);

            SaveConfigDocument(configPath, document);
        });
    }

    public void SaveActions()
    {
        Directory.CreateDirectory(RoamingPath);
        File.WriteAllText(Path.Combine(RoamingPath, ActionsFileName), _actionsRoot.ToJsonString(JsonOptions()));
    }

    public void SaveGestures()
    {
        Directory.CreateDirectory(RoamingPath);
        File.WriteAllText(Path.Combine(RoamingPath, GesturesFileName), _gesturesRoot.ToJsonString(JsonOptions()));
    }

    private static void SaveJsonArray(string path, JsonArray array)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, array.ToJsonString(JsonOptions()));
    }

    private static XDocument LoadConfigDocument(string configPath)
    {
        if (!File.Exists(configPath))
            return XDocument.Parse("<?xml version=\"1.0\" encoding=\"utf-8\" ?><configuration><appSettings /></configuration>");

        using var stream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return XDocument.Load(stream);
    }

    private static void SaveConfigDocument(string configPath, XDocument document)
    {
        var tempPath = configPath + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                document.Save(stream);
            }

            if (File.Exists(configPath))
                File.Replace(tempPath, configPath, null, true);
            else
                File.Move(tempPath, configPath);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }

    private static void RetryConfigFileAccess(Action action)
    {
        const int maxAttempts = 20;
        var delay = 40;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(delay);
                delay = Math.Min(delay + 30, 250);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(delay);
                delay = Math.Min(delay + 30, 250);
            }
        }
    }

    private static void AddIfExists(ZipArchive archive, string? sourcePath, string entryName)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            archive.CreateEntryFromFile(sourcePath, entryName, CompressionLevel.Optimal);
    }

    private static string? FirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);

    private static string? FirstExistingDirectory(params string[] paths) => paths.FirstOrDefault(Directory.Exists);

    private static JsonArray LoadJsonArray(string? path)
    {
        try
        {
            return path is not null && File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray root ? root : [];
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<LegacyApplication> LoadApplications(JsonArray root)
        => root.OfType<JsonObject>().Select(app =>
        {
            var actions = app["Actions"] as JsonArray;
            return new LegacyApplication
            {
                Source = app,
                Name = app.StringValue("Name", "(未命名程序)"),
                Group = app.StringValue("Group", ""),
                Type = AppType(app.StringValue("$type", "")),
                MatchUsing = app.IntValue("MatchUsing", 0),
                MatchString = app.StringValue("MatchString", ""),
                IsRegEx = app.BoolValue("IsRegEx", false),
                IsEnabled = app.BoolValue("IsEnabled", true),
                LimitNumberOfFingers = app.IntValue("LimitNumberOfFingers", 2),
                BlockTouchInputThreshold = app.IntValue("BlockTouchInputThreshold", 1),
                Actions = actions?.OfType<JsonObject>().Select(ReadAction).ToList() ?? []
            };
        }).ToList();

    private static bool NormalizeMatchUsingValues(JsonArray root)
    {
        var changed = false;
        foreach (var app in root.OfType<JsonObject>())
        {
            if (app.IntValue("MatchUsing", 0) != 3)
                continue;

            app["MatchUsing"] = 0;
            changed = true;
        }

        return changed;
    }

    private static LegacyAction ReadAction(JsonObject action)
    {
        var commands = action["Commands"] as JsonArray;
        return new LegacyAction
        {
            Source = action,
            Name = action.StringValue("Name", "(未命名动作)"),
            GestureName = action.StringValue("GestureName", ""),
            Condition = action.StringValue("Condition", ""),
            IsEnabled = action.BoolValue("IsEnabled", true),
            ActivateWindow = action.BoolValue("ActivateWindow", true),
            MouseHotkey = action.IntValue("MouseHotkey", 0),
            IgnoredDevices = action.IntValue("IgnoredDevices", 0),
            HotkeyJson = action["Hotkey"]?.ToJsonString(JsonOptions()) ?? "",
            ContinuousGestureJson = action["ContinuousGesture"]?.ToJsonString(JsonOptions()) ?? "",
            Commands = commands?.OfType<JsonObject>().Select(command => new LegacyCommand
            {
                Source = command,
                Name = command.StringValue("Name", "(未命名命令)"),
                Settings = command.StringValue("CommandSettings", ""),
                PluginClass = command.StringValue("PluginClass", ""),
                PluginFilename = command.StringValue("PluginFilename", ""),
                IsEnabled = command.BoolValue("IsEnabled", true)
            }).ToList() ?? []
        };
    }

    private static IReadOnlyList<LegacyGesture> LoadGestures(JsonArray root)
        => root.OfType<JsonObject>().Select(gesture =>
            {
                var pointPatterns = ParsePointPatterns(gesture);
                var patternCount = pointPatterns.Count;

                return new LegacyGesture
                {
                    Source = gesture,
                    Name = gesture.StringValue("Name", "(未命名手势)"),
                    FingerCount = Math.Max(1, patternCount),
                    PointPatterns = pointPatterns
                };
            }).ToList();

    private static IReadOnlyList<IReadOnlyList<(double X, double Y)>> ParsePointPatterns(JsonObject gesture)
    {
        if (gesture["PointPatterns"] is not JsonArray patterns ||
            patterns.FirstOrDefault()?["Points"] is not JsonArray points)
            return [];

        return points.OfType<JsonArray>()
            .Select(line => line.Select(ParsePoint).Where(point => point is not null).Select(point => point!.Value).ToList())
            .Where(line => line.Count > 0)
            .ToList();
    }

    private static bool NormalizeBuiltInApplications(JsonArray root)
    {
        var changed = false;
        foreach (var app in root.OfType<JsonObject>())
        {
            var name = app.StringValue("Name", "");
            var matchString = app.StringValue("MatchString", "");
            if (name.StartsWith("Browsers(", StringComparison.OrdinalIgnoreCase) ||
                matchString.Contains("firefox", StringComparison.OrdinalIgnoreCase) &&
                matchString.Contains("chrome", StringComparison.OrdinalIgnoreCase))
            {
                if (name != "浏览器")
                {
                    app["Name"] = "浏览器";
                    changed = true;
                }

                if (string.Equals(app.StringValue("Group", ""), "Internet", StringComparison.OrdinalIgnoreCase))
                {
                    app["Group"] = "";
                    changed = true;
                }

                if (EnsureBrowserExecutableAliases(app, matchString))
                    changed = true;
            }
        }

        return changed;
    }

    private static bool EnsureBrowserExecutableAliases(JsonObject app, string matchString)
    {
        var aliases = matchString
            .Split(['|', ';', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(alias => alias.Trim())
            .Where(alias => alias.Length != 0)
            .ToList();
        var aliasSet = aliases.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var alias in BrowserExecutableAliases)
        {
            if (aliasSet.Contains(alias))
                continue;

            aliases.Add(alias);
            aliasSet.Add(alias);
            changed = true;
        }

        if (changed)
            app["MatchString"] = string.Join("|", aliases);

        return changed;
    }

    private static bool EnsureSettingsWindowIgnored(JsonArray root)
    {
        var hasIgnoredSettingsWindow = root.OfType<JsonObject>().Any(app =>
            app.StringValue("$type", "").Contains("IgnoredApp", StringComparison.Ordinal)
            && string.Equals(app.StringValue("MatchString", ""), "GestureSign.WinUI.exe", StringComparison.OrdinalIgnoreCase));
        if (hasIgnoredSettingsWindow)
            return false;

        root.Add(new JsonObject
        {
            ["$type"] = "GestureSign.Common.Applications.IgnoredApp, GestureSign.Common",
            ["Name"] = "GestureSign V2",
            ["MatchUsing"] = 2,
            ["MatchString"] = "GestureSign.WinUI.exe",
            ["IsRegEx"] = false,
            ["IsEnabled"] = true
        });
        return true;
    }

    private static bool NormalizeGestureNames(JsonArray gesturesRoot, JsonArray actionsRoot, out bool changedActions)
    {
        var changedGestures = false;
        changedActions = false;
        var nameMap = GestureNameMap();
        var existingNames = gesturesRoot.OfType<JsonObject>()
            .Select(gesture => gesture.StringValue("Name", ""))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var gesture in gesturesRoot.OfType<JsonObject>())
        {
            var oldName = gesture.StringValue("Name", "");
            if (!nameMap.TryGetValue(oldName, out var newName) || string.Equals(oldName, newName, StringComparison.Ordinal))
                continue;

            if (existingNames.Contains(newName))
                continue;

            gesture["Name"] = newName;
            existingNames.Remove(oldName);
            existingNames.Add(newName);
            changedGestures = true;
        }

        foreach (var action in actionsRoot.OfType<JsonObject>()
                     .SelectMany(app => app["Actions"] as JsonArray ?? [])
                     .OfType<JsonObject>())
        {
            var oldGestureName = action.StringValue("GestureName", "");
            if (nameMap.TryGetValue(oldGestureName, out var newGestureName))
            {
                action["GestureName"] = newGestureName;
                changedActions = true;
            }
        }

        return changedGestures;
    }

    private static bool NormalizeEdgeCommandPlugins(JsonArray actionsRoot)
    {
        var changed = false;
        foreach (var action in actionsRoot.OfType<JsonObject>()
                     .SelectMany(app => app["Actions"] as JsonArray ?? [])
                     .OfType<JsonObject>())
        {
            var gestureName = action.StringValue("GestureName", "");
            if (!gestureName.StartsWith("TouchScreenEdge.", StringComparison.OrdinalIgnoreCase) &&
                !gestureName.StartsWith("TouchPadEdge.", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var command in (action["Commands"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var commandName = command.StringValue("Name", "");
                var pluginClass = command.StringValue("PluginClass", "");
                if (!commandName.Contains("快捷键", StringComparison.OrdinalIgnoreCase) ||
                    !pluginClass.Contains("RunCommand", StringComparison.OrdinalIgnoreCase))
                    continue;

                command["PluginClass"] = "GestureSign.CorePlugins.HotKey.HotKeyPlugin";
                command["PluginFilename"] = "GestureSign.CorePlugins.dll";
                if (string.IsNullOrWhiteSpace(command.StringValue("CommandSettings", "")) ||
                    command.StringValue("CommandSettings", "").Contains("\"Command\"", StringComparison.OrdinalIgnoreCase))
                    command["CommandSettings"] = "{\"Windows\":false,\"Control\":true,\"Shift\":false,\"Alt\":false,\"KeyCode\":[67],\"SendByKeybdEvent\":false}";
                changed = true;
            }
        }

        return changed;
    }

    private static Dictionary<string, string> GestureNameMap() => new(StringComparer.Ordinal)
    {
        ["3Left"] = "三指左滑",
        ["3L"] = "三指L形",
        ["S"] = "S形手势",
        ["left"] = "双指左滑",
        ["Up"] = "双指上滑",
        ["Point+DownLeft"] = "双指左下滑",
        ["UpDown"] = "双指上下滑",
        ["2Left"] = "双指平行左滑",
        ["2Up2"] = "双指平行上滑",
        ["L"] = "双指L形",
        ["2UpRight"] = "双指右上滑",
        ["3Right"] = "三指右滑",
        ["2Down"] = "双指下滑",
        ["3Down"] = "三指下滑",
        ["pDown"] = "双指点按下滑",
        ["Point Right"] = "双指点按右滑",
        ["PP"] = "双指上划",
        ["3 Finger Double Tap"] = "三指双击",
        ["ee"] = "双指弧线右滑",
        ["进"] = "双指前进",
        ["5P"] = "五指点按",
        ["4LL"] = "四指L形",
        ["3p"] = "三指点按",
        ["pp"] = "双指点按",
        ["4p"] = "四指点按",
        ["5d"] = "五指下滑",
        ["8dzacl"] = "三指上滑",
        ["fv76md"] = "四指下滑",
        ["hnduoc"] = "四指右滑",
        ["qzdl61"] = "四指左滑"
    };

    private static bool IsDefaultsPath(string? path)
        => path is null || path.Contains($"{Path.DirectorySeparatorChar}Defaults{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static (double X, double Y)? ParsePoint(JsonNode? node)
    {
        if (node is null)
            return null;

        var parts = node.ToString().Split(',');
        if (parts.Length < 2)
            return null;

        return double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
               double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            ? (x, y)
            : null;
    }

    private static LegacyOptions LoadOptions(string? path)
    {
        if (path is null)
            return new LegacyOptions();

        try
        {
            var document = XDocument.Load(path);
            var settings = document.Descendants("add")
                .Where(node => node.Attribute("key") is not null)
                .ToDictionary(node => node.Attribute("key")!.Value, node => node.Attribute("value")?.Value ?? "", StringComparer.OrdinalIgnoreCase);

            return new LegacyOptions
            {
                VisualFeedbackWidth = settings.IntValue("VisualFeedbackWidth", 9),
                MinimumPointDistance = settings.IntValue("MinimumPointDistance", 20),
                Opacity = settings.DoubleValue("Opacity", 0.35),
                ShowTrayIcon = settings.BoolValue("ShowTrayIcon", true),
                SendErrorReport = settings.BoolValue("SendErrorReport", true),
                RegisterTouchPad = settings.BoolValue("RegisterTouchPad", true),
                PreferWindowsTouchPadGestures = settings.BoolValue("PreferWindowsTouchPadGestures", false),
                RegisterTouchScreen = settings.BoolValue("RegisterTouchScreen", true),
                IgnoreFullScreen = settings.BoolValue("IgnoreFullScreen", false),
                IgnoreFullScreenVideo = settings.BoolValue("IgnoreFullScreenVideo", false),
                IgnoreTouchInputWhenUsingPen = settings.BoolValue("IgnoreTouchInputWhenUsingPen", true),
                DrawingButton = NormalizeConfiguredDrawingButton(settings),
                PreferEdgeMouseGestures = settings.BoolValue("PreferEdgeMouseGestures", false),
                PenGestureButton = settings.IntValue("PenGestureButton", 0),
                InitialTimeout = settings.IntValue("InitialTimeout", 0),
                RunAsAdmin = settings.BoolValue("RunAsAdmin", false),
                VisualFeedbackColor = settings.StringValue("VisualFeedbackColor", ""),
                ShowGestureActionHint = settings.BoolValue("ShowGestureActionHint", false),
                CultureName = settings.StringValue("CultureName", ""),
                OpenSettingsHotKey = settings.StringValue("OpenSettingsHotKey", ""),
                KandoEnabled = settings.BoolValue("KandoEnabled", false),
                KandoHotKey = settings.StringValue("KandoHotKey", ""),
                KandoSettingsHotKey = settings.StringValue("KandoSettingsHotKey", ""),
                KandoExecutablePath = settings.StringValue("KandoExecutablePath", ""),
                KandoMenuName = settings.StringValue("KandoMenuName", ""),
                KandoTrigger = settings.StringValue("KandoTrigger", "")
            };
        }
        catch
        {
            return new LegacyOptions();
        }
    }

    private static string AppType(string typeName)
    {
        if (typeName.Contains("IgnoredApp", StringComparison.OrdinalIgnoreCase))
            return "忽略";
        if (typeName.Contains("GlobalApp", StringComparison.OrdinalIgnoreCase))
            return "全局";
        return "程序";
    }

    private static int NormalizeDrawingButton(int value)
        => value is 2097152 or 4194304 or 8388608 or 16777216 ? value : 0;

    private static int NormalizeConfiguredDrawingButton(IReadOnlyDictionary<string, string> settings)
        => settings.TryGetValue("DrawingButton", out var value) && int.TryParse(value, out var result)
            ? NormalizeDrawingButton(result)
            : 2097152;

    private static JsonArray GetOrCreateArray(JsonObject source, string key)
    {
        if (source[key] is JsonArray array)
            return array;

        array = [];
        source[key] = array;
        return array;
    }

    private static JsonObject? ParseJsonObjectOrNull(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try
        {
            return JsonNode.Parse(value) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    private string GetUniqueGestureName(string name, string? existingName = null)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? "NewGesture" : name.Trim();
        var names = _gesturesRoot.OfType<JsonObject>()
            .Select(node => node.StringValue("Name", ""))
            .Where(item => !string.Equals(item, existingName, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!names.Contains(baseName))
            return baseName;

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    private static JsonArray CreateGesturePoints(int fingerCount, string direction)
    {
        var (dx, dy) = direction switch
        {
            "向左" => (-240, 0),
            "向上" => (0, -240),
            "向下" => (0, 240),
            "左上" => (-180, -180),
            "右上" => (180, -180),
            "左下" => (-180, 180),
            "右下" => (180, 180),
            _ => (240, 0)
        };

        var points = new JsonArray();
        for (var finger = 0; finger < fingerCount; finger++)
        {
            var line = new JsonArray();
            var startX = 420 + finger * 110;
            var startY = 420 + finger * 24;
            for (var step = 0; step < 9; step++)
            {
                var ratio = step / 8d;
                line.Add($"{Math.Round(startX + dx * ratio)}, {Math.Round(startY + dy * ratio)}");
            }
            points.Add(line);
        }

        return points;
    }

    private static JsonArray CreateGesturePoints(int fingerCount, IReadOnlyList<(double X, double Y)> sample)
    {
        var normalized = sample.Count == 0
            ? Enumerable.Range(0, 9).Select(i => (X: 420d + i * 28, Y: 420d)).ToList()
            : sample.ToList();

        var points = new JsonArray();
        for (var finger = 0; finger < fingerCount; finger++)
        {
            var line = new JsonArray();
            var offsetX = finger * 110;
            var offsetY = finger * 24;
            foreach (var point in normalized)
                line.Add($"{Math.Round(point.X + offsetX)}, {Math.Round(point.Y + offsetY)}");
            points.Add(line);
        }

        return points;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
}

internal sealed record LegacyApplication
{
    public JsonObject Source { get; init; } = [];
    public string Name { get; init; } = "";
    public string Group { get; init; } = "";
    public string Type { get; init; } = "";
    public int MatchUsing { get; init; }
    public string MatchString { get; init; } = "";
    public bool IsRegEx { get; init; }
    public bool IsEnabled { get; init; }
    public int LimitNumberOfFingers { get; init; }
    public int BlockTouchInputThreshold { get; init; }
    public IReadOnlyList<LegacyAction> Actions { get; init; } = [];
}

internal sealed record LegacyAction
{
    public JsonObject Source { get; init; } = [];
    public string Name { get; init; } = "";
    public string GestureName { get; init; } = "";
    public string Condition { get; init; } = "";
    public bool IsEnabled { get; init; }
    public bool ActivateWindow { get; init; } = true;
    public int MouseHotkey { get; init; }
    public int IgnoredDevices { get; init; }
    public string HotkeyJson { get; init; } = "";
    public string ContinuousGestureJson { get; init; } = "";
    public IReadOnlyList<LegacyCommand> Commands { get; init; } = [];
}

internal sealed record LegacyCommand
{
    public JsonObject Source { get; init; } = [];
    public string Name { get; init; } = "";
    public string Settings { get; init; } = "";
    public string PluginClass { get; init; } = "";
    public string PluginFilename { get; init; } = "";
    public bool IsEnabled { get; init; }
}

internal sealed record LegacyGesture
{
    public JsonObject Source { get; init; } = [];
    public string Name { get; init; } = "";
    public int FingerCount { get; init; }
    public IReadOnlyList<IReadOnlyList<(double X, double Y)>> PointPatterns { get; init; } = [];
}

internal sealed record LegacyOptions
{
    public int VisualFeedbackWidth { get; init; } = 9;
    public int MinimumPointDistance { get; init; } = 20;
    public double Opacity { get; init; } = 0.35;
    public bool ShowTrayIcon { get; init; } = true;
    public bool SendErrorReport { get; init; } = true;
    public bool RegisterTouchPad { get; init; } = true;
    public bool PreferWindowsTouchPadGestures { get; init; }
    public bool RegisterTouchScreen { get; init; } = true;
    public bool IgnoreFullScreen { get; init; }
    public bool IgnoreFullScreenVideo { get; init; }
    public bool IgnoreTouchInputWhenUsingPen { get; init; } = true;
    public int DrawingButton { get; init; }
    public bool PreferEdgeMouseGestures { get; init; }
    public int PenGestureButton { get; init; }
    public int InitialTimeout { get; init; }
    public bool RunAsAdmin { get; init; }
    public string VisualFeedbackColor { get; init; } = "";
    public bool ShowGestureActionHint { get; init; }
    public string CultureName { get; init; } = "";
    public string OpenSettingsHotKey { get; init; } = "";
    public bool KandoEnabled { get; init; }
    public string KandoHotKey { get; init; } = "";
    public string KandoSettingsHotKey { get; init; } = "";
    public string KandoExecutablePath { get; init; } = "";
    public string KandoMenuName { get; init; } = "";
    public string KandoTrigger { get; init; } = "";
}

internal static class LegacyJsonExtensions
{
    public static string StringValue(this JsonObject node, string key, string defaultValue)
        => node.TryGetPropertyValue(key, out var value) ? value?.GetValue<string>() ?? defaultValue : defaultValue;

    public static int IntValue(this JsonObject node, string key, int defaultValue)
        => node.TryGetPropertyValue(key, out var value) && value is not null && int.TryParse(value.ToString(), out var result) ? result : defaultValue;

    public static bool BoolValue(this JsonObject node, string key, bool defaultValue)
        => node.TryGetPropertyValue(key, out var value) && value is not null && bool.TryParse(value.ToString(), out var result) ? result : defaultValue;

    public static string StringValue(this IReadOnlyDictionary<string, string> values, string key, string defaultValue)
        => values.TryGetValue(key, out var value) ? value : defaultValue;

    public static int IntValue(this IReadOnlyDictionary<string, string> values, string key, int defaultValue)
        => values.TryGetValue(key, out var value) && int.TryParse(value, out var result) ? result : defaultValue;

    public static double DoubleValue(this IReadOnlyDictionary<string, string> values, string key, double defaultValue)
        => values.TryGetValue(key, out var value) && double.TryParse(value, out var result) ? result : defaultValue;

    public static bool BoolValue(this IReadOnlyDictionary<string, string> values, string key, bool defaultValue)
        => values.TryGetValue(key, out var value) && bool.TryParse(value, out var result) ? result : defaultValue;
}
