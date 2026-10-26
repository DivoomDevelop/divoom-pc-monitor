# 验证范围

版本：Tauri 桌面 v1.3.3；Windows 后台 v1.3.3、Mac 后台 v1.1.3。更新时间：2026-10-26。

v1.3.3 为重新构建发布版，保留 v1.3.2 的采集、协议、界面和设备白名单逻辑，随包包含最新中英文说明与固件验证范围。历史检查结果见下方；本次构建检查结果由对应 GitHub Actions 记录提供。

## 已完成

- v1.3.3 [构建及发布 40276664985](https://github.com/DivoomDevelop/divoom-pc-monitor/actions/runs/40276664985) 三个平台构建和发布全部成功。Windows 186 项、两个 Mac 平台各 76 项后台检查通过，各平台 6 项桌面检查及 Mac 最终 app 内后台启动检查通过。已匿名下载 SHA-256 文件，核对公开的四个软件包名称、状态、大小及服务器摘要。本机另生成 `dist/tauri-v1.3.3/` Windows 安装包和免安装 ZIP，检查了完整目录、后台版本和中英文说明；本机产物使用单独的 `SHA256SUMS-local-v1.3.3.txt`，不与云端构建摘要混用。
- v1.3.2 [构建及发布 40275171261](https://github.com/DivoomDevelop/divoom-pc-monitor/actions/runs/40275171261) 四个任务全部成功：Windows、Apple 芯片 Mac、Intel Mac 及发布。三个平台各通过 6 项桌面检查，包含真实后台桥接的 530 和 510/511/512 回环发送。已匿名下载校验文件，确认公开的四个软件包名称、上传状态、大小及 SHA-256 与服务器摘要一致；设备固件测试程序仅在本地提供。
- v1.3.2：TimesFrame 510/511/512 先发送 `Channel/SetClockSelectId`，ClockId 为 1631，再向 `9000/divoom_api` 发送 ScreenList/LcdId 六项指标；其它型号白名单和仅 400/402 显示五屏选项的规则保持不变。
- v1.3.2：本地 Windows 186 项、Mac 共享协议/解析/界面 76 项检查通过；桌面 5 项通过，CI 专用发送测试在本地跳过。新增 CI 真实桥接回环测试逐一覆盖 510/511/512 的选表顺序、1631、数据格式、Content-Length 与数字 LocalToken。
- v1.3.2：511175 实机发送记录 `artifacts/device-send-511-1631-v1.3.2.json` 确认先选 1631，然后 10 秒内 5 次数据更新均成功。只读 `Channel/GetClockInfo` 返回 1631，`Device/GetLocalClockInfo` 确认该表盘名称为“电脑监测 / Computer monitoring”。用户目视确认已显示表盘，但数值未刷新；510/512 尚无实机验收。
- 固件源码检查：`middle/include/divoom.h` 的 `DIVOOM_PC_PARA_CLOCK_ID` 原为 1018，`divoom_clock_control_update_pc_data` 按此常量查找控制器，找不到时直接返回。已在用户提供的固件源码中将 510/511/512 改为 1631，其它型号及只读资源边界保持原值。设备尚未更新该固件；桌面包更新本身不能解决旧固件中的查找错误。
- 511175 固件测试程序已完成 ARM 交叉编译与链接，反汇编确认 PC 更新函数常量为 `0x65f`（1631）。本地交付目录 `artifacts/firmware-511175-pc1631/`，SHA-256：`2045647510023096d7755698de6673e5725d64d042a51b80ba90f7b89e583497`。它是设备 ELF 程序，非完整 OTA 包，版本号仍为 511175；用户暂不能连接 ADB，因此仅提供产物，未更新设备或宣称刷新问题已通过实机验证。
- v1.3.1：旧 Windows 与 Tauri 的底部状态区改为有界、多行、可滚动和选择复制的只读文本框；后台不再截断 300 字符后的设备错误，令牌继续脱敏。旧 Windows 与最终打包的 Tauri 实际窗口均验证了 927 字符模拟错误、滚动查看尾部及全选；模拟服务仅监听本机回环地址，测试后已停止并恢复临时目标设置。
- v1.3.1：按 T113 固件 `net/divoom_device_json.c` 的 `divoom_device_pc_update_clock_data_respone_unpack` 修复 511：9000 接口使用 ScreenList/LcdId，该历史版本不自动切表盘。511175 固件实机 10 秒内连续 5 次返回成功；发送记录位于本地忽略目录 `artifacts/device-send-511-fixed.json`。只读查询返回当前表盘 1631。更正：用户确认 TimesFrame 的 PC 表盘为 1631，之前根据源码旧常量 1018 建议手动切换的判断有误；v1.3.2 将 510/511/512 加入自动切换 1631 白名单。返回成功不等于已经确认屏幕实际刷新。
- v1.3.1 本地 Windows 178 项、Mac 70 项回归通过，覆盖 511 的独立端口/数据格式、表盘白名单及长错误完整性和脱敏。
- v1.3.1 [云端构建及发布 40267174567](https://github.com/DivoomDevelop/divoom-pc-monitor/actions/runs/40267174567) 四个任务全部通过：Windows、Apple Silicon Mac、Intel Mac 构建及发布。所有平台均通过包含 530/511 格式的真实桥接模拟发送检查；发布的四个软件包及 SHA-256 文件已核对，并通过匿名下载校验文件复核服务器摘要。
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
