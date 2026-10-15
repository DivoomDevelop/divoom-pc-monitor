# Divoom Mac Monitor

macOS 桌面硬件监控，分别提供 Apple 芯片 `osx-arm64` 和 Intel `osx-x64` 自包含版本，不需要另装 .NET。技术最低版本 macOS 14；建议使用仍受 Apple/.NET 支持的 macOS 版本。当前 Windows 环境只验证了构建、离线解析和共享协议，尚未完成 Mac 实机验证、Developer ID 签名与公证，属于测试版本。

## 使用

解压对应芯片的 `.tar.gz`，将 `DivoomMacMonitor.app` 拷贝到“应用程序”后打开。包内保留可执行权限。未签名测试包可能被 Gatekeeper 阻止；正式交付应先按下面流程签名、公证，不需要关闭系统安全设置。

- CPU 型号/使用率：sysctl 与 Mach tick 差分。内存：应用（匿名减可清除）、固定与物理压缩页之和；与 Windows 内存口径不同。
- GPU 型号/使用率：IOKit PerformanceStatistics；CPU/GPU 温度：AppleSMC 只读访问。Intel 和 Apple 芯片传感器键独立识别；未知键或系统未开放时显示 `--`。多个 GPU 无法可靠关联 SMC 温度时不猜测对应关系。
- 磁盘型号/卷容量：diskutil 与系统卷信息。磁盘“活动”是累计读写服务时间差分占比，可能包含并行请求，与 Windows 空闲时间差分口径不同；驱动不提供计数器时显示 `--`。
- 磁盘温度：自动查找 macOS smartctl，优先应用 `Contents/MacOS/tools/smartctl`，然后 Homebrew 的 `/opt/homebrew`、`/usr/local` 下的 `sbin/bin`。当前包未内置 macOS smartctl，可自行安装 `smartmontools`，或在采集设置指定路径。每 30 秒更新，单命令 5 秒、整批 SMART 枚举预算 12 秒；不会自动提权。
- 采集运行于隔离进程；30 秒无响应时终止并重试。过期指标不发送为有效值。关闭窗口后从菜单栏图标恢复或退出。

## 设备连接

Windows/Mac 直接编译同一份 `Transport.cs`，端口和数据格式分别选择：90/92/400 → `IP:80/post`；其它型号 → `IP:9000/divoom_api`。400/402 使用 `ScreenList/LcdId` 六项字符串，402 同样使用 9000；530 等 API 型号使用 `DeviceId/AppName/DispData`。两种协议发送的指标顺序与 Windows 版相同。

90/92/96/400/402 选择表盘 625，530 选择 3191，其它型号不切换表盘。默认 PC 数据不带 ClockId；API 固件明确要求时用对应表盘 ID 重试。

只有 Hardware 为 400、402 时显示 LCD 选择和独立模式选项；其它型号隐藏该区域。

`LocalToken` 可留空。需要时打开 **Divoom APP → Wi-Fi 设备列表 → 对应设备“设置” → 本地令牌**，将数值填入连接区。作为 JSON 数字 `LocalToken` 随表盘切换和数据请求发送，不使用 `DeviceToken` 字段。仅在本次运行内保留，修改 IP 后清空，不写入设置或诊断报告。

## 语言与日志

首次启动跟随系统语言；不支持的系统语言回退英语。设置可选“跟随系统”或 19 种语言，保存后重启生效：英语、简中、繁中、日语、德语、泰语、俄语、韩语、越南语、法语、土耳其语、西班牙语、葡萄牙语、印尼语、意大利语、乌克兰语、瑞典语、丹麦语、波兰语。硬件型号、协议标识和原始错误保留原文。

设置/滚动日志位于 `~/Library/Application Support/Divoom/HardwareMonitor/`。“诊断 → 导出诊断报告”包含系统/架构、SMC 候选键、指标来源、错误和最近日志，不自动上传。

## 构建、签名与验证

Windows 交叉构建（PowerShell 7，.NET 10 SDK）：`./scripts/Package-Mac.ps1`。输出两种芯片包，包含运行时与许可证。原生符号调用及采集值仍需在 Mac 上验证。

Mac 本地构建：`bash macos/build-macos.sh osx-arm64`，Intel 改为 `osx-x64`。默认 ad-hoc 签名只用于本机测试。正式签名设置 `SIGNING_IDENTITY` 为自己的 Developer ID Application 证书；配置好的公证钥匙串名称通过 `NOTARY_PROFILE` 传入。脚本不会创建证书、购买账号或修改系统安全策略。

命令行读取（不联网）：`DivoomMacMonitor.app/Contents/MacOS/Divoom.MacMonitor --probe 10`。本地发送：`--send-test IPv4 Hardware Seconds DeviceId`；有令牌时通过进程环境变量 `DIVOOM_LOCAL_TOKEN` 提供，避免放入命令行。测试到时自动停止，实际会切换白名单表盘并发送 PC 数据。

开发验证：`dotnet run --project macos/tests/Mac.Tests.csproj -c Release`。覆盖 CPU tick 溢出、Darwin 内存字段/页大小、SMC 编码与传感器分类、plist/SMART 解析、19 语言资源、共享端口/表盘/请求格式与过期指标。需要补充 Intel Mac、Apple Silicon Mac 的采集、菜单栏、局域网权限与签名包实测。

依赖：Avalonia/Fluent（MIT）、SkiaSharp/HarfBuzzSharp 及 .NET 运行时。交叉构建脚本将可用许可证复制进 `Contents/Resources/licenses/`；SMC/IOKit 适配器为本项目代码，未复制第三方监控应用实现。
