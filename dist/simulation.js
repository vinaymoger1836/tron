import { getMap, spawnClear } from './maps.js';
export const STEP = 1 / 120;
export const SPEED = 10.6;
export const LIMIT = 29;
export const RIDER_RADIUS = 0.3;

const TRAIL_HALF_WIDTH = 0.09;
const TURN_COOLDOWN = 0.16;
const TURN_BUFFER = 0.22;
const CORNER_CLEARANCE = RIDER_RADIUS + TRAIL_HALF_WIDTH + 0.02;
const EPSILON = 1e-8;
const DIR = [[0, -1], [1, 0], [0, 1], [-1, 0]];
const COLORS = {
  player: [0.15, 0.88, 1.0],
  orange: [1.0, 0.38, 0.09],
  violet: [0.75, 0.30, 1.0],
  gold: [1.0, 0.73, 0.20],
};
const DEFAULT_RIDERS = [
  { id: 'YOU', x: 0, z: 16, dir: 0, color: COLORS.player, isPlayer: true },
  { id: 'AXIOM', x: -17, z: -16, dir: 1, color: COLORS.orange },
  { id: 'HELIX', x: 17, z: -14, dir: 3, color: COLORS.violet },
  { id: 'PULSE', x: 16, z: 14, dir: 0, color: COLORS.gold },
];

export const DUEL_RIVALS = Object.freeze({
  survivor: { id: 'AEGIS', label: 'Aegis', style: 'Survivor', cue: 'Protects open lanes', color: COLORS.violet },
  hunter: { id: 'VANTA', label: 'Vanta', style: 'Hunter', cue: 'Pressures your route', color: COLORS.orange },
  trapper: { id: 'KNOT', label: 'Knot', style: 'Trapper', cue: 'Closes exits with light walls', color: COLORS.gold },
});
export const DIFFICULTIES = Object.freeze({
  rookie: { label: 'Rookie', interval: 0.42, horizon: 8, margin: 1.3 },
  standard: { label: 'Standard', interval: 0.26, horizon: 11, margin: 0.65 },
  expert: { label: 'Expert', interval: 0.15, horizon: 15, margin: 0.2 },
});

export function createDuelRound({ seed = 1, rival = 'hunter', difficulty = 'standard', map = 'classic', rule = 'classic' } = {}) {
  const profile = DUEL_RIVALS[rival] || DUEL_RIVALS.hunter;
  const layout = getMap(map);
  const side = (seed >>> 0) & 1 ? 1 : -1;
  const offset = ((seed >>> 4) % 5 - 2) * 0.35;
  const [player, opponent] = layout.spawns.duel;
  const playerSpawn = { ...player, x: player.x * side + offset };
  const rivalSpawn = { ...opponent, x: opponent.x * side - offset };
  if (!spawnClear(layout, playerSpawn) || !spawnClear(layout, rivalSpawn))
    throw new Error('Unsafe Duel spawn on ' + layout.id);
  return createRound({
    seed, difficulty, map: layout.id, rule,
    riders: [
      { id: 'YOU', isPlayer: true, controller: 'none', ...playerSpawn },
      { id: profile.id, controller: 'ai', style: rival, ...rivalSpawn, color: profile.color },
    ],
  });
}

export function createArenaRound({ seed = 1, difficulty = 'standard', map = 'classic', rule = 'classic' } = {}) {
  const layout = getMap(map);
  const names = ['AXIOM', 'HELIX', 'PULSE'];
  const styles = ['hunter', 'survivor', 'trapper'];
  const colors = [COLORS.orange, COLORS.violet, COLORS.gold];
  const rotation = seed % 3;
  const spawns = layout.spawns.arena.map((spawn, index) => {
    const jitter = index === 0 ? 0 : (((seed >>> (index * 5)) % 5) - 2) * 0.35;
    const candidate = { ...spawn, x: spawn.x + jitter };
    if (!spawnClear(layout, candidate)) throw new Error('Unsafe Arena spawn on ' + layout.id);
    return candidate;
  });
  return createRound({
    seed, difficulty, map: layout.id, rule,
    riders: [
      { id: 'YOU', isPlayer: true, controller: 'none', ...spawns[0] },
      ...names.map((id, index) => ({
        id, controller: 'ai', style: styles[(index + rotation) % 3],
        color: colors[index], ...spawns[index + 1],
      })),
    ],
  });
}
function nextRandom(round) {
  let x = round.randomState;
  x ^= x << 13;
  x ^= x >>> 17;
  x ^= x << 5;
  round.randomState = x >>> 0;
  return round.randomState / 0x100000000;
}

