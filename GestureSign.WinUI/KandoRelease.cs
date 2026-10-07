using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GestureSign.WinUI;

internal sealed record KandoRelease(string TagName, Version Version, Uri DownloadUri, Uri ReleaseUri, string Notes, string? Sha256)
{
    public static Version? ParseVersion(string? value)
        => Version.TryParse((value ?? "").Trim().TrimStart('v', 'V'), out var version) ? version : null;

    public static KandoRelease Parse(string json, Architecture architecture)
    {
        var platform = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException("Kando requires x64 or ARM64 Windows.")
        };
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("The Kando release is not a stable release.");
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = ParseVersion(tag) ?? throw new InvalidDataException("Invalid Kando release version.");
        var expectedName = $"Kando-win32-{platform}-{tag.TrimStart('v', 'V')}.zip";
        var asset = root.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a => string.Equals(a.GetProperty("name").GetString(), expectedName, StringComparison.OrdinalIgnoreCase));
        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException($"The latest stable Kando release has no {platform} Windows ZIP. Please try again later.");
        var uri = new Uri(asset.GetProperty("browser_download_url").GetString()!);
        if (uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/kando-menu/kando/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected Kando download URL.");
        string? hash = null;
        if (asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String)
        {
            var value = digest.GetString()!;
            if (!Regex.IsMatch(value, "^sha256:[0-9a-fA-F]{64}$"))
                throw new InvalidDataException("Invalid Kando asset checksum.");
            hash = value.Substring(7);
        }
        return new KandoRelease(tag, version, uri, new Uri($"https://github.com/kando-menu/kando/releases/tag/{Uri.EscapeDataString(tag)}"),
            root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "", hash);
    }

    public static string? ReadInstalledVersion(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return null;
        var directory = Path.GetDirectoryName(executable)!;
        try
        {
            // The Electron executable's file version can be Electron's version.
            // Read the application metadata instead, including ZIP installations.
            var package = Path.Combine(directory, "resources", "app", "package.json");
            if (File.Exists(package)) return ReadPackageVersion(File.ReadAllText(package));
            var asar = Path.Combine(directory, "resources", "app.asar");
            if (File.Exists(asar))
            {
                using var stream = File.OpenRead(asar);
                using var reader = new BinaryReader(stream);
                if (reader.ReadUInt32() != 4) throw new InvalidDataException("Invalid ASAR header.");
                var headerSize = reader.ReadUInt32();
                reader.ReadUInt32();
                var jsonSize = reader.ReadUInt32();
                if (jsonSize > 16 * 1024 * 1024 || headerSize < jsonSize + 8 || 8L + headerSize > stream.Length)
                    throw new InvalidDataException("Invalid ASAR header size.");
                using var header = JsonDocument.Parse(reader.ReadBytes((int)jsonSize));
                var entry = header.RootElement.GetProperty("files").GetProperty("package.json");
                var size = entry.GetProperty("size").GetInt32();
                var offset = long.Parse(entry.GetProperty("offset").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                if (size < 0 || size > 1024 * 1024 || offset < 0 || 8L + headerSize + offset + size > stream.Length)
                    throw new InvalidDataException("Invalid ASAR package entry.");
                stream.Position = 8L + headerSize + offset;
                return ReadPackageVersion(System.Text.Encoding.UTF8.GetString(reader.ReadBytes(size)));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException or UnauthorizedAccessException) { }
        try
        {
            var marker = Path.Combine(directory, ".gesturesign-component-version");
            if (File.Exists(marker))
            {
                var version = File.ReadAllText(marker).Trim();
                if (ParseVersion(version) is not null) return version;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    private static string? ReadPackageVersion(string json)
    {
        using var document = JsonDocument.Parse(json);
        var value = document.RootElement.GetProperty("version").GetString();
        return ParseVersion(value) is not null ? value : null;
    }
}
