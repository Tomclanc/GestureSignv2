using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;
using WindowsInput;
using WindowsInput.Native;

namespace GestureSign.PieMenu;

internal static class MenuActions
{
    internal static async Task Run(PieAction action, nint foreground)
    {
        switch (action.Type)
        {
            case "open-file":
            case "open-uri":
                Desktop.Open(action.Value, command: false);
                break;
            case "execute-command":
                Desktop.Open(action.Value, command: true);
                break;
            case "set-clipboard":
                var data = new DataPackage();
                data.SetText(action.Value);
                Clipboard.SetContent(data);
                Clipboard.Flush();
                break;
            case "delay":
                await Task.Delay(ParseDelay(action.Value));
                break;
            case "simulate-hotkey":
                VerifyFocus(foreground);
                var names = action.Value.Split('+', StringSplitOptions.TrimEntries);
                if (PhysicalKeys.TryResolve(names, out var codes))
                    await PhysicalKeys.Press(codes, () => VerifyFocus(foreground));
                else
                {
                    var keyboard = new InputSimulator().Keyboard;
                    if (MenuShortcut.TryParse(action.Value, out var shortcut, out _) && shortcut.Key != 0)
                    {
                        (uint Flag, VirtualKeyCode Key)[] modifiers =
                        [
                            (2, VirtualKeyCode.LCONTROL), (4, VirtualKeyCode.LSHIFT),
                            (1, VirtualKeyCode.LMENU), (8, VirtualKeyCode.LWIN)
                        ];
                        keyboard.ModifiedKeyStroke(modifiers.Where(entry => (entry.Flag & shortcut.Modifiers) != 0)
                            .Select(entry => entry.Key), (VirtualKeyCode)shortcut.Key);
                    }
                    else
                    {
                        var keys = names.Select(ParseKey).ToArray();
                        keyboard.ModifiedKeyStroke(keys.Take(keys.Length - 1), keys[^1]);
                    }
                }
                break;
            case "execute-macro":
                await Macro(action.Value, foreground);
                break;
            case "focus-window":
                FocusWindow(action.Value);
                break;
            case "send-tcp-message":
                await Tcp(action.Value);
                break;
            case "send-websocket-message":
                await WebSocket(action.Value);
                break;
            default:
                throw new NotSupportedException("Unsupported action: " + action.Type);
        }
    }

    private static void VerifyFocus(nint expected)
    {
        if (expected == 0 || Desktop.GetForegroundWindow() != expected)
            throw new InvalidOperationException("The original window is no longer focused. The shortcut was skipped.");
    }

    private static int ParseDelay(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds)
            ? Math.Clamp(milliseconds, 0, 15000) : 0;

    private static async Task Macro(string raw, nint foreground)
    {
        using var document = JsonDocument.Parse(raw);
        var events = document.RootElement.EnumerateArray().Take(129)
            .Select(entry => (Type: entry.GetProperty("type").GetString(),
                Key: ParseKey(entry.GetProperty("key").GetString() ?? ""),
                Delay: entry.TryGetProperty("delay", out var delay) ? Math.Clamp(delay.GetInt32(), 0, 3000) : 0))
            .ToArray();
        if (events.Length > 128 || events.Sum(entry => entry.Delay) > 15000 ||
            events.Any(e => e.Type is not ("keyDown" or "keyUp")))
            throw new InvalidDataException("Invalid or excessively long keyboard macro.");
        var keyboard = new InputSimulator().Keyboard;
        var held = new HashSet<VirtualKeyCode>();
        try
        {
            foreach (var entry in events)
            {
                await Task.Delay(entry.Delay);
                VerifyFocus(foreground);
                if (entry.Type == "keyDown") { keyboard.KeyDown(entry.Key); held.Add(entry.Key); }
                else { keyboard.KeyUp(entry.Key); held.Remove(entry.Key); }
            }
        }
        finally
        {
            foreach (var key in held) keyboard.KeyUp(key);
        }
    }

    private static void FocusWindow(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var title = Property(doc.RootElement, "windowName");
        var app = Property(doc.RootElement, "appName");
        if (title.Length == 0 && app.Length == 0) throw new InvalidDataException("A window or app name is required.");
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var handle = process.MainWindowHandle;
                if (handle == 0 || !process.MainWindowTitle.Contains(title, StringComparison.OrdinalIgnoreCase) ||
                    !process.ProcessName.Contains(app, StringComparison.OrdinalIgnoreCase)) continue;
                if (Desktop.SetForegroundWindow(handle)) return;
            }
        }
        throw new InvalidOperationException("No matching window could be focused.");
    }

    private static async Task Tcp(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var node = doc.RootElement;
        var value = Property(node, "message");
        if (node.TryGetProperty("appendNewline", out var newline) && newline.ValueKind == JsonValueKind.True) value += "\n";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var client = new TcpClient();
        await client.ConnectAsync(Property(node, "host"), node.GetProperty("port").GetInt32(), deadline.Token);
        await client.GetStream().WriteAsync(Encoding.UTF8.GetBytes(value), deadline.Token);
    }

    private static async Task WebSocket(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri(Property(doc.RootElement, "url")), deadline.Token);
        await client.SendAsync(Encoding.UTF8.GetBytes(Property(doc.RootElement, "message")),
            WebSocketMessageType.Text, true, deadline.Token);
    }

    private static string Property(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static VirtualKeyCode ParseKey(string key)
    {
        var name = key.Trim().ToUpperInvariant();
        name = name switch
        {
            "CTRL" or "CONTROL" or "CONTROLLEFT" => "LCONTROL", "CONTROLRIGHT" => "RCONTROL",
            "SHIFT" or "SHIFTLEFT" => "LSHIFT", "SHIFTRIGHT" => "RSHIFT",
            "ALT" or "ALTLEFT" => "LMENU", "ALTRIGHT" => "RMENU",
            "META" or "SUPER" or "WIN" or "METALEFT" => "LWIN", "METARIGHT" => "RWIN",
            "ESC" => "ESCAPE", "ENTER" => "RETURN", "BACKSPACE" => "BACK",
            "ARROWUP" => "UP", "ARROWDOWN" => "DOWN", "ARROWLEFT" => "LEFT", "ARROWRIGHT" => "RIGHT",
            "PAGEUP" => "PRIOR", "PAGEDOWN" => "NEXT",
            _ => name
        };
        if (name.Length == 4 && name.StartsWith("KEY", StringComparison.Ordinal)) name = name[3..];
        if (name.Length == 6 && name.StartsWith("DIGIT", StringComparison.Ordinal)) name = name[5..];
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0])) name = "VK_" + name;
        if (Enum.TryParse<VirtualKeyCode>(name, ignoreCase: true, out var keyCode) && Enum.IsDefined(keyCode))
            return keyCode;
        throw new InvalidDataException("Unknown key: " + key);
    }
}
