# 18.2.9 可选 AI 组件

18.2.9 重打包版的 MSI 和便携版不再内置本组件。请在选项中下载或导入匹配的 AI ZIP。普通手势无需安装 AI。组件可安装到用户目录或可写的程序目录，个人样本和模型仍保存在用户数据目录。

The repackaged 18.2.9 MSI and portable app exclude this optional AI engine. Download or import the matching ZIP from Options. Ordinary gestures work without AI.

# GestureSign V2 · 开发者预览 0.4

基于 18.2.8 的实验性预览，重点改进本地意图学习与双指智能关闭防误触。此 Pre-release 不替代稳定版。

## 下载
- Windows x64 便携版：`GestureSign-DeveloperPreview-0.4-win-x64-Portable.zip`。完整解压，退出旧版后台后运行 `GestureSign.WinUI.exe`。
- AI 为可选 DLC：在选项中下载或导入本发布对应架构的 `GestureSign-IntentDlc-0.4.0-win-*.zip`。未安装 AI 仍可使用普通手势。旧 0.3 用户需要更新主程序和 DLC，卸载组件会保留训练数据。
- ARM64 DLC 供 ARM64 配套构建使用，本次主程序仅发布 x64；ARM64 只做构建验证。
- 不包含作者个人配置、训练样本或模型。与旧版本共用用户配置，试用前请自行备份。SHA256SUMS.txt 提供校验值。

## 本次改进
- 后台学习与 AI 否决拆成独立开关，可同时开启；按钮高亮反映实际状态。隐藏旧的验证门槛拦截入口。
- 实验性 AI 否决在动作执行前判断双指智能关闭，结合近期滚动上下文、模板转向证据和本地模型；可人工纠正误拦截。仅用户确认标签参与训练。
- 修复同步等待推理时异步回调依赖输入线程造成的死锁。
- AI 否决通知标题为 GestureSign V2；通知可关闭，最多每 30 秒一次并合并次数，点击进入样本纠正区域。修复托盘窗口句柄未创建导致通知失败。
- 样本支持多选、批量标注、列表/网格/磁贴视图，查看时保持列表位置，新样本手动刷新。明确区分零分、未评分、超时和推理失败。
- 显示每类有效样本与独立采样次数、还差多少；训练以文字显示进度和结果。
- 显示本机推理设备及预计 NPU → GPU → CPU 顺序，与实际后端分开展示。
- 便携版/MSI 可选择将 AI DLC 安装在用户目录或程序目录；程序目录可能需要管理员权限。已安装组件需卸载后更换位置，训练数据不随组件移动。商店包固定使用用户目录，此预览未验证商店安装流程。

## 已知限制
- AI 否决可能误拦截，仅针对触控板双指智能关闭，不会恢复已被捕获流程吞掉的滚动。训练和标注不会立即重算历史评分。
- Intel NPU 已在本地预览验证；重启后可能需再次点击“准备 NPU / GPU 组件”才能注册并使用 NPU。
- Ryzen AI Z2 Extreme 已能枚举 AMD VitisAI NPU，但当前模型在该设备上有节点要求 CPU 回退；本版禁止静默 CPU 回退，因此会选择 GPU。不要将“检测到 NPU”理解为已经在 NPU 上推理。
- ARM64/Qualcomm NPU 未实机验证。硬件组件可能需要联网下载，受驱动、系统版本及模型支持情况影响。
- 后台学习与观察评分本身不拦截动作；需要防误触时另外开启 AI 否决。个人训练在 CPU 上进行，样本和模型只保存在本机。

## English
Developer Preview 0.4 adds independent background-learning and AI-veto switches, grouped optional veto notifications, bulk sample correction and hardware diagnostics. It fixes an input-thread inference deadlock. The x64 portable app does not bundle AI: download/import the matching 0.4.0 DLC separately. ARM64 DLC is build-tested only. AMD Ryzen AI Z2 Extreme is detected, but this model currently falls back to GPU because strict NPU execution rejects CPU-assigned nodes. Intel NPU may require preparing providers again after restart. This experimental release is not a replacement for stable 18.2.8.

构建组件：installer/Build-IntentDlc.ps1。发布 catalog 必须使用 ZIP 实际长度与 SHA256，主程序需在更新 catalog 后构建。
