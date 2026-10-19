# Phase 2 status - 2026-10-19

**State: in progress.** The Unity client has a playable two-rider online Arena prototype using the existing Node room service. Phase 0 physical-device validation and Phase 1 content/device work remain open. This is not the online MVP exit gate.

## Prototype decision

Use the existing dependency-free Node server for this prototype. It already owns browser room creation, six-digit online codes, a fixed-step authoritative match, input validation, results, and disconnect waiting. Unity sends input commands and renders server snapshots; `ArenaMatch` does not decide online outcomes. No new host, provider, account, database, or C# match server was added. A final Node-versus-C# server choice remains open until latency, capacity, and maintainability are measured.

The native client uses the server's JSON room API, an in-memory cookie jar for the temporary seat, and NDJSON snapshot streaming. The entered server origin must be HTTPS except for localhost testing. The setup screen defaults to the previously supplied Render origin, but its live reachability was not verified in this session and can be edited in the client. The Android build explicitly requests Internet permission.

## Implemented slice

- A separate online Unity scene supports create/join by six-digit room code, a two-seat Arena lobby on Classic Grid, host start/pause/resume, live server riders/trails, relative turns, Auto Drive toggle, forward/fire holds, heat result, next heat, departure, and reconnection status. Unsupported modes, maps, seat counts, and wall rules display a clear leave-room message.
- The combined Phase 2 build starts at the offline Arena landing, opens Online Room, and can return to offline play after leaving a room. The Phase 1-only build hides the online link when the online scene is absent.
- The server remains authoritative. Unity does not advance local online collision, score, or pickup state.

## Verification

`GridRun.Editor.PhaseTwoBuild.BuildDesktop` compiled and built the combined desktop player with Unity 6000.3.26f1. `scripts/Verify-Online.ps1` started one local online server and two actual Unity desktop players. The final run passed **50/50 checks** (host 34, guest 16): numeric room creation, invalid-code explanation and recovery, join, deliberate guest stream interruption and recovery to the same seat, host start, server tick advance, two rendered riders, host pause/resume, result with crash causes, next heat, departure causing server connection-wait, and online/offline scene navigation. Phone-shaped 1170x540 and desktop 1280x720 host screenshots were reviewed for setup, lobby, match, result, and offline landing. All visible buttons fit their viewports. The second hidden Unity window rendered black screenshots on this Windows host, so guest behavior and control bounds were checked without claiming visual guest evidence. Logs, room code, reports, and screenshots are Git-ignored under `artifacts/android-phase-2/final/`.

`GridRun.Editor.PhaseTwoBuild.BuildAndroid` also completed on the final source. The local debug-signed APK is `android-game/Builds/Android/grid-run-phase2-preview.apk` (132,152,715 bytes; SHA-256 `DE1894310BA11DED7D48D5D9C5B814B619B8796630983B2CD1DAF7F8440D54CF`). Android build-tools verified its v2 signature and manifest: `com.gridrun.prototype`, version `0.2.0`/code 3, minimum API 29, ARM64, and `android.permission.INTERNET`. Its ZIP payload is much smaller than the file because of substantial padding between entries in this Unity build; package size needs investigation and reduction before a website release. The APK was not published to Vercel. No physical-phone networking, mobile data transition, or thermal result is claimed.

## Remaining Phase 2 work

- Finish Phase 1 gameplay and visual parity. The offline Unity Arena currently ends at three wins, while the existing online Node Arena ends after exactly three heats. Online snapshots include bullets/pickups and other modes, but this Unity scene currently renders only riders and trails on Classic Grid.
- Add 2-4 seat choice, supported map/rule selection, ready state, names and noncolor cues in-world, full weapon/pickup/effect rendering, interpolation and command ordering, and robust error/status messages during play.
- Define a versioned protocol with clear old-client errors; test reconnect, timeout, stale-room cleanup, server restart, host departure, app backgrounding, and network changes on devices.
- Measure latency, jitter, bandwidth, server tick time, concurrent rooms, capacity, and cost. Test four devices with simulated 50/150/250 ms latency and 1-5% loss where practical.
- Validate Android HTTP streaming/cookies, touch controls, safe areas, and power/network interruption on the Galaxy M35. The user plans to test the APK later.

## Reproduce locally

Build the desktop player from `android-game/` using pinned Unity with `-executeMethod GridRun.Editor.PhaseTwoBuild.BuildDesktop`, then run `android-game/scripts/Verify-Online.ps1` from the repository root. The script uses a localhost-only online server, two hidden Unity players, and ignored evidence. The Android build command is `GridRun.Editor.PhaseTwoBuild.BuildAndroid`; its preview APK is ignored under `android-game/Builds/Android/` and is not the website download.
