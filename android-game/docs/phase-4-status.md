# Phase 4 status - 2026-10-21

**State: in progress.** The first Phase 4 slice is a cosmetic showroom with no checkout. Phases 0-3 retain open device, content, and network gates. This is not a commercial release or an ownership system.

## Implemented slice

- CosmeticCatalog defines catalog version 1 and six stable concept IDs: default and Solar variants for a cycle, a trail, and a set. It has no prices, purchase callbacks, store product IDs, or ownership grants.
- CosmeticShowroom provides a 3D preview, category buttons, previous/next selection, clear names/descriptions, and explicit default-versus-concept status. The concept items say they are unavailable to equip or buy. It offers no purchase action and stores no entitlement.
- The offline Arena landing links to Style Preview when PhaseFourShowroom is packaged. The existing offline Arena, Online Room, and Solar Transit scenes remain in the combined Phase 4 preview.
- PhaseFourBuild prepares the scene and builds development desktop and Android players. No SDK, account, database, or service was added.

## Verification

Pinned Unity 6000.3.26f1 built the desktop player. android-game/scripts/Verify-Showroom.ps1 passed **27/27 checks** in the real player: included default selection, absence of a buy action, preview-only status, cycle/trail/set changes, previous/next navigation, Arena start after returning, and fresh showroom state after re-entry. Phone-landscape 1170x540, desktop 1280x720, and tablet 1024x768 screenshots were saved under ignored artifacts/android-phase-4/ and visually reviewed; controls fit all three viewports. This does not validate physical Android input or store behavior.

GridRun.Editor.PhaseFourBuild.BuildAndroid built android-game/Builds/Android/grid-run-phase4-preview.apk (79,531,978 bytes; SHA-256 ECBBD61273BEDCDA7F6807FA4827EF53C68D2D6F55EC052E826B3E537DCEE4E0). Android build-tools verified APK Signature Scheme v2, package com.gridrun.prototype, version 0.4.0 / code 5, minimum API 29, target API 36, ARM64, and Internet permission. ZIP CRC integrity passed. The APK is debug signed and Git-ignored. It has not been published.

## Approval-ready purchase direction

A proposed next step is to keep the existing Node game server and add an account/entitlement API to it, backed by a managed Render Postgres database. Firebase Authentication is a candidate for anonymous guest identity linked to Google sign-in; its Unity documentation describes this flow, but account-linking behavior must be validated in this Unity project. Unity IAP with Google Play would supply store product listings and checkout. The server would verify purchase tokens with Google's API, commit idempotent non-consumable entitlements in Postgres, then acknowledge fulfilled purchases. The app would obtain inventory only from that server; local preferences would never prove ownership. This is a proposal, not an approved implementation.

Google's integration guidance places verification before entitlement and requires acknowledgement of fulfilled purchases: https://developer.android.com/google/play/billing/integrate
Google describes backend purchase lifecycle, notifications, and voided purchases: https://developer.android.com/google/play/billing/backend
Firebase documents Unity anonymous-to-account linking: https://firebase.google.com/docs/auth/unity/anonymous-auth
Unity documents IAP pending purchase callbacks and repeated delivery: https://docs.unity.com/en-us/iap/purchases
Render documents managed Postgres and pricing: https://render.com/docs/postgresql and https://render.com/pricing

This direction requires approval for Firebase Authentication, managed Postgres and its costs, an account/privacy model, the Unity IAP package, and Google Play Console setup. None has been created or configured. Product prices and availability will come from the store after approval; they are not hardcoded in the preview. Do not enable payment until the backend can verify, deduplicate, restore, refund/revoke, and recover purchases in sandbox tests.

## Remaining Phase 4 work

- Select and approve identity, database, cost, and data-ownership choices; implement sign-in, guest linking, recovery, sign-out, and account deletion.
- Define versioned account, entitlement, catalog, and purchase APIs with old-client errors; keep user and Play credentials only on the server.
- Implement durable inventory, purchase-token uniqueness, acknowledgement after a stored grant, pending/cancelled/duplicate flows, restore/reinstall, refund revocation, backup and restore drills, and a purchase-disable switch.
- Validate Google Play license-tester flows and real-device behavior before exposing a buy button. Earlier Android and online device gates remain open.

## Reproduce locally

Run android-game/scripts/Verify-Showroom.ps1 from the repository root. It builds a development desktop player and saves an ignored report, logs, and screenshots under artifacts/android-phase-4/. The Android build method is GridRun.Editor.PhaseFourBuild.BuildAndroid. The preview APK and evidence directory are ignored by Git.
