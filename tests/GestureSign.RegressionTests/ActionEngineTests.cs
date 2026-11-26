#nullable disable
using GestureSign.Foundation.RadialMenus;
using GestureSign.Foundation.Security;
using GestureSign.Foundation.Steps;

/// <summary>Step engine, action templates, radial menu rings/submenus and action packages.</summary>
internal static class ActionEngineTests
{
    /// <summary>A 1×1 PNG: enough to exercise the embedded-icon path without a file.</summary>
    private const string PixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private sealed class FakeHost : IStepHost
    {
        public readonly List<string> Calls = new();
        public string Selected = "hello";
        public string Clipboard = "";
        public string Answer = "kw";
        public string WindowTitle { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public string GetSelectedText() => Selected;
        public string GetClipboardText() => Clipboard;
        public void SetClipboardText(string text) => Calls.Add("setclip:" + text);
        public void PasteText(string text) => Calls.Add("paste:" + text);
        public void TypeText(string text) => Calls.Add("type:" + text);
        public void SendKeys(string keys) => Calls.Add("keys:" + keys);
        public string Folder = @"C:\";
        public string RunProgram(string path, string arguments, bool wait, string workingDirectory, bool runAsAdmin)
        {
            Calls.Add("run:" + path);
            if (!string.IsNullOrEmpty(arguments) || runAsAdmin) Calls.Add($"args:{arguments}|{workingDirectory}|{runAsAdmin}");
            return null;
        }
        public string LaunchOrActivate(string path, string arguments, string processName, string titleFilter, string whenActive, bool hideLaunchWindow)
        {
            Calls.Add($"switch:{path}|{arguments}|{processName}|{whenActive}");
            return "activated";
        }
        public string Http(string method, string url, string body, string contentType) => "";
        public bool QuickActionsAvailable = true;
        public bool ShowQuickActions()
        {
            Calls.Add("quickactions");
            return QuickActionsAvailable;
        }
        public void Notify(string text, string kind) => Calls.Add("notify:" + kind + ":" + text);
        public bool ShowMessageBox(string title, string text, string buttons) => true;
        public string AskText(string prompt, string defaultValue, bool multiline) => Answer;
        public IReadOnlyList<int> AskChoice(string prompt, IReadOnlyList<string> labels, bool multiple)
        {
            Calls.Add("choices:" + string.Join("|", labels));
            if (multiple) return new[] { 0, labels.Count - 1 };
            return labels.Count > 1 ? new[] { 1 } : null;
        }
        public Dictionary<string, object> FormAnswers;
        public IReadOnlyDictionary<string, object> AskForm(string title, IReadOnlyList<StepFormField> fields)
        {
            Calls.Add("form:" + string.Join(",", fields.Select(f => f.Name + "/" + f.Kind)));
            return FormAnswers;
        }
        public IReadOnlyList<string> Picked;
        public IReadOnlyList<string> PickFiles(string mode, string title, string filter, string initialPath)
        {
            Calls.Add("pick:" + mode);
            return Picked;
        }
        public IReadOnlyList<string> GetClipboardFiles() => new[] { @"C:\a.txt", @"D:\b c.txt" };
        public IReadOnlyList<string> GetSelectedFiles() => new[] { @"C:\a.txt", @"D:\b c.txt" };
        public string GetExplorerFolder() => Folder;
        public string ConvertChinese(string text, bool toTraditional) => toTraditional ? text.Replace("简", "簡") : text.Replace("簡", "简");
        public string AiChat(string endpoint, string apiKey, string model, string systemPrompt, string prompt, double? temperature)
        {
            Calls.Add("ai:" + prompt);
            return "AI(" + prompt + ")";
        }
        public void ShowText(string title, string text) => Calls.Add("show:" + text);
        public void RunCommand(string pluginClass, string settings) => Calls.Add("cmd:" + pluginClass);
        // The real host uses Jint; the templates only need these few conditions.
        public object RunJavaScript(string code, StepVariables variables, bool expression) => code switch
        {
            "text.trim() != \"\"" => variables.Text("text").Trim() != "",
            "text.trim() == \"\"" => variables.Text("text").Trim() == "",
            "text == \"\"" => variables.Text("text") == "",
            "simplified != text" => variables.Text("simplified") != variables.Text("text"),
            "text == text.toUpperCase()" => variables.Text("text") == variables.Text("text").ToUpperInvariant(),
            "link != \"\"" => variables.Text("link") != "",
            "folder != \"\"" => variables.Text("folder") != "",
            "files.length > 0" => StepVariables.ToList(variables["files"]).Count > 0,
            "status == \"unavailable\"" => variables.Text("status") == "unavailable",
            "i == 2" => (double)variables["i"] == 2d,
            "i == 4" => (double)variables["i"] == 4d,
            _ => "JS"
        };
        public object RunLua(string code, StepVariables variables) => null;
        public void Log(string message) => Calls.Add("log:" + message);

        public string ReadTextFile(string path) => FileText ?? throw new InvalidOperationException("read:" + path);
        public string FileText;
        public string WriteTextFile(string path, string content, bool append) { Calls.Add("write:" + (append ? "append:" : "") + path + "=" + content); return null; }
        public IReadOnlyDictionary<string, object> GetFileInfo(string path)
        {
            if (FileInfoResult == null) return null;
            Calls.Add("fileinfo:" + path);
            return FileInfoResult;
        }
        public Dictionary<string, object> FileInfoResult;
        public string FileOperation(string operation, string source, string destination)
        {
            Calls.Add($"fileop:{operation}|{source}|{destination}");
            return FileOperationError;
        }
        public string FileOperationError = null;
    }

    private static FakeHost RunTemplate(string id, Action<FakeHost> setup = null)
    {
        var host = new FakeHost();
        setup?.Invoke(host);
        var program = StepProgram.FromJson(ActionTemplates.Find(id).Build(true).ToJson());
        var result = new StepRunner(host).Run(program);
        if (result.Error != null) host.Calls.Add("ERROR:" + result.Error);
        return host;
    }

    private static IEnumerable<Step> Walk(IEnumerable<Step> steps)
    {
        foreach (var step in steps ?? Enumerable.Empty<Step>())
        {
            yield return step;
            foreach (var inner in Walk(step.Body)) yield return inner;
            foreach (var inner in Walk(step.Else)) yield return inner;
        }
    }

    public static void TestTemplates()
    {
        Check(ActionTemplates.All.Select(t => t.Id).Distinct().Count() == ActionTemplates.All.Count);
        foreach (var template in ActionTemplates.All)
            foreach (var step in Walk(template.Build(true).Steps))
                Check(StepCatalog.Find(step.Type) != null);

        Check(RunTemplate("search-selection", h => h.Selected = "a b").Calls.Contains("run:https://www.bing.com/search?q=a%20b"));
        var noText = RunTemplate("search-selection", h => h.Selected = " ");
        Check(noText.Calls.Any(c => c.StartsWith("notify:warn:")), "a missing selection warns");
        var canceled = RunTemplate("ask-search", h => h.Answer = null);
        Check(!canceled.Calls.Any(c => c.StartsWith("run") || c.StartsWith("ERROR")));
        Check(RunTemplate("zh-convert", h => h.Selected = "簡体").Calls.Contains("paste:简体"));
        Check(RunTemplate("toggle-case", h => h.Selected = "ABC").Calls.Contains("paste:abc"));
        Check(RunTemplate("clean-lines", h => h.Selected = "a\r\n\r\n  \r\nb\nc").Calls.Contains("paste:a\r\nb\r\nc"));
        Check(RunTemplate("copy-file-paths").Calls.Contains("setclip:C:\\a.txt\r\nD:\\b c.txt"));
        Check(RunTemplate("paste-plain", h => h.Clipboard = "plain").Calls.Contains("paste:plain"));
        var openWith = RunTemplate("open-with");
        Check(openWith.Calls.Contains(@"args: ""C:\a.txt"" ""D:\b c.txt""||False"));
        var terminal = RunTemplate("terminal-here");
        Check(terminal.Calls.Contains("run:wt.exe") && terminal.Calls.Contains(@"args:-d ""C:\\""|C:\|False"));
        var noFolder = RunTemplate("terminal-here", h => h.Folder = "");
        Check(noFolder.Calls.Contains("run:wt.exe") && !noFolder.Calls.Any(c => c.StartsWith("args:")));
        Check(RunTemplate("open-folder-with", h => h.Folder = @"D:\Work").Calls.Contains(@"args:""D:\Work""||False"));
        Check(RunTemplate("system-tool").Calls.Contains("run:ms-settings:display"));
        Check(RunTemplate("search-choose-engine", h => h.Selected = "a b").Calls.Contains("run:https://www.baidu.com/s?wd=a%20b"));
        Check(RunTemplate("compose-email").Calls.Any(c => c.StartsWith("form:to/text")));
        Check(StepPresets.ForStep(StepCatalog.RunProgram).Count > 20 && StepPresets.ForStep(StepCatalog.Http).Count == 0);
        Check(StepPresets.ForStep(StepCatalog.LaunchOrActivate).Any(p => p.Path == "code" && p.ProcessName == "Code.exe"));
        Check(StepPresets.ForStep(StepCatalog.RunProgram).All(p => p.Path.Length > 0 && p.NameZh.Length > 0 && p.NameEn.Length > 0));
        Check(RunTemplate("launch-or-switch").Calls.Contains("switch:notepad.exe|||minimize"));
        // The app-switch action defines its own processName, so the gesture's program must not leak in:
        // otherwise the step looks for the program it was triggered in and minimizes that window.
        var fromChrome = RunTemplate("launch-or-switch", h => h.ProcessName = "chrome");
        Check(fromChrome.Calls.Contains("switch:notepad.exe|||minimize"), "a step variable replaces the built-in processName");
        // Actions without that variable still know the program the gesture came from.
        var builtIn = new StepProgram
        {
            Steps = { Step.Create(StepCatalog.LaunchOrActivate) }
        };
        builtIn.Steps[0].Args["Path"] = "notepad.exe";
        builtIn.Steps[0].Args["ProcessName"] = "{processName}";
        var builtInHost = new FakeHost { ProcessName = "chrome" };
        new StepRunner(builtInHost).Run(builtIn);
        Check(builtInHost.Calls.Contains("switch:notepad.exe||chrome|minimize"), "the built-in processName is still available");

        var quickActions = RunTemplate("open-quick-actions");
        Check(quickActions.Calls.Contains("quickactions"), "the component step runs: " + string.Join(" | ", quickActions.Calls));
        Check(!quickActions.Calls.Any(c => c.StartsWith("notify:")), "an available component does not warn: " + string.Join(" | ", quickActions.Calls));
        var noComponent = RunTemplate("open-quick-actions", h => h.QuickActionsAvailable = false);
        Check(noComponent.Calls.Any(c => c.StartsWith("notify:warn:")), "a missing Quick Actions component warns: " + string.Join(" | ", noComponent.Calls));
    }

    public static void TestStepsAndVariables()
    {
        var decode = Step.Create(StepCatalog.ConvertText);
        decode.Args["Input"] = "aGVsbG8=";
        decode.Args["Mode"] = "base64Decode";
        Check(new StepRunner(new FakeHost()).Run(new StepProgram { Steps = { decode } }).Variables.Text("text") == "hello");

        var regex = Step.Create(StepCatalog.RegexExtract);
        regex.Args["Input"] = "a1 b22";
        regex.Args["Pattern"] = @"\d+";
        regex.Args["All"] = "true";
        Check(new StepRunner(new FakeHost()).Run(new StepProgram { Steps = { regex } }).Variables.Text("match") == "1\n22");

        var defaults = new StepProgram
        {
            Variables =
            {
                new StepVariableDef { Name = "n", Kind = "number", Default = "2.5" },
                new StepVariableDef { Name = "l", Kind = "list", Default = "x\ny" }
            }
        };
        var result = new StepRunner(new FakeHost()).Run(defaults);
        Check(result.Variables["n"] is double n && n == 2.5);
        Check(StepVariables.ToList(result.Variables["l"]).Count == 2);

        var group = Step.Create(StepCatalog.Group);
        var notify = Step.Create(StepCatalog.Notify);
        notify.Args["Text"] = "in group";
        group.Body.Add(notify);
        var host = new FakeHost();
        new StepRunner(host).Run(new StepProgram { Steps = { group, Step.Create(StepCatalog.Comment) } });
        Check(host.Calls.Contains("notify:info:in group"), "the default kind is info");

        // Choices written as label|value: the label is shown, the value is kept.
        var select = Step.Create(StepCatalog.UserSelect);
        select.Args["Items"] = "Bing|https://bing.com/?q=\nPlain";
        var chooser = new FakeHost();
        var chosen = new StepRunner(chooser).Run(new StepProgram { Steps = { select } });
        Check(chooser.Calls.Contains("choices:Bing|Plain") && chosen.Variables.Text("choice") == "Plain");
        select.Args["Multiple"] = "true";
        var many = new StepRunner(new FakeHost()).Run(new StepProgram { Steps = { select } });
        Check(StepVariables.ToList(many.Variables["choice"]).Select(StepVariables.ToText).SequenceEqual(new[] { "https://bing.com/?q=", "Plain" }));

        var fields = StepFormField.ParseAll("name|名称|text|x\nn|数|number|2\n\n# note\nbad name|x\nc|分类|choice|b|a, b ,c\nok|同意|check|true\nname|dup");
        Check(fields.Count == 4 && fields[2].Options.SequenceEqual(new[] { "a", "b", "c" }) && fields[1].Kind == "number");
        var form = Step.Create(StepCatalog.UserForm);
        var formHost = new FakeHost { FormAnswers = new Dictionary<string, object> { ["name"] = "Ann", ["category"] = "生活", ["note"] = "", ["pinned"] = true } };
        var filled = new StepRunner(formHost).Run(new StepProgram { Steps = { form } });
        Check(filled.Variables.Text("name") == "Ann" && filled.Variables["pinned"] is true && formHost.Calls.Contains("form:name/text,category/choice,note/multiline,pinned/check"));
        var cancelled = new StepRunner(new FakeHost()).Run(new StepProgram { Steps = { form, Step.Create(StepCatalog.Notify) } });
        Check(cancelled.Error == null && cancelled.Variables["name"] == null);

        var pick = Step.Create(StepCatalog.ChooseFile);
        pick.Args["Mode"] = "openMultiple";
        var picked = new StepRunner(new FakeHost { Picked = new[] { "a", "b" } }).Run(new StepProgram { Steps = { pick } });
        Check(StepVariables.ToList(picked.Variables["path"]).Count == 2);
        pick.Args["Mode"] = "folder";
        Check(new StepRunner(new FakeHost { Picked = new[] { @"C:\x" } }).Run(new StepProgram { Steps = { pick } }).Variables.Text("path") == @"C:\x");

        var launch = Step.Create(StepCatalog.LaunchOrActivate);
        Check(launch.Arg("WhenActive") == "minimize");
        var launched = new StepRunner(new FakeHost()).Run(new StepProgram { Steps = { launch } });
        Check(launched.Error == null && launched.Variables.Text("status") == "activated");
        Check(ActionPackage.Review(new[] { new RadialMenuAction { Name = "L", Commands = { new RadialMenuCommand { PluginClass = ActionPackage.StepsPluginClass, CommandSettings = new StepProgram { Steps = { launch } }.ToJson() } } } })
            .Any(r => r.Kind == ActionRiskKind.RunProgram && r.Detail == "notepad.exe"));

        // The editor highlights exactly the tokens the runner replaces, in the same places.
        var found = StepRunner.FindTokens("x {text} {files:quote} { {text} {{lit}} {nope");
        Check(found.Count == 3, "tokens found: " + string.Join(",", found.Select(s => $"{s.Start}:{s.Length}")));
        Check(found[0] == (2, 6) && found[1] == (9, 13), "first two tokens: " + string.Join(",", found.Select(s => $"{s.Start}:{s.Length}")));
        Check(StepRunner.FindTokens("plain text").Count == 0, "no braces means no tokens");
        Check(StepRunner.FindTokens("{\"a\":1}").Count == 0, "JSON braces are not variables");

        // Break ends the nearest loop; Continue skips to the next iteration.
        var loop = Step.Create(StepCatalog.Repeat);
        loop.Args["Count"] = "5";
        loop.Args["Index"] = "i";
        var skip = Step.Create(StepCatalog.If);
        skip.Args["Condition"] = "i == 2";
        var cont = Step.Create(StepCatalog.Continue);
        skip.Body.Add(cont);
        var stop = Step.Create(StepCatalog.If);
        stop.Args["Condition"] = "i == 4";
        var brk = Step.Create(StepCatalog.Break);
        stop.Body.Add(brk);
        var addIndex = Step.Create(StepCatalog.SetVariable);
        addIndex.Args["Name"] = "seen";
        addIndex.Args["Value"] = "{seen}{i},";
        loop.Body.Add(skip);
        loop.Body.Add(stop);
        loop.Body.Add(addIndex);
        var loopResult = new StepRunner(new FakeHost()).Run(new StepProgram { Steps = { loop } });
        Check(loopResult.Variables.Text("seen") == "0,1,3,", "break/continue: " + loopResult.Variables.Text("seen"));

        // Random number stays within [min, max); GUID uses the chosen format.
        var rand = Step.Create(StepCatalog.RandomNumber);
        rand.Args["Min"] = "10";
        rand.Args["Max"] = "20";
        var randResult = new StepRunner(new FakeHost()).Run(new StepProgram { Steps = { rand } });
        Check(randResult.Variables["random"] is double r && r >= 10 && r < 20, "random in range: " + randResult.Variables.Text("random"));
        var guid = Step.Create(StepCatalog.Guid);
        guid.Args["Format"] = "N";
        var guidResult = new StepRunner(new FakeHost()).Run(new StepProgram { Steps = { guid } });
        var guidText = guidResult.Variables.Text("guid");
        Check(System.Guid.TryParseExact(guidText, "N", out _), "guid N format: " + guidText);

        // File read/write/info/operation route through the host.
        var read = Step.Create(StepCatalog.ReadFile);
        read.Args["Path"] = @"C:\x.txt";
        var readHost = new FakeHost { FileText = "hello file" };
        var readResult = new StepRunner(readHost).Run(new StepProgram { Steps = { read } });
        Check(readResult.Variables.Text("content") == "hello file", "read file: " + readResult.Variables.Text("content"));
        var write = Step.Create(StepCatalog.WriteTextFile);
        write.Args["Path"] = @"C:\y.txt";
        write.Args["Content"] = "out";
        var writeHost = new FakeHost();
        new StepRunner(writeHost).Run(new StepProgram { Steps = { write } });
        Check(writeHost.Calls.Contains("write:C:\\y.txt=out"), "write file: " + string.Join("|", writeHost.Calls));
        var info = Step.Create(StepCatalog.FileInfo);
        info.Args["Path"] = @"C:\z.txt";
        info.Args["Field"] = "size";
        var infoHost = new FakeHost { FileInfoResult = new Dictionary<string, object> { ["size"] = 42d } };
        Check(new StepRunner(infoHost).Run(new StepProgram { Steps = { info } }).Variables.Text("result") == "42", "file info");
        var op = Step.Create(StepCatalog.FileOperation);
        op.Args["Operation"] = "copy";
        op.Args["Source"] = @"C:\a";
        op.Args["Destination"] = @"C:\b";
        var opHost = new FakeHost();
        new StepRunner(opHost).Run(new StepProgram { Steps = { op } });
        Check(opHost.Calls.Contains("fileop:copy|C:\\a|C:\\b"), "file op: " + string.Join("|", opHost.Calls));
    }

    public static void TestAiTemplates()
    {
        foreach (var id in new[] { "ai-translate", "ai-rewrite", "ai-fix", "ai-summarize", "ai-explain", "ai-ask" })
        {
            var host = RunTemplate(id);
            Check(!host.Calls.Any(c => c.StartsWith("ERROR")) && host.Calls.Any(c => c.StartsWith("ai:")));
        }
        var empty = RunTemplate("ai-rewrite", h => h.Selected = " ");
        Check(!empty.Calls.Any(c => c.StartsWith("ai:")) && empty.Calls.Any(c => c.StartsWith("notify:")));
    }

    public static void TestRingsAndSubmenus()
    {
        var config = new RadialMenuConfig();
        config.Normalize();
        config.Actions.Add(new RadialMenuAction { Id = "a1", Name = "A" });
        Check(RadialMenuConfig.SlotKey(2, 3) == "Third.3");
        Check(RadialMenuConfig.TryParseSlotKey("Third.7", out var ring, out var index) && ring == 2 && index == 7);
        Check(!RadialMenuConfig.TryParseSlotKey("Bad.1", out _, out _));
        config.GlobalMenu.Slots[RadialMenuConfig.SlotKey(2, 3)] = "a1";
        Check(!config.ResolveSlots(config.GlobalMenu, 2).ContainsKey("Third.3"));
        Check(config.ResolveSlots(config.GlobalMenu, 3).ContainsKey("Third.3"));

        var submenu = new RadialMenuDefinition { Id = "s1", Scope = RadialMenuScope.Submenu, Name = "Folder" };
        submenu.Slots["Inner.2"] = "a1";
        config.Menus.Add(submenu);
        config.GlobalMenu.Slots["Inner.0"] = RadialMenuConfig.SubmenuRef("s1");
        config.GlobalMenu.Slots["Inner.1"] = "a1";
        config.GlobalMenu.Slots["Inner.3"] = "menu:missing";
        var app = new RadialMenuDefinition { Id = "app", Scope = RadialMenuScope.Application, Processes = "x.exe" };
        config.Menus.Add(app);
        config.Normalize();

        Check(!config.GlobalMenu.Slots.ContainsKey("Inner.3"));
        Check(config.ResolveSubmenus(config.GlobalMenu, 2).ContainsKey("Inner.0"));
        Check(!config.ResolveSlots(config.GlobalMenu, 2).ContainsKey("Inner.0"));
        Check(config.ResolveSubmenus(app, 2).ContainsKey("Inner.0") && config.ResolveSlots(app, 2).ContainsKey("Inner.1"));
        Check(config.ResolveSlots(submenu, 2).Count == 1);
        app.Slots["Inner.1"] = RadialMenuConfig.SubmenuRef("s1");
        Check(!config.ResolveSlots(app, 2).ContainsKey("Inner.1"));
        Check(config.FindMenu("x.exe", "") == app && config.FindMenu("", "") == config.GlobalMenu);

        // Dragging a slot onto an empty slot moves it; onto an occupied slot it swaps.
        var global = config.GlobalMenu;
        Check(config.MoveSlot(global, "Inner.1", "Inner.5"));
        Check(!global.Slots.ContainsKey("Inner.1") && global.Slots["Inner.5"] == "a1");
        Check(config.MoveSlot(global, "Inner.0", "Inner.5"));
        Check(global.Slots["Inner.0"] == "a1" && global.Slots["Inner.5"] == "menu:s1");
        // A swap keeps both values and the submenu reference stays valid.
        Check(config.ResolveSubmenus(global, 2).ContainsKey("Inner.5"));
        Check(!config.MoveSlot(global, "Inner.5", "Inner.5"));
        Check(!config.MoveSlot(global, "Inner.7", "Inner.6"));
    }

    public static void TestActionPackage()
    {
        var config = new RadialMenuConfig();
        config.Normalize();
        var library = new RadialMenuAction
        {
            Id = "lib", Name = "Lib",
            Commands = { new RadialMenuCommand { PluginClass = ActionPackage.StepsPluginClass, CommandSettings = new StepProgram { Steps = { Step.Create(StepCatalog.RunJavaScript) } }.ToJson() } }
        };
        var ai = Step.Create(StepCatalog.AiChat);
        ai.Args["ApiKey"] = "sk-secret";
        var call = Step.Create(StepCatalog.RunCommand);
        call.Args["PluginClass"] = ActionPackage.LibraryActionPluginClass;
        call.Args["Settings"] = "{\"ActionId\":\"lib\"}";
        var main = new RadialMenuAction
        {
            Id = "main", Name = "Main", Icon = @"C:\x\a.png",
            Commands = { new RadialMenuCommand { PluginClass = ActionPackage.StepsPluginClass, CommandSettings = new StepProgram { Steps = { ai, call } }.ToJson() } }
        };
        config.Actions.Add(library);
        config.Actions.Add(main);

        var package = ActionPackage.Create(config, new[] { main });
        Check(package.Actions.Count == 2);
        Check(!package.ToJson().Contains("sk-secret"));
        Check(package.Actions[0].Icon == null);
        var code = package.ToShareCode();
        Check(ActionPackage.LooksLikeShareCode(code));
        var parsed = ActionPackage.Parse(code);
        Check(parsed.Actions.Count == 2);

        var target = new RadialMenuConfig();
        target.Normalize();
        var added = parsed.ImportInto(target);
        Check(added[0].Id != "main" && added[1].Id != "lib");
        Check(ActionPackage.ReferencedActionIds(added[0]).Single() == added[1].Id);
        var risks = ActionPackage.Review(added);
        Check(risks.Any(r => r.Kind == ActionRiskKind.Ai) && risks.Any(r => r.Kind == ActionRiskKind.Script));

        var shell = new RadialMenuAction
        {
            Name = "S",
            Commands =
            {
                new RadialMenuCommand { PluginClass = "GestureSign.CorePlugins.RunCommand.RunCommandPlugin", CommandSettings = "{\"Command\":\"del x\"}" },
                new RadialMenuCommand { PluginClass = "Evil.Plugin" }
            }
        };
        var shellRisks = ActionPackage.Review(new[] { shell });
        Check(shellRisks.Count == 2 && shellRisks[0].Detail == "del x" && shellRisks[1].Kind == ActionRiskKind.CustomPlugin);

        // A picture embedded in the action is part of the package; a local path is not.
        var embedded = ActionIcons.EncodeInlineImage(Convert.FromBase64String(PixelPng), "icon.png");
        var picture = new RadialMenuAction { Id = "pic", Name = "Pic", Icon = embedded };
        config.Actions.Add(picture);
        var picturePackage = ActionPackage.Create(config, new[] { picture });
        Check(picturePackage.Actions[0].Icon == embedded, "an embedded picture is exported");
        var pictureRoundTrip = ActionPackage.Parse(picturePackage.ToShareCode());
        Check(pictureRoundTrip.Actions[0].Icon == embedded, "an embedded picture survives the share code");
        var pictureTarget = new RadialMenuConfig();
        pictureTarget.Normalize();
        var pictureAdded = pictureRoundTrip.ImportInto(pictureTarget);
        Check(pictureAdded.Count == 1 && ActionIcons.FromStored(pictureAdded[0].Icon) is { Kind: ActionIconKind.ImageData }, "an imported action keeps its picture");
        Check(ActionIcons.DecodeInlineImage(pictureAdded[0].Icon).SequenceEqual(Convert.FromBase64String(PixelPng)), "the imported picture is intact");

        var rejected = false;
        try { ActionPackage.Parse("{\"x\":1}"); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected);
    }

    public static void TestSecrets()
    {
        Check(SecretStore.Reveal("plain-key") == "plain-key");
        Check(SecretStore.Reveal(SecretStore.Protect("sk-test")) == "sk-test");
        Check(SecretStore.IsProtected(SecretStore.Protect("sk-test")) == OperatingSystem.IsWindows());
        Check(SecretStore.Protect("") == "" && SecretStore.Reveal(null) == "");
        Check(SecretStore.Reveal("dpapi:not base64!") == "");

        var ai = Step.Create(StepCatalog.AiChat);
        ai.Args["ApiKey"] = "sk-1";
        var group = Step.Create(StepCatalog.Group);
        group.Body.Add(ai);
        var program = new StepProgram { Steps = { group } };
        Check(StepSecrets.ProtectPlain(program, v => "dpapi:" + v) && ai.Args["ApiKey"] == "dpapi:sk-1");
        Check(!StepSecrets.ProtectPlain(program, v => "dpapi:" + v));
        Check(StepCatalog.Find(StepCatalog.AiChat).Parameters.Single(p => p.Key == "ApiKey").Kind == StepParameterKind.Secret);
    }

    /// <summary>The icon a library action shows: its own glyph or picture, else the program it runs.</summary>
    public static void TestActionIcons()
    {
        // Command settings are searched for the program or file that supplies the icon.
        var settings = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Path"] = "\"C:\\Windows\\System32\\notepad.exe\" /a",
            ["Url"] = "https://example.com/tool.exe",
            ["Share"] = "\\\\server\\share\\tool.exe",
            ["Nested"] = new Dictionary<string, object> { ["Path"] = @"C:\Windows" }
        });
        var referenced = ActionIcons.ReferencedPaths(settings).ToList();
        Check(referenced.Count == 2 && referenced[0].EndsWith("notepad.exe", StringComparison.OrdinalIgnoreCase), "quoted program with arguments");
        Check(referenced.Any(p => string.Equals(p, @"C:\Windows", StringComparison.OrdinalIgnoreCase)), "folder referenced by a step");

        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        var action = new RadialMenuAction
        {
            Name = "Notepad",
            Commands = { new RadialMenuCommand { PluginClass = "x", CommandSettings = System.Text.Json.JsonSerializer.Serialize(new { Path = exe }) } }
        };
        var source = ActionIcons.Resolve(action);
        Check(source is { Kind: ActionIconKind.Program } && string.Equals(source.Value, exe, StringComparison.OrdinalIgnoreCase), "program icon from the first command");

