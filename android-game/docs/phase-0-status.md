# Phase 0 status - 2026-10-17

**State: in progress.** The source project, generated scene, and installable development APK are ready. Physical-device validation and final performance/support budgets remain open.

## Completed

- Unity 6000.3.26f1 LTS installed with Android Build Support, SDK/NDK, and OpenJDK. The Editor imported and compiled the project successfully.
- Project pins URP 17.3.0 and uGUI 2.0.0. Their versions were confirmed in the installed Editor's bundled package manifests, and Unity generated `Packages/packages-lock.json`.
- A scene, materials, and URP renderer/pipeline were generated through `GridRun.Editor.PhaseZeroBuild.Prepare`. The scene contains a procedural grid, boundary, cycle, following camera, and safe-area HUD.
- The movement probe handles relative turns, Auto Drive/manual forward input, boundary stop, pause/resume, restart, background input clearing, and 30/60 FPS target selection. Desktop capture ignores OS focus loss only when the explicit verification flag is present.
- `scripts/Verify-Desktop.ps1` built and ran a Unity desktop development player. **17/17 checks passed**: movement, pause, manual drive, touch pointer turn, keyboard submit, boundary recovery, 30/60 target toggle, screenshot capture, and control bounds at desktop 1280x720, phone-shaped 1170x540, and tablet 1024x768. The revised phone and tablet screenshots were visually reviewed for HUD contrast and control readability. Report and PNGs are under `artifacts/android-phase-0/`.
- `scripts/Build-Android.ps1` produced `Builds/Android/grid-run-phase0.apk` (76,395,996 bytes). Unity reported build success. The APK ZIP integrity check passed: 443 entries, including ARM64 `libil2cpp.so`, `libunity.so`, and other required native libraries.
- Android `apksigner verify --verbose` passed with one signer and APK Signature Scheme v2. `aapt dump badging` reports package `com.gridrun.prototype`, version `0.0.1` / code 1, minimum API 29, target/compile API 36, and `arm64-v8a` native code.
- `scripts/Check-Device.ps1` currently reports no authorized Android phone; it records no serial numbers when a device is connected.
- Unity caches, APKs, screenshots, logs, keystores, and other generated files are Git-ignored. Source assets, their `.meta` files, `Packages`, and `ProjectSettings` remain visible to Git.

## Checks and evidence

Run from repository root:

```powershell
.\android-game\scripts\Build-Android.ps1 -PrepareOnly
.\android-game\scripts\Verify-Desktop.ps1
.\android-game\scripts\Build-Android.ps1
.\android-game\scripts\Check-Device.ps1
```

Observed: preparation passed after an unsupported `com.unity.modules.textrendering` manifest entry was removed; desktop verification passed 17/17 on the final run; Android build passed; device check correctly reported no connected phone. The initial `apksigner` attempt lacked `JAVA_HOME` in the shell; it passed when pointed to Unity's bundled OpenJDK. Earlier failed desktop runs exposed verification harness timing and hidden-window focus behavior; the final run passed after those were corrected.

Ignored evidence: `artifacts/android-phase-0/prepare.log`, `desktop-build.log`, `desktop-player.log`, `desktop-verification.json`, `desktop.png`, `phone-landscape.png`, `tablet.png`, `android-build.log`, and installer logs. The APK stays under ignored `android-game/Builds/Android/`.

## Pending Phase 0 gate

1. User will connect the Samsung Galaxy M35 and test the APK later. Capture exact Android version, RAM/SoC/GPU details, screen, screenshots, touch behavior, interruptions, and a 20-minute performance/thermal session using `docs/device-validation.md`.
2. Choose minimum-tier, high-end, and tablet reference devices. Validate the provisional API 29 compatibility floor and device support policy.
3. Establish measured frame-time tolerance, memory, launch-time, download-size, battery, and thermal budgets from physical-device evidence.
4. Confirm Unity license tier and eligibility for eventual commercial distribution. The local Unity license was active for this build; that does not establish commercial plan eligibility.

The current app is a movement and rendering probe. It is not the Arena MVP, and no in-app purchases or online rooms are in the Unity client yet. The Play Store identity/signing and release policy checks are later-phase work.
