[CmdletBinding()]
param(
    [string]$UnityEditor,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$versionLine = Get-Content (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') | Select-Object -First 1
$version = ($versionLine -split ':', 2)[1].Trim()
if (-not $UnityEditor) {
    $UnityEditor = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"
}
if (-not (Test-Path -LiteralPath $UnityEditor -PathType Leaf)) {
    throw "Unity $version was not found. Install it with Android Build Support, SDK/NDK and OpenJDK, or supply -UnityEditor with its full path."
}
$UnityEditor = (Resolve-Path -LiteralPath $UnityEditor).Path
$editorFolder = Split-Path -Parent $UnityEditor
$requiredFiles = @('Data/Resources/PackageManager/Server/UnityPackageManager.exe')
if (-not $PrepareOnly) {
    $requiredFiles += @('Data/PlaybackEngines/AndroidPlayer/UnityEditor.Android.Extensions.dll', 'Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/java.exe', 'Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe', 'Data/PlaybackEngines/AndroidPlayer/NDK/source.properties')
}
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $editorFolder $requiredFile))) {
        throw "Unity/Android installation is incomplete: $requiredFile is missing. Let installation finish or add the matching Android modules in Unity Hub."
    }
}
$evidence = [IO.Path]::GetFullPath((Join-Path $projectRoot '../artifacts/android-phase-0'))
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$method = if ($PrepareOnly) { 'GridRun.Editor.PhaseZeroBuild.Prepare' } else { 'GridRun.Editor.PhaseZeroBuild.BuildAndroid' }
$logFile = Join-Path $evidence $(if ($PrepareOnly) { 'prepare.log' } else { 'android-build.log' })
$buildTarget = if ($PrepareOnly) { 'StandaloneWindows64' } else { 'Android' }
$editorArguments = @('-batchmode', '-quit', '-projectPath', ('"' + $projectRoot + '"'), '-buildTarget', $buildTarget, '-executeMethod', $method, '-logFile', ('"' + $logFile + '"'))
$buildStarted = [DateTime]::UtcNow
Write-Host "Running Unity $version. Log: $logFile"
$editorProcess = Start-Process -FilePath $UnityEditor -ArgumentList $editorArguments -WindowStyle Hidden -PassThru -Wait
if ($editorProcess.ExitCode -ne 0) { throw "Unity exited with code $($editorProcess.ExitCode). Review $logFile." }
if (-not $PrepareOnly) {
    $apk = Join-Path $projectRoot 'Builds/Android/grid-run-phase0.apk'
    if (-not (Test-Path -LiteralPath $apk)) { throw "Unity exited without producing $apk. Review $logFile." }
    if ((Get-Item -LiteralPath $logFile).LastWriteTimeUtc -lt $buildStarted -or -not (Select-String -LiteralPath $logFile -SimpleMatch "PHASE0_APK_READY" -Quiet)) { throw "The build success marker is missing. Review $logFile." }
    Write-Host "Development APK: $apk"
}
