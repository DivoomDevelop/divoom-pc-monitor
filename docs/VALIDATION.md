# 验证范围

版本：Tauri 桌面 v1.3.1；Windows 后台 v1.3.1、Mac 后台 v1.1.1。更新时间：2026-10-26。

## 已完成

- v1.3.1：旧 Windows 与 Tauri 的底部状态区改为有界、多行、可滚动和选择复制的只读文本框；后台不再截断 300 字符后的设备错误，令牌继续脱敏。旧 Windows 实际窗口验证了 927 字符模拟错误、滚动到底部及全选；不涉及向真实设备发送模拟错误。
- v1.3.1：按 T113 固件 `net/divoom_device_json.c` 的 `divoom_device_pc_update_clock_data_respone_unpack` 修复 511：9000 接口使用 ScreenList/LcdId，保持不自动切表盘。511 实机 10 秒内连续 5 次返回成功；发送记录位于本地忽略目录 `artifacts/device-send-511-fixed.json`。需手动打开设备的 PC 监控表盘，未宣称已确认屏幕实际显示。
- v1.3.1 本地 Windows 178 项、Mac 70 项回归通过，覆盖 511 的独立端口/数据格式、表盘白名单及长错误完整性和脱敏。
- 新增 Tauri 2 桌面前端、Rust 后台生命周期管理和 C# 私有逐行 JSON 桥，复用原有硬件采集与设备通信逻辑。
- 新增桌面检查覆盖真实后台进程、基础硬件快照、非法请求、正常退出、19 种语言的局域网提示、指标时效及选卡选盘；本地默认不会连接设备或修改已有设置。CI 另在一次性 runner 上启用本机回环模拟设备，验证 3191 表盘、六项指标、Content-Length、数字 LocalToken 与令牌不落盘。
- Windows Tauri Release 编译通过；新连接区固定显示同局域网提示与发送按钮，字段内容可滚动。Tauri 完整窗口和安装包检查在下方验收范围中记录。
- Windows 已实际运行 Tauri 主窗口，确认中文同局域网提示、监控/设置/诊断三页显示正常；关闭后隐藏，再次启动恢复同一个窗口，未创建重复实例。
- GitHub Actions 三个平台原生构建均通过：Windows 安装包/免安装 ZIP、Apple Silicon/Intel Mac DMG；每个平台的 6 项桌面检查均通过，两个 Mac 平台额外通过签名后及最终 app 内后台启动检查。构建记录：[39198393223](https://github.com/DivoomDevelop/divoom-pc-monitor/actions/runs/39198393223)（其旧发布步骤因目录层级失败）；[发布重试 39200148173](https://github.com/DivoomDevelop/divoom-pc-monitor/actions/runs/39200148173) 成功复用产物、核对大小及 SHA-256 并公开 v1.3.0。已匿名下载校验文件并核对四个软件包的服务器摘要。本机也实际启动了最终 Windows 免安装目录中的程序。
- Windows Release 构建通过，现有 173 项回归检查覆盖数值格式、缺失/过期值、多显卡识别、SMART 超时及缓存、协议、取消/重试和语言资源。
- Mac 代码完成 Apple 芯片和 Intel 芯片交叉构建；67 项检查覆盖 Darwin 内存布局/页大小、CPU tick 溢出、SMC 温度编码与分类、plist/SMART 解析、共享设备协议和语言资源。
- 19 种语言的资源数量、动态占位符、系统语言匹配与手动设置检查通过；英语、俄语、泰语三个页面完成离屏渲染检查。
- 530 实机验证了 9000 接口、表盘 3191、DeviceId/AppName/DispData 格式及对应 ClockId 兼容重试。
- 402 实机验证了 9000 接口、表盘 625、ScreenList/LcdId 格式及数字 LocalToken；请求记录隐藏令牌。网络超时会退避重试。
- Windows v1.2.0 使用四张指标卡、左侧磁盘列表、右侧连接面板；明确网格行高，连接内容可滚动，发送按钮固定在面板底部。语言刷新不再改变按钮的布局属性。
- Windows/Mac 仅在 Hardware 为 400、402 时显示五屏相关选项，Windows 同时收起对应行。

## 待实机验证

- Windows 最小窗口、不同 DPI、长文本语言及安装/卸载流程仍需补充实际验收；默认尺寸中文窗口已检查。
- 2026-10-19 本机曾出现温度采集初始化超时；2026-10-26 本次实测已能获取 CPU 和磁盘温度，GPU 温度仍不可用。不同硬件、长时间运行下的温度兼容性仍需继续验证。
- Mac 用户环境的 CPU/GPU/磁盘采集、菜单栏交互、局域网授权、休眠唤醒及长时间运行；CI 原生构建和后台启动成功不等于已通过用户 Mac 实机验收。
- Mac Developer ID 签名、公证与用户安装流程。当前包属于测试版本。
- Windows 10、更多 AMD/NVIDIA 显卡、相同型号多显卡、RAID/USB 转接硬盘及更多固件组合。
- 400 的本地令牌鉴权仍需在提供正确令牌的设备上验收；没有令牌时的鉴权错误已正确呈现，不视为发送成功。
- 商业 HWiNFO/CPUID SDK 尚未集成，仅提供扩展接口。

## 复现

Windows 构建及回归：`./build.ps1 -BootstrapSdk`；本地已有编译好的 ADLX 桥时可使用 `-SkipNative`。

Mac 解析/协议/界面检查：`dotnet run --project macos/tests/Mac.Tests.csproj -c Release`。打包及签名说明见 [macos/README.md](../macos/README.md)。

硬件不可用的指标显示并发送 `--`。原始设备/系统错误保留原文，方便定位兼容问题。诊断报告由用户主动导出，程序不自动上传。
