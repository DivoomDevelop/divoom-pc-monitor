# Third-party components

The application uses the following components. Their notices and license terms remain applicable.

| Component | Version / reference | Upstream and license |
| --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor — MPL-2.0; see upstream third-party notices for its included interop and modules |
| AMD ADLX SDK headers | commit 32b5a740d42295c5dfe9026b9f52683da0f3af91 | https://github.com/GPUOpen-LibrariesAndSDKs/ADLX — AMD ADLX SDK License Agreement, included in licenses/ |
| .NET Windows Desktop Runtime | 10.0.12 | https://github.com/dotnet — runtime LICENSE.txt / ThirdPartyNotices.txt distributed with the published runtime |
| System.Management and other Microsoft packages | resolved by NuGet | https://github.com/dotnet/runtime — MIT |
| DiskInfoToolkit, HidSharp, RAMSPDToolkit-NDD and transitive packages | resolved by NuGet | Consult licenses/package-inventory.json and copied package license files |
| smartmontools / smartctl | 7.5 r5714, official unmodified Windows x64 executable | https://github.com/smartmontools/smartmontools/releases/tag/RELEASE_7_5 — GPL-2.0-or-later; license, notices, build documentation and complete corresponding source archive are included in third_party/smartmontools/ |

The AMD bridge contains this project's own C ABI implementation and uses AMD interface declarations. AMD's driver DLL is provided by the installed graphics driver and is not redistributed here.

The MPL-covered libraries are distributed unmodified. Corresponding upstream source locations and exact commits, when provided by the packages, are recorded in licenses/package-inventory.json; source is available from those repositories under MPL-2.0. The licenses directory includes package metadata, the MPL text, upstream notices, and .NET runtime-pack notices. The inventory includes restore-time dependencies for other operating systems that are not all shipped in this Windows package.

smartctl runs as a separate command-line process and communicates through its standard JSON output. No smartmontools code is linked into the desktop application. See third_party/smartmontools/BUNDLE.md for exact source and checksums, doc/COPYING.txt for its GPL terms, and smartmontools-7.5.tar.gz for the corresponding source and build scripts. Preserve these files when redistributing this package.

PawnIO, HWiNFO SDK, and CPUID SDK are not included in the distribution. Optional separately installed products remain subject to their own license terms. The application does not include commercial SDK credentials or license keys.
