$ErrorActionPreference = "Stop"
$testDir    = "C:\PhotoboothInstallTest3"
$storageDir = "C:\Photobooth"
$marker     = Join-Path $storageDir "marker.txt"

if (Test-Path $testDir) { Remove-Item -Recurse -Force $testDir }
if (Test-Path $storageDir) { Remove-Item -Recurse -Force $storageDir }

iscc installer\Photobooth.iss "/DMyAppVersion=0.0.0-test" "/DPublishDir=test-fixtures\fake-publish" "/O.\installer\test-fixtures\out" | Out-Null
$setupExe = "installer\test-fixtures\out\PhotoboothSetup-0.0.0-test.exe"

& $setupExe /VERYSILENT /SUPPRESSMSGBOXES /DIR="$testDir" | Out-Null
if (-not (Test-Path $storageDir)) { throw "FAIL: C:\Photobooth was not created by the installer" }

Set-Content -Path $marker -Value "keep-me"

& (Join-Path $testDir "unins000.exe") /VERYSILENT /SUPPRESSMSGBOXES | Out-Null
Start-Sleep -Seconds 2

if (-not (Test-Path $storageDir)) { throw "FAIL: C:\Photobooth was removed by uninstall" }
if (-not (Test-Path $marker)) { throw "FAIL: marker file inside C:\Photobooth was removed by uninstall" }

Write-Host "Task 3 verification PASSED" -ForegroundColor Green
Write-Host "(leaving C:\Photobooth and marker.txt in place — remove manually if this was a throwaway test machine)" -ForegroundColor Yellow
