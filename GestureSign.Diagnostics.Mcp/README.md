# GestureSign V2 MCP 诊断组件 0.1.0

这是可选的本地只读诊断组件。需要 Windows x64 或 ARM64 和对应架构的 .NET 10 Runtime。更新主程序与 0.7.7 圆环组件后，在本机复现故障，再调用 `get_gesturesign_diagnostics`。这个程序通过标准输入输出和客户端通信，由客户端按需启动。GestureSign 本体不需要 AI 账号或 API 密钥。

## Codex

解压后，把下面的路径换成实际路径，在终端登记：

```powershell
codex mcp add gesturesign -- "C:\Tools\GestureSign MCP\GestureSign.Diagnostics.Mcp.exe"
```

也可在 MCP 设置中添加 STDIO 服务器，将可执行文件填入命令。

## Claude

在 Claude Desktop 本地 MCP 配置文件 `claude_desktop_config.json` 的 `mcpServers` 中添加以下条目。保留已有条目，修改路径后重启客户端。也可以在 Claude Code 注册同一个 STDIO 入口。

```json
{
  "mcpServers": {
    "gesturesign": {
      "command": "C:\\Tools\\GestureSign MCP\\GestureSign.Diagnostics.Mcp.exe",
      "args": []
    }
  }
}
```

便携版默认读取 `%LOCALAPPDATA%\GestureSign V2`。若使用商店版，可在 PowerShell 查询应用的包族名称：

```powershell
Get-AppxPackage TomClancy.GestureSignV2 | Select-Object -ExpandProperty PackageFamilyName
```

然后给 MCP 进程附加两个参数，`--data-root` 和 `%LOCALAPPDATA%\Packages\<包族名称>\LocalState\GestureSign V2` 的实际完整路径。把这两个值作为独立的参数传入，路径中的环境变量需要先展开。

## 诊断内容

- 圆环。显示绑定数、注册数、冲突数、最近一次触发阶段、渲染器状态和呈现耗时。注册数按去重后的快捷键计，模拟触发带 `externalTrigger` 标记。
- 手势。显示最近完成的普通模式识别、设备、接触点数、初始和最终候选，以及配置中匹配的动作名称。匹配动作并不代表动作已经执行成功。
- 自启和托盘。显示普通启动快捷方式是否存在，管理员计划任务上次检查状态、后台进程状态和程序提交给通知区的可见设置。实际图标是否被 Windows 折叠需在桌面确认。打开「选项」可刷新管理员启动状态。

工具只提供有限的状态快照。报告可能包含用户命名的手势和动作。把报告交给外部 AI 前请确认这些名称可以分享。也可以在「关于 → 导出诊断包」勾选 `JSON · MCP`，预览后把报告交给网页端 AI。
