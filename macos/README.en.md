# Divoom Mac Monitor

[简体中文](README.md) | [English](README.en.md)

A macOS hardware monitor with separate self-contained Apple Silicon (`osx-arm64`) and Intel (`osx-x64`) packages. No separate .NET installation is needed. The technical minimum is macOS 14; use a macOS release still supported by Apple and .NET.

This is a beta. Windows cross-builds, offline parsers and the shared protocol have been checked. Physical-Mac testing, Developer ID signing and notarization have not been completed.

## Download and run

Download the matching architecture from [GitHub Releases](https://github.com/DivoomDevelop/divoom-pc-monitor/releases/latest). Extract the `.tar.gz`, copy `DivoomMacMonitor.app` to Applications and open it. Packages preserve executable permissions. Gatekeeper may block unsigned beta packages. Formal distribution should use the signing and notarization procedure below; disabling system security is not required.

## Metrics

- CPU model and utilization use sysctl and Mach tick differences. Memory includes anonymous application pages minus purgeable pages, wired pages and physically compressed pages; its definition differs from Windows.
- GPU model and utilization use IOKit PerformanceStatistics. CPU/GPU temperatures use read-only AppleSMC access, with separate Intel and Apple Silicon sensor key classification. Unsupported or inaccessible readings display `--`; temperatures are not guessed when multiple GPUs cannot be associated reliably.
- Disk models and volume capacity use diskutil and system volume information. Disk activity is the change in cumulative read/write service time, potentially including parallel requests; it differs from Windows idle-time accounting. Missing driver counters display `--`.
- Disk temperature fallback searches for macOS smartctl: first `Contents/MacOS/tools/smartctl`, then Homebrew `sbin/bin` paths under `/opt/homebrew` and `/usr/local`. macOS smartctl is not bundled. Install smartmontools separately or select its path in Collection Settings. Polling is every 30 seconds, with a five-second command timeout and a 12-second SMART enumeration budget. The application does not elevate automatically.
- Collection runs in an isolated process, restarted after 30 seconds without a response. Stale values are not sent as valid readings. Closing the window hides it; restore or quit through the menu-bar icon.

## Device connection

Windows and Mac compile the same `Transport.cs`. Models 90/92/400 use `IP:80/post`; other models use `IP:9000/divoom_api`. Models 400/402 use six-string ScreenList/LcdId payloads, with 402 still on port 9000. Models such as 530 use DeviceId/AppName/DispData. Metric ordering matches Windows.

Models 90/92/96/400/402 select clock 625, model 530 selects 3191, and other models skip clock selection. PC requests omit ClockId by default; firmware explicitly requiring it is retried with the mapped clock ID.

LCD selection and independent mode appear only for Hardware 400 or 402.

LocalToken is optional. Find it in **Divoom APP → Wi-Fi device list → Settings for the selected device → Local token**. Enter its numeric value. Clock and PC data requests send a JSON number named `LocalToken`, not `DeviceToken`. It is kept only for the current session, cleared when IP changes, and excluded from settings and diagnostic request details.

## Language and logs

The first launch follows the system language, falling back to English. Settings supports system language or English, Simplified Chinese, Traditional Chinese, Japanese, German, Thai, Russian, Korean, Vietnamese, French, Turkish, Spanish, Portuguese, Indonesian, Italian, Ukrainian, Swedish, Danish and Polish. Save and restart to apply changes. Hardware names, protocol identifiers and original errors retain their original text.

Settings and rotating logs are in `~/Library/Application Support/Divoom/HardwareMonitor/`. **Diagnostics → Export diagnostic report** includes system/architecture details, SMC candidate keys, metric sources, errors and recent logs. It does not upload automatically.

## Build, sign and test

Windows cross-builds require PowerShell 7 and .NET 10 SDK:

```powershell
./scripts/Package-Mac.ps1
```

The script builds both architectures, bundles runtimes and licenses, and includes both Chinese and English guides. Native calls and sensor readings still require testing on Macs.

On a Mac, build with `bash macos/build-macos.sh osx-arm64`, or use `osx-x64` for Intel. Default ad-hoc signing is for local testing. For distribution, set SIGNING_IDENTITY to your Developer ID Application certificate and NOTARY_PROFILE to an existing notarization keychain profile. The script does not create certificates, purchase accounts or change security policies.

Local probe, without network access:

```sh
DivoomMacMonitor.app/Contents/MacOS/Divoom.MacMonitor --probe 10
```

LAN test: `--send-test IPv4 Hardware Seconds DeviceId`. Provide a token through the process environment variable `DIVOOM_LOCAL_TOKEN` rather than putting it in command arguments. The test really selects the whitelisted clock and sends PC data, then stops automatically.

Run checks with `dotnet run --project macos/tests/Mac.Tests.csproj -c Release`. They cover CPU tick overflow, Darwin memory fields/page size, SMC encoding and classification, plist/SMART parsing, 19 language resources, shared endpoints/clocks/payloads and stale readings. Physical Intel and Apple Silicon testing is still needed for metrics, menu-bar behavior, local network permissions and signed installation.

Dependencies include Avalonia/Fluent (MIT), SkiaSharp/HarfBuzzSharp and .NET. Available licenses are copied into `Contents/Resources/licenses/`. The SMC/IOKit adapters are project code and do not copy third-party monitoring application implementations.
