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
| Arena | Survive against three AI riders. |
| Play lesson | Learn to turn, leave a trail, and avoid collisions. You can skip the lesson. |

Turn left or right with **Left/Right Arrow** or **A/D**. Press **P** or **Escape** to pause or resume, and **M** to mute or unmute. On touch devices, use the on-screen turn buttons. The HUD also has touch, sound, and pause buttons. Use the game menu to start or restart a run. After a crash, a six-second replay shows the recorded collision and its cause; use **Skip Replay**, **Enter**, or **Escape** to continue immediately.

## Settings and progress

The menu offers separate effects and music volume, Close/Standard/Far camera distance, Chase/Tactical camera views, High/Low graphics, and a Reduce flashes option. The game also respects your browser's reduced-motion preference. The game saves these preferences, mode choices, touch-control choice, and your best score in your browser's local storage. There is no account, online leaderboard, or PvP mode.

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
- `dist/match.js` — duel match rules and rival configuration.
- `tests/` — Node.js tests for simulation and match behavior.

To run the available tests, use:

```powershell
node --test tests/*.test.mjs
```

## Project status

Duel, Arena, a playable lesson, AI rivals, cinematic arena scenery, distinct rider silhouettes, procedural music/effects, camera views, and crash replays are implemented. Additional maps, expanded combat and transformation mechanics, and PvP are future ideas, not current features.

This is an unofficial fan project inspired by TRON. It is not affiliated with or endorsed by Disney.

