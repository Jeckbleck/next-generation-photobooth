$ErrorActionPreference = "Stop"
$testDir   = "C:\PhotoboothInstallTest"
$groupName = "Photobooth Kiosk"

if (Test-Path $testDir) { Remove-Item -Recurse -Force $testDir }

Write-Host "Compiling installer..." -ForegroundColor Cyan
iscc installer\Photobooth.iss `
    "/DMyAppVersion=0.0.0-test" `
    "/DPublishDir=test-fixtures\fake-publish" `
    "/O.\installer\test-fixtures\out"
if ($LASTEXITCODE -ne 0) { throw "ISCC compile failed" }

$setupExe = "installer\test-fixtures\out\PhotoboothSetup-0.0.0-test.exe"
if (-not (Test-Path $setupExe)) { throw "Compiled installer not found at $setupExe" }

Write-Host "Running silent install..." -ForegroundColor Cyan
& $setupExe /VERYSILENT /SUPPRESSMSGBOXES /DIR="$testDir" /NORESTART | Out-Null

$installedExe = Join-Path $testDir "Photobooth.exe"
if (-not (Test-Path $installedExe)) { throw "FAIL: Photobooth.exe not found in install dir" }
if ((Get-Content $installedExe -Raw).Trim() -ne "v1-fake-exe") { throw "FAIL: installed file content mismatch" }

$startMenuLnk = Join-Path ([Environment]::GetFolderPath("CommonPrograms")) "$groupName\Photobooth Kiosk.lnk"
if (-not (Test-Path $startMenuLnk)) { throw "FAIL: Start Menu shortcut not found at $startMenuLnk" }

$desktopLnk = Join-Path ([Environment]::GetFolderPath("CommonDesktopDirectory")) "Photobooth Kiosk.lnk"
if (-not (Test-Path $desktopLnk)) { throw "FAIL: Desktop shortcut not found at $desktopLnk" }

Write-Host "Running silent uninstall..." -ForegroundColor Cyan
$uninstaller = Join-Path $testDir "unins000.exe"
Start-Process -FilePath $uninstaller -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES" -Wait

Start-Sleep -Seconds 2
if (Test-Path $testDir) { throw "FAIL: install dir still present after uninstall" }
if (Test-Path $startMenuLnk) { throw "FAIL: Start Menu shortcut still present after uninstall" }
if (Test-Path $desktopLnk) { throw "FAIL: Desktop shortcut still present after uninstall" }

Write-Host "Task 1 verification PASSED" -ForegroundColor Green
