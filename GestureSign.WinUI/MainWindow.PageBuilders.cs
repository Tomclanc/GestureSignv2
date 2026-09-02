using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    // About is intentionally isolated from the event-heavy shell. Keeping page
    // construction in partials lets the remaining pages move incrementally
    // without changing navigation or the existing control helpers.
    private UIElement BuildAboutPage()
    {
        var root = NewSection();
        var content = NewCardPanel();
        content.Children.Add(new Image { Source = new BitmapImage(new Uri("ms-appx:///Assets/logo.png")), Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Left });
        content.Children.Add(new TextBlock { Text = "GestureSign V2", Style = ResourceStyle("TitleTextBlockStyle"), IsTextSelectionEnabled = true, Margin = new Thickness(0, 12, 0, 0) });
        content.Children.Add(new TextBlock
        {
            Text = $"{L("WinUI3重构", "WinUI 3 Rebuild", "WinUI3重構", "WinUI 3 再構築", "WinUI 3 재구축")}\n{L("版本", "Version", "版本", "バージョン", "버전")}：{AppVersion}",
            IsTextSelectionEnabled = true,
            Opacity = 0.72,
            Margin = new Thickness(0, 4, 0, 0)
        });
        content.Children.Add(new TextBlock
        {
            Text = L(
                "作者：风夏\nQQ 交流群：1054687130\n发现问题或建议欢迎反馈：z1021847549@outlook.com",
                "Author: 风夏\nQQ group: 1054687130\nFeedback: z1021847549@outlook.com",
                "作者：风夏\nQQ 交流群：1054687130\n問題或建議請回饋至：z1021847549@outlook.com",
                "作者：风夏\nQQ グループ：1054687130\n問題や提案：z1021847549@outlook.com",
                "작성자: 风夏\nQQ 그룹: 1054687130\n문제 및 제안: z1021847549@outlook.com"),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0)
        });
        content.Children.Add(NewSmallCommandBar([
            L("打开官网", "Open website", "開啟官網", "公式サイト", "웹사이트 열기"),
            L("Windows 应用商店版", "Microsoft Store", "Microsoft Store 版本", "Microsoft Store", "Microsoft Store"),
            L("发送反馈", "Send feedback", "傳送回饋", "フィードバック", "피드백 보내기"),
            L("查看日志", "View logs", "檢視記錄", "ログを表示", "로그 보기")
        ]));
        root.Children.Add(NewCard(content));
        root.Children.Add(NewProjectLinksCard(
            L("项目页面", "Project Pages", "專案頁面", "プロジェクトページ", "프로젝트 페이지"),
            [
                ("GestureSign V2", "https://github.com/Tomclanc/GestureSignv2"),
                (L("原始项目（TransposonY）", "Original project (TransposonY)", "原始專案（TransposonY）", "オリジナル（TransposonY）", "원본 프로젝트 (TransposonY)"), "https://github.com/TransposonY/GestureSign"),
                ("Kando (Simon Schneegans)", "https://github.com/kando-menu/kando")
            ],
            "Thanks: highsign, MahApps.Metro, WGestures."));
        return root;
    }
}
