# 验证范围

版本：Windows v1.2.0，macOS v1.0.1 测试版。更新时间：2026-10-15。

## 已完成

- Windows Release 构建通过，现有 173 项回归检查覆盖数值格式、缺失/过期值、多显卡识别、SMART 超时及缓存、协议、取消/重试和语言资源。
- Mac 代码完成 Apple 芯片和 Intel 芯片交叉构建；67 项检查覆盖 Darwin 内存布局/页大小、CPU tick 溢出、SMC 温度编码与分类、plist/SMART 解析、共享设备协议和语言资源。
- 19 种语言的资源数量、动态占位符、系统语言匹配与手动设置检查通过；英语、俄语、泰语三个页面完成离屏渲染检查。
- 530 实机验证了 9000 接口、表盘 3191、DeviceId/AppName/DispData 格式及对应 ClockId 兼容重试。
- 402 实机验证了 9000 接口、表盘 625、ScreenList/LcdId 格式及数字 LocalToken；请求记录隐藏令牌。网络超时会退避重试。
- Windows v1.2.0 使用四张指标卡、左侧磁盘列表、右侧连接面板；明确网格行高，连接内容可滚动，发送按钮固定在面板底部。语言刷新不再改变按钮的布局属性。
- Windows/Mac 仅在 Hardware 为 400、402 时显示五屏相关选项，Windows 同时收起对应行。

## 待实机验证

- Windows 最终发布包在默认/最小窗口、不同 DPI 和长文本语言下的完整视觉验收尚未完成；界面操作检查被用户中止。
- Mac 的真实 CPU/GPU/磁盘采集、菜单栏交互、局域网授权、休眠唤醒及长时间运行；交叉构建成功不等于已通过 Mac 实机验收。
- Mac Developer ID 签名、公证与用户安装流程。当前包属于测试版本。
- Windows 10、更多 AMD/NVIDIA 显卡、相同型号多显卡、RAID/USB 转接硬盘及更多固件组合。
- 400 的本地令牌鉴权仍需在提供正确令牌的设备上验收；没有令牌时的鉴权错误已正确呈现，不视为发送成功。
- 商业 HWiNFO/CPUID SDK 尚未集成，仅提供扩展接口。

## 复现

Windows 构建及回归：`./build.ps1 -BootstrapSdk`；本地已有编译好的 ADLX 桥时可使用 `-SkipNative`。

Mac 解析/协议/界面检查：`dotnet run --project macos/tests/Mac.Tests.csproj -c Release`。打包及签名说明见 [macos/README.md](../macos/README.md)。

硬件不可用的指标显示并发送 `--`。原始设备/系统错误保留原文，方便定位兼容问题。诊断报告由用户主动导出，程序不自动上传。
