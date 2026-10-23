# Phase 1 status - 2026-10-19

**State: in progress.** The first offline Arena slice runs in a separate Unity scene. Phase 0 physical-phone validation is still open, and this is a development preview rather than a Phase 1 release.

**Superseded 2026-10-22:** the offline modes in `phase-3-status.md` replaced this slice. `ArenaMatch` and `DevelopmentArenaCapture` were retired. The `PhaseOneArena` scene now hosts the main menu and the ported rules, which already include the gun, pickups, mode/difficulty/map choices, settings, and records listed below. The device items still apply; the first Galaxy M35 session is in `device-validation.md`.

## Implemented slice

- `ArenaMatch` owns the 120 Hz fixed-step rules for two equal-speed riders: relative left/right turns, no reverse, swept axis-aligned trail/wall collision, an AI rider that checks clearance, three-point match scoring, explicit crash reasons, and a 180-second boundary shrink.
- `ArenaGame` presents a safe-area landscape landing card, touch/keyboard steering, Auto Drive on by default, manual forward, pause/resume, heat result, next heat, and play again. It renders both trails and moves visible boundary walls as the arena contracts. The camera moves back on a crash so wall geometry does not obscure the result.
- `PhaseOneBuild` prepares a separate `PhaseOneArena.unity` scene and builds a Windows verification player or a debug-signed ARM64 Android preview. Phase 0 probe and browser game remain separate.

## Verification

Unity 6000.3.26f1 built the Windows development player. The opt-in desktop flow in `DevelopmentArenaCapture` passed **17/17 checks** for start, pause, manual movement, held forward, touch turn, crash/result, next-heat recovery, screenshot capture, and visible control bounds. Captures were reviewed at desktop 1280x720, phone landscape 1170x540, and tablet 1024x768. Evidence is ignored under `artifacts/android-phase-1/` (`arena-verification.json`, screenshots, build/player logs). The phone result screenshot is readable after moving the camera away from a near-field wall.

Unity also built `Builds/Android/grid-run-phase1-preview.apk` (127,015,886 bytes; SHA-256 `4707167002FE55F74C74B7EC1E84FF6280BF46B09B91E3399222A628EAC161B9`). The clean-output rebuild produced the same size. `apksigner verify --verbose` passed with one signer and APK Signature Scheme v2; `aapt dump badging` reports `com.gridrun.prototype`, version `0.1.0` / code 2, API 29 minimum, API 36 target, and ARM64. The APK remains debug signed and Git-ignored. Its size exceeds the Phase 0 APK by about 50 MB, largely because of ZIP padding before native libraries; the download-size budget and packaging optimization remain open. No physical-device or 20-minute performance result is claimed.

## Remaining Phase 1 work

- Finish art, materials, lighting, original/licensed audio, and effects within measured device budgets.
- Add mounted gun, periodic ammo/nitro/rocket pickups, recognizable icons, and AoE feedback.
- Add mode/difficulty/map selections with only implemented choices enabled; complete the settings modal and archive tabs.
- Add touch-size/placement and camera settings, onboarding, local settings/history with save recovery, audio and haptics, graphics presets, reduced-motion/flashes, and cues beyond color.
- Exercise all crash causes, AI strategy, score-to-three flow, and the full 180-second shrink in a real player session; refine AI and collision behavior from playtests.
- Validate install, controls, safe areas, background/foreground and Back behavior, thermals, sustained frame times, memory, and audio interruption on the Galaxy M35 and other chosen devices. The user will test the APK later.

## Reproduce locally

From `android-game/`, use the pinned Unity Editor in batch mode with `-executeMethod GridRun.Editor.PhaseOneBuild.BuildDesktop` or `GridRun.Editor.PhaseOneBuild.BuildAndroid`. Desktop flow capture is opt-in with `--phase1-capture-dir <ignored output directory>` passed to `Builds/Verification/GridRunArena.exe`. Both `Builds/` and `artifacts/` are Git-ignored. The Android preview stays at `Builds/Android/grid-run-phase1-preview.apk` and is not the production website APK.
