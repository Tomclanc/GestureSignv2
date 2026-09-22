# 本地意图学习 DLC（实验版）

目标：区分快速双指滚动与有意绘制的 L，在原识别器认为要执行智能关闭之后、真正执行之前增加意图判断。默认关闭，不附带未经真实样本验证的通用模型。

## 试用

1. 使用包含 `TouchPadIntentBridge` 的配套主程序。旧版 18.2.8 发布包没有采样接口，单独运行 DLC 无法录到数据。便携版试用前请通过托盘退出旧主程序，避免单实例继续运行旧后台。
2. 打开主程序「选项 → 本地意图学习」。未安装时显示组件大小、「下载学习组件」和「导入组件包」。只在需要时下载安装；主程序不包含 Windows ML / ONNX Runtime。离线试用请选择配套的 `GestureSign-IntentDlc-0.3.0-win-x64.zip`（ARM64 使用对应包），无需解压或运行脚本。
3. 推荐点击「开启后台学习（不拦截）」后照常使用。开启状态会保存，并随手势后台恢复；关闭设置窗口也会继续。后台不弹窗、不暂停原动作；普通轨迹最多每 10 秒保留一条，识别候选优先保留。首次训练前仍可能发生原有误触。
   有空在「待确认样本与纠错」确认滚动或有意手势，候选手势优先显示。后台不会把预测、未投诉或没有匹配到动作视为正确标签。只用确认标签训练，电脑空闲至少 2 分钟后自动检查；同一批标签不会反复训练，每次尝试至少间隔 5 分钟。自动训练不会开启保护。
   起步仍需要每类至少 40 条、4 个会话的真实标签。后台按完整 30 分钟时间段划分会话，避免相邻轨迹泄漏到验证集；可分多次确认。若想加快起步，在「主动补充样本（可选）」录制滚动或有意 L：倒计时 3 秒、每次 45 秒；每类至少 4 次录制，建议每次 10–20 条。
4. 采样期间暂停**绘制手势**命令执行；边缘、TipTap、连续手势仍按原设置运行。需要时先手动关掉这些绑定，以免干扰采样。超过 5 秒、超过两指、换指、静止或太短的轨迹不纳入模型。
5. 点击「训练」。训练只用 CPU：16 个时序/形状特征、带 L2 正则的类别平衡逻辑回归。它是从人工标注拟合权重的机器学习，不是预设阈值冒充的模型。
6. 后台学习本身不拦截；也可切换「观察评分」进行手动试验。选择轨迹纠正标注后，后台学习会在空闲时重新训练；其他模式可点「训练」。切换其他模式或点「停止」会关闭后台自动恢复。
7. 只有留出验证达标才允许手动启用实验性「双指智能关闭拦截」。第一版不保护其他命令，不会把一个动作自动替换成另一个动作，也不能恢复已经被系统/原有捕获逻辑吞掉的滚动。

## 验证与判定

训练集与验证集按完整采样会话分开，每类至少 10 条留出样本。达到「滚动误放行 0、真手势误拦截不超过 20%」才开放实验开关；这只是小样本门槛，不是日常准确率保证。模型分数未经概率校准，0.85 为放行阈值。删除或纠正标签会撤销旧验证资格，重新训练后才能启用保护。

保护开启且组件心跳有效时：低于阈值、无有效轨迹、推理异常或 45 ms 截止时间超时，都跳过本次双指智能关闭。其他动作保持原逻辑。新模型编译不在输入线程进行；运行中后端故障可能使当次请求超时，随后继续尝试下一后端。设置窗口关闭后，后台引擎继续工作；退出手势后台时引擎自动停止。后台学习在用户开启后会随后台恢复；智能关闭保护重启后仍需手动开启。引擎崩溃后心跳最多 15 秒过期并恢复原有行为。主程序读取开关最多有约 500 ms 延迟。

## NPU → GPU → CPU