function newSegment(round, rider) {
  return {
    id: round.nextSegmentId++,
    ownerId: rider.id,
    rider,
    x1: rider.x, z1: rider.z,
    x2: rider.x, z2: rider.z,
    active: true, closedAt: -1,
  };
}

export function createRound({ seed = 1, riders = DEFAULT_RIDERS, obstacles, difficulty = 'standard', map = 'classic', rule = 'classic' } = {}) {
  const layout = getMap(map);
  const round = {
    seed: seed >>> 0,
    randomState: (seed >>> 0) || 0x6d2b79f5,
    tick: 0,
    elapsed: 0,
    speed: SPEED,
    map: layout.id,
    limit: layout.limit,
    rule: rule === 'decay' ? 'decay' : 'classic',
    trailLifetime: rule === 'decay' ? 10 : Infinity,
    score: 0,
    scoreBreakdown: { derez: 0, closeCall: 0, trap: 0, win: 0 },
    difficulty: DIFFICULTIES[difficulty] ? difficulty : 'standard',
    nearMissAwarded: false,
    trapAwarded: false,
    outcome: null,
    riders: [],
    segments: [],
    obstacles: (obstacles || layout.obstacles).map(o => ({ ...o })),
    events: [],
    actionLog: [],
    nextSegmentId: 1,
    hasOpponents: riders.some(r => !r.isPlayer),
  };
  for (const source of riders) {
    const rider = {
      id: source.id,
      x: source.x,
      z: source.z,
      dir: source.dir,
      color: source.color || (source.isPlayer ? COLORS.player : COLORS.orange),
      isPlayer: Boolean(source.isPlayer),
      controller: source.controller || (source.isPlayer ? 'none' : 'ai'),
      style: source.style || (source.isPlayer ? 'player' : 'survivor'),
      tacticBias: nextRandom(round) * 2 - 1,
      tacticAt: 5 + nextRandom(round) * 7,
      alive: true,
      lastTurnAt: -Infinity,
      pendingTurn: null,
      nextDecisionAt: 0,
      ignoreSegmentId: null,
      distanceSinceTurn: Infinity,
      segment: null,
    };
    rider.segment = newSegment(round, rider);
    round.riders.push(rider);
    round.segments.push(rider.segment);
  }
  return round;
}

export function drainEvents(round) {
  return round.events.splice(0);
}

export function queueTurn(round, riderId, sign) {
  const rider = round.riders.find(r => r.id === riderId);
  if (!rider || !rider.alive || round.outcome || (sign !== -1 && sign !== 1)) return false;
  const readyAt = rider.lastTurnAt + TURN_COOLDOWN;
  rider.pendingTurn = {
    sign,
    expiresAt: round.elapsed + Math.max(TURN_BUFFER, readyAt - round.elapsed + STEP),
  };
  round.actionLog.push({ tick: round.tick, riderId, sign });
  if (readyAt > round.elapsed + EPSILON) {
    round.events.push({ type: 'turn-queued', riderId, sign });
  }
  return true;
}

