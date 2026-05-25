<#
.SYNOPSIS
    Builds a self-contained release package ready to copy to kiosk mini PCs.

.DESCRIPTION
    Runs dotnet publish (win-x86, self-contained) then bundles the output plus
    install.ps1 into a single ZIP file under releases\.

    The ZIP contains everything the target machine needs — no .NET runtime,
    no Visual Studio, no dev tools.

.PARAMETER Version
    Version string used in the ZIP filename. Defaults to today's date.

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
$zipPath    = Join-Path $releaseDir "photobooth-$Version.zip"

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

# Bundle install.ps1 so the ZIP is self-contained
Copy-Item (Join-Path $PSScriptRoot "install.ps1") $publishDir

Write-Host "Zipping to $zipPath ..." -ForegroundColor Cyan
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

# Clean up staging area
Remove-Item -Recurse -Force $publishDir

$size = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "Done — photobooth-$Version.zip ($size MB)" -ForegroundColor Green
Write-Host "Distribute this file to each kiosk and run: powershell -ExecutionPolicy Bypass .\install.ps1"
