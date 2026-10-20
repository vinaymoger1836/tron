[CmdletBinding()]
param([string]$UnityEditor)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..'))
if (-not $UnityEditor) { $UnityEditor = Join-Path $env:ProgramFiles 'Unity/Hub/Editor/6000.3.26f1/Editor/Unity.exe' }
if (-not (Test-Path -LiteralPath $UnityEditor)) { throw 'Pinned Unity Editor not found. Supply -UnityEditor with its full path.' }
$evidence = Join-Path $repositoryRoot 'artifacts/android-phase-3'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$buildLog = Join-Path $evidence 'desktop-build.log'
$buildArgs = @('-batchmode', '-quit', '-projectPath', ('"' + $projectRoot + '"'),
    '-buildTarget', 'StandaloneWindows64', '-executeMethod', 'GridRun.Editor.PhaseThreeBuild.BuildDesktop',
    '-logFile', ('"' + $buildLog + '"'))
$buildStarted = [DateTime]::UtcNow
$build = Start-Process -FilePath $UnityEditor -ArgumentList $buildArgs -WindowStyle Hidden -PassThru -Wait
if ($build.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $buildLog) -or
    (Get-Item -LiteralPath $buildLog).LastWriteTimeUtc -lt $buildStarted -or
    -not (Select-String -LiteralPath $buildLog -SimpleMatch 'PHASE3_DESKTOP_READY' -Quiet)) {
    throw "Phase 3 desktop build failed. Review $buildLog."
}
$player = Join-Path $projectRoot 'Builds/Verification/GridRunTransit.exe'
if (-not (Test-Path -LiteralPath $player)) { throw 'Transit verification player was not produced.' }
$playerLog = Join-Path $evidence 'desktop-player.log'
$playerArgs = @('-force-d3d11', '-screen-width', '1280', '-screen-height', '720',
    '-screen-fullscreen', '0', '-logFile', ('"' + $playerLog + '"'),
    '--phase3-capture-dir', ('"' + $evidence + '"'))
$runStarted = [DateTime]::UtcNow
$process = Start-Process -FilePath $player -ArgumentList $playerArgs -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(120000)) {
    $process.Kill()
    throw "Transit verification timed out. Review $playerLog."
}
$reportPath = Join-Path $evidence 'transit-verification.json'
if (-not (Test-Path -LiteralPath $reportPath) -or
    (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $runStarted) {
    throw "No fresh transit verification report. Review $playerLog."
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$failed = @($report.checks | Where-Object { -not $_.passed })
$report.checks | Format-Table name, passed
if ($process.ExitCode -ne 0 -or $failed.Count -or $report.checks.Count -lt 29) {
    throw "Transit verification failed. Review $reportPath and $playerLog."
}
Write-Host "Solar Transit: $($report.checks.Count)/$($report.checks.Count) desktop checks passed. Evidence: $evidence"
