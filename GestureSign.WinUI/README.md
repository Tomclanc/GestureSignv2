# GestureSign.WinUI

GestureSign V2 的 WinUI 3 设置前端，目标框架为 .NET 10，目标架构仅为 x64。

请从仓库根目录使用统一解决方案构建：

```powershell
dotnet build .\GestureSign.sln -c Release -p:Platform=x64
```

生成 MSI、便携版或 Microsoft Store 包时，请使用 `installer` 目录中的打包脚本。
