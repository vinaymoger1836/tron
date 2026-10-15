# TRON: Grid Run

An unofficial, browser-based 3D light-cycle game. Steer through the Grid, leave a solid light wall behind your cycle, and outlast AI riders.

## Play locally

You need a modern browser with WebGL enabled. The game is a static site with no package install or build step. From the repository root, start a local server:

```powershell
py -m http.server 8000 --directory dist
```

Open [http://localhost:8000](http://localhost:8000). A local HTTP server is needed because the game uses JavaScript modules.

## Game modes

| Mode | Goal |
| --- | --- |
| Duel | Face one selected rival in a first-to-three match. Choose Vanta (Hunter), Aegis (Survivor), or Knot (Trapper), and Rookie, Standard, or Expert difficulty. |
| Arena | Play three scored heats against a rotating mix of Hunter, Survivor, and Trapper AI. Survive at least two heats to clear the match. |
| Operations | Play Portal Run, System Sweep, or Arena Collapse alone, or link them into a short journey with an optional Sentinel rival. Timed gates, marked scanner beams, and shrinking rings have visible warnings and exact failure messages. |
| Grid Shift | Fight Echo in one open arena. Both fighters can ride to draw light walls and fire a short-range wall cutter, dismount to move freely and throw a returning disc, and remount in clear space. Two disc hits derez a rider; the 90-second lockout uses remaining integrity. |
| Daily Signal | Play an offline, first-to-three Duel with a fixed seed, map, rival, and Standard difficulty for the UTC date. The rules version and seed are shown before play. Replays that day use the same setup; the daily best and recent results are saved locally. |
| Play lesson | Learn to turn, leave a trail, and avoid collisions. You can skip the lesson. |

Choose **Classic Grid** for open space, **Solar Circuit** for long lanes around amber pylons, or **Data Vault** for tighter routes around violet gates. The menu preview marks fixed barriers and spawn positions. Duel, Arena, and Grid Shift have Rookie, Standard, and Expert AI difficulty; Operations uses this choice for the optional Sentinel rival. Arena also offers an optional advanced rule: closed light walls pulse during their last two seconds and disappear after ten seconds. The default rule keeps walls for the entire heat.

By default, turn left or right with **Left/Right Arrow** or **A/D**. Press **P** or **Escape** to pause or resume, and **M** to mute or unmute. Letters, numbers, and Space can be remapped in Settings; arrow keys remain movement shortcuts. On touch devices, use the on-screen turn buttons. The HUD also has touch, sound, and pause buttons. Use the game menu to start or restart a run. In Duel and Arena, a six-second replay shows a recorded crash and its cause; use **Skip Replay**, **Enter**, or **Escape** to continue immediately.

Grid Shift uses **A/D** or **Left/Right Arrow** to turn while mounted. Press **E** while mounted to fire a wall cutter that opens a narrow gap in one light wall; it has a cooldown and does not damage riders. Press **F** to dismount or remount. On foot, hold **W/A/S/D** or the arrow keys to move in arena directions and press **Space** to throw the disc. The on-foot camera stays north-up; stepping back into the wall left by your cycle is still lethal. The on-screen Grid Shift pad supplies movement, form, and a context-sensitive CUT/DISC button on touch devices. Echo has the same wall cutter and cooldown. Mounting and dismounting need a clear lane and briefly stop movement; form changes do not protect against walls or hits. Both riders use the same movement, collision, and weapon rules. The HUD shows both integrity values, your form, disc readiness, and the lockout timer. A short briefing teaches the controls before the first round; results offer a rematch.

Operations show their objective, remaining time, progress, and hazard warnings in the HUD and radar. The briefing cards can be skipped without hiding the objective. A failed operation offers a fast retry; returning to mode selection is always available from the result. Mission completion unlocks Grid Archive fragments and cosmetic trail colors. These looks never change cycle speed, collision, or AI behavior.

## Settings and progress

The menu offers unlocked trail looks, separate effects and music volume, Close/Standard/Far camera distance, Chase/Tactical camera views, High/Low graphics, render scale, steady or cinematic camera motion, flash intensity, a Reduce flashes option, and visible caption cues. Steady camera motion keeps the arena north-up. Touch controls have normal or large buttons and edge, compact, or raised placement. Optional standard-layout gamepad controls use the left stick or D-pad to steer, A to confirm/change form, B to fire the wall cutter when mounted or throw the disc on foot, and Start to pause. Rival trails have distinct stripe positions in the world and dash patterns on radar, so color is not the only cue. The game also respects your browser's reduced-motion preference. The game saves these preferences, mode choices, touch-control choice, a separate local best for each Duel/Arena/Grid Shift mode, map, difficulty, and wall rule, and operation completion/bests in your browser's local storage. Older best scores remain stored and are shown as legacy scores in the menu. The Run Archive stores up to eight recent results, a short finish highlight when storage allows, personal records, and skill badges. Settings can be reset separately, or **Clear Local Data** removes this browser's game preferences, progress, scores, and run history after confirmation. The game still runs when browser storage is unavailable, but new data cannot be saved. There is no account, online leaderboard, or PvP mode.

The game works without a gameplay server. Its optional Google Fonts are loaded from Google when a network connection is available.

## Deploy on Vercel

Import this repository as a Vercel project and use these build settings:

| Setting | Value |
| --- | --- |
| Framework Preset | Other |
| Root Directory | Repository root |
| Build Command | Leave blank |
| Output Directory | `dist` |

Vercel documents this setup for [static sites without a build step](https://vercel.com/docs/builds). The `dist/` directory already contains the site; there is no generated build output to create.

## Project layout

- `dist/index.html` — interface and styling.
- `dist/game.js` — WebGL renderer, game loop, input, audio, and persistence.
- `dist/simulation.js` — movement, trails, collisions, and AI simulation.
- `dist/match.js` - Duel match rules.
- `dist/arena.js` - three-heat Arena match rules.
- `dist/maps.js` - shared map geometry, palettes, spawns, and spawn validation.
- `dist/operations.js` - solo mission definitions, objectives, warnings, and cosmetic rewards.
- `dist/grid-shift.js` - two-form combat, shared human/AI collisions and disc rules, and Echo tactics.
- `tests/` — Node.js tests for simulation and match behavior.

To run the available tests, use:

```powershell
node --test tests/*.test.mjs
```

## Project status

Duel, offline Daily Signal, a local run archive, remappable controls, optional gamepad input, comfort settings, three-heat Arena, three maps, three solo operations, a linked journey, Grid Shift two-form combat, a playable cycle lesson, mixed AI riders, cinematic scenery, procedural music/effects, camera views, and crash replays are implemented. Grid Shift currently has one arena, one mounted wall cutter, one returning disc, and one AI rival; broader weapons and hybrid missions remain playtest candidates. PvP remains future scope.

This is an unofficial fan project inspired by TRON. It is not affiliated with or endorsed by Disney.

