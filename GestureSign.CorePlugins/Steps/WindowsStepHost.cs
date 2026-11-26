using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using GestureSign.CorePlugins.Common;
using GestureSign.Foundation.Steps;
using Jint;
using WindowsInput;
using WindowsInput.Native;

namespace GestureSign.CorePlugins.Steps
{
    /// <summary>Runs the outside-world parts of a step program on Windows.</summary>
    internal sealed class WindowsStepHost : IStepHost
    {
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(5);

        private readonly PointInfo _pointInfo;

        public WindowsStepHost(PointInfo pointInfo)
        {
            _pointInfo = pointInfo;
        }

        public string WindowTitle
        {
            get { try { return _pointInfo.Window?.Title ?? ""; } catch { return ""; } }
        }

        public string ProcessName
        {
            get
            {
                try
                {
                    var window = _pointInfo.Window;
                    if (window == null) return "";
                    using var process = Process.GetProcessById(window.ProcessId);
                    return process.ProcessName + ".exe";
                }
                catch
                {
                    return "";
                }
            }
        }

        public string GetSelectedText() => SelectedTextCapture.Capture(_pointInfo);
        public string GetClipboardText() => SelectedTextCapture.GetText(_pointInfo);
        public void SetClipboardText(string text) => SelectedTextCapture.SetText(_pointInfo, text);
        public void PasteText(string text) => SelectedTextCapture.Paste(_pointInfo, text);

        public void TypeText(string text)
        {
            if (!string.IsNullOrEmpty(text))
                new InputSimulator().Keyboard.TextEntry(text);
        }

        /// <summary>"Ctrl+C", "Alt+Left Enter": groups separated by spaces, keys in a group by '+'.</summary>
        public void SendKeys(string keys)
        {
            var simulator = new InputSimulator();
            foreach (var group in (keys ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var modifiers = new List<VirtualKeyCode>();
                var normal = new List<VirtualKeyCode>();
                foreach (var token in group.Split('+').Select(t => t.Trim()).Where(t => t.Length > 0))
                {
                    var modifier = ParseModifier(token);
                    if (modifier.HasValue) modifiers.Add(modifier.Value);
                    else normal.Add(ParseKey(token));
                }
                if (normal.Count == 0 && modifiers.Count > 0)
                    simulator.Keyboard.KeyPress(modifiers.ToArray());
                else if (modifiers.Count == 0)
                    simulator.Keyboard.KeyPress(normal.ToArray());
                else
                    simulator.Keyboard.ModifiedKeyStroke(modifiers, normal);
                Thread.Sleep(30);
            }
        }

        private static VirtualKeyCode? ParseModifier(string token) => token.ToLowerInvariant() switch
        {
            "ctrl" or "control" => VirtualKeyCode.CONTROL,
            "shift" => VirtualKeyCode.SHIFT,
            "alt" => VirtualKeyCode.MENU,
            "win" or "windows" or "meta" => VirtualKeyCode.LWIN,
            _ => null
        };

        private static VirtualKeyCode ParseKey(string token)
        {
            if (token.Length == 1)
            {
                var c = char.ToUpperInvariant(token[0]);
                if (c >= 'A' && c <= 'Z') return VirtualKeyCode.VK_A + (c - 'A');
                if (c >= '0' && c <= '9') return VirtualKeyCode.VK_0 + (c - '0');
            }
            var alias = token.ToLowerInvariant() switch
            {
                "esc" => "Escape",
                "del" => "Delete",
                "ins" => "Insert",
                "pgup" => "PageUp",
                "pgdn" or "pgdown" => "PageDown",
                "backspace" or "bksp" => "Back",
                "enter" => "Return",
                _ => token
            };
            if (Enum.TryParse<Keys>(alias, true, out var key) && key != Keys.None)
                return (VirtualKeyCode)(int)key;
            throw new InvalidOperationException($"Unknown key: {token}");
        }

        public string RunProgram(string path, string arguments, bool wait, string workingDirectory, bool runAsAdmin)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("No program, file or URL was given.");
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            workingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? "" : Environment.ExpandEnvironmentVariables(workingDirectory.Trim().Trim('"'));
            // Output can only be read from a process started without the shell, which cannot elevate.
            if (!wait || runAsAdmin)
            {
                var startInfo = new ProcessStartInfo(path, arguments ?? "") { UseShellExecute = true, WorkingDirectory = workingDirectory };
                if (runAsAdmin)
                    startInfo.Verb = "runas";
                try
                {
                    using var started = Process.Start(startInfo);
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    // The user declined the administrator prompt.
                    Logging.LogMessage($"Step run program: elevation was cancelled. Path={path}");
                }
                return null;
            }
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(path, arguments ?? "")
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException($"{path} did not finish within 30 seconds.");
            }
            return output.GetAwaiter().GetResult().TrimEnd('\r', '\n');
        }

        public string LaunchOrActivate(string path, string arguments, string processName, string titleFilter, string whenActive, bool hideLaunchWindow)
            => AppSwitcher.LaunchOrActivate(path, arguments, processName, titleFilter, whenActive, hideLaunchWindow);

        public bool ShowQuickActions() => GestureSign.Foundation.Components.QuickActionsLauncher.Show();

        public string Http(string method, string url, string body, string contentType)
        {
            using var request = new HttpRequestMessage(new HttpMethod(string.IsNullOrWhiteSpace(method) ? "GET" : method.ToUpperInvariant()), url);
            if (!string.IsNullOrEmpty(body) && request.Method != HttpMethod.Get)
                request.Content = new StringContent(body, Encoding.UTF8, string.IsNullOrWhiteSpace(contentType) ? "text/plain" : contentType);
            using var response = HttpClient.Send(request);
            var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                Logging.LogMessage($"Step HTTP request returned {(int)response.StatusCode}. Url={url}");
            return text;
        }

