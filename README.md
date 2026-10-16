# TRON: Grid Run

An unofficial, browser-based 3D light-cycle game. Steer through the Grid, leave a solid light wall behind your cycle, and outlast AI riders.

## Play locally

You need a modern browser with WebGL enabled. The game is a static site with no package install or build step. From the repository root, start a local server:

```powershell
py -m http.server 8000 --directory dist
```

Open [http://localhost:8000](http://localhost:8000). A local HTTP server is needed because the game uses JavaScript modules.

## Play together online

Deploy this repository as one persistent HTTPS game server, then open that server's URL on every player's device. On the landing page choose **Online Room**. The host chooses Duel, Arena, or Grid Shift, a map, and 2, 3, or 4 seats, creates the room, and shares the displayed six-digit numeric code. The host can change the mode and map in the waiting room before starting; Arena also offers persistent or ten-second fading walls. Friends open the same site, enter the code, and join. The host starts when every seat is connected. All three maps are available in Grid Shift. Players do not run a local command or exchange WebRTC codes.

The server runs the authoritative fixed-step simulation and streams match state to every browser. It stores active rooms only in memory. Run one server instance; restarting it ends open rooms. A dropped connection pauses a heat for up to 15 seconds while that player reconnects. Duel and Grid Shift go to three heat wins. Arena ends after exactly three heats; the rider with the most heat wins takes the match, and equal top scores draw. If a heat is still running after three minutes, the arena shrinks every 15 seconds. Duel and Arena end in a draw at five minutes if riders still survive. Grid Shift ends at five minutes; the surviving rider with the most integrity wins on timeout, with ties drawing. There are no accounts or permanent online scores.

Online matches use the same 3D game screen, radar, camera options, remappable keys, gamepad support, and touch controls as solo play. Use the settings button during play to adjust your view; the match continues while settings are open. In online Duel and Arena, use A/D or Left/Right to turn relative to the cycle, W or Up to move forward when Auto Drive is off, and hold E to fire wall-cutting bullets. Online Grid Shift uses the same cycle controls; press F to dismount or remount, move on foot with WASD or the arrow keys, and press Space to throw a disc toward the nearest rider. On touch screens, Grid Shift uses forward and turn buttons while mounted, foot movement buttons when dismounted, FORM, and a contextual FIRE/DISC button. Only the host can pause an online heat.

The older optional local host is still available through `npm run lan` and uses six-character room codes on a private network. The browser-only manual peer room remains at `peer.html` for static hosting.

## Game modes

| Mode | Goal |
| --- | --- |
| Duel | Face one selected rival in a first-to-three match. Choose Vanta (Hunter), Aegis (Survivor), or Knot (Trapper), and Rookie, Standard, or Expert difficulty. |
| Arena | Play three scored heats against a rotating mix of Hunter, Survivor, and Trapper AI. Survive at least two heats to clear the match. |
| Online Room | Play Duel, three-heat Arena, or Grid Shift with 2-4 human riders. The host chooses the mode before starting and shares one six-digit numeric room code. |
| Solar Transit | Race a short solo course. Launch the light jet over the amber gap, then use the solar sail over the longer cyan gap and reach the finish before the 12-second timer. |
| Operations | Play Portal Run, System Sweep, or Arena Collapse alone, or link them into a short journey with an optional Sentinel rival. Timed gates, marked scanner beams, and shrinking rings have visible warnings and exact failure messages. |
| Grid Shift | Fight Echo on the selected map. Both fighters can ride to draw light walls and hold fire to shoot a 24-bullet mounted gun, dismount to move freely and throw a returning disc, and remount in clear space. Two disc hits derez a rider; the five-minute lockout uses remaining integrity. |
| Daily Signal | Play an offline, first-to-three Duel with a fixed seed, map, rival, and Standard difficulty for the UTC date. The rules version and seed are shown before play. Replays that day use the same setup; the daily best and recent results are saved locally. |
| Play lesson | Learn to turn, leave a trail, and avoid collisions. You can skip the lesson. |

Choose **Classic Grid** for a broad open arena, **Solar Circuit** for long lanes around round amber reactor pylons, or **Data Vault** for tight routes around circular violet nodes. The menu preview marks fixed barriers and spawn positions. Duel, Arena, and Grid Shift have Rookie, Standard, and Expert AI difficulty; Operations uses this choice for the optional Sentinel rival. Arena also offers an optional advanced rule: closed light walls pulse during their last two seconds and disappear after ten seconds. The default rule keeps walls for the entire heat.

Cycles move forward automatically by default. **A/D** or **Left/Right** turn relative to the direction the cycle faces; **W** or **Up** moves forward without turning. Turn off **Auto Drive** in Settings to move only while W or Up is held. S/Down does not reverse a cycle. In every cycle mode, hold **E** to fire mounted-gun bullets. Press **Q** for a 0.8-second light-jet burst (five-second cooldown) or **R** for a two-second solar-sail glide (seven-second cooldown). Both lift the cycle over light walls without drawing a wall in the air; landing on a wall crashes. Solid barriers and arena boundaries still block you. In Grid Shift, flight works while mounted and dismounting is unavailable until landing. Jet and sail keys can be remapped, and their buttons show cooldowns on screen. Standard gamepads use the left and right triggers for jet and sail. Each rider starts with 24 bullets; ordinary bullets cut a narrow gap in a light wall, while riders and fixed barriers block them. Ride over blue cartridge pickups for 12 extra bullets (up to 48), green lightning-bolt nitro for seven seconds of faster movement, or an orange rocket pickup to replace the next gun shot with one explosive rocket. Rockets damage nearby riders and cut nearby light walls; after firing, the normal gun returns. The arena starts without boosters; one appears after four seconds and another about every eight seconds at a safe new location. Booster types rotate in a different order each heat, and uncollected boosters fade after fourteen seconds. All maps have larger bounds, and any heat still active at three minutes starts shrinking every 15 seconds until it ends. On touch screens, use **Left**, **Forward**, **Right**, and **FIRE**. Hold Forward when Auto Drive is off. Press **P** or **Escape** to pause or resume, and **M** to mute or unmute. Letters, numbers, and Space can be remapped in Settings; arrow keys remain movement shortcuts. The HUD also has touch, sound, and pause buttons. Use the game menu to start or restart a run. In Duel and Arena, a six-second replay shows a recorded crash and its cause; use **Skip Replay**, **Enter**, or **Escape** to continue immediately.

Grid Shift uses **A/D** or **Left/Right** to turn while mounted and **W** or **Up** for forward movement in manual drive. Hold **E** while mounted to fire the gun. Press **F** to dismount or remount. On foot, hold **W/A/S/D** or the arrow keys to move in arena directions and press **Space** to throw the disc. The on-foot camera stays north-up; stepping back into the wall left by your cycle is still lethal. The on-screen Grid Shift pad supplies mounted forward/turn controls, on-foot movement, form, and a context-sensitive FIRE/DISC button on touch devices. Echo starts with the same 24-bullet gun and can collect the same pickups. Mounting and dismounting need a clear lane and briefly stop movement; form changes do not protect against walls or hits. Both riders use the same movement, collision, and weapon rules. The HUD shows both integrity values, your form, disc readiness, and the lockout timer. A short briefing teaches the controls before the first round; results offer a rematch.

Solar Transit shows both marked gaps, a finish line, progress, and the countdown in the HUD and radar. Crossing a gap requires the matching ability to stay active until you clear its far edge. Failing a gap or running out of time offers a retry. Operations show their objective, remaining time, progress, and hazard warnings in the HUD and radar. The briefing cards can be skipped without hiding the objective. A failed operation offers a fast retry; returning to mode selection is always available from the result. Mission completion unlocks Grid Archive fragments and cosmetic trail colors. These looks never change cycle speed, collision, or AI behavior.

## Settings and progress

The landing menu keeps mode, difficulty, map, and rule choices visible. Its corner settings button opens Settings, Grid Archive, and Run Archive tabs. The Settings tab offers unlocked trail looks, separate effects and music volume, Close/Standard/Far camera distance, Chase/Tactical camera views, High/Low graphics, render scale, steady or cinematic camera motion, flash intensity, a Reduce flashes option, Auto Drive (on by default), and visible caption cues. Steady camera motion keeps the arena north-up. Touch controls have normal or large buttons and edge, compact, or raised placement. Optional standard-layout gamepad controls use left/right on the stick or D-pad to turn, up for forward movement in manual drive, A to confirm/change form, hold B to fire the mounted gun in any cycle mode or throw the disc on foot, and Start to pause. Rival trails have distinct stripe positions in the world and dash patterns on radar, so color is not the only cue. The game also respects your browser's reduced-motion preference. The game saves these preferences, mode choices, touch-control choice, a separate local best for each Duel/Arena/Grid Shift mode, map, difficulty, and wall rule, and operation completion/bests in your browser's local storage. Older best scores remain stored and are shown as legacy scores in the menu. The Run Archive stores up to eight recent results, a short finish highlight when storage allows, personal records, and skill badges. Settings can be reset separately, or **Clear Local Data** removes this browser's game preferences, progress, scores, and run history after confirmation. The game still runs when browser storage is unavailable, but new data cannot be saved. There is no account or online leaderboard. Online rooms run on the game server and use temporary room codes. Local preferences and solo records stay in each browser and site origin; Vercel and Render store their settings separately.

Solo play remains usable as a static site. Online rooms require the persistent game server. Optional Google Fonts are loaded from Google when a network connection is available.

## Deploy the online game

Run this repository as **one persistent Node.js 20+ or Docker web service** with HTTPS at the public edge. The included `Dockerfile` starts the full site and room server. On Render, the server uses `RENDER_EXTERNAL_URL` automatically. On another host, set `TRON_PUBLIC_ORIGIN` to the exact public HTTPS origin, such as `https://grid.example.com`, without a trailing path. The host platform supplies `PORT`; the Docker image defaults to port 10000, and direct Node startup defaults to port 8000. Use `npm start` when deploying without Docker. Configure one instance and a service that does not sleep during a match. The health endpoint is `/healthz`.

Give players that public URL. The entire game, room API, and live match stream are served from it. This online mode cannot run on a static-only Vercel deployment because it needs a continuously running match process. The Vercel static landing page links Online Room to the Render game server; solo play stays available on Vercel, and the manual browser peer room remains at peer.html.

To exercise the online server locally as the site owner:

```powershell
$env:TRON_PUBLIC_ORIGIN = 'http://localhost:8000'
npm start
```

Only `localhost` and `127.0.0.1` may use HTTP. Public deployments require HTTPS. Room sessions use HTTP-only cookies, with `Secure` on HTTPS. Avoid sharing room codes publicly: anyone with a code can take an open seat. Room creation and joining have a basic request limit, but a public deployment still needs normal hosting abuse protection. The server has no database, so active rooms end on restart.

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
- `dist/peer.html` and `dist/peer.js` - optional browser peer room UI with manual connection codes.
- `dist/lan.html` and `dist/lan.js` - online or local room lobby and map preview; active matches open the shared 3D screen in index.html/game.js.
- `lan-server.mjs` — optional dependency-free LAN HTTP host and authoritative room loop.

To run the available tests, use:

```powershell
node --test tests/*.test.mjs
```

## Project status

Duel, offline Daily Signal (rules version 3), a 24-bullet mounted gun in every mode, a local run archive, remappable controls, optional gamepad input, comfort settings, three-heat Arena, three maps, three solo operations, a separate timed Solar Transit course, light-jet and solar-sail traversal in solo and PvP cycle modes, a linked journey, Grid Shift two-form combat, a playable cycle lesson, mixed AI riders, cinematic scenery, procedural music/effects, camera views, and crash replays are implemented. Grid Shift currently has three maps, collectible ammo, nitro, and rockets, one returning disc, and one AI rival; hybrid missions remain playtest candidates. Online rooms support 2-4 riders with a six-digit code when the persistent server is deployed. The optional local host and browser peer page remain available. Public matchmaking remains future scope.

This is an unofficial fan project inspired by TRON. It is not affiliated with or endorsed by Disney.

