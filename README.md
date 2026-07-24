<p align="center">
  <img src="docs/assets/logo.png" width="96" alt="GestureSign V2 Logo">
</p>

<h1 align="center">GestureSign V2</h1>

<p align="center">
  为 Windows 11 重新打磨的触控板 / 鼠标手势工具。
</p>

<p align="center">
  <a href="https://github.com/Tomclanc/GestureSignv2/releases/tag/v16.4.55">
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
- 新增“快捷操作”页面，内置 Kando 圆环菜单，可用独立快捷键唤起漂亮的径向菜单。
- 新增“边缘交互”页面，可为触控板和触摸屏上 / 下 / 左 / 右边缘点击与边缘滑动单独绑定动作。
- 边缘手势可作为普通动作加入任意程序分组，当前应用动作优先，未命中时自动回退全局动作。
- 支持按程序、窗口类名、可执行文件、标题和分组管理动作。
- 支持快捷键、浏览器、窗口、媒体、系统操作等常用命令；新增动作时可直接配置要执行的命令，音量、亮度、打开文件、运行命令等常用命令提供专用编辑控件。
- 支持忽略列表，可按 exe、窗口类名、标题等规则排除指定程序。
- 支持优先使用系统触控板设置、Edge 自带手势，并可排除全屏场景。
- 支持将配置文件切换到 OneDrive `Apps\GestureSign V2` 目录，由 OneDrive 负责跨设备同步。
- 支持托盘图标、托盘菜单、单实例启动和更方便阅读的手势日志；托盘可一键暂停/恢复手势识别。
- 支持简体中文、英文、繁体中文（台湾）、日语、韩语界面语言。
- 针对高 DPI、高刷新率屏幕做了界面和输入体验优化。

## 下载

### Microsoft Store（推荐）

<a href="https://apps.microsoft.com/store/detail/9P2WKMHF43PN?cid=DevShareMCLPCB"><img alt="从 Microsoft Store 获取" width="240px" src="https://get.microsoft.com/images/zh-cn%20dark.svg" /></a>

### WinGet

GestureSign V2 已发布到 Windows Package Manager，可以直接通过 winget 安装：

```powershell
winget install --id Tomclanc.GestureSignV2 --source winget
```

