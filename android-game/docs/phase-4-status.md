# Phase 4 status - 2026-10-21

**State: in progress.** The first Phase 4 slice is a cosmetic showroom with no checkout. Phases 0-3 retain open device, content, and network gates. This is not a commercial release or an ownership system.

## Implemented slice

- CosmeticCatalog defines catalog version 1 and six stable concept IDs: default and Solar variants for a cycle, a trail, and a set. It has no prices, purchase callbacks, store product IDs, or ownership grants.
- CosmeticShowroom provides a 3D preview, category buttons, previous/next selection, clear names/descriptions, and explicit default-versus-concept status. The concept items say they are unavailable to equip or buy. It offers no purchase action and stores no entitlement.
- The offline Arena landing links to Style Preview when PhaseFourShowroom is packaged. The existing offline Arena, Online Room, and Solar Transit scenes remain in the combined Phase 4 preview.
- PhaseFourBuild prepares the scene and builds development desktop and Android players. No SDK, account, database, or service was added.

## Visual refinement

A shared procedural CycleVisual now replaces the cube-based cycles and riders in the prototype, offline Arena, online rooms, Solar Transit, and the showroom. It has a tapered fairing, helmet and visor, two lit wheel rims, animated wheel spin and turn lean, a pulsing light-jet plume, and a deploying solar sail. The showroom camera, display trail, and body contrast were adjusted so the cycle is readable at phone and desktop sizes. A generated rounded UI shape gives menus and controls a consistent edge. This is a clearer playable art baseline; authored models, materials, character animation, effects, and sound are still needed for the intended production look.

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

## Remaining Phase 4 work

- Select and approve the database, costs, and guest purchase-data policy; define retention and deletion support without an in-app account.
- Define versioned guest-session, entitlement, catalog, and purchase APIs with old-client errors; keep Play credentials only on the server.
- Implement durable inventory, purchase-token uniqueness, acknowledgement after a stored grant, pending/cancelled/duplicate flows, restore/reinstall, refund revocation, backup and restore drills, and a purchase-disable switch.
- Validate Google Play license-tester flows and real-device behavior before exposing a buy button. Earlier Android and online device gates remain open.

## Reproduce locally

Run android-game/scripts/Verify-Showroom.ps1 from the repository root. It builds a development desktop player and saves an ignored report, logs, and screenshots under artifacts/android-phase-4/. The Android build method is GridRun.Editor.PhaseFourBuild.BuildAndroid. The preview APK and evidence directory are ignored by Git.
