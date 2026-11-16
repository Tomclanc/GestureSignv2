# GestureSign V2 18.4.6

- 修复连续滑动与固定手指手势串触发：每根手指都须朝同一方向移动足够距离。
- 恢复精确调整音量后的 Windows 原生提示条，保留设备重取、音量读回和一次重试，避免重复叠加音量变化。
- 补齐 86 个语言目录中的 3354 条英文残留，覆盖诊断导出、备份导入、动作编辑和桌面边缘操作。
- 地区回退保留显式文字脚本，补充 RTL、语言回退和实际译文回归检查。
- 打包时同时核对外部语言目录、AI 资源及更新助手内嵌目录，拒绝复用构建失败后的旧更新助手。

Downloads: x64 / ARM64 MSI and portable ZIP. Shared .NET Desktop Runtime 10 and Windows App Runtime are required; optional Kando/AI components are not bundled.

The gesture and volume fixes passed regression checks and were confirmed on the reporting device. All 90 locale catalogs pass resource, placeholder and lookup checks. Translations are machine-assisted with selected revisions; this is not native-speaker certification or visual testing of every screen in every language.

## English

- Require coherent movement from every selected finger before firing continuous actions, preventing anchored one-finger gestures from also triggering volume commands.
- Show the native volume flyout after exact percentage-point changes, with endpoint reacquisition, readback and a single retry that retains the absolute target.
- Replace 3,354 English-valued entries across 86 UI catalogs and preserve explicit scripts during regional fallback.
- Validate external UI, embedded intent and embedded updater catalogs in release payloads, and fail packaging if the updater cannot be rebuilt.

Validation reports and SHA256SUMS.txt are attached to this release. WinGet availability follows its repository review and source synchronization.
