using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
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

        public string AskChoice(string prompt, IReadOnlyList<string> items)
        {
            if (items == null || items.Count == 0)
                return null;
            string answer = null;
            _pointInfo.Invoke(() => answer = StepPromptForm.AskChoice(prompt, items));
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

        public static string AskChoice(string prompt, IReadOnlyList<string> items)
        {
            var list = new ListBox { IntegralHeight = false };
            foreach (var item in items)
                list.Items.Add(item);
            list.SelectedIndex = 0;
            using var form = new StepPromptForm(prompt, list, Math.Min(480, 120 + items.Count * 22));
            list.DoubleClick += (_, __) => { if (list.SelectedItem != null) form.DialogResult = DialogResult.OK; };
            form.Shown += (_, __) => { form.Activate(); list.Focus(); };
            return form.ShowDialog() == DialogResult.OK ? list.SelectedItem as string : null;
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
