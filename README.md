<p align="center">
  <img src="docs/assets/logo.png" width="96" alt="GestureSign V2 Logo">
</p>

<h1 align="center">GestureSign V2</h1>

<p align="center">
  面向 Windows 11 的触控板、触摸屏和鼠标手势工具，支持 TipTap、边缘交互与本地 AI 意图判断。
</p>

<p align="center">
  <a href="https://github.com/Tomclanc/GestureSignv2/releases/tag/v18.2.9">
    <img alt="Release" src="https://img.shields.io/github/v/release/Tomclanc/GestureSignv2?style=flat-square">
  </a>
  <a href="https://winstall.app/apps/Tomclanc.GestureSignV2">
    <img alt="WinGet" src="https://img.shields.io/badge/winget-Tomclanc.GestureSignV2-0078D4?style=flat-square">
  </a>
  <a href="https://apps.microsoft.com/store/detail/9P2WKMHF43PN?cid=DevShareMCLPCB">
    <img alt="Microsoft Store" src="https://img.shields.io/badge/Microsoft%20Store-Download-0078D4?style=flat-square&logo=microsoft">
  </a>
  <img alt="Windows 11" src="https://img.shields.io/badge/Windows-11-0078D4?style=flat-square">
  <img alt="WinUI 3" src="https://img.shields.io/badge/UI-WinUI%203-0078D4?style=flat-square">
  <img alt="Platform" src="https://img.shields.io/badge/Platform-x64-555?style=flat-square">
</p>

<p align="center">
  简体中文 | <a href="#english">English</a> | <a href="#日本語">日本語</a>
</p>

![GestureSign V2 主界面](docs/assets/screenshot-main-2026-07-02.png)

## 项目简介

