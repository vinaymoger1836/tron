# Phase 3 status - 2026-10-20

**State: in progress.** Solar Transit is the first playable Phase 3 slice. Phases 0-2 still have open device, content, and online reliability gates. This is a debug preview, not a feature beta or release build.

## Implemented slice

- TransitRun is a fixed-step, straight timed course using the browser course's two gap widths, 12-second limit, 10.6-unit base speed, jet and sail durations, speed multipliers, and cooldowns. The Unity route advances toward positive Z. The jet crosses the short amber gap; the sail crosses the longer cyan gap. A wrong or late ability causes a named crash, and standing still causes a timeout. The finish requires both gaps cleared.
- TransitGame provides the landing, Auto Drive on by default, touch Forward/JET/SAIL controls, Q/R and W/Up keyboard controls, pause/resume, retry, return to offline Arena, visible timer/checkpoints, active flight visuals, clear results, and a browser-local-equivalent best time in versioned non-sensitive PlayerPrefs.
- The existing offline Arena landing shows Solar Transit when the scene is packaged. The combined Android preview still contains offline Arena and the Phase 2 Online Room. The separate Phase 1 and Phase 2 build commands exclude this new scene.
- PhaseThreeBuild prepares the scene and creates desktop or Android development players without new packages, services, accounts, or server changes.

## Verification

Pinned Unity 6000.3.26f1 built the combined desktop player. android-game/scripts/Verify-Transit.ps1 then ran an opt-in flow in that real player and passed **33/33 checks**: landing and course layout, start, pause freeze, an unassisted jet-gap crash, retry, manual Forward, jet and sail timing, successful finish, local best-time saving, idle timeout, second retry, menu return, offline Arena navigation, and persisted best time after scene navigation. The run saved desktop 1280x720 and phone-landscape 1170x540 screenshots under ignored artifacts/android-phase-3/. The screenshots were visually reviewed; the controls fit both viewports. This does not establish Android touch performance or accessibility on a physical device.

GridRun.Editor.PhaseThreeBuild.BuildAndroid built android-game/Builds/Android/grid-run-phase3-preview.apk on the same source (79,471,888 bytes; SHA-256 7CDACED76BFC3E5E191332F0DD01135CD81120D5FC1EC459A6C1D3D6CD6356CC). Android build-tools verified its v2 signature and manifest: com.gridrun.prototype, version 0.3.0 / code 4, minimum API 29, target API 36, ARM64, and Internet permission. ZIP CRC integrity passed. It is debug signed and Git-ignored. It has not been published to the Vercel site or Play Store.

## Remaining Phase 3 work

- Port Duel, the other maps, Grid Shift, Operations, Daily Signal, and combat-mode jet/sail mechanics with tested rule parity. Solar Transit alone does not complete Phase 3.
- Improve the cycle model, animation, sound, effects, readability and accessibility settings with measured device budgets. The current course uses simple geometry and has not been playtested on a phone.
- Establish import and performance budgets, archive content, and gameplay validation for every advertised mode. The browser game remains the feature reference.
- Complete prior phase gates: the Galaxy M35 install/control/performance checks, Phase 1 Arena content, and Phase 2 four-device online/network tests.

## Reproduce locally

Run android-game/scripts/Verify-Transit.ps1 from the repository root. It builds a desktop development player and saves an ignored report, logs, and screenshots under artifacts/android-phase-3/. The Android build method is GridRun.Editor.PhaseThreeBuild.BuildAndroid. Both the APK and the evidence directory are ignored by Git.
