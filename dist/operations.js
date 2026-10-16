// Original PvE set pieces. Rectangles here drive both collision and rendering.
export const OPERATIONS = Object.freeze({
  transit: {
    id: 'transit', name: 'Solar Transit', map: 'classic', kind: 'traversal',
    objective: 'Cross the jet gap, then the sail gap, and reach the finish before time runs out.',
    briefing: 'Two broken spans block the northbound route. Lift over the first with the light jet, then glide over the second with the solar sail.',
    tip: 'Use the jet just before the amber gap, then the sail just before the cyan gap. Land beyond each gap.',
    hudTip: 'Amber gap: jet (Q). Cyan gap: sail (R).',
    timeLimit: 12, spawn: { x: 0, z: 22, dir: 0 },
    gaps: [
      { id: 'Jet gap', kind: 'jet', minX: -52, maxX: 52, minZ: 10, maxZ: 15 },
      { id: 'Sail gap', kind: 'sail', minX: -52, maxX: 52, minZ: -12, maxZ: -2 },
    ],
    finishZ: -32,
    reward: { look: null, lore: 'The light jet clears short breaks; a solar sail carries momentum across a longer span.' },
  },
  portal: {
    id: 'portal', name: 'Portal Run', map: 'solar', kind: 'checkpoints',
    objective: 'Cross three gates in order before the timer ends.',
    briefing: 'The transit corridor is restarting. Follow the lit sequence; closed shutters are solid.',
    tip: 'Ride north through Gate 1, turn east through Gate 2, then go north outside the east pylon to Gate 3.',
    hudTip: 'Follow the green gates in order.',
    timeLimit: 18, spawn: { x: 0, z: 22, dir: 0 },
    gates: [
      { id: 'Gate 1', minX: -4, maxX: 4, minZ: 7, maxZ: 10, openFrom: 0.65, openUntil: 6 },
      { id: 'Gate 2', minX: 8, maxX: 11, minZ: 6, maxZ: 10, openFrom: 1.55, openUntil: 10 },
      { id: 'Gate 3', minX: 18, maxX: 22, minZ: -9, maxZ: -5, openFrom: 3.15, openUntil: 17 },
    ],
    reward: { look: 'amber', lore: 'The transit portals were built for speed, but timing keeps the Grid alive.' },
  },
  sweep: {
    id: 'sweep', name: 'System Sweep', map: 'classic', kind: 'scanner',
    objective: 'Survive three marked scanner sweeps.',
    briefing: 'A search craft has locked onto this sector. Its floor mark appears before each beam.',
    tip: 'Watch the yellow lane mark. Turn out of that lane before the beam fires.',
    hudTip: 'Leave the marked lane before the beam fires.',
    timeLimit: 16.5, spawn: { x: 0, z: 18, dir: 0 },
    pulses: [4, 9, 14], warningTime: 1.5, activeTime: 0.7,
    reward: { look: 'violet', lore: 'The scanner only sees a lane it has already marked. Movement breaks its prediction.' },
  },
  collapse: {
    id: 'collapse', name: 'Arena Collapse', map: 'classic', kind: 'collapse',
    objective: 'Survive until the containment ring stabilizes.',
    briefing: 'The perimeter is folding inward. Each amber warning ring becomes the new boundary.',
    tip: 'Stay inside the marked ring before it locks. Keep an escape lane clear.',
    hudTip: 'Stay inside the amber warning ring.',
    timeLimit: 13.5, spawn: { x: 0, z: 18, dir: 0 },
    steps: [{ at: 4, limit: 25 }, { at: 8, limit: 21 }, { at: 12, limit: 17 }],
    warningTime: 1.5,
    reward: { look: 'white', lore: 'Containment walls do not chase riders. They close on a fixed schedule.' },
  },
  sentinel: {
    id: 'sentinel', name: 'The Sentinel', map: 'solar', kind: 'rival',
    objective: 'Derez the Sentinel to complete the optional final challenge.',
    briefing: 'One guard program remains at the exit. Its cycle follows the same collision rules as yours.',
    tip: 'The Sentinel favors pressure, but it cannot cross a live light wall.',
    hudTip: 'Trap the Sentinel with light walls.',
    timeLimit: null, spawn: { x: -5, z: 19, dir: 0 },
    rivalSpawn: { x: 5, z: -19, dir: 2 },
    reward: { look: null, lore: 'The Sentinel was the last test, not the last path through the Grid.' },
  },
});
export const JOURNEY = Object.freeze(['portal', 'sweep', 'collapse']);
export function getOperation(id) { return OPERATIONS[id] || OPERATIONS.portal; }
