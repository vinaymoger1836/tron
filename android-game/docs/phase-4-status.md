# Phase 4 status - 2026-10-21

**State: in progress.** The first Phase 4 slice is a cosmetic showroom with no checkout; since 2026-10-22 it is excluded from Android builds (see "Preview UI removed"). The shipped app is Grid Run v0.5.1, published through GitHub Releases. Phases 0-3 retain open device, content, and network gates. This is not a commercial release or an ownership system.

## Implemented slice

- CosmeticCatalog defines catalog version 1 and six stable concept IDs: default and Solar variants for a cycle, a trail, and a set. It has no prices, purchase callbacks, store product IDs, or ownership grants.
- CosmeticShowroom provides a 3D preview, category buttons, previous/next selection, clear names/descriptions, and explicit default-versus-concept status. The concept items say they are unavailable to equip or buy. It offers no purchase action and stores no entitlement.
- The offline Arena landing links to Style Preview when PhaseFourShowroom is packaged. The existing offline Arena, Online Room, and Solar Transit scenes remain in the combined Phase 4 preview.
- PhaseFourBuild prepares the scene and builds development desktop and Android players. No SDK, account, database, or service was added.

## Visual refinement

A shared procedural CycleVisual now replaces the cube-based cycles and riders in the prototype, offline Arena, online rooms, Solar Transit, and the showroom. It has a tapered fairing, helmet and visor, two lit wheel rims, animated wheel spin and turn lean, a pulsing light-jet plume, and a deploying solar sail. The showroom camera, display trail, and body contrast were adjusted so the cycle is readable at phone and desktop sizes. A generated rounded UI shape gives menus and controls a consistent edge. This is a clearer playable art baseline; authored models, materials, character animation, effects, and sound are still needed for the intended production look.

## Grid look - 2026-10-22

The user asked for realistic TRON-style characters, vehicles, and arenas instead of block shapes and flat colour. They chose code-built models first, with Blender-scripted models as the fallback if this is not satisfactory.

- CycleVisual was rebuilt from lofted, lathed, and swept meshes: a sculpted glossy-black shell, hubless ring wheels with luminous side rings and tread lines, fenders with light edges, a prone rider (helmet, visor line, suit circuit lines, back identity disc), deployable light-jet wings with twin thrusters, and a solar sail with lit ribs. Meshes are built once and shared; the rider and shell use Lit materials while team colour lives only in the light lines, so the rival is a black cycle with amber lines.
- Every scene now uses GridLook: reflective procedural grid floor, glowing floor pools under cycles, spectator tiers and a back wall around arenas, a fogged distant skyline, translucent light-wall trails and boundaries, HDR bloom, tonemapping, and environment reflections. Gameplay rules, hitboxes, cameras, and controls are unchanged.
- GFX: HIGH/LOW on the Arena landing (persisted in PlayerPrefs, `--gridrun-graphics low|high` on the command line) turns HDR, post-processing, and planar reflections on or off.
- `scripts/Capture-Look.ps1` builds the combined desktop player and saves art-review screenshots (showroom, arena gameplay and close views, low quality, transit jet and sail) to ignored `artifacts/android-look/`. It does not assert gameplay.

These visuals are original procedural designs in a digital-arena style; they copy no film assets. They are still not authored production art, and no device frame time has been measured with them.

Verification on 2026-10-22 after the change: showroom 27/27, Solar Transit 33/33, two-client online 50/50 (host 34, guest 16), offline Arena (Phase 1 flow) 17/17, and Phase 0 probe desktop checks passed. GridRun.Editor.PhaseFourBuild.BuildAndroid built the debug-signed grid-run-phase4-preview.apk (132,677,119 bytes; SHA-256 137B9726BC5BDB383897EF33703ACDDB581908FDD0DBE208E1F461276DD2A399) with no shader or script errors. That APK has not been installed or profiled on a phone.

