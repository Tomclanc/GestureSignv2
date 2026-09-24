using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GestureSign.Foundation.Intent;

public static class IntentComponentLocation
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint length, StringBuilder? name);
    public static bool IsPackaged
    {
        get { uint length = 0; return GetCurrentPackageFullName(ref length, null) != 15700; }
    }
    public static string ApplicationRoot(string baseDirectory)
    {
        var root = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(Path.GetFileName(root), "Backend", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(root)!.FullName : root;
    }
    public static string UserDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GestureSign V2", "Components", "IntentDlc");
    public static string ProgramDirectory(string baseDirectory) => Path.Combine(ApplicationRoot(baseDirectory), "Components", "IntentDlc");
    private static string PreferencePath(string baseDirectory)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ApplicationRoot(baseDirectory).ToUpperInvariant())))[..20];
        return Path.Combine(IntentFiles.Root, "component-location-" + key + ".json");
    }
    public static bool UsesProgramDirectory(string baseDirectory)
    {
        if (IsPackaged) return false;
        try { if (File.Exists(PreferencePath(baseDirectory))) return IntentFiles.Read<bool>(PreferencePath(baseDirectory)); } catch { }
        // A copied portable application finds its colocated component without importing preferences.
        return File.Exists(Path.Combine(ProgramDirectory(baseDirectory), "component.json"));
    }
    public static string Resolve(string baseDirectory) => UsesProgramDirectory(baseDirectory) ? ProgramDirectory(baseDirectory) : UserDirectory;
    public static void Select(string baseDirectory, bool programDirectory)
    {
        if (programDirectory && IsPackaged) throw new InvalidOperationException("商店版使用用户数据目录安装 AI 组件。");
        IntentFiles.Write(PreferencePath(baseDirectory), programDirectory);
    }
    public static void CheckWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-check-" + Guid.NewGuid().ToString("N"));
            using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException("无法写入组件目录。请选择用户数据目录，或以管理员身份运行设置后安装到程序目录。", ex);
        }
    }
}
