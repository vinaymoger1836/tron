[CmdletBinding()]
param([string]$Adb)
$ErrorActionPreference = 'Stop'
if (-not $Adb) { $Adb = Join-Path $env:LOCALAPPDATA 'Android/Sdk/platform-tools/adb.exe' }
if (-not (Test-Path -LiteralPath $Adb)) { throw 'ADB not found. Supply -Adb with the full path from the Unity Android module.' }
$rows = @(& $Adb devices)
if ($LASTEXITCODE -ne 0) { throw 'ADB could not enumerate devices.' }
$connected = @($rows | Where-Object { $_ -match '^\S+\s+device$' })
$unauthorized = @($rows | Where-Object { $_ -match '\s+unauthorized$' })
if ($connected.Count -eq 0) {
    if ($unauthorized.Count) { throw 'Unlock your phone and accept its USB debugging prompt, then retry.' }
    throw 'No authorized Android device connected. Enable USB debugging and connect your phone using a data cable.'
}
$profiles = @()
foreach ($row in $connected) {
    $deviceId = ($row -split '\s+')[0]
    $profile = [ordered]@{ capturedUtc = [DateTime]::UtcNow.ToString('o') }
    foreach ($entry in @(@('model','ro.product.model'),@('androidVersion','ro.build.version.release'),@('apiLevel','ro.build.version.sdk'),@('abi','ro.product.cpu.abi'),@('soc','ro.soc.model'))) {
        $propertyValue = & $Adb -s $deviceId shell getprop $entry[1]
        if ($LASTEXITCODE -ne 0) { throw "Could not read device property $($entry[0])." }
        $profile[$entry[0]] = ($propertyValue -join '').Trim()
    }
    $profile['screenSize'] = ((& $Adb -s $deviceId shell wm size) -join ' ').Trim()
    $memory = & $Adb -s $deviceId shell cat /proc/meminfo
    $profile['memory'] = ($memory | Where-Object { $_ -match '^MemTotal:' } | Select-Object -First 1).Trim()
    $profiles += [pscustomobject]$profile
}
$evidence = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/android-phase-0'))
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$profiles | ConvertTo-Json -Depth 3 | Set-Content -Encoding UTF8 (Join-Path $evidence 'device-profiles.json')
$profiles | Format-Table model,androidVersion,apiLevel,abi,soc,screenSize
Write-Host 'Saved a device profile without serial numbers to artifacts/android-phase-0/device-profiles.json.'
