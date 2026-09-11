param([string]$Makensis)
$ErrorActionPreference='Stop'
if (-not $Makensis) {
 $Makensis=(Get-ChildItem -LiteralPath "$env:LOCALAPPDATA/electron-builder/Cache" -Filter makensis.exe -Recurse | Sort-Object {$_.FullName.Length} | Select-Object -First 1).FullName
}
if (-not $Makensis) { throw 'NSIS compiler not found. Build the installer first, or pass -Makensis.' }
$testDir=(New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot "../test-results/installer-$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())")).FullName
foreach ($scenario in @('success','unchecked','failure','timeout')) {
 & $Makensis /V2 "/DOWL_SCENARIO=$scenario" "/DTEST_DIR=$testDir" (Join-Path $PSScriptRoot '../tests/installer-finish.nsi')
 if ($LASTEXITCODE -ne 0) { throw "NSIS test compile failed: $scenario" }
 $testProc=Start-Process -FilePath (Join-Path $testDir "finish-$scenario.exe") -WindowStyle Hidden -PassThru
 if (-not $testProc.WaitForExit(45000)) {
  Stop-Process -Id $testProc.Id
  throw "Installer test exceeded 45 seconds: $scenario"
 }
 $result=Get-Content -LiteralPath (Join-Path $testDir "result-$scenario.ini") -Raw
 if ($result -notmatch '(?m)^Errors=0\s*$') { throw "Installer UI test failed: $scenario $result" }
 $callsPath=Join-Path $testDir "calls-$scenario.ini"
 if ($scenario -eq 'unchecked') {
  if (Test-Path -LiteralPath $callsPath) { throw 'Unchecked Run unexpectedly launched a helper' }
 } else {
  $calls=Get-Content -LiteralPath $callsPath -Raw
  if ($calls -notmatch '(?m)^Count=1\s*$') { throw "Unexpected launch count: $calls" }
  if ($result -notmatch '(?m)^State=done\s*$') { throw "Finish did not recover: $result" }
  $null=$result -match 'DisabledChecks=(\d+)'
  if ([int]$Matches[1] -lt 3) { throw "Opening status was not responsive: $result" }
 }
 Write-Output "PASS $scenario : Finish state, status, responsive timer and launch count"
}
