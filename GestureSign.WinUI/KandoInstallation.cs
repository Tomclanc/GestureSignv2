using System;
using System.IO;
using System.Threading.Tasks;

namespace GestureSign.WinUI;

internal static class KandoInstallation
{
    // Keep the previous application and settings until replacement and startup
    // validation both succeed. Download/extraction happen before entering here.
    public static async Task ReplaceAsync(string payload, string destination, string userData,
        Func<Task>? beforeReplace, Func<Task>? validate, Func<Task>? stop, Func<Task>? restore)
    {
        var backup = destination + ".backup-" + Guid.NewGuid().ToString("N");
        var dataBackup = destination + ".settings-" + Guid.NewGuid().ToString("N");
        var hadApplication = Directory.Exists(destination);
        var hadData = Directory.Exists(userData);
        var replaced = false;
        var movedOld = false;
        var stopped = false;
        var settingsSaved = false;
        try
        {
            stopped = true;
            if (beforeReplace is not null) await beforeReplace();
            await Task.Run(() =>
            {
                if (hadData) CopyDirectory(userData, dataBackup);
                settingsSaved = true;
                if (hadApplication)
                {
                    Directory.Move(destination, backup);
                    movedOld = true;
                }
                Directory.Move(payload, destination);
                replaced = true;
            });
            if (validate is not null) await validate();
        }
        catch (Exception error)
        {
            try
            {
                if (replaced && stop is not null) await stop();
                await Task.Run(() =>
                {
                    if (replaced) Directory.Delete(destination, true);
                    if (movedOld) Directory.Move(backup, destination);
                    if (replaced && settingsSaved)
                    {
                        if (Directory.Exists(userData)) Directory.Delete(userData, true);
                        if (hadData) CopyDirectory(dataBackup, userData);
                    }
                });
                if (stopped && restore is not null) await restore();
            }
            catch (Exception rollbackError)
            {
                // Retain both backups if rollback could not complete.
                throw new AggregateException($"Kando update failed. Recovery files: {backup}; {dataBackup}", error, rollbackError);
            }
            TryDeleteDirectory(dataBackup);
            throw;
        }
        TryDeleteDirectory(backup);
        TryDeleteDirectory(dataBackup);
    }

    public static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
