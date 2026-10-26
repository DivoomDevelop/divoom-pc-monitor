# Divoom PC 硬件监控

[简体中文](README.md) | [English](README.en.md)

Windows 10/11 x64、macOS 14+ 桌面工具，v1.3.0 起使用 Tauri 2 统一界面和安装包，保留 C# 硬件采集与设备通信后台。本机监控独立运行，不需要先连接设备或访问云端。

当前应用版本 v1.3.1。macOS 分别提供 Apple 芯片与 Intel 芯片测试版，使用、温度支持范围及签名说明见 [macos/README.md](macos/README.md)。Tauri 构建说明见 [docs/TAURI.md](docs/TAURI.md)。

## 下载即用

无需编译，下载对应系统的软件包并完整解压。所有下载集中在 [GitHub Releases](https://github.com/DivoomDevelop/divoom-pc-monitor/releases/latest)，源码 ZIP 不包含可执行程序。

| 系统 | 下载 | 使用 |
| --- | --- | --- |
| Windows 10/11 x64 | [Tauri 安装包 v1.3.1](https://github.com/DivoomDevelop/divoom-pc-monitor/releases/download/v1.3.1/DivoomPCMonitor-win-x64-v1.3.1-setup.exe) | 安装后打开 DivoomPCMonitor |
| Windows 10/11 x64 | [免安装 ZIP v1.3.1](https://github.com/DivoomDevelop/divoom-pc-monitor/releases/download/v1.3.1/DivoomPCMonitor-win-x64-v1.3.1.zip) | 完整解压，运行 `DivoomPCMonitor/DivoomPCMonitor.exe` |
| macOS 14+ Apple 芯片 | [Apple 芯片 DMG v1.3.1](https://github.com/DivoomDevelop/divoom-pc-monitor/releases/download/v1.3.1/DivoomPCMonitor-mac-arm64-v1.3.1.dmg) | 打开 DMG，将 `.app` 拖到“应用程序” |
| macOS 14+ Intel 芯片 | [Intel 芯片 DMG v1.3.1](https://github.com/DivoomDevelop/divoom-pc-monitor/releases/download/v1.3.1/DivoomPCMonitor-mac-x64-v1.3.1.dmg) | 打开 DMG，将 `.app` 拖到“应用程序” |

发布包自带 .NET 运行时。macOS 包尚未完成 Developer ID 签名、公证及 Mac 实机验收，可能被 Gatekeeper 阻止，详见 [Mac 使用说明](macos/README.md)。Release 同时提供 SHA-256 校验文件。

## 使用

安装后打开 DivoomPCMonitor；使用 ZIP 时保留完整目录和 `monitor` 后台目录。发布包包含 .NET 运行时，Windows 界面需要 WebView2，Tauri 安装程序会在缺少时安装该运行时。

- 设备连接区常驻提示：**仅支持连接与本电脑处于同一局域网的设备。** 该提示支持 19 种语言；不是自动检测网络连通性的结果。

- 主面板显示 CPU/GPU 型号、使用率、温度、内存占用、物理磁盘活动率和温度、本地卷容量占用。
- 默认选择独立显卡和系统盘；可在“发送显卡/硬盘”选择数据来源。
- 点击“刷新设备”读取设备列表并自动填写 Hardware 型号，或手动填写设备 IPv4 地址及 Hardware。90、92、400 使用 80 端口，其它型号使用 9000 端口；界面显示当前接口并保存型号设置。
- 只有 Hardware 为 400、402 时显示 TimeGate 五屏、LCD 1–5 和独立模式选项；其它型号隐藏并收起该区域。联网设备列表可自动读取模式，手动 IP 可完全离线设置。
- 点击“开始发送”时，90/92/96/400/402 切换到表盘 625，530 切换到表盘 3191，其它型号不发送切换表盘命令；随后每两秒发送 PC 数据。停止发送不停止本机监控。
- 关闭窗口隐藏到托盘，双击托盘恢复。彻底退出请使用托盘菜单“退出”。默认不启用开机启动，不自动恢复发送。
- 首次启动跟随系统语言，不匹配时使用英语；中文区分简体/繁体。采集设置提供“跟随系统”及 19 种语言，Tauri 界面保存后即时生效：英语、简体中文、繁体中文、日语、德语、泰语、俄语、韩语、越南语、法语、土耳其语、西班牙语、葡萄牙语、印尼语、意大利语、乌克兰语、瑞典语、丹麦语、波兰语。产品界面/状态使用语言资源；硬件型号、命令标识、系统/设备原始错误及诊断内容保留原文。
- 连接区提供可选 `LocalToken`。获取路径：**Divoom APP → Wi-Fi 设备列表 → 对应设备“设置” → 本地令牌**。填写非负整数，随表盘切换与 PC 数据请求作为 JSON 数字发送；留空不发该字段。界面遮挡显示，仅本次运行内保存，修改 IP 时清空，不写入设置或诊断明细。设备错误中的 `DeviceToken is err` 表示需要检查此令牌，实际请求字段仍是 `LocalToken`。

## 温度与兼容性

1. Windows 原生 PDH、DXGI、GlobalMemoryStatusEx 获取基础指标，计数器使用英文接口，不依赖系统显示语言。
2. LibreHardwareMonitorLib 0.9.6 获取传感器；其内部已有 NVIDIA NVAPI/NVML 和 Intel IGCL 接入。
3. 自带的 `Divoom.AdlxBridge.dll` 只从系统目录加载 AMD 显卡驱动的 `amdadlx64.dll`，补充可用的 AMD 指标；无 AMD 设备时不影响其他采集。
4. CPU 等低层传感器可能需要 PawnIO 和管理员权限。在“采集设置”打开 [PawnIO 官方网站](https://pawnio.eu/)，安装官方签名版，然后重新检测，必要时点击“以管理员身份重启”。程序不自动安装驱动或更改系统安全设置。
5. 已内置官方 [smartmontools 7.5](https://github.com/smartmontools/smartmontools/releases/tag/RELEASE_7_5) x64 `smartctl.exe` 和磁盘数据库。默认自动使用，无需安装或选择路径：已有采集器取得有效温度时保留原来源，仅对缺失/过期温度的物理磁盘补充查询。每 30 秒查询，单盘超时 5 秒、整批最多 12 秒，部分磁盘仍需要管理员权限。RAID/USB 转接等仍取决于控制器能力。

“采集设置 → 硬盘温度补充”可选择自动（默认）、禁用、自定义路径。自动模式固定使用发布目录 `third_party/smartmontools/smartctl.exe`，与 EXE 一起移动仍可定位。原先手动启用并指定路径的设置会保留为自定义模式；原先默认关闭的设置升级为自动模式。明确选择禁用后会持续保持禁用。

smartctl 以独立子进程读取 JSON，不安装服务、不运行安装器、不修改 PATH。对应 GPL 许可证、说明和完整上游源码压缩包位于 `third_party/smartmontools/`；发布时需要保留整个目录。

读取失败显示 `--`，不会把缺失值变成 0。过期温度也发送 `--`。CPU 温度优先 Package/Tdie，核心回退取最高值；不把 ACPI 热区当作 CPU 温度。GPU 负载为每张卡最忙引擎，厂商回退核心负载会注明来源；不同口径不保证与任务管理器每一帧完全相同。

两个相同 PCI 型号/子系统的显卡无法仅凭当前身份字段可靠关联温度时，保留缺失值，避免把另一张卡的数据发送出去。容量按卷显示，活动率和温度按物理磁盘显示；跨盘卷不强行映射到一个磁盘。

硬件采集运行于隔离子进程，30 秒无响应时终止并退避重启，界面和 Windows 基础指标继续运行。

## 设备协议

表盘切换和 PC 数据发送均按 Hardware 型号选择 HTTP POST 地址，使用原有 `Channel/SetClockSelectId` 和 `Device/UpdatePCParaInfo`。

| Hardware | 地址 |
| --- | --- |
| 90、92、400 | `http://设备IP:80/post` |
| 其它型号（含 530） | `http://设备IP:9000/divoom_api` |

端口 80 在部分 URL 显示中会自动省略。Hardware 0/未知按其它型号处理，手填 IP 时应填写实际型号。

HTTP 请求先缓冲为 UTF-8 JSON，明确设置 Content-Length；避免设备 HTTP 服务不支持分块请求时收不到 Command。桌面与诊断模式均使用相同发送格式。

表盘切换按 Hardware 白名单执行：90/92/96/400/402 → 625，530 → 3191，其它型号跳过。连接失败重试也遵守该规则。此规则独立于端口规则，96/402 仍使用 9000 接口。

端口与数据格式独立选择：**400/402/511 使用 `ScreenList/LcdId` 格式，402/511 的地址仍为 9000**，不能把 530 的格式用于所有 9000 设备。511 不自动切换表盘；请在设备上手动打开 PC 监控表盘，固件只更新已打开的 PC 监控页面。

530 等 API 数据格式使用 `DeviceId`、`AppName` 和包含 `AppTitle/AppData` 的 `DispData` 数组。PC 数据默认不带 `ClockId`；白名单型号遇到旧固件参数格式错误时，使用对应表盘 ID 兼容重试（530 使用 3191），成功后在本次运行中记住该设备要求。其它型号不增加 ClockId 兼容重试。该格式需要有效 DeviceId：选择设备列表会自动填写，也可手动填写。更换 IP 时会清除旧 DeviceId 和 LocalToken。

80 接口保留原有 `ScreenList[0].LcdId`（0–4）及六项字符串。两类接口的数据顺序一致：CPU 使用率、GPU 使用率、CPU 温度、GPU 温度、内存占用率、硬盘温度。使用率带 `%`，温度带 `C`，不可用为 `--`。新增容量/磁盘活动率不写入旧协议。

发送失败按 2、4、8、16、30 秒退避，只发送最新快照，不补发过期队列。HTTP 和设备返回错误均完整显示在底部的多行只读框，可滚动、全选并复制；不再将设备响应截到 300 个字符，令牌仍会脱敏。

## 构建和验证

当前界面和安装包使用 Tauri，需要 Node.js 24、Rust stable、.NET 10 SDK；Windows 还需要 Visual Studio C++ x64 工具。默认构建：`./scripts/Build-Tauri.ps1`，然后运行 `./scripts/Package-Tauri.ps1` 整理可下载文件，详见 [Tauri 构建说明](docs/TAURI.md)。

下面的兼容脚本只构建 C# 后台及旧原生界面，供诊断和后台开发使用：

```powershell
./build.ps1 -BootstrapSdk
```

脚本在 `.tools` 下载并校验固定版本 SDK，下载固定提交的 AMD SDK 头文件，编译桥、程序，执行回归检查并发布自包含目录。已安装 .NET 10 时可省略 `-BootstrapSdk`。已有桥或明确不需要 ADLX 时可用 `-SkipNative`。

smartmontools 已放在源码目录 `third_party/smartmontools/` 中，构建时会校验固定版本 EXE 与源码的 SHA-256，再连同许可证一起复制到发布目录。更新该组件的方法见其 `BUNDLE.md`。

命令行诊断（不会访问设备）：

```powershell
./dist/DivoomPCMonitor/Divoom.HardwareMonitor.exe --probe 10
```

WinExe 的命令行输出应重定向到文件或由调用者捕获。界面的“诊断”页也可导出 JSON。

局域网发送测试：`--send-test IPv4 Hardware [发送秒数，默认20] [DeviceId]`。该模式会切换目标 PC 表盘并发送数据，到时自动停止；使用与桌面界面相同的采集和发送逻辑，标准输出报告包含实际请求、HTTP 响应、设备返回码及成功次数。9000 接口必须提供 DeviceId。

排查桌面差异时在命令末尾加 `--untraced`，直接使用默认传输，不插入请求记录器；该模式仅输出成功次数和状态，不包含 Exchanges 请求明细。

## 商业 SDK 扩展

HWiNFO/CPUID SDK 尚未购买和集成。公开接口 `IHardwareProvider` 接收 `SensorFrame`，可用于自行实现已授权的适配器。

- 在 .NET 10 Windows x64 类库中引用 `Divoom.HardwareMonitor.dll`，公开无参构造的 `IHardwareProvider` 实现。
- 输出名以 `.Provider.dll` 结尾，连同依赖和 `deps.json` 放到发布目录 `providers/`。
- `Read()` 返回 cpu/gpu/disk 指标：GPU `MatchKey` 使用 `VEN:DEV:SUBSYS` 大写十六进制格式，disk ID 使用 Windows 物理磁盘索引 `disk/N`；携带真实时间戳、单位、来源与错误。
- 不可用字段返回 null；禁止以列表顺序映射设备。`Dispose()` 释放 SDK 资源。
- 默认不加载扩展；勾选“启用 SDK 扩展”并保存后，在隔离采集进程中加载。

## 本地数据

设置和滚动日志保存在 `%LOCALAPPDATA%/Divoom/HardwareMonitor/`。诊断报告只在主动导出时生成，程序不自动上传硬件信息。设备发现使用原工具的云端接口；数据发送直达填写的设备 IP。

## 验证范围

回归检查覆盖百分比边界、温度选择、多 GPU 匹配和负载聚合、SMART 状态码、旧协议字段、HTTP/设备错误、发送取消和退避。

公开验证范围见 [docs/VALIDATION.md](docs/VALIDATION.md)。本机详细诊断记录和运行日志不上传仓库。未连接的硬件型号、未提供的商业 SDK、未连接的真实 Divoom 设备不视为已验证。
