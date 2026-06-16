using dnlib.DotNet;
using dnlib.DotNet.Emit;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: WinUIPatch <GestureSign.WinUI.dll> [...]");
    return 2;
}

foreach (var path in args)
{
    if (args.Contains("--inspect"))
    {
        if (path == "--inspect")
            continue;
        Inspect(path);
        continue;
    }

    if (args.Contains("--inspect-edit-controls"))
    {
        if (path == "--inspect-edit-controls")
            continue;
        InspectEditControls(path);
        continue;
    }

    if (args.Contains("--inspect-color-row"))
    {
        if (path == "--inspect-color-row")
            continue;
        InspectColorRow(path);
        continue;
    }

    if (args.Contains("--inspect-recognition-card"))
    {
        if (path == "--inspect-recognition-card")
            continue;
        InspectRecognitionCard(path);
        continue;
    }

    if (args.Contains("--inspect-open-hotkey"))
    {
        if (path == "--inspect-open-hotkey")
            continue;
        InspectOpenHotKey(path);
        continue;
    }

    if (args.Contains("--inspect-daemon-recognition"))
    {
        if (path == "--inspect-daemon-recognition")
            continue;
        InspectDaemonRecognition(path);
        continue;
    }

    if (args.Contains("--mica-only"))
    {
        if (path == "--mica-only")
            continue;
        PatchMicaOnly(path);
        Console.WriteLine($"Patched Mica overlay {path}");
        continue;
    }

    if (args.Contains("--dialog-scroll-only"))
    {
        if (path == "--dialog-scroll-only")
            continue;
        PatchDialogScrollOnly(path);
        Console.WriteLine($"Patched dialog scrolling {path}");
        continue;
    }

    if (args.Contains("--move-touchpad-button-only"))
    {
        if (path == "--move-touchpad-button-only")
            continue;
        PatchMoveTouchpadButtonOnly(path);
        Console.WriteLine($"Moved touchpad button {path}");
        continue;
    }

    if (args.Contains("--hide-continuous-gesture-only"))
    {
        if (path == "--hide-continuous-gesture-only")
            continue;
        PatchHideContinuousGestureOnly(path);
        Console.WriteLine($"Hid continuous gesture box {path}");
        continue;
    }

    if (args.Contains("--steady-recognition-card-only"))
    {
        if (path == "--steady-recognition-card-only")
            continue;
        PatchSteadyRecognitionCardOnly(path);
        Console.WriteLine($"Stabilized recognition card {path}");
        continue;
    }

    if (args.Contains("--instant-color-only"))
    {
        if (path == "--instant-color-only")
            continue;
        PatchInstantColorOnly(path);
        Console.WriteLine($"Patched instant color {path}");
        continue;
    }

    if (args.Contains("--recognition-on-only"))
    {
        if (path == "--recognition-on-only")
            continue;
        PatchRecognitionOnOnly(path);
        Console.WriteLine($"Forced recognition switch on {path}");
        continue;
    }

    if (args.Contains("--open-hotkey-save-only"))
    {
        if (path == "--open-hotkey-save-only")
            continue;
        PatchOpenHotKeySaveOnly(path);
        Console.WriteLine($"Patched open-settings hotkey save {path}");
        continue;
    }

    if (args.Contains("--brand-version-only"))
    {
        if (path == "--brand-version-only")
            continue;
        PatchBrandVersionOnly(path, "8.1.9735");
        Console.WriteLine($"Patched brand/version text {path}");
        continue;
    }

    Patch(path);
    Console.WriteLine($"Patched {path}");
}

return 0;

static void Inspect(string path)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    var mainWindow = module.Find("GestureSign.WinUI.MainWindow", isReflectionName: false)
        ?? throw new InvalidOperationException("GestureSign.WinUI.MainWindow not found.");
    Console.WriteLine("Fields:");
    foreach (var field in mainWindow.Fields.Where(field =>
        field.Name.String.Contains("Root", StringComparison.OrdinalIgnoreCase)
        || field.Name.String.Contains("Backdrop", StringComparison.OrdinalIgnoreCase)
        || field.Name.String.Contains("Title", StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine($"{field.Name} : {field.FieldType.FullName}");

    Console.WriteLine("Methods:");
    foreach (var m in mainWindow.Methods.Where(method =>
        method.Name.String.Contains("ctor", StringComparison.OrdinalIgnoreCase)
        || method.Name.String.Contains("ActualTheme", StringComparison.OrdinalIgnoreCase)
        || method.Name.String.Contains("ConfigureCaption", StringComparison.OrdinalIgnoreCase)
        || method.Name.String.Contains("ShowSelected", StringComparison.OrdinalIgnoreCase)
        || method.Name.String.Contains("IsDark", StringComparison.OrdinalIgnoreCase)
        || method.Name.String.Contains("b__", StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine($"{m.Name} {m.MethodSig}");

    var ctor = mainWindow.FindMethod(".ctor") ?? throw new InvalidOperationException(".ctor not found.");
    Console.WriteLine("Ctor IL:");
    var index = 0;
    foreach (var instruction in ctor.Body.Instructions)
        Console.WriteLine($"{index++:000}: {instruction}");

    var themeChanged = mainWindow.Methods.FirstOrDefault(method =>
        method.Name.String.Contains("<.ctor>b__")
        && method.MethodSig.Params.Count == 2
        && method.MethodSig.Params[0].FullName == "Microsoft.UI.Xaml.FrameworkElement"
        && method.MethodSig.Params[1].FullName == "System.Object");
    if (themeChanged is not null)
    {
        Console.WriteLine("Theme IL:");
        index = 0;
        foreach (var instruction in themeChanged.Body.Instructions)
            Console.WriteLine($"{index++:000}: {instruction}");
    }

    foreach (var nested in mainWindow.NestedTypes.Where(type => type.Name.String.Contains("<EditActionAsync>d__", StringComparison.Ordinal)))
    {
        var moveNext = nested.FindMethod("MoveNext");
        if (moveNext?.Body is null)
            continue;
        Console.WriteLine($"{nested.Name}.MoveNext IL:");
        index = 0;
        foreach (var instruction in moveNext.Body.Instructions)
            Console.WriteLine($"{index++:000}: {instruction}");
    }

    var tempDisable = mainWindow.FindMethod("TemporarilyDisableMouseGestureCaptureAsync");
    if (tempDisable?.Body is not null)
    {
        Console.WriteLine("TemporarilyDisableMouseGestureCaptureAsync IL:");
        index = 0;
        foreach (var instruction in tempDisable.Body.Instructions)
            Console.WriteLine($"{index++:000}: {instruction}");
    }

    foreach (var nested in mainWindow.NestedTypes.Where(type => type.Name.String.Contains("<TemporarilyDisableMouseGestureCaptureAsync>d__", StringComparison.Ordinal)))
    {
        var moveNext = nested.FindMethod("MoveNext");
        if (moveNext?.Body is null)
            continue;
        Console.WriteLine($"{nested.Name}.MoveNext IL:");
        index = 0;
        foreach (var instruction in moveNext.Body.Instructions)
            Console.WriteLine($"{index++:000}: {instruction}");
    }
}

static void InspectEditControls(string path)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    var mainWindow = module.Find("GestureSign.WinUI.MainWindow", isReflectionName: false)
        ?? throw new InvalidOperationException("GestureSign.WinUI.MainWindow not found.");
    var moveNext = FindAsyncMoveNext(mainWindow, "EditActionAsync")
        ?? throw new InvalidOperationException("EditActionAsync.MoveNext not found.");
    var instructions = moveNext.Body.Instructions;

    for (var i = 0; i < instructions.Count; i++)
    {
        if (instructions[i].Operand is not IMethod method)
            continue;

        if (method.DeclaringType.FullName.Contains("CheckBox", StringComparison.Ordinal)
            || method.Name.String.Contains("IsChecked", StringComparison.Ordinal)
            || method.Name.String.Contains("ActivateWindow", StringComparison.Ordinal)
            || method.Name.String.Contains("Content", StringComparison.Ordinal))
        {
            Console.WriteLine($"--- around {i:0000}: {instructions[i]}");
            for (var j = Math.Max(0, i - 14); j <= Math.Min(instructions.Count - 1, i + 18); j++)
                Console.WriteLine($"{j:0000}: {instructions[j]}");
        }
    }
}

static void InspectColorRow(string path)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    var mainWindow = module.Find("GestureSign.WinUI.MainWindow", isReflectionName: false)
        ?? throw new InvalidOperationException("GestureSign.WinUI.MainWindow not found.");

    foreach (var method in mainWindow.Methods.Concat(mainWindow.NestedTypes.SelectMany(type => type.Methods))
        .Where(method => method.Name.String.Contains("VisualFeedbackColor", StringComparison.Ordinal)
            || method.Name.String.Contains("ColorPreset", StringComparison.Ordinal)
            || method.Name.String.Contains("ApplyPreview", StringComparison.Ordinal)
            || method.Name.String.Contains("b__", StringComparison.Ordinal)))
    {
        if (method.Body is null)
            continue;

        var interesting = method.Name.String.Contains("VisualFeedbackColor", StringComparison.Ordinal)
            || method.Name.String.Contains("ColorPreset", StringComparison.Ordinal)
            || method.Body.Instructions.Any(instruction =>
                instruction.Operand is string text && (text.Contains("撤销修改", StringComparison.Ordinal) || text.Contains("保存颜色", StringComparison.Ordinal) || text.Contains("VisualFeedbackColor", StringComparison.Ordinal)));
        if (!interesting)
            continue;

        Console.WriteLine($"{method.DeclaringType.FullName}::{method.Name} IL:");
        var index = 0;
        foreach (var instruction in method.Body.Instructions)
            Console.WriteLine($"{index++:0000}: {instruction}");
    }
}

static void InspectRecognitionCard(string path)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    var mainWindow = module.Find("GestureSign.WinUI.MainWindow", isReflectionName: false)
        ?? throw new InvalidOperationException("GestureSign.WinUI.MainWindow not found.");
    var method = mainWindow.FindMethod("NewRecognitionCard")
        ?? throw new InvalidOperationException("NewRecognitionCard not found.");
    var index = 0;
    foreach (var instruction in method.Body.Instructions)
        Console.WriteLine($"{index++:0000}: {instruction}");
}

static void InspectOpenHotKey(string path)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    var mainWindow = module.Find("GestureSign.WinUI.MainWindow", isReflectionName: false)
        ?? throw new InvalidOperationException("GestureSign.WinUI.MainWindow not found.");

    foreach (var method in mainWindow.Methods.Concat(mainWindow.NestedTypes.SelectMany(type => type.Methods))
        .Where(method => method.Body is not null && (method.Name.String.Contains("OpenSettingsHotKey", StringComparison.Ordinal)
            || method.Name.String.Contains("HotKey", StringComparison.Ordinal)
            || method.Body!.Instructions.Any(instruction => instruction.Operand is string text && text.Contains("OpenSettingsHotKey", StringComparison.Ordinal)))))
    {
        Console.WriteLine($"{method.DeclaringType.FullName}::{method.Name} {method.MethodSig}");
        var index = 0;
        foreach (var instruction in method.Body!.Instructions)
            Console.WriteLine($"{index++:0000}: {instruction}");
    }
}

static void InspectDaemonRecognition(string path)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    foreach (var type in module.Types.Concat(module.Types.SelectMany(type => type.NestedTypes)))
    {
        if (!type.FullName.Contains("MessageProcessor", StringComparison.Ordinal)
            && !type.FullName.Contains("MainWindow", StringComparison.Ordinal))
            continue;

        foreach (var method in type.Methods.Where(method => method.Body is not null
            && (method.Name.String.Contains("ProcessMessages", StringComparison.Ordinal)
                || method.Name.String.Contains("NewRecognitionCard", StringComparison.Ordinal)
                || method.Name.String.Contains("b__", StringComparison.Ordinal))))
        {
            Console.WriteLine($"{type.FullName}::{method.Name} {method.MethodSig}");
            var index = 0;
            foreach (var instruction in method.Body!.Instructions)
                Console.WriteLine($"{index++:0000}: {instruction}");
        }
    }
}

static void Patch(string path)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    if (module.Find("GestureSign.Common.Configuration.FileManager", isReflectionName: false) is { } fileManager)
    {
        PatchCommonFileManager(module, fileManager);
        WriteModule(module, path);
        return;
    }

    if (module.Find("GestureSign.Daemon.MessageProcessor", isReflectionName: false) is { } messageProcessor)
    {
        PatchDaemonRecognitionCommands(module, messageProcessor);
        WriteModule(module, path);
        return;
    }

    var mainWindow = module.Find("GestureSign.WinUI.MainWindow", isReflectionName: false)
        ?? throw new InvalidOperationException("GestureSign.WinUI.MainWindow not found.");
    PatchCommandEditorVisibility(module, mainWindow);
    PatchMicaDimmingOverlay(module, mainWindow);
    PatchDialogScrollContent(module, mainWindow);
    PatchRecognitionToggleCommands(module, mainWindow);

    WriteModule(module, path);
}

static void PatchMicaOnly(string path)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    var mainWindow = module.Find("GestureSign.WinUI.MainWindow", isReflectionName: false)
        ?? throw new InvalidOperationException("GestureSign.WinUI.MainWindow not found.");
    PatchMicaDimmingOverlay(module, mainWindow);
    WriteModule(module, path);
}

