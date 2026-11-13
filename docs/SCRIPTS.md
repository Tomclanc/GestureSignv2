# 用脚本扩展手势动作

新增或编辑动作，在命令中选择“运行命令”，点击“选择脚本”。支持 `.ps1`、`.cmd`、`.bat`、`.py`、`.ahk`；选择后会填入 PowerShell 调用命令，并将脚本所在文件夹设为工作目录。命令仍然可以编辑，以添加参数或指定解释器完整路径。

Python 需要 `python` 可执行文件在 PATH 中，或手动改为 Python 的完整路径。AutoHotkey 脚本通过 Windows 文件关联启动，需要安装与脚本语法匹配的 AutoHotkey 并配置 `.ahk` 文件关联。

例如，创建自己的 `C:\Scripts\demo.ps1`：

```powershell
param([string]$Message = 'GestureSign')
Add-Content -LiteralPath (Join-Path $env:TEMP 'GestureSign-script-demo.txt') -Value $Message
```

选择脚本后，可以把最后一行改成：

```powershell
& 'C:\Scripts\demo.ps1' -Message '来自手势'
```

手势触发时运行已有脚本，不会自动修改全局执行策略或安装解释器。命令中的“显示窗口”适用于排查输出；“管理员权限”会使用系统提权流程。需要常驻的脚本应自行处理单实例，避免每次手势都启动一个副本。

# Script extensions

Choose **Run command → Choose script** in the action editor. The generated PowerShell command invokes the selected script with its containing folder as the working directory. Edit the command to add arguments or specify a full interpreter path.

Supported types: PowerShell (`.ps1`), CMD/BAT, Python (`.py`), AutoHotkey (`.ahk`). Python uses `python` from PATH. AutoHotkey uses the Windows `.ahk` association; install the interpreter version appropriate for your script. Interpreters are not bundled or installed automatically. Resident scripts should handle their own single-instance behavior.

# 手势忽略 / Gesture exclusions

“忽略”页保留原有的程序忽略列表，并增加“手势忽略”。选择一个手势，可以应用于所有程序，或按 exe、窗口标题、窗口类限定范围。规则可编辑、启停、删除，并包含在配置导入/导出中。

手势忽略只阻止该手势对应的动作执行，其他手势仍可用。绘制手势通常需要捕捉结束后才知道匹配结果；忽略动作不会重放或还原已拦截的原生输入。

The Ignored page provides separate application exclusions and gesture exclusions. A gesture rule blocks the selected gesture's actions globally or within an executable/title/class scope. Other gestures remain available. Rules are included in configuration archives. Ignoring a recognized action does not replay native input already consumed during capture.

# 连续手势 / Continuous gestures

新增或编辑动作，打开“连续滑动”，选择手指数与滑动方向，再设置要执行的命令（如音量增加）。手指保持接触并沿该方向继续移动，会重复执行命令；不需要先画一个图案。输入设备和程序范围仍受动作设置及全局识别设置约束。

In Add/Edit Action, enable continuous sliding and select the finger count and direction. Configure the command, for example Volume Up. Continue sliding without lifting to repeat it; a drawn pattern is optional. Application scope, input device selection and global recognition settings still apply.
