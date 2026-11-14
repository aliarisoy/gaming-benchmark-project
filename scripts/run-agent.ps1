<#
.SYNOPSIS
    Builds and starts the RigSight PC agent.

.DESCRIPTION
    Uses the user-local .NET SDK from scripts\setup-dev.ps1 when available.
    The agent is started elevated by default because LibreHardwareMonitor needs
    administrator rights for CPU package temperature and power. Use -NoElevate to
    run in safe mode (GPU + FPS + CPU load only).

.PARAMETER Configuration
    Debug (default) or Release.

.PARAMETER NoBuild
    Start the existing build without rebuilding.

.PARAMETER Demo
    Start a synthetic demo session without a game.

.PARAMETER NoBle
    Disable the Bluetooth peripheral and use the WebSocket link only.

.PARAMETER Port
    Override the WebSocket port for this run (default comes from the saved config).

.PARAMETER NoElevate
    Do not request UAC elevation. CPU temperature and package power will be missing.

.PARAMETER Wait
    Wait for the agent process to exit before returning.

.EXAMPLE
    .\scripts\run-agent.ps1

.EXAMPLE
    .\scripts\run-agent.ps1 -Demo -NoBle -Port 48000
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoBuild,
    [switch]$Demo,
    [switch]$NoBle,
    [ValidateRange(0, 65535)]
    [int]$Port = 0,
    [switch]$NoElevate,
    [switch]$Wait
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'pc\RigSight.sln'
$agentProject = Join-Path $repoRoot 'pc\src\RigSight.Agent\RigSight.Agent.csproj'
$agentExe = Join-Path $repoRoot "pc\src\RigSight.Agent\bin\$Configuration\net10.0-windows10.0.19041.0\RigSight.Agent.exe"

$localDotnet = Join-Path $env:LOCALAPPDATA 'RigSight\tools\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $localDotnet) {
    $dotnet = $localDotnet
} else {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw 'The .NET SDK was not found. Run .\scripts\setup-dev.ps1 first.'
    }
    $dotnet = $dotnetCommand.Source
}

if (-not $NoBuild) {
    Write-Host "Building RigSight ($Configuration)..." -ForegroundColor Cyan
    & $dotnet build $solution -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw 'The PC solution failed to build.' }
}

if (-not (Test-Path -LiteralPath $agentExe)) {
    & $dotnet build $agentProject -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $agentExe)) {
        throw "Agent executable not found: $agentExe"
    }
}

$agentArgs = @()
if ($Demo) { $agentArgs += '--demo' }
if ($NoBle) { $agentArgs += '--no-ble' }
if ($Port -gt 0) { $agentArgs += @('--port', $Port.ToString()) }

$displayArgs = if ($agentArgs.Count -gt 0) { ' ' + ($agentArgs -join ' ') } else { '' }
Write-Host "Starting $agentExe$displayArgs" -ForegroundColor Cyan

if ($NoElevate) {
    Write-Host 'Running unelevated: safe mode (no CPU package temperature/power).' -ForegroundColor Yellow
    & $agentExe @agentArgs
    exit $LASTEXITCODE
}

Write-Host 'Requesting administrator rights (UAC). Elevation is required for full CPU sensors.' -ForegroundColor Yellow
$startParams = @{
    FilePath = $agentExe
    WorkingDirectory = Split-Path -Parent $agentExe
    Verb = 'RunAs'
    PassThru = $true
}
if ($agentArgs.Count -gt 0) { $startParams.ArgumentList = $agentArgs }
$process = Start-Process @startParams

if ($Wait) {
    $process.WaitForExit()
    exit $process.ExitCode
}

Write-Host "RigSight.Agent started (PID $($process.Id)). Use the tray icon to quit." -ForegroundColor Green
