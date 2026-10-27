$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $projectRoot 'build.ps1')
$programPath = Join-Path $projectRoot 'NightreignTrainer-v0.4.exe'
$nativeCheck = Start-Process -FilePath $programPath -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($nativeCheck.ExitCode -ne 0) { throw 'Native memory self-test failed; inspect last-error.txt.' }
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @('Trainer.cs','Advanced.cs','AdvancedTests.cs','Overlay.cs','OverlayTests.cs','tests\RecoveryTests.cs') | ForEach-Object { Join-Path $projectRoot $_ }
$recoveryPath = Join-Path $PSScriptRoot 'RecoveryTests.exe'
& $compilerPath /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 /main:NightreignLocalTrainer.RecoveryTests /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll "/out:$recoveryPath" @sources
if ($LASTEXITCODE -ne 0) { throw 'Recovery test compilation failed.' }
$recoveryCheck = Start-Process -FilePath $recoveryPath -WindowStyle Hidden -Wait -PassThru
if ($recoveryCheck.ExitCode -ne 0) { throw 'Recovery test failed; inspect tests\rollback-error.txt.' }
Write-Output 'PASS: native process-memory self-test and isolated rollback integration tests.'
Write-Output 'Optional interactive desktop test: NightreignTrainer-v0.4.exe --verify-overlay'
