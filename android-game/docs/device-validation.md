# Phase 0 device targets and verification

## Device matrix

| Tier | Device | Android / RAM / GPU | State |
| --- | --- | --- | --- |
| First midrange target | Samsung Galaxy M35 (user confirmed) | Exact OS, RAM variant, and runtime GPU pending device capture | User will test the APK later; physical validation deferred |
| Minimum supported tier | Not selected | Pending | Required before final support policy |
| High-end reference | Not selected | Pending | Required before higher quality preset sign-off |
| Tablet | Not selected | Pending | Required before tablet layout sign-off |

Samsung documents the Galaxy M35 5G with an Exynos 1380 and a display supporting up to 120 Hz. Verify the actual device variant and refresh setting; panel refresh is not a game FPS guarantee.
Source: https://www.samsung.com/in/smartphones/galaxy-m/galaxy-m35-5g-dark-blue-128gb-sm-m356bdbbins/

## Provisional targets (not measured results)

- Galaxy M35: aim for sustained 60 FPS with a selectable 30 FPS fallback.
- At 60 FPS, a frame has 16.67 ms; at 30 FPS, 33.33 ms. Report median, p95, p99, and long-frame counts rather than just average FPS. Establish acceptance tolerances after the first captured baseline.
- Input acknowledgement: target next rendered frame for a valid steering action. Measure touch behavior on the device, including simultaneous forward and turn input.
- Minimum Android: API 29 provisional, pending representative-device validation.
- Memory, APK/download size, cold launch time, battery use, and thermal budgets: pending measurement; do not invent baseline values.
- Test landscape both directions, cutouts, small phone displays, and tablet layouts. Portrait gameplay is intentionally disabled.

## End-to-end checklist

- [ ] Record APK version, build command, Unity version, phone/OS, quality/render scale, refresh setting, and test conditions.
- [ ] Install and cold-launch the APK without an error or blank/pink scene.
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

Save sanitized screenshots, logs, profiles, and the completed checklist under `artifacts/android-phase-0/` (Git-ignored). `scripts/Check-Device.ps1` writes model/OS/ABI/SoC/memory/screen information without serial numbers. Do not capture account identifiers or real player data.

The on-screen p95 covers a rolling window of up to 240 rendered frame intervals. It is a local diagnostic, includes presentation scheduling, and is not a GPU profiler or a full-session performance report. Use Unity's device profiler for CPU/GPU and memory attribution, then validate a non-development build before commercial performance claims.

Phase 0 is complete only when the rendered APK runs on a physical target device, reproducible build evidence exists, and unresolved device/budget decisions are recorded explicitly.
