#requires -Version 5.1
<#
.SYNOPSIS
    Builds the Chicken Killer native combat core and installs the editor Player plugin.

.DESCRIPTION
    Configures Native/ with CMake (Visual Studio 2022, x64), builds ck_combat.dll,
    copies it to Assets/Plugins/Windows/x86_64 and runs CTest.

.PARAMETER Configuration
    Debug (default) or Release.

.PARAMETER SkipTests
    Skip the CTest run after building.

.EXAMPLE
    powershell -NoProfile -File Tools/build_native.ps1 -Configuration Debug
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$nativeDir = Join-Path $root 'Native'
$buildDir = Join-Path $nativeDir 'build'
$pluginDir = Join-Path $root 'Assets/Plugins/Windows/x86_64'
$configLower = $Configuration.ToLowerInvariant()

Write-Host "== Chicken Killer native build ($Configuration) ==" -ForegroundColor Cyan

if (-not (Test-Path $pluginDir)) {
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
}

function Invoke-Checked {
    param([string]$FilePath, [string[]]$Arguments)
    Write-Host "> $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

Invoke-Checked cmake @(
    '-S', $nativeDir,
    '-B', $buildDir,
    '-G', 'Visual Studio 17 2022',
    '-A', 'x64'
)

Invoke-Checked cmake @(
    '--build', $buildDir,
    '--config', $Configuration,
    '--parallel'
)

$builtDll = Join-Path $buildDir "$Configuration/ck_combat.dll"
if (-not (Test-Path $builtDll)) {
    throw "Expected DLL not found: $builtDll"
}
Copy-Item -Force $builtDll (Join-Path $pluginDir 'ck_combat.dll')

$builtPdb = Join-Path $buildDir "$Configuration/ck_combat.pdb"
if (Test-Path $builtPdb) {
    Copy-Item -Force $builtPdb (Join-Path $pluginDir 'ck_combat.pdb')
}

$testExe = Join-Path $buildDir "$Configuration/ck_native_tests.exe"
Write-Host "DLL: $builtDll" -ForegroundColor Green
Write-Host "Installed: $pluginDir/ck_combat.dll" -ForegroundColor Green

if (-not $SkipTests) {
    Write-Host "== CTest ==" -ForegroundColor Cyan
    $junit = Join-Path $root "TestArtifacts/ctest-$configLower.xml"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $junit) | Out-Null
    Push-Location $buildDir
    try {
        & ctest -C $Configuration --output-on-failure --output-junit $junit
        if ($LASTEXITCODE -ne 0) {
            throw "CTest failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
    if (Test-Path $testExe) {
        & $testExe
        if ($LASTEXITCODE -ne 0) {
            throw "Native test executable failed with exit code $LASTEXITCODE"
        }
    }
}
