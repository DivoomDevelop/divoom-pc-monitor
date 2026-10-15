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
