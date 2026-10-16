# TRON: Grid Run

An unofficial, browser-based 3D light-cycle game. Steer through the Grid, leave a solid light wall behind your cycle, and outlast AI riders.

## Play locally

You need a modern browser with WebGL enabled. The game is a static site with no package install or build step. From the repository root, start a local server:

```powershell
py -m http.server 8000 --directory dist
```

Open [http://localhost:8000](http://localhost:8000). A local HTTP server is needed because the game uses JavaScript modules.

## Play together on a local network

LAN rooms are optional. On one computer connected to the same Wi-Fi or hotspot as the other players, install Node.js 20 or newer and run:

```powershell
npm run lan
```

The host prints a LAN address such as `http://192.168.1.38:8000/lan.html`. Open that address on the host and on each friend's device. Create a room for 2, 3, or 4 riders, choose a map, and share the displayed link or six-character room code. Once every seat connects, the host starts the match. Each heat uses the same fixed-step movement, trails, collisions, and 24-shot wall gun as the cycle modes. The first rider to win three heats takes the match. LAN play uses a top-down tactical view, keyboard or touch controls, host pause/resume, and a rematch button.

The local host keeps the authoritative match state in memory. A page refresh reconnects to its seat in the same browser; a dropped connection holds the heat for up to 15 seconds before that rider forfeits the heat. Stopping the host process or having the host leave its room ends the session. No account, online matchmaking, cloud relay, or internet connection is needed for devices on the same reachable LAN. The host computer's firewall may need to allow Node.js on a private network. Room traffic uses HTTP on the local network; use a trusted private network.

The ordinary solo site remains static and works without Node. A static deployment cannot host a LAN room by itself.

## Game modes

| Mode | Goal |
| --- | --- |
| Duel | Face one selected rival in a first-to-three match. Choose Vanta (Hunter), Aegis (Survivor), or Knot (Trapper), and Rookie, Standard, or Expert difficulty. |
| Arena | Play three scored heats against a rotating mix of Hunter, Survivor, and Trapper AI. Survive at least two heats to clear the match. |
| LAN Room | Play live, local-network heats with 2-4 human riders. First to three heat wins takes the match. Requires the optional local Node host. |
| Operations | Play Portal Run, System Sweep, or Arena Collapse alone, or link them into a short journey with an optional Sentinel rival. Timed gates, marked scanner beams, and shrinking rings have visible warnings and exact failure messages. |
| Grid Shift | Fight Echo in one open arena. Both fighters can ride to draw light walls and hold fire to shoot a 24-bullet mounted gun, dismount to move freely and throw a returning disc, and remount in clear space. Two disc hits derez a rider; the 90-second lockout uses remaining integrity. |
| Daily Signal | Play an offline, first-to-three Duel with a fixed seed, map, rival, and Standard difficulty for the UTC date. The rules version and seed are shown before play. Replays that day use the same setup; the daily best and recent results are saved locally. |
| Play lesson | Learn to turn, leave a trail, and avoid collisions. You can skip the lesson. |

Choose **Classic Grid** for a broad open arena, **Solar Circuit** for long lanes around round amber reactor pylons, or **Data Vault** for tight routes around circular violet nodes. The menu preview marks fixed barriers and spawn positions. Duel, Arena, and Grid Shift have Rookie, Standard, and Expert AI difficulty; Operations uses this choice for the optional Sentinel rival. Arena also offers an optional advanced rule: closed light walls pulse during their last two seconds and disappear after ten seconds. The default rule keeps walls for the entire heat.

By default, turn left or right with **Left/Right Arrow** or **A/D**. In every cycle mode, hold **E** to fire individual mounted-gun bullets continuously. Each rider has 24 bullets per round; a bullet opens a narrow gap in your own or another rider's light wall. The gun does not reload during a round. Fixed barriers, operation hazards, and riders block bullets without taking damage. On touch screens, hold **FIRE** between the turn buttons. Press **P** or **Escape** to pause or resume, and **M** to mute or unmute. Letters, numbers, and Space can be remapped in Settings; arrow keys remain movement shortcuts. The HUD also has touch, sound, and pause buttons. Use the game menu to start or restart a run. In Duel and Arena, a six-second replay shows a recorded crash and its cause; use **Skip Replay**, **Enter**, or **Escape** to continue immediately.

Grid Shift uses **A/D** or **Left/Right Arrow** to turn while mounted. Hold **E** while mounted to fire the gun. Press **F** to dismount or remount. On foot, hold **W/A/S/D** or the arrow keys to move in arena directions and press **Space** to throw the disc. The on-foot camera stays north-up; stepping back into the wall left by your cycle is still lethal. The on-screen Grid Shift pad supplies movement, form, and a context-sensitive FIRE/DISC button on touch devices. Echo has the same 24-bullet gun. Mounting and dismounting need a clear lane and briefly stop movement; form changes do not protect against walls or hits. Both riders use the same movement, collision, and weapon rules. The HUD shows both integrity values, your form, disc readiness, and the lockout timer. A short briefing teaches the controls before the first round; results offer a rematch.

Operations show their objective, remaining time, progress, and hazard warnings in the HUD and radar. The briefing cards can be skipped without hiding the objective. A failed operation offers a fast retry; returning to mode selection is always available from the result. Mission completion unlocks Grid Archive fragments and cosmetic trail colors. These looks never change cycle speed, collision, or AI behavior.

## Settings and progress

The menu offers unlocked trail looks, separate effects and music volume, Close/Standard/Far camera distance, Chase/Tactical camera views, High/Low graphics, render scale, steady or cinematic camera motion, flash intensity, a Reduce flashes option, and visible caption cues. Steady camera motion keeps the arena north-up. Touch controls have normal or large buttons and edge, compact, or raised placement. Optional standard-layout gamepad controls use the left stick or D-pad to steer, A to confirm/change form, hold B to fire the mounted gun in any cycle mode or throw the disc on foot, and Start to pause. Rival trails have distinct stripe positions in the world and dash patterns on radar, so color is not the only cue. The game also respects your browser's reduced-motion preference. The game saves these preferences, mode choices, touch-control choice, a separate local best for each Duel/Arena/Grid Shift mode, map, difficulty, and wall rule, and operation completion/bests in your browser's local storage. Older best scores remain stored and are shown as legacy scores in the menu. The Run Archive stores up to eight recent results, a short finish highlight when storage allows, personal records, and skill badges. Settings can be reset separately, or **Clear Local Data** removes this browser's game preferences, progress, scores, and run history after confirmation. The game still runs when browser storage is unavailable, but new data cannot be saved. There is no account or online leaderboard. Local LAN PvP uses the optional host.

Solo play works without a gameplay server. LAN rooms use the optional local host described above. Optional Google Fonts are loaded from Google when a network connection is available.

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
- `dist/lan.html` and `dist/lan.js` — LAN room UI and tactical renderer.
- `lan-server.mjs` — optional dependency-free LAN HTTP host and authoritative room loop.

To run the available tests, use:

```powershell
node --test tests/*.test.mjs
```

## Project status

Duel, offline Daily Signal (rules version 2), a 24-bullet mounted gun in every mode, a local run archive, remappable controls, optional gamepad input, comfort settings, three-heat Arena, three maps, three solo operations, a linked journey, Grid Shift two-form combat, a playable cycle lesson, mixed AI riders, cinematic scenery, procedural music/effects, camera views, and crash replays are implemented. Grid Shift currently has one arena, one mounted gun, one returning disc, and one AI rival; broader weapons and hybrid missions remain playtest candidates. Online PvP remains future scope; local LAN rooms are available through the optional host.

This is an unofficial fan project inspired by TRON. It is not affiliated with or endorsed by Disney.