static void PatchDialogScrollOnly(string path)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    var mainWindow = module.Find("GestureSign.WinUI.MainWindow", isReflectionName: false)
        ?? throw new InvalidOperationException("GestureSign.WinUI.MainWindow not found.");
    if (!module.GetTypeRefs().Any(type => type.FullName == "Microsoft.UI.Xaml.Controls.ScrollViewer"))
    {
        WriteModule(module, path);
        return;
    }
    PatchDialogScrollContent(module, mainWindow);
    if (mainWindow.FindMethod("NewDialogScrollContent") is { } helper)
        UpdateDialogScrollHeight(helper);
    WriteModule(module, path);
}

static void PatchMoveTouchpadButtonOnly(string path)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    var mainWindow = module.Find("GestureSign.WinUI.MainWindow", isReflectionName: false)
        ?? throw new InvalidOperationException("GestureSign.WinUI.MainWindow not found.");
    PatchMoveTouchpadButton(module, mainWindow);
    WriteModule(module, path);
}

static void PatchHideContinuousGestureOnly(string path)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    var mainWindow = module.Find("GestureSign.WinUI.MainWindow", isReflectionName: false)
        ?? throw new InvalidOperationException("GestureSign.WinUI.MainWindow not found.");
    PatchHideContinuousGestureBox(mainWindow);
    WriteModule(module, path);
}

static void PatchSteadyRecognitionCardOnly(string path)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    var mainWindow = module.Find("GestureSign.WinUI.MainWindow", isReflectionName: false)
        ?? throw new InvalidOperationException("GestureSign.WinUI.MainWindow not found.");
    PatchSteadyRecognitionCard(module, mainWindow);
    PatchActionToggleRefresh(mainWindow);
    PatchRecognitionToggleCommands(module, mainWindow);
    WriteModule(module, path);
}

static void PatchInstantColorOnly(string path)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    var mainWindow = module.Find("GestureSign.WinUI.MainWindow", isReflectionName: false)
        ?? throw new InvalidOperationException("GestureSign.WinUI.MainWindow not found.");
    PatchInstantVisualFeedbackColor(mainWindow);
    WriteModule(module, path);
}

static void PatchRecognitionOnOnly(string path)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    var mainWindow = module.Find("GestureSign.WinUI.MainWindow", isReflectionName: false)
        ?? throw new InvalidOperationException("GestureSign.WinUI.MainWindow not found.");
    PatchRecognitionSwitchOn(module, mainWindow);
    WriteModule(module, path);
}

static void PatchOpenHotKeySaveOnly(string path)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    if (module.Find("GestureSign.WinUI.MainWindow", isReflectionName: false) is { } mainWindow)
    {
        if (PatchOpenSettingsHotKeySave(module, mainWindow))
            WriteModule(module, path);
        else
            Console.WriteLine("Open-settings hotkey UI methods not found; skipping this WinUI assembly.");
        return;
    }

    if (module.Find("GestureSign.Daemon.Triggers.HotKeyManager", isReflectionName: false) is { } hotKeyManager)
    {
        PatchDaemonOpenHotKeyParser(module, hotKeyManager);
        WriteModule(module, path);
        return;
    }

    Console.WriteLine("No supported open hotkey target found; skipping.");
}

static void PatchBrandVersionOnly(string path, string version)
{
    var module = ModuleDefMD.Load(File.ReadAllBytes(path));
    var mainWindow = module.Find("GestureSign.WinUI.MainWindow", isReflectionName: false)
        ?? throw new InvalidOperationException("GestureSign.WinUI.MainWindow not found.");

    RemoveProductNameStartupPatch(mainWindow);
    RemoveTitleBarProductNameLoadedPatch(mainWindow);
    PatchAboutBrandAndVersion(mainWindow, version);
    WriteModule(module, path);
}

static void RemoveTitleBarProductNameLoadedPatch(TypeDef mainWindow)
{
    var ctor = mainWindow.FindMethod(".ctor");
    if (ctor?.Body is null)
        return;

    var instructions = ctor.Body.Instructions;
    for (var i = instructions.Count - 1; i >= 0; i--)
    {
        if (instructions[i].Operand is not IMethod method || method.Name != "add_Loaded")
            continue;
        if (i < 4 || instructions[i - 2].Operand is not IMethod handler || handler.Name != "ApplyTitleBarProductNameOnLoaded")
            continue;

        for (var remove = 0; remove < 5; remove++)
            instructions.RemoveAt(i - 4);
    }

    ctor.Body.OptimizeBranches();
    ctor.Body.OptimizeMacros();
}

static void PatchTitleBarProductNameLoaded(ModuleDef module, TypeDef mainWindow)
{
    var handler = mainWindow.FindMethod("ApplyTitleBarProductNameOnLoaded")
        ?? CreateApplyTitleBarProductNameOnLoaded(module, mainWindow);
    var ctor = mainWindow.FindMethod(".ctor") ?? throw new InvalidOperationException(".ctor not found.");
    if (ctor.Body is null || ctor.Body.Instructions.Any(instruction => instruction.Operand == handler))
        return;

    var rootField = mainWindow.FindField("Root") ?? throw new InvalidOperationException("Root field not found.");
    var frameworkElementType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.FrameworkElement");
    var routedEventHandlerType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.RoutedEventHandler");
    var addLoaded = module.Import(new MemberRefUser(
        module,
        "add_Loaded",
        MethodSig.CreateInstance(module.CorLibTypes.Void, new ClassSig(routedEventHandlerType)),
        frameworkElementType));
    var routedEventHandlerCtor = module.Import(new MemberRefUser(
        module,
        ".ctor",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Object, module.CorLibTypes.IntPtr),
        routedEventHandlerType));

    var setTitleBar = ctor.Body.Instructions.FirstOrDefault(instruction =>
        instruction.OpCode == OpCodes.Call && instruction.Operand is IMethod method && method.Name == "SetTitleBar");
    if (setTitleBar is null)
        throw new InvalidOperationException("Window.SetTitleBar call not found.");

    var index = ctor.Body.Instructions.IndexOf(setTitleBar) + 1;
    ctor.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldarg_0));
    ctor.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldfld, rootField));
    ctor.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldarg_0));
    ctor.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldftn, handler));
    ctor.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Newobj, routedEventHandlerCtor));
    ctor.Body.Instructions.Insert(index, Instruction.Create(OpCodes.Callvirt, addLoaded));
    ctor.Body.OptimizeBranches();
    ctor.Body.OptimizeMacros();
}

static MethodDef CreateApplyTitleBarProductNameOnLoaded(ModuleDef module, TypeDef mainWindow)
{
    var method = new MethodDefUser(
        "ApplyTitleBarProductNameOnLoaded",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Object, new ClassSig(module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.RoutedEventArgs"))),
        MethodImplAttributes.IL | MethodImplAttributes.Managed,
        MethodAttributes.Private | MethodAttributes.HideBySig);
    mainWindow.Methods.Add(method);

    var panelType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.Panel");
    var uiElementCollectionType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.UIElementCollection");
    var textBlockType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.TextBlock");
    var appTitleBarField = mainWindow.FindField("AppTitleBar") ?? throw new InvalidOperationException("AppTitleBar field not found.");

    var getChildren = module.Import(new MemberRefUser(
        module,
        "get_Children",
        MethodSig.CreateInstance(new ClassSig(uiElementCollectionType)),
        panelType));
    var getCount = module.Import(new MemberRefUser(
        module,
        "get_Count",
        MethodSig.CreateInstance(module.CorLibTypes.Int32),
        uiElementCollectionType));
    var getItem = module.Import(new MemberRefUser(
        module,
        "get_Item",
        MethodSig.CreateInstance(new ClassSig(module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.UIElement")), module.CorLibTypes.Int32),
        uiElementCollectionType));
    var setText = module.Import(new MemberRefUser(
        module,
        "set_Text",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String),
        textBlockType));

    var body = new CilBody { InitLocals = true };
    body.Variables.Add(new Local(new ClassSig(textBlockType)));
    method.Body = body;
    var i = body.Instructions;
    var ret = Instruction.Create(OpCodes.Ret);

    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ldfld, appTitleBarField));
    i.Add(Instruction.Create(OpCodes.Callvirt, getChildren));
    i.Add(Instruction.Create(OpCodes.Callvirt, getCount));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_1));
    i.Add(Instruction.Create(OpCodes.Ble_S, ret));

    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ldfld, appTitleBarField));
    i.Add(Instruction.Create(OpCodes.Callvirt, getChildren));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_1));
    i.Add(Instruction.Create(OpCodes.Callvirt, getItem));
    i.Add(Instruction.Create(OpCodes.Isinst, textBlockType));
    i.Add(Instruction.Create(OpCodes.Stloc_0));
    i.Add(Instruction.Create(OpCodes.Ldloc_0));
    i.Add(Instruction.Create(OpCodes.Brfalse_S, ret));
    i.Add(Instruction.Create(OpCodes.Ldloc_0));
    i.Add(Instruction.Create(OpCodes.Ldstr, "GestureSign V2"));
    i.Add(Instruction.Create(OpCodes.Callvirt, setText));
    i.Add(ret);

    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();
    return method;
}

static void RemoveProductNameStartupPatch(TypeDef mainWindow)
{
    var ctor = mainWindow.FindMethod(".ctor");
    if (ctor?.Body is null)
        return;

    var instructions = ctor.Body.Instructions;
    for (var i = instructions.Count - 1; i >= 0; i--)
    {
        if (instructions[i].Operand is not IMethod method || method.Name != "ApplyProductName")
            continue;

        instructions.RemoveAt(i);
        if (i > 0 && instructions[i - 1].OpCode == OpCodes.Ldarg_0)
            instructions.RemoveAt(i - 1);
    }

    ctor.Body.OptimizeBranches();
    ctor.Body.OptimizeMacros();
}

static void PatchAboutBrandAndVersion(TypeDef mainWindow, string version)
{
    var buildAboutPage = mainWindow.FindMethod("BuildAboutPage")
        ?? throw new InvalidOperationException("BuildAboutPage not found.");
    if (buildAboutPage.Body is null)
        return;

    var subtitlePatched = false;
    foreach (var instruction in buildAboutPage.Body.Instructions)
    {
        if (instruction.OpCode != OpCodes.Ldstr || instruction.Operand is not string text)
            continue;

        if (text == "GestureSign")
        {
            instruction.Operand = "GestureSign V2";
            continue;
        }

        if (text == "WinUI 3 前端重构预览" || text.StartsWith("WinUI 3 前端重构预览\r\n版本：", StringComparison.Ordinal))
        {
            instruction.Operand = $"WinUI 3 前端重构预览\r\n版本：{version}";
            subtitlePatched = true;
        }
    }

    if (!subtitlePatched)
        throw new InvalidOperationException("About subtitle text not found.");
}

static void WriteModule(ModuleDef module, string path)
{
    var tempPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
    module.Write(tempPath);
    File.Copy(tempPath, path, overwrite: true);
    File.Delete(tempPath);
}

static TypeRef GetOrCreateTypeRef(ModuleDef module, string ns, string name, string assemblyName)
{
    return module.GetTypeRefs().FirstOrDefault(type => type.Namespace == ns && type.Name == name)
        ?? new TypeRefUser(module, ns, name, FindTypeScope(module, ns, assemblyName));
}

static IResolutionScope FindTypeScope(ModuleDef module, string ns, string assemblyName)
{
    if (module.GetTypeRefs().FirstOrDefault(type => type.Namespace == ns) is { } existingType)
        return existingType.ResolutionScope;

    if (module.GetAssemblyRefs().FirstOrDefault(assembly => assembly.Name == assemblyName) is { } exactAssembly)
        return exactAssembly;

    if (module.GetAssemblyRefs().FirstOrDefault(assembly => assembly.Name.String.Equals(assemblyName, StringComparison.OrdinalIgnoreCase)) is { } caseInsensitiveAssembly)
        return caseInsensitiveAssembly;

    if (module.GetAssemblyRefs().FirstOrDefault(assembly => assembly.Name.String.Contains("Xaml", StringComparison.OrdinalIgnoreCase)) is { } xamlAssembly)
        return xamlAssembly;

    throw new InvalidOperationException($"Assembly reference for {assemblyName} not found.");
}

static void PatchProductName(ModuleDef module, TypeDef mainWindow)
{
    var applyMethod = mainWindow.FindMethod("ApplyProductName") ?? CreateApplyProductName(module, mainWindow);
    var ctor = mainWindow.FindMethod(".ctor") ?? throw new InvalidOperationException(".ctor not found.");

    if (ctor.Body.Instructions.Any(instruction => instruction.Operand == applyMethod))
        return;

    var setTitleBar = ctor.Body.Instructions.FirstOrDefault(instruction =>
        instruction.OpCode == OpCodes.Call && instruction.Operand is IMethod method && method.Name == "SetTitleBar");
    if (setTitleBar is null)
        throw new InvalidOperationException("Window.SetTitleBar call not found.");

    var index = ctor.Body.Instructions.IndexOf(setTitleBar) + 1;
    ctor.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldarg_0));
    ctor.Body.Instructions.Insert(index, Instruction.Create(OpCodes.Call, applyMethod));
    ctor.Body.OptimizeBranches();
    ctor.Body.OptimizeMacros();
}

