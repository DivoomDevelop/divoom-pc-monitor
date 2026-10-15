# English

Windows v1.2.0 / macOS v1.0.1 beta

## Download and run

- **Windows 10/11 x64:** download `DivoomPCMonitor-win-x64-v1.2.0.zip`, extract the entire archive and run `DivoomPCMonitor/Divoom.HardwareMonitor.exe`. Keep the complete directory. No separate .NET installation is needed.
- **Apple Silicon Mac:** download `DivoomMacMonitor-osx-arm64-v1.0.1.tar.gz`.
- **Intel Mac:** download `DivoomMacMonitor-osx-x64-v1.0.1.tar.gz`.
- Mac requires macOS 14 or later. Extract the archive and copy the `.app` to Applications. Mac packages are betas without Developer ID signing, notarization or physical-Mac acceptance testing; Gatekeeper may block them. See the [Mac guide](https://github.com/DivoomDevelop/divoom-pc-monitor/blob/main/macos/README.en.md) for signing and notarization.
- `SHA256SUMS-v1.2.0.txt` contains SHA-256 checksums for all three application packages. GitHub's automatically generated **Source code** archives contain source files, not ready-to-run applications.
- Documentation: [English README](https://github.com/DivoomDevelop/divoom-pc-monitor/blob/main/README.en.md) · [中文 README](https://github.com/DivoomDevelop/divoom-pc-monitor/blob/main/README.md). Both languages are included in the application packages.

## Changes

- Reorganized the Windows dashboard using the imagegen layout reference: four metric cards at the top, disk list and source selectors on the left, and device connection controls on the right. Explicit grid row heights prevent overlapping controls. The send button stays at the bottom; connection content can scroll in smaller windows.
- Windows shows TimeGate five-screen, LCD selection and independent-mode controls only for Hardware **400 or 402**; other models hide and collapse that section. Mac LCD controls also appear only for 400 or 402.
- Preserves 19 interface languages, LocalToken input and its retrieval hint, and the existing endpoint and clock-selection whitelist rules.
- Packages include the runtime and applicable third-party licenses. Windows also includes the matching smartmontools source archive.

## Validation

Windows builds and all **173 regression checks** passed. Both Mac architectures cross-build, and all **67 parser, protocol and language UI checks** passed. Package structure, Mac architectures and executable permissions were checked. The automated release workflow also verifies uploaded file sizes and SHA-256 digests.

Full visual acceptance of the final Windows package across window sizes and DPI settings remains pending; interactive UI checking was stopped by the user. Mac hardware collection, actual installation and long-running behavior still require physical-Mac testing. See [validation scope](https://github.com/DivoomDevelop/divoom-pc-monitor/blob/main/docs/VALIDATION.md).

---

# 简体中文

Windows v1.2.0 / macOS v1.0.1 测试版

## 下载和使用

- Windows 10/11 x64：下载 `DivoomPCMonitor-win-x64-v1.2.0.zip`，完整解压，运行 `DivoomPCMonitor/Divoom.HardwareMonitor.exe`。保留整个目录，无需另装 .NET。
- Apple 芯片 Mac：下载 `DivoomMacMonitor-osx-arm64-v1.0.1.tar.gz`。
- Intel 芯片 Mac：下载 `DivoomMacMonitor-osx-x64-v1.0.1.tar.gz`。
- Mac 最低 macOS 14，解压后将 `.app` 拷贝到“应用程序”。Mac 包是未经 Developer ID 签名、公证及实机验收的测试版本，可能被 Gatekeeper 阻止。签名、公证说明见仓库 macos/README.md。
- `SHA256SUMS-v1.2.0.txt` 提供三个软件包的 SHA-256 校验值。GitHub 自动提供的 Source code ZIP 是源码，不是可运行的软件包。

## 修改

- 按 imagegen 设计参考重新组织 Windows 主页面：上方四张指标卡、左侧磁盘列表与数据来源、右侧设备连接面板。改用明确的网格行高，避免自动撑高导致控件重叠；发送按钮固定在面板底部，小窗口下连接内容可滚动。
- Windows 仅在 Hardware 为 400、402 时显示 TimeGate 五屏、LCD 选择和独立模式，其他型号隐藏并收起对应区域；Mac 的 LCD 选项同样仅对 400、402 显示。
- 保留 19 种语言、LocalToken 输入和获取提示、已有端口和表盘白名单规则。
- 软件包包含运行时及相关第三方许可证。Windows 同时保留 smartmontools 的对应源码压缩包。

## 验证范围

Windows 编译通过，173 项回归检查通过；Mac 两种架构交叉构建通过，67 项解析、协议和语言界面检查通过。软件包结构、Mac 架构及可执行权限已检查。

Windows 最终发布包的完整窗口尺寸/DPI视觉验收尚未完成，界面操作检查被用户中止。Mac 硬件采集、实际安装和长时间运行仍待实机验证。更多范围见仓库 docs/VALIDATION.md。
