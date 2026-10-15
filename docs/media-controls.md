# 多媒体控制

在动作的“设置命令”中添加或编辑命令，可选择六个独立操作：媒体播放、媒体暂停、媒体快进、媒体快退、媒体下一首、媒体上一首。也可以把它们加入现有的多命令序列。

播放和暂停分别执行，不使用“播放/暂停”切换键；重复播放不会暂停，重复暂停不会开始播放。原有媒体快捷键配置保持可用。

优先控制 Windows 当前选中的系统媒体会话，不需要先把播放器切换到前台。多个播放器同时存在时，由 Windows 的当前媒体会话选择决定控制对象。

支持进度控制时，快进/快退移动 10 秒，限制在可跳转的时间范围内；计算播放位置时考虑上次进度更新后的经过时间和播放速度。播放器只支持自身的快进/快退模式时，使用播放器提供的对应操作。

播放器未公开系统媒体会话时，尝试向前台窗口发送标准 Windows 媒体命令。若当前媒体会话明确不支持所选操作，则记录原因，不改为控制其他播放器。已发出的请求超时后不会重复发送，避免跳过两首或连续跳转。

日志 `GestureSign.log` 记录操作、使用的后端、结果和不支持/超时原因。没有持续轮询；媒体会话管理器按需初始化并复用。

90 种语言资源已同步。商店清单增加 `globalMediaControl`，供系统媒体会话控制使用。

验证：

```powershell
dotnet run --project tests/GestureSign.MediaControlTests -c Release
dotnet run --project tests/GestureSign.MediaControlIntegrationTests -c Release -- --run-integration
```

第二项创建静音测试播放器，要求机器当前没有其他媒体会话，验证六个操作、重复播放/暂停和 10 秒跳转；不会操控用户正在播放的媒体。

API 说明：[Windows 系统媒体会话](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssession)、[Windows 媒体命令](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-appcommand)。
