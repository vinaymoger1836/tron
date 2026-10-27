[CmdletBinding()]
param([int]$Port = 0, [string]$UnityPlayer, [ValidateRange(2, 4)][int]$Seats = 2,
    [ValidateSet('classic', 'solar', 'vault')][string]$Map = 'classic', [ValidateSet('classic', 'decay')][string]$Rule = 'classic',
    [ValidateSet('arena', 'duel', 'shift')][string]$Mode = 'arena',
    # Instead of a match: HTTP checks of the server's protocol refusals, then one Unity client posing as an outdated build.
    [switch]$ProtocolCheck,
    # Instead of the standard match: interrupt (3 riders, 10 s hold: background drops, a forfeit, a released seat)
    # or close (2 riders: a frozen server, a server restart, and the host closing the room).
    [ValidateSet('', 'interrupt', 'close')][string]$Scenario = '')
$ErrorActionPreference = 'Stop'
if ($Scenario) { $Seats = $(if ($Scenario -eq 'interrupt') { 3 } else { 2 }); $Map = 'classic'; $Rule = 'classic'; $Mode = 'arena' }
if ($Rule -eq 'decay' -and $Mode -ne 'arena') { throw 'Fading walls exist only in Arena.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$variant = $(if ($Mode -ne 'arena') { "-$Mode" }) + $(if ($Seats -ne 2) { "-$Seats-seats" }) + $(if ($Map -ne 'classic') { "-$Map" }) + $(if ($Rule -eq 'decay') { '-fading' })
if ($ProtocolCheck) { $variant = '-protocol' }
if ($Scenario) { $variant = '-' + $Scenario }
$evidence = Join-Path $root ('artifacts/android-phase-2/final' + $variant)
$roles = if ($ProtocolCheck) { @('outdated') } else { @('host', 'guest', 'guest2', 'guest3')[0..($Seats - 1)] }
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
if (-not $UnityPlayer) { $UnityPlayer = Join-Path $root 'android-game/Builds/Verification/GridRunOnline.exe' }
if (-not (Test-Path -LiteralPath $UnityPlayer)) { throw 'Build the Phase 2 desktop player first.' }
foreach ($stale in @('room-code.txt', 'room-code-2.txt', 'freeze.request', 'restart.request', 'guest-returned.txt')) {
    $path = Join-Path $evidence $stale
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
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
$env:TRON_RECONNECT_MS = $(if ($Scenario -eq 'interrupt') { '10000' } else { $null })
# The close scenario freezes the server process to imitate a connection that dies without an error.
Add-Type -Namespace GridRunVerify -Name Process -MemberDefinition @'
[DllImport("ntdll.dll")] public static extern int NtSuspendProcess(IntPtr handle);
[DllImport("ntdll.dll")] public static extern int NtResumeProcess(IntPtr handle);
'@
function Start-RoomServer([string]$log) {
    $process = Start-Process -FilePath $node -ArgumentList 'online-server.mjs' -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence "$log.log") -RedirectStandardError (Join-Path $evidence "$log-error.log")
    for ($i = 0; $i -lt 40; $i++) {
        try { if ((Invoke-RestMethod ($serverOrigin + '/healthz') -TimeoutSec 1).healthy) { return $process } }
        catch { }
        Start-Sleep -Milliseconds 200
    }
    throw 'Local online server did not become healthy.'
}
$server = $null; $players = @{}; $serverEvents = @()
try {
    $server = Start-RoomServer 'server'
    $httpChecks = @()
    if ($ProtocolCheck) {
        function Invoke-Room([string]$path, $body) {
            try {
                $reply = Invoke-WebRequest -UseBasicParsing -Method Post -Uri ($serverOrigin + $path) -ContentType 'application/json' -Body ($body | ConvertTo-Json -Compress) -TimeoutSec 5
                return @{ status = [int]$reply.StatusCode; text = $reply.Content }
            } catch {
                if (-not $_.Exception.Response) { throw }
                return @{ status = [int]$_.Exception.Response.StatusCode; text = [string]$_.ErrorDetails.Message }
            }
        }
        $status = Invoke-RestMethod ($serverOrigin + '/api/lan/status') -TimeoutSec 5
        $httpChecks += [pscustomobject]@{ name = 'Status reports protocol 1, minimum 1'; passed = ($status.protocol -eq 1 -and $status.minProtocol -eq 1) }
        $httpChecks += [pscustomobject]@{ name = 'Status reports a 2 s heartbeat and a 30 s seat hold'; passed = ($status.heartbeatMs -eq 2000 -and $status.reconnectMs -eq 30000) }
        $create = @{ name = 'CHECK'; map = 'classic'; mode = 'arena'; maxPlayers = 2 }
        $old = Invoke-Room '/api/rooms' ($create + @{ protocol = 0 })
        $httpChecks += [pscustomobject]@{ name = 'Server refuses an older app with an update message'; passed = ($old.status -eq 426 -and $old.text -like '*Update Grid Run*') }
        $newer = Invoke-Room '/api/rooms' ($create + @{ protocol = 2 })
        $httpChecks += [pscustomobject]@{ name = 'Server explains when it is older than the app'; passed = ($newer.status -eq 426 -and $newer.text -like '*older than your app*') }
        $legacy = Invoke-Room '/api/rooms' $create
        $httpChecks += [pscustomobject]@{ name = 'Clients that send no version still create rooms'; passed = ($legacy.status -eq 201) }
        $current = Invoke-Room '/api/rooms' ($create + @{ protocol = 1 })
        $httpChecks += [pscustomobject]@{ name = 'Protocol 1 creates rooms'; passed = ($current.status -eq 201) }
        $join = Invoke-Room ('/api/rooms/' + ($current.text | ConvertFrom-Json).code + '/join') @{ name = 'OLD'; protocol = 0 }
        $httpChecks += [pscustomobject]@{ name = 'Server refuses an older app joining'; passed = ($join.status -eq 426 -and $join.text -like '*Update Grid Run*') }
        $httpChecks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'protocol-http.json') -Encoding utf8
    }
    $common = @('-force-d3d11', '-screen-fullscreen', '0', '--phase2-capture-dir', ('"' + $evidence + '"'), '--phase2-origin', $serverOrigin, '--phase2-seats', [string]$Seats, '--phase2-map', $Map, '--phase2-rule', $Rule, '--phase2-mode', $Mode)
    if ($Scenario) { $common += @('--phase2-scenario', $Scenario) }
    foreach ($role in $roles) {
        # Windows launched at phone size capture black on this host; the window that takes screenshots starts at 1280x720.
        $size = if ($role -eq 'host' -or $role -eq 'outdated') { @('-screen-width','1280','-screen-height','720') } else { @('-screen-width','1170','-screen-height','540') }
        $arguments = $size + @('-logFile',('"' + (Join-Path $evidence ($role + '-player.log')) + '"'),'--phase2-role',$role) + $common
        $players[$role] = Start-Process -FilePath $UnityPlayer -ArgumentList $arguments -WindowStyle Hidden -PassThru
    }
    $deadline = [DateTime]::UtcNow.AddSeconds($(if ($Scenario) { 180 } else { 125 }))
    while (@($roles | Where-Object { -not $players[$_].HasExited }).Count) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Online verification timed out.' }
        $freeze = Join-Path $evidence 'freeze.request'
        $restart = Join-Path $evidence 'restart.request'
        if (Test-Path -LiteralPath $freeze) {
            Remove-Item -LiteralPath $freeze -Force
            [void][GridRunVerify.Process]::NtSuspendProcess($server.Handle)
            $serverEvents += "server frozen at $([DateTime]::UtcNow.ToString('o'))"
            Start-Sleep -Seconds 12
            [void][GridRunVerify.Process]::NtResumeProcess($server.Handle)
            $serverEvents += "server resumed at $([DateTime]::UtcNow.ToString('o'))"
        }
        if (Test-Path -LiteralPath $restart) {
            Remove-Item -LiteralPath $restart -Force
            Stop-Process -Id $server.Id -Force
            $server.WaitForExit()
            $serverEvents += "server stopped at $([DateTime]::UtcNow.ToString('o'))"
            $server = Start-RoomServer 'server-restarted'
            $serverEvents += "server restarted at $([DateTime]::UtcNow.ToString('o'))"
        }
        Start-Sleep -Milliseconds 100
    }
    if ($serverEvents.Count) { $serverEvents | Set-Content -LiteralPath (Join-Path $evidence 'server-events.txt') -Encoding utf8 }
    $total = @($httpChecks | Where-Object { -not $_.passed }).Count
    if ($httpChecks.Count) {
        Write-Host "server HTTP: $($httpChecks.Count) checks, $total failed."
        $httpChecks | Where-Object { -not $_.passed } | Format-Table name,passed
    }
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
    Write-Host $(if ($ProtocolCheck) { 'Protocol check passed.' } elseif ($Scenario) { "Online $Scenario scenario passed." } else { "$Seats-client online verification passed." }) "Evidence: $evidence"
}
finally {
    foreach ($player in $players.Values) { if (-not $player.HasExited) { Stop-Process -Id $player.Id -Force } }
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force }
}