也可以前往 [Releases](https://github.com/Tomclanc/GestureSignv2/releases/tag/v16.4.55) 下载最新版安装包。

当前版本：

- [GestureSign-V2-16.4.55-x64.msi](https://github.com/Tomclanc/GestureSignv2/releases/download/v16.4.55/GestureSign-V2-16.4.55-x64.msi)
- [GestureSign-V2-16.4.55-portable-x64.zip](https://github.com/Tomclanc/GestureSignv2/releases/download/v16.4.55/GestureSign-V2-16.4.55-portable-x64.zip)
- [GestureSign-V2-16.4.55.0-x64-store.msix](https://github.com/Tomclanc/GestureSignv2/releases/download/v16.4.55/GestureSign-V2-16.4.55.0-x64-store.msix)

## 更新内容

### 16.4.55

- “显示触发的手势操作”现在会随当前轨迹动态更新；继续绘制导致手势不再匹配动作时，会立即清除之前显示的动作名称。
- 清除动作名称时保留仍在绘制的手势轨迹；轨迹显示关闭时则隐藏提示层，避免残留旧提示。

### 16.4.54

- “快捷操作”启用时，GestureSign V2 随 Windows 启动后会自动拉起内置 Kando；关闭该选项时不启动 Kando，并避免重复创建进程。
- 为 WinUI 主窗口补充原生窗口类大小图标，改善任务管理器等传统 Win32 界面对主程序图标的识别。
- 为应用包 `StoreLogo` 补齐 100%–400% DPI 缩放资源，修复任务管理器最外层 GestureSign V2 应用分组显示通用窗口图标的问题。

### 16.4.51

- 修复以管理员身份运行时无法正确创建开机启动计划任务、重启后没有自动运行的问题。
- “Windows 启动时运行”现在按启用时的权限保存启动方式：管理员运行时以最高权限启动，普通运行时保持普通启动；两种方式会自动互斥切换。
- 微软商店安装版新增后台执行别名，使计划任务无需依赖会随更新变化的 `WindowsApps` 版本目录；同时允许笔记本使用电池时启动。
- 启动选项会读取实际快捷方式和计划任务状态，失败时显示 `schtasks` 的具体错误信息。

### 16.4.47

- 将缺少明确抬手数据时的触控板释放补判从 450 毫秒缩短到 120 毫秒，让边缘手势在手指释放后更快执行。
- 修复一批 HID 报告中后续数据包仍读取首包按钮状态的问题，提升触控板多包输入的稳定性。
- MSIX 打包时显式包含 Backend 与 Kando，防止商店包缺少手势后台或径向菜单文件。

### 16.4.46

- 补充 Windows 11 浅色主题专用的透明小图标资源，避免任务栏右键菜单等位置为 GestureSign V2 图标自动添加蓝色底板。

### 16.4.45

- 在新增、编辑动作和边缘命令时，为“智能关闭”显示使用说明：部分窗口需要先切换到前台；关闭高权限程序时，以管理员身份运行 GestureSign V2 效果更好。

### 16.4.44

- 修复 Clash Party 上绘制手势时，目标窗口被 Windows 错报为 `TopLevelWindowForOverflowXamlIsland`，导致智能关闭连续命中资源管理器、需要多次尝试的问题；仅在手势起点位于唯一可见的 Clash Party 主窗口内时纠偏。
- 管理员运行后台时直接向 Clash Party 发送 Alt+F4，不再额外启动一次性 UIAccess 助手。
- 移除 16.4.42–16.4.43 的常驻 UIAccess 输入通道与自动重启逻辑，恢复为不常驻辅助进程的稳定路径；同时保留配置未变化时不重装鼠标钩子的修复。

### 16.4.43

- 为常驻 UIAccess 输入助手增加监督与自动重启：鼠标管道断开或助手异常退出后，主进程会记录退出状态并重新创建助手。
- 修复助手静默退出后 Clash Party 重新丢失移动/抬起事件、快捷键通道回退到普通权限发送的问题。

### 16.4.42

- 借鉴 FastGestures 的 UIAccess + `SendInput` 路径，新增常驻 UIAccess 快捷键通道，将目标窗口和按键组合通过本地命名管道交给已签名助手执行。
- 普通“发送快捷键”、内置快捷键和“智能关闭”优先使用常驻助手，避免 Clash Party 等受保护窗口拒绝普通权限进程注入的按键。
- 常驻通道不可用时自动退回原有快捷键发送及一次性 Alt+F4 助手，不影响启动初期或非打包环境。

### 16.4.41

- 被动 UIAccess 鼠标中继扩展到本地和远程桌面会话，让 Clash Party 在电脑前直接操作时也能补齐移动与抬起事件。
- 配置内容未改变时不再重复卸载和安装鼠标钩子，避免设置界面的周期刷新打断正在绘制的手势。
- 保持父进程监视计时器存活，确保主进程退出后中继助手同步退出，不留下后台残留进程。

### 16.4.40

- 通过 Windows Shell 启动已签名的 UIAccess 被动中继，修复普通启动方式被系统拒绝的问题。

### 16.4.39

- 新增仅在远程桌面会话启用的被动 UIAccess 鼠标中继，用于补齐 Clash Party 等受保护窗口上缺失的右键手势轨迹。
- 中继只观察并转发输入，永不拦截鼠标消息；主 GestureSign 进程继续保持普通权限，避免影响 RDP 输入。
- 普通低级鼠标钩子仍是主通道，中继事件仅在主通道没有开始捕捉时接管，避免普通程序重复触发。

### 16.4.38

- 修复 RDP 注入的右键不更新 `GetAsyncKeyState`，导致鼠标轮询在按下后立即误判松开、远程右键轨迹只剩 1–2 个点的问题。远程会话恢复使用原有低级钩子路径，轮询补点仅用于本机控制台会话。
- 本地轮询只有先观察到真实按下状态后才允许补充松开事件，避免对合成输入制造额外右键点击。

### 16.4.37

- 撤销主后台 UIAccess，避免远程桌面会话中的右键输入被高权限全局钩子长期占用。
- 普通权限鼠标钩子在高权限窗口上超过 35 毫秒没有移动事件时，改用只读的鼠标位置与按键状态轮询补齐轨迹和松开事件；不阻断或重放 RDP 输入，也不启用触控板指针捕捉层。

### 16.4.36

- 修复普通权限鼠标钩子在轨迹进入高权限 Clash Party 窗口后收不到移动点、手势被当成普通右键的问题。MSIX 后台使用已签名的 UIAccess 清单读取鼠标轨迹，同时继续采用普通 Release 编译并保持 UIAccess 指针捕捉层关闭，避免再次影响触控板录制与识别。

### 16.4.35

- 修复高 DPI 屏幕上鼠标手势使用物理像素坐标、而普通 Win32 窗口矩形被 DPI 虚拟化，导致 Clash Party 等透明/高权限窗口命中范围偏移的问题。桌面壳层回退现在使用 DWM 扩展边框的物理像素坐标。
- 智能关闭命中桌面时，如果系统中恰好只有一个可见且标题、窗口类、进程名均匹配的 Clash Party 主窗口，则只对本次智能关闭恢复该目标；不会影响其他手势，也不会选中隐藏的托盘窗口。

### 16.4.34

- 针对 Clash Party 不触发正常前台切换、而是在手势开始时直接隐藏 Electron 主窗口的情况，新增窗口隐藏事件监听；仅缓存标题和窗口类同时匹配的 Clash Party 主窗口，避免误用托盘、渲染器或输入法子窗口。

### 16.4.33

- 修复 Clash Party 在开始鼠标手势时立即隐藏 Electron 主窗口，导致目标退化为桌面、智能关闭失效的问题。后台现在会观察前台窗口切换，并在 Clash Party 切换到桌面壳层的瞬间保留原窗口句柄供本次手势使用。

### 16.4.32

- 统一修复 Clash Party 等透明或高权限窗口的命中：当 Windows 返回桌面壳层时，后台手势与 WinUI 窗口拾取都会按 Z 顺序寻找鼠标坐标下真正可见的顶层窗口。
- 运行中程序列表改为逐进程容错读取；无法取得完整路径时回退为 `进程名.exe`，并取消数量截断。
- 新增程序分组使用 exe 匹配时不再允许保存空的匹配内容，避免生成永远无法命中的程序分组。

### 16.4.31

- Clash Party 的鼠标绘制手势在首次确认前台窗口后保留 3 秒稳定目标，避免右键短捕捉让桌面夺走前台、导致随后完成的智能关闭手势再次落到 `Progman`。

### 16.4.30

- 修复在 Clash Party 前台使用鼠标绘制手势时，Electron 窗口被命中为桌面 `Progman/WorkerW`、导致智能关闭被安全跳过的问题；该回退仅对前台 Clash Party 生效，不改变普通桌面手势的目标选择。

### 16.4.29

- 触控板手势改为锁定手势开始时的前台窗口，不再因鼠标停在任务栏、托盘或溢出面板上而把“智能关闭”发送给 Windows Shell。
- 仅为绑定“智能关闭”的 L 形手势增加有限识别容差；普通手势仍使用原阈值，并继续校验横纵转角比例，降低双指滚动误触风险。
- “智能关闭”不再重新激活可能过期的捕捉窗口，改善 Clash Party 和 Windows Terminal 的触发稳定性。

### 16.4.28

- 恢复 16.4.21 的普通 Release 手势捕捉后台；仅在关闭 Clash Party 时启动一次性、已签名的 UIAccess 快捷键助手，避免 UIAccess 捕捉路径影响触控板录制和识别。
- 移除 VS Code 智能关闭中的 UI Automation 遍历，改用轻量标题回退判断，避免外部窗口检查拖住动作线程。
- 手势在开始阶段被取消时记录具体原因，包括忽略程序、全屏排除和触点数量限制。
- 重新生成 MSIX 方形及商店图标资源，修复任务管理器应用分组图标缺失。

### 16.4.27

- 修复一次智能关闭卡在目标窗口重新定位或标题读取后，后续所有手势动作都排队失效的问题。智能关闭现在直接使用手势开始时捕获的稳定窗口句柄，只在 VS Code 判定需要时读取标题。

### 16.4.26

- 修复 UIAccess 版本启动后双指手势偶发被捕捉层提前取消、或绘制的 L 手势被误识别为其他手势的问题。
- 后台改为使用普通 Release 的稳定触控捕捉路径，同时在打包阶段嵌入并签名 UIAccess 清单；继续支持向高权限 Clash Party 发送 `Alt+F4`，且不再启用会干扰触控板识别的指针输入目标窗口。

### 16.4.25

- Clash Party 的“智能关闭”固定使用 `Alt+F4`，撤销与其 Electron 关闭逻辑不兼容的窗口消息方案；配合签名 UIAccess 后台，可向高权限 Clash Party 窗口发送真实关闭快捷键。

### 16.4.24

- MSIX 后台改用项目原有的 UIAccess 发布配置并进行可执行文件签名，使手势命令可以控制 Clash Party 等以高权限运行的窗口。

### 16.4.22

- 修复 VS Code 只剩空编辑区时“智能关闭”仍发送 `Ctrl+W`、无法关闭窗口的问题：有编辑器标签时发送 `Ctrl+W`，没有编辑器标签时发送 `Ctrl+Shift+W`。
- VS Code 的标签判定只在执行“智能关闭”时进行一次；其他程序仍沿用进程名和窗口类的快速判定，不采用连续发送两个关闭快捷键的方式。

### 16.4.21

- 修复 MSIX 安装版内置 Kando 在任务栏错误显示 GestureSign 图标的问题；Kando 窗口现在使用独立的 `menu.kando.Kando` 身份和自身原生图标。
- 新增“智能关闭”命令：浏览器、资源管理器、微信等使用 `Ctrl+W`，Windows Terminal 使用 `Ctrl+Shift+W`，Excel、Windows 设置及其他程序使用 `Alt+F4`。

### 16.4.19

- 手势识别会优先匹配当前应用中实际绑定了动作的手势，改善便签、微信图片和微信小程序等窗口中相似手势难以触发的问题。
- 为应用优先匹配增加横纵形状比例校验，防止双指垂直滚动被误识别为“向右再向上”等 L 形浏览器手势。
- 应用内版本、MSI 构建版本和 MSIX 包版本统一更新为 `16.4.19`。

### 16.4.17

- 更新“关于”页面，维护者信息改为“风夏”，并显示完整版本号 `16.4.17`。
- 将版本说明统一为“WinUI3重构”，更新 QQ 交流群为 `1054687130`，反馈邮箱为 `z1021847549@outlook.com`。
- 项目页面补充 GestureSign V2 当前仓库、TransposonY 原始项目及 Kando 项目链接，并保留 highsign、MahApps.Metro、WGestures 致谢信息。

### 16.4.16

- 托盘菜单会跟随 GestureSign V2 的界面语言动态切换，托盘提示名称统一为 `GestureSign V2`；单击菜单“设置”或双击托盘图标都会进入新的 WinUI 3 设置界面。
- 移除旧版 WPF 控制面板及相关打包文件，动作页底部旧版“编辑入口”同步隐藏，避免误入已经停用的旧界面。
- 优化 ROG Xbox 等触屏设备上的托盘菜单操作：增大菜单宽度、字体和单项触控区域，消除菜单项之间无法点击的空隙，并修复右键事件重复显示菜单的问题。
- 修复 MSIX / 微软商店安装后后台可能因 `System.Memory`、`System.Resources.Extensions` 等依赖版本冲突而启动失败的问题，前端和后台依赖改为隔离部署。
- 完善 MSIX 商店身份、图标资源、语言资源和启动布局，移除不需要的调试文件，提升商店审核环境中的启动稳定性。
- 修复动作页新增、编辑、启用或停用动作后列表可能跳回顶部的问题，并优化程序分组快速切换和动作缩略图即时刷新。
- 优化微信图片等独立窗口中的手势触发稳定性：绘制过程中若已匹配到当前分组的可执行动作，松手瞬间漂移到无动作手势时会保守回退到最近的有效候选。
- 微信程序分组会自动包含 `WeChatAppEx.exe`，覆盖微信小程序、微信内 PDF/图片等独立窗口。
- 普通手势缩略图增加方向箭头，新建或录制的手势在动作列表中更容易判断绘制方向。
- 远程桌面窗口默认透传鼠标手势输入，修复本机 GestureSign 截获右键轨迹后，远端 Edge 等应用无法触发自身鼠标手势的问题；支持 `mstsc.exe`、`msrdc.exe`、`RdClient.Windows.exe`、`Windows365.exe` 和 `vmconnect.exe`。

### 16.3

- 优化动作页左侧程序分组的快速点击体验，程序行改为鼠标释放时立即切换，减少 WinUI 点击判定带来的延迟。
- 右侧动作列表改为可中断的分批渲染，快速切换分组时旧列表绘制会自动作废，只保留最后一次选择。
- 修复靠近左侧列表底部的程序分组点击时可能触发自动滚动，导致切换偶发变慢的问题。
- 修复在右侧列表新增或编辑动作后，保存时动作列表可能跳回顶部的问题。
- 修复编辑动作时通过触控板/触控录制新手势后，动作没有立即生效、缩略图不能及时更新的问题。
- 增强 Windows 资源管理器窗口识别，对资源管理器和桌面 Shell 窗口稳定兜底为 `explorer.exe`，减少必须手动拾取窗口后才生效的情况。

### 16.2

- 修复动作页左侧程序分组和右侧动作的启用/停用按钮状态不会即时刷新的问题。
- 修复点击启用/停用后，左右列表或页面滚动位置跳到顶部的问题。
- 修复新增多个同名或未命名手势动作后，编辑最后一个动作可能改到前一个手势图案的问题。
- 修复桌面等非浏览器窗口下，手势可能误命中浏览器分组的问题，强化空匹配和程序匹配规则。
- 动作页左右列表布局更紧凑，手势动作列表支持独立滚动，减少底部空白。
- 切换界面语言时，轨迹颜色预设名称会同步切换语言。
- MSI 打包流程会重新编译并复制最新 WinUI、后台服务和 Kando 文件，避免安装包带入旧界面文件。

### 8.2.16

- 修复“添加程序 / 捕捉窗口”对 Windows Terminal 等现代窗口识别不稳定的问题，优先使用低权限进程路径查询，并兼容 `WindowsTerminal.exe`、`wt.exe`、`OpenConsole.exe` 等终端进程名。
- 捕捉窗口时增加悬停确认：选中同一个目标窗口持续 3 秒会自动确认并返回 GestureSign V2，触控板操作时不再必须点击目标窗口。
- 优化捕捉结束后的窗口恢复逻辑，减少触控板点击后无法回到保存弹窗、退出捕捉时偶发闪退的问题。
- 修复普通手势缩略图被强制拉伸的问题，保留原有比例显示；触控板边缘动作继续使用完整触控板样式缩略图。
- MSI 和便携版恢复简洁正式命名，统一发布为 `GestureSign-V2-8.2.16-x64.msi` 与 `GestureSign-V2-8.2.16-portable-x64.zip`。

### 8.2.15

- 优化触控板边缘手势缩略图，改为显示完整触控板样式，动作列表更直观。
- 动作编辑弹窗中的内置触控板/触摸屏边缘触发方式会按当前界面语言显示，保存时仍保留兼容旧配置的内部手势名。
- 统一普通手势缩略图的视觉尺寸，让动作列表排版更整齐。
- 暂时隐藏动作页顶部导入、导出、备份、恢复快捷按钮，减少日常配置时的界面干扰。
- MSI 开始菜单只创建主程序快捷方式，不再额外创建卸载快捷方式。
- 修复安装后 Daemon 可能因 `System.Resources.Extensions` 版本绑定不匹配而启动报错的问题。

### 8.2.14

- 新增 OneDrive 配置同步选项，可将 GestureSign V2 配置切换到当前用户 OneDrive 的 `Apps\GestureSign V2` 目录。
- 新增动作时可在弹窗底部直接配置“要执行的命令”，保存动作时同步创建初始命令。
- “新动作”按钮会默认添加到当前选中的程序或分组，不再总是落到全局动作。
- 边缘手势动作支持程序/分组匹配：当前应用动作优先，找不到可执行动作时回退全局动作。
- 触控板边缘、触控屏边缘动作在动作列表中显示专用缩略图，不再显示英文原始手势名。
- 手势名称匹配改为忽略大小写，减少旧配置或手动输入导致的边缘动作不触发问题。
- 手势动作提示改为识别过程中实时显示匹配到的动作名称，松手后立即消失。
- MSI 继续内置 Kando，安装后释放到 `GestureSign V2\Kando\kando.exe`。

### 8.2.13

- 重构 WinUI 3 设置界面，完善 Windows 11 风格、Mica / 圆角托盘菜单和深色模式适配。
- 新增“边缘交互”页面，支持触控板与触摸屏四边点击和边缘滑动动作配置。
- 修复触摸屏边缘手势触发、轨迹显示、托盘菜单交互和命令执行相关问题。
- 适配 Windows 11 Xbox 大屏模式，进入 / 退出大屏模式时自动调整窗口控制按钮。
- 改进命令编辑体验：音量、亮度、打开文件、运行命令等命令提供专用编辑控件。
- 运行命令支持 CMD / PowerShell、管理员权限和显示窗口选项。
- 完善简体中文、英文、繁体中文（台湾）、日语、韩语多语言适配。
- 新增选项页“退出”操作，可退出相关进程；卸载时可选择清理相关残留文件。
- 同步发布 MSI 安装包和 x64 便携版。

### 8.1.9807

- 新增触控屏边缘操作，可为屏幕上 / 下 / 左 / 右边缘点击和边缘滑动单独绑定动作。
- 适配 Windows 11 Xbox 大屏模式，进入大屏模式时隐藏右上角窗口按钮，退出后自动恢复。
- “显示触发的手势操作”提示增加淡入淡出动画，出现和消失更自然。
- 优化提示和大屏模式状态刷新，减少切换时的突兀感。

### 8.1.9783

- 配置目录迁移到 `%AppData%\GestureSign V2`。
- 首次启动会从旧目录 `%AppData%\GestureSign` 自动复制现有配置，保留原有动作、选项和手势数据。
- 旧目录不会自动删除，方便回滚或手动备份。

### 8.1.9782

- 退出 GestureSign V2 后台时，会同步结束集成安装目录下的 Kando 进程。

### 8.1.9781

- 新增“快捷操作”页面，安装包内置 Kando，可直接从 GestureSign V2 选择 Kando 菜单并同步快捷键。
- 支持实时读取 `%AppData%\kando\menus.json` 中的菜单名称和快捷键，菜单列表可单击选择。
- “启用快捷操作”开关现在会在开启时拉起 Kando，关闭时结束集成的 Kando 进程；启动 GestureSign V2 时也会按配置自动拉起。
- 新增触发手势动作提示，可在任务栏上方显示刚触发的手势/动作名称。
- 托盘菜单“关闭手势识别”改为真正的总开关，关闭后图标变红，并可再次点击恢复。
- 修复托盘红色图标使用旧资源导致 `System.Resources.Extensions` 加载错误的问题。
- MSI 改为合并包，安装后会释放 Kando 到 `GestureSign V2\Kando\kando.exe`。

### 8.1.9760

- 修正安装包发布方式，WinUI 3 前端改为随 MSI 自带 .NET 运行时。
- 重新生成 MSI 文件清单，避免在未安装 .NET 8 Desktop Runtime 的电脑上启动失败。
- 更新关于页、程序集版本和安装包版本，方便覆盖安装识别新版。

### 8.1.9759

- 修复触控板多指轨迹的可视反馈错位问题，保留多根手指之间的相对位置。
- 优化触控板训练预览，左手点按、右手绘制等组合手势会按真实相对位置显示。
- 保持手势识别判断逻辑不变，仅调整屏幕轨迹显示和训练预览坐标。

### 8.1.9758

- 优化命令编辑弹窗，启动应用 / 运行命令不再要求手写 JSON。
- 新增已安装应用下拉列表，可直接选择桌面应用。
- 新增“浏览 EXE”，支持用户手动选择任意可执行文件。

### 8.1.9757

- 修复覆盖安装时 WinUI 前端 DLL 可能不被替换的问题。
- 为 WinUI 前端补充文件版本号，并增强安装器的进程关闭和强制覆盖逻辑。

### 8.1.9756

- 优化触控板边缘页面排版，去掉多余方向线条。
- 调整上 / 下边缘操作为横向排列，避免文字遮挡。

### 8.1.9755

- 新增触控板边缘功能，支持四边点击和边缘滑动触发动作。
- 新增“触控板边缘”导航页，可直接为每个边缘动作配置命令。
- 修复触控板边缘动作捕获顺序，避免单指边缘操作被全局手势过滤。

### 8.1.9754

- 优化托盘右键菜单退出逻辑，退出时会关闭相关 GestureSign V2 进程。
- 修复 WinUI 前端安装后双击无窗口、进程快速退出的问题。
- 安装包补充 WinUI 资源文件，提升覆盖安装稳定性。

### 8.1.9752

- 为日常触控板手势增加空闲释放兜底，减少触控板手势结束后鼠标右键手势偶发不触发的问题。
- 保持触控板录制原有释放补偿逻辑，同时扩展到普通触控板手势输入。

### 8.1.9751

- 修复触控板手势结束后，后续鼠标右键手势可能不再触发的问题。
- 在触控板释放事件后清理残留触摸状态，避免必须重启后台程序才能恢复鼠标手势。

### 8.1.9750

- 修复开启“优先使用 Windows 触控板系统手势”后触控板手势被整体禁用的问题。
- 保留触控板 raw input 注册，避免旧配置文件导致触控板手势、轨迹线失效。
- 说明：开启“Edge 优先使用自带鼠标手势”后，Edge 窗口内的右键手势会交给 Edge 自带手势处理。

### 8.1.9749

- 修复触控板、触控屏和鼠标右键手势的轨迹显示与触发稳定性。
- 修复子动作“启用 / 停用”后后台动作列表不立即生效的问题。
- 恢复“手势识别”总开关为动态开关，可直接控制后台识别服务启停。
- 修复“优先使用 Windows 触控板系统手势”选项不生效的问题。
- 优化触控板或触控录制入口描述，保留多指手势录制兼容逻辑。
- 更新 WinUI 3 设置界面版本号与下载链接。

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
- “边缘交互”：设置触控板和触摸屏四边点击与边缘滑动动作。
- “选项”：调整轨迹颜色、宽度、透明度、输入设备、全屏排除和启动项。
- “关于”：查看版本、项目链接、日志和维护信息。

## 兼容性

- 推荐系统：Windows 11 x64
- 安装包：MSI x64
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

“快捷操作”功能集成并随安装包分发了 [Kando](https://github.com/kando-menu/kando) 的圆环菜单交互。Kando 是独立的开源项目，遵循 MIT License；安装包中保留了 Kando 自带的 `LICENSE` 和 Chromium 相关许可证文件。

---

## English

GestureSign V2 is a Windows 11 focused rebuild of the classic open-source project [TransposonY/GestureSign](https://github.com/TransposonY/GestureSign).

The original GestureSign has not been actively maintained for a long time. On newer Windows systems, users may run into sticky modifier keys, dated UI behavior, DPI issues, and inconsistent gesture capture. GestureSign V2 keeps the original gesture workflow while improving the Windows 11 experience and moving the configuration interface to a modern WinUI 3 design.

## Features

- Rebuilt WinUI 3 interface with Windows 11 rounded corners, Mica styling, and light / dark theme support.
- Touchpad, touchscreen, and mouse gestures with gesture trails and thumbnail previews.
- New Quick Actions page with bundled Kando radial menus and a dedicated hotkey trigger.
- New Edge Interaction page for touchpad and touchscreen edge taps and edge swipes.
- Edge gestures can also be added to regular app groups; app-specific actions take priority and fall back to global actions when no executable app action is found.
- Per-app actions with matching by executable, window class, title, and groups.
- Common commands such as hotkeys, browser actions, window actions, media controls, system operations, file launching, volume, brightness, and command execution. New actions can include their initial command directly from the add-action dialog.
- Ignore list support for excluding specific apps, windows, or matching rules.
- Options to prefer Windows touchpad gestures or built-in browser gestures, with fullscreen exclusions.
- Optional OneDrive sync stores configuration under `OneDrive\Apps\GestureSign V2` and lets OneDrive handle cross-device synchronization.
- Tray icon, tray menu, single-instance startup, readable gesture logs, and one-click pause/resume from the tray.
- Simplified Chinese, English, Traditional Chinese (Taiwan), Japanese, and Korean UI languages.
- Improved UI and input behavior for high-DPI and high-refresh-rate displays.

## Download

### Microsoft Store (recommended)

<a href="https://apps.microsoft.com/store/detail/9P2WKMHF43PN?cid=DevShareMCLPCB"><img alt="Get it from Microsoft Store" width="240px" src="https://get.microsoft.com/images/en-us%20dark.svg" /></a>

### WinGet

GestureSign V2 is available from Windows Package Manager. Install it with winget:

```powershell
winget install --id Tomclanc.GestureSignV2 --source winget
```

You can also get the latest installer from [Releases](https://github.com/Tomclanc/GestureSignv2/releases/tag/v16.4.55).

Current version:

- [GestureSign-V2-16.4.55-x64.msi](https://github.com/Tomclanc/GestureSignv2/releases/download/v16.4.55/GestureSign-V2-16.4.55-x64.msi)
- [GestureSign-V2-16.4.55-portable-x64.zip](https://github.com/Tomclanc/GestureSignv2/releases/download/v16.4.55/GestureSign-V2-16.4.55-portable-x64.zip)
- [GestureSign-V2-16.4.55.0-x64-store.msix](https://github.com/Tomclanc/GestureSignv2/releases/download/v16.4.55/GestureSign-V2-16.4.55.0-x64-store.msix)

### What's new in 16.4.55

- The “Show triggered gesture action” label now follows the current live trace and clears immediately when additional drawing invalidates the action match.
- Clearing the action label preserves an enabled gesture trail; when trail drawing is disabled, the hint surface is hidden so stale text cannot remain.

### What's new in 16.4.54

- Automatically starts the bundled Kando after Windows sign-in when Quick Actions is enabled, skips it when disabled, and avoids duplicate Kando processes.
- Sets native large and small class icons on the WinUI main window for better identification in classic Win32 surfaces such as Task Manager.
- Adds 100%–400% DPI variants for the package `StoreLogo`, fixing the generic icon on the top-level GestureSign V2 application group in Task Manager.

### What's new in 16.4.51

- Fixed administrator startup task creation and the issue where GestureSign V2 did not start after signing in again.
- Startup now preserves the permission level used when enabling it: elevated sessions create a highest-privilege task, while normal sessions use a regular startup shortcut. The two modes switch cleanly without conflicting.
- Added a packaged daemon execution alias so Store installations no longer depend on versioned `WindowsApps` paths, and allowed startup while running on battery power.
- Startup toggles now reflect the actual shortcut and scheduled-task state, with detailed `schtasks` errors when registration fails.

### What's new in 16.4.47

- Reduced the Precision Touchpad idle-release fallback from 450 ms to 120 ms so edge actions run almost immediately after lifting a finger.
- Fixed multi-report HID parsing so each packet reads its own button state instead of reusing the first packet.
- Explicitly included Backend and Kando files in MSIX packages to prevent incomplete Store builds.

### What's new in 16.4.46

- Added transparent compact-icon resources for the Windows 11 light theme so the shell no longer adds a blue plate behind the GestureSign V2 icon in taskbar menus.

### What's new in 16.4.45

- Added Smart Close, which quickly selects Ctrl+W, Ctrl+Shift+W, or Alt+F4 for browsers, File Explorer, WeChat, Windows Terminal, VS Code, and standard desktop windows.
- Improved target recovery and gesture capture for Clash Party, protected windows, touchpads, mouse gestures, and Remote Desktop sessions without keeping a privileged input helper running.
- Added an in-app Smart Close note explaining foreground-window requirements and when running GestureSign V2 as administrator may help.
- Restored Kando's own taskbar identity and icon, and refreshed the Store asset set.

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
- Edge Interaction: Configure touchpad and touchscreen edge taps and edge swipes.
- Options: Adjust trail color, width, opacity, input devices, fullscreen exclusions, and startup behavior.
- About: View the version, project links, logs, and maintenance information.

## Compatibility

- Recommended OS: Windows 11 x64
- Installer: MSI x64
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

The Quick Actions feature integrates and redistributes the radial menu experience from [Kando](https://github.com/kando-menu/kando). Kando is an independent open-source project under the MIT License; its bundled `LICENSE` and Chromium license files are preserved in the installer.

---

## 日本語

GestureSign V2 は、クラシックなオープンソースプロジェクト [TransposonY/GestureSign](https://github.com/TransposonY/GestureSign) を Windows 11 向けに再構築したバージョンです。

元の GestureSign は長い間積極的にメンテナンスされていません。新しい Windows 環境では、修飾キーが押されたままになる、UI の挙動が古い、高 DPI 環境で表示が崩れる、ジェスチャー入力が安定しない、といった問題が起こることがあります。GestureSign V2 は従来のジェスチャーワークフローを保ちながら、Windows 11 での体験を改善し、設定画面をモダンな WinUI 3 デザインへ移行しています。

## 主な機能

- Windows 11 の角丸、Mica スタイル、ライト / ダークテーマに対応した WinUI 3 インターフェイス。
- タッチパッド、タッチスクリーン、マウスジェスチャー、ジェスチャー軌跡、ジェスチャーサムネイルプレビュー。
- Kando のラジアルメニューを同梱した Quick Actions ページと、専用ホットキーによる呼び出し。
- タッチパッドとタッチスクリーンのエッジタップ / エッジスワイプを設定できる Edge Interaction ページ。
- エッジジェスチャーは通常のアプリグループにも追加でき、アプリ別アクションを優先し、見つからない場合はグローバルアクションへフォールバックします。
- 実行ファイル、ウィンドウクラス、タイトル、グループによるアプリ別アクション管理。
- ホットキー、ブラウザー操作、ウィンドウ操作、メディア制御、システム操作などの一般的なコマンド。新規アクション作成時に初期コマンドも同じダイアログで設定できます。
- 特定のアプリ、ウィンドウ、マッチングルールを除外できる無視リスト。
- Windows タッチパッドジェスチャーやブラウザー内蔵ジェスチャーを優先するオプションと、全画面除外設定。
- OneDrive 同期を有効にすると、設定を `OneDrive\Apps\GestureSign V2` に保存し、OneDrive でデバイス間同期できます。
- トレイアイコン、トレイメニュー、単一インスタンス起動、読みやすいジェスチャーログ、トレイからの一時停止 / 再開。
- 簡体字中国語、英語、繁体字中国語（台湾）、日本語、韓国語の UI 言語。
- 高 DPI および高リフレッシュレート環境向けの UI と入力体験の改善。

## ダウンロード

### Microsoft Store（推奨）

<a href="https://apps.microsoft.com/store/detail/9P2WKMHF43PN?cid=DevShareMCLPCB"><img alt="Microsoft Store から入手" width="240px" src="https://get.microsoft.com/images/ja%20dark.svg" /></a>

### WinGet

GestureSign V2 は Windows Package Manager からインストールできます:

```powershell
winget install --id Tomclanc.GestureSignV2 --source winget
```

最新のインストーラーは [Releases](https://github.com/Tomclanc/GestureSignv2/releases/tag/v16.4.55) からも入手できます。

現在のバージョン:

- [GestureSign-V2-16.4.55-x64.msi](https://github.com/Tomclanc/GestureSignv2/releases/download/v16.4.55/GestureSign-V2-16.4.55-x64.msi)
- [GestureSign-V2-16.4.55-portable-x64.zip](https://github.com/Tomclanc/GestureSignv2/releases/download/v16.4.55/GestureSign-V2-16.4.55-portable-x64.zip)
- [GestureSign-V2-16.4.55.0-x64-store.msix](https://github.com/Tomclanc/GestureSignv2/releases/download/v16.4.55/GestureSign-V2-16.4.55.0-x64-store.msix)

### 16.4.55 の更新内容

- 「実行されるジェスチャー操作を表示」が現在の軌跡に合わせて動的に更新され、描画を追加して操作との一致が失われると以前の操作名をすぐ消去します。
- 操作名を消去しても有効なジェスチャー軌跡は保持し、軌跡表示が無効な場合はヒント面を非表示にして古い文字が残らないようにしました。

### 16.4.54 の更新内容

- 「クイック操作」が有効な場合、Windows サインイン後に同梱の Kando を自動起動します。無効な場合は起動せず、重複プロセスも作成しません。
- WinUI メインウィンドウへネイティブの大小クラスアイコンを設定し、タスクマネージャーなど従来の Win32 画面での識別を改善しました。
- パッケージの `StoreLogo` に 100%～400% の DPI リソースを追加し、タスクマネージャーの最上位 GestureSign V2 グループに汎用アイコンが表示される問題を修正しました。

### 16.4.51 の更新内容

- 管理者として実行した際のスタートアップタスク作成と、再ログイン後に GestureSign V2 が起動しない問題を修正しました。
- スタートアップを有効にしたときの権限を保持し、管理者実行時は最高権限タスク、通常実行時は通常のスタートアップショートカットを使用します。両方式は競合せず自動的に切り替わります。
- Store 版にバックグラウンド実行エイリアスを追加し、更新で変化する `WindowsApps` のバージョン別パスに依存しないようにしました。バッテリー駆動時の起動にも対応しました。
- 実際のショートカットとタスク状態を設定画面へ反映し、登録失敗時には `schtasks` の詳細を表示します。

### 16.4.47 の更新内容

- Precision Touchpad が明示的なリリース情報を送らない場合の待機時間を 450 ms から 120 ms に短縮し、指を離した直後にエッジ操作を実行できるようにしました。
- 複数 HID レポートの各パケットから正しいボタン状態を読み取るよう修正し、タッチパッド入力の安定性を改善しました。
- MSIX に Backend と Kando を明示的に含め、不完全な Store パッケージが生成されないようにしました。

### 16.4.46 の更新内容

- Windows 11 のライトテーマ向け透明小型アイコンを追加し、タスクバーのメニューなどで GestureSign V2 アイコンに青い背景が付かないようにしました。

### 16.4.45 の更新内容

- ブラウザー、エクスプローラー、WeChat、Windows Terminal、VS Code、通常のデスクトップウィンドウに応じて Ctrl+W、Ctrl+Shift+W、Alt+F4 を選ぶ「スマートクローズ」を追加しました。
- 常駐する高権限入力ヘルパーを使用せず、Clash Party、高権限ウィンドウ、タッチパッド、マウスジェスチャー、リモートデスクトップでの対象判定と入力取得を改善しました。
- スマートクローズに、前面ウィンドウへの切り替えと管理者としての実行に関する説明を追加しました。
- Kando 固有のタスクバー ID とアイコンを復元し、Microsoft Store 用アセットを更新しました。

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
- Edge Interaction: タッチパッドとタッチスクリーンのエッジタップ / エッジスワイプを設定します。
- Options: 軌跡の色、幅、透明度、入力デバイス、全画面除外、起動動作を調整します。
- About: バージョン、プロジェクトリンク、ログ、メンテナンス情報を確認します。

## 互換性

- 推奨 OS: Windows 11 x64
- インストーラー: MSI x64
- Windows 10 でも一部機能は動作する可能性がありますが、主な対象は Windows 11 です。

## フィードバック

ジェスチャー、記録、保存、UI 表示に関する問題を報告する場合は、次の情報を含めてください。

- Windows のバージョンとディスプレイの拡大率。
- マウスジェスチャーまたはタッチパッドジェスチャーのどちらを使用しているか。
- 対象アプリ名と、全画面表示かどうか。
- About ページのログ。
- スクリーンショットまたは再現手順。

## クレジット

[TransposonY/GestureSign](https://github.com/TransposonY/GestureSign)、HighSign、MahApps.Metro、WGestures、および本プロジェクトの基礎となった各プロジェクトに感謝します。

Quick Actions 機能では、[Kando](https://github.com/kando-menu/kando) のラジアルメニュー体験を統合し、インストーラーに同梱しています。Kando は MIT License の独立したオープンソースプロジェクトであり、同梱される `LICENSE` と Chromium 関連のライセンスファイルを保持しています。
