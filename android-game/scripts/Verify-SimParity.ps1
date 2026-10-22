[CmdletBinding()]
param([string]$UnityEditor)
# Records browser rule traces with Node, then replays them through the Android C# rules port in
# the pinned Unity Editor. Every tick must match exactly. Run from any directory; Unity must be closed.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..'))
if (-not $UnityEditor) { $UnityEditor = Join-Path $env:ProgramFiles 'Unity/Hub/Editor/6000.3.26f1/Editor/Unity.exe' }
if (-not (Test-Path -LiteralPath $UnityEditor)) { throw 'Pinned Unity Editor not found. Supply -UnityEditor with its full path.' }
$evidence = Join-Path $repositoryRoot 'artifacts/sim-parity'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$traces = Join-Path $evidence 'traces.txt'
& node (Join-Path $repositoryRoot 'scripts/sim-traces.mjs') $traces
if ($LASTEXITCODE -ne 0) { throw 'Trace recording failed.' }
$log = Join-Path $evidence 'unity-parity.log'
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $projectRoot + '"'),
    '-executeMethod', 'GridRun.Editor.SimParity.Run', '--parity-traces', ('"' + $traces + '"'),
    '-logFile', ('"' + $log + '"'))
$unity = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
$report = Join-Path $evidence 'parity-report.txt'
if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -TotalCount 40 }
if ($unity.ExitCode -ne 0 -or -not (Select-String -LiteralPath $log -SimpleMatch 'SIM_PARITY_PASS' -Quiet)) {
    throw "Rules parity failed (exit $($unity.ExitCode)). Review $report and $log."
}
Write-Host "Rules parity passed. Evidence: $evidence"
