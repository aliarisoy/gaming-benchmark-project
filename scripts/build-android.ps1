<#
.SYNOPSIS
    Builds the RigSight Android APK.

.DESCRIPTION
    Uses the user-local JDK 17, Android SDK, and Gradle from
    scripts\setup-dev.ps1. Runs Gradle assemble + lint by default, then copies
    the APK to artifacts\RigSight-<configuration>.apk. Pass -Install to install
    it on a USB-connected phone with adb.

.PARAMETER Configuration
    Debug (default) or Release. Release builds are unsigned.

.PARAMETER SkipLint
    Skip Android lint.

.PARAMETER Install
    Install the resulting APK with adb after a successful build.

.PARAMETER Serial
    adb device serial, for example "emulator-5554" when more than one phone is attached.

.EXAMPLE
    .\scripts\build-android.ps1

.EXAMPLE
    .\scripts\build-android.ps1 -Install
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipLint,
    [switch]$Install,
    [string]$Serial
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$androidDir = Join-Path $repoRoot 'android'
$toolsRoot = Join-Path $env:LOCALAPPDATA 'RigSight\tools'
$jdkDir = Join-Path $toolsRoot 'jdk-17'
$androidSdk = Join-Path $toolsRoot 'android-sdk'
$gradleBat = Join-Path $toolsRoot 'gradle-8.9\bin\gradle.bat'

if (-not (Test-Path -LiteralPath (Join-Path $jdkDir 'bin\java.exe'))) {
    throw 'JDK 17 not found. Run .\scripts\setup-dev.ps1 first.'
}
if (-not (Test-Path -LiteralPath (Join-Path $androidSdk 'platforms\android-35'))) {
    throw 'Android SDK platform 35 not found. Run .\scripts\setup-dev.ps1 first.'
}
if (-not (Test-Path -LiteralPath $gradleBat)) {
    throw 'Gradle 8.9 not found. Run .\scripts\setup-dev.ps1 first.'
}

$env:JAVA_HOME = $jdkDir
$env:ANDROID_HOME = $androidSdk
$env:ANDROID_SDK_ROOT = $androidSdk

$localProperties = Join-Path $androidDir 'local.properties'
if (-not (Test-Path -LiteralPath $localProperties)) {
    $sdkDirValue = 'sdk.dir=' + ($androidSdk -replace '\\', '/')
    Set-Content -LiteralPath $localProperties -Value $sdkDirValue -Encoding ASCII
}

$tasks = @()
if ($Configuration -eq 'Release') {
    $tasks += 'assembleRelease'
    if (-not $SkipLint) { $tasks += 'lintRelease' }
} else {
    $tasks += 'assembleDebug'
    if (-not $SkipLint) { $tasks += 'lintDebug' }
}

Write-Host "Building Android ($Configuration): $($tasks -join ', ')" -ForegroundColor Cyan
Push-Location $androidDir
try {
    & $gradleBat --no-daemon @tasks
    $gradleExit = $LASTEXITCODE
} finally {
    Pop-Location
}
if ($gradleExit -ne 0) {
    throw 'The Android build failed. See the Gradle output above.'
}

$apkName = if ($Configuration -eq 'Release') { 'app-release-unsigned.apk' } else { 'app-debug.apk' }
$apkSubdir = if ($Configuration -eq 'Release') { 'release' } else { 'debug' }
$apkPath = Join-Path $androidDir "app\build\outputs\apk\$apkSubdir\$apkName"
if (-not (Test-Path -LiteralPath $apkPath)) {
    throw "APK not found after the build: $apkPath"
}

$artifactsDir = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
$artifactName = if ($Configuration -eq 'Release') { 'RigSight-release-unsigned.apk' } else { 'RigSight-debug.apk' }
$artifactPath = Join-Path $artifactsDir $artifactName
Copy-Item -LiteralPath $apkPath -Destination $artifactPath -Force

$sizeMb = [Math]::Round((Get-Item -LiteralPath $artifactPath).Length / 1MB, 1)
Write-Host "APK: $artifactPath ($sizeMb MB)" -ForegroundColor Green

if ($Install) {
    $adbExe = Join-Path $androidSdk 'platform-tools\adb.exe'
    if (-not (Test-Path -LiteralPath $adbExe)) {
        $adbCommand = Get-Command adb -ErrorAction SilentlyContinue
        if ($null -eq $adbCommand) { throw 'adb was not found.' }
        $adbExe = $adbCommand.Source
    }

    $adbArgs = @()
    if ($Serial) { $adbArgs += @('-s', $Serial) }
    $adbArgs += @('install', '-r', $artifactPath)

    Write-Host 'Installing on the connected phone...' -ForegroundColor Cyan
    & $adbExe @adbArgs
    if ($LASTEXITCODE -ne 0) {
        throw 'adb install failed. Check USB debugging and that the phone is unlocked.'
    }
    Write-Host 'Installed. Launch RigSight on the phone.' -ForegroundColor Green
}
