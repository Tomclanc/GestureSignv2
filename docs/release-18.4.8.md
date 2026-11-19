GestureSign V2 18.4.8

- 修复部分电脑开启管理员自启动后，开关立即弹回的问题。
- 验证自启动注册结果，失败时显示提示并记录详情。切换启动方式时先创建新启动项。
- 稳定版提供 x64 和 ARM64 的 MSI 与便携 ZIP。

Fix the administrator startup switch reverting on some PCs. Verify startup registration and log failures. Create the replacement startup entry before removing the previous one.

The optional AI component remains at 18.4.7.
