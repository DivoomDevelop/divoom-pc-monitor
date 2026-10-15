# Bundled smartmontools 7.5

This directory contains the unmodified official Windows x64 smartctl 7.5 (r5714), its drive database, documentation, GPL license and complete corresponding release source. The desktop application starts smartctl as a separate command-line process and reads its JSON output. It does not link smartmontools code, install smartd, register a service, modify PATH, or install the upstream installer.

Upstream release: https://github.com/smartmontools/smartmontools/releases/tag/RELEASE_7_5

License: GPL version 2 or, at your option, any later version. See `doc/COPYING.txt`, `doc/AUTHORS.txt` and `doc/README.txt`. The complete corresponding source is provided in this same package as `smartmontools-7.5.tar.gz`, including upstream build instructions and scripts. This component carries the upstream warranty disclaimer.

Original download URLs:

- https://github.com/smartmontools/smartmontools/releases/download/RELEASE_7_5/smartmontools-7.5.win32-setup.exe
- https://github.com/smartmontools/smartmontools/releases/download/RELEASE_7_5/smartmontools-7.5.tar.gz

SHA-256 checksums:

| File | SHA-256 |
| --- | --- |
| Original Windows installer (not redistributed) | 896337fcc253220614cf8cdbd5cf2321c5aa326a37a04160a672a281e6104c70 |
| smartctl.exe | b5db94e5082c042be44994b7a4fa8f7b5c8e713b2ab1c9a560d8f7a7995ea27d |
| smartmontools-7.5.tar.gz | 690b83ca331378da9ea0d9d61008c4b22dde391387b9bbad7f29387f2595f76e |

To refresh this bundle, download the official installer and matching source release; verify their published checksums/signatures. Extract `bin/smartctl.exe`, `bin/drivedb.h`, and the corresponding documentation with 7-Zip into this directory; do not run the installer. Update the pinned checksums in `build.ps1` and this notice together, and validate `smartctl --version` and real disk JSON reads.