        // An action's own icon wins; an unreadable one falls back to the commands.
        Check(ActionIcons.Resolve(new RadialMenuAction { Icon = "\uE8B7" }) is { Kind: ActionIconKind.Glyph }, "glyph icon");
        Check(ActionIcons.Resolve(new RadialMenuAction { Icon = @"C:\missing\a.exe", Commands = action.Commands }) is { Kind: ActionIconKind.Program }, "unreadable icon path falls back");
        Check(ActionIcons.ForPath(Path.GetTempPath()) is { Kind: ActionIconKind.Program }, "folders have an icon too");
        Check(ActionIcons.ForPath("relative\\file.exe") is null, "relative paths are ignored");
        Check(ActionIcons.IsImageFile("a.PNG") && !ActionIcons.IsImageFile("a.exe"), "image detection");

        // A picture chosen in the editor is carried by the action itself, so it keeps
        // working after the file is moved or deleted, and across machines.
        var inline = ActionIcons.EncodeInlineImage(Convert.FromBase64String(PixelPng), "icon.png");
        Check(ActionIcons.IsInlineImage(inline), "a data uri is an embedded picture");
        var inlineSource = ActionIcons.FromStored(inline);
        Check(inlineSource is { Kind: ActionIconKind.ImageData }, "an embedded picture needs no file");
        Check(inlineSource.CacheKey == ActionIcons.FromStored(inline).CacheKey, "the cache key is stable");
        var other = ActionIcons.EncodeInlineImage(new byte[] { 1, 2, 3, 4 }, "icon.png");
        Check(inlineSource.CacheKey != ActionIcons.FromStored(other).CacheKey, "another picture gets another key");
        Check(ActionIcons.DecodeInlineImage(inline).SequenceEqual(Convert.FromBase64String(PixelPng)), "the picture bytes survive the round trip");
        Check(ActionIcons.DecodeInlineImage("data:image/png;base64,@@@") is null, "damaged data is not a picture");
        Check(ActionIcons.Resolve(new RadialMenuAction { Icon = inline }) is { Kind: ActionIconKind.ImageData }, "an action shows its embedded picture");
        Check(ActionIcons.FromStored(@"C:\missing\a.exe") is null, "a missing path still yields nothing");

        if (!OperatingSystem.IsWindows() || !File.Exists(exe))
            return;
        Check(ActionIcons.TryGetIconPixels(exe, 0, 48, out var pixels), "read a program icon");
        Check(pixels.Width >= 32 && pixels.Height == pixels.Width && pixels.Bgra.Length == pixels.Width * pixels.Height * 4, "icon pixel buffer");
        var opaque = false;
        for (var i = 3; i < pixels.Bgra.Length; i += 4)
        {
            if (pixels.Bgra[i] != 255)
                continue;
            opaque = true;
            break;
        }
        Check(opaque, "icon has opaque pixels");
        Check(!ActionIcons.TryGetIconPixels(@"C:\does\not\exist.exe", 0, 48, out _), "a missing file has no icon");
    }

    private static void Check(bool condition, string description, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (!condition) throw new InvalidOperationException($"Action engine regression failed ({description}) at ActionEngineTests.cs:{line}");
    }

    private static void Check(bool condition, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (!condition) throw new InvalidOperationException($"Action engine regression failed at ActionEngineTests.cs:{line}");
    }
}