static MethodDef CreateApplyProductName(ModuleDef module, TypeDef mainWindow)
{
    var method = new MethodDefUser(
        "ApplyProductName",
        MethodSig.CreateInstance(module.CorLibTypes.Void),
        MethodImplAttributes.IL | MethodImplAttributes.Managed,
        MethodAttributes.Private | MethodAttributes.HideBySig);
    mainWindow.Methods.Add(method);

    var windowType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Window");
    var panelType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.Panel");
    var uiElementCollectionType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.UIElementCollection");
    var textBlockType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.TextBlock");
    var textBlockSig = new ClassSig(textBlockType);
    var appTitleBarField = mainWindow.FindField("AppTitleBar") ?? throw new InvalidOperationException("AppTitleBar field not found.");

    var setWindowTitle = module.Import(new MemberRefUser(
        module,
        "set_Title",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String),
        windowType));
    var getChildren = module.Import(new MemberRefUser(
        module,
        "get_Children",
        MethodSig.CreateInstance(new ClassSig(uiElementCollectionType)),
        panelType));
    var getCount = module.Import(new MemberRefUser(
        module,
        "get_Count",
        MethodSig.CreateInstance(module.CorLibTypes.Int32),
        uiElementCollectionType));
    var getItem = module.Import(new MemberRefUser(
        module,
        "get_Item",
        MethodSig.CreateInstance(new ClassSig(module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.UIElement")), module.CorLibTypes.Int32),
        uiElementCollectionType));
    var setText = module.Import(new MemberRefUser(
        module,
        "set_Text",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String),
        textBlockType));

    var body = new CilBody { InitLocals = true };
    body.Variables.Add(new Local(textBlockSig));
    method.Body = body;
    var i = body.Instructions;
    var skipTitleBarText = Instruction.Create(OpCodes.Ret);

    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ldstr, "GestureSign V2"));
    i.Add(Instruction.Create(OpCodes.Call, setWindowTitle));

    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ldfld, appTitleBarField));
    i.Add(Instruction.Create(OpCodes.Callvirt, getChildren));
    i.Add(Instruction.Create(OpCodes.Callvirt, getCount));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_1));
    i.Add(Instruction.Create(OpCodes.Ble_S, skipTitleBarText));

    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ldfld, appTitleBarField));
    i.Add(Instruction.Create(OpCodes.Callvirt, getChildren));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_1));
    i.Add(Instruction.Create(OpCodes.Callvirt, getItem));
    i.Add(Instruction.Create(OpCodes.Isinst, textBlockType));
    i.Add(Instruction.Create(OpCodes.Stloc_0));
    i.Add(Instruction.Create(OpCodes.Ldloc_0));
    i.Add(Instruction.Create(OpCodes.Brfalse_S, skipTitleBarText));
    i.Add(Instruction.Create(OpCodes.Ldloc_0));
    i.Add(Instruction.Create(OpCodes.Ldstr, "GestureSign V2"));
    i.Add(Instruction.Create(OpCodes.Callvirt, setText));
    i.Add(skipTitleBarText);

    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();
    return method;
}

static void PatchCommonFileManager(ModuleDef module, TypeDef fileManager)
{
    var normalizeMethod = fileManager.FindMethod("NormalizeActionHotkeyKeyCodes") ?? CreateNormalizeActionHotkeyKeyCodes(module, fileManager);
    var loadObject = fileManager.Methods.FirstOrDefault(method => method.Name == "LoadObject" && method.HasGenericParameters)
        ?? throw new InvalidOperationException("FileManager.LoadObject<T> not found.");

    if (loadObject.Body.Instructions.Any(instruction => instruction.Operand == normalizeMethod))
        return;

    var readAllTextIndex = loadObject.Body.Instructions.ToList().FindIndex(instruction =>
        instruction.OpCode == OpCodes.Call
        && instruction.Operand is IMethod method
        && method.DeclaringType.FullName == "System.IO.File"
        && method.Name == "ReadAllText");
    if (readAllTextIndex < 0)
        throw new InvalidOperationException("File.ReadAllText call not found in LoadObject<T>.");

    loadObject.Body.Instructions.Insert(readAllTextIndex + 1, Instruction.Create(OpCodes.Call, normalizeMethod));
    loadObject.Body.OptimizeBranches();
    loadObject.Body.OptimizeMacros();
}

static MethodDef CreateNormalizeActionHotkeyKeyCodes(ModuleDef module, TypeDef fileManager)
{
    var method = new MethodDefUser(
        "NormalizeActionHotkeyKeyCodes",
        MethodSig.CreateStatic(module.CorLibTypes.String, module.CorLibTypes.String),
        MethodImplAttributes.IL | MethodImplAttributes.Managed,
        MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig);
    fileManager.Methods.Add(method);

    var stringType = module.CorLibTypes.GetTypeRef("System", "String");
    var systemAssembly = module.GetAssemblyRefs().FirstOrDefault(assembly => assembly.Name == "System")
        ?? module.CorLibTypes.AssemblyRef;
    var regexType = new TypeRefUser(module, "System.Text.RegularExpressions", "Regex", systemAssembly);

    var isNullOrWhiteSpace = module.Import(new MemberRefUser(
        module,
        "IsNullOrWhiteSpace",
        MethodSig.CreateStatic(module.CorLibTypes.Boolean, module.CorLibTypes.String),
        stringType));
    var indexOf = module.Import(new MemberRefUser(
        module,
        "IndexOf",
        MethodSig.CreateInstance(module.CorLibTypes.Int32, module.CorLibTypes.String, module.CorLibTypes.Int32),
        stringType));
    var replace = module.Import(new MemberRefUser(
        module,
        "Replace",
        MethodSig.CreateStatic(
            module.CorLibTypes.String,
            module.CorLibTypes.String,
            module.CorLibTypes.String,
            module.CorLibTypes.String),
        regexType));

    var body = new CilBody();
    method.Body = body;
    var i = body.Instructions;
    var checkHotkey = Instruction.Create(OpCodes.Ldarg_0);
    var replaceKeyCode = Instruction.Create(OpCodes.Ldarg_0);
    var returnOriginal = Instruction.Create(OpCodes.Ldarg_0);

    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Call, isNullOrWhiteSpace));
    i.Add(Instruction.Create(OpCodes.Brfalse_S, checkHotkey));
    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ret));

    i.Add(checkHotkey);
    i.Add(Instruction.Create(OpCodes.Ldstr, "\"Hotkey\""));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_4)); // StringComparison.OrdinalIgnoreCase
    i.Add(Instruction.Create(OpCodes.Callvirt, indexOf));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_0));
    i.Add(Instruction.Create(OpCodes.Bge_S, replaceKeyCode));
    i.Add(returnOriginal);
    i.Add(Instruction.Create(OpCodes.Ret));

    i.Add(replaceKeyCode);
    i.Add(Instruction.Create(OpCodes.Ldstr, "(\"Hotkey\"\\s*:\\s*\\{[^\\}]*?\"KeyCode\"\\s*:\\s*)\\[\\s*(-?\\d+)\\s*(?:,\\s*[^\\]]*)?\\]"));
    i.Add(Instruction.Create(OpCodes.Ldstr, "$1$2"));
    i.Add(Instruction.Create(OpCodes.Call, replace));
    i.Add(Instruction.Create(OpCodes.Ret));

    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();
    return method;
}

static void PatchCommandEditorVisibility(ModuleDef module, TypeDef mainWindow)
{
    var method = mainWindow.FindMethod("UpdateCommandEditorVisibility")
        ?? throw new InvalidOperationException("UpdateCommandEditorVisibility not found.");

    var stringType = module.CorLibTypes.GetTypeRef("System", "String");
    var stringComparisonType = module.CorLibTypes.GetTypeRef("System", "StringComparison");
    var visibilityType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Visibility");
    var uiElementType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.UIElement");

    var contains = module.Import(new MemberRefUser(
        module,
        "Contains",
        MethodSig.CreateInstance(
            module.CorLibTypes.Boolean,
            module.CorLibTypes.String,
            new ValueTypeSig(stringComparisonType)),
        stringType));

    var isNullOrWhiteSpace = module.Import(new MemberRefUser(
        module,
        "IsNullOrWhiteSpace",
        MethodSig.CreateStatic(module.CorLibTypes.Boolean, module.CorLibTypes.String),
        stringType));

    var setVisibility = module.Import(new MemberRefUser(
        module,
        "set_Visibility",
        MethodSig.CreateInstance(module.CorLibTypes.Void, new ValueTypeSig(visibilityType)),
        uiElementType));

    var body = new CilBody { InitLocals = true };
    body.Variables.Add(new Local(module.CorLibTypes.Boolean)); // isHotKey
    body.Variables.Add(new Local(module.CorLibTypes.Boolean)); // isCustom
    body.Variables.Add(new Local(module.CorLibTypes.Boolean)); // isSettingsFree
    method.Body = body;

    var i = body.Instructions;
    var settingsFreeDone = Instruction.Create(OpCodes.Nop);
    var setSettingsFree = Instruction.Create(OpCodes.Ldc_I4_1);
    var pluginVisible = Instruction.Create(OpCodes.Ldc_I4_0);
    var pluginSet = Instruction.Create(OpCodes.Callvirt, setVisibility);
    var hotkeyVisible = Instruction.Create(OpCodes.Ldc_I4_0);
    var hotkeySet = Instruction.Create(OpCodes.Callvirt, setVisibility);
    var settingsCollapsed = Instruction.Create(OpCodes.Ldc_I4_1);
    var settingsVisible = Instruction.Create(OpCodes.Ldc_I4_0);
    var settingsSet = Instruction.Create(OpCodes.Callvirt, setVisibility);

    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ldstr, "HotKey"));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_5));
    i.Add(Instruction.Create(OpCodes.Callvirt, contains));
    i.Add(Instruction.Create(OpCodes.Stloc_0));

    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Call, isNullOrWhiteSpace));
    i.Add(Instruction.Create(OpCodes.Stloc_1));

    i.Add(Instruction.Create(OpCodes.Ldc_I4_0));
    i.Add(Instruction.Create(OpCodes.Stloc_2));
    i.Add(Instruction.Create(OpCodes.Ldloc_1));
    i.Add(Instruction.Create(OpCodes.Brtrue_S, settingsFreeDone));
    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ldstr, "DefaultBrowser"));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_5));
    i.Add(Instruction.Create(OpCodes.Callvirt, contains));
    i.Add(Instruction.Create(OpCodes.Brtrue_S, setSettingsFree));
    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ldstr, "PreviousApplication"));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_5));
    i.Add(Instruction.Create(OpCodes.Callvirt, contains));
    i.Add(Instruction.Create(OpCodes.Brtrue_S, setSettingsFree));
    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ldstr, "NextApplication"));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_5));
    i.Add(Instruction.Create(OpCodes.Callvirt, contains));
    i.Add(Instruction.Create(OpCodes.Brfalse_S, settingsFreeDone));
    i.Add(setSettingsFree);
    i.Add(Instruction.Create(OpCodes.Stloc_2));
    i.Add(settingsFreeDone);

    i.Add(Instruction.Create(OpCodes.Ldarg_1));
    i.Add(Instruction.Create(OpCodes.Ldloc_1));
    i.Add(Instruction.Create(OpCodes.Brtrue_S, pluginVisible));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_1));
    i.Add(Instruction.Create(OpCodes.Br_S, pluginSet));
    i.Add(pluginVisible);
    i.Add(pluginSet);

    i.Add(Instruction.Create(OpCodes.Ldarg_2));
    i.Add(Instruction.Create(OpCodes.Ldloc_0));
    i.Add(Instruction.Create(OpCodes.Brtrue_S, hotkeyVisible));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_1));
    i.Add(Instruction.Create(OpCodes.Br_S, hotkeySet));
    i.Add(hotkeyVisible);
    i.Add(hotkeySet);

    i.Add(Instruction.Create(OpCodes.Ldarg_3));
    i.Add(Instruction.Create(OpCodes.Ldloc_0));
    i.Add(Instruction.Create(OpCodes.Brtrue_S, settingsCollapsed));
    i.Add(Instruction.Create(OpCodes.Ldloc_2));
    i.Add(Instruction.Create(OpCodes.Brtrue_S, settingsCollapsed));
    i.Add(Instruction.Create(OpCodes.Br_S, settingsVisible));
    i.Add(settingsCollapsed);
    i.Add(Instruction.Create(OpCodes.Br_S, settingsSet));
    i.Add(settingsVisible);
    i.Add(settingsSet);
    i.Add(Instruction.Create(OpCodes.Ret));

    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();
}

static void PatchMicaDimmingOverlay(ModuleDef module, TypeDef mainWindow)
{
    var applyMethod = mainWindow.FindMethod("ApplyMicaDimmingOverlay") ?? CreateApplyMicaDimmingOverlay(module, mainWindow);
    UpdateMicaDimmingOverlayColor(applyMethod, darkAlpha: 150, darkRed: 48, darkGreen: 52, darkBlue: 58, lightAlpha: 125);
    PatchDarkThemeBrush(module, mainWindow, "CardBrush", 46, 255, 255, 255);
    PatchDarkThemeBrush(module, mainWindow, "SubtleBrush", 32, 255, 255, 255);
    PatchDarkThemeBrush(module, mainWindow, "SelectionBrush", 56, 255, 255, 255);
    PatchDarkThemeBrush(module, mainWindow, "BorderBrush", 58, 255, 255, 255);
    PatchLightThemeBrush(module, mainWindow, "CardBrush", 255, 255, 255, 255);
    PatchLightThemeBrush(module, mainWindow, "SubtleBrush", 255, 255, 255, 255);
    PatchLightThemeBrush(module, mainWindow, "SelectionBrush", 255, 250, 252, 255);
    PatchLightThemeBrush(module, mainWindow, "BorderBrush", 255, 240, 244, 249);
    RemoveActionsPageTranslucentPanel(mainWindow);
    var ctor = mainWindow.FindMethod(".ctor") ?? throw new InvalidOperationException(".ctor not found.");
    var setSystemBackdrop = ctor.Body.Instructions.FirstOrDefault(instruction =>
        instruction.OpCode == OpCodes.Call && instruction.Operand is IMethod method && method.Name == "set_SystemBackdrop");
    if (setSystemBackdrop is not null && !ctor.Body.Instructions.Any(instruction => instruction.Operand == applyMethod))
    {
        var index = ctor.Body.Instructions.IndexOf(setSystemBackdrop) + 1;
        ctor.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldarg_0));
        ctor.Body.Instructions.Insert(index, Instruction.Create(OpCodes.Call, applyMethod));
        ctor.Body.OptimizeBranches();
        ctor.Body.OptimizeMacros();
    }

    var themeChanged = mainWindow.Methods.FirstOrDefault(method =>
        method.Name.String.Contains("<.ctor>b__")
        && method.MethodSig.Params.Count == 2
        && method.MethodSig.Params[0].FullName == "Microsoft.UI.Xaml.FrameworkElement"
        && method.MethodSig.Params[1].FullName == "System.Object")
        ?? throw new InvalidOperationException("Theme changed handler not found.");

    if (!themeChanged.Body.Instructions.Any(instruction => instruction.Operand == applyMethod))
    {
        themeChanged.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Call, applyMethod));
        themeChanged.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Ldarg_0));
        themeChanged.Body.OptimizeBranches();
        themeChanged.Body.OptimizeMacros();
    }
}

