# GestureSign 环形菜单 · 开发预览 0.7.12

Windows x64 或 ARM64。需要 .NET 10、Windows App Runtime 2 和 Microsoft Edge WebView2 Runtime。

配合本次提供的 GestureSign 主程序，打开「快捷操作」，选「下载组件」或「从 ZIP 安装」并选择对应架构的 0.7.12 组件包。配置项直接排列在页面上。主题可先用「预览菜单」查看，再用「保存菜单」应用到快捷键触发的菜单。

点击「菜单图标」或「菜单项图标」行中的图标，打开可搜索的图标库。内置 Kando SVG、Material Symbols Rounded、Simple Icons 单色和彩色版及 Emoji。Base64 选项可选择本地图片或从 EXE 提取图标。

1. 在 GestureSign V2 的「快捷操作」页安装对应架构组件，按齿轮按钮在同一页展开设置。
2. 在「菜单项」行选择类型并点击「+」添加，再选中菜单项设置图标、固定角度、快捷选择键和工作流。使用「上移」「下移」调整顺序。
3. 点击顶部的播放图标 **Preview menu**，测试弹出菜单。方向键移动选择，Enter 确认，Backspace 返回，Esc 取消。
4. 工具栏的导入按钮可读取 Kando 的 `%APPDATA%\kando\menus.json`。导入会添加菜单，先检查其中的命令再保存。保存按钮会写入本组件自己的配置。
5. 本组件由 GestureSign V2 托管，配置和预览入口均在「快捷操作」。
6. 选中已有菜单，在「快捷键」录制或输入组合键。0.8.21 主程序会单独保存这项修改，开启快捷操作后即可触发。新建菜单先点「保存菜单」。多个菜单可共用一组快捷键，在「菜单条件」里用前台应用、窗口标题和鼠标区域区分。选中子菜单或动作可录制快捷选择键，「模拟按键」动作按物理键位录制并保留左右修饰键。

组件使用 GestureSign V2 的主托盘入口。Windows 已占用的快捷键会在保存后报告。

弹出的交互圆环使用原版 Kando 前端、五套内置 CSS 主题及各主题配色，通过 .NET 10 宿主与 WebView2 DirectComposition 透明窗口运行。设置编辑器使用 WinUI 3。自定义 CSS 主题安装、所有工作流事件和部分 Windows 输入设备还没接完。

快捷操作使用 GestureSign 主程序的语言设置。

重新构建需要干净的 Kando 源码 `6df468a66695ac504afebfff2c581a42296511b4`。在 WebRenderer 目录运行 `pnpm install --ignore-scripts --frozen-lockfile`，为 Python 安装 `fonttools[woff]`，再运行 `node build.mjs "Kando源码目录"`，最后使用 installer/Build-PieMenuPreview.ps1 打包两种架构。

Kando 默认主题参考自 Simon Schneegans 的 [默认主题](https://github.com/kando-menu/kando/tree/6df468a66695ac504afebfff2c581a42296511b4/assets/menu-themes/default)，主题配置和样式采用 CC0-1.0。本组件独立维护。

0.7.9 加入主题预览卡、九项高级参数、独立菜单与设置备份、节点复制／删除／角度锁定／拖动排序，以及根菜单中心点击工作流。导入 Kando 菜单时保留中心工作流与快速选择键。