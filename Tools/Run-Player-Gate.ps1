#requires -Version 5.1
<#
.SYNOPSIS
    运行 Windows Player Gate（M0/M7/STABILITY）并写入证据。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Run-Player-Gate.ps1 -Module M7
#>
param(
    [ValidateSet('Mono', 'IL2CPP')]
    [string]$Build = 'IL2CPP',
    [ValidateSet('M0', 'M7', 'STABILITY')]
    [string]$Module = 'M7',
    [int]$Duration = 0,
    [int]$TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$buildDir = if ($Build -eq 'Mono') { 'Build/Win64-Mono' } else { 'Build/Win64' }
$exe = Join-Path $root "$buildDir/ChickenKiller.exe"
if (-not (Test-Path $exe)) {
    throw "Player not found: $exe. Run the BuildScript for $Build first."
}
$stamp = Get-Date -Format 'yyyyMMddTHHmmss'
$evidence = Join-Path $root "TestArtifacts/$Module/$Build-player-$stamp"
New-Item -ItemType Directory -Force -Path $evidence, (Join-Path $evidence 'screenshots') | Out-Null
$sha = ''
try { $sha = (git -C $root rev-parse HEAD 2>$null) } catch { $sha = '' }

$arguments = @(
    '-ckGate', $Module,
    '-ckEvidence', "`"$evidence`"",
    '-ckGitCommit', $sha,
    '-screen-width', '1280',
    '-screen-height', '720',
    '-screen-fullscreen', '0',
    '-logFile', "`"$evidence\player.log`""
)
if ($Duration -gt 0) {
    $arguments += @('-ckDuration', "$Duration")
}
$argLine = $arguments -join ' '
Write-Host "Launching $exe $argLine"
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.Arguments = $argLine
$psi.WorkingDirectory = Split-Path $exe
$psi.UseShellExecute = $false
$process = [System.Diagnostics.Process]::Start($psi)
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Write-Host "Player gate timed out; killing process" -ForegroundColor Red
    try { $process.Kill() } catch { }
    throw "Player gate $Module timed out"
}
Write-Host "Player exit code: $($process.ExitCode)"
$summaryPath = Join-Path $evidence 'summary.json'
if (-not (Test-Path $summaryPath)) {
    throw "No summary.json produced: $evidence"
}
$summary = [System.IO.File]::ReadAllText($summaryPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
Write-Host "Module : $($summary.module)"
Write-Host "Status : $($summary.status)"
Write-Host "Shots  : $(@($summary.screenshots).Count)"
Write-Host "Res    : $($summary.resolution)   DX: $($summary.graphicsDevice)"
Write-Host "Summary: $summaryPath"
if ($summary.consoleErrors -gt 0) {
    Write-Host "Console errors: $($summary.consoleErrors)" -ForegroundColor Red
}
if ($summary.status -ne 'PASS') {
    exit 1
}