static void RemoveActionsPageTranslucentPanel(TypeDef mainWindow)
{
    var method = mainWindow.FindMethod("BuildActionsPage");
    if (method?.Body is null)
        return;

    var instructions = method.Body.Instructions;
    for (var index = 0; index < instructions.Count; index++)
    {
        if (instructions[index].Operand is not string marker || marker != "Codex.ActionsPageTranslucentPanel")
            continue;

        var removeCount = Math.Min(10, instructions.Count - index);
        for (var offset = 0; offset < removeCount; offset++)
            instructions.RemoveAt(index);

        method.Body.OptimizeBranches();
        method.Body.OptimizeMacros();
        return;
    }
}

static void RedirectLegacyMicaDimmingOverlay(MethodDef legacyMethod, MethodDef applyMethod)
{
    legacyMethod.Body = new CilBody();
    legacyMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
    legacyMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Call, applyMethod));
    legacyMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    legacyMethod.Body.OptimizeBranches();
    legacyMethod.Body.OptimizeMacros();
}

static FieldDef CreateIsWindowActiveField(ModuleDef module, TypeDef mainWindow)
{
    var field = new FieldDefUser(
        "_isWindowActive",
        new FieldSig(module.CorLibTypes.Boolean),
        FieldAttributes.Private);
    mainWindow.Fields.Add(field);

    var ctor = mainWindow.FindMethod(".ctor") ?? throw new InvalidOperationException(".ctor not found.");
    var baseCallIndex = ctor.Body.Instructions.ToList().FindIndex(instruction =>
        instruction.OpCode == OpCodes.Call
        && instruction.Operand is IMethod method
        && method.Name == ".ctor"
        && method.DeclaringType.FullName == "Microsoft.UI.Xaml.Window");
    if (baseCallIndex < 0)
        baseCallIndex = 0;

    var insertIndex = baseCallIndex + 1;
    ctor.Body.Instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Ldarg_0));
    ctor.Body.Instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Ldc_I4_1));
    ctor.Body.Instructions.Insert(insertIndex, Instruction.Create(OpCodes.Stfld, field));
    ctor.Body.OptimizeBranches();
    ctor.Body.OptimizeMacros();
    return field;
}

static void PatchWindowActivatedOverlay(ModuleDef module, TypeDef mainWindow, FieldDef activeField, MethodDef applyMethod)
{
    var activatedMethod = mainWindow.FindMethod("MainWindow_ActivatedOverlay")
        ?? CreateMainWindowActivatedOverlay(module, mainWindow, activeField, applyMethod);

    var ctor = mainWindow.FindMethod(".ctor") ?? throw new InvalidOperationException(".ctor not found.");
    if (ctor.Body.Instructions.Any(instruction => instruction.Operand == activatedMethod))
        return;

    var windowType = GetOrCreateTypeRef(module, "Microsoft.UI.Xaml", "Window", "Microsoft.UI.Xaml");
    var activatedEventHandlerType = GetOrCreateTypeRef(module, "Microsoft.UI.Xaml", "WindowActivatedEventHandler", "Microsoft.UI.Xaml");
    var activatedEventHandlerSig = new ClassSig(activatedEventHandlerType);

    var handlerCtor = module.Import(new MemberRefUser(
        module,
        ".ctor",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Object, module.CorLibTypes.IntPtr),
        activatedEventHandlerType));
    var addActivated = module.Import(new MemberRefUser(
        module,
        "add_Activated",
        MethodSig.CreateInstance(module.CorLibTypes.Void, activatedEventHandlerSig),
        windowType));

    var ensureDaemonIndex = ctor.Body.Instructions.ToList().FindIndex(instruction =>
        instruction.OpCode == OpCodes.Call
        && instruction.Operand is IMethod method
        && method.Name == "EnsureDaemonRunningAsync");
    var index = ensureDaemonIndex >= 0 ? ensureDaemonIndex : ctor.Body.Instructions.Count - 1;

    ctor.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldarg_0));
    ctor.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldarg_0));
    ctor.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Ldftn, activatedMethod));
    ctor.Body.Instructions.Insert(index++, Instruction.Create(OpCodes.Newobj, handlerCtor));
    ctor.Body.Instructions.Insert(index, Instruction.Create(OpCodes.Call, addActivated));
    ctor.Body.OptimizeBranches();
    ctor.Body.OptimizeMacros();
}

static void UpdateMicaDimmingOverlayAlpha(MethodDef applyMethod, int darkAlpha, int lightAlpha)
{
    if (applyMethod.Body is null)
        return;

    var fromArgbCalls = applyMethod.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Call
            && instruction.Operand is IMethod method
            && method.Name == "FromArgb")
        .ToList();

    if (fromArgbCalls.Count >= 2)
    {
        ReplaceInstruction(fromArgbCalls[0], -4, darkAlpha);
        ReplaceInstruction(fromArgbCalls[1], -4, lightAlpha);
    }
    else
    {
        foreach (var instruction in applyMethod.Body.Instructions.Where(instruction => IsLdcI4Value(instruction, 128)).ToList())
            ReplaceInstruction(instruction, 0, darkAlpha);
    }

    applyMethod.Body.OptimizeBranches();
    applyMethod.Body.OptimizeMacros();

    void ReplaceInstruction(Instruction anchor, int offset, int alpha)
    {
        var index = applyMethod.Body.Instructions.IndexOf(anchor) + offset;
        if (index < 0 || index >= applyMethod.Body.Instructions.Count)
            return;

        var replacement = LdcI4(alpha);
        applyMethod.Body.Instructions[index].OpCode = replacement.OpCode;
        applyMethod.Body.Instructions[index].Operand = replacement.Operand;
    }
}

static void UpdateMicaDimmingOverlayColor(MethodDef applyMethod, int darkAlpha, int darkRed, int darkGreen, int darkBlue, int lightAlpha)
{
    if (applyMethod.Body is null)
        return;

    var fromArgbCalls = applyMethod.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Call
            && instruction.Operand is IMethod method
            && method.Name == "FromArgb")
        .ToList();
    if (fromArgbCalls.Count < 2)
        return;

    ReplaceInstruction(fromArgbCalls[0], -4, darkAlpha);
    ReplaceInstruction(fromArgbCalls[0], -3, darkRed);
    ReplaceInstruction(fromArgbCalls[0], -2, darkGreen);
    ReplaceInstruction(fromArgbCalls[0], -1, darkBlue);
    ReplaceInstruction(fromArgbCalls[1], -4, lightAlpha);
    ReplaceInstruction(fromArgbCalls[1], -3, 255);
    ReplaceInstruction(fromArgbCalls[1], -2, 255);
    ReplaceInstruction(fromArgbCalls[1], -1, 255);

    applyMethod.Body.OptimizeBranches();
    applyMethod.Body.OptimizeMacros();

    void ReplaceInstruction(Instruction anchor, int offset, int value)
    {
        var index = applyMethod.Body.Instructions.IndexOf(anchor) + offset;
        if (index < 0 || index >= applyMethod.Body.Instructions.Count)
            return;

        var replacement = LdcI4(value);
        applyMethod.Body.Instructions[index].OpCode = replacement.OpCode;
        applyMethod.Body.Instructions[index].Operand = replacement.Operand;
    }
}

static void PatchDarkThemeBrush(ModuleDef module, TypeDef mainWindow, string methodName, int alpha, int red, int green, int blue)
{
    var method = mainWindow.FindMethod(methodName);
    if (method is null)
        return;
    if (method.Body is null)
        return;

    var fromArgb = method.Body.Instructions.FirstOrDefault(instruction =>
        instruction.OpCode == OpCodes.Call
        && instruction.Operand is IMethod called
        && called.Name == "FromArgb");
    if (fromArgb is null)
        return;

    ReplaceInstruction(fromArgb, -4, alpha);
    ReplaceInstruction(fromArgb, -3, red);
    ReplaceInstruction(fromArgb, -2, green);
    ReplaceInstruction(fromArgb, -1, blue);
    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();

    void ReplaceInstruction(Instruction anchor, int offset, int value)
    {
        var index = method.Body.Instructions.IndexOf(anchor) + offset;
        if (index < 0 || index >= method.Body.Instructions.Count)
            return;

        var replacement = LdcI4(value);
        method.Body.Instructions[index].OpCode = replacement.OpCode;
        method.Body.Instructions[index].Operand = replacement.Operand;
    }
}

static void PatchLightThemeBrush(ModuleDef module, TypeDef mainWindow, string methodName, int alpha, int red, int green, int blue)
{
    var method = mainWindow.FindMethod(methodName);
    if (method?.Body is null)
        return;

    var fromArgbCalls = method.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Call
            && instruction.Operand is IMethod called
            && called.Name == "FromArgb")
        .ToList();
    if (fromArgbCalls.Count < 2)
        return;

    ReplaceInstruction(fromArgbCalls[1], -4, alpha);
    ReplaceInstruction(fromArgbCalls[1], -3, red);
    ReplaceInstruction(fromArgbCalls[1], -2, green);
    ReplaceInstruction(fromArgbCalls[1], -1, blue);
    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();

    void ReplaceInstruction(Instruction anchor, int offset, int value)
    {
        var index = method.Body.Instructions.IndexOf(anchor) + offset;
        if (index < 0 || index >= method.Body.Instructions.Count)
            return;

        var replacement = LdcI4(value);
        method.Body.Instructions[index].OpCode = replacement.OpCode;
        method.Body.Instructions[index].Operand = replacement.Operand;
    }
}

static bool IsLdcI4Value(Instruction instruction, int value)
{
    return instruction.OpCode.Code switch
    {
        Code.Ldc_I4_M1 => value == -1,
        Code.Ldc_I4_0 => value == 0,
        Code.Ldc_I4_1 => value == 1,
        Code.Ldc_I4_2 => value == 2,
        Code.Ldc_I4_3 => value == 3,
        Code.Ldc_I4_4 => value == 4,
        Code.Ldc_I4_5 => value == 5,
        Code.Ldc_I4_6 => value == 6,
        Code.Ldc_I4_7 => value == 7,
        Code.Ldc_I4_8 => value == 8,
        Code.Ldc_I4_S => instruction.Operand is sbyte shortValue && shortValue == value,
        Code.Ldc_I4 => instruction.Operand is int intValue && intValue == value,
        _ => false
    };
}

static void PatchDialogScrollContent(ModuleDef module, TypeDef mainWindow)
{
    var helper = mainWindow.FindMethod("NewDialogScrollContent") ?? CreateNewDialogScrollContent(module, mainWindow);
    UpdateDialogScrollHeight(helper);
    var confirm = FindAsyncMoveNext(mainWindow, "ConfirmDialogAsync")
        ?? mainWindow.FindMethod("ConfirmDialogAsync")
        ?? throw new InvalidOperationException("ConfirmDialogAsync not found.");

    var instructions = confirm.Body.Instructions;
    if (instructions.Any(instruction => instruction.Operand == helper))
        return;

    var setContentIndex = instructions.ToList().FindIndex(instruction =>
        (instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Call)
        && instruction.Operand is IMethod method
        && method.Name == "set_Content");
    if (setContentIndex < 0)
        throw new InvalidOperationException("ContentDialog.set_Content call not found.");

    var contentLoadIndex = setContentIndex - 1;
    while (contentLoadIndex >= 0 && !IsConfirmDialogContentLoad(instructions[contentLoadIndex]))
        contentLoadIndex--;
    if (contentLoadIndex < 0)
        throw new InvalidOperationException("ConfirmDialogAsync content argument load not found.");

    instructions.Insert(contentLoadIndex, Instruction.Create(OpCodes.Ldarg_0));
    if (confirm.HasThis && confirm.DeclaringType != mainWindow)
        instructions.Insert(contentLoadIndex + 1, Instruction.Create(OpCodes.Ldfld, confirm.DeclaringType.Fields.First(field => field.FieldType.FullName == mainWindow.FullName)));
    instructions.Insert(contentLoadIndex + (confirm.HasThis && confirm.DeclaringType != mainWindow ? 3 : 2), Instruction.Create(OpCodes.Call, helper));
    confirm.Body.OptimizeBranches();
    confirm.Body.OptimizeMacros();
}