GestureSign V2 是基于经典开源项目 [TransposonY/GestureSign](https://github.com/TransposonY/GestureSign) 的 Windows 11 适配重构版。

原版 GestureSign 长期未维护，在新系统和高强度使用场景下容易遇到按键粘滞、界面老旧、DPI 适配不足等问题。这个版本的目标很直接：保留原来的手势能力，同时修复 Windows 11 下的体验问题，并用更现代的 WinUI 3 界面重新承载配置流程。

## 主要特性

- WinUI 3 重构界面，适配 Windows 11 圆角、Mica 风格、深色 / 亮色模式动态切换。
- 支持触控板手势、触摸屏手势、鼠标手势、手势轨迹显示和手势缩略图预览。
- 新增“快捷操作”页面，可按需下载 Kando 可选组件，并用独立快捷键唤起径向菜单。
- 新增“边缘交互”页面，可为触控板和触摸屏上 / 下 / 左 / 右边缘点击与边缘滑动单独绑定动作。
- 边缘手势可作为普通动作加入任意程序分组，当前应用动作优先，未命中时自动回退全局动作。
- 本地意图学习与实验性 AI 否决，支持手动纠正误判；可用推理后端及回退情况在设置中显示。
- 支持按程序、窗口类名、可执行文件、标题和分组管理动作。
- 支持快捷键、浏览器、窗口、媒体、系统操作等常用命令；新增动作时可直接配置要执行的命令，音量、亮度、打开文件、运行命令等常用命令提供专用编辑控件。
- 支持忽略列表，可按 exe、窗口类名、标题等规则排除指定程序。
- 支持优先使用系统触控板设置、Edge 自带手势，并可排除全屏场景。
- 支持将配置文件切换到 OneDrive `Apps\GestureSign V2` 目录，由 OneDrive 负责跨设备同步。
- 支持托盘图标、托盘菜单、单实例启动和更方便阅读的手势日志；托盘可一键暂停/恢复手势识别。
- 支持 90 种 Windows 语言和地区变体，并为阿拉伯语、波斯语、希伯来语、乌尔都语和维吾尔语提供从右到左布局。
- 针对高 DPI、高刷新率屏幕做了界面和输入体验优化。

## 下载

### Microsoft Store（推荐）

<a href="https://apps.microsoft.com/store/detail/9P2WKMHF43PN?cid=DevShareMCLPCB"><img alt="从 Microsoft Store 获取" width="240px" src="https://get.microsoft.com/images/zh-cn%20dark.svg" /></a>

### WinGet

GestureSign V2 已发布到 Windows Package Manager，可以直接通过 winget 安装：

```powershell
winget install --id Tomclanc.GestureSignV2 --source winget
```

也可以前往 [Releases](https://github.com/Tomclanc/GestureSignv2/releases/tag/v18.2.9) 下载最新便携版。

GitHub 当前版本为 **18.2.9**；Microsoft Store 和 WinGet 的上架进度可能不同，获取此版本请使用下方 GitHub 附件。

当前版本：

- [GestureSign-V2-18.2.9-x64.msi](https://github.com/Tomclanc/GestureSignv2/releases/download/v18.2.9/GestureSign-V2-18.2.9-x64.msi)
- [GestureSign-V2-18.2.9-x64-portable.zip](https://github.com/Tomclanc/GestureSignv2/releases/download/v18.2.9/GestureSign-V2-18.2.9-x64-portable.zip)

18.2.9 运行需要 .NET 10 Desktop Runtime 和 Windows App SDK Runtime。NPU 另需兼容硬件、驱动与 Windows ML 官方执行提供程序。

## 更新内容

### 18.2.9

- 整合开发者预览 0.4 的本地意图学习与实验性 AI 否决，用于减少触控板双指智能关闭误触；后台学习与 AI 否决可分别开启。
- 支持样本批量标注、人工纠正、列表 / 网格 / 磁贴查看，以及可关闭、合并计数的 AI 否决通知。
- 接入 AMD NPU：NPU 执行线性计算，CPU 完成归一化、Sigmoid 和精度复核；界面显示实际后端，初始化或数值验证失败时回退 GPU / CPU。
- 修复 AMD 组件已安装但尚未就绪时无法检测 NPU、WindowsApps 路径导致编译失败及工作目录写入受限的问题。
- MSI 与便携版已内置本地 AI 推理组件，另提供离线组件 ZIP；不包含个人训练模型或样本。AMD 官方运行时按目标电脑环境准备，可点击“准备 NPU / GPU 组件”。
- 根据本版发布验证，Ryzen AI Z2 Extreme 通过 1,559 组输入测试，最终分数相对 CPU ONNX 最大误差约 0.000000894。当前最终使用 CPU 复核后的分数，不代表纯 NPU 推理或性能提升，其他 NPU 型号尚未完成本版实机验证。

AI 否决仍属实验功能，可能误拦截。训练在 CPU 上进行，样本和模型保存在本机；社区样本可通过 [提交入口](https://github.com/Tomclanc/GestureSignv2/issues/5) 自愿分享。

### 历史版本

此前版本加入了动作编辑中的 12 种 TipTap 选择与示意图、 Windows 11 原生亮度条、多指四方向 TipTap、单独发送 Win 键、边缘音量与亮度连续调节、四边滚动映射、触控板边缘光标固定和页面返回按钮；改进了无点击窗口激活、鼠标下方目标选择、智能关闭、桌面与全屏过滤及实时动作提示，并完善了 WinUI 3 界面、90 种语言与地区变体、RTL 布局、Kando 可选组件与升级迁移，以及输入、轨迹、触控和应用启动方面的修复。各版本详情请参阅 [GitHub Releases](https://github.com/Tomclanc/GestureSignv2/releases)。

## 安装

推荐使用 winget 安装：

```powershell
winget install --id Tomclanc.GestureSignV2 --source winget
```

也可以手动下载安装包：

1. 下载 MSI 或 MSIX 安装包。
2. 双击安装，按提示完成安装。
3. 从桌面快捷方式或开始菜单打开 `GestureSign V2`。
4. 在“动作”页面启用手势识别，并按需添加程序、手势和命令。

配置文件默认保存在：

```text
%AppData%\GestureSign V2
```

日志文件默认保存在：

```text
%LocalAppData%\GestureSign V2
```

## 快速使用

1. 打开“动作”页面，确认“手势识别”已开启。
2. 在左侧选择“全局动作”或某个程序分组。
3. 点击“新动作”，录制或绘制一个手势图案。
4. 点击“设置命令”，为这个手势绑定快捷键、浏览器、窗口或系统命令。
5. 回到桌面或目标应用中使用手势触发操作。

如果某个程序已经有系统级手势或自带手势，例如 Windows 11 触控板设置、Microsoft Edge 鼠标手势，可以在“选项”中开启优先使用系统或应用自带行为。

## 页面说明

- “动作”：管理全局动作、程序动作、分组、手势和命令。
- “忽略”：添加不参与识别的程序、窗口或匹配规则。
- “手势”：查看、导入、导出、重训和整理手势库。
- “快捷操作”：选择 Kando 菜单、同步唤起快捷键、打开 Kando 设置或测试弹出菜单。
- “边缘交互”：设置触控板和触摸屏四边点击与边缘滑动动作，以及触控板 TipTap 动作：按住 1、2 或 3 个手指，再用另一指在按住的手指组左、右、上、下轻点，可分别绑定共 12 种动作，支持连续轻点。
- “选项”：调整轨迹颜色、宽度、透明度、输入设备、全屏排除和启动项，并管理本地意图学习、AI 否决、样本纠正及推理组件。
- “关于”：查看版本、项目链接、日志和维护信息。

## 兼容性

- 推荐系统：Windows 11 x64；Microsoft Store 包同时提供 x64 与 ARM64 架构。
- 当前安装包：MSI x64、便携版 x64；此前商店上传包包含 x64 / ARM64；18.2.9 本次 GitHub 发布为 x64；不再提供 x86 包。
- Windows 10 理论上可运行部分功能，但主要适配目标是 Windows 11。

## 反馈问题

如果遇到手势无法触发、录制异常、配置无法保存或界面显示问题，请在 Issues 中提供：

- 系统版本和屏幕缩放比例。
- 使用的是鼠标手势还是触控板手势。
- 目标应用名称，以及是否全屏。
- “关于”页面中的日志内容。
- 相关截图或复现步骤。

## 致谢

感谢原项目 [TransposonY/GestureSign](https://github.com/TransposonY/GestureSign) 以及 HighSign、MahApps.Metro、WGestures 等项目。GestureSign V2 仍然站在这些工作的基础上继续前进。

“快捷操作”可以按需下载 [Kando](https://github.com/kando-menu/kando) 圆环菜单可选组件。Kando 默认不包含在 GestureSign 安装包中，可在应用内单独下载或卸载；Kando 是遵循 MIT License 的独立开源项目，组件保留其自带的 `LICENSE` 和 Chromium 相关许可证文件。

## 赞赏

如果 GestureSign V2 对你有帮助，欢迎通过微信赞赏支持项目的持续开发。感谢每一份支持。

<img alt="Tom 的微信赞赏码" width="360" src="docs/assets/donation-wechat.jpg" />

---

## English

GestureSign V2 is a Windows 11 focused rebuild of the classic open-source project [TransposonY/GestureSign](https://github.com/TransposonY/GestureSign).

The original GestureSign has not been actively maintained for a long time. On newer Windows systems, users may run into sticky modifier keys, dated UI behavior, DPI issues, and inconsistent gesture capture. GestureSign V2 keeps the original gesture workflow while improving the Windows 11 experience and moving the configuration interface to a modern WinUI 3 design.

## Features

- Rebuilt WinUI 3 interface with Windows 11 rounded corners, Mica styling, and light / dark theme support.
- Touchpad, touchscreen, and mouse gestures with gesture trails and thumbnail previews.
- New Quick Actions page with an optional on-demand Kando component and dedicated hotkey triggers.
- New Edge Interaction page for touchpad and touchscreen edge taps and edge swipes.
- Edge gestures can also be added to regular app groups; app-specific actions take priority and fall back to global actions when no executable app action is found.
- Local intent learning and experimental AI veto with manual corrections and visible inference backend/fallback status.
- Per-app actions with matching by executable, window class, title, and groups.
- Common commands such as hotkeys, browser actions, window actions, media controls, system operations, file launching, volume, brightness, and command execution. New actions can include their initial command directly from the add-action dialog.
- Ignore list support for excluding specific apps, windows, or matching rules.
- Options to prefer Windows touchpad gestures or built-in browser gestures, with fullscreen exclusions.
- Optional OneDrive sync stores configuration under `OneDrive\Apps\GestureSign V2` and lets OneDrive handle cross-device synchronization.
- Tray icon, tray menu, single-instance startup, readable gesture logs, and one-click pause/resume from the tray.
- 90 Windows language and regional variants, with right-to-left layout for Arabic, Persian, Hebrew, Urdu, and Uyghur.
- Improved UI and input behavior for high-DPI and high-refresh-rate displays.

## Download

### Microsoft Store (recommended)

<a href="https://apps.microsoft.com/store/detail/9P2WKMHF43PN?cid=DevShareMCLPCB"><img alt="Get it from Microsoft Store" width="240px" src="https://get.microsoft.com/images/en-us%20dark.svg" /></a>

### WinGet

GestureSign V2 is available from Windows Package Manager. Install it with winget:

```powershell
winget install --id Tomclanc.GestureSignV2 --source winget
```

You can also get the latest portable build from [Releases](https://github.com/Tomclanc/GestureSignv2/releases/tag/v18.2.9).

The current GitHub release is **18.2.9**. Microsoft Store and WinGet availability may differ; use the GitHub assets below for this version.

Current version:

- [GestureSign-V2-18.2.9-x64.msi](https://github.com/Tomclanc/GestureSignv2/releases/download/v18.2.9/GestureSign-V2-18.2.9-x64.msi)
- [GestureSign-V2-18.2.9-x64-portable.zip](https://github.com/Tomclanc/GestureSignv2/releases/download/v18.2.9/GestureSign-V2-18.2.9-x64-portable.zip)

18.2.9 requires .NET 10 Desktop Runtime and Windows App SDK Runtime. NPU use additionally requires compatible hardware, drivers and an official Windows ML execution provider.

### What's new

#### 18.2.9

- Integrates local intent learning and experimental AI veto from Developer Preview 0.4 to reduce accidental two-finger Smart Close actions. Background learning and AI veto have independent controls.
- Adds bulk sample labeling, manual corrections, list/grid/tile views, and optional grouped AI-veto notifications.
- Adds AMD NPU integration: the NPU performs linear computation; the CPU handles normalization, Sigmoid and numerical verification. The UI shows the actual backend and falls back to GPU / CPU if initialization or validation fails.
- Fixes NPU detection when installed AMD components are not ready, compilation failures caused by WindowsApps paths, and restricted working-directory writes.
- MSI and portable packages now include local AI inference components; a separate offline component ZIP is also available. Personal models and samples are not bundled. Prepare the official AMD runtime for the target PC using “Prepare NPU / GPU components”.
- Release validation on Ryzen AI Z2 Extreme covered 1,559 inputs, with a maximum final-score difference of approximately 0.000000894 versus CPU ONNX. The final score is CPU-verified: this is hybrid NPU + CPU execution, not a claim of pure NPU inference or improved performance. Other NPU models have not completed hardware validation for this release.

AI veto remains experimental and may block intended gestures. Training uses the CPU; samples and models stay local. Community sample sharing is voluntary through the [submission issue](https://github.com/Tomclanc/GestureSignv2/issues/5).

### Previous releases

Earlier releases added a 12-combination TipTap selector and visual previews in action editors, the native Windows 11 brightness flyout, multi-finger TipTap in four directions, standalone Win key selection, continuous edge volume and brightness adjustment, scrolling mappings on all four edges, touchpad edge pointer locking, and back navigation. They also improved activation without clicking, selection of the window under the pointer, Smart Close, desktop and fullscreen filtering, and live action hints, alongside the WinUI 3 interface, 90 language and regional variants, RTL layout, optional Kando integration and upgrade migration, and fixes for input, gesture trails, touch interactions, and application launching. See [GitHub Releases](https://github.com/Tomclanc/GestureSignv2/releases) for version-by-version details.

## Installation

Recommended:

```powershell
winget install --id Tomclanc.GestureSignV2 --source winget
```

Manual installation:

1. Download the MSI or MSIX installer.
2. Double-click the installer and follow the setup prompts.
3. Open `GestureSign V2` from the desktop shortcut or Start menu.
4. Go to the Actions page, enable gesture recognition, and add apps, gestures, and commands as needed.

Configuration files are stored in:

```text
%AppData%\GestureSign V2
```

When OneDrive sync is enabled, configuration is stored in:

```text
%UserProfile%\OneDrive\Apps\GestureSign V2
```

Log files are stored in:

```text
%LocalAppData%\GestureSign V2
```

## Quick Start

1. Open the Actions page and make sure gesture recognition is enabled.
2. Select Global Actions or an app group on the left.
3. Click New Action and record or draw a gesture pattern.
4. Click Set Command and bind the gesture to a hotkey, browser action, window action, or system command.
5. Return to the desktop or target app and trigger the gesture.

If an app already has system-level or built-in gestures, such as Windows 11 touchpad gestures or Microsoft Edge mouse gestures, you can enable the related preference options on the Options page.

## Pages

- Actions: Manage global actions, app actions, groups, gestures, and commands.
- Ignore: Exclude apps, windows, or matching rules from gesture recognition.
- Gestures: View, import, export, retrain, and organize the gesture library.
- Quick Actions: Select Kando menus, sync hotkeys, open Kando settings, or test the radial menu.
- Edge Interaction: Configure taps and swipes along all four edges of the touchpad and touchscreen, plus touchpad TipTap actions: hold one, two, or three fingers and tap with another finger to the left, right, above, or below the held finger group. Each of the 12 combinations can have its own action, and repeated taps are supported.
- Options: Adjust trail color, width, opacity, input devices, fullscreen exclusions, and startup behavior; manage local intent learning, AI veto, sample corrections, and inference components.
- About: View the version, project links, logs, and maintenance information.

## Compatibility

- Recommended OS: Windows 11 x64. The Microsoft Store package also includes x64 and ARM64 variants.
- Current packages: x64 MSI and x64 portable ZIP; previous Store packages include x64 / ARM64 variants; this GitHub release of 18.2.9 is x64. No x86 package is produced.
- Windows 10 may run some features, but Windows 11 is the primary target.

## Feedback

When reporting gesture, recording, saving, or UI issues, please include:

- Windows version and display scaling.
- Whether you are using mouse gestures or touchpad gestures.
- Target app name and whether it is fullscreen.
- Logs from the About page.
- Screenshots or reproduction steps.

## Credits

Thanks to [TransposonY/GestureSign](https://github.com/TransposonY/GestureSign), HighSign, MahApps.Metro, WGestures, and the projects this work builds on.

Quick Actions can download the [Kando](https://github.com/kando-menu/kando) radial-menu component on demand. Kando is not bundled with GestureSign by default and can be installed or removed separately in the app. Kando remains an independent MIT-licensed project, and the downloaded component retains its `LICENSE` and Chromium license files.

## Support the project

If GestureSign V2 is useful to you, you can support its continued development via WeChat Pay. Every contribution is appreciated.

<img alt="Tom's WeChat appreciation code" width="360" src="docs/assets/donation-wechat.jpg" />

---

## 日本語

GestureSign V2 は、クラシックなオープンソースプロジェクト [TransposonY/GestureSign](https://github.com/TransposonY/GestureSign) を Windows 11 向けに再構築したバージョンです。

元の GestureSign は長い間積極的にメンテナンスされていません。新しい Windows 環境では、修飾キーが押されたままになる、UI の挙動が古い、高 DPI 環境で表示が崩れる、ジェスチャー入力が安定しない、といった問題が起こることがあります。GestureSign V2 は従来のジェスチャーワークフローを保ちながら、Windows 11 での体験を改善し、設定画面をモダンな WinUI 3 デザインへ移行しています。

## 主な機能

- Windows 11 の角丸、Mica スタイル、ライト / ダークテーマに対応した WinUI 3 インターフェイス。
- タッチパッド、タッチスクリーン、マウスジェスチャー、ジェスチャー軌跡、ジェスチャーサムネイルプレビュー。
- Kando のオプションコンポーネントを必要なときにダウンロードできる Quick Actions ページと、専用ホットキーによる呼び出し。
- タッチパッドとタッチスクリーンのエッジタップ / エッジスワイプを設定できる Edge Interaction ページ。
- エッジジェスチャーは通常のアプリグループにも追加でき、アプリ別アクションを優先し、見つからない場合はグローバルアクションへフォールバックします。
- ローカル意図学習と実験的な AI 拒否、誤判定の手動修正、推論バックエンドとフォールバック状態の表示。
- 実行ファイル、ウィンドウクラス、タイトル、グループによるアプリ別アクション管理。
- ホットキー、ブラウザー操作、ウィンドウ操作、メディア制御、システム操作などの一般的なコマンド。新規アクション作成時に初期コマンドも同じダイアログで設定できます。
- 特定のアプリ、ウィンドウ、マッチングルールを除外できる無視リスト。
- Windows タッチパッドジェスチャーやブラウザー内蔵ジェスチャーを優先するオプションと、全画面除外設定。
- OneDrive 同期を有効にすると、設定を `OneDrive\Apps\GestureSign V2` に保存し、OneDrive でデバイス間同期できます。
- トレイアイコン、トレイメニュー、単一インスタンス起動、読みやすいジェスチャーログ、トレイからの一時停止 / 再開。
- 90 種類の Windows 言語／地域バリアント。アラビア語、ペルシア語、ヘブライ語、ウルドゥー語、ウイグル語では右から左のレイアウトに対応します。
- 高 DPI および高リフレッシュレート環境向けの UI と入力体験の改善。

## ダウンロード

### Microsoft Store（推奨）

<a href="https://apps.microsoft.com/store/detail/9P2WKMHF43PN?cid=DevShareMCLPCB"><img alt="Microsoft Store から入手" width="240px" src="https://get.microsoft.com/images/ja%20dark.svg" /></a>

### WinGet

GestureSign V2 は Windows Package Manager からインストールできます:

```powershell
winget install --id Tomclanc.GestureSignV2 --source winget
```

最新のポータブル版は [Releases](https://github.com/Tomclanc/GestureSignv2/releases/tag/v18.2.9) からも入手できます。

GitHub の現在のリリースは **18.2.9** です。Microsoft Store と WinGet では公開時期が異なる場合があるため、このバージョンは以下の GitHub 添付ファイルから入手してください。

現在のバージョン:

- [GestureSign-V2-18.2.9-x64.msi](https://github.com/Tomclanc/GestureSignv2/releases/download/v18.2.9/GestureSign-V2-18.2.9-x64.msi)
- [GestureSign-V2-18.2.9-x64-portable.zip](https://github.com/Tomclanc/GestureSignv2/releases/download/v18.2.9/GestureSign-V2-18.2.9-x64-portable.zip)

18.2.9 には .NET 10 Desktop Runtime と Windows App SDK Runtime が必要です。NPU の利用には対応ハードウェア、ドライバー、Windows ML 公式実行プロバイダーも必要です。

### 18.2.9 の更新内容

- 開発者プレビュー 0.4 のローカル意図学習と実験的な AI 拒否機能を統合し、タッチパッドの 2 本指スマートクローズの誤作動を抑制します。バックグラウンド学習と AI 拒否は個別に有効化できます。
- サンプルの一括ラベル付け、手動修正、リスト／グリッド／タイル表示、無効化できる集約型の AI 拒否通知に対応しました。
- AMD NPU に対応しました。NPU が線形計算を行い、CPU が正規化、Sigmoid、数値検証を担当します。実際のバックエンドを表示し、初期化や数値検証に失敗した場合は GPU / CPU に切り替えます。
- インストール済みの AMD コンポーネントが未準備の場合の NPU 検出、WindowsApps パスによるコンパイル失敗、作業ディレクトリへの書き込み制限に関する問題を修正しました。
- MSI とポータブル版にローカル AI 推論コンポーネントを同梱し、別途オフライン用 ZIP も提供します。個人の学習モデルやサンプルは含みません。AMD 公式ランタイムは対象 PC で「NPU / GPU コンポーネントを準備」から準備できます。
- 本リリースの Ryzen AI Z2 Extreme 検証では 1,559 組の入力をテストし、最終スコアの CPU ONNX との差は最大約 0.000000894 でした。最終的には CPU で再検証したスコアを使用します。純粋な NPU 推論や性能向上を意味するものではなく、他の NPU 機種は本リリースでの実機検証を完了していません。

AI 拒否は実験的な機能で、意図したジェスチャーを誤ってブロックする場合があります。学習は CPU で行い、サンプルとモデルはローカルに保存します。サンプルは任意で [コミュニティ投稿窓口](https://github.com/Tomclanc/GestureSignv2/issues/5) に共有できます。

### 過去のバージョン

これまでのバージョンでは、アクション編集画面の 12 通りの TipTap 選択とプレビュー、Windows 11 標準の明るさ表示、複数指・四方向の TipTap、Win キー単独送信、エッジ操作による音量・明るさの連続調整、四辺へのスクロール割り当て、タッチパッドのエッジ操作中のカーソル固定、戻るボタンを追加しました。また、クリックを伴わないウィンドウのアクティブ化、カーソル下の対象選択、Smart Close、デスクトップと全画面の判定、リアルタイムのアクションヒントを改善し、WinUI 3 UI、90 種類の言語・地域対応、RTL レイアウト、Kando のオプション連携とアップグレード移行を整備するとともに、入力、ジェスチャー軌跡、タッチ操作、アプリ起動の問題を修正しました。各バージョンの詳細は [GitHub Releases](https://github.com/Tomclanc/GestureSignv2/releases) をご覧ください。

## インストール

推奨:

```powershell
winget install --id Tomclanc.GestureSignV2 --source winget
```

手動インストール:

1. MSI または MSIX インストーラーをダウンロードします。
2. インストーラーをダブルクリックし、画面の案内に従ってセットアップを完了します。
3. デスクトップショートカットまたはスタートメニューから `GestureSign V2` を開きます。
4. Actions ページでジェスチャー認識を有効にし、必要に応じてアプリ、ジェスチャー、コマンドを追加します。

設定ファイルは次の場所に保存されます:

```text
%AppData%\GestureSign V2
```

OneDrive 同期を有効にした場合、設定ファイルは次の場所に保存されます:

```text
%UserProfile%\OneDrive\Apps\GestureSign V2
```

ログファイルは次の場所に保存されます:

```text
%LocalAppData%\GestureSign V2
```

## クイックスタート

1. Actions ページを開き、ジェスチャー認識が有効になっていることを確認します。
2. 左側で Global Actions またはアプリグループを選択します。
3. New Action をクリックし、ジェスチャーパターンを記録または描画します。
4. Set Command をクリックし、そのジェスチャーにホットキー、ブラウザー操作、ウィンドウ操作、またはシステムコマンドを割り当てます。
5. デスクトップまたは対象アプリに戻り、ジェスチャーを実行します。

アプリが Windows 11 のタッチパッドジェスチャーや Microsoft Edge のマウスジェスチャーなど、システムまたはアプリ内蔵のジェスチャーを持っている場合は、Options ページで関連する優先オプションを有効にできます。

## ページ

- Actions: グローバルアクション、アプリアクション、グループ、ジェスチャー、コマンドを管理します。
- Ignore: ジェスチャー認識から除外するアプリ、ウィンドウ、マッチングルールを設定します。
- Gestures: ジェスチャーライブラリの表示、インポート、エクスポート、再学習、整理を行います。
- Quick Actions: Kando メニューの選択、ホットキー同期、Kando 設定の起動、ラジアルメニューのテストを行います。
- Edge Interaction: タッチパッドとタッチスクリーンの四辺でのタップ／スワイプに加え、タッチパッドの TipTap アクションを設定します。1 本、2 本、または 3 本の指を置いたまま、別の指でその指のグループの左・右・上・下を軽くタップすると、合計 12 通りの操作にそれぞれアクションを割り当てられます。連続タップにも対応しています。
- Options: 軌跡の色、幅、透明度、入力デバイス、全画面除外、起動動作を調整し、ローカル意図学習、AI 拒否、サンプル修正、推論コンポーネントを管理します。
- About: バージョン、プロジェクトリンク、ログ、メンテナンス情報を確認します。

## 互換性

- 推奨 OS: Windows 11 x64。Microsoft Store パッケージには x64 と ARM64 の両方を含めています。
- 現在のパッケージ: x64 MSI、x64 ポータブル ZIP、従来の Store パッケージは x64 / ARM64 対応（18.2.9 の今回の GitHub リリースは x64）。x86 パッケージは生成しません。
- Windows 10 でも一部機能は動作する可能性がありますが、主な対象は Windows 11 です。

## ポータブルパッケージの検証

リリース前に、ポータブル展開先の必須ファイルと不要な診断ファイルを検証できます。PowerShell で次を実行してください。

```powershell
.\tools\Test-PortablePackage.ps1 -PackagePath .\publish\portable -MinimumFileCount 200
```

この検証では WinUI とバックエンドのエントリポイント、主要 DLL の存在を確認し、PDB / ダンプ / 診断ログ、および同梱された Kando 実行ファイル・ライブラリを拒否します。
MSI を生成せず、検証済みのポータブル Payload だけを作成する場合は、次を使用できます。

```powershell
.\installer\Build-GestureSignV2-KandoMsi.ps1 -PayloadOnly -PublishDir .\publish\portable
```

## ビルド警告の回帰防止

CI はビルドログ中の警告をファイル・行・警告コード単位で正規化し、`tools/warning-baseline.json` と比較します。既存の警告は直ちにビルドを止めませんが、新しい警告地点が追加されると CI が失敗します。ローカルでは次のように実行できます。

```powershell
dotnet build GestureSign.Daemon/GestureSign.Daemon.csproj -c Release *> daemon-build.log
dotnet build GestureSign.WinUI/GestureSign.WinUI.csproj -c Release *> winui-build.log
.\tools\Test-WarningBaseline.ps1 -LogPath daemon-build.log,winui-build.log
```

現在のローカル基線は 0 個の警告位置です。新しい警告地点が追加された場合は CI が失敗します。`-UpdateBaseline` は警告を意図的に修正した後にのみ使用してください。

## フィードバック

ジェスチャー、記録、保存、UI 表示に関する問題を報告する場合は、次の情報を含めてください。

- Windows のバージョンとディスプレイの拡大率。
- マウスジェスチャーまたはタッチパッドジェスチャーのどちらを使用しているか。
- 対象アプリ名と、全画面表示かどうか。
- About ページのログ。
- スクリーンショットまたは再現手順。

## クレジット

[TransposonY/GestureSign](https://github.com/TransposonY/GestureSign)、HighSign、MahApps.Metro、WGestures、および本プロジェクトの基礎となった各プロジェクトに感謝します。

Quick Actions では、[Kando](https://github.com/kando-menu/kando) のラジアルメニューをオプションコンポーネントとして必要なときにダウンロードできます。Kando は GestureSign に既定では同梱されず、アプリ内で個別にインストールまたは削除できます。Kando は MIT License の独立したオープンソースプロジェクトで、ダウンロードしたコンポーネントには `LICENSE` と Chromium 関連のライセンスファイルが保持されます。

## プロジェクトを支援

GestureSign V2 が役に立った場合は、WeChat Pay の赞赏码から継続開発を支援できます。ご支援ありがとうございます。

<img alt="Tom の WeChat 赞赏码" width="360" src="docs/assets/donation-wechat.jpg" />
