<#
.SYNOPSIS
    Creates Desktop and Start Menu shortcuts for the RigSight PC agent.

.DESCRIPTION
    Shortcuts point at the built agent executable. Because the executable's
    manifest requests administrator rights, Windows shows the normal UAC prompt
    when the shortcut is opened; no special shortcut configuration is required.

    Run .\scripts\run-agent.ps1 at least once (or build the solution) before
    creating the shortcuts. Re-run this script after switching Debug/Release or
    after moving the repository.

.PARAMETER Configuration
    Which build the shortcuts should launch: Debug (default) or Release.

.PARAMETER NoDesktop
    Do not create the Desktop shortcut.

.PARAMETER NoStartMenu
    Do not create the Start Menu shortcut.

.PARAMETER Remove
    Remove the RigSight shortcuts instead of creating them.

.EXAMPLE
    .\scripts\install-shortcuts.ps1

.EXAMPLE
    .\scripts\install-shortcuts.ps1 -Configuration Release

.EXAMPLE
    .\scripts\install-shortcuts.ps1 -Remove
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoDesktop,
    [switch]$NoStartMenu,
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$binRoot = Join-Path $repoRoot "pc\src\RigSight.Agent\bin\$Configuration"

$locations = @()
if (-not $NoDesktop) {
    $locations += [Environment]::GetFolderPath('Desktop')
}
if (-not $NoStartMenu) {
    $locations += [Environment]::GetFolderPath('Programs')
}

if ($locations.Count -eq 0) {
    throw 'Both shortcuts are disabled; nothing to do.'
}

if ($Remove) {
    foreach ($directory in $locations) {
        $link = Join-Path $directory 'RigSight.lnk'
        if (Test-Path -LiteralPath $link) {
            Remove-Item -LiteralPath $link -Force
            Write-Host "Removed $link"
        } else {
            Write-Host "Not present: $link"
        }
    }
    return
}

$exe = Get-ChildItem -LiteralPath $binRoot -Filter 'RigSight.Agent.exe' -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\net[^\\]+-windows' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $exe) {
    throw "RigSight.Agent.exe was not found under $binRoot. Build it first with .\scripts\run-agent.ps1."
}

$shell = New-Object -ComObject WScript.Shell

foreach ($directory in $locations) {
    $link = Join-Path $directory 'RigSight.lnk'
    $shortcut = $shell.CreateShortcut($link)
    $shortcut.TargetPath = $exe.FullName
    $shortcut.WorkingDirectory = $exe.DirectoryName
    $shortcut.Description = "RigSight - your rig's vitals, on your phone"
    $shortcut.IconLocation = "$($exe.FullName),0"
    $shortcut.WindowStyle = 1
    $shortcut.Save()

    $check = $shell.CreateShortcut($link)
    Write-Host "Created $link"
    Write-Host "    target: $($check.TargetPath)"
}

Write-Host ''
Write-Host 'Opening the shortcut prompts for administrator rights (needed for full CPU sensors).' -ForegroundColor Cyan
Write-Host 'Tip: right-click the Start Menu entry and choose Pin to Start or Pin to taskbar.' -ForegroundColor Cyan
