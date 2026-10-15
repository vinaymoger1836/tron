# AGENTS.md — TRON: Grid Run

> Repository-wide instructions for coding agents. Adapted from `C:\Projects\Automiq\AGENTS.md`: its engineering, security, testing, and Git rules apply here; its Automiq architecture and phase plan do not describe this game.

## 1. Source of truth and precedence

1. Follow the user's current explicit instructions and higher-priority system or developer instructions.
2. Follow this file for how work in this repository is carried out.
3. Follow any project architecture or planning document if one is added and is relevant to the requested work.
4. Follow existing code conventions when they do not conflict with the above.

Do not silently change the game's architecture or expand the requested scope. Explain major tradeoffs and obtain approval before adding major frameworks, infrastructure, or external services.

## 2. Session startup

1. Read this file and any relevant project architecture or planning documents that actually exist.
2. Run `git status --short` and preserve unrelated user changes.
3. Inspect relevant files and tooling rather than assuming paths or commands exist.
4. Identify the smallest coherent, playable slice that addresses the request.
5. Before a substantial change, briefly state the files to touch, expected behavior, risks, and verification approach.
6. Stay within the requested feature or phase.

## 3. Project architecture

- This is a buildless browser game: `dist/index.html` contains the UI and `dist/game.js` contains the WebGL renderer, game loop, AI riders, collision logic, and input handling.
- Keep the game playable as a static site. Do not add a backend, account system, database, third-party game engine, or external runtime dependency without explaining the need and obtaining approval.
- Keep rendering, input, game state, and persistence behavior understandable. Use browser-local storage only for non-sensitive local preferences and scores.
- Preserve keyboard and touch play, pause/resume, and the ability to restart a round. Keep gameplay rules consistent across screen sizes and frame rates.
- Favor a complete player-visible behavior over disconnected scaffolding.

## 4. UI, responsiveness, and accessibility

- Keep the interface polished, readable, and suited to the Grid's established visual style. Do not add effects that reduce clarity or interaction speed.
- Check mobile (~375px), tablet (~768px), laptop (~1280px), and wide desktop (~1440px+) layouts. Avoid horizontal overflow.
- Provide keyboard navigation, labeled controls, visible focus, legible contrast, and clear status text. Do not rely on color alone for critical information. Respect reduced-motion preferences where motion can be reduced without breaking play.
- The current game has a cinematic dark theme. Do not claim a light theme exists. If theme support is introduced, use semantic tokens, respect `prefers-color-scheme`, provide a visible persisted switch, and verify both themes at mobile and desktop widths.
- When UI changes, capture desktop and mobile screenshots during end-to-end verification. Capture both themes if both are supported at that time.

## 5. Security, privacy, and Git hygiene

**Never commit, push, paste, publish, upload, or otherwise expose secrets or private data.** Treat credentials, API keys, OAuth tokens, cookies, private keys, customer data, and database dumps as sensitive.

- Use environment variables or a secret manager for secrets. Commit only sanitized placeholder values in `.env.example`.
- Keep `.env` files, `.secrets/`, credentials, local databases, browser profiles, screenshots, traces, reports, and generated archives ignored. Verify evidence paths are ignored before generating evidence.
- Do not print raw secrets in logs or command output. Use synthetic data for tests.
- Before finalizing, inspect `git diff` and `git status`, and run an available secret scanner or a careful manual scan of tracked and changed files. A clean scan does not by itself authorize publishing private data.
- If a suspected secret is in tracked history, stop and report its path and risk without repeating the value. Do not rewrite history or contact a remote service to address it without user approval.
- Git is read-only by default. Do not add, commit, push, tag, reset, clean, rebase, or change branches unless the user explicitly authorizes that action. An explicit request to commit or push overrides this default for that request.

## 6. Testing policy — end-to-end first

- Prefer end-to-end verification of complete player-visible behavior. Do not add routine unit or component tests after implementation merely to mirror functions or increase coverage.
- For a complex gameplay feature, exercise the completed flow, including meaningful failure and recovery paths, in a real browser where practical. Prefer Playwright if browser automation is available.
- Static checks, builds, linting, dependency scans, and secret scans are allowed and expected where relevant. Never claim a test passed unless it ran and passed.
- If isolation is truly required for a critical algorithm that cannot reasonably be checked end-to-end, document plausible failure modes and the reason before implementation, then write those checks before the implementation.
- Keep repeatable E2E evidence under a Git-ignored path such as `artifacts/e2e/<feature>/`. Record the command, observed result, browser and viewport, saved report or screenshots, and rerun instructions. If E2E cannot run, state the limitation precisely.

## 7. Implementation conventions

- Prefer clear, maintainable code and minimal dependencies. Do not add a dependency for trivial functionality.
- Handle loading, failure, unavailable WebGL, pause, and restart states explicitly. Do not hide failures or fabricate test results.
- Keep browser storage non-sensitive and avoid transmitting player data without an explicit feature request.
- Preserve existing behavior when making bounded changes; review input controls, collision behavior, scoring, and performance when relevant.

## 8. Handoff

At the end of a work session, report changed files, key decisions, exact checks run and their observed results, responsive and accessibility checks when relevant, secret-scan result, known limitations, and Git status. Include commit and push state if the user authorized those actions. Do not claim the game is production-ready or fully tested without supporting evidence.

## 9. Completion checklist

- [ ] Requested behavior works as described and unrelated changes are preserved.
- [ ] Relevant mobile and desktop layouts, controls, and accessibility were reviewed.
- [ ] No secret or private data was added to tracked files or test evidence.
- [ ] Relevant E2E and static checks ran, or their limits are stated.
- [ ] `git diff` and `git status` were reviewed.
- [ ] Git writes occurred only with the user's explicit authorization.
