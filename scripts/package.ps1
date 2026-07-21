<#
.SYNOPSIS
    Builds a self-contained installer ready to copy to kiosk mini PCs.

.DESCRIPTION
    Runs dotnet publish (win-x86, self-contained) then compiles the output
    into a double-click Inno Setup installer (PhotoboothSetup-{version}.exe)
    under releases\.

    The installer contains everything the target machine needs - no .NET
    runtime, no Visual Studio, no dev tools.

.PARAMETER Version
    Version string used in the installer filename. Defaults to today's date.

.EXAMPLE
    .\package.ps1
    .\package.ps1 -Version 1.2.0
#>
param(
    [string]$Version = (Get-Date -Format "yyyy.MM.dd")
)

$ErrorActionPreference = "Stop"

$repoRoot   = Split-Path $PSScriptRoot -Parent
$project    = Join-Path $repoRoot "Photobooth\Photobooth.csproj"
$publishDir = Join-Path $repoRoot ".publish-temp"
$releaseDir = Join-Path $repoRoot "releases"

# Clean staging area
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

Write-Host "Publishing v$Version ..." -ForegroundColor Cyan

dotnet publish $project `
    -c Release `
    -r win-x86 `
    --self-contained true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed (exit $LASTEXITCODE)"; exit 1 }

$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc) {
    Write-Error "ISCC.exe (Inno Setup Compiler) not found on PATH. Install it with 'choco install innosetup' or from https://jrsoftware.org/isdl.php"
    exit 1
}

$issScript = Join-Path $repoRoot "installer\Photobooth.iss"
Write-Host "Compiling installer..." -ForegroundColor Cyan
& $iscc.Source $issScript "/DMyAppVersion=$Version" "/DPublishDir=$publishDir" "/O$releaseDir"
if ($LASTEXITCODE -ne 0) { Write-Error "ISCC compile failed (exit $LASTEXITCODE)"; exit 1 }

# Clean up staging area
Remove-Item -Recurse -Force $publishDir

$setupExe = Join-Path $releaseDir "PhotoboothSetup-$Version.exe"
$size = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
Write-Host "Done - PhotoboothSetup-$Version.exe ($size MB)" -ForegroundColor Green
Write-Host "Distribute this file to each kiosk and double-click it to install."