function sweptBox(x, z, dx, dz, minX, maxX, minZ, maxZ) {
  let enter = 0;
  let exit = 1;
  for (const [start, delta, min, max] of [
    [x, dx, minX, maxX],
    [z, dz, minZ, maxZ],
  ]) {
    if (Math.abs(delta) < EPSILON) {
      if (start < min || start > max) return null;
      continue;
    }
    const near = Math.min((min - start) / delta, (max - start) / delta);
    const far = Math.max((min - start) / delta, (max - start) / delta);
    enter = Math.max(enter, near);
    exit = Math.min(exit, far);
    if (enter > exit) return null;
  }
  return exit >= 0 && enter <= 1 ? Math.max(0, enter) : null;
}

function boundaryHit(limitValue, x, z, dx, dz) {
  const limit = limitValue - RIDER_RADIUS;
  if (Math.abs(x) > limit || Math.abs(z) > limit) return 0;
  let t = null;
  if (dx > EPSILON && x + dx > limit) t = (limit - x) / dx;
  if (dx < -EPSILON && x + dx < -limit) t = (-limit - x) / dx;
  if (dz > EPSILON && z + dz > limit) t = (limit - z) / dz;
  if (dz < -EPSILON && z + dz < -limit) t = (-limit - z) / dz;
  return t;
}

function staticHit(round, rider, x, z, dx, dz) {
  let hit = null;
  const boundaryTime = boundaryHit(round.limit, x, z, dx, dz);
  if (boundaryTime !== null) hit = { t: boundaryTime, reason: 'boundary' };

  for (const obstacle of round.obstacles) {
    const t = sweptBox(
      x, z, dx, dz,
      obstacle.minX - RIDER_RADIUS, obstacle.maxX + RIDER_RADIUS,
      obstacle.minZ - RIDER_RADIUS, obstacle.maxZ + RIDER_RADIUS,
    );
    if (t !== null && (!hit || t < hit.t)) {
      hit = { t, reason: 'obstacle', by: obstacle.id };
    }
  }

  for (const segment of round.segments) {
    if (segment === rider.segment) continue;
    if (segment.id === rider.ignoreSegmentId &&
        rider.distanceSinceTurn < CORNER_CLEARANCE) continue;
    const minX = Math.min(segment.x1, segment.x2) - TRAIL_HALF_WIDTH - RIDER_RADIUS;
    const maxX = Math.max(segment.x1, segment.x2) + TRAIL_HALF_WIDTH + RIDER_RADIUS;
    const minZ = Math.min(segment.z1, segment.z2) - TRAIL_HALF_WIDTH - RIDER_RADIUS;
    const maxZ = Math.max(segment.z1, segment.z2) + TRAIL_HALF_WIDTH + RIDER_RADIUS;
    const t = sweptBox(x, z, dx, dz, minX, maxX, minZ, maxZ);
    if (t !== null && (!hit || t < hit.t)) {
      hit = { t, reason: 'trail', by: segment.ownerId };
    }
  }
  return hit;
}

function movingRiderHit(a, b) {
  const px = a.rider.x - b.rider.x;
  const pz = a.rider.z - b.rider.z;
  const vx = a.dx - b.dx;
  const vz = a.dz - b.dz;
  const radius = RIDER_RADIUS * 2;
  const c = px * px + pz * pz - radius * radius;
  if (c <= 0) return 0;
  const aa = vx * vx + vz * vz;
  if (aa < EPSILON) return null;
  const bb = 2 * (px * vx + pz * vz);
  const discriminant = bb * bb - 4 * aa * c;
  if (discriminant < 0) return null;
  const t = (-bb - Math.sqrt(discriminant)) / (2 * aa);
  return t >= 0 && t <= 1 ? t : null;
}

function freeDistance(round, rider, dir, maxDistance = 10) {
  const [vx, vz] = DIR[dir];
  const hit = staticHit(round, rider, rider.x, rider.z, vx * maxDistance, vz * maxDistance);
  let distance = hit ? hit.t * maxDistance : maxDistance;
  for (const other of round.riders) {
    if (other === rider || !other.alive) continue;
    const forward = (other.x - rider.x) * vx + (other.z - rider.z) * vz;
    const side = Math.abs((other.x - rider.x) * vz - (other.z - rider.z) * vx);
    if (forward > 0 && side < RIDER_RADIUS * 2) {
      distance = Math.min(distance, Math.max(0, forward - RIDER_RADIUS * 2));
    }
  }
  return distance;
}

