param([ValidateSet('osx-arm64','osx-x64','both')][string]$Runtime = 'both', [switch]$SkipTests, [switch]$Offline)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = 'dotnet' }
# Offline revocation lookup keeps NuGet signature checks bounded on restricted networks.
$env:NUGET_CERT_REVOCATION_MODE = 'offline'
if (!$SkipTests) {
    & $taskDotnet run --project (Join-Path $taskRoot 'macos/tests/Mac.Tests.csproj') -c Release -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Mac regression checks failed' }
}
$taskRuntimes = if ($Runtime -eq 'both') { @('osx-arm64','osx-x64') } else { @($Runtime) }
foreach ($taskRid in $taskRuntimes) {
    $taskContainer = Join-Path $taskRoot ('dist/DivoomMacMonitor-' + $taskRid)
    $taskBundle = Join-Path $taskContainer 'DivoomMacMonitor.app'
    $taskExecutableDirectory = Join-Path $taskBundle 'Contents/MacOS'
    $taskResources = Join-Path $taskBundle 'Contents/Resources'
    if ((Test-Path -LiteralPath $taskContainer) -and (Get-ChildItem -LiteralPath $taskContainer -Recurse -File | Select-Object -First 1)) { throw "Output already exists; choose a fresh output location or move the existing build first: $taskContainer" }
    New-Item -ItemType Directory -Path $taskExecutableDirectory,$taskResources -Force | Out-Null
    $taskRestoreArguments = @('restore', (Join-Path $taskRoot 'macos/Divoom.MacMonitor.csproj'), '-r', $taskRid, '-p:NuGetAudit=false')
    if ($Offline) { $taskRestoreArguments += @('--source', (Join-Path $env:USERPROFILE '.nuget/packages'), '--source', (Join-Path $taskRoot '.tools/mac-nuget')) }
    & $taskDotnet @taskRestoreArguments
    if ($LASTEXITCODE -ne 0) { throw "Mac restore failed: $taskRid" }
    & $taskDotnet publish (Join-Path $taskRoot 'macos/Divoom.MacMonitor.csproj') -c Release -r $taskRid --self-contained true --no-restore -o $taskExecutableDirectory
    if ($LASTEXITCODE -ne 0) { throw "Mac publish failed: $taskRid" }
    $taskInfo = @'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>Divoom.MacMonitor</string>
<key>CFBundleIdentifier</key><string>com.divoom.macmonitor</string>
<key>CFBundleName</key><string>Divoom Mac Monitor</string>
<key>CFBundleDisplayName</key><string>Divoom Mac Monitor</string>
<key>CFBundleShortVersionString</key><string>1.0.0</string>
<key>CFBundleVersion</key><string>1.0.0</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleIconFile</key><string>icon.icns</string>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>NSHighResolutionCapable</key><true/>
<key>NSLocalNetworkUsageDescription</key><string>Send hardware monitoring data to your selected Divoom device over the local network.</string>
<key>NSAppTransportSecurity</key><dict><key>NSAllowsLocalNetworking</key><true/></dict>
</dict></plist>
'@
    [IO.File]::WriteAllText((Join-Path $taskBundle 'Contents/Info.plist'), $taskInfo, [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $taskRoot 'macos/Assets/icon.icns') -Destination $taskResources
    Copy-Item -LiteralPath (Join-Path $taskRoot 'macos/README.md') -Destination (Join-Path $taskContainer 'README.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'macos/entitlements.plist') -Destination $taskContainer
    # Include package licenses; Windows monitoring libraries and smartctl.exe are not shipped in the Mac bundle.
    $taskLicenseDirectory = Join-Path $taskResources 'licenses'
    New-Item -ItemType Directory -Path $taskLicenseDirectory -Force | Out-Null
    $taskAssets = Get-Content (Join-Path $taskRoot 'macos/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
    foreach ($taskPackage in $taskAssets.libraries.GetEnumerator()) {
        if ($taskPackage.Value.type -ne 'package') { continue }
        foreach ($taskPackageRoot in $taskAssets.packageFolders.Keys) {
            $taskPackageDirectory = Join-Path $taskPackageRoot $taskPackage.Value.path
            if (!(Test-Path $taskPackageDirectory)) { continue }
            $taskLicenses = Get-ChildItem -LiteralPath $taskPackageDirectory -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE|THIRD.PARTY)' }
            foreach ($taskLicense in $taskLicenses) {
                $taskLicenseName = ($taskPackage.Key -replace '/', '-') + '-' + $taskLicense.Name
                if (!(Test-Path (Join-Path $taskLicenseDirectory $taskLicenseName))) { Copy-Item -LiteralPath $taskLicense.FullName -Destination (Join-Path $taskLicenseDirectory $taskLicenseName) }
            }
        }
    }
    foreach ($taskFolder in $taskAssets.packageFolders.Keys) {
        $taskRuntimePackage = Join-Path $taskFolder "microsoft.netcore.app.runtime.$taskRid/10.0.12"
        if (Test-Path $taskRuntimePackage) {
            foreach ($taskNotice in Get-ChildItem -LiteralPath $taskRuntimePackage -File | Where-Object { $_.Name -match '(?i)license|notice' }) {
                Copy-Item -LiteralPath $taskNotice.FullName -Destination (Join-Path $taskLicenseDirectory ('dotnet-' + $taskNotice.Name))
            }
        }
    }
    $taskArchive = Join-Path $taskRoot ("dist/DivoomMacMonitor-$taskRid-v1.0.0.tar.gz")
    if (Test-Path -LiteralPath $taskArchive) { throw "Archive exists: $taskArchive" }
    # Explicit Unix modes avoid losing executable permissions when packaging on Windows.
    Add-Type -AssemblyName System.Formats.Tar
    $taskFileStream = [IO.File]::Create($taskArchive)
    $taskGzip = [IO.Compression.GZipStream]::new($taskFileStream,[IO.Compression.CompressionLevel]::SmallestSize)
    $taskWriter = [System.Formats.Tar.TarWriter]::new($taskGzip,$true)
    try {
        foreach ($taskFile in Get-ChildItem -LiteralPath $taskContainer -File -Recurse) {
            $taskRelative = [IO.Path]::GetRelativePath($taskContainer,$taskFile.FullName).Replace('\','/')
            $taskEntry = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile,$taskRelative)
            $taskEntry.Mode = [IO.UnixFileMode]$(if ($taskRelative -like '*/Contents/MacOS/*') { 493 } else { 420 })
            $taskEntry.ModificationTime = [DateTimeOffset]$taskFile.LastWriteTimeUtc
            $taskInput = [IO.File]::OpenRead($taskFile.FullName)
            try { $taskEntry.DataStream = $taskInput; $taskWriter.WriteEntry($taskEntry) } finally { $taskInput.Dispose() }
        }
    } finally { $taskWriter.Dispose(); $taskGzip.Dispose(); $taskFileStream.Dispose() }
    Write-Output "Ready (requires Mac validation and Developer ID signing): $taskArchive"
}
