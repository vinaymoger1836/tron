[CmdletBinding()]
param([string]$UnityEditor)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $UnityEditor) { $UnityEditor = Join-Path $env:ProgramFiles 'Unity/Hub/Editor/6000.3.26f1/Editor/Unity.exe' }
if (-not (Test-Path -LiteralPath $UnityEditor)) { throw 'Pinned Unity Editor not found. Supply -UnityEditor with its full path.' }
$evidence = [IO.Path]::GetFullPath((Join-Path $projectRoot '../artifacts/android-phase-0'))
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$buildLog = Join-Path $evidence 'desktop-build.log'
$buildArguments = @('-batchmode', '-quit', '-projectPath', ('"' + $projectRoot + '"'), '-buildTarget', 'StandaloneWindows64', '-executeMethod', 'GridRun.Editor.PhaseZeroBuild.BuildVerificationPlayer', '-logFile', ('"' + $buildLog + '"'))
$started = [DateTime]::UtcNow
$buildProcess = Start-Process -FilePath $UnityEditor -ArgumentList $buildArguments -WindowStyle Hidden -PassThru -Wait
if ($buildProcess.ExitCode -ne 0) { throw "Verification build failed. Review $buildLog." }
$player = Join-Path $projectRoot 'Builds/Verification/GridRunProbe.exe'
if (-not (Test-Path -LiteralPath $player)) { throw 'Verification player was not produced.' }
if ((Get-Item -LiteralPath $buildLog).LastWriteTimeUtc -lt $started -or -not (Select-String -LiteralPath $buildLog -SimpleMatch 'PHASE0_VERIFICATION_READY' -Quiet)) { throw 'Current verification build did not report success.' }
$playerLog = Join-Path $evidence 'desktop-player.log'
$playerArguments = @('-screen-width', '1280', '-screen-height', '720', '-screen-fullscreen', '0', '-logFile', ('"' + $playerLog + '"'), '--phase0-capture-dir', ('"' + $evidence + '"'))
$runStarted = [DateTime]::UtcNow
$playerProcess = Start-Process -FilePath $player -ArgumentList $playerArguments -WindowStyle Hidden -PassThru
if (-not $playerProcess.WaitForExit(120000)) {
    $playerProcess.Kill()
    throw "Verification player timed out. Review $playerLog."
}
$reportPath = Join-Path $evidence 'desktop-verification.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw "No verification report was produced. Review $playerLog." }
if ((Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $runStarted) { throw 'Verification report is stale.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$failed = @($report.checks | Where-Object { -not $_.passed })
$report.checks | Format-Table name,passed
if ($failed.Count -or $report.checks.Count -lt 17) { throw "Desktop verification did not pass completely. Review $reportPath." }
Write-Host "Desktop checks passed. Screenshots/report: $evidence. Physical Android validation remains separate."
