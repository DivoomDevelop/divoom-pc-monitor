# Tauri desktop / Tauri 桌面版

## 中文

v1.3.0 起，默认发布由 Tauri 2 构建 Windows NSIS 安装包、Windows 免安装 ZIP、Apple Silicon 和 Intel Mac DMG。旧 WinForms/Avalonia 界面仍留在仓库用于兼容和后台诊断；不再由版本标签自动发布。

界面位于 `desktop/`。Rust 主进程只从安装目录的 `monitor/` 启动既有 C# 程序的 `--bridge` 模式；采集、SMART、设备发现、表盘切换、发送格式和重试仍使用原实现。通信使用私有标准输入/输出的逐行 JSON，不开放本机 HTTP 服务。关闭窗口隐藏到托盘，托盘退出时先停止发送和采集后台。启动时不自动连接设备。

“局域网设备”标题下方固定显示浅蓝提示：**仅支持连接与本电脑处于同一局域网的设备。** 提示和按钮不随设置区滚动；设置内容在小窗口内独立滚动。提示支持全部 19 种语言，是连接前的说明，不代表已验证当前网络可达。

沿用现有设置和日志目录。LocalToken 仅随发送请求放入当前进程内存，不写入设置；修改 IP 或退出应用后需要重新填写。显示 LCD 选项的条件保持为 Hardware 400/402。

### 开发和打包

需要 Node.js 24、Rust stable、.NET 10 SDK。Windows 需要 Visual Studio C++ x64 与 WebView2；Mac 需要 Xcode Command Line Tools。在项目根目录运行：

```powershell
./scripts/Build-Tauri.ps1
./scripts/Package-Tauri.ps1
```

本机已有 ADLX 桥时可传 `-SkipNative`。只验证程序、不生成安装包时传 `-NoBundle`；`-DebugBuild` 构建调试程序；`-SkipBackendBuild` 仅供已有匹配版本后台时使用。`-UseLld` 是 Windows 可选链接器选择，不更改系统安全策略。

默认发布包整理在 `dist/tauri-v1.3.3/`。Windows ZIP 入口是 `DivoomPCMonitor.exe`，`monitor/Divoom.HardwareMonitor.exe` 是后台。Mac DMG 入口是 `DivoomPCMonitor.app`，后台位于 `Contents/Resources/monitor/Divoom.MacMonitor`。命令行 `--probe` / `--send-test` 仍由后台程序提供。

开发热重载：先准备 `desktop/backend/`，然后在 `desktop/` 执行 `npm run tauri -- dev --no-default-features`。单独执行 `npm run dev` 只提供前端开发服务器，无法连接真实采集后台。

检查：`npm test`（在 desktop 目录，包含真实后台进程测试，需先准备 backend）；`npm run build`；C# 原有 Windows/Mac 回归；Rust/Tauri 编译。本地默认测试不会连接设备或修改现有设置。仅一次性 CI runner 设置 `DIVOOM_CI_SENDING_TESTS=1`，额外向本机回环模拟设备发送并保存临时目标设置；不要在用户环境启用该选项。

发布流程 `.github/workflows/tauri-release.yml` 分别在 Windows、Apple Silicon Mac 和 Intel Mac 运行构建和检查，收集四个软件包和 SHA-256 校验文件后统一公开 Release。推送版本标签或手动传入已存在的标签启动。标签须与 Tauri 版本一致，并先提供 `docs/releases/v版本.md` 的中英文说明。仅发布步骤失败时，可手动填写 `build_run_id` 复用已完成且三个平台构建均成功的产物；流程会校验来源，并要求已有标签与产物对应的源码提交一致，不修改标签。旧原生打包流程改为仅手动运行。

安装包附带中英文说明、.NET/第三方依赖许可证、Rust 依赖清单及可用许可证。Windows 后台仍内置 smartmontools 可执行文件、数据库、GPL 许可证及匹配源码。Mac 后台不内置 smartctl。

Mac 包和后台使用 ad-hoc 签名，尚未完成 Developer ID 签名、公证和用户实机验收。正式 Mac 发布应配置 Developer ID 签名和公证；CI 构建成功不代表已完成实际安装、温度兼容性或休眠唤醒验收。

## English

Version 1.3.0 uses Tauri 2 for Windows NSIS installers, a Windows portable ZIP, and Apple Silicon/Intel Mac DMGs. The legacy native interfaces remain available for backend development and compatibility; version tags no longer publish them automatically.

The frontend is in `desktop/`. Rust launches only the bundled C# executable in `monitor/` with `--bridge`. Existing collectors, SMART fallback, discovery and device transport are reused. Private NDJSON standard I/O connects the processes; there is no local HTTP listener. Closing hides the window to the tray; quitting stops transmission and collection. Sending never starts automatically.

A persistent pale-blue notice under the LAN devices heading states: **Only devices on the same local network as this computer can be connected.** It supports all 19 languages and stays outside the scrollable settings area. It is guidance, not the result of an automatic network test. Existing settings locations and the 400/402 LCD visibility rule are preserved. LocalToken remains session-only and is cleared on IP changes.

### Build and release

Install Node.js 24, Rust stable and .NET 10 SDK. Windows also needs Visual Studio C++ x64 tools and WebView2; Mac needs Xcode Command Line Tools. Run the two PowerShell commands above from the repository root. `-SkipNative` reuses an existing ADLX bridge; `-NoBundle` builds without installers; `-DebugBuild` selects debug output. Use `-SkipBackendBuild` only when a matching backend is already prepared. `-UseLld` optionally selects the Windows Rust LLD linker.

Packages are staged in `dist/tauri-v1.3.3/`. Run `DivoomPCMonitor.exe` from the portable ZIP. The Windows backend is `monitor/Divoom.HardwareMonitor.exe`; the Mac backend is `DivoomPCMonitor.app/Contents/Resources/monitor/Divoom.MacMonitor`. Existing command-line probes and sending tests are provided by these backend executables.

For hot reload, prepare `desktop/backend/` and run `npm run tauri -- dev --no-default-features` in desktop. A standalone Vite server has no hardware connection. Default local tests include a real child-process bridge check without device traffic or settings writes. Disposable CI runners additionally set `DIVOOM_CI_SENDING_TESTS=1` to send to a loopback mock device and save its temporary target settings; do not enable this option in a user's environment. Existing C# regressions and native Tauri builds remain part of validation.

The release workflow builds on native Windows, Apple Silicon and Intel Mac runners, verifies uploaded sizes and SHA-256 digests, and publishes all platforms together. Trigger it with a version tag or manually select an existing tag. Add bilingual notes at `docs/releases/vVERSION.md` before releasing. If only publication fails, the optional `build_run_id` input reuses a completed run after verifying all three native builds succeeded and the existing tag matches the source commit that produced those packages. It does not modify tags. Chinese and English guides, runtime and third-party licenses, Rust dependency notices and the Windows smartmontools matching source are bundled.

Mac packages use ad-hoc signing and still require Developer ID signing, notarization and physical-Mac acceptance for formal distribution. CI success does not validate every sensor, actual installation or sleep/wake behavior.

## References

- [Tauri resources](https://v2.tauri.app/develop/resources/)
- [Tauri sidecars](https://v2.tauri.app/develop/sidecar/)
- [Tauri prerequisites](https://v2.tauri.app/start/prerequisites/)