The 132.7 MB development APK exceeded Vercel Hobby's 100 MB CLI upload limit (the production deploy was rejected, leaving production unchanged). GridRun.Editor.PhaseFourBuild.BuildAndroidRelease packages the same content and debug signature without development-build debugging data: grid-run-phase4-release.apk is 36,098,572 bytes (SHA-256 219D4DFF9262EC4CCE5AFE38924CDC738FD218054929BDD7B17C14E1337D293F), com.gridrun.prototype 0.4.0 / code 5, API 29 minimum / 36 target, ARM64, Internet permission, APK Signature Scheme v2 verified. On 2026-10-22, at the user's request, it was staged with scripts/stage-android-apk.mjs and deployed to Vercel production (https://tron-flax.vercel.app); the live /downloads/grid-run-android.apk returned the same SHA-256. Development capture flows are compiled out of this build. It remains an untested preview, not a Play Store release.

## Chase camera - 2026-10-22

Offline Arena, Online Room, and the Phase 0 probe replaced the fixed north-up overhead camera with the browser game's default chase view (`cameraPose()` chase mode in dist/game.js): 14 units behind and 5 to the right of the rider at 10.2 height, aimed 5 ahead, with the same exponential smoothing (rate 6), so the view swings around with each turn. The crash overview is unchanged. Shared logic is in Runtime/ChaseCamera.cs. Desktop checks after the change: Phase 1 Arena 17/17, Phase 0 probe passed, two-client online 50/50, showroom 27/27. The APK on Vercel predates this change.

## Main menu and interface style - 2026-10-22

The app now opens on a full-screen main menu instead of the small Arena card (whose buttons overlapped its description). A slow camera orbit shows the parked cycle on the right; the left column has the GRID RUN title and NEW GAME, ONLINE GAME, SOLAR TRANSIT, STYLE PREVIEW, and SETTINGS. Settings (dimmed modal; Esc/Back closes) holds Graphics HIGH/LOW, a persisted Auto Drive default (`gridrun.autodrive.v1`, on unless changed; the in-heat AUTO toggle is not persisted), and a controls summary. NEW GAME is focused for keyboard/controller Enter. UiSkin now generates one shared style for every scene: dark glass frames with chamfered corners, a thin cyan edge, and an inner glow; a primary variant marks each screen's main action. Return buttons in Transit, Online, and Style Preview now read MAIN MENU. Control object names used by the verifiers are unchanged. Reviewed at desktop 1280x720, phone 1170x540, and tablet 1024x768 via Capture-Look.ps1; desktop checks after the change: Phase 1 Arena 17/17, Phase 0 passed, Transit 33/33, online 50/50, showroom 27/27. The text still uses Unity's built-in font; a licensed display font would be a separate asset decision.

## Verification

Pinned Unity 6000.3.26f1 built desktop players. The final showroom run passed **27/27 checks** and saved phone-landscape 1170x540, desktop 1280x720, and tablet 1024x768 screenshots under ignored artifacts/android-phase-4/. The final Solar Transit run passed **33/33 checks**, including jet, sail, timeout, retry, and phone/desktop layout checks. The two-client online run passed **34/34 host and 16/16 guest checks**, and the movement prototype passed **17/17 checks**. The showroom phone and desktop screenshots were visually reviewed. These desktop runs do not validate physical Android touch, sustained device performance, or store behavior.

GridRun.Editor.PhaseFourBuild.BuildAndroid built android-game/Builds/Android/grid-run-phase4-preview.apk (132,475,251 bytes; SHA-256 775AA463A5EAFD3C0E1C817B1AE8385040CA56728354B5B4514A3FCF4D10318B). Android build-tools verified APK Signature Scheme v2, package com.gridrun.prototype, version 0.4.0 / code 5, minimum API 29, target API 36, and ARM64. ZIP CRC integrity passed. The APK is debug signed and Git-ignored. It has not been published.

## Guest-only purchase direction

The user chose no login or signup for now. A proposed Android-only purchase flow uses Unity IAP and Google Play for direct, non-consumable cosmetics. The app would fetch the Play account's owned purchases on startup and after checkout. The existing Node game server could verify each purchase token with Google and store an idempotent, durable grant in managed Postgres. A temporary guest session could carry requests, but it would not prove ownership. The verified, active Play purchase would be the basis for restoring an item after reinstall or a device change. This is a proposal, not an approved implementation; the showroom remains preview-only.

Restoration requires the same Google Play account and a successful store fetch and server verification. An offline or failed product/purchase fetch must show a retryable verification state, not silently remove an owned item. This flow does not restore local progress, browser purchases, or purchases made through a different Play account. It does not provide cross-platform ownership or account transfer. Limit the first shop to non-consumables; currency wallets, consumables, and subscriptions need separate recovery designs.

Unity documents Play restoration through FetchPurchases and warns that unavailable product metadata can omit owned purchases: https://docs.unity.com/en-us/iap/restore-purchases
Unity documents pending callbacks, repeated delivery, and confirmation after fulfillment: https://docs.unity.com/en-us/iap/purchases
Google recommends backend purchase-token verification before granting ownership: https://developer.android.com/google/play/billing/security
Google describes purchase lifecycle, notifications, and voided purchases: https://developer.android.com/google/play/billing/backend
Render documents managed Postgres and pricing: https://render.com/docs/postgresql and https://render.com/pricing

This direction still requires approval for managed Postgres and its costs, the Unity IAP package, and Google Play Console setup. None has been created or configured. Product prices and availability will come from the store after approval; they are not hardcoded in the preview. Do not enable payment until the backend can verify, deduplicate, restore, refund/revoke, and recover purchases in sandbox tests.

## Preview UI removed from the shipped app - 2026-10-22

At the user's request ("remove all preview UI ... production ready with no preview"), the shipped app no longer contains preview surfaces: the STYLE PREVIEW menu entry is gone and the cosmetic showroom scene is excluded from Android builds (it remains in the project and in the desktop verification player for future cosmetic work), the app is named **Grid Run**, the Online Room's unsupported-room message no longer calls the app a preview, and the website download card reads ANDROID APP with "Version / size / Android 10 or later" instead of "Android Preview" and "Phase N development build". Version 0.5.0 / code 6 installs over 0.4.0. The player APK is `Builds/Android/grid-run.apk` from `BuildAndroidRelease` (no development watermark, debugger, or capture flows).

Checks after the change: solo flow 97/97 (including "main menu has no preview entry"), showroom 28/28 (now ending with the same menu check), Solar Transit 33/33. `grid-run.apk` (36,403,066 bytes; SHA-256 5583F4A582EF1E420C74008D97E696975B4C3DDA80B1EE350AB10A5D136C2673; label "Grid Run", 0.5.0 / code 6, API 29-36, ARM64, v2 signature verified) was staged and, at the user's request, deployed to Vercel production (https://tron-flax.vercel.app) with the updated download card; the live APK returned the same SHA-256 and the page no longer shows preview or development-build text.

Production gaps that remain outside UI: the APK is debug signed (a release keystore and its safe storage are a user decision), the Android application ID is still the development `com.gridrun.prototype` (changing it creates a separate app identity), and no physical-device install, performance, or accessibility validation has been done.

## First phone run and APK hosting - 2026-10-23

v0.5.0 ran on the user's Galaxy M35 (SM-M356B, Android 16 / API 36, Exynos 1380 with a Mali GPU, 1080x2340, display at 60 Hz) over USB debugging. About four minutes of play were sampled every 10 s with SurfaceFlinger timestats: a steady 60 fps, p99 frame time 16 ms, 2 frames over 33 ms in total, battery 32.8 to 34.5 °C, thermal status 0 throughout, memory 250-310 MB PSS, and no crash. The user judged performance good. Evidence is in ignored artifacts/android-device/session-1/. The log showed one error: GameObject.CreatePrimitive added a collider, but player builds strip the unused physics module. Cubes and the sky dome now come from GridMesh (GridLook.Cube), with no visual change. Checks after the fix: solo 97/97, Transit 33/33, showroom 28/28, and the 28 Capture-Look screenshots reviewed. v0.5.1 / code 7 (36,397,646 bytes; SHA-256 E0318F4EF5461FD962C82F3FED0907737076716E1300A60C73B25AB0976A3BB4) has not yet been installed on the phone; the device disconnected before the install.

Git pushes to main redeploy Vercel production from the repository, which never contained the Git-ignored APK, so the download kept disappearing. At the user's request the APK moved to GitHub Releases: scripts/publish-android-release.mjs uploads it (tag android-v0.5.1), downloads it back to confirm size and SHA-256, and then writes the tracked dist/android-release.json that the landing page reads. The old CLI staging script was removed.

## Remaining Phase 4 work

- Select and approve the database, costs, and guest purchase-data policy; define retention and deletion support without an in-app account.
- Define versioned guest-session, entitlement, catalog, and purchase APIs with old-client errors; keep Play credentials only on the server.
- Implement durable inventory, purchase-token uniqueness, acknowledgement after a stored grant, pending/cancelled/duplicate flows, restore/reinstall, refund revocation, backup and restore drills, and a purchase-disable switch.
- Validate Google Play license-tester flows and real-device behavior before exposing a buy button. Earlier Android and online device gates remain open.

## Reproduce locally

Run android-game/scripts/Verify-Showroom.ps1 from the repository root. It builds a development desktop player and saves an ignored report, logs, and screenshots under artifacts/android-phase-4/. The player APK comes from GridRun.Editor.PhaseFourBuild.BuildAndroidRelease (`Builds/Android/grid-run.apk`); BuildAndroid makes the larger development APK. Publish with `node scripts/publish-android-release.mjs <version> android-game/Builds/Android/grid-run.apk` (see the repository README). APKs and evidence are ignored by Git.
