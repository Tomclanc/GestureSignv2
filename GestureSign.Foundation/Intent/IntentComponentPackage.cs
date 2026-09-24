using System.IO.Compression;
using System.Security.Cryptography;

namespace GestureSign.Foundation.Intent;

public sealed record IntentComponentAsset(string Architecture, string Version, string FileName, long Bytes, string Sha256, string Url);
public sealed record IntentComponentManifest(int Protocol, string Version, string Architecture);

public static class IntentComponentPackage
{
    public const string ComponentVersion = "18.2.9";
    public const int Protocol = 2;

    // Archive digests come from the application-shipped catalog, not from the downloaded archive.
    public static void Install(string archivePath, IntentComponentAsset asset, string installDirectory, CancellationToken cancellationToken = default)
    {
        if (asset.Version != ComponentVersion || asset.Architecture is not ("x64" or "arm64")) throw new InvalidDataException("不兼容的学习组件版本。");
        using (var input = File.OpenRead(archivePath))
        {
            if (input.Length != asset.Bytes || !Convert.ToHexString(SHA256.HashData(input)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("组件校验失败，请重新下载或选择匹配的离线包。");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidDataException("Invalid installation path.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".Intent-stage-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(parent, ".Intent-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                if (archive.Entries.Count > 2000 || archive.Entries.Sum(e => e.Length) > 512L * 1024 * 1024) throw new InvalidDataException("组件解压大小异常。");
                Directory.CreateDirectory(staging);
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!path.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains(':') || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                        throw new InvalidDataException("组件包含不安全路径。");
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(path); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!); entry.ExtractToFile(path);
                }
            }
            var manifest = IntentFiles.Read<IntentComponentManifest>(Path.Combine(staging, "component.json"));
            if (manifest is null || manifest.Protocol != Protocol || manifest.Version != asset.Version || manifest.Architecture != asset.Architecture) throw new InvalidDataException("组件协议或架构不匹配。");
            VerifyExecutable(Path.Combine(staging, "Runtime", "GestureSign.IntentDlc.exe"), asset.Architecture);
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(destination)) Directory.Move(destination, backup);
            try { Directory.Move(staging, destination); }
            catch { if (Directory.Exists(backup)) Directory.Move(backup, destination); throw; }
        }
        finally
        {
            // Only generated sibling directories are eligible for recursive cleanup; never user data.
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            if (Directory.Exists(backup) && Directory.Exists(destination)) Directory.Delete(backup, true);
        }
    }
    private static void VerifyExecutable(string path, string architecture)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.BaseStream.Length < 64 || reader.ReadUInt16() != 0x5A4D) throw new InvalidDataException("组件缺少有效的后台引擎。");
        reader.BaseStream.Position = 0x3c; int offset = reader.ReadInt32();
        if (offset < 64 || offset > reader.BaseStream.Length - 6) throw new InvalidDataException("Invalid executable header.");
        reader.BaseStream.Position = offset;
        if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != (architecture == "x64" ? 0x8664 : 0xAA64)) throw new InvalidDataException("组件处理器架构不匹配。");
    }
}
