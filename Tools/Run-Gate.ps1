#requires -Version 5.1
<#
.SYNOPSIS
    Runs one Chicken Killer Gate: native CTest, EditMode/PlayMode tests and the
    interactive Play Mode scenario with screenshots, then prints the verdict.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Run-Gate.ps1 -Module M3
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('M0', 'M1', 'M2', 'M3', 'M3_4STEPS', 'M4', 'M5', 'M6', 'M6_4SLOTS', 'M7', 'STABILITY')]
    [string]$Module,
    [switch]$SkipTests,
    [switch]$SkipNative,
    [int]$TimeoutSeconds = 900,
    [int]$StabilitySeconds = 300
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$unity = 'D:\ProgrammingSoftware\Unity6\Editor\6000.3.26f1\Editor\Unity.exe'
$stamp = Get-Date -Format 'yyyyMMddTHHmmss'
$evidence = Join-Path $root "TestArtifacts/$Module/$stamp"
$screenshots = Join-Path $evidence 'screenshots'

Write-Host "== Gate $Module ==" -ForegroundColor Cyan
Write-Host "Evidence: $evidence"

$running = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'Unity.exe' -and $_.CommandLine -notmatch 'adb2|AssetImportWorker|UnityCLI'
}
if ($running) {
    throw "Another Unity Editor is running with a project. Close it before running a gate."
}

New-Item -ItemType Directory -Force -Path $evidence, $screenshots | Out-Null

function Invoke-BatchUnity {
    param([string[]]$Arguments, [string]$Description)
    Write-Host "> $Description" -ForegroundColor DarkGray
    $argLine = ($Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    Write-Host "  Unity $argLine" -ForegroundColor DarkGray
    $process = Start-Process -FilePath $unity -ArgumentList $argLine -PassThru -Wait
    $code = $process.ExitCode
    if ($code -ne 0) {
        Write-Host "  Unity exit code: $code" -ForegroundColor Yellow
    }
    return $code
}

function Read-NUnitCounts {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        return @{ total = 0; passed = 0; failed = 0; found = $false }
    }
    [xml]$doc = Get-Content $Path -Raw
    $root = $doc.DocumentElement
    if ($root.Name -eq 'test-run') {
        return @{
            total = [int]$root.total
            passed = [int]$root.passed
            failed = [int]$root.failed
            found = $true
        }
    }
    if ($root.Name -eq 'test-results') {
        $total = [int]$root.total
        $failed = [int]$root.failures + [int]$root.errors
        return @{ total = $total; passed = $total - $failed; failed = $failed; found = $true }
    }
    return @{ total = 0; passed = 0; failed = 0; found = $false }
}

# 1. Native build + CTest
if (-not $SkipNative) {
    Write-Host "== Native ==" -ForegroundColor Cyan
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Tools/build_native.ps1') -Configuration Debug
    if ($LASTEXITCODE -ne 0) {
        throw "Native build/CTest failed"
    }
    $ctest = Join-Path $root 'TestArtifacts/ctest-debug.xml'
    if (Test-Path $ctest) {
        Copy-Item -Force $ctest (Join-Path $evidence 'native-results.xml')
    }
}

# 2. Tests
if (-not $SkipTests) {
    Write-Host "== EditMode tests ==" -ForegroundColor Cyan
    $editXml = Join-Path $evidence 'editmode-results.xml'
    $editLog = Join-Path $evidence 'editmode.log'
    Invoke-BatchUnity -Description 'EditMode tests' -Arguments @(
        '-batchmode', '-projectPath', $root, '-runTests', '-testPlatform', 'EditMode',
        '-testResults', $editXml, '-logFile', $editLog, '-force-d3d11'
    ) | Out-Null
    $editCounts = Read-NUnitCounts $editXml
    Write-Host ("  EditMode: {0}/{1} passed, {2} failed" -f $editCounts.passed, $editCounts.total, $editCounts.failed)
    if (-not $editCounts.found -or $editCounts.failed -gt 0 -or $editCounts.total -eq 0) {
        throw "EditMode tests failed or did not run (see $editLog)"
    }

    Write-Host "== PlayMode tests ==" -ForegroundColor Cyan
    $playXml = Join-Path $evidence 'playmode-results.xml'
    $playLog = Join-Path $evidence 'playmode.log'
    Invoke-BatchUnity -Description 'PlayMode tests' -Arguments @(
        '-batchmode', '-projectPath', $root, '-runTests', '-testPlatform', 'PlayMode',
        '-testResults', $playXml, '-logFile', $playLog, '-force-d3d11'
    ) | Out-Null
    $playCounts = Read-NUnitCounts $playXml
    Write-Host ("  PlayMode: {0}/{1} passed, {2} failed" -f $playCounts.passed, $playCounts.total, $playCounts.failed)
    if (-not $playCounts.found -or $playCounts.failed -gt 0 -or $playCounts.total -eq 0) {
        throw "PlayMode tests failed or did not run (see $playLog)"
    }
}

