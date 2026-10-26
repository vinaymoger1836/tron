# Grid Run for Android

A separate Unity Android project that ports the browser game in `dist/`. The browser modules remain the behavior reference.

## Status

The app is **Grid Run** v0.5.1 (code 7). It opens on a main menu with New Game, Online Game, Solar Transit, Records, and Settings, and it plays every offline mode: Duel, Arena, Daily Signal, Grid Shift, Operations and Journey, Solar Transit, and the Play lesson. The rules come from an exact C# port of the browser modules (`Assets/GridRun/Runtime/Sim/`), proven tick-for-tick against browser traces. The Online Room is a client for the existing Node server; the current source plays Arena, Duel, and Grid Shift for 2-4 riders on all three maps, with the solo game's renderer and controls (the v0.5.1 APK predates this: two seats on Classic Grid); online play for the other modes is deferred.

v0.5.0 ran on a Galaxy M35 at a steady 60 fps with no throttling (see `docs/device-validation.md`). The APK is still debug signed with the development application ID, so it is not a Play Store release. Per-phase detail is in `docs/phase-0-status.md` through `docs/phase-4-status.md`.

## Pinned toolchain

- Unity Editor **6000.3.26f1 LTS**, Windows x86_64.
- URP **17.3.0** and uGUI **2.0.0**, confirmed in the pinned Editor's bundled package manifests. Keep the generated `packages-lock.json`.
- Matching Android Build Support, SDK/NDK tools, and OpenJDK from Unity Hub. The resolved modules include OpenJDK 17.0.18+8, NDK r27c, SDK Build Tools 36.0.0, and Android platform 36.
- Player: ARM64, IL2CPP, OpenGL ES 3, landscape, linear color, API 29 minimum and API 36 target. The minimum is provisional; check store API requirements again before release.
- Development application ID: `com.gridrun.prototype`. This is not a final store identity; changing it later creates a separate app.

## First setup

1. Sign in to Unity Hub and activate a license you are eligible to use. Do not put account credentials or license keys in this repository.
2. Install the pinned Editor with **Android Build Support**, **Android SDK & NDK Tools**, and **OpenJDK**.
3. Open this `android-game` folder through Unity Hub and let packages resolve and scripts compile.
4. Run **Grid Run > Phase 4 > Prepare Cosmetic Preview**. It prepares every scene and regenerates the Grid look materials, and it sets the shipped product name, version, and scene list. Each `Phase*Build.Prepare` rewrites those player settings for its own phase, so run the Phase 4 one last.
5. Open `Assets/GridRun/Scenes/PhaseOneArena.unity` (the main menu) and press Play.

## Build

Run from the repository root with Unity closed for this project. The release-packaged APK is the one players get:

```powershell
$unity = "$env:ProgramFiles\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe"
& $unity -batchmode -quit -projectPath android-game -buildTarget Android -executeMethod GridRun.Editor.PhaseFourBuild.BuildAndroidRelease -logFile artifacts/android-build.log
```

Output: `android-game/Builds/Android/grid-run.apk` (about 36 MB, no development watermark, debugger, or verification flows). `PhaseFourBuild.BuildAndroid` makes the larger development APK, and `PhaseFourBuild.BuildDesktop` makes the Windows player that the verification scripts drive. Builds use a debug signature; production signing and store uploads are separate release decisions. Build output and evidence are ignored by Git.

To publish a build to the website's download card, see "Android APK download" in the repository `README.md` (`scripts/publish-android-release.mjs`, which uses GitHub Releases).

## Install on a phone

1. On the phone, enable Developer options and USB debugging, connect a USB data cable, choose **Transferring files** in the USB notification, and accept the debugging prompt.
2. Record the device profile (model, Android version, ABI, SoC, screen, and memory, without serial numbers):

```powershell
.\android-game\scripts\Check-Device.ps1
```

3. Install over any earlier version, which keeps local records and settings:

```powershell
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" install -r .\android-game\Builds\Android\grid-run.apk
```

Players can also download the APK from the website and allow installation from that source.

Controls: **LEFT / RIGHT** turn relative to the rider, Auto Drive is on by default (hold **FORWARD** when it is off), and **FIRE**, **JET**, **SAIL**, and **PAUSE** are on screen. Grid Shift adds **FORM**, an on-foot direction pad, and **DISC**. Keyboard: A/D or arrows turn, W forward, E fire, Q jet, R sail, Esc or P pause; in Grid Shift, F switches form, WASD moves on foot, and Space throws the disc. Settings holds Graphics HIGH/LOW, the Auto Drive default, Trail Look, and Sound.

## Verification

Each script builds a desktop player (unless noted) and writes an ignored report, logs, and screenshots under `artifacts/`. They prove desktop behavior only.

| Script | What it checks |
| --- | --- |
| `Verify-SimParity.ps1` | Browser vs C# rules, tick-exact, over 29 scenarios |
| `Verify-Solo.ps1` | Every offline mode end to end through the real controls |
| `Verify-Transit.ps1` | Solar Transit course, jet, sail, timeout, retry |
| `Verify-Showroom.ps1` | Cosmetic showroom (desktop player only) and the shipped menu |
| `Verify-Online.ps1` | A local room server and one Unity client per seat (`-Seats 2`, `3`, or `4`; `-Map classic|solar|vault`; `-Rule classic|decay`) |
| `Verify-Desktop.ps1` | The Phase 0 movement probe |
| `Capture-Look.ps1` | Art-review screenshots, no assertions (`-SkipBuild` reuses the player) |
| `Build-Android.ps1` | The Phase 0 probe APK, or `-PrepareOnly` for Phase 0 preparation |

The hidden verification players read real keyboard input, so don't type on the machine while a run is in progress. Use `docs/device-validation.md` for phone evidence; a desktop frame rate is not device performance.

## Source layout

- `Assets/GridRun/Runtime/Sim/`: the browser rules in plain C# (`GridRun.Sim`); change rules here and in `dist/` together.
- `Assets/GridRun/Runtime/Solo/`: the offline game (flow, world rendering, HUD and radar, local profile, audio, crash replay).
- `Assets/GridRun/Runtime/`: main menu and settings (`ArenaGame`), `TransitGame`, `OnlineArenaGame` and `OnlineRoomClient`, `CosmeticShowroom`, the Phase 0 `ArenaProbe`, and shared visuals (`GridLook`, `GridMesh`, `CycleVisual`, `RunnerVisual`, `ChaseCamera`, `UiSkin`, `SafeArea`, `HoldControl`).
- `Assets/GridRun/Shaders/`: grid floor, light wall, and sky dome.
- `Assets/GridRun/Editor/`: per-phase prepare and build methods, Grid look asset generation, and the rules parity check.
- `scripts/`: build, device, and verification helpers. `docs/`: architecture, device targets, and phase status.

Preserve `.meta` files, `Assets`, `Packages`, and `ProjectSettings`. Never commit `Library`, builds, logs, signing keys, or captured device evidence.
