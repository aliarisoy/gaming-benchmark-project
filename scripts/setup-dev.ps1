<#
.SYNOPSIS
    Installs the user-local RigSight development toolchain.

.DESCRIPTION
    Installs the .NET 10 SDK, Temurin JDK 17, Android command-line tools,
    Android platform 35 / build-tools, Gradle 8.9, and PresentMon 2.6.0.

    Everything goes under %LOCALAPPDATA%\RigSight\tools by default. No admin
    rights and no machine-wide changes are required. Re-running is safe; existing
    components are left alone.

.PARAMETER ToolsRoot
    Override the toolchain location. Defaults to %LOCALAPPDATA%\RigSight\tools.

.PARAMETER SkipPresentMon
    Skip the PresentMon download (hardware telemetry still works, FPS capture
    will be unavailable until PresentMon.exe exists).

.EXAMPLE
    .\scripts\setup-dev.ps1
#>
[CmdletBinding()]
param(
    [string]$ToolsRoot = (Join-Path $env:LOCALAPPDATA 'RigSight\tools'),
    [switch]$SkipPresentMon
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnetDir = Join-Path $ToolsRoot 'dotnet'
$jdkDir = Join-Path $ToolsRoot 'jdk-17'
$androidSdk = Join-Path $ToolsRoot 'android-sdk'
$gradleDir = Join-Path $ToolsRoot 'gradle-8.9'
$presentMonPath = Join-Path $repoRoot 'tools\presentmon\PresentMon.exe'

New-Item -ItemType Directory -Path $ToolsRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $repoRoot 'tools\presentmon') -Force | Out-Null

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Get-RemoteFile([string]$Uri, [string]$OutFile) {
    Write-Host "    downloading $Uri"
    Invoke-WebRequest -Uri $Uri -OutFile $OutFile -UseBasicParsing
    if (-not (Test-Path -LiteralPath $OutFile) -or (Get-Item -LiteralPath $OutFile).Length -eq 0) {
        throw "Download failed or produced an empty file: $Uri"
    }
}

function Expand-Zip([string]$ZipPath, [string]$Destination) {
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $Destination -Force
}

Write-Host 'RigSight development setup' -ForegroundColor Green
Write-Host "Toolchain root: $ToolsRoot"
Write-Host "Repository:     $repoRoot"

# ---------------------------------------------------------------- .NET 10 SDK
Write-Step '.NET 10 SDK'
$dotnetExe = Join-Path $dotnetDir 'dotnet.exe'
if (Test-Path -LiteralPath $dotnetExe) {
    Write-Host '    already installed'
} else {
    $installer = Join-Path $env:TEMP 'rigsight-dotnet-install.ps1'
    Get-RemoteFile 'https://dot.net/v1/dotnet-install.ps1' $installer
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer `
        -Channel '10.0' -Quality 'GA' -InstallDir $dotnetDir -NoPath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $dotnetExe)) {
        throw '.NET SDK installation failed.'
    }
}

# ------------------------------------------------------------- Temurin JDK 17
Write-Step 'Temurin JDK 17'
$javaExe = Join-Path $jdkDir 'bin\java.exe'
if (Test-Path -LiteralPath $javaExe) {
    Write-Host '    already installed'
} else {
    $jdkZip = Join-Path $env:TEMP 'rigsight-jdk17.zip'
    $jdkTemp = Join-Path $env:TEMP 'rigsight-jdk17-extract'
    Get-RemoteFile 'https://api.adoptium.net/v3/binary/latest/17/ga/windows/x64/jdk/hotspot/normal/eclipse?project=jdk' $jdkZip
    Expand-Zip $jdkZip $jdkTemp
    $inner = Get-ChildItem -LiteralPath $jdkTemp -Directory | Select-Object -First 1
    if ($null -eq $inner) { throw 'The JDK archive did not contain a folder.' }
    if (Test-Path -LiteralPath $jdkDir) { Remove-Item -LiteralPath $jdkDir -Recurse -Force }
    Move-Item -LiteralPath $inner.FullName -Destination $jdkDir
    Remove-Item -LiteralPath $jdkTemp -Recurse -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path -LiteralPath $javaExe)) { throw 'JDK installation failed.' }
}

# --------------------------------------------------------------- Android SDK
Write-Step 'Android SDK command-line tools'
$sdkManager = Join-Path $androidSdk 'cmdline-tools\latest\bin\sdkmanager.bat'
if (Test-Path -LiteralPath $sdkManager) {
    Write-Host '    already installed'
} else {
    $cmdToolsZip = Join-Path $env:TEMP 'rigsight-cmdline-tools.zip'
    $cmdToolsTemp = Join-Path $env:TEMP 'rigsight-cmdline-tools'
    New-Item -ItemType Directory -Path $androidSdk -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $androidSdk 'cmdline-tools') -Force | Out-Null
    Get-RemoteFile 'https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip' $cmdToolsZip
    Expand-Zip $cmdToolsZip $cmdToolsTemp
    $cmdToolsDest = Join-Path $androidSdk 'cmdline-tools\latest'
    if (Test-Path -LiteralPath $cmdToolsDest) { Remove-Item -LiteralPath $cmdToolsDest -Recurse -Force }
    Move-Item -LiteralPath (Join-Path $cmdToolsTemp 'cmdline-tools') -Destination $cmdToolsDest
    Remove-Item -LiteralPath $cmdToolsTemp -Recurse -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path -LiteralPath $sdkManager)) { throw 'Android command-line tools installation failed.' }
}

Write-Step 'Android SDK platform and build tools'
$env:JAVA_HOME = $jdkDir
$adbExe = Join-Path $androidSdk 'platform-tools\adb.exe'
$missingPackages = @()
if (-not (Test-Path -LiteralPath $adbExe)) { $missingPackages += 'platform-tools' }
if (-not (Test-Path -LiteralPath (Join-Path $androidSdk 'platforms\android-35'))) { $missingPackages += 'platforms;android-35' }
if (-not (Test-Path -LiteralPath (Join-Path $androidSdk 'build-tools\35.0.0'))) { $missingPackages += 'build-tools;35.0.0' }
if (-not (Test-Path -LiteralPath (Join-Path $androidSdk 'build-tools\34.0.0'))) { $missingPackages += 'build-tools;34.0.0' }

if ($missingPackages.Count -eq 0) {
    Write-Host '    already installed'
} else {
    Write-Host "    installing: $($missingPackages -join ', ')"
    $licenseAnswers = (1..40 | ForEach-Object { 'y' }) -join "`n"
    $savedErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $licenseAnswers | & $sdkManager --sdk_root="$androidSdk" --licenses 2>&1 | Out-Null
        & $sdkManager --sdk_root="$androidSdk" @missingPackages
        $sdkExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $savedErrorAction
    }
    if ($sdkExitCode -ne 0) { throw 'Android SDK package installation failed.' }
}
if (-not (Test-Path -LiteralPath $adbExe)) { throw 'Android platform-tools installation failed.' }

