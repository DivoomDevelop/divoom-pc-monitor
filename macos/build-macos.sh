#!/bin/bash
set -euo pipefail
task_root="$(cd "$(dirname "$0")/.." && pwd)"
task_rid="${1:-osx-arm64}"
case "$task_rid" in osx-arm64|osx-x64) ;; *) echo 'Use osx-arm64 or osx-x64' >&2; exit 2 ;; esac
task_destination="$task_root/dist/DivoomMacMonitor-$task_rid-native"
if [ -e "$task_destination" ]; then echo "Output already exists: $task_destination" >&2; exit 1; fi
task_bundle="$task_destination/DivoomMacMonitor.app"
mkdir -p "$task_bundle/Contents/MacOS" "$task_bundle/Contents/Resources"
dotnet run --project "$task_root/macos/tests/Mac.Tests.csproj" -c Release
dotnet publish "$task_root/macos/Divoom.MacMonitor.csproj" -c Release -r "$task_rid" --self-contained true -o "$task_bundle/Contents/MacOS"
cp "$task_root/macos/Assets/icon.icns" "$task_bundle/Contents/Resources/"
cp "$task_root/macos/README.md" "$task_destination/README.md"
task_cache="$(dotnet nuget locals global-packages --list | sed 's/^[^:]*: //')"
mkdir -p "$task_bundle/Contents/Resources/licenses"
for task_package_dir in "$task_cache"/avalonia*/* "$task_cache"/skiasharp*/* "$task_cache"/harfbuzzsharp*/* "$task_cache"/microcom.runtime/* "$task_cache"/tmds.dbus.protocol/* "$task_cache"/microsoft.netcore.app.runtime."$task_rid"/*; do
    [ -d "$task_package_dir" ] || continue
    task_package="$(basename "$(dirname "$task_package_dir")")-$(basename "$task_package_dir")"
    mkdir -p "$task_bundle/Contents/Resources/licenses/$task_package"
    while IFS= read -r -d '' task_notice; do
        cp "$task_notice" "$task_bundle/Contents/Resources/licenses/$task_package/"
    done < <(find "$task_package_dir" -type f \( -iname '*license*' -o -iname '*notice*' -o -iname 'copying*' \) -print0)
done
cat > "$task_bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>Divoom.MacMonitor</string>
<key>CFBundleIdentifier</key><string>com.divoom.macmonitor</string>
<key>CFBundleName</key><string>Divoom Mac Monitor</string>
<key>CFBundleShortVersionString</key><string>1.0.0</string>
<key>CFBundleVersion</key><string>1.0.0</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleIconFile</key><string>icon.icns</string>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>NSHighResolutionCapable</key><true/>
<key>NSLocalNetworkUsageDescription</key><string>Send hardware data to the Divoom device you select.</string>
</dict></plist>
PLIST
chmod +x "$task_bundle/Contents/MacOS/Divoom.MacMonitor"
task_identity="${SIGNING_IDENTITY:--}"
# Sign nested Mach-O files before the bundle. '-' makes an ad-hoc local test build.
while IFS= read -r -d '' task_file; do
    if file "$task_file" | grep -q 'Mach-O'; then
        codesign --force --options runtime --entitlements "$task_root/macos/entitlements.plist" --sign "$task_identity" "$task_file"
    fi
done < <(find "$task_bundle/Contents/MacOS" -type f -print0)
codesign --force --options runtime --entitlements "$task_root/macos/entitlements.plist" --sign "$task_identity" "$task_bundle"
codesign --verify --deep --strict "$task_bundle"
if [ -n "${NOTARY_PROFILE:-}" ]; then
    if [ "$task_identity" = '-' ]; then echo 'Notarization needs SIGNING_IDENTITY' >&2; exit 1; fi
    ditto -c -k --keepParent "$task_bundle" "$task_destination/notarize.zip"
    xcrun notarytool submit "$task_destination/notarize.zip" --keychain-profile "$NOTARY_PROFILE" --wait
    xcrun stapler staple "$task_bundle"
fi
tar -czf "$task_destination.tar.gz" -C "$task_destination" DivoomMacMonitor.app README.md
echo "Ready: $task_destination.tar.gz"
