param([string]$DotnetRoot)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot
$taskLicenses = Join-Path $taskRoot 'licenses'
New-Item -ItemType Directory -Path $taskLicenses -Force | Out-Null
$taskAssets = Get-Content (Join-Path $taskRoot 'obj\project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$taskInventory = foreach ($taskEntry in $taskAssets.libraries.GetEnumerator() | Sort-Object Key) {
    if ($taskEntry.Value.type -ne 'package') { continue }
    $taskPackage = $null
    foreach ($taskFolder in $taskAssets.packageFolders.Keys) {
        $taskCandidate = Join-Path $taskFolder $taskEntry.Value.path
        if (Test-Path -LiteralPath $taskCandidate) { $taskPackage = $taskCandidate; break }
    }
    if (!$taskPackage) { throw "Missing package: $($taskEntry.Key)" }
    $taskNuspec = Get-ChildItem -LiteralPath $taskPackage -Filter '*.nuspec' | Select-Object -First 1
    [xml]$taskSpec = Get-Content -LiteralPath $taskNuspec.FullName
    $taskMetadata = $taskSpec.package.metadata
    $taskDestination = Join-Path $taskLicenses ($taskEntry.Key.Replace('/', '-'))
    New-Item -ItemType Directory -Path $taskDestination -Force | Out-Null
    Copy-Item -LiteralPath $taskNuspec.FullName -Destination $taskDestination
    Get-ChildItem -LiteralPath $taskPackage -File | Where-Object { $_.Name -match '(?i)license|notice|copying|copyright' } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $taskDestination
    }
    [ordered]@{
        Package = $taskEntry.Key
        Sha512 = $taskEntry.Value.sha512
        LicenseType = [string]$taskMetadata.license.type
        License = [string]$taskMetadata.license.InnerText
        LicenseUrl = [string]$taskMetadata.licenseUrl
        Source = [string]$taskMetadata.repository.url
        SourceCommit = [string]$taskMetadata.repository.commit
        Copyright = [string]$taskMetadata.copyright
    }
}
$taskInventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskLicenses 'package-inventory.json') -Encoding utf8
$taskDownloads = @{
    'MPL-2.0.txt' = 'https://www.mozilla.org/media/MPL/2.0/index.txt'
    'LibreHardwareMonitor-LICENSE.txt' = 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/3d331e3370efb858411f19511373eff65a218701/LICENSE'
    'Mono-LICENSE.txt' = 'https://raw.githubusercontent.com/mono/mono/main/LICENSE'
    'ADLX SDK License Agreement.pdf' = 'https://raw.githubusercontent.com/GPUOpen-LibrariesAndSDKs/ADLX/32b5a740d42295c5dfe9026b9f52683da0f3af91/ADLX%20SDK%20License%20Agreement.pdf'
}
foreach ($taskDownload in $taskDownloads.GetEnumerator()) {
    $taskFile = Join-Path $taskLicenses $taskDownload.Key
    if (!(Test-Path -LiteralPath $taskFile)) { Invoke-WebRequest $taskDownload.Value -OutFile $taskFile }
}
foreach ($taskRuntime in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
    # Runtime packs are DownloadDependencies, not entries in assets.libraries.
    $taskVersion = [string]$taskAssets.project.frameworks.Values[0].downloadDependencies.Where({ $_.name -eq $taskRuntime })[0].version
    $taskVersion = $taskVersion.Trim('[', ']').Split(',')[0].Trim()
    foreach ($taskFolder in $taskAssets.packageFolders.Keys) {
        $taskPack = Join-Path $taskFolder "$taskRuntime\$taskVersion"
        if (Test-Path -LiteralPath $taskPack) {
            Get-ChildItem -LiteralPath $taskPack -File | Where-Object { $_.Name -match '(?i)license|notice' } | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskLicenses "$taskRuntime-$($_.Name)")
            }
            break
        }
    }
}
Write-Output "Collected $($taskInventory.Count) NuGet package records and upstream notices."
