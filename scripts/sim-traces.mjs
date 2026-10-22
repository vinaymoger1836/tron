// Records reference traces from the browser rules (dist/*.js) for the Android C# port.
// Usage: node scripts/sim-traces.mjs [output-file]
// Each scenario lists its inputs, so the C# replay (android-game Editor/SimParity.cs) needs no copy of them.
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import {
  createRound, createDuelRound, createArenaRound, createOperationRound, stepRound, drainEvents,
  queueTurn, setCycleFire, setCycleThrottle, setCycleAutoDrive, requestFlight,
} from '../dist/simulation.js';
import {
  createShiftRound, stepShiftRound, queueShiftTurn, setShiftFire, requestShiftForm, throwShiftDisc, setShiftMove,
} from '../dist/grid-shift.js';
import { roundSeed } from '../dist/match.js';
import { getMap } from '../dist/maps.js';

const output = resolve(process.argv[2] || 'artifacts/sim-parity/traces.txt');
const view = new DataView(new ArrayBuffer(8));
function hex(value) {
  view.setFloat64(0, value === 0 ? 0 : value); // -0 and +0 compare equal
  return view.getBigUint64(0).toString(16).padStart(16, '0');
}
function sum(items, fields) {
  let total = 0;
  for (const item of items) for (const field of fields) total += item[field];
  return hex(total);
}