function routeScore(round, rider, direction, horizon, sign) {
  const distance = freeDistance(round, rider, direction, horizon);
  const [vx, vz] = DIR[direction];
  const probe = Math.min(distance, 4);
  const future = { ...rider, x: rider.x + vx * probe, z: rider.z + vz * probe };
  const left = freeDistance(round, future, (direction + 3) % 4, 6);
  const right = freeDistance(round, future, (direction + 1) % 4, 6);
  const escape = Math.max(left, right);
  const opponent = round.riders.find(other => other.isPlayer && other.alive);
  const targetDistance = opponent
    ? Math.hypot(future.x - opponent.x, future.z - opponent.z) : 20;
  const crowded = round.segments.reduce((count, segment) => {
    if (segment.ownerId === rider.id && segment === rider.segment) return count;
    const cx = (segment.x1 + segment.x2) * 0.5;
    const cz = (segment.z1 + segment.z2) * 0.5;
    return count + (Math.hypot(future.x - cx, future.z - cz) < 5 ? 1 : 0);
  }, 0);
  let score = distance + escape * 0.34 - crowded * 0.35 - (sign ? 0.55 : 0);
  const playerTurnedRecently = opponent && round.elapsed - opponent.lastTurnAt < 1.5;
  if (rider.style === 'hunter') {
    score -= targetDistance * (round.elapsed > rider.tacticAt ? 0.35 : 0.24);
    if (playerTurnedRecently && direction === opponent.dir && targetDistance < 12) score += 1.5;
  }
  else if (rider.style === 'trapper') {
    score -= targetDistance * 0.16;
    if (opponent && direction % 2 !== opponent.dir % 2 && targetDistance < 14) score += playerTurnedRecently ? 3.4 : 2.2;
  } else score += targetDistance * 0.23;
  score += rider.tacticBias * sign * (round.elapsed > rider.tacticAt ? 1.15 : 0.55);
  return { score, distance };
}

function decideAI(round, rider) {
  if (round.elapsed + EPSILON < rider.nextDecisionAt || rider.pendingTurn) return;
  const level = DIFFICULTIES[round.difficulty];
  rider.nextDecisionAt = round.elapsed + level.interval + nextRandom(round) * 0.05;
  const horizon = level.horizon + (round.elapsed > rider.tacticAt ? 2 : 0);
  const choices = [
    { sign: 0, direction: rider.dir },
    { sign: -1, direction: (rider.dir + 3) % 4 },
    { sign: 1, direction: (rider.dir + 1) % 4 },
  ].map(choice => ({ ...choice, ...routeScore(round, rider, choice.direction, horizon, choice.sign) }));
  const forward = choices[0];
  const safeDistance = round.speed * (level.interval + 0.18);
  const safeTurns = choices.slice(1).filter(choice => choice.distance > safeDistance);
  const best = safeTurns.sort((a, b) => b.score - a.score)[0];
  if (best && (forward.distance < safeDistance || best.score > forward.score + level.margin)) {
    queueTurn(round, rider.id, best.sign);
    round.events.push({ type: 'ai-intent', riderId: rider.id, style: rider.style, sign: best.sign });
  }
}
function applyTurn(round, rider, sign) {
  const priorFree = freeDistance(round, rider, rider.dir, 3);
  rider.segment.active = false;
  rider.segment.closedAt = round.elapsed;
  rider.ignoreSegmentId = rider.segment.id;
  rider.distanceSinceTurn = 0;
  rider.dir = (rider.dir + sign + 4) % 4;
  rider.segment = newSegment(round, rider);
  round.segments.push(rider.segment);
  rider.lastTurnAt = round.elapsed;
  const nearMiss = priorFree < 2.4;
  round.events.push({ type: 'turn', riderId: rider.id, sign, nearMiss });
  if (rider.isPlayer && nearMiss && !round.nearMissAwarded) {
    round.nearMissAwarded = true;
    round.score += 75;
    round.scoreBreakdown.closeCall = 75;
    round.events.push({ type: 'skill', kind: 'close-call', points: 75 });
  }
}

