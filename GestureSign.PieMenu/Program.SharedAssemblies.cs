#if SHARE_PIE_HOST_ASSEMBLIES
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GestureSign.PieMenu;

internal static class Program
{
    private static readonly string[] ShareableAssemblies =
        ["Microsoft.Windows.SDK.NET", "Microsoft.WinUI"];

    [STAThread]
    private static void Main(string[] args)
    {
        var directory = AppContext.BaseDirectory;
        var checksums = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(directory, "shared-assemblies.json")))
            ?? throw new InvalidDataException("Missing Pie Menu assembly checksums.");
        var hostIndex = Array.IndexOf(args, "--host");
        var hostDirectory = hostIndex >= 0 && hostIndex + 1 < args.Length
            ? Path.GetDirectoryName(args[hostIndex + 1])
            : null;

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (!ShareableAssemblies.Contains(name.Name, StringComparer.OrdinalIgnoreCase))
                return null;

            var filename = name.Name + ".dll";
            if (!checksums.TryGetValue(filename, out var checksum))
                throw new InvalidDataException($"Missing checksum for {filename}.");

            var shared = TryLoadFromHost(context, hostDirectory, filename, checksum);
            if (shared is not null)
                return shared;

            using var archive = ZipFile.OpenRead(Path.Combine(directory, "shared-assemblies.zip"));
            var entry = archive.GetEntry(filename) ?? throw new InvalidDataException($"Missing fallback for {filename}.");
            if (entry.Length is <= 0 or > 32 * 1024 * 1024)
                throw new InvalidDataException($"Unexpected fallback size for {filename}.");
            var bytes = new byte[(int)entry.Length];
            using var source = entry.Open();
            source.ReadExactly(bytes);
            using var unpacked = new MemoryStream(bytes, writable: false);
            if (source.ReadByte() != -1 || !MatchesChecksum(unpacked, checksum))
                throw new InvalidDataException($"The fallback for {filename} failed verification.");
            unpacked.Position = 0;
            return context.LoadFromStream(unpacked);
        };

        StartApp();
    }

    private static Assembly? TryLoadFromHost(AssemblyLoadContext context, string? directory, string filename, string checksum)
    {
        if (string.IsNullOrEmpty(directory) || !Path.IsPathFullyQualified(directory))
            return null;

        var path = Path.Combine(directory, filename);
        try
        {
            using var file = File.OpenRead(path);
            return MatchesChecksum(file, checksum) ? context.LoadFromAssemblyPath(path) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static bool MatchesChecksum(Stream stream, string expected) =>
        Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartApp()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }
}
#endif