static void PatchMoveTouchpadButton(ModuleDef module, TypeDef mainWindow)
{
    var moveNext = FindAsyncMoveNext(mainWindow, "EditActionAsync")
        ?? throw new InvalidOperationException("EditActionAsync.MoveNext not found.");
    var instructions = moveNext.Body.Instructions;

    var trainButtonStoreIndex = instructions.ToList().FindIndex(instruction =>
        instruction.OpCode == OpCodes.Stloc_S
        && instruction.Operand is Local local
        && instructions.Take(instructions.IndexOf(instruction)).Reverse().Take(6).Any(previous =>
            previous.OpCode == OpCodes.Call
            && previous.Operand is IMethod method
            && method.Name == "NewPillButton"));
    if (trainButtonStoreIndex < 0)
    {
        Console.WriteLine("Touchpad button local store not found; skipping this assembly.");
        return;
    }

    var trainButtonLocal = (Local)instructions[trainButtonStoreIndex].Operand!;
    var changed = SetElementAlignmentAndMargin(module, instructions, trainButtonStoreIndex, trainButtonLocal, -38d, 32d);

    if (!SetActivateWindowCheckBoxAlignmentAndMargin(module, instructions, -32d, 32d))
        Console.WriteLine("Activate-window checkbox local store not found; skipping that control.");
    else
        changed = true;

    if (!changed)
        return;

    moveNext.Body.OptimizeBranches();
    moveNext.Body.OptimizeMacros();
}

static void PatchHideContinuousGestureBox(TypeDef mainWindow)
{
    var moveNext = FindAsyncMoveNext(mainWindow, "EditActionAsync")
        ?? throw new InvalidOperationException("EditActionAsync.MoveNext not found.");
    var instructions = moveNext.Body.Instructions;
    var module = mainWindow.Module;
    var borderType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.Border");
    var borderCtor = module.Import(new MemberRefUser(
        module,
        ".ctor",
        MethodSig.CreateInstance(module.CorLibTypes.Void),
        borderType));

    for (var i = 0; i < instructions.Count; i++)
    {
        if ((instructions[i].OpCode != OpCodes.Callvirt && instructions[i].OpCode != OpCodes.Call)
            || instructions[i].Operand is not IMethod method
            || method.Name != "Add"
            || i < 2)
            continue;

        var loadContinuousGesture = instructions[i - 1];
        if (loadContinuousGesture.OpCode != OpCodes.Ldfld
            || loadContinuousGesture.Operand is not IField field
            || !field.Name.String.Contains("continuousGestureJson", StringComparison.Ordinal))
            continue;

        instructions[i - 2] = Instruction.Create(OpCodes.Newobj, borderCtor);
        instructions[i - 1] = Instruction.Create(OpCodes.Nop);
        moveNext.Body.OptimizeBranches();
        moveNext.Body.OptimizeMacros();
        return;
    }

    Console.WriteLine("Continuous gesture box add call not found; skipping.");
}

static void PatchSteadyRecognitionCard(ModuleDef module, TypeDef mainWindow)
{
    RestoreRecognitionToggle(module, mainWindow);
    PatchActionToggleRefresh(mainWindow);
    return;

    var method = mainWindow.FindMethod("NewRecognitionCard")
        ?? throw new InvalidOperationException("NewRecognitionCard not found.");
    if (method.Body is null)
        return;

    var instructions = method.Body.Instructions;
    var textBlockType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.TextBlock");
    var textBlockCtor = module.Import(new MemberRefUser(
        module,
        ".ctor",
        MethodSig.CreateInstance(module.CorLibTypes.Void),
        textBlockType));
    var setText = module.Import(new MemberRefUser(
        module,
        "set_Text",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String),
        textBlockType));

    var toggleCtorIndex = instructions.ToList().FindIndex(instruction =>
        instruction.OpCode == OpCodes.Newobj
        && instruction.Operand is IMethod called
        && called.DeclaringType.FullName == "Microsoft.UI.Xaml.Controls.ToggleSwitch");
    if (toggleCtorIndex < 0)
    {
        Console.WriteLine("Recognition ToggleSwitch constructor not found; skipping.");
        return;
    }

    var storeIndex = -1;
    for (var i = toggleCtorIndex; i < Math.Min(instructions.Count, toggleCtorIndex + 32); i++)
    {
        if (instructions[i].IsStloc())
        {
            storeIndex = i;
            break;
        }
    }

    if (storeIndex < 0)
        throw new InvalidOperationException("Recognition status local store not found.");

    var statusLocal = instructions[storeIndex].GetLocal(method.Body.Variables);
    statusLocal.Type = new ClassSig(textBlockType);

    instructions[toggleCtorIndex] = Instruction.Create(OpCodes.Newobj, textBlockCtor);
    instructions[toggleCtorIndex + 1] = Instruction.Create(OpCodes.Dup);
    instructions[toggleCtorIndex + 2] = Instruction.Create(OpCodes.Ldstr, "开");
    instructions[toggleCtorIndex + 3] = Instruction.Create(OpCodes.Callvirt, setText);
    for (var i = toggleCtorIndex + 4; i < storeIndex - 3; i++)
        instructions[i] = Instruction.Create(OpCodes.Nop);

    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();
}

static void FreezeRecognitionToggle(ModuleDef module, TypeDef mainWindow)
{
    var method = mainWindow.FindMethod("NewRecognitionCard")
        ?? throw new InvalidOperationException("NewRecognitionCard not found.");
    if (method.Body is null)
        return;

    var instructions = method.Body.Instructions;
    var toggleCtorIndex = instructions.ToList().FindIndex(instruction =>
        instruction.OpCode == OpCodes.Newobj
        && instruction.Operand is IMethod called
        && called.DeclaringType.FullName == "Microsoft.UI.Xaml.Controls.ToggleSwitch");
    if (toggleCtorIndex < 0)
        return;

    var storeIndex = -1;
    for (var i = toggleCtorIndex; i < Math.Min(instructions.Count, toggleCtorIndex + 32); i++)
    {
        if (instructions[i].IsStloc())
        {
            storeIndex = i;
            break;
        }
    }

    if (storeIndex < 0)
        throw new InvalidOperationException("Recognition status local store not found.");

    var textBlockType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.TextBlock");
    var textBlockCtor = module.Import(new MemberRefUser(
        module,
        ".ctor",
        MethodSig.CreateInstance(module.CorLibTypes.Void),
        textBlockType));
    var setText = module.Import(new MemberRefUser(
        module,
        "set_Text",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String),
        textBlockType));
    var setVerticalAlignment = module.Import(new MemberRefUser(
        module,
        "set_VerticalAlignment",
        MethodSig.CreateInstance(module.CorLibTypes.Void, new ValueTypeSig(module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.VerticalAlignment"))),
        module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.FrameworkElement")));

    var statusLocal = instructions[storeIndex].GetLocal(method.Body.Variables);
    statusLocal.Type = new ClassSig(textBlockType);
    instructions[toggleCtorIndex] = Instruction.Create(OpCodes.Newobj, textBlockCtor);
    instructions[toggleCtorIndex + 1] = Instruction.Create(OpCodes.Dup);
    instructions[toggleCtorIndex + 2] = Instruction.Create(OpCodes.Ldstr, "开");
    instructions[toggleCtorIndex + 3] = Instruction.Create(OpCodes.Callvirt, setText);
    instructions[toggleCtorIndex + 4] = Instruction.Create(OpCodes.Dup);
    instructions[toggleCtorIndex + 5] = Instruction.Create(OpCodes.Ldc_I4_1);
    instructions[toggleCtorIndex + 6] = Instruction.Create(OpCodes.Callvirt, setVerticalAlignment);
    for (var i = toggleCtorIndex + 7; i < storeIndex; i++)
        instructions[i] = Instruction.Create(OpCodes.Nop);

    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();
}

static void RestoreRecognitionToggle(ModuleDef module, TypeDef mainWindow)
{
    var method = mainWindow.FindMethod("NewRecognitionCard")
        ?? throw new InvalidOperationException("NewRecognitionCard not found.");
    if (method.Body is null)
        return;

    var instructions = method.Body.Instructions;
    if (instructions.Any(instruction =>
        instruction.OpCode == OpCodes.Newobj
        && instruction.Operand is IMethod called
        && called.DeclaringType.FullName == "Microsoft.UI.Xaml.Controls.ToggleSwitch"))
        return;

    var openTextIndex = instructions.ToList().FindIndex(instruction =>
        instruction.OpCode == OpCodes.Ldstr
        && instruction.Operand is string text
        && text == "开");
    if (openTextIndex < 1)
    {
        Console.WriteLine("Recognition static text marker not found; skipping toggle restore.");
        return;
    }

    var ctorIndex = -1;
    for (var i = openTextIndex; i >= Math.Max(0, openTextIndex - 6); i--)
    {
        if (instructions[i].OpCode == OpCodes.Newobj)
        {
            ctorIndex = i;
            break;
        }
    }

    if (ctorIndex < 0)
    {
        Console.WriteLine("Recognition static text constructor not found; skipping toggle restore.");
        return;
    }

    var storeIndex = -1;
    for (var i = ctorIndex; i < Math.Min(instructions.Count, ctorIndex + 32); i++)
    {
        if (instructions[i].IsStloc())
        {
            storeIndex = i;
            break;
        }
    }

    if (storeIndex < 0)
        throw new InvalidOperationException("Recognition status local store not found.");

    var toggleSwitchType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.ToggleSwitch");
    var toggleSwitchCtor = module.Import(new MemberRefUser(
        module,
        ".ctor",
        MethodSig.CreateInstance(module.CorLibTypes.Void),
        toggleSwitchType));
    var setIsOn = module.Import(new MemberRefUser(
        module,
        "set_IsOn",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Boolean),
        toggleSwitchType));
    var setOnContent = module.Import(new MemberRefUser(
        module,
        "set_OnContent",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Object),
        toggleSwitchType));
    var setOffContent = module.Import(new MemberRefUser(
        module,
        "set_OffContent",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Object),
        toggleSwitchType));

    var statusLocal = instructions[storeIndex].GetLocal(method.Body.Variables);
    statusLocal.Type = new ClassSig(toggleSwitchType);

    instructions[ctorIndex] = Instruction.Create(OpCodes.Newobj, toggleSwitchCtor);
    instructions[ctorIndex + 1] = Instruction.Create(OpCodes.Dup);
    instructions[ctorIndex + 2] = LdcI4(1);
    instructions[ctorIndex + 3] = Instruction.Create(OpCodes.Callvirt, setIsOn);
    instructions[ctorIndex + 4] = Instruction.Create(OpCodes.Dup);
    instructions[ctorIndex + 5] = Instruction.Create(OpCodes.Ldstr, "开");
    instructions[ctorIndex + 6] = Instruction.Create(OpCodes.Callvirt, setOnContent);
    instructions[ctorIndex + 7] = Instruction.Create(OpCodes.Dup);
    instructions[ctorIndex + 8] = Instruction.Create(OpCodes.Ldstr, "关");
    instructions[ctorIndex + 9] = Instruction.Create(OpCodes.Callvirt, setOffContent);
    for (var i = ctorIndex + 10; i < storeIndex; i++)
        instructions[i] = Instruction.Create(OpCodes.Nop);

    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();
}

static void PatchActionToggleRefresh(TypeDef mainWindow)
{
    var toggleEnabled = mainWindow.FindMethod("ToggleEnabledAsync");
    var reloadActionDataOnly = mainWindow.FindMethod("ReloadActionDataOnly");
    var reloadData = mainWindow.FindMethod("ReloadData");
    if (toggleEnabled?.Body is null || reloadActionDataOnly is null || reloadData is null)
        return;

    var changed = false;
    var instructions = toggleEnabled.Body.Instructions;
    for (var index = 0; index < instructions.Count; index++)
    {
        var instruction = instructions[index];
        if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
            && instruction.Operand is IMethod methodRef
            && (methodRef.FullName == reloadData.FullName || methodRef.FullName == reloadActionDataOnly.FullName))
        {
            if (index > 0 && instructions[index - 1].OpCode == OpCodes.Ldarg_0)
                instructions[index - 1].OpCode = OpCodes.Nop;
            instruction.OpCode = OpCodes.Nop;
            instruction.Operand = null;
            changed = true;
        }
    }

    if (changed)
    {
        toggleEnabled.Body.OptimizeBranches();
        toggleEnabled.Body.OptimizeMacros();
    }
}

static void PatchDaemonRecognitionCommands(ModuleDef module, TypeDef messageProcessor)
{
    var nested = messageProcessor.NestedTypes.FirstOrDefault(type => type.Name.String.Contains("DisplayClass", StringComparison.Ordinal))
        ?? throw new InvalidOperationException("MessageProcessor display class not found.");
    var method = nested.Methods.FirstOrDefault(method => method.Name.String.Contains("<ProcessMessages>b__", StringComparison.Ordinal))
        ?? throw new InvalidOperationException("MessageProcessor callback not found.");
    if (method.Body is null)
        return;
    if (method.Body.Instructions.Any(instruction => instruction.Operand is string marker && marker == "Codex.RecognitionCommands"))
        return;

    var pointCaptureType = module.GetTypes().First(type => type.FullName == "GestureSign.Daemon.Input.PointCapture");
    var captureModeType = module.GetTypeRefs().First(type => type.FullName == "GestureSign.Common.Input.CaptureMode");
    var getInstance = pointCaptureType.FindMethod("get_Instance") ?? throw new InvalidOperationException("PointCapture.Instance not found.");
    var setMode = pointCaptureType.FindMethod("set_Mode") ?? throw new InvalidOperationException("PointCapture.Mode setter not found.");
    var instructions = method.Body.Instructions;

    var ret = instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Ret)
        ?? throw new InvalidOperationException("MessageProcessor callback ret not found.");
    var insertIndex = instructions.IndexOf(ret);
    var enableBlock = Instruction.Create(OpCodes.Ldstr, "Codex.RecognitionCommands");
    var disableBlock = Instruction.Create(OpCodes.Call, getInstance);
    var originalRet = ret;

    instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Ldloc_0));
    instructions.Insert(insertIndex++, LdcI4(9));
    instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Beq_S, enableBlock));
    instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Ldloc_0));
    instructions.Insert(insertIndex++, LdcI4(10));
    instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Bne_Un_S, originalRet));
    instructions.Insert(insertIndex++, disableBlock);
    instructions.Insert(insertIndex++, LdcI4(2));
    instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Callvirt, setMode));
    instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Ret));
    instructions.Insert(insertIndex++, enableBlock);
    instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Pop));
    instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Call, getInstance));
    instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Ldc_I4_0));
    instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Callvirt, setMode));
    instructions.Insert(insertIndex, Instruction.Create(OpCodes.Ret));

    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();
}

