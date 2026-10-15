param([switch]$BootstrapSdk, [switch]$SkipNative, [switch]$TestOnly)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$taskSdkVersion = '10.0.401'
$taskSdkHash = '24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6efee437ab874bb7c79430'
$taskLocalDotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
$taskDotnet = if (Test-Path -LiteralPath $taskLocalDotnet) { $taskLocalDotnet } else { 'dotnet' }
if ($BootstrapSdk -and !(Test-Path -LiteralPath $taskLocalDotnet)) {
    $taskZip = Join-Path $PSScriptRoot ".tools\dotnet-sdk-$taskSdkVersion.zip"
    New-Item -ItemType Directory -Path (Split-Path $taskZip) -Force | Out-Null
    if (!(Test-Path -LiteralPath $taskZip) -or (Get-FileHash -LiteralPath $taskZip -Algorithm SHA512).Hash.ToLowerInvariant() -ne $taskSdkHash) {
        & curl.exe -fL --retry 3 -C - -o $taskZip "https://builds.dotnet.microsoft.com/dotnet/Sdk/$taskSdkVersion/dotnet-sdk-$taskSdkVersion-win-x64.zip"
        if ($LASTEXITCODE -ne 0) { throw 'SDK download failed' }
    }
    if ((Get-FileHash -LiteralPath $taskZip -Algorithm SHA512).Hash.ToLowerInvariant() -ne $taskSdkHash) { throw 'SDK SHA512 mismatch' }
    Expand-Archive -LiteralPath $taskZip -DestinationPath (Split-Path $taskLocalDotnet) -Force
    $taskDotnet = $taskLocalDotnet
}
if ($taskDotnet -eq $taskLocalDotnet) {
    $env:DOTNET_ROOT = Split-Path $taskLocalDotnet
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
}
& $taskDotnet --version
if ($LASTEXITCODE -ne 0) { throw 'Install .NET 10 SDK or run ./build.ps1 -BootstrapSdk' }
$taskSmartHashes = @{
    'smartctl.exe' = 'b5db94e5082c042be44994b7a4fa8f7b5c8e713b2ab1c9a560d8f7a7995ea27d'
    'smartmontools-7.5.tar.gz' = '690b83ca331378da9ea0d9d61008c4b22dde391387b9bbad7f29387f2595f76e'
}
foreach ($taskSmartFile in $taskSmartHashes.GetEnumerator()) {
    $taskSmartPath = Join-Path $PSScriptRoot ('third_party\smartmontools\' + $taskSmartFile.Key)
    if (!(Test-Path -LiteralPath $taskSmartPath) -or (Get-FileHash -LiteralPath $taskSmartPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskSmartFile.Value) { throw "Bundled smartmontools file missing or checksum mismatch: $taskSmartPath. See third_party/smartmontools/BUNDLE.md" }
}
if (!(Test-Path -LiteralPath 'third_party\smartmontools\doc\COPYING.txt')) { throw 'Bundled smartmontools license missing' }
if (!$SkipNative) {
    $taskCommit = '32b5a740d42295c5dfe9026b9f52683da0f3af91'
    $taskHeaders = Join-Path $PSScriptRoot '.tools\adlx'
    New-Item -ItemType Directory -Path $taskHeaders -Force | Out-Null
    $taskRemoteFiles = Invoke-RestMethod "https://api.github.com/repos/GPUOpen-LibrariesAndSDKs/ADLX/contents/SDK/Include?ref=$taskCommit"
    foreach ($taskFile in $taskRemoteFiles) {
        $taskDestination = Join-Path $taskHeaders $taskFile.name
        if (!(Test-Path -LiteralPath $taskDestination)) { Invoke-WebRequest $taskFile.download_url -OutFile $taskDestination }
    }
    $taskVswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $taskVs = if (Test-Path -LiteralPath $taskVswhere) { & $taskVswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath }
    if (!$taskVs) { throw 'ADLX bridge requires Visual Studio C++ x64 tools. Use -SkipNative only if the bridge is already built or ADLX is not required.' }
    $taskVcvars = Join-Path $taskVs 'VC\Auxiliary\Build\vcvars64.bat'
    & cmd.exe /d /c "`"$taskVcvars`" >nul && set" | ForEach-Object { if ($_ -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process') } }
    New-Item -ItemType Directory -Path 'native\bin' -Force | Out-Null
    & cl.exe /nologo /utf-8 /std:c++17 /EHsc /LD /MT /O2 /I $taskHeaders 'native\AdlxBridge.cpp' /Fo'native\bin\AdlxBridge.obj' /link /OUT:'native\bin\Divoom.AdlxBridge.dll' /IMPLIB:'native\bin\Divoom.AdlxBridge.lib'
    if ($LASTEXITCODE -ne 0) { throw 'ADLX bridge compilation failed' }
}
& $taskDotnet build 'Divoom.HardwareMonitor.csproj' -c Release
if ($LASTEXITCODE -ne 0) { throw 'Application build failed' }
& $taskDotnet run --project 'tests\Monitor.Tests.csproj' -c Release
if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed' }
if (!$TestOnly) {
    & $taskDotnet publish 'Divoom.HardwareMonitor.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o 'dist\DivoomPCMonitor'
    if ($LASTEXITCODE -ne 0) { throw 'Publication failed' }
    $taskDotnetCommand = Get-Command $taskDotnet
    & (Join-Path $PSScriptRoot 'scripts\Collect-Licenses.ps1') -DotnetRoot (Split-Path $taskDotnetCommand.Source)
    if (Test-Path -LiteralPath 'README.md') { Copy-Item -LiteralPath 'README.md' -Destination 'dist\DivoomPCMonitor\README.md' }
    if (Test-Path -LiteralPath 'THIRD_PARTY_NOTICES.md') { Copy-Item -LiteralPath 'THIRD_PARTY_NOTICES.md' -Destination 'dist\DivoomPCMonitor\THIRD_PARTY_NOTICES.md' }
    if (Test-Path -LiteralPath 'docs\VALIDATION.md') { Copy-Item -LiteralPath 'docs\VALIDATION.md' -Destination 'dist\DivoomPCMonitor\VALIDATION.md' }
    if (Test-Path -LiteralPath 'docs') { Copy-Item -LiteralPath 'docs' -Destination 'dist\DivoomPCMonitor' -Recurse -Force }
    if (Test-Path -LiteralPath 'licenses') { Copy-Item -LiteralPath 'licenses' -Destination 'dist\DivoomPCMonitor' -Recurse -Force }
    Write-Output 'Ready: dist\DivoomPCMonitor\Divoom.HardwareMonitor.exe'
}