使用 Windows ML 2.3.42 + ONNX Runtime Managed 1.27.1。按实际设备类型枚举 NPU（Intel OpenVINO、AMD VitisAI、Qualcomm QNN），再尝试 GPU，最后 CPU。GPU 包含 NVIDIA 独显，走 DirectML，无需用户单独安装 CUDA；自动模式使用首先成功的设备，并不强制优先独显。初次点击「准备 NPU / GPU 组件」会由 Windows ML 下载并注册兼容组件；普通启动仅注册已安装组件，不主动下载。

加速会话禁用静默 CPU 节点回退，并用已知输入比对 CPU 数值后才显示对应后端。存在 NPU 不保证模型算子、数据类型、驱动和运行时兼容。这个小型浮点模型可能在某些 NPU 上不支持而回退 GPU/CPU；第一版没有强行声称已支持所有芯片，也未提供 QNN 专用量化模型。极小模型的 GPU/NPU 调度开销可能高于 CPU，优先级遵从用户设置需求，未宣称一定更快或更省电。

- [Windows ML 支持的执行提供程序](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/supported-execution-providers)
- [显式选择设备](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/select-execution-providers)

## 数据与卸载

样本、人工标签、原识别结果、模型评分和模型在 `%LOCALAPPDATA%\GestureSign V2\IntentDlc`，与 OneDrive 配置目录分离。只记录触控轨迹/时间，不保存网页内容或窗口标题，不上传数据。最多保留 2000 条，超过后优先淘汰最旧未标注样本；后台新样本不会挤掉已确认标签；每条最多 5 秒。列表显示最新 100 条，完整数据可通过「打开本地数据」查看。

停用：在主程序内点击「停止 / 关闭判断」。卸载：点击「卸载组件」，会先停止后台引擎，仅删除组件程序，保留个人样本与模型。主程序缺少组件或没有有效启用状态时保持原行为。`GestureSign.IntentDlc.exe` 现在仅为无窗口后台引擎，不再包含独立设置界面。

## 下载与发布

主程序随附 `Assets/intent-dlc.catalog.json`，按系统架构选择固定版本 ZIP，并验证完整 SHA-256、长度、协议、PE 架构和解压路径，再原子安装。下载可取消；失败不覆盖现有组件。组件目录与学习数据目录分离。0.1 独立窗口版不是可导入的 0.2 组件包。

下载地址约定为 GitHub Release `intent-dlc-v0.3.0` 下的两个架构资产。构建代码不等于资源已经上线；若尚未上传，按钮会明确提示「尚未上传，可导入配套离线包」。发布时必须上传与随主程序 catalog 校验值匹配的原始 ZIP；重打包会改变哈希，需同步更新 catalog 并重新构建主程序。

## 构建与测试

```powershell
dotnet run --project tests/GestureSign.IntentTests -c Release
dotnet run --project tests/GestureSign.IntentBridgeTests -c Release
dotnet run --project tests/GestureSign.IntentPackageTests -c Release
dotnet run --project tests/GestureSign.IntentHostTests -c Release
dotnet build GestureSign.IntentDlc -c Release
dotnet GestureSign.IntentDlc/bin/Release/net10.0-windows10.0.26100.0/win-x64/GestureSign.IntentDlc.dll --self-test runtime-test.json
./installer/Build-IntentDlc.ps1 -Architecture x64 -OutputDirectory ./publish/IntentDlc-x64
```

构建脚本同时生成 ZIP 与 `.zip.catalog.json`；将 x64 / ARM64 两份 catalog 条目组合成数组，保存到 WinUI 的 `Assets/intent-dlc.catalog.json` 后构建主程序。主程序与组件分别交付，不把可选运行库重新塞回基础包。

自测合成数据只验证提取、训练、留出隔离、IPC、超时和后端数值一致性。降低实际误触的效果必须用用户真实轨迹验证。ARM64 包需要独立构建；x64 运行结果不能证明高通 NPU 可用。
