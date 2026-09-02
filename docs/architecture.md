# GestureSign V2 稳定化架构

输入捕获的生命周期由 `GestureSign.Foundation.Input.CaptureSession` 统一管理：

`Pending → Capturing/Previewing → Recognizing → Executing → Completed/Canceled`

`VisualCaptureLifecycle` 为每次轨迹分配 generation，结束或取消时使旧帧失效，防止异步绘制把上一条手势的提示重新显示。

配置升级和 Kando 数据迁移位于 Foundation 服务中，便于在不启动 WinUI 或真实输入设备的情况下测试。

WinUI 页面构建入口按 partial 文件分层：About、Gestures/Ignored 及 Settings（Options、Quick Actions、TouchPad）均通过独立 partial builder 接入；Options、Quick Actions 与 TouchPad 的构建主体现已迁移到对应 partial 文件，主窗口仅保留共享状态与事件协调。

Actions 页面构建与范围筛选、列表批量渲染也已迁移到 `MainWindow.PageBuilders.Actions.cs`。应用/动作编辑对话框和 Daemon 通知仍由主窗口协调，后续可进一步提取为独立编辑服务。

Daemon 命名管道通讯已封装到 `Services/DaemonBridgeService.cs`，主窗口只负责启动重试策略和生命周期协调；协议字节和连接超时集中在无 UI 服务中，便于独立测试。

主窗口命令分发已移至 `MainWindow.CommandRouting.cs`，备份、导入导出、动作/手势、Kando、日志和反馈等命令不再与窗口控件布局混在同一文件中。

`GestureSign.RegressionTests` 覆盖动作回退、完整轨迹提示、取消/残留帧、generation 竞态、连续运动、配置迁移和 Kando 迁移七组回归场景。
