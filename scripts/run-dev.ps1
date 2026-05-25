<#
.SYNOPSIS
    Publishes and launches the photobooth app directly (no Docker).

.DESCRIPTION
    Use this on Windows 10/11 dev machines where Windows container process
    isolation is not available.  Produces a self-contained Release build and
    runs Photobooth.exe — no Visual Studio or IDE required.

    On first run NuGet packages are restored automatically.

.PARAMETER NoBuild
    Skip the publish step and run the last published output as-is.
#>
param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repoRoot   = Split-Path $PSScriptRoot -Parent
$project    = Join-Path $repoRoot "Photobooth\Photobooth.csproj"
$publishDir = Join-Path $repoRoot "publish"
$exe        = Join-Path $publishDir "Photobooth.exe"

if (-not $NoBuild) {
    Write-Host "Publishing Release build..." -ForegroundColor Cyan

    dotnet publish $project `
        -c Release `
        -r win-x86 `
        --self-contained true `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed (exit code $LASTEXITCODE)"
        exit 1
    }
}

if (-not (Test-Path $exe)) {
    Write-Error "Executable not found at $exe — run without -NoBuild first"
    exit 1
}

Write-Host "Launching Photobooth..." -ForegroundColor Green
& $exe
