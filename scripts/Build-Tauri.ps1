param([switch]$SkipNative, [switch]$SkipBackendBuild, [switch]$NoBundle, [switch]$DebugBuild, [switch]$UseLld)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $taskRoot
$taskDesktop=Join-Path $taskRoot 'desktop'
$taskBackend=Join-Path $taskDesktop 'backend'
$taskDotnet=Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $taskDotnet)) { $taskDotnet='dotnet' }
$taskCargo=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.cargo/bin'
if ($IsWindows -and (Test-Path -LiteralPath $taskCargo)) { $env:PATH=$taskCargo+[IO.Path]::PathSeparator+$env:PATH }
if (!$env:RUSTUP_TOOLCHAIN) { $env:RUSTUP_TOOLCHAIN='stable' }
function Assert-Tool([string]$taskAction) { if ($LASTEXITCODE -ne 0) { throw "$taskAction failed (exit $LASTEXITCODE)" } }
if (!$SkipBackendBuild) {
    if ($IsWindows) {
        & (Join-Path $taskRoot 'build.ps1') -SkipNative:$SkipNative
        Assert-Tool 'Windows backend build'
        New-Item -ItemType Directory -Path $taskBackend -Force | Out-Null
        Copy-Item -Path (Join-Path $taskRoot 'dist/DivoomPCMonitor/*') -Destination $taskBackend -Recurse -Force
    } else {
        & $taskDotnet run --project macos/tests/Mac.Tests.csproj -c Release
        Assert-Tool 'Mac regression checks'
        $taskRid=if ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { 'osx-arm64' } else { 'osx-x64' }
        & (Join-Path $taskRoot 'scripts/Package-Mac.ps1') -Runtime $taskRid -SkipTests
        Assert-Tool 'Mac backend build'
        $taskVersion=([xml](Get-Content macos/Divoom.MacMonitor.csproj -Raw)).Project.PropertyGroup.Version
        $taskContainer=Join-Path $taskRoot "dist/DivoomMacMonitor-$taskRid-v$taskVersion"
        New-Item -ItemType Directory -Path $taskBackend -Force | Out-Null
        Copy-Item -Path (Join-Path $taskContainer 'DivoomMacMonitor.app/Contents/MacOS/*') -Destination $taskBackend -Recurse -Force
        Copy-Item -LiteralPath (Join-Path $taskContainer 'DivoomMacMonitor.app/Contents/Resources/licenses') -Destination $taskBackend -Recurse -Force
        Copy-Item -LiteralPath macos/README.md,macos/README.en.md -Destination $taskBackend
        chmod 755 (Join-Path $taskBackend 'Divoom.MacMonitor')
    }
}
# npm.cmd is used on Windows; npm is used on macOS.
$taskNpm=if ($IsWindows) { 'npm.cmd' } else { 'npm' }
Push-Location -LiteralPath $taskDesktop
try {
    & $taskNpm ci --ignore-scripts
    Assert-Tool 'Frontend dependency restore'
    & $taskNpm test
    Assert-Tool 'Desktop regression checks'
    node scripts/collect-licenses.mjs
    Assert-Tool 'Tauri license collection'
    if (!$IsWindows) {
        foreach ($taskFile in Get-ChildItem -LiteralPath $taskBackend -Recurse -File) {
            $taskKind=& /usr/bin/file -b $taskFile.FullName
            if ($taskKind -like '*Mach-O*') {
                & codesign --force --entitlements (Join-Path $taskRoot 'macos/entitlements.plist') --sign - $taskFile.FullName
                Assert-Tool 'Mac backend ad-hoc signing'
            }
        }
        node --test tests/bridge.test.mjs
        Assert-Tool 'Signed Mac backend smoke test'
    }
    if ($UseLld -and $IsWindows) {
        $taskVswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        $taskVs=& $taskVswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        $taskVcvars=Join-Path $taskVs 'VC/Auxiliary/Build/vcvars64.bat'
        & cmd.exe /d /c "`"$taskVcvars`" >nul && set" | ForEach-Object { if ($_ -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($matches[1],$matches[2],'Process') } }
        $taskSysroot=& rustc --print sysroot
        $env:CARGO_TARGET_X86_64_PC_WINDOWS_MSVC_LINKER=Join-Path $taskSysroot 'lib/rustlib/x86_64-pc-windows-msvc/bin/rust-lld.exe'
        $env:RUSTFLAGS='-C linker-flavor=lld-link'
    }
    if (!$env:CARGO_BUILD_JOBS) { $env:CARGO_BUILD_JOBS='2' }
    $taskArguments=@('run','tauri','--','build')
    if ($NoBundle) { $taskArguments+='--no-bundle' }
    else { $taskArguments+=@('--bundles',$(if ($IsWindows) { 'nsis' } else { 'dmg' })) }
    if ($DebugBuild) { $taskArguments+='--debug' }
    $taskArguments+=@('--','--locked')
    & $taskNpm @taskArguments
    Assert-Tool 'Tauri application build'
    if (!$IsWindows -and !$NoBundle) {
        $taskPreviousBackend=$env:DIVOOM_BACKEND
        try {
            $env:DIVOOM_BACKEND=Join-Path $taskDesktop 'src-tauri/target/release/bundle/macos/DivoomPCMonitor.app/Contents/Resources/monitor/Divoom.MacMonitor'
            node --test tests/bridge.test.mjs
            Assert-Tool 'Bundled Mac backend smoke test'
        } finally { $env:DIVOOM_BACKEND=$taskPreviousBackend }
    }
} finally { Pop-Location }
