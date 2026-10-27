$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework 4 compiler not found.' }
& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /out:"$PSScriptRoot\NightreignTrainer-v0.4.exe" "$PSScriptRoot\Trainer.cs" "$PSScriptRoot\Advanced.cs" "$PSScriptRoot\AdvancedTests.cs" "$PSScriptRoot\Overlay.cs" "$PSScriptRoot\OverlayTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
