using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
using System.Linq;
using System.Net.Http;
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

        public string RunProgram(string path, string arguments, bool wait)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("No program, file or URL was given.");
            if (!wait)
            {
                using var started = Process.Start(new ProcessStartInfo(path, arguments ?? "") { UseShellExecute = true });
                return null;
            }
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(path, arguments ?? "")
                {
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

        public void Notify(string text) => _pointInfo.Invoke(() => StepToast.ShowMessage(text));

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

    /// <summary>A small click-through message near the bottom-right corner that fades after a moment.</summary>
    internal sealed class StepToast : Form
    {
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 2500 };

        private StepToast(string text)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(40, 40, 40);
            ForeColor = Color.White;
            Opacity = 0.92;
            Padding = new Padding(14, 10, 14, 10);
            var label = new Label
            {
                Text = text.Length > 500 ? text.Substring(0, 500) + "…" : text,
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                Font = new Font("Microsoft YaHei UI", 10f),
                ForeColor = Color.White
            };
            Controls.Add(label);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _timer.Tick += (_, __) => Close();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= 0x08000000 /* NOACTIVATE */ | 0x00000080 /* TOOLWINDOW */ | 0x00000008 /* TOPMOST */;
                return p;
            }
        }

        public static void ShowMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            var toast = new StepToast(text);
            toast.FormClosed += (_, __) => { toast._timer.Dispose(); toast.Dispose(); };
            toast.Show();
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            toast.Location = new Point(area.Right - toast.Width - 16, area.Bottom - toast.Height - 16);
            toast._timer.Start();
        }
    }
}
