$ErrorActionPreference = "Stop"
$testDir   = "C:\PhotoboothInstallTest4"
# Photobooth.exe is a win-x86 (32-bit) app, so its own Setup.exe is also 32-bit and its
# HKLM writes go through WOW64 redirection to WOW6432Node on 64-bit Windows.
$uninstKey = "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{8F3A1C9E-4B7D-4A2F-9E61-3C8D5F2A71B4}_is1"

if (Test-Path $testDir) { Remove-Item -Recurse -Force $testDir }
Remove-Item -Path $uninstKey -Recurse -Force -ErrorAction SilentlyContinue

# v1 publish fixture
$v1Dir = "installer\test-fixtures\fake-publish-v1"
New-Item -ItemType Directory -Force -Path $v1Dir | Out-Null
Set-Content -Path (Join-Path $v1Dir "Photobooth.exe") -Value "v1-fake-exe"

# v2 publish fixture
$v2Dir = "installer\test-fixtures\fake-publish-v2"
New-Item -ItemType Directory -Force -Path $v2Dir | Out-Null
Set-Content -Path (Join-Path $v2Dir "Photobooth.exe") -Value "v2-fake-exe"

# ISCC resolves [Files] Source paths relative to the .iss file's own directory
# (installer/), not the caller's working directory — so PublishDir here is
# given relative to installer/, e.g. "test-fixtures\fake-publish-v1".
iscc installer\Photobooth.iss "/DMyAppVersion=1.0.0" "/DPublishDir=test-fixtures\fake-publish-v1" "/O.\installer\test-fixtures\out" | Out-Null
iscc installer\Photobooth.iss "/DMyAppVersion=2.0.0" "/DPublishDir=test-fixtures\fake-publish-v2" "/O.\installer\test-fixtures\out" | Out-Null

Write-Host "Installing v1..." -ForegroundColor Cyan
& "installer\test-fixtures\out\PhotoboothSetup-1.0.0.exe" /VERYSILENT /SUPPRESSMSGBOXES /DIR="$testDir" | Out-Null

Write-Host "Installing v2 over v1..." -ForegroundColor Cyan
& "installer\test-fixtures\out\PhotoboothSetup-2.0.0.exe" /VERYSILENT /SUPPRESSMSGBOXES /DIR="$testDir" | Out-Null

$installedExe = Join-Path $testDir "Photobooth.exe"
if ((Get-Content $installedExe -Raw).Trim() -ne "v2-fake-exe") { throw "FAIL: install dir still has v1 content after upgrade" }

$uninstEntries = Get-ChildItem "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" |
    Where-Object { $_.PSChildName -eq "{8F3A1C9E-4B7D-4A2F-9E61-3C8D5F2A71B4}_is1" }
if ($uninstEntries.Count -ne 1) { throw "FAIL: expected exactly 1 uninstall registry entry, found $($uninstEntries.Count)" }

& (Join-Path $testDir "unins000.exe") /VERYSILENT /SUPPRESSMSGBOXES | Out-Null
Write-Host "Task 4 verification PASSED" -ForegroundColor Green
