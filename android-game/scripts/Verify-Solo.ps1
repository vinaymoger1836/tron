[CmdletBinding()]
param([string]$UnityEditor, [switch]$SkipBuild)
# Builds the combined desktop player and drives every offline mode through its real UI controls.
# Evidence (report, logs, phone/desktop/tablet screenshots) goes to ignored artifacts/android-solo/.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..'))
if (-not $UnityEditor) { $UnityEditor = Join-Path $env:ProgramFiles 'Unity/Hub/Editor/6000.3.26f1/Editor/Unity.exe' }
$evidence = Join-Path $repositoryRoot 'artifacts/android-solo'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
if (-not $SkipBuild) {
    if (-not (Test-Path -LiteralPath $UnityEditor)) { throw 'Pinned Unity Editor not found. Supply -UnityEditor with its full path.' }
    $buildLog = Join-Path $evidence 'desktop-build.log'
    $buildArgs = @('-batchmode', '-quit', '-projectPath', ('"' + $projectRoot + '"'),
        '-buildTarget', 'StandaloneWindows64', '-executeMethod', 'GridRun.Editor.PhaseFourBuild.BuildDesktop',
        '-logFile', ('"' + $buildLog + '"'))
    $build = Start-Process -FilePath $UnityEditor -ArgumentList $buildArgs -WindowStyle Hidden -PassThru -Wait
    if ($build.ExitCode -ne 0 -or -not (Select-String -LiteralPath $buildLog -SimpleMatch 'PHASE4_DESKTOP_READY' -Quiet)) {
        throw "Desktop build failed. Review $buildLog."
    }
}
$player = Join-Path $projectRoot 'Builds/Verification/GridRunShowroom.exe'
$playerLog = Join-Path $evidence 'desktop-player.log'
$reportPath = Join-Path $evidence 'solo-verification.json'
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $player -PassThru -WindowStyle Hidden -ArgumentList @('-force-d3d11',
    '-screen-width', '1280', '-screen-height', '720', '-screen-fullscreen', '0',
    '-logFile', ('"' + $playerLog + '"'), '--solo-capture-dir', ('"' + $evidence + '"'))
if (-not $process.WaitForExit(330000)) { $process.Kill(); throw "Solo verification timed out. Review $playerLog." }
if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) {
    throw "No fresh solo verification report. Review $playerLog."
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$failed = @($report.checks | Where-Object { -not $_.passed })
foreach ($check in $failed) { Write-Host ("FAILED: " + $check.name + " " + $check.detail) }
if ($process.ExitCode -ne 0 -or $failed.Count) { throw "Solo verification failed ($($failed.Count) of $($report.checks.Count)). Review $reportPath." }
Write-Host "Offline modes: $($report.checks.Count)/$($report.checks.Count) desktop checks passed. Evidence: $evidence"