static void PatchRecognitionToggleCommands(ModuleDef module, TypeDef mainWindow)
{
    var method = mainWindow.FindMethod("NewRecognitionCard")
        ?? throw new InvalidOperationException("NewRecognitionCard not found.");
    if (method.Body is null)
        return;
    if (method.Body.Instructions.Any(instruction => instruction.Operand is IMethod called && called.Name == "RecognitionToggle_Toggled"))
        return;

    var handler = mainWindow.FindMethod("RecognitionToggle_Toggled") ?? CreateRecognitionToggleToggled(module, mainWindow);
    var instructions = method.Body.Instructions;
    var toggleSwitchType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.ToggleSwitch");
    var routedEventHandlerType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.RoutedEventHandler");
    var routedEventHandlerSig = new ClassSig(routedEventHandlerType);
    var handlerCtor = module.Import(new MemberRefUser(
        module,
        ".ctor",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Object, module.CorLibTypes.IntPtr),
        routedEventHandlerType));
    var addToggled = module.Import(new MemberRefUser(
        module,
        "add_Toggled",
        MethodSig.CreateInstance(module.CorLibTypes.Void, routedEventHandlerSig),
        toggleSwitchType));

    var storeIndex = instructions.ToList().FindIndex(instruction =>
        instruction.IsStloc()
        && instructions.Take(instructions.IndexOf(instruction)).Reverse().Take(24).Any(previous =>
            previous.OpCode == OpCodes.Newobj
            && previous.Operand is IMethod called
            && called.DeclaringType.FullName == "Microsoft.UI.Xaml.Controls.ToggleSwitch"));
    if (storeIndex < 0)
        return;

    var toggleLocal = instructions[storeIndex].GetLocal(method.Body.Variables);
    var insertIndex = storeIndex + 1;
    instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Ldloc, toggleLocal));
    instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Ldarg_0));
    instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Ldftn, handler));
    instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Newobj, handlerCtor));
    instructions.Insert(insertIndex, Instruction.Create(OpCodes.Callvirt, addToggled));

    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();
}

static MethodDef CreateRecognitionToggleToggled(ModuleDef module, TypeDef mainWindow)
{
    var method = new MethodDefUser(
        "RecognitionToggle_Toggled",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Object, new ClassSig(module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.RoutedEventArgs"))),
        MethodImplAttributes.IL | MethodImplAttributes.Managed,
        MethodAttributes.Private | MethodAttributes.HideBySig);
    mainWindow.Methods.Add(method);

    var toggleSwitchType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.ToggleSwitch");
    var getIsOn = module.Import(new MemberRefUser(
        module,
        "get_IsOn",
        MethodSig.CreateInstance(module.CorLibTypes.Boolean),
        toggleSwitchType));
    var notifyDaemon = mainWindow.FindMethod("NotifyDaemonAsync") ?? throw new InvalidOperationException("NotifyDaemonAsync not found.");

    method.Body = new CilBody { InitLocals = true };
    method.Body.Variables.Add(new Local(new ClassSig(toggleSwitchType)));
    var i = method.Body.Instructions;
    var sendDisable = Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)10);
    var sendCommand = Instruction.Create(OpCodes.Call, notifyDaemon);
    var ret = Instruction.Create(OpCodes.Ret);

    i.Add(Instruction.Create(OpCodes.Ldarg_1));
    i.Add(Instruction.Create(OpCodes.Isinst, toggleSwitchType));
    i.Add(Instruction.Create(OpCodes.Stloc_0));
    i.Add(Instruction.Create(OpCodes.Ldloc_0));
    i.Add(Instruction.Create(OpCodes.Brfalse_S, ret));
    i.Add(Instruction.Create(OpCodes.Ldloc_0));
    i.Add(Instruction.Create(OpCodes.Callvirt, getIsOn));
    i.Add(Instruction.Create(OpCodes.Brfalse_S, sendDisable));
    i.Add(LdcI4(9));
    i.Add(Instruction.Create(OpCodes.Br_S, sendCommand));
    i.Add(sendDisable);
    i.Add(sendCommand);
    i.Add(Instruction.Create(OpCodes.Pop));
    i.Add(ret);

    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();
    return method;
}

static bool PatchOpenSettingsHotKeySave(ModuleDef module, TypeDef mainWindow)
{
    var newOpenRow = mainWindow.FindMethod("NewOpenSettingsHotKeyRow");
    if (newOpenRow?.Body is null)
        return false;

    var updateOptionNow = mainWindow.FindMethod("UpdateOptionAndReloadNow")
        ?? throw new InvalidOperationException("UpdateOptionAndReloadNow not found.");

    var frameworkElementType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.FrameworkElement");
    var setTag = module.Import(new MemberRefUser(
        module,
        "set_Tag",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Object),
        frameworkElementType));
    var getTag = module.Import(new MemberRefUser(
        module,
        "get_Tag",
        MethodSig.CreateInstance(module.CorLibTypes.Object),
        frameworkElementType));
    var stringEquals = module.Import(new MemberRefUser(
        module,
        "Equals",
        MethodSig.CreateStatic(module.CorLibTypes.Boolean, module.CorLibTypes.String, module.CorLibTypes.String, new ValueTypeSig(module.CorLibTypes.GetTypeRef("System", "StringComparison"))),
        module.CorLibTypes.String.TypeDefOrRef));
    var getText = module.Import(new MemberRefUser(
        module,
        "get_Text",
        MethodSig.CreateInstance(module.CorLibTypes.String),
        module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.TextBox")));

    var rowInstructions = newOpenRow.Body.Instructions;
    if (!rowInstructions.Any(instruction => instruction.OpCode == OpCodes.Ldstr && instruction.Operand is string text && text == "OpenSettingsHotKeyTag"))
    {
        var storeSettingsIndex = rowInstructions.ToList().FindIndex(instruction =>
            instruction.OpCode == OpCodes.Stfld
            && instruction.Operand is IField field
            && field.Name == "settings");
        if (storeSettingsIndex < 0)
            throw new InvalidOperationException("OpenSettings settings field store not found.");

        var displayClassLocal = rowInstructions[0].OpCode == OpCodes.Newobj && rowInstructions[1].IsStloc()
            ? rowInstructions[1].GetLocal(newOpenRow.Body!.Variables)
            : null;
        if (displayClassLocal is null)
            throw new InvalidOperationException("OpenSettings display class local not found.");

        var settingsField = (IField)rowInstructions[storeSettingsIndex].Operand!;
        var insert = storeSettingsIndex + 1;
        rowInstructions.Insert(insert++, Instruction.Create(OpCodes.Ldloc, displayClassLocal));
        rowInstructions.Insert(insert++, Instruction.Create(OpCodes.Ldfld, settingsField));
        rowInstructions.Insert(insert++, Instruction.Create(OpCodes.Ldstr, "OpenSettingsHotKeyTag"));
        rowInstructions.Insert(insert++, Instruction.Create(OpCodes.Callvirt, setTag));
        newOpenRow.Body!.OptimizeBranches();
        newOpenRow.Body.OptimizeMacros();
    }

    var lambda = mainWindow.NestedTypes.SelectMany(type => type.Methods)
        .FirstOrDefault(method => method.Name.String.Contains("<LowLevelKeyboardCallback>b__0", StringComparison.Ordinal) && method.Body is not null)
        ?? throw new InvalidOperationException("LowLevelKeyboardCallback save lambda not found.");

    if (lambda.Body!.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldstr && instruction.Operand is string text && text == "OpenSettingsHotKey"))
        return true;

    var lambdaType = lambda.DeclaringType;
    var ownerField = lambdaType.Fields.First(field => field.Name == "<>4__this");
    var settingsLambdaField = lambdaType.Fields.First(field => field.Name == "settings");
    var ret = lambda.Body.Instructions.Last(instruction => instruction.OpCode == OpCodes.Ret);

    var skipSave = Instruction.Create(OpCodes.Nop);
    var insertIndex = lambda.Body.Instructions.IndexOf(ret);
    var toInsert = new[]
    {
        Instruction.Create(OpCodes.Ldarg_0),
        Instruction.Create(OpCodes.Ldfld, settingsLambdaField),
        Instruction.Create(OpCodes.Callvirt, getTag),
        Instruction.Create(OpCodes.Isinst, module.CorLibTypes.String.TypeDefOrRef),
        Instruction.Create(OpCodes.Ldstr, "OpenSettingsHotKeyTag"),
        LdcI4(4),
        Instruction.Create(OpCodes.Call, stringEquals),
        Instruction.Create(OpCodes.Brfalse_S, skipSave),
        Instruction.Create(OpCodes.Ldarg_0),
        Instruction.Create(OpCodes.Ldfld, ownerField),
        Instruction.Create(OpCodes.Ldstr, "OpenSettingsHotKey"),
        Instruction.Create(OpCodes.Ldarg_0),
        Instruction.Create(OpCodes.Ldfld, settingsLambdaField),
        Instruction.Create(OpCodes.Callvirt, getText),
        Instruction.Create(OpCodes.Call, updateOptionNow),
        skipSave
    };
    foreach (var instruction in toInsert)
        lambda.Body.Instructions.Insert(insertIndex++, instruction);

    lambda.Body.OptimizeBranches();
    lambda.Body.OptimizeMacros();
    return true;
}

static void PatchDaemonOpenHotKeyParser(ModuleDef module, TypeDef hotKeyManager)
{
    var parseFirstKeyCode = hotKeyManager.FindMethod("ParseFirstKeyCode")
        ?? throw new InvalidOperationException("ParseFirstKeyCode not found.");
    if (parseFirstKeyCode.Body is null)
        return;

    if (parseFirstKeyCode.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldstr && instruction.Operand is string text && text.Contains("\\[?\\s*", StringComparison.Ordinal)))
        return;

    foreach (var instruction in parseFirstKeyCode.Body.Instructions)
    {
        if (instruction.OpCode == OpCodes.Ldstr
            && instruction.Operand is string text
            && text.Contains("\\[\\s*(\\d+)", StringComparison.Ordinal))
        {
            instruction.Operand = "\"KeyCode\"\\s*:\\s*\\[?\\s*(\\d+)";
            parseFirstKeyCode.Body.OptimizeBranches();
            parseFirstKeyCode.Body.OptimizeMacros();
            return;
        }
    }
}

static void PatchRecognitionSwitchOn(ModuleDef module, TypeDef mainWindow)
{
    var method = mainWindow.FindMethod("NewRecognitionCard")
        ?? throw new InvalidOperationException("NewRecognitionCard not found.");
    if (method.Body is null)
        return;

    var instructions = method.Body.Instructions;
    var toggleCtorIndex = instructions.ToList().FindIndex(instruction =>
        instruction.OpCode == OpCodes.Newobj
        && instruction.Operand is IMethod called
        && called.DeclaringType.FullName == "Microsoft.UI.Xaml.Controls.ToggleSwitch");
    if (toggleCtorIndex < 0)
    {
        Console.WriteLine("Recognition ToggleSwitch constructor not found; skipping.");
        return;
    }

    if (instructions.Skip(toggleCtorIndex).Take(48).Any(instruction =>
        instruction.Operand is IMethod methodRef && methodRef.Name == "set_IsOn"))
        return;

    var toggleSwitchType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.ToggleSwitch");
    var setIsOn = module.Import(new MemberRefUser(
        module,
        "set_IsOn",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Boolean),
        toggleSwitchType));

    var insertIndex = toggleCtorIndex + 1;
    instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Dup));
    instructions.Insert(insertIndex++, LdcI4(1));
    instructions.Insert(insertIndex, Instruction.Create(OpCodes.Callvirt, setIsOn));
    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();
}

