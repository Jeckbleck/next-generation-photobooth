<#
.SYNOPSIS
    Installs or uninstalls the Photobooth kiosk app on this machine.

.DESCRIPTION
    Run this script from the unzipped package folder as Administrator.

    What it does:
      - Copies all files to C:\Program Files\Photobooth (or a custom path)
      - Creates a desktop shortcut for all users
      - Creates C:\Photobooth for photo storage
      - Optionally enables auto-start on Windows login (-AutoStart)

.PARAMETER InstallDir
    Where to install the app. Defaults to C:\Program Files\Photobooth.

.PARAMETER AutoStart
    Register the app to launch automatically when Windows starts.
    Recommended for kiosk machines.

.PARAMETER Uninstall
    Removes the installation, shortcut, and auto-start entry.

.EXAMPLE
    # Standard install
    powershell -ExecutionPolicy Bypass .\install.ps1

    # Kiosk install with auto-start
    powershell -ExecutionPolicy Bypass .\install.ps1 -AutoStart

    # Remove
    powershell -ExecutionPolicy Bypass .\install.ps1 -Uninstall
#>
param(
    [string]$InstallDir = "C:\Program Files\Photobooth",
    [switch]$AutoStart,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

$appName    = "Photobooth Kiosk"
$exeName    = "Photobooth.exe"
$regRunKey  = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
$desktopDir = [Environment]::GetFolderPath("CommonDesktopDirectory")
$shortcutPath = Join-Path $desktopDir "$appName.lnk"

# Require Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Please run this script as Administrator (right-click → Run as administrator)."
    exit 1
}

# ── Uninstall ────────────────────────────────────────────────────────────────
if ($Uninstall) {
    Write-Host "Uninstalling $appName ..." -ForegroundColor Yellow

    Remove-ItemProperty -Path $regRunKey -Name $appName -ErrorAction SilentlyContinue

    if (Test-Path $shortcutPath) { Remove-Item -Force $shortcutPath; Write-Host "  Removed desktop shortcut." }
    if (Test-Path $InstallDir)   { Remove-Item -Recurse -Force $InstallDir; Write-Host "  Removed $InstallDir." }

    Write-Host "Uninstalled." -ForegroundColor Green
    exit 0
}

# ── Install ──────────────────────────────────────────────────────────────────
$scriptDir = $PSScriptRoot
$sourceExe = Join-Path $scriptDir $exeName

if (-not (Test-Path $sourceExe)) {
    Write-Error "Cannot find $exeName in $scriptDir`nMake sure you are running install.ps1 from inside the unzipped package folder."
    exit 1
}

Write-Host "Installing $appName to $InstallDir ..." -ForegroundColor Cyan

# Copy application files (excluding the install script itself)
if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Get-ChildItem $scriptDir -Exclude "install.ps1" | Copy-Item -Destination $InstallDir -Recurse
Write-Host "  Files copied."

# Create photo storage directory
New-Item -ItemType Directory -Force -Path "C:\Photobooth" | Out-Null
Write-Host "  Photo storage: C:\Photobooth"

# Desktop shortcut
$shell            = New-Object -ComObject WScript.Shell
$shortcut         = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath       = Join-Path $InstallDir $exeName
$shortcut.WorkingDirectory = $InstallDir
$shortcut.Description      = $appName
$shortcut.Save()
Write-Host "  Desktop shortcut created."

# Auto-start
if ($AutoStart) {
    Set-ItemProperty -Path $regRunKey -Name $appName -Value (Join-Path $InstallDir $exeName)
    Write-Host "  Auto-start on login: enabled."
}

Write-Host "`nInstallation complete." -ForegroundColor Green
Write-Host "Launch from the desktop shortcut or:"
Write-Host "  $InstallDir\$exeName"
