using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

namespace GestureSign.Shared;

internal static class HostAssemblySharing
{
    public static void RegisterIfPresent(string[] args, params string[] allowedAssemblies)
    {
        var directory = AppContext.BaseDirectory;
        var manifest = Path.Combine(directory, "shared-assemblies.json");
        if (!File.Exists(manifest)) return;

        var checksums = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifest))
            ?? throw new InvalidDataException("Missing shared assembly checksums.");
        var hostIndex = Array.IndexOf(args, "--host");
        var hostDirectory = hostIndex >= 0 && hostIndex + 1 < args.Length
            ? Path.GetDirectoryName(args[hostIndex + 1])
            : null;

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (!allowedAssemblies.Contains(name.Name, StringComparer.OrdinalIgnoreCase))
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
}