# 3. Optional drill config (only JSON changes, code untouched)
$extraArgs = @()
if ($Module -eq 'M3_4STEPS' -or $Module -eq 'M6_4SLOTS') {
    Write-Host "== Preparing temporary drill config ==" -ForegroundColor Cyan
    $baseConfigPath = Join-Path $root 'Config/CombatPrototype.json'
    $config = Get-Content $baseConfigPath -Raw | ConvertFrom-Json
    if ($Module -eq 'M3_4STEPS') {
        $config.combo.steps = @('normal_1', 'normal_2', 'normal_3', 'normal_1')
    }
    else {
        $fourth = [pscustomobject]@{
            id               = 'sword_04'
            displayName      = '训练剑 IV'
            length           = 1.8
            radius           = 0.08
            damageMultiplier = 1.0
            color            = '#B58CFF'
            handOffset       = @(0.4, 1.1, 0.25)
        }
        $config.weapons = @($config.weapons) + $fourth
        $config.loadout.weaponSlots = 4
        $config.loadout.equipmentSlots = 4
    }
    $drillPath = Join-Path $evidence 'config-drill.json'
    $json = $config | ConvertTo-Json -Depth 32
    [System.IO.File]::WriteAllText($drillPath, $json, (New-Object System.Text.UTF8Encoding($false)))
    $extraArgs += @('-ckConfig', $drillPath)
    Write-Host "  drill config: $drillPath"
}

# 4. Interactive Play Mode gate
Write-Host "== Play Mode gate $Module ==" -ForegroundColor Cyan
$sha = ''
try { $sha = (git -C $root rev-parse HEAD 2>$null) } catch { $sha = '' }
$gateLog = Join-Path $evidence 'unity-console.log'
$gateArgs = @(
    '-projectPath', $root,
    '-executeMethod', 'CK.Editor.GateEntry.Run',
    '-ckGate', $Module,
    '-ckEvidence', $evidence,
    '-ckGitCommit', $sha,
    '-ckTimeout', '840',
    '-force-d3d11',
    '-logFile', $gateLog
) + $extraArgs
if ($Module -eq 'STABILITY') {
    $gateArgs += @('-ckDuration', "$StabilitySeconds")
}

$argLine = ($gateArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
Write-Host "> Unity $argLine" -ForegroundColor DarkGray
$process = Start-Process -FilePath $unity -ArgumentList $argLine -PassThru
$timeoutMs = $TimeoutSeconds * 1000
if (-not $process.WaitForExit($timeoutMs)) {
    Write-Host "Gate timed out after $TimeoutSeconds s; terminating Unity" -ForegroundColor Red
    try { $process.Kill() } catch { }
    Start-Sleep -Seconds 3
    throw "Gate $Module timed out"
}
Write-Host "  Unity gate exit code: $($process.ExitCode)" -ForegroundColor DarkGray

# 5. Summary
$summaryPath = Join-Path $evidence 'summary.json'
if (-not (Test-Path $summaryPath)) {
    throw "Gate did not produce summary.json: $summaryPath"
}
$summary = Get-Content $summaryPath -Raw | ConvertFrom-Json
$shotCount = @($summary.screenshots).Count
Write-Host ""
Write-Host "Module : $($summary.module)" -ForegroundColor Cyan
Write-Host "Status : $($summary.status)" -ForegroundColor $(if ($summary.status -eq 'PASS') { 'Green' } else { 'Red' })
Write-Host "Verdict: $($summary.verdict)"
Write-Host "Shots  : $shotCount"
Write-Host "Errors : $($summary.consoleErrors)"
Write-Host "Summary: $summaryPath"
if ($summary.failures) {
    Write-Host "Failures:" -ForegroundColor Red
    $summary.failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
}
if ($summary.status -ne 'PASS') {
    exit 1
}
