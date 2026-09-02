using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private async void HandleCommand(string command)
    {
        try
        {
            switch (command)
            {
                case "打开配置文件夹":
                    Directory.CreateDirectory(_legacyData.RoamingPath);
                    Process.Start(new ProcessStartInfo("explorer.exe", _legacyData.RoamingPath) { UseShellExecute = true });
                    break;
                case "备份":
                    var backupPath = _legacyData.CreateBackup();
                    await ShowInfoDialog("备份完成", backupPath);
                    break;
                case "恢复":
                    await RestoreArchiveAsync();
                    break;
                case "退出":
                case "Exit":
                case "結束":
                case "終了":
                case "종료":
                    await ExitAllGestureSignProcessesAsync();
                    break;
                case "导入":
                    await ImportActionsAsync();
                    break;
                case "导出":
                    await ExportActionsAsync();
                    break;
                case "添加程序":
                    await AddApplicationAsync(false);
                    break;
                case "添加忽略项":
                    await AddApplicationAsync(true);
                    break;
                case "新动作":
                    await AddActionAsync(ResolveDefaultActionTarget());
                    break;
                case "导入手势文件":
                    await ImportGesturesAsync();
                    break;
                case "导出手势文件":
                    await ExportGesturesAsync();
                    break;
                case "新建手势":
                    await AddGestureAsync();
                    break;
                case "绘制手势":
                    await DrawGestureAsync(null);
                    break;
                case "后台训练手势":
                    await StartDaemonGestureTrainingAsync();
                    break;
                case "下载列表":
                    await DownloadSharedSettingsAsync();
                    break;
                case "查看日志":
                case "View logs":
                case "檢視記錄":
                case "ログを表示":
                case "로그 보기":
                    await ShowLogAsync();
                    break;
                case "发送反馈":
                case "Send feedback":
                case "傳送回饋":
                case "フィードバック":
                case "피드백 보내기":
                    await SendFeedbackAsync();
                    break;
                case "打开官网":
                case "Open website":
                case "開啟官網":
                case "公式サイト":
                case "웹사이트 열기":
                    Process.Start(new ProcessStartInfo("https://github.com/Tomclanc/GestureSignv2") { UseShellExecute = true });
                    break;
                case "Windows 应用商店版":
                case "Microsoft Store":
                case "Microsoft Store 版本":
                    Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=9P2WKMHF43PN") { UseShellExecute = true });
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            await ShowInfoDialog("操作失败", ex.Message);
        }
    }

}

