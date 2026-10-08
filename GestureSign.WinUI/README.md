# GestureSign.WinUI

GestureSign V2 的 WinUI 3 设置前端，目标框架为 .NET 10，支持 x64 / ARM64 架构。

请从仓库根目录使用统一解决方案构建：

```powershell
dotnet build .\GestureSign.sln -c Release -p:Platform=x64
```

生成 MSI、便携版或 Microsoft Store 包时，请使用 `installer` 目录中的打包脚本。

GitHub MSI 和便携版使用共享 .NET 10 / Windows App SDK Runtime，打包时剔除运行环境包。Kando 与 AI 组件作为可选下载，不捆绑在主程序中。正式打包规则与最新下载见[仓库首页](../README.md)。
