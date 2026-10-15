param([string]$Executable = (Join-Path (Split-Path $PSScriptRoot) 'dist\DivoomPCMonitor\Divoom.HardwareMonitor.exe'))
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot
$taskEvidence = Join-Path $taskRoot ('artifacts\smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $taskEvidence -Force | Out-Null
# Prove the published package runs without the private build SDK.
$env:DOTNET_ROOT = $null
$env:DOTNET_ROOT_X64 = $null
$taskProcesses = @()
$taskKilled = @()
try {
    foreach ($taskCase in @(@{Name='degraded';Seconds=25}, @{Name='recovered';Seconds=50})) {
        $taskOutput = Join-Path $taskEvidence ($taskCase.Name + '.json')
        $taskProcess = Start-Process -FilePath $Executable -ArgumentList @('--probe', $taskCase.Seconds) -WindowStyle Hidden -PassThru -RedirectStandardOutput $taskOutput -RedirectStandardError (Join-Path $taskEvidence ($taskCase.Name + '.stderr'))
        $taskProcesses += @{Process=$taskProcess;Name=$taskCase.Name;Output=$taskOutput}
    }
    Start-Sleep -Seconds 7
    foreach ($taskCase in $taskProcesses) {
        $taskChild = Get-CimInstance Win32_Process -Filter "ParentProcessId = $($taskCase.Process.Id)" | Where-Object { $_.CommandLine -match '--sensors' } | Select-Object -First 1
        if (!$taskChild) { throw "Missing sensor child: $($taskCase.Name)" }
        $taskKilled += [int]$taskChild.ProcessId
        Stop-Process -Id $taskChild.ProcessId
    }
    foreach ($taskCase in $taskProcesses) {
        if (!$taskCase.Process.WaitForExit(55000)) { throw "Probe timed out: $($taskCase.Name)" }
        if ($taskCase.Process.ExitCode -ne 0) { throw "Probe failed: $($taskCase.Name)" }
        $taskSnapshot = Get-Content -LiteralPath $taskCase.Output -Raw | ConvertFrom-Json
        $taskCpuAge = ([DateTimeOffset]$taskSnapshot.SampledAt - [DateTimeOffset]$taskSnapshot.CpuLoad.SampledAt).TotalSeconds
        if ($null -eq $taskSnapshot.CpuLoad.Value -or $null -eq $taskSnapshot.MemoryLoad.Value -or [Math]::Abs($taskCpuAge) -gt 3) { throw 'Native metrics stopped after worker exit' }
        if ($taskCase.Name -eq 'degraded') {
            if (!($taskSnapshot.Diagnostics -match '采集进程已退出')) { throw 'Worker failure was not reported' }
            foreach ($taskDisk in $taskSnapshot.Disks) {
                if ($null -ne $taskDisk.Temperature.Value -and ([DateTimeOffset]$taskSnapshot.SampledAt - [DateTimeOffset]$taskDisk.Temperature.SampledAt).TotalSeconds -le 10) { throw 'Dead worker temperature is still marked fresh' }
            }
        } else {
            if ($taskSnapshot.Diagnostics -match '采集进程已退出') { throw 'Worker did not recover' }
            foreach ($taskDisk in $taskSnapshot.Disks) {
                if ($null -ne $taskDisk.Temperature.Value -and ([DateTimeOffset]$taskSnapshot.SampledAt - [DateTimeOffset]$taskDisk.Temperature.SampledAt).TotalSeconds -gt 10) { throw 'Recovered sensor is stale' }
            }
        }
        Write-Output "PASS: $($taskCase.Name) child crash, native continuity and temperature freshness"
    }
    Start-Sleep -Seconds 2
    foreach ($taskCase in $taskProcesses) {
        if (Get-CimInstance Win32_Process -Filter "ParentProcessId = $($taskCase.Process.Id)" | Where-Object { $_.CommandLine -match '--sensors' }) { throw 'Orphan sensor process remains' }
    }
    Write-Output "PASS: all probe children cleaned up. Evidence: $taskEvidence"
} finally {
    foreach ($taskCase in $taskProcesses) { if (!$taskCase.Process.HasExited) { Stop-Process -Id $taskCase.Process.Id } }
}