static void PatchInstantVisualFeedbackColor(TypeDef mainWindow)
{
    var displayClass = mainWindow.NestedTypes.FirstOrDefault(type =>
        type.Fields.Any(field => field.Name == "committedValue")
        && type.Fields.Any(field => field.Name == "undo"))
        ?? mainWindow.Module.Types.SelectMany(type => type.NestedTypes).FirstOrDefault(type =>
            type.Fields.Any(field => field.Name == "committedValue")
            && type.Fields.Any(field => field.Name == "undo"));
    if (displayClass is null)
    {
        Console.WriteLine("Visual feedback color display class not found; skipping this assembly.");
        return;
    }
    var applyPreview = displayClass.Methods.FirstOrDefault(method =>
        method.Name.String.Contains("ApplyPreview", StringComparison.Ordinal)
        && method.Body is not null)
        ?? throw new InvalidOperationException("ApplyPreview local function not found.");

    var committedValueField = displayClass.Fields.First(field => field.Name == "committedValue");
    var instructions = applyPreview.Body!.Instructions;
    var setVisibilityIndex = instructions.ToList().FindIndex(instruction =>
        (instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Call)
        && instruction.Operand is IMethod method
        && method.Name == "set_Visibility");
    if (setVisibilityIndex >= 0 && setVisibilityIndex >= 9)
    {
        instructions[setVisibilityIndex - 9] = Instruction.Create(OpCodes.Nop);
        instructions[setVisibilityIndex - 8] = Instruction.Create(OpCodes.Nop);
        instructions[setVisibilityIndex - 7] = Instruction.Create(OpCodes.Nop);
        instructions[setVisibilityIndex - 6] = Instruction.Create(OpCodes.Nop);
        instructions[setVisibilityIndex - 5] = Instruction.Create(OpCodes.Nop);
        instructions[setVisibilityIndex - 4] = Instruction.Create(OpCodes.Nop);
        instructions[setVisibilityIndex - 3] = Instruction.Create(OpCodes.Nop);
        instructions[setVisibilityIndex - 2] = Instruction.Create(OpCodes.Nop);
        instructions[setVisibilityIndex - 1] = LdcI4(1);
    }

    var updateIndex = instructions.ToList().FindIndex(instruction =>
        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
        && instruction.Operand is IMethod method
        && method.Name == "UpdateOptionAndReloadNow");
    if (updateIndex >= 0)
    {
        var insertIndex = updateIndex + 1;
        if (!instructions.Skip(updateIndex + 1).Take(4).Any(instruction => instruction.Operand is IField field && field.Name == committedValueField.Name))
        {
            instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Ldarg_0));
            instructions.Insert(insertIndex++, Instruction.Create(OpCodes.Ldloc_0));
            instructions.Insert(insertIndex, Instruction.Create(OpCodes.Stfld, committedValueField));
        }
    }

    applyPreview.Body.OptimizeBranches();
    applyPreview.Body.OptimizeMacros();

    var colorRow = mainWindow.FindMethod("NewVisualFeedbackColorRow");
    if (colorRow?.Body is not null)
    {
        var rowInstructions = colorRow.Body.Instructions;
        var saveButtonStore = rowInstructions.ToList().FindIndex(instruction =>
            instruction.OpCode == OpCodes.Stloc_S
            && instruction.Operand is Local
            && rowInstructions.Take(rowInstructions.IndexOf(instruction)).Reverse().Take(8).Any(previous =>
                previous.OpCode == OpCodes.Ldstr
                && previous.Operand is string text
                && text.Contains("保存颜色", StringComparison.Ordinal)));
        if (saveButtonStore >= 0 && rowInstructions[saveButtonStore].Operand is Local saveLocal)
        {
            var setSaveVisibility = rowInstructions.ToList().FindIndex(saveButtonStore + 1, instruction =>
                (instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Call)
                && instruction.Operand is IMethod method
                && method.Name == "set_Visibility"
                && rowInstructions.Take(rowInstructions.IndexOf(instruction)).Reverse().Take(4).Any(previous => IsLdloc(previous, saveLocal)));
            if (setSaveVisibility < 0)
            {
                var addSaveIndex = rowInstructions.ToList().FindIndex(saveButtonStore + 1, instruction =>
                    (instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Call)
                    && instruction.Operand is IMethod method
                    && method.Name == "Add"
                    && rowInstructions.Take(rowInstructions.IndexOf(instruction)).Reverse().Take(4).Any(previous => IsLdloc(previous, saveLocal)));
                if (addSaveIndex > 0)
                {
                    rowInstructions.Insert(addSaveIndex - 1, Instruction.Create(OpCodes.Ldloc, saveLocal));
                    rowInstructions.Insert(addSaveIndex, LdcI4(1));
                    rowInstructions.Insert(addSaveIndex + 1, Instruction.Create(OpCodes.Callvirt, rowInstructions
                        .Select(instruction => instruction.Operand)
                        .OfType<IMethod>()
                        .First(method => method.Name == "set_Visibility")));
                }
            }
        }

        for (var i = 0; i < rowInstructions.Count; i++)
        {
            if (rowInstructions[i].OpCode == OpCodes.Ldstr
                && rowInstructions[i].Operand is string text
                && text.Contains("选择后只是预览", StringComparison.Ordinal))
                rowInstructions[i].Operand = "点击颜色后立即生效。";
        }

        colorRow.Body.OptimizeBranches();
        colorRow.Body.OptimizeMacros();
    }
}

static bool SetElementAlignmentAndMargin(ModuleDef module, IList<Instruction> instructions, int storeIndex, Local elementLocal, double top, double right)
{
    var frameworkElementType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.FrameworkElement");
    var horizontalAlignmentType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.HorizontalAlignment");
    var thicknessType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Thickness");

    var setHorizontalAlignment = module.Import(new MemberRefUser(
        module,
        "set_HorizontalAlignment",
        MethodSig.CreateInstance(module.CorLibTypes.Void, new ValueTypeSig(horizontalAlignmentType)),
        frameworkElementType));
    var thicknessCtor = module.Import(new MemberRefUser(
        module,
        ".ctor",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Double, module.CorLibTypes.Double, module.CorLibTypes.Double, module.CorLibTypes.Double),
        thicknessType));
    var setMargin = module.Import(new MemberRefUser(
        module,
        "set_Margin",
        MethodSig.CreateInstance(module.CorLibTypes.Void, new ValueTypeSig(thicknessType)),
        frameworkElementType));

    for (var i = storeIndex + 1; i < Math.Min(instructions.Count, storeIndex + 28); i++)
    {
        if ((instructions[i].OpCode == OpCodes.Callvirt || instructions[i].OpCode == OpCodes.Call)
            && instructions[i].Operand is IMethod method
            && method.Name == "set_Margin"
            && i >= 6
            && IsLdloc(instructions[i - 6], elementLocal)
            && instructions[i - 5].OpCode == OpCodes.Ldc_R8
            && instructions[i - 4].OpCode == OpCodes.Ldc_R8
            && instructions[i - 3].OpCode == OpCodes.Ldc_R8
            && instructions[i - 2].OpCode == OpCodes.Ldc_R8
            && instructions[i - 1].OpCode == OpCodes.Newobj)
        {
            instructions[i - 5].Operand = 0d;
            instructions[i - 4].Operand = top;
            instructions[i - 3].Operand = right;
            instructions[i - 2].Operand = 0d;
            return true;
        }
    }

    var index = storeIndex + 1;
    instructions.Insert(index++, Instruction.Create(OpCodes.Ldloc, elementLocal));
    instructions.Insert(index++, LdcI4(2));
    instructions.Insert(index++, Instruction.Create(OpCodes.Callvirt, setHorizontalAlignment));
    instructions.Insert(index++, Instruction.Create(OpCodes.Ldloc, elementLocal));
    instructions.Insert(index++, Instruction.Create(OpCodes.Ldc_R8, 0d));
    instructions.Insert(index++, Instruction.Create(OpCodes.Ldc_R8, top));
    instructions.Insert(index++, Instruction.Create(OpCodes.Ldc_R8, right));
    instructions.Insert(index++, Instruction.Create(OpCodes.Ldc_R8, 0d));
    instructions.Insert(index++, Instruction.Create(OpCodes.Newobj, thicknessCtor));
    instructions.Insert(index, Instruction.Create(OpCodes.Callvirt, setMargin));
    return true;
}

static bool SetActivateWindowCheckBoxAlignmentAndMargin(ModuleDef module, IList<Instruction> instructions, double top, double right)
{
    var getActivateWindowIndex = instructions.ToList().FindIndex(instruction =>
        (instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Call)
        && instruction.Operand is IMethod method
        && method.Name == "get_ActivateWindow");
    if (getActivateWindowIndex < 0)
        return false;

    var setMarginIndex = -1;
    for (var i = getActivateWindowIndex; i < Math.Min(instructions.Count, getActivateWindowIndex + 24); i++)
    {
        if ((instructions[i].OpCode == OpCodes.Callvirt || instructions[i].OpCode == OpCodes.Call)
            && instructions[i].Operand is IMethod method
            && method.Name == "set_Margin"
            && i >= 6
            && instructions[i - 6].OpCode == OpCodes.Dup
            && instructions[i - 5].OpCode == OpCodes.Ldc_R8
            && instructions[i - 4].OpCode == OpCodes.Ldc_R8
            && instructions[i - 3].OpCode == OpCodes.Ldc_R8
            && instructions[i - 2].OpCode == OpCodes.Ldc_R8
            && instructions[i - 1].OpCode == OpCodes.Newobj)
        {
            setMarginIndex = i;
            break;
        }
    }

    if (setMarginIndex < 0)
        return false;

    var alreadyAligned = instructions
        .Skip(Math.Max(0, getActivateWindowIndex - 2))
        .Take(setMarginIndex - getActivateWindowIndex + 2)
        .Any(instruction => instruction.Operand is IMethod method && method.Name == "set_HorizontalAlignment");

    instructions[setMarginIndex - 5].Operand = 0d;
    instructions[setMarginIndex - 4].Operand = top;
    instructions[setMarginIndex - 3].Operand = right;
    instructions[setMarginIndex - 2].Operand = 0d;

    if (!alreadyAligned)
    {
        var frameworkElementType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.FrameworkElement");
        var horizontalAlignmentType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.HorizontalAlignment");
        var setHorizontalAlignment = module.Import(new MemberRefUser(
            module,
            "set_HorizontalAlignment",
            MethodSig.CreateInstance(module.CorLibTypes.Void, new ValueTypeSig(horizontalAlignmentType)),
            frameworkElementType));

        instructions.Insert(setMarginIndex - 6, Instruction.Create(OpCodes.Dup));
        instructions.Insert(setMarginIndex - 5, LdcI4(2));
        instructions.Insert(setMarginIndex - 4, Instruction.Create(OpCodes.Callvirt, setHorizontalAlignment));
    }

    return true;
}

static bool IsLdloc(Instruction instruction, Local local)
{
    return instruction.OpCode.Code switch
    {
        Code.Ldloc_0 => local.Index == 0,
        Code.Ldloc_1 => local.Index == 1,
        Code.Ldloc_2 => local.Index == 2,
        Code.Ldloc_3 => local.Index == 3,
        Code.Ldloc_S or Code.Ldloc => instruction.Operand == local,
        _ => false
    };
}

static void UpdateDialogScrollHeight(MethodDef helper)
{
    if (helper.Body is null)
        return;

    foreach (var instruction in helper.Body.Instructions)
    {
        if (instruction.OpCode == OpCodes.Ldc_R8 && instruction.Operand is double value)
        {
            instruction.Operand = value switch
            {
                520d => 640d,
                620d => 760d,
                320d => 180d,
                280d => 320d,
                _ => value
            };
        }
    }

    helper.Body.OptimizeBranches();
    helper.Body.OptimizeMacros();
}

static MethodDef? FindAsyncMoveNext(TypeDef owner, string asyncMethodName)
{
    var nestedTypeName = $"<{asyncMethodName}>d__";
    return owner.NestedTypes
        .FirstOrDefault(type => type.Name.String.StartsWith(nestedTypeName, StringComparison.Ordinal))
        ?.FindMethod("MoveNext");
}

static bool IsConfirmDialogContentLoad(Instruction instruction)
{
    if (instruction.OpCode == OpCodes.Ldarg_2)
        return true;

    return instruction.OpCode == OpCodes.Ldfld
        && instruction.Operand is IField field
        && field.Name == "content";
}

