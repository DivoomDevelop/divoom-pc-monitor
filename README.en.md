# Divoom PC Hardware Monitor

[简体中文](README.md) | [English](README.en.md)

A Windows 10/11 x64 and macOS 14+ desktop hardware monitor. Version 1.3.0 uses Tauri 2 for the shared interface and installers, with the existing C# sensor and device-transport backend. Local monitoring works independently of device connections and cloud access.

Current application: **v1.3.0**. Separate Apple Silicon and Intel Mac beta packages share the device transport. See the [Mac guide](macos/README.en.md) and [Tauri build guide](docs/TAURI.md).

## Download and run

Download a ready-to-run package from [GitHub Releases](https://github.com/DivoomDevelop/divoom-pc-monitor/releases/latest). The automatically generated Source code ZIP contains source files, not the application.

| Platform | Download | Start |
| --- | --- | --- |
| Windows 10/11 x64 | [Tauri installer v1.3.0](https://github.com/DivoomDevelop/divoom-pc-monitor/releases/download/v1.3.0/DivoomPCMonitor-win-x64-v1.3.0-setup.exe) | Install and open DivoomPCMonitor |
| Windows 10/11 x64 | [Portable ZIP v1.3.0](https://github.com/DivoomDevelop/divoom-pc-monitor/releases/download/v1.3.0/DivoomPCMonitor-win-x64-v1.3.0.zip) | Extract everything and run `DivoomPCMonitor/DivoomPCMonitor.exe` |
| macOS 14+ Apple Silicon | [ARM64 DMG v1.3.0](https://github.com/DivoomDevelop/divoom-pc-monitor/releases/download/v1.3.0/DivoomPCMonitor-mac-arm64-v1.3.0.dmg) | Open the DMG and drag the `.app` into Applications |
| macOS 14+ Intel | [Intel DMG v1.3.0](https://github.com/DivoomDevelop/divoom-pc-monitor/releases/download/v1.3.0/DivoomPCMonitor-mac-x64-v1.3.0.dmg) | Open the DMG and drag the `.app` into Applications |

Packages include the .NET runtime. Keep the complete application directory. Each release includes SHA-256 checksums. Mac packages have not completed Developer ID signing, notarization, or testing on physical Macs; Gatekeeper may block them. See the Mac guide.

## Usage

- The connection card always shows: **Only devices on the same local network as this computer can be connected.** The notice is localized into all 19 languages and does not indicate an automatic connectivity check.
- Keep the complete portable directory, including `monitor/`. Windows needs WebView2; the Tauri installer installs it if missing.
- The dashboard shows CPU/GPU models, utilization and temperature, memory usage, physical disk activity and temperature, and volume capacity usage.
- The default sources are the discrete GPU and system disk. Change them using **GPU to send** and **Disk to send**.
- **Refresh devices** retrieves the device list and fills in Hardware. Alternatively, enter the device IPv4 address and Hardware manually. Models 90, 92 and 400 use port 80; all others use port 9000. The selected endpoint is displayed and Hardware is saved.
- TimeGate five-screen controls, LCD 1–5 and independent mode appear only for Hardware 400 or 402. They are hidden and their rows collapse for other models. Device selection can read the LCD mode; manual IP settings also work offline.
- **Start sending** selects clock 625 for models 90/92/96/400/402, or clock 3191 for 530. Other models skip clock selection. PC data is then sent every two seconds. Stopping transmission keeps local monitoring running.
- Closing the window hides it to the tray. Double-click the tray icon to restore it, or choose **Exit** to quit. Startup launch is disabled by default; sending does not resume automatically.
- The first launch follows the system language, with English as the fallback. Simplified and Traditional Chinese are distinguished. Collection Settings supports system language or 19 languages: English, Simplified Chinese, Traditional Chinese, Japanese, German, Thai, Russian, Korean, Vietnamese, French, Turkish, Spanish, Portuguese, Indonesian, Italian, Ukrainian, Swedish, Danish and Polish. Language changes apply immediately after saving in the Tauri interface. Hardware names, protocol identifiers, original system/device errors and diagnostic details retain their original text.
- `LocalToken` is optional. Find it in **Divoom APP → Wi-Fi device list → Settings for the selected device → Local token**. Enter a nonnegative integer; it is sent as a JSON number in clock and data requests. Leaving it empty omits the field. Input is masked, kept only for the current session, cleared when the IP changes, and excluded from settings and request diagnostics. An error saying `DeviceToken is err` refers to token authentication; the actual request field is still `LocalToken`.

## Temperature and compatibility

1. Native Windows PDH, DXGI and GlobalMemoryStatusEx provide basic metrics. English counter APIs avoid dependence on the system display language.
2. LibreHardwareMonitorLib 0.9.6 reads sensors and includes NVIDIA NVAPI/NVML and Intel IGCL integrations.
3. The bundled `Divoom.AdlxBridge.dll` supplements supported AMD metrics by loading the driver's `amdadlx64.dll` only from the system directory. Other collection continues when no AMD adapter exists.
4. Low-level CPU sensors may require PawnIO and administrator rights. Open the [official PawnIO website](https://pawnio.eu/) from Collection Settings, install the official signed release and rescan sensors. Use **Restart as administrator** if needed. This application does not install drivers or change Windows security settings automatically.
5. Official [smartmontools 7.5](https://github.com/smartmontools/smartmontools/releases/tag/RELEASE_7_5) x64 `smartctl.exe` and the drive database are bundled. Automatic fallback requires no installation or path selection. Valid primary readings are retained; only physical disks with missing or stale temperatures are queried. Polling is every 30 seconds, with a five-second per-disk timeout and a 12-second batch budget. Some drives require administrator rights; RAID and USB support depends on the controller.

**Collection Settings → Disk temperature fallback** offers Automatic (default), Disable and Custom path. Automatic mode uses `third_party/smartmontools/smartctl.exe` relative to the published executable. Existing custom paths are preserved. Legacy default-off settings migrate to Automatic; an explicit Disable choice remains disabled.

smartctl runs as a separate process and returns JSON. It does not install a service, run an installer or modify PATH. Its GPL license, notices and complete matching upstream source archive are included in `third_party/smartmontools/`; retain that directory when distributing the application.

Unavailable or stale readings display and transmit `--`, never a fabricated zero. CPU temperature prefers Package/Tdie, with the highest core temperature as a fallback; ACPI thermal zones are not treated as CPU temperature. GPU utilization is the busiest engine per adapter. Vendor core-load fallbacks identify their source and may differ from Task Manager.

When identical PCI model/subsystem identities prevent reliable GPU temperature association, the temperature remains unavailable. Capacity belongs to volumes; activity and temperature belong to physical disks. A volume spanning multiple disks is not forced onto one disk.

Sensor collection runs in an isolated child process. After 30 seconds without a response, the worker is terminated and restarted with backoff while the UI and native Windows metrics continue.

## Device protocol

Clock selection and PC data use HTTP POST with `Channel/SetClockSelectId` and `Device/UpdatePCParaInfo`.

| Hardware | Endpoint |
| --- | --- |
| 90, 92, 400 | `http://DEVICE_IP:80/post` |
| All other models, including 530 | `http://DEVICE_IP:9000/divoom_api` |

Port 80 may be omitted when a URL is displayed. Hardware 0/unknown follows the second rule; enter the actual Hardware when using a manual IP.

Requests buffer UTF-8 JSON and explicitly set Content-Length to support firmware that cannot read chunked requests. Desktop and diagnostic sending use the same transport.

The clock whitelist is independent of the port rule: 90/92/96/400/402 → 625; 530 → 3191; other models skip selection. Retries respect this whitelist. Models 96 and 402 still use port 9000.

Data format is also independent of the port: **400 and 402 both use ScreenList/LcdId, while 402 uses port 9000**. Do not use the 530 format for every port-9000 device.

The API format used by 530 and similar models includes `DeviceId`, `AppName` and a `DispData` array with `AppTitle/AppData`. PC requests omit `ClockId` by default. When older firmware explicitly reports a parameter-format error, a mapped model retries with its own clock ID (3191 for 530); success is remembered for that device during the session. Unmapped models do not add this retry. The API format needs a valid DeviceId, filled by device selection or entered manually. Changing IP clears DeviceId and LocalToken.

The legacy format retains `ScreenList[0].LcdId` (0–4) and six strings. Both formats send: CPU utilization, GPU utilization, CPU temperature, GPU temperature, memory utilization and disk temperature. Percentages include `%`, temperatures include `C`, and unavailable values are `--`. Volume capacity and disk activity are not added to the legacy payload.

Failures back off for 2, 4, 8, 16 and 30 seconds. Only the newest snapshot is sent; expired queued data is not replayed. HTTP and device errors appear in the status line.

## Build, test and release

The default Tauri build requires Node.js 24, Rust stable and .NET 10 SDK, plus Visual Studio C++ x64 tools on Windows. Run `./scripts/Build-Tauri.ps1` then `./scripts/Package-Tauri.ps1`. See the [Tauri guide](docs/TAURI.md). The following compatibility script builds the C# backend and legacy native interface for backend development and diagnostics:

```powershell
./build.ps1 -BootstrapSdk
```

The script downloads and verifies a pinned SDK in `.tools`, downloads AMD headers from a fixed commit, builds the bridge and application, runs regression checks and publishes a self-contained directory. Omit `-BootstrapSdk` when an SDK is already installed. Use `-SkipNative` only when the bridge already exists or ADLX is intentionally unnecessary.

Bundled smartmontools executable and source SHA-256 hashes are checked before publishing, and licenses are copied. See its `BUNDLE.md` for updates.

The GitHub Actions release workflow builds and uploads Windows and both Mac packages with SHA-256 checksums. Push a version tag matching the Windows project version, or run the workflow manually with that tag. Assets are verified before publishing the release.

Local diagnostics, without device access:

```powershell
./dist/DivoomPCMonitor/Divoom.HardwareMonitor.exe --probe 10
```

Capture or redirect command-line output from the WinExe. Diagnostics can also be exported as JSON from the UI.

LAN test: `--send-test IPv4 Hardware [Seconds, default 20] [DeviceId]`. This actually selects a whitelisted clock and sends PC data, then stops automatically. It uses the desktop collection and sending code and reports requests, responses, return codes and success counts. API devices on port 9000 need DeviceId.

Append `--untraced` to use the default transport without request recording when comparing desktop behavior. It reports status and success counts without Exchanges details.

## Commercial SDK providers

HWiNFO/CPUID SDKs have not been purchased or integrated. The public `IHardwareProvider` interface accepts `SensorFrame` for independently implemented, licensed adapters.

- Reference `Divoom.HardwareMonitor.dll` from a .NET 10 Windows x64 class library. Expose a public parameterless `IHardwareProvider` implementation.
- Name the output `*.Provider.dll` and put it, dependencies and `deps.json` in the published `providers/` directory.
- `Read()` returns CPU/GPU/disk metrics. GPU MatchKey uses uppercase hexadecimal `VEN:DEV:SUBSYS`; disk IDs use physical disk indexes as `disk/N`. Include genuine timestamps, units, sources and errors.
- Return null for unavailable fields. Do not associate devices by list order. Release SDK resources in `Dispose()`.
- Providers are disabled by default. Enable SDK providers and save to load them in the isolated sensor process.

## Local data and validation

Settings and rotating logs are stored in `%LOCALAPPDATA%/Divoom/HardwareMonitor/`. Diagnostic files are created only on export; the application does not automatically upload hardware information. Device discovery uses the original tool's cloud endpoint; PC data goes directly to the entered device IP.

Regression checks cover percentage boundaries, temperature selection, multi-GPU matching and load aggregation, SMART status codes, protocol fields, HTTP/device errors, cancellation and backoff.

See [validation scope](docs/VALIDATION.md). Detailed local diagnostic records and runtime logs are excluded from the repository. Unconnected hardware, unavailable commercial SDKs and untested physical devices are not considered validated. Final Windows layout checks at all window sizes/DPI settings remain pending; Mac hardware, installation, signing and notarization require physical-Mac validation.