        public void Notify(string text, string kind) => _pointInfo.Invoke(() => StepToast.ShowMessage(text, kind));

        public bool ShowMessageBox(string title, string text, string buttons)
        {
            var result = DialogResult.None;
            var kind = buttons switch
            {
                "OKCancel" => MessageBoxButtons.OKCancel,
                "YesNo" => MessageBoxButtons.YesNo,
                _ => MessageBoxButtons.OK
            };
            _pointInfo.Invoke(() =>
            {
                using var owner = StepPromptForm.NewTopmostOwner();
                result = System.Windows.Forms.MessageBox.Show(owner, text ?? "", string.IsNullOrWhiteSpace(title) ? "GestureSign" : title, kind);
            });
            return result == DialogResult.OK || result == DialogResult.Yes;
        }

        public string AskText(string prompt, string defaultValue, bool multiline)
        {
            string answer = null;
            _pointInfo.Invoke(() => answer = StepPromptForm.AskText(prompt, defaultValue, multiline));
            return answer;
        }

        public IReadOnlyList<int> AskChoice(string prompt, IReadOnlyList<string> labels, bool multiple)
        {
            if (labels == null || labels.Count == 0)
                return null;
            IReadOnlyList<int> answer = null;
            _pointInfo.Invoke(() => answer = StepPromptForm.AskChoice(prompt, labels, multiple));
            return answer;
        }

        public IReadOnlyDictionary<string, object> AskForm(string title, IReadOnlyList<StepFormField> fields)
        {
            IReadOnlyDictionary<string, object> answer = null;
            _pointInfo.Invoke(() => answer = StepPromptForm.AskForm(title, fields));
            return answer;
        }

        public IReadOnlyList<string> PickFiles(string mode, string title, string filter, string initialPath)
        {
            IReadOnlyList<string> answer = null;
            _pointInfo.Invoke(() => answer = StepPromptForm.PickFiles(mode, title, filter, initialPath));
            return answer;
        }

