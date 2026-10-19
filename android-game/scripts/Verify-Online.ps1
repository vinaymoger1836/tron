[CmdletBinding()]
param([int]$Port = 0, [string]$UnityPlayer)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$evidence = Join-Path $root 'artifacts/android-phase-2/final'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
if (-not $UnityPlayer) { $UnityPlayer = Join-Path $root 'android-game/Builds/Verification/GridRunOnline.exe' }
if (-not (Test-Path -LiteralPath $UnityPlayer)) { throw 'Build the Phase 2 desktop player first.' }
$codeFile = Join-Path $evidence 'room-code.txt'
if (Test-Path -LiteralPath $codeFile) { Remove-Item -LiteralPath $codeFile -Force }
$node = (Get-Command node -ErrorAction Stop).Source
if ($Port -eq 0) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $Port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    $listener.Stop()
}
$runStartedUtc = [DateTime]::UtcNow
$serverOrigin = 'http://127.0.0.1:' + $Port
$env:TRON_PUBLIC_ORIGIN = $serverOrigin
$env:PORT = [string]$Port
$server = $null; $hostPlayer = $null; $guestPlayer = $null
try {
    $server = Start-Process -FilePath $node -ArgumentList 'online-server.mjs' -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'server.log') -RedirectStandardError (Join-Path $evidence 'server-error.log')
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        try { if ((Invoke-RestMethod ($serverOrigin + '/healthz') -TimeoutSec 1).healthy) { $ready = $true; break } }
        catch { }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) { throw 'Local online server did not become healthy.' }
    $common = @('-force-d3d11', '-screen-fullscreen', '0', '--phase2-capture-dir', ('"' + $evidence + '"'), '--phase2-origin', $serverOrigin)
    $hostArguments = @('-screen-width','1280','-screen-height','720','-logFile',('"' + (Join-Path $evidence 'host-player.log') + '"'),'--phase2-role','host') + $common
    $guestArguments = @('-screen-width','1170','-screen-height','540','-logFile',('"' + (Join-Path $evidence 'guest-player.log') + '"'),'--phase2-role','guest') + $common
    $hostPlayer = Start-Process -FilePath $UnityPlayer -ArgumentList $hostArguments -WindowStyle Hidden -PassThru
    $guestPlayer = Start-Process -FilePath $UnityPlayer -ArgumentList $guestArguments -WindowStyle Hidden -PassThru
    if (-not $hostPlayer.WaitForExit(125000)) { throw 'Host verification timed out.' }
    if (-not $guestPlayer.WaitForExit(125000)) { throw 'Guest verification timed out.' }
    $total = 0
    foreach ($role in @('host','guest')) {
        $path = Join-Path $evidence ($role + '-verification.json')
        if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).LastWriteTimeUtc -lt $runStartedUtc) {
            throw "$role verification report is missing or stale."
        }
        $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $failed = @($report.checks | Where-Object { -not $_.passed })
        Write-Host "$role`: $($report.checks.Count) checks, $($failed.Count) failed."
        $failed | Format-Table name,passed
        $total += $failed.Count
    }
    if ($hostPlayer.ExitCode -ne 0 -or $guestPlayer.ExitCode -ne 0 -or $total) {
        throw "Online verification failed. Host exit $($hostPlayer.ExitCode); guest exit $($guestPlayer.ExitCode). Review $evidence."
    }
    Write-Host "Two-client online verification passed. Evidence: $evidence"
}
finally {
    if ($hostPlayer -and -not $hostPlayer.HasExited) { Stop-Process -Id $hostPlayer.Id -Force }
    if ($guestPlayer -and -not $guestPlayer.HasExited) { Stop-Process -Id $guestPlayer.Id -Force }
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force }
}