# ------------------------------------------------------------------- Gradle
Write-Step 'Gradle 8.9'
$gradleBat = Join-Path $gradleDir 'bin\gradle.bat'
if (Test-Path -LiteralPath $gradleBat) {
    Write-Host '    already installed'
} else {
    $gradleZip = Join-Path $env:TEMP 'rigsight-gradle-8.9.zip'
    Get-RemoteFile 'https://services.gradle.org/distributions/gradle-8.9-bin.zip' $gradleZip
    Expand-Archive -LiteralPath $gradleZip -DestinationPath $ToolsRoot -Force
    if (-not (Test-Path -LiteralPath $gradleBat)) { throw 'Gradle installation failed.' }
}

# --------------------------------------------------------------- PresentMon
Write-Step 'Intel PresentMon 2.6.0'
if ($SkipPresentMon) {
    Write-Host '    skipped (-SkipPresentMon)'
} elseif ((Test-Path -LiteralPath $presentMonPath) -and (Get-Item -LiteralPath $presentMonPath).Length -gt 100000) {
    Write-Host '    already installed'
} else {
    try {
        Get-RemoteFile 'https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe' $presentMonPath
    } catch {
        Write-Warning "PresentMon could not be downloaded: $($_.Exception.Message)"
        Write-Warning 'FPS capture will be unavailable until tools\presentmon\PresentMon.exe exists.'
    }
}

# --------------------------------------------------------- Android properties
Write-Step 'Android local.properties'
$localProperties = Join-Path $repoRoot 'android\local.properties'
$sdkDirValue = 'sdk.dir=' + ($androidSdk -replace '\\', '/')
Set-Content -LiteralPath $localProperties -Value $sdkDirValue -Encoding ASCII
Write-Host "    $sdkDirValue"

# --------------------------------------------------------------------- Done
$dotnetVersion = (& $dotnetExe --version).Trim()
Write-Step 'Ready'
Write-Host "    .NET SDK        $dotnetVersion"
Write-Host "    JDK 17          $jdkDir"
Write-Host "    Android SDK     $androidSdk"
Write-Host "    Gradle          $gradleDir"
Write-Host "    adb             $adbExe"
if (Test-Path -LiteralPath $presentMonPath) {
    Write-Host "    PresentMon      $presentMonPath"
}
Write-Host ''
Write-Host 'Next: .\scripts\run-agent.ps1   and   .\scripts\build-android.ps1' -ForegroundColor Green