export function stepRound(round) {
  if (round.outcome) return round;
  if (round.rule === 'decay') {
    round.segments = round.segments.filter(segment =>
      segment.active || round.elapsed < segment.closedAt + round.trailLifetime);
  }
  for (const rider of round.riders) {
    if (!rider.alive) continue;
    if (rider.controller === 'ai') decideAI(round, rider);
    const pending = rider.pendingTurn;
    if (!pending) continue;
    if (pending.expiresAt + EPSILON < round.elapsed) {
      rider.pendingTurn = null;
    } else if (round.elapsed - rider.lastTurnAt + EPSILON >= TURN_COOLDOWN) {
      rider.pendingTurn = null;
      applyTurn(round, rider, pending.sign);
    }
  }

  const distance = round.speed * STEP;
  const proposals = round.riders.filter(r => r.alive).map(rider => {
    const [vx, vz] = DIR[rider.dir];
    const dx = vx * distance;
    const dz = vz * distance;
    return {
      rider, dx, dz,
      hit: staticHit(round, rider, rider.x, rider.z, dx, dz),
    };
  });

  const riderEncounters = [];
  for (let i = 0; i < proposals.length; i++) {
    for (let j = i + 1; j < proposals.length; j++) {
      const t = movingRiderHit(proposals[i], proposals[j]);
      if (t !== null) riderEncounters.push({ i, j, t });
    }
  }
  riderEncounters.sort((a, b) => a.t - b.t);
  for (const { i, j, t } of riderEncounters) {
    const a = proposals[i], b = proposals[j];
    if ((!a.hit || t <= a.hit.t + EPSILON) &&
        (!b.hit || t <= b.hit.t + EPSILON)) {
      a.hit = { t, reason: 'rider', by: b.rider.id };
      b.hit = { t, reason: 'rider', by: a.rider.id };
    }
  }

  for (const proposal of proposals) {
    const { rider, dx, dz, hit } = proposal;
    const fraction = hit ? Math.max(0, Math.min(1, hit.t)) : 1;
    rider.x += dx * fraction;
    rider.z += dz * fraction;
    rider.segment.x2 = rider.x;
    rider.segment.z2 = rider.z;
    rider.distanceSinceTurn += distance * fraction;
    if (hit) {
      rider.alive = false;
      rider.segment.active = false;
      rider.segment.closedAt = round.elapsed + STEP * fraction;
      round.events.push({
        type: 'crash', riderId: rider.id,
        reason: hit.reason, by: hit.by,
        x: rider.x, z: rider.z,
      });
      if (!rider.isPlayer) {
        round.score += 250;
        round.scoreBreakdown.derez += 250;
        if (hit.reason === 'trail' && hit.by === 'YOU' && !round.trapAwarded) {
          round.trapAwarded = true;
          round.score += 150;
          round.scoreBreakdown.trap = 150;
          round.events.push({ type: 'skill', kind: 'clean-trap', points: 150 });
        }
      }
    }
  }

  round.tick++;
  round.elapsed = round.tick * STEP;
  const player = round.riders.find(r => r.isPlayer);
  if (player && !player.alive) {
    const crash = [...round.events].reverse().find(e => e.type === 'crash' && e.riderId === player.id);
    round.outcome = { kind: 'lost', reason: crash?.reason || 'unknown', by: crash?.by };
  } else if (round.hasOpponents && round.riders.every(r => r.isPlayer || !r.alive)) {
    round.score += 1000;
    round.scoreBreakdown.win = 1000;
    round.outcome = { kind: 'won' };
  }
  if (round.outcome) round.events.push({ type: 'round-end', ...round.outcome, score: round.score });
  return round;
}
