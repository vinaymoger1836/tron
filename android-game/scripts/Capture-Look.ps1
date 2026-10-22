[CmdletBinding()]
param([string]$UnityEditor, [switch]$SkipBuild)
# Builds the combined desktop player and saves art-review screenshots of every scene.
# Screenshots are review evidence only; gameplay checks stay in the Verify-*.ps1 scripts.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..'))
if (-not $UnityEditor) { $UnityEditor = Join-Path $env:ProgramFiles 'Unity/Hub/Editor/6000.3.26f1/Editor/Unity.exe' }
$evidence = Join-Path $repositoryRoot 'artifacts/android-look'
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
$playerLog = Join-Path $evidence 'look-player.log'
Get-ChildItem -LiteralPath $evidence -Filter 'look-*.png' -ErrorAction SilentlyContinue | Remove-Item
$process = Start-Process -FilePath $player -PassThru -WindowStyle Hidden -ArgumentList @('-force-d3d11',
    '-screen-width', '1280', '-screen-height', '720', '-screen-fullscreen', '0',
    '-logFile', ('"' + $playerLog + '"'), '--look-capture-dir', ('"' + $evidence + '"'))
if (-not $process.WaitForExit(120000)) { $process.Kill(); throw "Look capture timed out. Review $playerLog." }
$shots = @(Get-ChildItem -LiteralPath $evidence -Filter 'look-*.png')
Write-Host "Saved $($shots.Count) screenshots to $evidence"