// Inputs: [tick, type, ...args]. Applied before the step at that tick.
function turns(start, every, count, signs) {
  return Array.from({ length: count }, (_, index) => [start + index * every, 'turn', signs[index % signs.length]]);
}
const scenarios = [
  { name: 'duel-hunter-standard-classic', kind: 'duel', seed: 12345, rival: 'hunter', difficulty: 'standard', map: 'classic', ticks: 6000,
    actions: [[50, 'turn', 1], [140, 'turn', -1], [260, 'turn', 1], [330, 'turn', 1], [380, 'fire', 1], [420, 'fire', 0],
      [450, 'flight', 'jet'], [520, 'turn', -1], [700, 'flight', 'sail'], ...turns(800, 160, 20, [1, 1, -1, 1, -1, -1])] },
  { name: 'duel-trapper-expert-vault', kind: 'duel', seed: 777, rival: 'trapper', difficulty: 'expert', map: 'vault', ticks: 6000,
    actions: [[90, 'turn', -1], [150, 'fire', 1], [200, 'fire', 0], [200, 'turn', 1], [310, 'turn', -1], ...turns(420, 140, 24, [1, -1, -1, 1])] },
  { name: 'duel-survivor-rookie-solar-manual', kind: 'duel', seed: 99, rival: 'survivor', difficulty: 'rookie', map: 'solar', ticks: 6000,
    actions: [[0, 'auto', 0], [30, 'throttle', 1], [400, 'throttle', 0], [520, 'throttle', 1], ...turns(120, 150, 26, [1, -1, 1, 1, -1, -1])] },
  { name: 'duel-hunter-expert-solar', kind: 'duel', seed: 4000000000, rival: 'hunter', difficulty: 'expert', map: 'solar', ticks: 6000,
    actions: [[10, 'fire', 1], [80, 'fire', 0], ...turns(60, 110, 30, [-1, 1, 1, -1])] },
  { name: 'arena-standard-classic', kind: 'arena', seed: 4242, difficulty: 'standard', map: 'classic', ticks: 8000,
    actions: [...turns(100, 150, 40, [1, -1, -1, 1, 1, -1]), [300, 'fire', 1], [360, 'fire', 0], [900, 'flight', 'jet'], [1500, 'flight', 'sail']] },
  { name: 'arena-expert-solar-decay', kind: 'arena', seed: 9001, difficulty: 'expert', map: 'solar', rule: 'decay', ticks: 8000,
    actions: turns(80, 130, 50, [1, 1, -1, -1, 1, -1]) },
  { name: 'arena-rookie-vault', kind: 'arena', seed: 31, difficulty: 'rookie', map: 'vault', ticks: 8000,
    actions: [...turns(70, 120, 50, [-1, -1, 1, 1]), [200, 'fire', 1], [500, 'fire', 0]] },
  { name: 'op-portal-route', kind: 'operation', op: 'portal', seed: 5, difficulty: 'standard', ticks: 2400,
    actions: [[158, 'turn', 1], [384, 'turn', -1]] },
  { name: 'op-portal-late', kind: 'operation', op: 'portal', seed: 6, difficulty: 'standard', ticks: 2400,
    actions: [[0, 'auto', 0], [200, 'throttle', 1]] },
  { name: 'op-sweep', kind: 'operation', op: 'sweep', seed: 7, difficulty: 'standard', ticks: 2400,
    actions: turns(90, 110, 20, [1, 1, 1, -1, -1, -1]) },
  { name: 'op-collapse', kind: 'operation', op: 'collapse', seed: 9, difficulty: 'standard', ticks: 2400,
    actions: turns(60, 95, 24, [1, 1, 1, 1, -1, -1, -1, -1]) },
  { name: 'op-sentinel', kind: 'operation', op: 'sentinel', seed: 11, difficulty: 'expert', ticks: 6000,
    actions: [...turns(70, 140, 30, [1, -1, -1, 1]), [250, 'fire', 1], [300, 'fire', 0]] },
  { name: 'op-transit', kind: 'operation', op: 'transit', seed: 13, difficulty: 'standard', ticks: 1600,
    actions: [[66, 'flight', 'jet'], [199, 'flight', 'sail']] },
  { name: 'shift-standard-classic', kind: 'shift', seed: 31337, difficulty: 'standard', map: 'classic', ticks: 8000,
    actions: [[100, 'fire', 1], [160, 'fire', 0], [240, 'form'], [250, 'move', 1, 0], [300, 'disc'], [400, 'move', 0, -1],
      [450, 'disc'], [520, 'move', -.6, .8], [600, 'move', 0, 0], [610, 'form'], [700, 'turn', 1], [720, 'fire', 1],
      [760, 'fire', 0], [800, 'flight', 'jet'], [1000, 'form'], [1010, 'move', 0, 1], [1100, 'disc'], [1300, 'form'],
      ...turns(1400, 150, 20, [1, -1])] },
  { name: 'shift-expert-solar', kind: 'shift', seed: 4, difficulty: 'expert', map: 'solar', ticks: 8000,
    actions: [[60, 'form'], [70, 'move', .7, -.7], [150, 'disc'], [300, 'disc'], [420, 'move', -1, 0], [600, 'form'],
      ...turns(700, 120, 30, [-1, 1, 1, -1])] },
  { name: 'shift-rookie-vault', kind: 'shift', seed: 2024, difficulty: 'rookie', map: 'vault', ticks: 8000,
    actions: [...turns(50, 100, 40, [1, -1, -1, 1]), [500, 'fire', 1], [700, 'fire', 0], [900, 'form'], [905, 'move', 0, -1], [1000, 'disc']] },
  { name: 'shift-foot-idle', kind: 'shift', seed: 77, difficulty: 'expert', map: 'classic', ticks: 36010, actions: [[10, 'form']] },
  { name: 'shift-cycle-parked', kind: 'shift', seed: 78, difficulty: 'standard', map: 'vault', ticks: 36010, actions: [[0, 'auto', 0]] },
  { name: 'shift-foot-dodge', kind: 'shift', seed: 79, difficulty: 'rookie', map: 'solar', ticks: 36010,
    actions: [[5, 'form'], [60, 'move', 1, 0], [180, 'move', 0, 1], [300, 'move', -1, 0], [420, 'move', 0, -1], [540, 'move', 1, 0],
      [200, 'disc'], [400, 'disc'], [600, 'disc'], [800, 'disc'], [660, 'move', 0, 0]] },
  { name: 'lesson', kind: 'lesson', seed: 21, ticks: 1600, actions: [[100, 'turn', 1], [300, 'turn', -1], [500, 'turn', -1]] },
  // Long AI-driven rounds: every rider decides with the same AI, so tactics, shooting, flight,
  // pickups, decay, and late shrink all get exercised over thousands of ticks.
  { name: 'ai4-standard-classic', kind: 'ai4', seed: 101, difficulty: 'standard', map: 'classic', ticks: 36010, actions: [] },
  { name: 'ai4-expert-solar-decay', kind: 'ai4', seed: 202, difficulty: 'expert', map: 'solar', rule: 'decay', ticks: 36010, actions: [] },
  { name: 'ai4-rookie-vault', kind: 'ai4', seed: 303, difficulty: 'rookie', map: 'vault', ticks: 36010, actions: [] },
  { name: 'ai4-expert-classic-decay', kind: 'ai4', seed: 404, difficulty: 'expert', map: 'classic', rule: 'decay', ticks: 36010, actions: [] },
  { name: 'autoplayer-standard-classic', kind: 'autoplayer', seed: 505, difficulty: 'standard', map: 'classic', ticks: 36010, actions: [] },
  { name: 'autoplayer-expert-solar', kind: 'autoplayer', seed: 606, difficulty: 'expert', map: 'solar', ticks: 36010, actions: [] },
  { name: 'autoplayer-rookie-vault-decay', kind: 'autoplayer', seed: 707, difficulty: 'rookie', map: 'vault', rule: 'decay', ticks: 36010, actions: [] },
  { name: 'autoplayer-expert-vault', kind: 'autoplayer', seed: 808, difficulty: 'expert', map: 'vault', ticks: 36010, actions: [] },
  { name: 'shrink-idle', kind: 'lesson', seed: 3, ticks: 36010, actions: [[0, 'auto', 0]] },
];

