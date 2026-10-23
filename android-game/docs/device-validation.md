# Device targets and verification

## Device matrix

| Tier | Device | Android / RAM / GPU | State |
| --- | --- | --- | --- |
| First midrange target | Samsung Galaxy M35 5G, SM-M356B (user's phone) | Android 16 (API 36), 5.3 GiB visible RAM (6 GB variant), Exynos 1380 (s5e8835) with Mali GPU, 1080x2340, display at 60 Hz with 120 Hz available | First baseline captured 2026-10-23 (below); full checklist still open |
| Minimum supported tier | Not selected | Pending | Required before final support policy |
| High-end reference | Not selected | Pending | Required before higher quality preset sign-off |
| Tablet | Not selected | Pending | Required before tablet layout sign-off |

Samsung documents the Galaxy M35 5G with an Exynos 1380 and a display supporting up to 120 Hz. Verify the actual device variant and refresh setting; panel refresh is not a game FPS guarantee.
Source: https://www.samsung.com/in/smartphones/galaxy-m/galaxy-m35-5g-dark-blue-128gb-sm-m356bdbbins/

## First measured baseline - 2026-10-23

Grid Run v0.5.0 (release-packaged APK, debug signed), installed from the website and launched over USB debugging. The user played about four minutes of offline modes, starting at the default Graphics HIGH setting, while `dumpsys SurfaceFlinger --timestats` was sampled every 10 seconds on the game's surface, with battery temperature, thermal status, and memory alongside it. The phone was charging over USB.

| Measure | Result |
| --- | --- |
| Frame rate | Steady 60 fps (about 610 frames per 10 s window), matching the 60 Hz display mode |
| Frame time | Median, p95, and p99 all 16 ms |
| Long frames | 2 frames over 33 ms in the whole session, both in one window |
| Thermal | Battery 32.8 to 34.5 °C; thermal status 0 (no throttling) throughout |
| Memory | 250-310 MB PSS, no growth trend |
| Stability | No crash or ANR |

The user judged the performance good. The log showed one Unity error at launch: `GameObject.CreatePrimitive` tried to add a collider, which the release build strips. v0.5.1 removes those calls; it has not yet been rechecked on the phone. The render surface also reports that 2x MSAA is unsupported and falls back to 1 sample. These are four minutes on a charging phone, not the 20-minute session below, nothing ran at 120 Hz, and whether part of the session used Graphics LOW was not recorded. Evidence (timeline CSV and logcat) is in ignored `artifacts/android-device/session-1/`.

## Targets

- Galaxy M35: sustained 60 FPS, met in the first short session above; confirm over 20 minutes on battery.
- At 60 FPS, a frame has 16.67 ms; at 30 FPS, 33.33 ms. Report median, p95, p99, and long-frame counts rather than just average FPS. Establish acceptance tolerances after the first captured baseline.
- Input acknowledgement: target next rendered frame for a valid steering action. Measure touch behavior on the device, including simultaneous forward and turn input.
- Minimum Android: API 29 provisional, pending representative-device validation.
- Memory, APK/download size, cold launch time, battery use, and thermal budgets: only the first short baseline above exists. Set budgets after a full session; do not invent values.
- Test landscape both directions, cutouts, small phone displays, and tablet layouts. Portrait gameplay is intentionally disabled.

## End-to-end checklist

Some items were written for the Phase 0 probe (HOLD FORWARD, the 60/30 FPS toggle); apply them to the equivalent controls in the current app.

- [ ] Record APK version, build command, Unity version, phone/OS, quality/render scale, refresh setting, and test conditions.
- [x] Install and cold-launch the APK without a blank or pink scene (v0.5.0, 2026-10-23; one launch error, fixed in v0.5.1 and not yet rechecked).
- [ ] Confirm rendered arena, player heading, safe-area HUD, legible labels, and reachable controls.
- [ ] Turn left/right while automatically moving. Confirm forward input does not turn and there is no reverse action.
- [ ] Disable Auto Drive: release forward to stop; hold forward to move; turn and move with separate fingers.
- [ ] Release or slide off Forward; background/foreground the app; confirm no stuck input.
- [ ] Pause: position remains fixed; resume restores movement. Restart resets position and boundary-hit state.
- [ ] Hit each arena boundary, read the cause, and restart successfully.
- [ ] Rotate landscape both ways; confirm controls and pointer cancellation remain correct.
- [ ] Switch 60/30 FPS; record actual achieved frame timing.
- [ ] Test Android Back, notification interruptions, and focus loss; returning requires deliberate resume.
- [ ] Capture phone and tablet screenshots. Editor desktop captures are supplementary.
- [ ] Run a 20-minute session; record frame-time distribution, memory, thermal/battery observations, and any stutter or crashes.
- [ ] Repeat the build from the same pinned project; install and verify it again.

## Evidence

Save sanitized screenshots, logs, profiles, and the completed checklist under `artifacts/android-device/` (Git-ignored; `Check-Device.ps1` writes to `artifacts/android-phase-0/`). `scripts/Check-Device.ps1` writes model/OS/ABI/SoC/memory/screen information without serial numbers. Do not capture account identifiers or real player data.

The on-screen p95 covers a rolling window of up to 240 rendered frame intervals. It is a local diagnostic, includes presentation scheduling, and is not a GPU profiler or a full-session performance report. Use Unity's device profiler for CPU/GPU and memory attribution, then validate a non-development build before commercial performance claims.

Phase 0 is complete only when the rendered APK runs on a physical target device, reproducible build evidence exists, and unresolved device/budget decisions are recorded explicitly.