        public IReadOnlyList<string> GetClipboardFiles()
        {
            var files = new List<string>();
            _pointInfo.Invoke(() =>
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        if (Clipboard.ContainsFileDropList())
                            files.AddRange(Clipboard.GetFileDropList().Cast<string>());
                        return;
                    }
                    catch (ExternalException)
                    {
                        Thread.Sleep(50);
                    }
                }
            });
            return files;
        }

        public IReadOnlyList<string> GetSelectedFiles()
        {
            var window = _pointInfo.Window?.HWnd ?? IntPtr.Zero;
            string[] files = null;
            _pointInfo.Invoke(() =>
            {
                try { files = ExplorerSelection.SelectedFiles(window); }
                catch (Exception ex) { Logging.LogMessage($"Step selected files: Explorer query failed. Error={ex.GetType().Name}: {ex.Message}"); }
            });
            // Desktop, other file managers and Explorer windows that could not be read: copy with Ctrl+C.
            return files ?? SelectedTextCapture.CaptureFiles(_pointInfo);
        }

        public string GetExplorerFolder()
        {
            var window = _pointInfo.Window?.HWnd ?? IntPtr.Zero;
            string folder = null;
            _pointInfo.Invoke(() =>
            {
                try { folder = ExplorerSelection.CurrentFolder(window); }
                catch (Exception ex) { Logging.LogMessage($"Step current folder: Explorer query failed. Error={ex.GetType().Name}: {ex.Message}"); }
            });
            return folder ?? "";
        }

        // ----- files ---------------------------------------------------------

        private static Encoding EncodingFor(string name)
        {
            try
            {
                return (name ?? "").Trim().ToLowerInvariant() switch
                {
                    "utf8bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                    "gbk" => Encoding.GetEncoding("GB18030"),
                    "ansi" => Encoding.Default,
                    _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                };
            }
            catch
            {
                return new UTF8Encoding(false);
            }
        }

        public string ReadTextFile(string path)
        {
            try { return File.ReadAllText(path, EncodingFor(null)); }
            catch (Exception ex)
            {
                Logging.LogMessage($"Step read file failed. Path={path} Error={ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        public string WriteTextFile(string path, string content, bool append)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (append) File.AppendAllText(path, content ?? "", EncodingFor(null));
                else File.WriteAllText(path, content ?? "", EncodingFor(null));
                return null;
            }
            catch (Exception ex)
            {
                Logging.LogMessage($"Step write file failed. Path={path} Error={ex.GetType().Name}: {ex.Message}");
                return $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        public IReadOnlyDictionary<string, object> GetFileInfo(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                var directory = Directory.Exists(full);
                var file = !directory && File.Exists(full);
                if (!directory && !file) return null;
                FileSystemInfo info = directory ? new DirectoryInfo(full) : new FileInfo(full);
                var result = new Dictionary<string, object>
                {
                    ["exists"] = true,
                    ["size"] = directory ? 0d : (double)((FileInfo)info).Length,
                    ["modified"] = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    ["created"] = info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    ["name"] = info.Name,
                    ["nameWithoutExtension"] = Path.GetFileNameWithoutExtension(full),
                    ["extension"] = directory ? "" : Path.GetExtension(full).TrimStart('.'),
                    ["directory"] = Path.GetDirectoryName(full) ?? "",
                    ["fullName"] = full,
                    ["isDirectory"] = directory,
                    ["isReadOnly"] = (info.Attributes & FileAttributes.ReadOnly) != 0
                };
                return result;
            }
            catch (Exception ex)
            {
                Logging.LogMessage($"Step file info failed. Path={path} Error={ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        public string FileOperation(string operation, string source, string destination)
        {
            try
            {
                switch ((operation ?? "").Trim().ToLowerInvariant())
                {
                    case "copy":
                        if (Directory.Exists(source)) CopyDirectory(source, destination);
                        else File.Copy(source, destination, overwrite: true);
                        return null;
                    case "move":
                        if (Directory.Exists(source)) Directory.Move(source, destination);
                        else File.Move(source, destination, overwrite: true);
                        return null;
                    case "rename":
                        if (Directory.Exists(source)) Directory.Move(source, destination);
                        else File.Move(source, destination, overwrite: false);
                        return null;
                    case "delete":
                        if (Directory.Exists(source)) Directory.Delete(source, recursive: true);
                        else File.Delete(source);
                        return null;
                    default:
                        return $"Unknown file operation: {operation}";
                }
            }
            catch (Exception ex)
            {
                Logging.LogMessage($"Step file operation failed. Op={operation} Src={source} Dst={destination} Error={ex.GetType().Name}: {ex.Message}");
                return $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var entry in Directory.GetFileSystemEntries(source))
            {
                var target = Path.Combine(destination, Path.GetFileName(entry));
                if (Directory.Exists(entry)) CopyDirectory(entry, target);
                else File.Copy(entry, target, overwrite: true);
            }
        }

        // ----- AI ------------------------------------------------------------

        private static readonly HttpClient AiClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        /// <summary>"https://host/v1" or ".../chat/completions" both work.</summary>
        private static string ChatCompletionsUrl(string endpoint)
        {
            var url = endpoint.Trim().TrimEnd('/');
            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return url;
            // Versioned base addresses: ".../v1", ".../api/paas/v4", ".../compatible-mode/v1".
            if (System.Text.RegularExpressions.Regex.IsMatch(url, @"/v\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return url + "/chat/completions";
            return url + "/v1/chat/completions";
        }

        public string AiChat(string endpoint, string apiKey, string model, string systemPrompt, string prompt, double? temperature)
        {
            endpoint = string.IsNullOrWhiteSpace(endpoint) ? GestureSign.Common.Configuration.AppConfig.AiEndpoint : endpoint;
            // A key typed into the step itself is stored encrypted, like the global one.
            apiKey = GestureSign.Foundation.Security.SecretStore.Reveal(apiKey);
            apiKey = string.IsNullOrWhiteSpace(apiKey) ? GestureSign.Common.Configuration.AppConfig.AiApiKey : apiKey;
            model = string.IsNullOrWhiteSpace(model) ? GestureSign.Common.Configuration.AppConfig.AiModel : model;
            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException("AI is not set up: fill in the API address and model in AI settings (Action library page).");

            var messages = new List<object>();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
                messages.Add(new { role = "system", content = systemPrompt });
            messages.Add(new { role = "user", content = prompt ?? "" });
            var body = new Dictionary<string, object> { ["model"] = model, ["messages"] = messages, ["stream"] = false };
            if (temperature.HasValue)
                body["temperature"] = Math.Clamp(temperature.Value, 0, 2);

            using var request = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsUrl(endpoint))
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(apiKey))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey.Trim());
            using var response = AiClient.Send(request);
            var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                var detail = text.Length > 300 ? text.Substring(0, 300) + "…" : text;
                throw new InvalidOperationException($"AI request failed ({(int)response.StatusCode}): {detail}");
            }
            using var document = JsonDocument.Parse(text);
            var choices = document.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
                return "";
            var content = choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            // Reasoning models may wrap their thinking in <think>...</think>; keep the answer only.
            var thinkEnd = content.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (content.TrimStart().StartsWith("<think>", StringComparison.OrdinalIgnoreCase) && thinkEnd >= 0)
                content = content.Substring(thinkEnd + "</think>".Length);
            return content.Trim();
        }

        public void ShowText(string title, string text) => _pointInfo.Invoke(() => StepPromptForm.ShowText(title, text));

        private const uint LCMAP_SIMPLIFIED_CHINESE = 0x02000000;
        private const uint LCMAP_TRADITIONAL_CHINESE = 0x04000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int LCMapStringEx(string lpLocaleName, uint dwMapFlags, string lpSrcStr, int cchSrc,
            [Out] char[] lpDestStr, int cchDest, IntPtr lpVersionInformation, IntPtr lpReserved, IntPtr sortHandle);

        public string ConvertChinese(string text, bool toTraditional)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? "";
            var flags = toTraditional ? LCMAP_TRADITIONAL_CHINESE : LCMAP_SIMPLIFIED_CHINESE;
            var length = LCMapStringEx("zh-CN", flags, text, text.Length, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (length <= 0)
                throw new InvalidOperationException($"Chinese conversion failed ({Marshal.GetLastWin32Error()}).");
            var buffer = new char[length];
            length = LCMapStringEx("zh-CN", flags, text, text.Length, buffer, buffer.Length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (length <= 0)
                throw new InvalidOperationException($"Chinese conversion failed ({Marshal.GetLastWin32Error()}).");
            return new string(buffer, 0, length);
        }

        public void RunCommand(string pluginClass, string settings)
        {
            var plugin = GestureSign.Common.Plugins.PluginManager.Instance.FindPluginByClassAndFilename(pluginClass, null)
                         ?? throw new InvalidOperationException($"Plugin not found: {pluginClass}");
            if (plugin.Plugin is StepsPlugin)
                throw new InvalidOperationException("A step program cannot run another step program.");
            plugin.Plugin.Deserialize(settings ?? "");
            plugin.Plugin.Gestured(_pointInfo);
        }

        public void Log(string message) => Logging.LogMessage(message);

        // ----- JavaScript (Jint) ------------------------------------------

        public object RunJavaScript(string code, StepVariables variables, bool expression)
        {
            var engine = new Jint.Engine(options => options
                .TimeoutInterval(ScriptTimeout)
                .LimitRecursion(256)
                .MaxStatements(5_000_000));
            engine.SetValue("setVar", new Action<string, object>((name, value) => variables[name] = FromScript(value)));
            engine.SetValue("getVar", new Func<string, object>(name => ToScript(variables[name])));
            engine.SetValue("log", new Action<object>(value => Logging.LogMessage("Step JS: " + StepVariables.ToText(FromScript(value)))));
            foreach (var pair in variables.ToList())
                if (StepRunner.IsVariableName(pair.Key))
                    engine.SetValue(pair.Key, ToScript(pair.Value));
            try
            {
                var result = expression
                    ? engine.Evaluate(code)
                    : engine.Evaluate("(function(){\n" + code + "\n})()");
                if (!expression)
                {
                    // Plain assignments to a variable (text = ...) write it back.
                    foreach (var name in variables.Names.ToList())
                        if (StepRunner.IsVariableName(name))
                            variables[name] = FromScript(engine.GetValue(name).ToObject());
                }
                return FromScript(result.ToObject());
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new InvalidOperationException("JavaScript: " + ex.Message, ex);
            }
        }

        // ----- Lua (MoonSharp) -------------------------------------------

        public object RunLua(string code, StepVariables variables)
        {
            var script = new MoonSharp.Interpreter.Script(MoonSharp.Interpreter.CoreModules.Preset_SoftSandbox);
            script.Globals["setVar"] = MoonSharp.Interpreter.DynValue.NewCallback((context, args) =>
            {
                variables[args[0].CastToString()] = FromLua(args.Count > 1 ? args[1] : MoonSharp.Interpreter.DynValue.Nil);
                return MoonSharp.Interpreter.DynValue.Nil;
            });
            script.Globals["getVar"] = MoonSharp.Interpreter.DynValue.NewCallback((context, args) => ToLua(script, variables[args[0].CastToString()]));
            script.Globals["log"] = MoonSharp.Interpreter.DynValue.NewCallback((context, args) =>
            {
                Logging.LogMessage("Step Lua: " + StepVariables.ToText(FromLua(args.Count > 0 ? args[0] : MoonSharp.Interpreter.DynValue.Nil)));
                return MoonSharp.Interpreter.DynValue.Nil;
            });
            foreach (var pair in variables.ToList())
                if (StepRunner.IsVariableName(pair.Key))
                    script.Globals[pair.Key] = ToLua(script, pair.Value);
            try
            {
                var function = script.LoadString(code);
                // Run as a coroutine that yields every 1000 instructions, so a
                // runaway loop can be stopped instead of hanging all actions.
                var coroutine = script.CreateCoroutine(function).Coroutine;
                coroutine.AutoYieldCounter = 1000;
                var stopwatch = Stopwatch.StartNew();
                var result = coroutine.Resume();
                while (result.Type == MoonSharp.Interpreter.DataType.YieldRequest)
                {
                    if (stopwatch.Elapsed > ScriptTimeout)
                        throw new TimeoutException($"The Lua script ran longer than {ScriptTimeout.TotalSeconds:0} seconds.");
                    result = coroutine.Resume();
                }
                foreach (var name in variables.Names.ToList())
                    if (StepRunner.IsVariableName(name))
                        variables[name] = FromLua(script.Globals.Get(name));
                if (result.Type == MoonSharp.Interpreter.DataType.Tuple)
                    result = result.Tuple.Length > 0 ? result.Tuple[0] : MoonSharp.Interpreter.DynValue.Nil;
                return FromLua(result);
            }
            catch (MoonSharp.Interpreter.InterpreterException ex)
            {
                throw new InvalidOperationException("Lua: " + (ex.DecoratedMessage ?? ex.Message), ex);
            }
        }

        // ----- value conversion -------------------------------------------

        /// <summary>Step values are text, double, bool, lists or null; objects become JSON text.</summary>
        private static object FromScript(object value) => value switch
        {
            null => null,
            string or bool or double => value,
            int i => (double)i,
            long l => (double)l,
            float f => (double)f,
            IDictionary<string, object> map => JsonSerializer.Serialize(map),
            string[] strings => strings.Cast<object>().ToList(),
            IEnumerable list when value is not string => list.Cast<object>().Select(FromScript).ToList(),
            _ => value.ToString()
        };

        private static object ToScript(object value) => value switch
        {
            IList<object> list => list.Select(ToScript).ToArray(),
            _ => value
        };

        private static MoonSharp.Interpreter.DynValue ToLua(MoonSharp.Interpreter.Script script, object value)
        {
            switch (value)
            {
                case null: return MoonSharp.Interpreter.DynValue.Nil;
                case string s: return MoonSharp.Interpreter.DynValue.NewString(s);
                case bool b: return MoonSharp.Interpreter.DynValue.NewBoolean(b);
                case double d: return MoonSharp.Interpreter.DynValue.NewNumber(d);
                case IList<object> list:
                    var table = new MoonSharp.Interpreter.Table(script);
                    foreach (var item in list) table.Append(ToLua(script, item));
                    return MoonSharp.Interpreter.DynValue.NewTable(table);
                default: return MoonSharp.Interpreter.DynValue.NewString(StepVariables.ToText(value));
            }
        }

        private static object FromLua(MoonSharp.Interpreter.DynValue value)
        {
            switch (value.Type)
            {
                case MoonSharp.Interpreter.DataType.Nil:
                case MoonSharp.Interpreter.DataType.Void:
                    return null;
                case MoonSharp.Interpreter.DataType.Boolean: return value.Boolean;
                case MoonSharp.Interpreter.DataType.Number: return value.Number;
                case MoonSharp.Interpreter.DataType.String: return value.String;
                case MoonSharp.Interpreter.DataType.Table:
                    var table = value.Table;
                    if (table.Length > 0)
                        return Enumerable.Range(1, table.Length).Select(i => FromLua(table.Get(i))).ToList();
                    var map = new Dictionary<string, object>();
                    foreach (var pair in table.Pairs)
                        map[pair.Key.CastToString() ?? ""] = FromLua(pair.Value);
                    return JsonSerializer.Serialize(map);
                default:
                    return value.ToPrintString();
            }
        }
    }

    /// <summary>Small topmost input and choice dialogs for step programs.</summary>
    internal sealed class StepPromptForm : Form
    {
        public static Form NewTopmostOwner()
        {
            var owner = new Form
            {
                TopMost = true,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = Cursor.Position,
                Size = new Size(1, 1),
                Opacity = 0
            };
            owner.Show();
            owner.Activate();
            return owner;
        }

        private StepPromptForm(string prompt, Control input, int height)
        {
            Text = "GestureSign";
            TopMost = true;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(420, height);
            Padding = new Padding(12);

            var label = new Label { Text = prompt ?? "", Dock = DockStyle.Top, AutoSize = false, Height = string.IsNullOrEmpty(prompt) ? 0 : 28 };
            var ok = new Button { Text = Ui("确定", "OK"), DialogResult = DialogResult.OK, Width = 88, Height = 30 };
            var cancel = new Button { Text = Ui("取消", "Cancel"), DialogResult = DialogResult.Cancel, Width = 88, Height = 30 };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(0, 6, 0, 0) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            input.Dock = DockStyle.Fill;
            Controls.Add(input);
            Controls.Add(buttons);
            Controls.Add(label);
            AcceptButton = input is TextBox { Multiline: true } ? null : ok;
            CancelButton = cancel;

            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(
                Math.Max(area.Left, Math.Min(Cursor.Position.X - Width / 2, area.Right - Width)),
                Math.Max(area.Top, Math.Min(Cursor.Position.Y - Height / 2, area.Bottom - Height)));
        }

        public static string AskText(string prompt, string defaultValue, bool multiline)
        {
            var box = new TextBox { Text = defaultValue ?? "", Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, AcceptsReturn = multiline };
            using var form = new StepPromptForm(prompt, box, multiline ? 240 : 120);
            form.Shown += (_, __) => { form.Activate(); box.Focus(); box.SelectAll(); };
            return form.ShowDialog() == DialogResult.OK ? box.Text : null;
        }

        /// <summary>Chinese or English button text, following the UI language.</summary>
        private static string Ui(string zh, string en)
            => System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? zh : en;

        /// <summary>A resizable window with read-only text and a Copy button.</summary>
        public static void ShowText(string title, string text)
        {
            var box = new TextBox
            {
                Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Font = new Font("Microsoft YaHei UI", 10.5f)
            };
            var form = new Form
            {
                Text = string.IsNullOrWhiteSpace(title) ? "GestureSign" : title,
                TopMost = true,
                ShowInTaskbar = true,
                StartPosition = FormStartPosition.Manual,
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96f, 96f),
                ClientSize = new Size(560, 380),
                Padding = new Padding(10),
                KeyPreview = true
            };
            var copy = new Button { Text = Ui("复制", "Copy"), Width = 96, Height = 30 };
            var close = new Button { Text = Ui("关闭", "Close"), Width = 96, Height = 30, DialogResult = DialogResult.Cancel };
            copy.Click += (_, __) =>
            {
                try { Clipboard.SetText(box.Text); copy.Text = Ui("已复制", "Copied"); }
                catch (ExternalException) { }
            };
            close.Click += (_, __) => form.Close();
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 42, Padding = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(close);
            buttons.Controls.Add(copy);
            box.Dock = DockStyle.Fill;
            form.Controls.Add(box);
            form.Controls.Add(buttons);
            form.CancelButton = close;
            form.FormClosed += (_, __) => form.Dispose();
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            form.Location = new Point(
                Math.Max(area.Left, Math.Min(Cursor.Position.X - form.Width / 2, area.Right - form.Width)),
                Math.Max(area.Top, Math.Min(Cursor.Position.Y - form.Height / 2, area.Bottom - form.Height)));
            form.Show();
            form.Activate();
            box.SelectionStart = 0;
            box.SelectionLength = 0;
        }

        public static IReadOnlyList<int> AskChoice(string prompt, IReadOnlyList<string> items, bool multiple)
        {
            var height = Math.Min(480, 120 + items.Count * 22);
            if (multiple)
            {
                var checks = new CheckedListBox { IntegralHeight = false, CheckOnClick = true };
                foreach (var item in items)
                    checks.Items.Add(item);
                using var multiForm = new StepPromptForm(prompt, checks, height);
                multiForm.Shown += (_, __) => { multiForm.Activate(); checks.Focus(); };
                return multiForm.ShowDialog() == DialogResult.OK ? checks.CheckedIndices.Cast<int>().ToList() : null;
            }
            var list = new ListBox { IntegralHeight = false };
            foreach (var item in items)
                list.Items.Add(item);
            list.SelectedIndex = 0;
            using var form = new StepPromptForm(prompt, list, height);
            list.DoubleClick += (_, __) => { if (list.SelectedItem != null) form.DialogResult = DialogResult.OK; };
            form.Shown += (_, __) => { form.Activate(); list.Focus(); };
            return form.ShowDialog() == DialogResult.OK && list.SelectedIndex >= 0 ? new[] { list.SelectedIndex } : null;
        }

        /// <summary>One window with a labelled control per field; null when cancelled.</summary>
        public static IReadOnlyDictionary<string, object> AskForm(string title, IReadOnlyList<StepFormField> fields)
        {
            var table = new TableLayoutPanel { ColumnCount = 2, AutoScroll = true, Padding = new Padding(0, 4, 8, 0) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var readers = new List<(string Name, Func<object> Read)>();
            var height = 90;
            Control first = null;
            foreach (var field in fields)
            {
                var label = new Label { Text = field.Label, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
                Control control;
                switch (field.Kind)
                {
                    case "multiline":
                        var area = new TextBox { Text = field.Default, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Height = 72, Dock = DockStyle.Fill };
                        readers.Add((field.Name, () => area.Text));
                        control = area;
                        label.TextAlign = ContentAlignment.TopLeft;
                        break;
                    case "number":
                        var number = new NumericUpDown { DecimalPlaces = 2, Minimum = decimal.MinValue / 2, Maximum = decimal.MaxValue / 2, Dock = DockStyle.Fill };
                        if (decimal.TryParse(field.Default, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var start))
                            number.Value = start;
                        readers.Add((field.Name, () => (object)(double)number.Value));
                        control = number;
                        break;
                    case "choice":
                        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
                        foreach (var option in field.Options)
                            combo.Items.Add(option);
                        combo.SelectedIndex = combo.Items.Count == 0 ? -1 : Math.Max(0, field.Options.ToList().IndexOf(field.Default));
                        readers.Add((field.Name, () => combo.SelectedItem as string ?? ""));
                        control = combo;
                        break;
                    case "check":
                        var check = new CheckBox { Text = "", Checked = StepVariables.IsTruthy(field.Default), Dock = DockStyle.Fill };
                        readers.Add((field.Name, () => (object)check.Checked));
                        control = check;
                        break;
                    case "file":
                    case "folder":
                        var pathBox = new TextBox { Text = field.Default, Dock = DockStyle.Fill };
                        var browse = new Button { Text = "…", Width = 32, Dock = DockStyle.Right };
                        var kind = field.Kind;
                        browse.Click += (_, __) =>
                        {
                            var picked = PickFiles(kind == "folder" ? "folder" : "open", field.Label, "", pathBox.Text);
                            if (picked != null && picked.Count > 0) pathBox.Text = picked[0];
                        };
                        var holder = new Panel { Height = pathBox.PreferredHeight, Dock = DockStyle.Fill };
                        holder.Controls.Add(pathBox);
                        holder.Controls.Add(browse);
                        readers.Add((field.Name, () => pathBox.Text));
                        control = holder;
                        first ??= pathBox;
                        break;
                    default:
                        var text = new TextBox { Text = field.Default, Dock = DockStyle.Fill };
                        readers.Add((field.Name, () => text.Text));
                        control = text;
                        break;
                }
                first ??= control;
                var rowHeight = field.Kind == "multiline" ? 80 : 32;
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
                table.Controls.Add(label);
                table.Controls.Add(control);
                height += rowHeight;
            }
            using var form = new StepPromptForm(null, table, Math.Min(560, height));
            if (!string.IsNullOrWhiteSpace(title))
                form.Text = title;
            form.Shown += (_, __) => { form.Activate(); first?.Focus(); };
            if (form.ShowDialog() != DialogResult.OK)
                return null;
            return readers.ToDictionary(r => r.Name, r => r.Read(), StringComparer.Ordinal);
        }

        /// <summary>Windows open, save or folder dialog; null when cancelled.</summary>
        public static IReadOnlyList<string> PickFiles(string mode, string title, string filter, string initialPath)
        {
            using var owner = NewTopmostOwner();
            initialPath = Environment.ExpandEnvironmentVariables(initialPath ?? "").Trim().Trim('"');
            var initialFolder = System.IO.Directory.Exists(initialPath) ? initialPath : System.IO.Path.GetDirectoryName(initialPath);
            var initialName = System.IO.Directory.Exists(initialPath) ? "" : System.IO.Path.GetFileName(initialPath);
            switch ((mode ?? "").Trim().ToLowerInvariant())
            {
                case "folder":
                    using (var dialog = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = title ?? "", ShowNewFolderButton = true })
                    {
                        if (!string.IsNullOrEmpty(initialPath) && System.IO.Directory.Exists(initialPath))
                            dialog.SelectedPath = initialPath;
                        return dialog.ShowDialog(owner) == DialogResult.OK ? new[] { dialog.SelectedPath } : null;
                    }
                case "save":
                    using (var dialog = new SaveFileDialog { Title = title ?? "", FileName = initialName ?? "", OverwritePrompt = true })
                    {
                        if (!string.IsNullOrWhiteSpace(filter)) TrySetFilter(dialog, filter);
                        if (!string.IsNullOrEmpty(initialFolder)) dialog.InitialDirectory = initialFolder;
                        return dialog.ShowDialog(owner) == DialogResult.OK ? new[] { dialog.FileName } : null;
                    }
                default:
                    using (var dialog = new OpenFileDialog { Title = title ?? "", Multiselect = string.Equals(mode, "openMultiple", StringComparison.OrdinalIgnoreCase), FileName = initialName ?? "" })
                    {
                        if (!string.IsNullOrWhiteSpace(filter)) TrySetFilter(dialog, filter);
                        if (!string.IsNullOrEmpty(initialFolder)) dialog.InitialDirectory = initialFolder;
                        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileNames : null;
                    }
            }
        }

        /// <summary>A malformed filter ("a|b|c") would throw; fall back to all files.</summary>
        private static void TrySetFilter(FileDialog dialog, string filter)
        {
            try { dialog.Filter = filter; }
            catch (ArgumentException) { dialog.Filter = ""; }
        }
    }

    /// <summary>
    /// A short message floating above the taskbar, centered on the screen the pointer is on.
    /// The card is drawn with per-pixel transparency, so its rounded corners, icon and soft
    /// shadow stay smooth at any DPI; a thin line along the bottom counts down the time left.
    /// </summary>
    internal sealed class StepToast : Form
    {
        private const int ShadowRoom = 14;
        private const int CornerRadius = 8;
        private const int PadX = 18;
        private const int PadY = 12;
        private const int IconSize = 20;
        private const int IconGap = 10;
        private const int MinWidth = 120;
        private const int MaxWidth = 480;
        private const int AboveTaskbar = 24;
        private const int RisePixels = 8;
        private const int MaxLines = 5;
        private const int ShowMs = 2500;
        private const int FadeInMs = 140;
        private const int FadeOutMs = 180;

        private static StepToast _current;
        private static string _fontFamily;

        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 16 };
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly string _text;
        private readonly string _kind;

        private Bitmap _layer;
        private Color _accent;
        private float _scale = 1f;
        private int _width;
        private int _height;
        private int _cardWidth;
        private int _cardHeight;
        private int _left;
        private int _top;

        private StepToast(string text, string kind)
        {
            _text = text;
            _kind = kind;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            _timer.Tick += OnTick;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                // Layered for per-pixel alpha, transparent so clicks reach whatever is behind,
                // noactivate so the gesture never moves the focus away from the user's window.
                p.ExStyle |= 0x00080000 /* LAYERED */ | 0x00000020 /* TRANSPARENT */
                           | 0x08000000 /* NOACTIVATE */ | 0x00000080 /* TOOLWINDOW */;
                return p;
            }
        }

        public static void ShowMessage(string text, string kind)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var previous = _current;
            _current = null;
            if (previous is { IsDisposed: false })
                previous.CloseNow();

            var toast = new StepToast(text, kind);
            _current = toast;
            toast.Present();
        }

        private void Present()
        {
            // Move to the pointer's screen first, so the device DPI belongs to that monitor.
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Left, area.Top);
            _ = Handle;
            _scale = Math.Max(1f, DeviceDpi / 96f);

            using (var measure = Graphics.FromHwnd(IntPtr.Zero))
                BuildLayer(measure);
            if (_layer == null)
            {
                Dispose();
                return;
            }

            _left = area.Left + (area.Width - _width) / 2;
            _top = area.Bottom - (int)Math.Round(AboveTaskbar * _scale) - _cardHeight - ShadowRoom;
            Size = new Size(_width, _height);

            Render(0f, (int)Math.Round(RisePixels * _scale), 0f);
            Show();
            _clock.Restart();
            _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            var elapsed = (float)_clock.Elapsed.TotalMilliseconds;
            float alpha;
            var rise = 0;
            if (elapsed < FadeInMs)
            {
                var k = elapsed / FadeInMs;
                alpha = k;
                rise = (int)Math.Round((1f - k) * RisePixels * _scale);
            }
            else if (elapsed < FadeInMs + ShowMs)
            {
                alpha = 1f;
            }
            else
            {
                var k = (elapsed - FadeInMs - ShowMs) / FadeOutMs;
                if (k >= 1f)
                {
                    CloseNow();
                    return;
                }
                alpha = 1f - k;
            }

            var progress = Math.Clamp((elapsed - FadeInMs) / ShowMs, 0f, 1f);
            Render(alpha, rise, progress);
        }

        private void CloseNow()
        {
            _timer.Stop();
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            _layer?.Dispose();
            _layer = null;
            if (ReferenceEquals(_current, this))
                _current = null;
            base.OnFormClosed(e);
        }

        private void BuildLayer(Graphics measure)
        {
            var light = IsLightTheme();
            var background = light ? Color.FromArgb(255, 255, 255) : Color.FromArgb(44, 44, 44);
            var border = light ? Color.FromArgb(20, 0, 0, 0) : Color.FromArgb(28, 255, 255, 255);
            var foreground = light ? Color.FromArgb(27, 27, 27) : Color.FromArgb(243, 243, 243);
            _accent = _kind switch
            {
                "warn" => light ? Color.FromArgb(186, 117, 23) : Color.FromArgb(239, 159, 39),
                "error" => light ? Color.FromArgb(163, 45, 45) : Color.FromArgb(240, 149, 149),
                _ => light ? Color.FromArgb(24, 95, 165) : Color.FromArgb(133, 183, 235)
            };

            using var font = new Font(FontFamilyName(), 14f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
            using var format = new StringFormat(StringFormat.GenericDefault) { LineAlignment = StringAlignment.Center };

            var textWidth = MaxWidth * _scale - (PadX * 2 + IconSize + IconGap) * _scale;
            var lineHeight = measure.MeasureString("国", font).Height;
            var text = FitLines(measure, _text.Trim(), font, textWidth, format, lineHeight * MaxLines);
            var measured = measure.MeasureString(text, font, (int)Math.Ceiling(textWidth), format);

            var contentWidth = Math.Min(textWidth, Math.Max(measured.Width, 1f) + 1f);
            var cardWidth = Math.Min(MaxWidth * _scale, Math.Max(MinWidth * _scale, contentWidth + (IconSize + IconGap + PadX * 2) * _scale));
            var cardHeight = Math.Max(40f * _scale, measured.Height + PadY * 2f);
            _cardWidth = (int)Math.Ceiling(cardWidth);
            _cardHeight = (int)Math.Ceiling(cardHeight);
            _width = _cardWidth + ShadowRoom * 2;
            _height = _cardHeight + ShadowRoom * 2;

            var layer = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(layer))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                var card = new RectangleF(ShadowRoom, ShadowRoom, _cardWidth, _cardHeight);

                // Barely-there lift under the card: two soft passes, no glow around the sides.
                var strength = light ? 5 : 7;
                for (var i = 2; i >= 1; i--)
                {
                    var drop = i * 1.3f * _scale;
                    var rect = RectangleF.Inflate(card, drop * 0.4f, 0f);
                    rect.Offset(0f, drop);
                    using var shadow = Rounded(rect, CornerRadius * _scale);
                    using var brush = new SolidBrush(Color.FromArgb(strength, 0, 0, 0));
                    g.FillPath(brush, shadow);
                }

                using (var fill = Rounded(card, CornerRadius * _scale))
                using (var brush = new SolidBrush(background))
                    g.FillPath(brush, fill);

                using (var edge = Rounded(RectangleF.Inflate(card, -0.5f, -0.5f), CornerRadius * _scale - 0.5f))
                using (var pen = new Pen(border, 1f))
                    g.DrawPath(pen, edge);

                DrawIcon(g, new RectangleF(card.Left + PadX * _scale, card.Top + (_cardHeight - IconSize * _scale) / 2f, IconSize * _scale, IconSize * _scale));

                using (var brush = new SolidBrush(foreground))
                    g.DrawString(text, font, brush, new RectangleF(card.Left + (PadX + IconSize + IconGap) * _scale, card.Top, textWidth, _cardHeight), format);
            }

            _layer?.Dispose();
            _layer = layer;
        }

        private void DrawIcon(Graphics g, RectangleF box)
        {
            var state = g.Save();
            g.TranslateTransform(box.Left, box.Top);
            g.ScaleTransform(box.Width / 20f, box.Height / 20f);
            using var pen = new Pen(_accent, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var brush = new SolidBrush(_accent);
            g.DrawEllipse(pen, 1.4f, 1.4f, 17.2f, 17.2f);
            switch (_kind)
            {
                case "warn":
                    g.DrawLine(pen, 10f, 5.2f, 10f, 11.4f);
                    g.FillEllipse(brush, 9f, 13.4f, 2f, 2f);
                    break;
                case "error":
                    g.DrawLine(pen, 6.8f, 6.8f, 13.2f, 13.2f);
                    g.DrawLine(pen, 13.2f, 6.8f, 6.8f, 13.2f);
                    break;
                default:
                    g.DrawLine(pen, 10f, 9.2f, 10f, 14.6f);
                    g.FillEllipse(brush, 9f, 5.4f, 2f, 2f);
                    break;
            }
            g.Restore(state);
        }

        private void Render(float alpha, int rise, float progress)
        {
            if (_layer == null)
                return;

            using var frame = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(frame))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawBitmap(g, _layer, alpha);
                if (progress > 0f && progress < 1f)
                    DrawCountdown(g, alpha, progress);
            }
            Push(frame, _left, _top + rise);
        }

        private void DrawCountdown(Graphics g, float alpha, float progress)
        {
            var card = new RectangleF(ShadowRoom, ShadowRoom, _cardWidth, _cardHeight);
            var height = Math.Max(2f, 1.75f * _scale);
            var width = card.Width * (1f - progress);
            if (width <= 1f)
                return;

            using var path = Rounded(card, CornerRadius * _scale);
            var previous = g.Clip;
            g.SetClip(path);
            using var brush = new SolidBrush(Color.FromArgb((int)Math.Round(125 * alpha), _accent));
            g.FillRectangle(brush, card.Left, card.Bottom - height, width, height);
            g.Clip = previous;
        }

        private void Push(Bitmap frame, int left, int top)
        {
            var screen = GetDC(IntPtr.Zero);
            var memory = CreateCompatibleDC(screen);
            var bitmap = frame.GetHbitmap(Color.FromArgb(0));
            var previous = SelectObject(memory, bitmap);
            try
            {
                var size = new SIZE(frame.Width, frame.Height);
                var source = new POINT(0, 0);
                var destination = new POINT(left, top);
                var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screen, ref destination, ref size, memory, ref source, 0, ref blend, 2);
            }
            finally
            {
                SelectObject(memory, previous);
                DeleteObject(bitmap);
                DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        private static void DrawBitmap(Graphics g, Image image, float alpha)
        {
            if (alpha >= 0.999f)
            {
                g.DrawImageUnscaled(image, 0, 0);
                return;
            }
            var matrix = new ColorMatrix { Matrix33 = alpha };
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix);
            g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }

        private static string FitLines(Graphics g, string text, Font font, float width, StringFormat format, float maxHeight)
        {
            var limit = (int)Math.Ceiling(width);
            if (g.MeasureString(text, font, limit, format).Height <= maxHeight)
                return text;

            var low = 0;
            var high = text.Length;
            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                var candidate = text.Substring(0, middle).TrimEnd() + "…";
                if (g.MeasureString(candidate, font, limit, format).Height <= maxHeight)
                    low = middle;
                else
                    high = middle - 1;
            }
            return text.Substring(0, low).TrimEnd() + "…";
        }

        private static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            var size = radius * 2f;
            path.AddArc(rect.Left, rect.Top, size, size, 180f, 90f);
            path.AddArc(rect.Right - size, rect.Top, size, size, 270f, 90f);
            path.AddArc(rect.Right - size, rect.Bottom - size, size, size, 0f, 90f);
            path.AddArc(rect.Left, rect.Bottom - size, size, size, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        private static bool IsLightTheme()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>YaHei keeps Chinese text crisp; other systems fall back to their own UI font.</summary>
        private static string FontFamilyName()
        {
            if (_fontFamily != null)
                return _fontFamily;

            _fontFamily = SystemFonts.MessageBoxFont?.FontFamily.Name ?? "Segoe UI";
            try
            {
                using var families = new InstalledFontCollection();
                foreach (var family in families.Families)
                {
                    if (!string.Equals(family.Name, "Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase))
                        continue;
                    _fontFamily = family.Name;
                    break;
                }
            }
            catch
            {
            }
            return _fontFamily;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int Width;
            public int Height;

            public SIZE(int width, int height)
            {
                Width = width;
                Height = height;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr destinationDc, ref POINT destination, ref SIZE size, IntPtr sourceDc, ref POINT source, int colorKey, ref BLENDFUNCTION blend, int flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);
    }
}
