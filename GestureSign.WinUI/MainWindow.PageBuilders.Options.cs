using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Xaml;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private UIElement BuildOptionsPageCore()
    {
        var root = NewSection();
        var options = _legacyData.Options;
        root.Children.Add(NewSettingsGroup(L("视觉反馈", "Visual Feedback", "視覺回饋", "視覚フィードバック", "시각 피드백"),
        [
            NewToggleRow(L("显示手势轨迹", "Show gesture trail", "顯示手勢軌跡", "ジェスチャ軌跡を表示", "제스처 궤적 표시"), options.VisualFeedbackWidth > 0, "VisualFeedbackWidth", options.VisualFeedbackWidth == 0 ? "9" : options.VisualFeedbackWidth.ToString(), "0"),
            NewToggleRow(L("显示触发的手势操作", "Show triggered gesture action", "顯示觸發的手勢動作", "実行したジェスチャアクションを表示", "실행된 제스처 동작 표시"), options.ShowGestureActionHint, "ShowGestureActionHint"),
            NewSliderRow(L("轨迹透明度", "Trail opacity", "軌跡透明度", "軌跡の透明度", "궤적 투명도"), options.Opacity, 0.05, 1, 0.01, "Opacity", value => value.ToString("0.00", CultureInfo.InvariantCulture), value => $"{Math.Round(value * 100)}%"),
            NewSliderRow(L("轨迹宽度", "Trail width", "軌跡寬度", "軌跡の幅", "궤적 너비"), options.VisualFeedbackWidth, 0, 30, 1, "VisualFeedbackWidth", value => ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture), value => $"{(int)Math.Round(value)} px"),
            NewSliderRow(L("最小点距离", "Minimum point distance", "最小點距離", "最小ポイント距離", "최소 지점 거리"), options.MinimumPointDistance, 1, 100, 1, "MinimumPointDistance", value => ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture), value => $"{(int)Math.Round(value)} px"),
            NewVisualFeedbackColorRow(options.VisualFeedbackColor)
        ]));
        root.Children.Add(NewSettingsGroup(L("输入设备", "Input Devices", "輸入裝置", "入力デバイス", "입력 장치"),
        [
            NewToggleRow(L("启用鼠标手势", "Enable mouse gestures", "啟用滑鼠手勢", "マウスジェスチャを有効にする", "마우스 제스처 사용"), NormalizeDrawingButton(options.DrawingButton) != 0, "DrawingButton", NormalizeDrawingButton(options.DrawingButton, 2097152).ToString(CultureInfo.InvariantCulture), "0"),
            NewToggleRow(L("Edge 优先使用自带鼠标手势", "Prefer built-in Edge mouse gestures", "Edge 優先使用內建滑鼠手勢", "Edge 内蔵マウスジェスチャを優先", "Edge 기본 마우스 제스처 우선 사용"), options.PreferEdgeMouseGestures, "PreferEdgeMouseGestures"),
            NewComboRow(L("绘制按钮", "Drawing button", "繪製按鈕", "描画ボタン", "그리기 버튼"), [L("右键", "Right button", "右鍵", "右ボタン", "오른쪽 버튼"), L("中键", "Middle button", "中鍵", "中央ボタン", "가운데 버튼"), "X1", "X2"], ["2097152", "4194304", "8388608", "16777216"], "DrawingButton", DrawingButtonIndex(NormalizeDrawingButton(options.DrawingButton, 2097152))),
            NewToggleRow(L("启用触摸屏手势", "Enable touchscreen gestures", "啟用觸控螢幕手勢", "タッチスクリーンジェスチャを有効にする", "터치스크린 제스처 사용"), options.RegisterTouchScreen, "RegisterTouchScreen"),
            NewTouchScreenBlockedAreaRow(options),
            NewToggleRow(L("启用触控板手势", "Enable touchpad gestures", "啟用觸控板手勢", "タッチパッドジェスチャを有効にする", "터치패드 제스처 사용"), options.RegisterTouchPad, "RegisterTouchPad"),
            NewToggleRow(L("优先使用 Windows 触控板系统手势", "Prefer Windows touchpad gestures", "優先使用 Windows 觸控板系統手勢", "Windows のタッチパッドジェスチャを優先", "Windows 터치패드 제스처 우선 사용"), options.PreferWindowsTouchPadGestures, "PreferWindowsTouchPadGestures"),
            NewToggleRow(L("启用触控笔手势", "Enable pen gestures", "啟用觸控筆手勢", "ペンジェスチャを有効にする", "펜 제스처 사용"), options.PenGestureButton != 0, "PenGestureButton", options.PenGestureButton == 0 ? "4" : options.PenGestureButton.ToString(CultureInfo.InvariantCulture), "0"),
            NewPenButtonRow(options.PenGestureButton)
        ]));
        var languageValues = new[] { "" }.Concat(UiTranslationCatalog.SupportedCultureNames).ToArray();
        var languageItems = new[] { L("跟随系统", "Follow system", "跟隨系統", "システムに合わせる", "시스템 설정 따르기") }.Concat(UiTranslationCatalog.SupportedCultureNames.Select(UiTranslationCatalog.GetNativeDisplayName)).ToArray();
        var systemRows = new List<FrameworkElement>
        {
            NewComboRow(L("语言", "Language", "語言", "言語", "언어"), languageItems, languageValues, "CultureName", CultureIndex(_uiCultureName)),
            NewToggleRow(L("启用初始超时", "Enable initial timeout", "啟用初始逾時", "初期タイムアウトを有効にする", "초기 시간 제한 사용"), options.InitialTimeout > 0, "InitialTimeout", options.InitialTimeout == 0 ? "1000" : options.InitialTimeout.ToString(), "0"),
            NewSliderRow(L("初始超时", "Initial timeout", "初始逾時", "初期タイムアウト", "초기 시간 제한"), options.InitialTimeout / 1000d, 0, 2, 0.1, "InitialTimeout", value => ((int)Math.Round(value * 1000)).ToString(CultureInfo.InvariantCulture), value => $"{value:0.0} {L("秒", "sec", "秒", "秒", "초")}"),
            NewStartupToggleRow(), NewAdminStartupToggleRow(),
            NewToggleRow(L("排除全屏游戏/应用", "Ignore fullscreen games/apps", "排除全螢幕遊戲/應用程式", "全画面ゲーム/アプリを除外", "전체 화면 게임/앱 제외"), options.IgnoreFullScreen, "IgnoreFullScreen"),
            NewToggleRow(L("排除全屏播放视频（试验）", "Ignore fullscreen video playback (experimental)", "排除全螢幕影片播放（實驗）", "全画面動画再生を除外（実験）", "전체 화면 동영상 재생 제외(실험)"), options.IgnoreFullScreenVideo, "IgnoreFullScreenVideo"),
            NewToggleRow(L("使用笔时忽略触摸输入", "Ignore touch input while using pen", "使用筆時忽略觸控輸入", "ペン使用中はタッチ入力を無視", "펜 사용 중 터치 입력 무시"), options.IgnoreTouchInputWhenUsingPen, "IgnoreTouchInputWhenUsingPen"),
            NewToggleRow(L("显示托盘图标", "Show tray icon", "顯示系統匣圖示", "トレイアイコンを表示", "트레이 아이콘 표시"), options.ShowTrayIcon, "ShowTrayIcon"),
            NewOneDriveSyncRow(), NewOpenSettingsHotKeyRow(options.OpenSettingsHotKey),
            NewToggleRow(L("错误日志提示", "Error log notifications", "錯誤記錄提示", "エラーログ通知", "오류 로그 알림"), options.SendErrorReport, "SendErrorReport"),
            NewButtonRow(L("配置文件", "Configuration files", "設定檔", "設定ファイル", "구성 파일"), [L("备份", "Backup", "備份", "バックアップ", "백업"), L("恢复", "Restore", "還原", "復元", "복원"), L("打开配置文件夹", "Open config folder", "開啟設定檔資料夾", "設定フォルダーを開く", "구성 폴더 열기")]),
            NewButtonRow(L("退出", "Exit", "結束", "終了", "종료"), [L("退出", "Exit", "結束", "終了", "종료")])
        };
        if (!IsPackagedInstallation()) systemRows.Insert(systemRows.Count - 2, NewUpdateSettingsRow(options));
        root.Children.Add(NewSettingsGroup(L("系统", "System", "系統", "システム", "시스템"), systemRows.ToArray()));
        return root;
    }
}