static MethodDef CreateNewDialogScrollContent(ModuleDef module, TypeDef mainWindow)
{
    var objectSig = module.CorLibTypes.Object;
    var scrollViewerType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.ScrollViewer");
    var frameworkElementType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.FrameworkElement");
    var rootField = mainWindow.FindField("Root") ?? throw new InvalidOperationException("Root field not found.");

    var helper = new MethodDefUser(
        "NewDialogScrollContent",
        MethodSig.CreateInstance(new ClassSig(scrollViewerType), objectSig),
        MethodImplAttributes.IL | MethodImplAttributes.Managed,
        MethodAttributes.Private | MethodAttributes.HideBySig);
    mainWindow.Methods.Add(helper);

    var getActualHeight = module.Import(new MemberRefUser(
        module,
        "get_ActualHeight",
        MethodSig.CreateInstance(module.CorLibTypes.Double),
        frameworkElementType));
    var mathMax = module.Import(new MemberRefUser(
        module,
        "Max",
        MethodSig.CreateStatic(module.CorLibTypes.Double, module.CorLibTypes.Double, module.CorLibTypes.Double),
        module.CorLibTypes.GetTypeRef("System", "Math")));
    var mathMin = module.Import(new MemberRefUser(
        module,
        "Min",
        MethodSig.CreateStatic(module.CorLibTypes.Double, module.CorLibTypes.Double, module.CorLibTypes.Double),
        module.CorLibTypes.GetTypeRef("System", "Math")));
    var scrollViewerCtor = module.Import(new MemberRefUser(
        module,
        ".ctor",
        MethodSig.CreateInstance(module.CorLibTypes.Void),
        scrollViewerType));
    var setContent = module.Import(new MemberRefUser(
        module,
        "set_Content",
        MethodSig.CreateInstance(module.CorLibTypes.Void, objectSig),
        scrollViewerType));
    var setMaxHeight = module.Import(new MemberRefUser(
        module,
        "set_MaxHeight",
        MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Double),
        frameworkElementType));
    var setVerticalScrollBarVisibility = module.Import(new MemberRefUser(
        module,
        "set_VerticalScrollBarVisibility",
        MethodSig.CreateInstance(module.CorLibTypes.Void, new ValueTypeSig(module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.ScrollBarVisibility"))),
        scrollViewerType));
    var setVerticalScrollMode = module.Import(new MemberRefUser(
        module,
        "set_VerticalScrollMode",
        MethodSig.CreateInstance(module.CorLibTypes.Void, new ValueTypeSig(module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.ScrollMode"))),
        scrollViewerType));
    var setHorizontalScrollBarVisibility = module.Import(new MemberRefUser(
        module,
        "set_HorizontalScrollBarVisibility",
        MethodSig.CreateInstance(module.CorLibTypes.Void, new ValueTypeSig(module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.ScrollBarVisibility"))),
        scrollViewerType));
    var setHorizontalScrollMode = module.Import(new MemberRefUser(
        module,
        "set_HorizontalScrollMode",
        MethodSig.CreateInstance(module.CorLibTypes.Void, new ValueTypeSig(module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.ScrollMode"))),
        scrollViewerType));
    var setZoomMode = module.Import(new MemberRefUser(
        module,
        "set_ZoomMode",
        MethodSig.CreateInstance(module.CorLibTypes.Void, new ValueTypeSig(module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.ZoomMode"))),
        scrollViewerType));

    var body = new CilBody { InitLocals = true };
    body.Variables.Add(new Local(module.CorLibTypes.Double)); // maxHeight
    body.Variables.Add(new Local(new ClassSig(scrollViewerType))); // scrollViewer
    helper.Body = body;
    var i = body.Instructions;
    var fallback = Instruction.Create(OpCodes.Ldc_R8, 640d);
    var afterHeight = Instruction.Create(OpCodes.Stloc_0);

    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ldfld, rootField));
    i.Add(Instruction.Create(OpCodes.Callvirt, getActualHeight));
    i.Add(Instruction.Create(OpCodes.Ldc_R8, 0d));
    i.Add(Instruction.Create(OpCodes.Ble_Un_S, fallback));
    i.Add(Instruction.Create(OpCodes.Ldc_R8, 320d));
    i.Add(Instruction.Create(OpCodes.Ldc_R8, 760d));
    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ldfld, rootField));
    i.Add(Instruction.Create(OpCodes.Callvirt, getActualHeight));
    i.Add(Instruction.Create(OpCodes.Ldc_R8, 180d));
    i.Add(Instruction.Create(OpCodes.Sub));
    i.Add(Instruction.Create(OpCodes.Call, mathMin));
    i.Add(Instruction.Create(OpCodes.Call, mathMax));
    i.Add(Instruction.Create(OpCodes.Br_S, afterHeight));
    i.Add(fallback);
    i.Add(afterHeight);
    i.Add(Instruction.Create(OpCodes.Newobj, scrollViewerCtor));
    i.Add(Instruction.Create(OpCodes.Stloc_1));
    i.Add(Instruction.Create(OpCodes.Ldloc_1));
    i.Add(Instruction.Create(OpCodes.Ldarg_1));
    i.Add(Instruction.Create(OpCodes.Callvirt, setContent));
    i.Add(Instruction.Create(OpCodes.Ldloc_1));
    i.Add(Instruction.Create(OpCodes.Ldloc_0));
    i.Add(Instruction.Create(OpCodes.Callvirt, setMaxHeight));
    i.Add(Instruction.Create(OpCodes.Ldloc_1));
    i.Add(LdcI4(1));
    i.Add(Instruction.Create(OpCodes.Callvirt, setVerticalScrollBarVisibility));
    i.Add(Instruction.Create(OpCodes.Ldloc_1));
    i.Add(LdcI4(1));
    i.Add(Instruction.Create(OpCodes.Callvirt, setVerticalScrollMode));
    i.Add(Instruction.Create(OpCodes.Ldloc_1));
    i.Add(LdcI4(0));
    i.Add(Instruction.Create(OpCodes.Callvirt, setHorizontalScrollBarVisibility));
    i.Add(Instruction.Create(OpCodes.Ldloc_1));
    i.Add(LdcI4(0));
    i.Add(Instruction.Create(OpCodes.Callvirt, setHorizontalScrollMode));
    i.Add(Instruction.Create(OpCodes.Ldloc_1));
    i.Add(LdcI4(0));
    i.Add(Instruction.Create(OpCodes.Callvirt, setZoomMode));
    i.Add(Instruction.Create(OpCodes.Ldloc_1));
    i.Add(Instruction.Create(OpCodes.Ret));

    helper.Body.OptimizeBranches();
    helper.Body.OptimizeMacros();
    return helper;
}

static MethodDef CreateMainWindowActivatedOverlay(ModuleDef module, TypeDef mainWindow, FieldDef activeField, MethodDef applyMethod)
{
    var method = new MethodDefUser(
        "MainWindow_ActivatedOverlay",
        MethodSig.CreateInstance(
            module.CorLibTypes.Void,
            module.CorLibTypes.Object,
            new ClassSig(GetOrCreateTypeRef(module, "Microsoft.UI.Xaml", "WindowActivatedEventArgs", "Microsoft.UI.Xaml"))),
        MethodImplAttributes.IL | MethodImplAttributes.Managed,
        MethodAttributes.Private | MethodAttributes.HideBySig);
    mainWindow.Methods.Add(method);

    var argsType = GetOrCreateTypeRef(module, "Microsoft.UI.Xaml", "WindowActivatedEventArgs", "Microsoft.UI.Xaml");
    var stateType = GetOrCreateTypeRef(module, "Microsoft.UI.Xaml", "WindowActivationState", "Microsoft.UI.Xaml");
    var getState = module.Import(new MemberRefUser(
        module,
        "get_WindowActivationState",
        MethodSig.CreateInstance(new ValueTypeSig(stateType)),
        argsType));

    var body = new CilBody();
    method.Body = body;
    var i = body.Instructions;
    var inactive = Instruction.Create(OpCodes.Ldc_I4_0);
    var setField = Instruction.Create(OpCodes.Stfld, activeField);

    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ldarg_2));
    i.Add(Instruction.Create(OpCodes.Callvirt, getState));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_1));
    i.Add(Instruction.Create(OpCodes.Beq_S, inactive));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_1));
    i.Add(Instruction.Create(OpCodes.Br_S, setField));
    i.Add(inactive);
    i.Add(setField);
    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Call, applyMethod));
    i.Add(Instruction.Create(OpCodes.Ret));

    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();
    return method;
}

static MethodDef CreateApplyMicaDimmingOverlay(ModuleDef module, TypeDef mainWindow)
{
    var method = new MethodDefUser(
        "ApplyMicaDimmingOverlay",
        MethodSig.CreateInstance(module.CorLibTypes.Void),
        MethodImplAttributes.IL | MethodImplAttributes.Managed,
        MethodAttributes.Private | MethodAttributes.HideBySig);
    mainWindow.Methods.Add(method);

    var colorType = module.GetTypeRefs().First(type => type.FullName == "Windows.UI.Color");
    var solidColorBrushType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Media.SolidColorBrush");
    var panelType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.Panel");
    var rootField = mainWindow.FindField("Root") ?? throw new InvalidOperationException("Root field not found.");
    var getIsDark = mainWindow.FindMethod("get_IsDark") ?? throw new InvalidOperationException("get_IsDark not found.");

    var fromArgb = module.Import(new MemberRefUser(
        module,
        "FromArgb",
        MethodSig.CreateStatic(
            new ValueTypeSig(colorType),
            module.CorLibTypes.Byte,
            module.CorLibTypes.Byte,
            module.CorLibTypes.Byte,
            module.CorLibTypes.Byte),
        colorType));

    var brushCtor = module.Import(new MemberRefUser(
        module,
        ".ctor",
        MethodSig.CreateInstance(module.CorLibTypes.Void, new ValueTypeSig(colorType)),
        solidColorBrushType));

    var setBackground = module.Import(new MemberRefUser(
        module,
        "set_Background",
        MethodSig.CreateInstance(module.CorLibTypes.Void, new ClassSig(module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Media.Brush"))),
        panelType));

    var body = new CilBody();
    method.Body = body;
    var i = body.Instructions;
    var lightOverlay = Instruction.Create(OpCodes.Ldarg_0);

    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ldfld, rootField));
    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Call, getIsDark));
    i.Add(Instruction.Create(OpCodes.Brfalse_S, lightOverlay));
    i.Add(LdcI4(51));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_0));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_0));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_0));
    i.Add(Instruction.Create(OpCodes.Call, fromArgb));
    var createBrush = Instruction.Create(OpCodes.Newobj, brushCtor);
    i.Add(Instruction.Create(OpCodes.Br_S, createBrush));
    i.Add(lightOverlay);
    i.Add(Instruction.Create(OpCodes.Pop));
    i.Add(LdcI4(77));
    i.Add(LdcI4(255));
    i.Add(LdcI4(255));
    i.Add(LdcI4(255));
    i.Add(Instruction.Create(OpCodes.Call, fromArgb));
    i.Add(createBrush);
    i.Add(Instruction.Create(OpCodes.Callvirt, setBackground));
    i.Add(Instruction.Create(OpCodes.Ret));

    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();
    return method;
}

static MethodDef CreateApplyMicaDimmingOverlayV2(ModuleDef module, TypeDef mainWindow, FieldDef activeField)
{
    var method = new MethodDefUser(
        "ApplyMicaDimmingOverlayV2",
        MethodSig.CreateInstance(module.CorLibTypes.Void),
        MethodImplAttributes.IL | MethodImplAttributes.Managed,
        MethodAttributes.Private | MethodAttributes.HideBySig);
    mainWindow.Methods.Add(method);

    var colorType = module.GetTypeRefs().First(type => type.FullName == "Windows.UI.Color");
    var solidColorBrushType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Media.SolidColorBrush");
    var panelType = module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Controls.Panel");
    var rootField = mainWindow.FindField("Root") ?? throw new InvalidOperationException("Root field not found.");
    var getIsDark = mainWindow.FindMethod("get_IsDark") ?? throw new InvalidOperationException("get_IsDark not found.");

    var fromArgb = module.Import(new MemberRefUser(
        module,
        "FromArgb",
        MethodSig.CreateStatic(
            new ValueTypeSig(colorType),
            module.CorLibTypes.Byte,
            module.CorLibTypes.Byte,
            module.CorLibTypes.Byte,
            module.CorLibTypes.Byte),
        colorType));
    var brushCtor = module.Import(new MemberRefUser(
        module,
        ".ctor",
        MethodSig.CreateInstance(module.CorLibTypes.Void, new ValueTypeSig(colorType)),
        solidColorBrushType));
    var setBackground = module.Import(new MemberRefUser(
        module,
        "set_Background",
        MethodSig.CreateInstance(module.CorLibTypes.Void, new ClassSig(module.GetTypeRefs().First(type => type.FullName == "Microsoft.UI.Xaml.Media.Brush"))),
        panelType));

    var body = new CilBody();
    method.Body = body;
    var i = body.Instructions;
    var lightOverlay = Instruction.Create(OpCodes.Ldarg_0);
    var unfocusedDark = Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)51);
    var createDark = Instruction.Create(OpCodes.Ldc_I4_0);
    var createBrush = Instruction.Create(OpCodes.Newobj, brushCtor);

    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ldfld, rootField));
    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Call, getIsDark));
    i.Add(Instruction.Create(OpCodes.Brfalse_S, lightOverlay));
    i.Add(Instruction.Create(OpCodes.Ldarg_0));
    i.Add(Instruction.Create(OpCodes.Ldfld, activeField));
    i.Add(Instruction.Create(OpCodes.Brfalse_S, unfocusedDark));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)26));
    i.Add(Instruction.Create(OpCodes.Br_S, createDark));
    i.Add(unfocusedDark);
    i.Add(createDark);
    i.Add(Instruction.Create(OpCodes.Ldc_I4_0));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_0));
    i.Add(Instruction.Create(OpCodes.Call, fromArgb));
    i.Add(Instruction.Create(OpCodes.Br_S, createBrush));
    i.Add(lightOverlay);
    i.Add(Instruction.Create(OpCodes.Pop));
    i.Add(Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)77));
    i.Add(LdcI4(255));
    i.Add(LdcI4(255));
    i.Add(LdcI4(255));
    i.Add(Instruction.Create(OpCodes.Call, fromArgb));
    i.Add(createBrush);
    i.Add(Instruction.Create(OpCodes.Callvirt, setBackground));
    i.Add(Instruction.Create(OpCodes.Ret));

    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();
    return method;
}

static Instruction LdcI4(int value)
    => value switch
    {
        0 => Instruction.Create(OpCodes.Ldc_I4_0),
        1 => Instruction.Create(OpCodes.Ldc_I4_1),
        2 => Instruction.Create(OpCodes.Ldc_I4_2),
        3 => Instruction.Create(OpCodes.Ldc_I4_3),
        4 => Instruction.Create(OpCodes.Ldc_I4_4),
        5 => Instruction.Create(OpCodes.Ldc_I4_5),
        6 => Instruction.Create(OpCodes.Ldc_I4_6),
        7 => Instruction.Create(OpCodes.Ldc_I4_7),
        8 => Instruction.Create(OpCodes.Ldc_I4_8),
        >= sbyte.MinValue and <= sbyte.MaxValue => Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)value),
        _ => Instruction.CreateLdcI4(value)
    };
