# Grid Run Android - Phase 0

An isolated Unity Android project for the first device and movement probe. The browser game remains in `dist/`.

## Status

The source scaffold contains a procedural arena, a movable cycle, touch/keyboard steering, Auto Drive, manual forward input, boundary collision feedback, pause/restart, safe-area UI, and frame-time diagnostics. The editor setup command generates the actual scene, materials, and URP assets.

**This is a Phase 0 probe, not the Arena MVP.** Combat, AI, trail collisions, purchases, and online rooms are later phases. Compilation, screenshots, APK build, and physical-device verification must be recorded before claiming they passed. See `docs/phase-0-status.md`.

## Pinned toolchain

- Unity Editor **6000.3.26f1 LTS**, Windows x86_64.
- URP **17.3.0** and uGUI **2.0.0**, confirmed in the pinned Editor's bundled package manifests; verify project resolution on first import. Keep the generated `packages-lock.json` after successful resolution.
- Matching Android Build Support, SDK/NDK tools, and OpenJDK from Unity Hub.
- The resolved installer modules include OpenJDK 17.0.18+8, NDK r27c, SDK Build Tools 36.0.0, and Android platform 36.
- Development APK: ARM64, IL2CPP, OpenGL ES 3, landscape, linear color, API 29 minimum and API 36 target. The minimum is provisional; store API requirements must be checked again before release.
- Development application ID: `com.gridrun.prototype`. This is not a final store identity.

## First setup

1. Sign in to Unity Hub and activate a license you are eligible to use. Do not put account credentials or license keys in this repository.
2. Install the pinned Editor with **Android Build Support**, **Android SDK & NDK Tools**, and **OpenJDK**. If the CLI install is already running, let it finish rather than starting a second install.
3. Open this `android-game` folder through Unity Hub. Let the packages resolve and scripts compile.
4. Run **Grid Run > Phase 0 > Prepare Project**. It creates `Assets/GridRun/Scenes/PhaseZero.unity`, referenced materials, and the URP renderer/pipeline. Existing scene/material edits are preserved on later preparation runs.
5. Open `PhaseZero.unity` and press Play.

Preparation is intentionally explicit. It changes Android player settings and build scene selection for this prototype. If you cancel the unsaved-scene prompt, preparation/build must stop. Do not run preparation inside a later production project without reviewing these settings.

### Command-line prepare and build

Run from the repository root, with Unity closed for this project:

```powershell
.\android-game\scripts\Build-Android.ps1 -PrepareOnly
.\android-game\scripts\Build-Android.ps1
```

For an Editor outside Unity Hub's default path:

```powershell
.\android-game\scripts\Build-Android.ps1 -UnityEditor 'C:\YourUnityInstall\Editor\Unity.exe'
```

Output: `android-game/Builds/Android/grid-run-phase0.apk`.
Logs: `artifacts/android-phase-0/prepare.log` and `android-build.log`.

The script fails on a missing Editor, a nonzero Editor exit, or a missing APK. The Editor method rejects a different Editor version. Builds use a development/debug signature; production signing and store uploads are outside Phase 0. Build output and evidence are ignored by Git.

## Galaxy M35 installation and controls

1. On the phone, enable Developer options and USB debugging, connect a USB data cable, and accept the phone's debugging prompt.
2. Record the device profile:

```powershell
.\android-game\scripts\Check-Device.ps1
```

Supply `-Adb 'C:\path\to\adb.exe'` if Android platform tools are elsewhere. This command records hardware/software properties without device serial numbers; it does not install or change the phone.

3. With one test device connected, install the development build:

```powershell
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" install -r .\android-game\Builds\Android\grid-run-phase0.apk
```

4. Open **Grid Run - Phase 0** on the phone.

- **LEFT / RIGHT** turn relative to the rider immediately on touch down. There is no reverse input.
- Auto Drive starts enabled. Turn it off to move only while **HOLD FORWARD** is pressed.
- **PAUSE / RESUME**, **RESTART**, and **TARGET: 60 / 30 FPS** are available in the HUD.
- Editor keyboard: A/D or arrows turn; W/Up moves in manual mode; Escape pauses; Enter restarts after a boundary hit.
- Backgrounding or losing focus pauses the probe and clears held touch input. Resume explicitly after returning.
- Cyan arena boundaries stop the cycle. This scene has no damaging trails or combat yet.

## Verification

To build a development desktop player and exercise the real UI event handlers:

```powershell
.\android-game\scripts\Verify-Desktop.ps1
```

This runs only when explicitly requested, captures desktop/phone-shaped/tablet layouts, and reports movement, manual-drive, pause, boundary recovery, FPS-toggle, and control-fit checks under `artifacts/android-phase-0/`. It does not install anything on the phone or prove Android performance.


Use `docs/device-validation.md` for the Phase 0 checklist and evidence format. A desktop screenshot or Editor frame rate is not Galaxy M35 performance evidence. Measure sustained device behavior before finalizing quality budgets.

## Source layout

- `Assets/GridRun/Runtime/ProbeSimulation.cs`: fixed-step movement state, no Unity dependency.
- `Assets/GridRun/Runtime/ArenaProbe.cs`: procedural visualization, input routing, UI, lifecycle, and diagnostics.
- `Assets/GridRun/Runtime/HoldControl.cs`: per-pointer held input with cancel/reset handling.
- `Assets/GridRun/Runtime/SafeArea.cs`: safe-area layout updates.
- `Assets/GridRun/Editor/PhaseZeroBuild.cs`: repeatable asset preparation and Android development build.
- `scripts/`: build and device discovery helpers.
- `docs/`: architecture, device targets, and current verification status.

Preserve `.meta` files, `Assets`, `Packages`, and `ProjectSettings`. Never commit `Library`, builds, logs, signing keys, or captured device evidence.
