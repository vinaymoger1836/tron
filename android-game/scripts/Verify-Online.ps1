[CmdletBinding()]
param([int]$Port = 0, [string]$UnityPlayer, [ValidateRange(2, 4)][int]$Seats = 2,
    [ValidateSet('classic', 'solar', 'vault')][string]$Map = 'classic', [ValidateSet('classic', 'decay')][string]$Rule = 'classic')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$variant = $(if ($Seats -ne 2) { "-$Seats-seats" }) + $(if ($Map -ne 'classic') { "-$Map" }) + $(if ($Rule -eq 'decay') { '-fading' })
$evidence = Join-Path $root ('artifacts/android-phase-2/final' + $variant)
$roles = @('host', 'guest', 'guest2', 'guest3')[0..($Seats - 1)]
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
$server = $null; $players = @{}
try {
    $server = Start-Process -FilePath $node -ArgumentList 'online-server.mjs' -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'server.log') -RedirectStandardError (Join-Path $evidence 'server-error.log')
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        try { if ((Invoke-RestMethod ($serverOrigin + '/healthz') -TimeoutSec 1).healthy) { $ready = $true; break } }
        catch { }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) { throw 'Local online server did not become healthy.' }
    $common = @('-force-d3d11', '-screen-fullscreen', '0', '--phase2-capture-dir', ('"' + $evidence + '"'), '--phase2-origin', $serverOrigin, '--phase2-seats', [string]$Seats, '--phase2-map', $Map, '--phase2-rule', $Rule)
    foreach ($role in $roles) {
        $size = if ($role -eq 'host') { @('-screen-width','1280','-screen-height','720') } else { @('-screen-width','1170','-screen-height','540') }
        $arguments = $size + @('-logFile',('"' + (Join-Path $evidence ($role + '-player.log')) + '"'),'--phase2-role',$role) + $common
        $players[$role] = Start-Process -FilePath $UnityPlayer -ArgumentList $arguments -WindowStyle Hidden -PassThru
    }
    foreach ($role in $roles) { if (-not $players[$role].WaitForExit(125000)) { throw "$role verification timed out." } }
    $total = 0
    foreach ($role in $roles) {
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
    $exits = $roles | ForEach-Object { "$_ exit $($players[$_].ExitCode)" }
    if (@($roles | Where-Object { $players[$_].ExitCode -ne 0 }).Count -or $total) {
        throw "Online verification failed. $($exits -join '; '). Review $evidence."
    }
    Write-Host "$Seats-client online verification passed. Evidence: $evidence"
}
finally {
    foreach ($player in $players.Values) { if (-not $player.HasExited) { Stop-Process -Id $player.Id -Force } }
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force }
}
