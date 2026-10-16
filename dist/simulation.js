import { getOperation } from './operations.js';
import { getMap, spawnClear } from './maps.js';
import { initArenaRules, updateArenaRules, ROUND_END } from './arena-rules.js';
export const STEP = 1 / 120;
export const SPEED = 10.6;
export const LIMIT = 52;
export const RIDER_RADIUS = 0.3;

const TRAIL_HALF_WIDTH = 0.09;
const TURN_COOLDOWN = 0.16;
const TURN_BUFFER = 0.22;
const BULLET_SPEED = 48;
const BULLET_RANGE = 20;
const FIRE_INTERVAL = 0.12;
const BULLET_GAP = 1.35;
export const ROUND_AMMO = 24;
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

export function createLanRound({ seed = 1, playerIds = [], map = 'classic', roundNumber = 1, rule = 'classic' } = {}) {
  if (playerIds.length < 2 || playerIds.length > 4 ||
      new Set(playerIds).size !== playerIds.length)
    throw new RangeError('LAN rounds require two to four distinct riders');
  const layout = getMap(map);
  const spawns = playerIds.length === 2 ? layout.spawns.duel : layout.spawns.arena;
  const rotation = (Math.max(1, roundNumber) - 1) % spawns.length;
  const colors = [COLORS.player, COLORS.orange, COLORS.violet, COLORS.gold];
  const styles = ['player', 'hunter', 'survivor', 'trapper'];
  const riders = playerIds.map((id, index) => {
    const spawn = spawns[(index + rotation) % spawns.length];
    if (!spawnClear(layout, spawn)) throw new Error('Unsafe LAN spawn on ' + layout.id);
    return { id, ...spawn, color: colors[index], style: styles[index],
      isPlayer: false, controller: 'none' };
  });
  return createRound({ seed, riders, map: layout.id, rule, multiplayer: true });
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
export function createOperationRound({ id = 'portal', seed = 1, difficulty = 'standard' } = {}) {
  const mission = getOperation(id);
  const map = getMap(mission.map);
  if (!spawnClear(map, mission.spawn) ||
      (mission.rivalSpawn && !spawnClear(map, mission.rivalSpawn)))
    throw new Error('Unsafe operation spawn: ' + mission.id);
  const riders = [{ id: 'YOU', isPlayer: true, controller: 'none', ...mission.spawn }];
  if (mission.rivalSpawn) riders.push({
    id: 'SENTINEL', controller: 'ai', style: 'hunter',
    color: COLORS.gold, ...mission.rivalSpawn,
  });
  const round = createRound({ seed, map: mission.map, difficulty, riders });
  round.operation = {
    id: mission.id, progress: 0, warning: '', dynamicObstacles: [],
    pulseIndex: 0, targetLane: null, beam: null, nextLimit: null,
    collapseIndex: 0,
  };
  round.dynamicObstacles = [];
  return round;
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

export function createRound({ seed = 1, riders = DEFAULT_RIDERS, obstacles, difficulty = 'standard', map = 'classic', rule = 'classic', multiplayer = false } = {}) {
  const layout = getMap(map);
  const round = {
    seed: seed >>> 0,
    multiplayer: Boolean(multiplayer),
    randomState: (seed >>> 0) || 0x6d2b79f5,
    tick: 0,
    elapsed: 0,
    speed: SPEED,
    map: layout.id,
    limit: layout.limit,
    rule: rule === 'decay' ? 'decay' : 'classic',
    trailLifetime: rule === 'decay' ? 10 : Infinity,
    score: 0,
    scoreBreakdown: { derez: 0, closeCall: 0, trap: 0, checkpoint: 0, win: 0 },
    difficulty: DIFFICULTIES[difficulty] ? difficulty : 'standard',
    nearMissAwarded: false,
    trapAwarded: false,
    outcome: null,
    riders: [],
    segments: [],
    bullets: [],
    obstacles: (obstacles || layout.obstacles).map(o => ({ ...o })),
    dynamicObstacles: [],
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
      ammo: ROUND_AMMO,
      nextShotAt: 0,
      fireHeld: false,
      autoDrive: true,
      throttleHeld: false,
      flightKind: null, flightStartedAt: -Infinity, flightUntil: 0,
      jetReadyAt: 0, sailReadyAt: 0,
    };
    rider.segment = newSegment(round, rider);
    round.riders.push(rider);
    round.segments.push(rider.segment);
  }
  initArenaRules(round);
  return round;
}

export const FLIGHT = Object.freeze({
  jet: { duration: .8, cooldown: 5, speed: 1.7 },
  sail: { duration: 2.0, cooldown: 7, speed: 1.22 },
});
export function airborne(round, rider) {
  return Boolean(rider?.flightKind && round.elapsed < rider.flightUntil);
}
export function requestFlight(round, riderId, kind) {
  const rider = round.riders.find(r => r.id === riderId);
  const spec = FLIGHT[kind];
  if (!spec || !rider?.alive || round.outcome || rider.flightKind ||
      rider.form === 'foot' || round.elapsed < rider[kind + 'ReadyAt']) return false;
  rider.flightKind = kind;
  rider.flightStartedAt = round.elapsed;
  rider.flightUntil = round.elapsed + spec.duration;
  rider[kind + 'ReadyAt'] = round.elapsed + spec.cooldown;
  if (rider.segment) {
    rider.segment.active = false;
    rider.segment.closedAt = round.elapsed;
    if (Math.hypot(rider.x - rider.segment.x1, rider.z - rider.segment.z1) < EPSILON)
      round.segments.splice(round.segments.indexOf(rider.segment), 1);
    rider.segment = null;
  }
  round.events.push({ type: 'flight-start', riderId, kind });
  round.actionLog.push({ tick: round.tick, riderId, action: kind });
  return true;
}
export function settleFlight(round, rider) {
  if (!rider.flightKind || round.elapsed < rider.flightUntil) return;
  const kind = rider.flightKind;
  rider.flightKind = null;
  rider.flightUntil = 0;
  rider.ignoreSegmentId = null;
  rider.distanceSinceTurn = Infinity;
  const segment = newSegment(round, rider);
  rider.segment = segment;
  round.segments.push(segment);
  round.events.push({ type: 'flight-land', riderId: rider.id, kind });
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

export function setCycleAutoDrive(round, riderId, enabled) {
  const rider = round.riders.find(r => r.id === riderId);
  if (!rider || typeof enabled !== 'boolean') return false;
  rider.autoDrive = enabled;
  return true;
}

export function setCycleThrottle(round, riderId, held) {
  const rider = round.riders.find(r => r.id === riderId);
  if (!rider || typeof held !== 'boolean') return false;
  rider.throttleHeld = held;
  return true;
}

export function queueDirection(round, riderId, direction) {
  const rider = round.riders.find(r => r.id === riderId);
  if (!rider || !Number.isInteger(direction) || direction < 0 || direction > 3 ||
      direction === rider.dir || direction === (rider.dir + 2) % 4) return false;
  return queueTurn(round, riderId, (direction - rider.dir + 4) % 4 === 1 ? 1 : -1);
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

export function sweptCircle(x, z, dx, dz, cx, cz, radius) {
  const px = x - cx, pz = z - cz;
  const c = px * px + pz * pz - radius * radius;
  if (c <= 0) return 0;
  const a = dx * dx + dz * dz;
  if (a < EPSILON) return null;
  const b = 2 * (px * dx + pz * dz);
  const discriminant = b * b - 4 * a * c;
  if (discriminant < 0) return null;
  const t = (-b - Math.sqrt(discriminant)) / (2 * a);
  return t >= 0 && t <= 1 ? t : null;
}
function boundaryHit(limitValue, x, z, dx, dz, radius = RIDER_RADIUS) {
  const limit = limitValue - radius;
  if (Math.abs(x) > limit || Math.abs(z) > limit) return 0;
  let t = null;
  if (dx > EPSILON && x + dx > limit) t = (limit - x) / dx;
  if (dx < -EPSILON && x + dx < -limit) t = (-limit - x) / dx;
  if (dz > EPSILON && z + dz > limit) t = (limit - z) / dz;
  if (dz < -EPSILON && z + dz < -limit) t = (-limit - z) / dz;
  return t;
}

function staticHit(round, rider, x, z, dx, dz, radius = RIDER_RADIUS) {
  let hit = null;
  const boundaryTime = boundaryHit(round.limit, x, z, dx, dz, radius);
  if (boundaryTime !== null) hit = { t: boundaryTime, reason: round.operation?.id === 'collapse' && round.limit < getMap(round.map).limit ? 'collapse' : 'boundary' };

  for (const obstacle of round.obstacles) {
    const t = obstacle.shape === 'circle'
      ? sweptCircle(x, z, dx, dz, obstacle.x, obstacle.z, obstacle.radius + radius)
      : sweptBox(
        x, z, dx, dz,
        obstacle.minX - radius, obstacle.maxX + radius,
        obstacle.minZ - radius, obstacle.maxZ + radius,
      );
    if (t !== null && (!hit || t < hit.t)) {
      hit = { t, reason: 'obstacle', by: obstacle.id };
    }
  }

  for (const obstacle of round.dynamicObstacles) {
    if (obstacle.kind === 'gap' && airborne(round, rider) && rider.flightKind === obstacle.flightKind) continue;
    const t = sweptBox(x, z, dx, dz,
      obstacle.minX - radius, obstacle.maxX + radius,
      obstacle.minZ - radius, obstacle.maxZ + radius);
    if (t !== null && (!hit || t < hit.t))
      hit = { t, reason: obstacle.kind === 'gap' ? 'gap' : 'hazard', by: obstacle.id };
  }

  for (const segment of round.segments) {
    if (airborne(round, rider)) break;
    if (segment === rider?.segment) continue;
    if (segment.id === rider?.ignoreSegmentId &&
        rider.distanceSinceTurn < CORNER_CLEARANCE) continue;
    const minX = Math.min(segment.x1, segment.x2) - TRAIL_HALF_WIDTH - radius;
    const maxX = Math.max(segment.x1, segment.x2) + TRAIL_HALF_WIDTH + radius;
    const minZ = Math.min(segment.z1, segment.z2) - TRAIL_HALF_WIDTH - radius;
    const maxZ = Math.max(segment.z1, segment.z2) + TRAIL_HALF_WIDTH + radius;
    const t = sweptBox(x, z, dx, dz, minX, maxX, minZ, maxZ);
    if (t !== null && (!hit || t < hit.t)) {
      hit = { t, reason: 'trail', by: segment.ownerId, segment };
    }
  }
  return hit;
}

function shootBullet(round, rider) {
  if (!rider.alive || rider.form === 'foot' || round.outcome ||
      (rider.ammo <= 0 && !rider.rocketReady) || round.elapsed + EPSILON < rider.nextShotAt) return false;
  const [vx, vz] = DIR[rider.dir];
  round.bullets.push({ ownerId: rider.id, x: rider.x + vx * .8,
    z: rider.z + vz * .8, vx, vz, traveled: 0, rocket: rider.rocketReady });
  if (rider.rocketReady) rider.rocketReady = false;
  else rider.ammo--;
  rider.nextShotAt = round.elapsed + FIRE_INTERVAL;
  if (!rider.ammo) rider.fireHeld = false;
  round.events.push({ type: 'bullet-fire', riderId: rider.id, ammo: rider.ammo,
    rocket: round.bullets.at(-1).rocket });
  round.actionLog.push({ tick: round.tick, riderId: rider.id, action: 'fire' });
  return true;
}
export function setCycleFire(round, riderId, held) {
  const rider = round.riders.find(r => r.id === riderId);
  if (!rider || !rider.alive || round.outcome) return false;
  rider.fireHeld = Boolean(held && (rider.ammo > 0 || rider.rocketReady));
  const fired = rider.fireHeld && shootBullet(round, rider);
  return Boolean(rider.fireHeld || fired || !held);
}
function cutTrail(round, segment, x, z) {
  const dx = segment.x2 - segment.x1, dz = segment.z2 - segment.z1;
  const length = Math.hypot(dx, dz);
  if (length < EPSILON) return;
  const along = Math.max(0, Math.min(length,
    ((x - segment.x1) * dx + (z - segment.z1) * dz) / length));
  const before = Math.max(0, along - BULLET_GAP);
  const after = Math.min(length, along + BULLET_GAP);
  const point = distance => ({ x: segment.x1 + dx * distance / length,
    z: segment.z1 + dz * distance / length });
  const firstEnd = point(before), lastStart = point(after);
  if (segment.active) {
    if (before > EPSILON) round.segments.push({ ...segment, id: round.nextSegmentId++,
      x2: firstEnd.x, z2: firstEnd.z, active: false, closedAt: round.elapsed });
    segment.x1 = lastStart.x; segment.z1 = lastStart.z;
  } else {
    if (after < length - EPSILON) round.segments.push({ ...segment,
      id: round.nextSegmentId++, x1: lastStart.x, z1: lastStart.z });
    if (before > EPSILON) {
      segment.x2 = firstEnd.x; segment.z2 = firstEnd.z;
    } else round.segments.splice(round.segments.indexOf(segment), 1);
  }
}
function moveBullet(round, bullet) {
  const travel = Math.min(BULLET_SPEED * STEP, BULLET_RANGE - bullet.traveled);
  if (travel <= 0) return false;
  const dx = bullet.vx * travel, dz = bullet.vz * travel;
  const hit = staticHit(round, null, bullet.x, bullet.z, dx, dz, .12);
  let riderHit = null;
  for (const rider of round.riders) {
    if (!rider.alive || rider.id === bullet.ownerId) continue;
    const t = sweptBox(bullet.x, bullet.z, dx, dz,
      rider.x - RIDER_RADIUS - .12, rider.x + RIDER_RADIUS + .12,
      rider.z - RIDER_RADIUS - .12, rider.z + RIDER_RADIUS + .12);
    if (t !== null && (riderHit === null || t < riderHit)) riderHit = t;
  }
  if (riderHit !== null && (!hit || riderHit < hit.t)) {
    if (bullet.rocket) explodeRocket(round, bullet, bullet.x + dx * riderHit, bullet.z + dz * riderHit);
    else round.events.push({ type: 'bullet-blocked', riderId: bullet.ownerId, reason: 'rider' });
    return false;
  }
  if (hit) {
    if (bullet.rocket) {
      explodeRocket(round, bullet, bullet.x + dx * hit.t, bullet.z + dz * hit.t);
      return false;
    }
    if (hit.segment) {
      const x = bullet.x + dx * hit.t, z = bullet.z + dz * hit.t;
      cutTrail(round, hit.segment, x, z);
      round.events.push({ type: 'bullet-cut', riderId: bullet.ownerId,
        by: hit.segment.ownerId, x, z });
    } else round.events.push({ type: 'bullet-blocked', riderId: bullet.ownerId,
      reason: hit.reason });
    return false;
  }
  bullet.x += dx; bullet.z += dz; bullet.traveled += travel;
  if (bullet.rocket && bullet.traveled >= BULLET_RANGE - EPSILON) {
    explodeRocket(round, bullet, bullet.x, bullet.z);
    return false;
  }
  return bullet.traveled < BULLET_RANGE - EPSILON;
}
function explodeRocket(round, bullet, x, z) {
  for (const segment of [...round.segments]) {
    const px = Math.max(Math.min(x, Math.max(segment.x1, segment.x2)), Math.min(segment.x1, segment.x2));
    const pz = Math.max(Math.min(z, Math.max(segment.z1, segment.z2)), Math.min(segment.z1, segment.z2));
    if (Math.hypot(px - x, pz - z) <= 3) cutTrail(round, segment, px, pz);
  }
  for (const rider of round.riders) {
    if (!rider.alive || rider.id === bullet.ownerId || Math.hypot(rider.x - x, rider.z - z) > 3.2) continue;
    rider.alive = false;
    if (rider.segment) {
      rider.segment.active = false;
      rider.segment.closedAt = round.elapsed;
    }
    round.events.push({ type: 'crash', riderId: rider.id, reason: 'rocket', by: bullet.ownerId, x: rider.x, z: rider.z });
    if (!round.multiplayer && !rider.isPlayer) {
      round.score += 250;
      round.scoreBreakdown.derez += 250;
    }
  }
  round.events.push({ type: 'rocket-explode', riderId: bullet.ownerId, x, z });
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
  const [vx, vz] = DIR[rider.dir];
  const forwardHit = staticHit(round, rider, rider.x, rider.z, vx * 10, vz * 10);
  const front = forwardHit ? forwardHit.t * 10 : 10;
  if (forwardHit?.reason === 'trail' && front > 3.7 && front < 10 &&
      rider.ammo > 0 && setCycleFire(round, rider.id, true)) return;
  setCycleFire(round, rider.id, false);
  const horizon = level.horizon + (round.elapsed > rider.tacticAt ? 2 : 0);
  const choices = [
    { sign: 0, direction: rider.dir },
    { sign: -1, direction: (rider.dir + 3) % 4 },
    { sign: 1, direction: (rider.dir + 1) % 4 },
  ].map(choice => ({ ...choice, ...routeScore(round, rider, choice.direction, horizon, choice.sign) }));
  const forward = choices[0];
  const safeDistance = round.speed * (level.interval + 0.18);
  const safeTurns = choices.slice(1).filter(choice => choice.distance > safeDistance);
  if (forwardHit?.reason === 'trail' && front > 1.1 && front < 3.6 && !safeTurns.length &&
      requestFlight(round, rider.id, round.elapsed >= rider.jetReadyAt ? 'jet' : 'sail')) return;
  const best = safeTurns.sort((a, b) => b.score - a.score)[0];
  if (best && (forward.distance < safeDistance || best.score > forward.score + level.margin)) {
    queueTurn(round, rider.id, best.sign);
    round.events.push({ type: 'ai-intent', riderId: rider.id, style: rider.style, sign: best.sign });
  }
}
function applyTurn(round, rider, sign) {
  const priorFree = freeDistance(round, rider, rider.dir, 3);
  if (rider.segment && Math.hypot(rider.x - rider.segment.x1, rider.z - rider.segment.z1) < EPSILON) {
    round.segments.splice(round.segments.indexOf(rider.segment), 1);
  } else if (rider.segment) {
    rider.segment.active = false;
    rider.segment.closedAt = round.elapsed;
    rider.ignoreSegmentId = rider.segment.id;
    rider.distanceSinceTurn = 0;
  }
  rider.dir = (rider.dir + sign + 4) % 4;
  if (!airborne(round, rider)) {
    rider.segment = newSegment(round, rider);
    round.segments.push(rider.segment);
  }
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

function prepareOperation(round) {
  const op = round.operation;
  if (!op) return;
  const mission = getOperation(op.id);
  const t = round.elapsed;
  round.dynamicObstacles = [];
  op.warning = '';
  op.nextLimit = null;
  op.beam = null;
  if (mission.kind === 'traversal') {
    round.dynamicObstacles = mission.gaps.map(gap => ({ ...gap, kind: 'gap', flightKind: gap.kind }));
    op.warning = op.progress < mission.gaps.length
      ? mission.gaps[op.progress].id + ': use ' + mission.gaps[op.progress].kind.toUpperCase() + ' before the edge'
      : 'Both spans cleared. Reach the finish line.';
  } else if (mission.kind === 'checkpoints') {
    for (const [index, gate] of mission.gates.entries()) {
      const open = t >= gate.openFrom && t < gate.openUntil;
      if (!open) round.dynamicObstacles.push({ ...gate, id: gate.id + ' shutter' });
      if (index === op.progress) {
        if (open && op.announcedGate !== index) {
          op.announcedGate = index;
          round.events.push({ type: 'operation-warning', message: gate.id + ' is open.' });
        }
        if (!open) {
          const wait = gate.openFrom - t;
          op.warning = wait > 0 ? gate.id + ' opens in ' + wait.toFixed(1) + 's'
            : gate.id + ' is closed. Retry the route.';
          if (wait <= 0 && op.missedGate !== index) {
            op.missedGate = index;
            round.events.push({ type: 'operation-warning', message: gate.id + ' is closed.' });
          }
        }
      }
    }
  } else if (mission.kind === 'scanner') {
    const pulse = mission.pulses[op.pulseIndex];
    if (pulse !== undefined && t >= pulse + mission.activeTime) {
      op.pulseIndex++;
      op.targetLane = null;
    }
    const next = mission.pulses[op.pulseIndex];
    if (next !== undefined && t >= next - mission.warningTime) {
      if (op.targetLane === null) {
        const player = round.riders.find(r => r.isPlayer);
        op.targetLane = Math.max(-22, Math.min(22, Math.round(player.x / 2) * 2));
        round.events.push({ type: 'operation-warning', message: 'Scanner locked lane ' + op.targetLane + '. Move sideways.' });
      }
      const active = t >= next;
      op.beam = { x: op.targetLane, active, firesAt: next, endsAt: next + mission.activeTime };
      op.warning = active ? 'SCANNER BEAM ACTIVE' : 'SCANNER BEAM IN ' + Math.max(0, next - t).toFixed(1) + 's';
      if (active) round.dynamicObstacles.push({
        id: 'Scanner beam', minX: op.targetLane - 0.65, maxX: op.targetLane + 0.65,
        minZ: -24, maxZ: 24,
      });
    }
  } else if (mission.kind === 'collapse') {
    const next = mission.steps[op.collapseIndex];
    if (next && t >= next.at) {
      round.limit = next.limit;
      op.collapseIndex++;
      round.events.push({ type: 'operation-warning', message: 'Containment ring locked at ' + next.limit + '.' });
    }
    const upcoming = mission.steps[op.collapseIndex];
    if (upcoming && t >= upcoming.at - mission.warningTime) {
      op.nextLimit = upcoming.limit;
      op.warning = 'RING CLOSES IN ' + Math.max(0, upcoming.at - t).toFixed(1) + 's';
      if (!op.warnedLimit || op.warnedLimit !== upcoming.limit) {
        op.warnedLimit = upcoming.limit;
        round.events.push({ type: 'operation-warning', message: 'Move inside the marked ring before it closes.' });
      }
    }
  }
}
function completeOperation(round, bonus) {
  round.score += bonus;
  round.scoreBreakdown.win += bonus;
  round.outcome = { kind: 'won' };
}
function updateOperationProgress(round, player) {
  const op = round.operation;
  const mission = getOperation(op.id);
  if (mission.kind === 'traversal') {
    const gap = mission.gaps[op.progress];
    if (gap && player.z < gap.minZ - RIDER_RADIUS) {
      op.progress++;
      round.score += 400;
      round.scoreBreakdown.checkpoint += 400;
      round.events.push({ type: 'operation-checkpoint', progress: op.progress, total: mission.gaps.length, name: gap.id });
    }
    if (op.progress === mission.gaps.length && player.z <= mission.finishZ)
      completeOperation(round, 1000 + Math.floor((mission.timeLimit - round.elapsed) * 50));
    if (!round.outcome && round.elapsed >= mission.timeLimit)
      round.outcome = { kind: 'lost', reason: 'timeout', by: 'finish line' };
  } else if (mission.kind === 'checkpoints') {
    const gate = mission.gates[op.progress];
    if (gate && round.elapsed >= gate.openFrom && round.elapsed < gate.openUntil &&
        player.x >= gate.minX && player.x <= gate.maxX &&
        player.z >= gate.minZ && player.z <= gate.maxZ) {
      op.progress++;
      round.score += 400;
      round.scoreBreakdown.checkpoint += 400;
      round.events.push({ type: 'operation-checkpoint', progress: op.progress, total: mission.gates.length, name: gate.id });
      if (op.progress === mission.gates.length)
        completeOperation(round, 800 + Math.max(0, Math.floor((mission.timeLimit - round.elapsed) * 25)));
    }
    if (!round.outcome && round.elapsed >= mission.timeLimit)
      round.outcome = { kind: 'lost', reason: 'timeout', by: gate?.id || 'final gate' };
  } else if (mission.kind === 'scanner') {
    if (round.elapsed >= mission.timeLimit) completeOperation(round, 1400);
  } else if (mission.kind === 'collapse') {
    if (round.elapsed >= mission.timeLimit) completeOperation(round, 1500);
  } else if (mission.kind === 'rival' && round.riders.every(r => r.isPlayer || !r.alive)) {
    completeOperation(round, 1250);
  }
}

export function stepRound(round) {
  if (round.outcome) return round;
  prepareOperation(round);
  updateArenaRules(round);
  if (round.rule === 'decay') {
    round.segments = round.segments.filter(segment =>
      segment.active || round.elapsed < segment.closedAt + round.trailLifetime);
  }
  for (const rider of round.riders) {
    if (!rider.alive) continue;
    settleFlight(round, rider);
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

  for (const rider of round.riders) if (rider.fireHeld) shootBullet(round, rider);
  round.bullets = round.bullets.filter(bullet => moveBullet(round, bullet));

  const proposals = round.riders.filter(r => r.alive).map(rider => {
    const distance = (rider.autoDrive || rider.throttleHeld ? 1 : 0) *
      round.speed * (round.elapsed < rider.nitroUntil ? 1.5 : 1) *
      (airborne(round, rider) ? FLIGHT[rider.flightKind].speed : 1) * STEP;
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
      if (t !== null && !airborne(round, proposals[i].rider) && !airborne(round, proposals[j].rider))
        riderEncounters.push({ i, j, t });
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
    if (rider.segment) { rider.segment.x2 = rider.x; rider.segment.z2 = rider.z; }
    rider.distanceSinceTurn += Math.hypot(dx, dz) * fraction;
    if (hit) {
      rider.alive = false;
      if (rider.segment) {
        rider.segment.active = false;
        rider.segment.closedAt = round.elapsed + STEP * fraction;
      }
      round.events.push({
        type: 'crash', riderId: rider.id,
        reason: hit.reason, by: hit.by,
        x: rider.x, z: rider.z,
      });
      if (!round.multiplayer && !rider.isPlayer) {
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
  if (round.multiplayer) {
    const alive = round.riders.filter(r => r.alive);
    if (alive.length <= 1)
      round.outcome = { kind: alive.length ? 'won' : 'draw',
        winnerId: alive[0]?.id || null };
  } else if (player && !player.alive) {
    const crash = [...round.events].reverse().find(e => e.type === 'crash' && e.riderId === player.id);
    round.outcome = { kind: 'lost', reason: crash?.reason || 'unknown', by: crash?.by };
  } else if (round.operation) {
    updateOperationProgress(round, player);
  } else if (round.hasOpponents && round.riders.every(r => r.isPlayer || !r.alive)) {
    round.score += 1000;
    round.scoreBreakdown.win = 1000;
    round.outcome = { kind: 'won' };
  }
  if (!round.outcome && round.elapsed >= ROUND_END)
    round.outcome = round.operation
      ? { kind: 'lost', reason: 'timeout' }
      : { kind: 'draw', winnerId: null, reason: 'timeout' };
  if (round.outcome) round.events.push({ type: 'round-end', ...round.outcome, score: round.score });
  return round;
}
