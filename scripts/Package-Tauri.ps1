param()
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $taskRoot
$taskVersion=(Get-Content desktop/src-tauri/tauri.conf.json -Raw | ConvertFrom-Json).version
$taskOutput=Join-Path $taskRoot "dist/tauri-v$taskVersion"
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskRelease=Join-Path $taskRoot 'desktop/src-tauri/target/release'
if ($IsWindows) {
    $taskPortable=Join-Path $taskOutput 'DivoomPCMonitor'
    if (Test-Path -LiteralPath $taskPortable) { throw 'Portable folder already exists; use a fresh output directory' }
    New-Item -ItemType Directory -Path $taskPortable | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRelease 'divoom-pc-monitor.exe') -Destination (Join-Path $taskPortable 'DivoomPCMonitor.exe')
    Copy-Item -LiteralPath desktop/backend -Destination (Join-Path $taskPortable 'monitor') -Recurse
    Copy-Item -LiteralPath README.md,README.en.md,THIRD_PARTY_NOTICES.md -Destination $taskPortable
    Copy-Item -LiteralPath docs -Destination $taskPortable -Recurse
    $taskArchive=Join-Path $taskOutput "DivoomPCMonitor-win-x64-v$taskVersion.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($taskPortable,$taskArchive,[IO.Compression.CompressionLevel]::Optimal,$true)
    $taskInstaller=Get-ChildItem -LiteralPath (Join-Path $taskRelease 'bundle/nsis') -Filter '*.exe' -File
    if ($taskInstaller.Count -ne 1) { throw 'Expected one NSIS installer' }
    Copy-Item -LiteralPath $taskInstaller.FullName -Destination (Join-Path $taskOutput "DivoomPCMonitor-win-x64-v$taskVersion-setup.exe")
} else {
    $taskArch=if ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
    $taskDmg=Get-ChildItem -LiteralPath (Join-Path $taskRelease 'bundle/dmg') -Filter '*.dmg' -File
    if ($taskDmg.Count -ne 1) { throw 'Expected one Mac DMG' }
    Copy-Item -LiteralPath $taskDmg.FullName -Destination (Join-Path $taskOutput "DivoomPCMonitor-mac-$taskArch-v$taskVersion.dmg")
}
Get-ChildItem -LiteralPath $taskOutput -File | Select-Object Name,Length
