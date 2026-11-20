using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace GestureSign.Foundation.Diagnostics;

public enum PieStage { Waiting, Received, Blocked, NoMatchingMenu, StartingRenderer, RendererReady, Presented, ClosedBeforePresentation, RendererFailed }
public enum GestureOutcome { NoMatch, Matched, Suppressed }

public sealed record DiagnosticProcess(int Id, DateTime StartedUtc)
{
    public static DiagnosticProcess Current { get; } = IdentifyCurrent();

    public bool? IsRunning()
    {
        try
        {
            using var process = Process.GetProcessById(Id);
            return !process.HasExited && process.StartTime.ToUniversalTime() == StartedUtc;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static DiagnosticProcess IdentifyCurrent()
    {
        using var process = Process.GetCurrentProcess();
        return new(process.Id, process.StartTime.ToUniversalTime());
    }
}

public sealed record PieDiagnostic(DateTimeOffset RecordedUtc, DiagnosticProcess Process,
    int ConfiguredShortcuts = 0, int RegisteredShortcuts = 0, int ShortcutConflicts = 0, int InvalidShortcuts = 0,
    int? RegistrationError = null, bool BindingsPaused = false, bool RendererReady = false,
    int TriggerCount = 0, bool ExternalTrigger = false, PieStage Stage = PieStage.Waiting, DateTimeOffset? LastTriggerUtc = null,
    double? PresentationMilliseconds = null, int? RendererError = null);

public sealed record GestureDiagnostic(DateTimeOffset RecordedUtc, DiagnosticProcess Process,
    string Device, int Contacts, string? InitialCandidate, string? ResolvedCandidate, GestureOutcome Outcome,
    IReadOnlyList<string?> MatchingActions);

public sealed record TrayDiagnostic(DateTimeOffset RecordedUtc, DiagnosticProcess Process,
    bool TrayEnabled, bool NotifyIconVisible, bool QuickActionsEnabled);

public sealed record StartupDiagnostic(DateTimeOffset RecordedUtc, bool AdminTaskEnabled, int? LastChangeError);

public sealed record LocalDiagnosticReport(int SchemaVersion, DateTimeOffset GeneratedUtc,
    bool StartupShortcutPresent, StartupDiagnostic? AdminStartup,
    bool? BackgroundRunning, TrayDiagnostic? Tray, GestureDiagnostic? Gesture,
    bool? PieComponentRunning, PieDiagnostic? QuickActions);

public static class LocalDiagnostics
{
    public const string PieFile = "quick-actions-health.json";
    public const string GestureFile = "recognition-health.json";
    public const string TrayFile = "tray-health.json";
    public const string StartupFile = "startup-health.json";
    private const int FileLimit = 16 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static string DirectoryFor(string productDirectory) => Path.Combine(productDirectory, "Diagnostics");

    public static async Task<string> ReportJsonAsync(string productDirectory, CancellationToken token = default,
        StartupDiagnostic? startupSnapshot = null)
    {
        var diagnostics = DirectoryFor(productDirectory);
        var pie = await ReadAsync<PieDiagnostic>(Path.Combine(productDirectory, "PieMenu", "Integrated", PieFile), token);
        var tray = await ReadAsync<TrayDiagnostic>(Path.Combine(diagnostics, TrayFile), token);
        var gesture = await ReadAsync<GestureDiagnostic>(Path.Combine(diagnostics, GestureFile), token);
        var startup = startupSnapshot ?? await ReadAsync<StartupDiagnostic>(Path.Combine(diagnostics, StartupFile), token);
        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var shortcutExists = startupFolder.Length > 0 && File.Exists(Path.Combine(startupFolder, "GestureSign.lnk"));
        var report = new LocalDiagnosticReport(1, DateTimeOffset.UtcNow, shortcutExists, startup,
            tray?.Process?.IsRunning() ?? gesture?.Process?.IsRunning(), tray, gesture, pie?.Process?.IsRunning(), pie);
        return JsonSerializer.Serialize(report, Json);
    }

    public static string? GestureLabel(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return new string(name.Take(80).Where(character => !char.IsControl(character)).ToArray());
    }

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken token) where T : class
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var size = stream.Length;
            if (size is <= 0 or > FileLimit) return null;
            var data = new byte[(int)size];
            await stream.ReadExactlyAsync(data, token);
            return JsonSerializer.Deserialize<T>(data, Json);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public sealed class Writer<T> where T : class
    {
        private readonly Channel<T> _updates = Channel.CreateBounded<T>(new BoundedChannelOptions(1)
        {
            SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest
        });

        public Writer(string directory, string name) => _ = Task.Run(() => DrainAsync(directory, name));

        public void Publish(T state) => _updates.Writer.TryWrite(state);

        private async Task DrainAsync(string directory, string name)
        {
            var target = Path.Combine(directory, name);
            await foreach (var state in _updates.Reader.ReadAllAsync())
            {
                var temporary = target + "." + Environment.ProcessId + ".tmp";
                try
                {
                    Directory.CreateDirectory(directory);
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, Json));
                    File.Move(temporary, target, overwrite: true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    try { File.Delete(temporary); }
                    catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
                }
            }
        }
    }
}
