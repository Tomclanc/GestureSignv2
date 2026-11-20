using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private sealed record BuiltInName(string Chinese, string English, string TraditionalChinese, string Korean, params string[] Aliases)
    {
        public IEnumerable<string> KnownNames => new[] { Chinese, English, TraditionalChinese, Korean }.Concat(Aliases);
    }

    private static readonly BuiltInName[] ApplicationNames =
    [
        new("(全局动作)", "(Global Actions)", "(全域動作)", "(전역 동작)", "全局动作"),
        new("Windows 资源管理器", "Windows Explorer", "Windows 檔案總管", "Windows 탐색기"),
        new("浏览器", "Browser", "瀏覽器", "브라우저"),
        new("UWP 应用", "UWP App", "UWP 應用程式", "UWP 앱", "uwp application"),
        new("Windows Ink 工作区", "Windows Ink Workspace", "Windows Ink 工作區", "Windows Ink 작업 영역"),
    ];

    private static readonly BuiltInName[] ActionNames =
    [
        new("保存", "Save", "儲存", "저장"),
        new("复制", "Copy", "複製", "복사"),
        new("剪切", "Cut", "剪下", "잘라내기"),
        new("粘贴", "Paste", "貼上", "붙여넣기"),
        new("撤销", "Undo", "復原", "실행 취소"),
        new("重做", "Redo", "重做", "다시 실행"),
        new("删除", "Delete", "刪除", "삭제"),
        new("全选", "Select All", "全選", "모두 선택"),
        new("打开网页浏览器", "Open Web Browser", "開啟網頁瀏覽器", "웹 브라우저 열기", "open browser", "open default browser", "打开浏览器", "打开默认浏览器"),
        new("关闭", "Close", "關閉", "닫기"),
        new("关闭窗口", "Close Window", "關閉視窗", "창 닫기"),
        new("关闭标签页", "Close Tab", "關閉分頁", "탭 닫기", "현재 탭 닫기"),
        new("新建标签页", "New Tab", "新增分頁", "새 탭", "새 탭 생성"),
        new("重新打开关闭的标签页", "Reopen Closed Tab", "重新開啟關閉的分頁", "닫은 탭 다시 열기"),
        new("上一个标签页", "Previous Tab", "上一個分頁", "이전 탭", "왼쪽 탭으로"),
        new("下一个标签页", "Next Tab", "下一個分頁", "다음 탭", "오른쪽 탭으로"),
        new("后退", "Back", "返回", "뒤로"),
        new("前进", "Forward", "前進", "앞으로", "앞으로 가기"),
        new("刷新", "Refresh", "重新整理", "새로 고침"),
        new("最小化", "Minimize", "最小化", "최소화", "현재 창 최소화"),
        new("最大化", "Maximize", "最大化", "최대화"),
        new("还原", "Restore", "還原", "복원"),
        new("最大化/还原", "Maximize/Restore", "最大化/還原", "최대화/복원", "Maximize or Restore"),
        new("显示桌面", "Show Desktop", "顯示桌面", "바탕 화면 표시"),
        new("切换窗口", "Switch Window", "切換視窗", "창 전환"),
        new("上一个窗口", "Previous Window", "上一個視窗", "이전 창", "previous application"),
        new("下一个窗口", "Next Window", "下一個視窗", "다음 창", "next application"),
        new("增大音量", "Volume Up", "增大音量", "볼륨 높이기", "increase volume"),
        new("减小音量", "Volume Down", "降低音量", "볼륨 낮추기", "decrease volume"),
        new("静音", "Mute", "靜音", "음소거"),
        new("播放/暂停", "Play/Pause", "播放/暫停", "재생/일시 정지", "play pause"),
        new("媒体播放/暂停", "Media Play/Pause", "媒體播放/暫停", "미디어 재생/일시 정지", "미디어 일시중지/재생"),
        new("上一曲", "Previous Track", "上一首", "이전 트랙"),
        new("下一曲", "Next Track", "下一首", "다음 트랙"),
        new("运行命令", "Run Command", "執行命令", "명령 실행"),
        new("打开文件", "Open File", "開啟檔案", "파일 열기"),
        new("启动应用", "Launch App", "啟動應用程式", "앱 실행"),
        new("延迟等待", "Delay", "延遲等待", "지연"),
        new("鼠标动作", "Mouse Action", "滑鼠動作", "마우스 동작", "mouse actions"),
        new("屏幕亮度", "Screen Brightness", "螢幕亮度", "화면 밝기"),
        new("提高亮度", "Brightness Up", "提高亮度", "밝기 높이기", "밝기 높히기"),
        new("降低亮度", "Brightness Down", "降低亮度", "밝기 낮추기"),
        new("激活窗口", "Activate Window", "啟用視窗", "창 활성화"),
        new("触摸键盘", "Touch Keyboard", "觸控鍵盤", "터치 키보드"),
        new("窗口置顶", "Toggle Topmost", "視窗置頂", "항상 위 전환", "toggle window topmost"),
        new("临时禁用手势", "Temporarily Disable Gestures", "暫時停用手勢", "제스처 임시 비활성화", "temporarily disable"),
        new("切换禁用手势", "Toggle Gesture Disable", "切換停用手勢", "제스처 비활성화 전환", "toggle disable gestures"),
        new("发送快捷键", "Send Hotkey", "傳送快速鍵", "단축키 보내기", "Send Hot Key"),
        new("屏幕截图", "Screen Capture", "螢幕擷取", "화면 캡처", "Screenshot"),
        new("切换应用", "Switch Apps", "切換應用程式", "앱 전환"),
        new("任务视图", "Task View", "工作檢視", "작업 보기", "모든 작업 보기"),
        new("全屏", "Full Screen", "全螢幕", "전체 화면", "Fullscreen"),
        new("切换到右侧桌面", "Switch to Desktop on the Right", "切換到右側桌面", "오른쪽 데스크톱으로 전환"),
        new("切换到左侧桌面", "Switch to Desktop on the Left", "切換到左側桌面", "왼쪽 데스크톱으로 전환"),
        new("下一曲（笔橡皮擦）", "Next Track (Pen Eraser)", "下一首（筆橡皮擦）", "다음 트랙(펜 지우개)"),
        new("上一曲（笔橡皮擦）", "Previous Track (Pen Eraser)", "上一首（筆橡皮擦）", "이전 트랙(펜 지우개)"),
        new("右键单击", "Right click", "按一下滑鼠右鍵", "마우스 오른쪽 버튼 클릭", "우클릭"),
        new("显示/隐藏触摸键盘", "Show/Hide Touch Keyboard", "顯示/隱藏觸控鍵盤", "터치 키보드 표시/숨기기"),
        new("S形手势", "S-shaped Gesture", "S 形手勢", "S자 제스처"),
        new("双指左滑", "Two-finger Swipe Left", "雙指左滑", "두 손가락 왼쪽 스와이프"),
        new("双指上下滑", "Two-finger Vertical Swipe", "雙指上下滑", "두 손가락 위아래 스와이프"),
        new("双指平行左滑", "Two-finger Parallel Swipe Left", "雙指平行左滑", "두 손가락 평행 왼쪽 스와이프"),
        new("双指手", "Two-finger Gesture", "雙指手勢", "두 손가락 제스처"),
        new("三指左滑", "Three-finger Swipe Left", "三指左滑", "세 손가락 왼쪽 스와이프"),
        new("三指L形", "Three-finger L Shape", "三指 L 形", "세 손가락 L자"),
        new("四指L形", "Four-finger L Shape", "四指 L 形", "네 손가락 L자"),
        new("四指点按", "Four-finger Tap", "四指點按", "네 손가락 탭"),
        new("四指下滑", "Four-finger Swipe Down", "四指下滑", "네 손가락 아래 스와이프"),
        new("四指右滑", "Four-finger Swipe Right", "四指右滑", "네 손가락 오른쪽 스와이프"),
        new("四指左滑", "Four-finger Swipe Left", "四指左滑", "네 손가락 왼쪽 스와이프"),
        new("触控板上边缘点击", "Touchpad Top Edge Tap", "觸控板上邊緣點擊", "터치패드 위쪽 가장자리 탭", "touchpadedge.top"),
        new("触控板下边缘点击", "Touchpad Bottom Edge Tap", "觸控板下邊緣點擊", "터치패드 아래쪽 가장자리 탭", "touchpadedge.bottom"),
        new("触控板左边缘点击", "Touchpad Left Edge Tap", "觸控板左邊緣點擊", "터치패드 왼쪽 가장자리 탭", "touchpadedge.left"),
        new("触控板右边缘点击", "Touchpad Right Edge Tap", "觸控板右邊緣點擊", "터치패드 오른쪽 가장자리 탭", "touchpadedge.right"),
        new("触控屏上边缘点击", "Touchscreen Top Edge Tap", "觸控螢幕上邊緣點擊", "터치스크린 위쪽 가장자리 탭", "touchscreenedge.top"),
        new("触控屏下边缘点击", "Touchscreen Bottom Edge Tap", "觸控螢幕下邊緣點擊", "터치스크린 아래쪽 가장자리 탭", "touchscreenedge.bottom"),
        new("触控屏左边缘点击", "Touchscreen Left Edge Tap", "觸控螢幕左邊緣點擊", "터치스크린 왼쪽 가장자리 탭", "touchscreenedge.left"),
        new("触控屏右边缘点击", "Touchscreen Right Edge Tap", "觸控螢幕右邊緣點擊", "터치스크린 오른쪽 가장자리 탭", "touchscreenedge.right"),
        new("向左", "Left", "向左", "왼쪽"),
        new("向右", "Right", "向右", "오른쪽"),
        new("向上", "Up", "向上", "위"),
        new("向下", "Down", "向下", "아래"),
        new("缩小页面", "Zoom Out", "縮小頁面", "페이지 축소", "页面缩小"),
        new("放大页面", "Zoom In", "放大頁面", "페이지 확대", "页面放大"),
        new("返回", "Go Back", "返回", "뒤로 가기"),
    ];

    private static readonly Lazy<Dictionary<string, BuiltInName>> ApplicationNameIndex = new(() => IndexBuiltInNames(ApplicationNames));
    private static readonly Lazy<Dictionary<string, BuiltInName>> ActionNameIndex = new(() => IndexBuiltInNames(ActionNames));
    private static readonly Lazy<HashSet<string>> UntitledNames = new(() =>
        new HashSet<string>(UiTranslationCatalog.GetKnownTranslations("Untitled"), StringComparer.OrdinalIgnoreCase));

    private static Dictionary<string, BuiltInName> IndexBuiltInNames(BuiltInName[] names)
    {
        var explicitNames = new Dictionary<string, BuiltInName>(StringComparer.OrdinalIgnoreCase);
        var translatedNames = new Dictionary<string, BuiltInName>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            foreach (var alias in name.KnownNames)
                explicitNames.TryAdd(alias, name);

            foreach (var translation in UiTranslationCatalog.GetKnownTranslations(name.English))
            {
                if (!translatedNames.TryAdd(translation, name) && translatedNames[translation] != name)
                    ambiguous.Add(translation);
            }
        }

        foreach (var (translation, name) in translatedNames)
            if (!ambiguous.Contains(translation))
                explicitNames.TryAdd(translation, name);

        return explicitNames;
    }

    private string ApplicationDisplayName(LegacyApplication app)
    {
        const int matchExecutable = 2;
        var value = app.Name;
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (app.MatchUsing == matchExecutable)
        {
            var productName = ApplicationProductNames.DisplayName(ResolveUiCultureName(_uiCultureName), value, app.MatchString);
            if (!string.Equals(value, productName, StringComparison.Ordinal))
                return productName;
        }
        if (ApplicationNameIndex.Value.TryGetValue(value.Trim(), out var name))
            return L(name.Chinese, name.English, name.TraditionalChinese, name.Korean);

        const string oneNoteSuffix = " - OneNote";
        if (value.EndsWith(oneNoteSuffix, StringComparison.OrdinalIgnoreCase) &&
            UntitledNames.Value.Contains(value[..^oneNoteSuffix.Length].Trim()))
            return L("无标题", "Untitled", "無標題", "제목 없음") + oneNoteSuffix;

        return value;
    }

    private string DisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return L("未命名", "Unnamed", "未命名", "이름 없음");

        var trimmed = value.Trim();
        if (DefaultGestureCaption(trimmed) is string caption) return caption;
        var names = ActionNameIndex.Value;
        if (names.TryGetValue(trimmed, out var name))
            return L(name.Chinese, name.English, name.TraditionalChinese, name.Korean);

        // The legacy command editor appends "(2)", "(3)", etc. to duplicates.
        // Translate the recognized caption and keep the original numbering.
        var suffix = trimmed.LastIndexOf('(');
        if (suffix > 0 && trimmed.EndsWith(')') &&
            int.TryParse(trimmed.AsSpan(suffix + 1, trimmed.Length - suffix - 2), NumberStyles.None,
                CultureInfo.InvariantCulture, out var number) && number >= 2 &&
            names.TryGetValue(trimmed[..suffix].TrimEnd(), out name))
            return L(name.Chinese, name.English, name.TraditionalChinese, name.Korean) + trimmed[suffix..];

        return value;
    }
}
