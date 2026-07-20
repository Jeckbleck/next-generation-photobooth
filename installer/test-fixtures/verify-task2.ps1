$ErrorActionPreference = "Stop"
$testDir  = "C:\PhotoboothInstallTest2"
# Photobooth.exe is a win-x86 (32-bit) app, so its own Setup.exe is also 32-bit and its
# HKLM writes go through WOW64 redirection to WOW6432Node on 64-bit Windows.
$runKey   = "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
$valName  = "Photobooth Kiosk"

if (Test-Path $testDir) { Remove-Item -Recurse -Force $testDir }
Remove-ItemProperty -Path $runKey -Name $valName -ErrorAction SilentlyContinue

iscc installer\Photobooth.iss "/DMyAppVersion=0.0.0-test" "/DPublishDir=test-fixtures\fake-publish" "/O.\installer\test-fixtures\out" | Out-Null
$setupExe = "installer\test-fixtures\out\PhotoboothSetup-0.0.0-test.exe"

Write-Host "Install with default tasks (autostart should be ON)..." -ForegroundColor Cyan
& $setupExe /VERYSILENT /SUPPRESSMSGBOXES /DIR="$testDir" | Out-Null
$value = (Get-ItemProperty -Path $runKey -Name $valName -ErrorAction SilentlyContinue).$valName
if (-not $value) { throw "FAIL: autostart registry value missing when task should default ON" }
if ($value -notmatch [regex]::Escape((Join-Path $testDir "Photobooth.exe"))) { throw "FAIL: autostart value does not point at install dir" }

& (Join-Path $testDir "unins000.exe") /VERYSILENT /SUPPRESSMSGBOXES | Out-Null
Start-Sleep -Seconds 2
if (Get-ItemProperty -Path $runKey -Name $valName -ErrorAction SilentlyContinue) { throw "FAIL: autostart registry value survived uninstall" }

Write-Host "Install with autostart explicitly OFF..." -ForegroundColor Cyan
& $setupExe /VERYSILENT /SUPPRESSMSGBOXES /DIR="$testDir" "/TASKS=!autostart" | Out-Null
if (Get-ItemProperty -Path $runKey -Name $valName -ErrorAction SilentlyContinue) { throw "FAIL: autostart registry value created despite task being off" }

& (Join-Path $testDir "unins000.exe") /VERYSILENT /SUPPRESSMSGBOXES | Out-Null
Write-Host "Task 2 verification PASSED" -ForegroundColor Green