function build(s) {
  if (s.kind === 'duel') return createDuelRound({ seed: s.seed, rival: s.rival, difficulty: s.difficulty, map: s.map, rule: s.rule || 'classic' });
  if (s.kind === 'arena') return createArenaRound({ seed: s.seed, difficulty: s.difficulty, map: s.map, rule: s.rule || 'classic' });
  if (s.kind === 'operation') return createOperationRound({ id: s.op, seed: s.seed, difficulty: s.difficulty });
  if (s.kind === 'shift') return createShiftRound({ seed: s.seed, difficulty: s.difficulty, map: s.map });
  if (s.kind === 'ai4' || s.kind === 'autoplayer') {
    const spawns = getMap(s.map).spawns.arena;
    const riders = s.kind === 'ai4'
      ? spawns.map((spawn, i) => ({ id: 'AI' + i, ...spawn, controller: 'ai', style: ['hunter', 'survivor', 'trapper', 'survivor'][i] }))
      : [{ id: 'YOU', ...spawns[0], isPlayer: true, controller: 'ai' },
        ...['hunter', 'survivor', 'trapper'].map((style, i) => ({ id: ['AXIOM', 'HELIX', 'PULSE'][i], ...spawns[i + 1], controller: 'ai', style }))];
    return createRound({ seed: s.seed, difficulty: s.difficulty, map: s.map, rule: s.rule || 'classic', multiplayer: s.kind === 'ai4', riders });
  }
  return createRound({ seed: s.seed, riders: [{ id: 'YOU', x: 0, z: 16, dir: 0, isPlayer: true, controller: 'none' }] });
}

function apply(round, shift, [, type, a, b]) {
  if (type === 'turn') return shift ? queueShiftTurn(round, 'YOU', a) : queueTurn(round, 'YOU', a);
  if (type === 'fire') return shift ? setShiftFire(round, 'YOU', a === 1) : setCycleFire(round, 'YOU', a === 1);
  if (type === 'throttle') return setCycleThrottle(round, 'YOU', a === 1);
  if (type === 'auto') return setCycleAutoDrive(round, 'YOU', a === 1);
  if (type === 'flight') return requestFlight(round, 'YOU', a);
  if (type === 'form') return requestShiftForm(round, 'YOU');
  if (type === 'disc') return throwShiftDisc(round, 'YOU');
  if (type === 'move') return setShiftMove(round, 'YOU', a, b);
  throw new Error('Unknown action ' + type);
}

function frame(round, events, applied) {
  const riders = round.riders.map(r => [r.id, hex(r.x), hex(r.z), r.dir, r.alive ? 1 : 0, r.ammo, r.hp ?? '-',
    r.form ?? '-', r.flightKind ?? '-', r.rocketReady ? 1 : 0, hex(r.nitroUntil)].join(':')).join(' ');
  const pickups = round.pickups.map(p => p.type + '@' + hex(p.x) + ',' + hex(p.z) + (p.active ? '' : '!')).join(';');
  const bullets = round.shift ? round.shift.bullets : round.bullets;
  const discs = round.shift ? round.shift.discs : [];
  const evs = events.map(e => e.type === 'crash' ? 'crash:' + e.riderId + ':' + e.reason + ':' + (e.by ?? '') : e.type).join(',');
  const outcome = round.outcome ? round.outcome.kind + ':' + (round.outcome.reason ?? '') + ':' + (round.outcome.by ?? '') : '-';
  return ['F', round.tick, round.randomState >>> 0, round.score, hex(round.limit), round.segments.length,
    sum(round.segments, ['x1', 'z1', 'x2', 'z2']), bullets.length, sum(bullets, ['x', 'z']), discs.length,
    sum(discs, ['x', 'z']), outcome, applied, evs || '-', pickups || '-', riders].join(' | ');
}

const lines = [];
for (const s of scenarios) {
  const shift = s.kind === 'shift';
  lines.push(['S', s.name, s.kind, s.seed, s.rival || '-', s.difficulty || 'standard', s.map || 'classic', s.rule || 'classic', s.op || '-', s.ticks].join(' '));
  for (const action of s.actions) lines.push(['A', ...action].join(' '));
  const round = build(s);
  const byTick = new Map();
  for (const action of s.actions) {
    if (!byTick.has(action[0])) byTick.set(action[0], []);
    byTick.get(action[0]).push(action);
  }
  for (let tick = 0; tick < s.ticks && !round.outcome; tick++) {
    const results = (byTick.get(tick) || []).map(action => apply(round, shift, action) ? 1 : 0).join('');
    if (shift) stepShiftRound(round); else stepRound(round);
    lines.push(frame(round, drainEvents(round), results || '-'));
  }
  lines.push('E ' + s.name);
}
for (const day of ['2026-10-22', '2026-01-01', '2025-12-31', '2027-06-15', '2030-02-28']) {
  let seed = 2166136261;
  for (const char of 'grid-run:daily:v3:' + day) seed = Math.imul(seed ^ char.charCodeAt(0), 16777619) >>> 0;
  lines.push(['D', day, seed, ['classic', 'solar', 'vault'][seed % 3], ['hunter', 'survivor', 'trapper'][(seed >>> 8) % 3]].join(' '));
}
for (const seed of [1, 12345, 4294967295, 2654435769])
  for (let roundNumber = 1; roundNumber <= 5; roundNumber++)
    lines.push(['R', seed, roundNumber, roundSeed({ seed, roundNumber })].join(' '));

mkdirSync(dirname(output), { recursive: true });
writeFileSync(output, lines.join('\n') + '\n');
console.log('Wrote ' + lines.length + ' lines for ' + scenarios.length + ' scenarios to ' + output);
