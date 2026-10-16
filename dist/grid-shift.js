import { createRound, sweptCircle, STEP, FLIGHT, airborne, requestFlight, settleFlight } from './simulation.js';
import { getMap, spawnClear } from './maps.js';
import { updateArenaRules, ROUND_END } from './arena-rules.js';

const DIR = [[0, -1], [1, 0], [0, 1], [-1, 0]];
const EPS = 1e-8;
const SPEED = { cycle: 10.6, foot: 5.2, disc: 22, bullet: 48 };
const RADIUS = { cycle: 0.36, foot: 0.28 };
const TIME_LIMIT = ROUND_END;
const FORM_COOLDOWN = 1.15;
const FORM_RECOVERY = 0.36;
const DISC_COOLDOWN = 1.45;
const FIRE_INTERVAL = .12;
const BULLET_RANGE = 20;
const BULLET_GAP = 1.35;
const ROUND_AMMO = 24;

function random(round) {
  let x = round.randomState;
  x ^= x << 13; x ^= x >>> 17; x ^= x << 5;
  round.randomState = x >>> 0;
  return round.randomState / 0x100000000;
}
function boxHit(x, z, dx, dz, minX, maxX, minZ, maxZ) {
  let enter = 0, exit = 1;
  for (const [start, delta, min, max] of [[x, dx, minX, maxX], [z, dz, minZ, maxZ]]) {
    if (Math.abs(delta) < EPS) {
      if (start < min || start > max) return null;
      continue;
    }
    const near = Math.min((min - start) / delta, (max - start) / delta);
    const far = Math.max((min - start) / delta, (max - start) / delta);
    enter = Math.max(enter, near); exit = Math.min(exit, far);
    if (enter > exit) return null;
  }
  return exit >= 0 && enter <= 1 ? Math.max(0, enter) : null;
}
function worldHit(round, rider, x, z, dx, dz, radius, skipId = null) {
  const bound = round.limit - radius;
  let hit = null;
  if (Math.abs(x) > bound || Math.abs(z) > bound) hit = { t: 0, reason: 'boundary' };
  else {
    for (const [p, d] of [[x, dx], [z, dz]]) {
      const t = d > EPS && p + d > bound ? (bound - p) / d
        : d < -EPS && p + d < -bound ? (-bound - p) / d : null;
      if (t !== null && (!hit || t < hit.t)) hit = { t, reason: 'boundary' };
    }
  }
  for (const wall of round.obstacles) {
    const t = wall.shape === 'circle'
      ? sweptCircle(x, z, dx, dz, wall.x, wall.z, wall.radius + radius)
      : boxHit(x, z, dx, dz, wall.minX - radius, wall.maxX + radius,
        wall.minZ - radius, wall.maxZ + radius);
    if (t !== null && (!hit || t < hit.t)) hit = { t, reason: 'obstacle', by: wall.id };
  }
  for (const segment of round.segments) {
    if (airborne(round, rider)) break;
    if (segment === rider?.segment && rider.form === 'cycle') continue;
    if (segment.id === skipId || (segment.id === rider?.ignoreSegmentId &&
        rider.distanceSinceTurn < 0.72)) continue;
    const t = boxHit(x, z, dx, dz,
      Math.min(segment.x1, segment.x2) - radius - .09,
      Math.max(segment.x1, segment.x2) + radius + .09,
      Math.min(segment.z1, segment.z2) - radius - .09,
      Math.max(segment.z1, segment.z2) + radius + .09);
    if (t !== null && (!hit || t < hit.t))
      hit = { t, reason: 'trail', by: segment.ownerId, segment };
  }
  return hit;
}
function actorHit(a, b, dxA, dzA, dxB, dzB) {
  const px = a.x - b.x, pz = a.z - b.z;
  const vx = dxA - dxB, vz = dzA - dzB;
  const radius = RADIUS[a.form] + RADIUS[b.form];
  const c = px * px + pz * pz - radius * radius;
  if (c <= 0) return 0;
  const aa = vx * vx + vz * vz;
  if (aa < EPS) return null;
  const bb = 2 * (px * vx + pz * vz);
  const det = bb * bb - 4 * aa * c;
  if (det < 0) return null;
  const t = (-bb - Math.sqrt(det)) / (2 * aa);
  return t >= 0 && t <= 1 ? t : null;
}
function closeTrail(round, rider) {
  if (!rider.segment?.active) return;
  rider.segment.active = false;
  rider.segment.closedAt = round.elapsed;
}
function openTrail(round, rider) {
  const segment = { id: round.nextSegmentId++, ownerId: rider.id, rider,
    x1: rider.x, z1: rider.z, x2: rider.x, z2: rider.z,
    active: true, closedAt: -1 };
  rider.segment = segment;
  round.segments.push(segment);
}
function safeMount(round, rider) {
  const [vx, vz] = DIR[rider.dir];
  if (worldHit(round, rider, rider.x, rider.z, vx * 1.25, vz * 1.25, RADIUS.cycle))
    return false;
  return round.riders.every(other => other === rider || !other.alive ||
    Math.hypot(other.x - rider.x, other.z - rider.z) > 1.55);
}
export function createShiftRound({ seed = 1, difficulty = 'standard',
  playerIds = null, roundNumber = 1, map = 'classic' } = {}) {
  if (playerIds && (playerIds.length < 2 || playerIds.length > 4 ||
      new Set(playerIds).size !== playerIds.length))
    throw new RangeError('Grid Shift requires two to four distinct riders');
  const layout = getMap(map);
  const colors = [[.15, .88, 1], [1, .47, .18], [.75, .3, 1], [1, .73, .2]];
  const styles = ['player', 'hunter', 'survivor', 'trapper'];
  const spawns = playerIds?.length === 2 ? layout.spawns.duel : layout.spawns.arena;
  const rotation = (Math.max(1, roundNumber) - 1) % (spawns?.length || 1);
  const riders = playerIds ? playerIds.map((id, index) => {
    const spawn = spawns[(index + rotation) % spawns.length];
    if (!spawnClear(layout, spawn)) throw new Error('Unsafe Grid Shift spawn');
    return { id, ...spawn, color: colors[index], style: styles[index],
      controller: 'none', isPlayer: false };
  }) : [
    { id: 'YOU', ...layout.spawns.duel[0], isPlayer: true, controller: 'none' },
    { id: 'ECHO', ...layout.spawns.duel[1], controller: 'none',
      style: 'hunter', color: [1, .47, .18] },
  ];
  const round = createRound({ seed, map: layout.id, difficulty,
    riders, multiplayer: Boolean(playerIds) });
  round.shift = { discs: [], bullets: [], timeLimit: TIME_LIMIT, aiIntent: 'reading lanes' };
  for (const rider of round.riders) {
    rider.form = 'cycle';
    rider.hp = 2;
    rider.formReadyAt = 0;
    rider.recoveryUntil = 0;
    rider.discReadyAt = 0;
    rider.ammo = ROUND_AMMO;
    rider.nextShotAt = 0;
    rider.fireHeld = false;
    rider.formChangedAt = -10;
    rider.input = { x: 0, z: 0 };
    rider.footTime = 0;
    rider.aiNextAt = .28 + random(round) * .28;
    rider.tactic = {
      aggression: .35 + random(round) * .5,
      footAt: .75 + random(round) * .8,
      preferCycle: random(round) < .36,
      dodge: .35 + random(round) * .45,
      strafe: random(round) < .5 ? -1 : 1,
    };
  }
  return round;
}
export function setShiftMove(round, id, x, z) {
  const rider = round.riders.find(r => r.id === id);
  if (!rider || !rider.alive || !round.shift) return false;
  const length = Math.hypot(x, z);
  rider.input = length > 1 ? { x: x / length, z: z / length } : { x, z };
  return true;
}
export function queueShiftTurn(round, id, sign) {
  const rider = round.riders.find(r => r.id === id);
  if (!rider || !rider.alive || rider.form !== 'cycle' || round.outcome ||
      (sign !== -1 && sign !== 1)) return false;
  rider.pendingTurn = { sign, expiresAt: round.elapsed + .22 };
  round.actionLog.push({ tick: round.tick, riderId: id, action: 'turn', sign });
  return true;
}
export function queueShiftDirection(round, id, direction) {
  const rider = round.riders.find(r => r.id === id);
  if (!rider || rider.form !== 'cycle' || !Number.isInteger(direction) ||
      direction < 0 || direction > 3 || direction === rider.dir ||
      direction === (rider.dir + 2) % 4) return false;
  return queueShiftTurn(round, id, (direction - rider.dir + 4) % 4 === 1 ? 1 : -1);
}
export function requestShiftForm(round, id) {
  const rider = round.riders.find(r => r.id === id);
  if (!rider || !rider.alive || round.outcome || rider.flightKind) return false;
  if (round.elapsed < rider.formReadyAt || round.elapsed < rider.recoveryUntil) {
    round.events.push({ type: 'shift-denied', riderId: id, reason: 'Form system recharging' });
    return false;
  }
  if (rider.form === 'cycle') {
    const [vx, vz] = DIR[rider.dir];
    const dx = vx * .82, dz = vz * .82;
    if (worldHit(round, rider, rider.x, rider.z, dx, dz, RADIUS.foot, rider.segment.id)) {
      round.events.push({ type: 'shift-denied', riderId: id, reason: 'No clear dismount lane' });
      return false;
    }
    if (round.riders.some(other => other !== rider && other.alive &&
        Math.hypot(other.x - (rider.x + dx), other.z - (rider.z + dz)) < 1)) {
      round.events.push({ type: 'shift-denied', riderId: id, reason: 'Rival blocks dismount' });
      return false;
    }
    closeTrail(round, rider);
    rider.fireHeld = false;
    rider.x += dx; rider.z += dz;
    rider.form = 'foot';
    rider.segment = null;
    rider.footTime = 0;
    rider.ignoreSegmentId = null;
  } else {
    if (round.shift.discs.some(d => d.ownerId === id)) {
      round.events.push({ type: 'shift-denied', riderId: id, reason: 'Recall your disc before mounting' });
      return false;
    }
    if (!safeMount(round, rider)) {
      round.events.push({ type: 'shift-denied', riderId: id, reason: 'No clear space to mount' });
      return false;
    }
    rider.form = 'cycle';
    rider.input = { x: 0, z: 0 };
    openTrail(round, rider);
  }
  rider.formChangedAt = round.elapsed;
  rider.formReadyAt = round.elapsed + FORM_COOLDOWN;
  rider.recoveryUntil = round.elapsed + FORM_RECOVERY;
  round.events.push({ type: 'shift-form', riderId: id, form: rider.form, x: rider.x, z: rider.z });
  round.actionLog.push({ tick: round.tick, riderId: id, action: 'form', form: rider.form });
  return true;
}
export function throwShiftDisc(round, id) {
  const rider = round.riders.find(r => r.id === id);
  if (!rider || !rider.alive || rider.form !== 'foot' || round.outcome ||
      round.elapsed < rider.discReadyAt || round.elapsed < rider.recoveryUntil ||
      round.shift.discs.some(d => d.ownerId === id)) return false;
  const target = round.riders.filter(r => r !== rider && r.alive)
    .sort((a, b) => Math.hypot(a.x - rider.x, a.z - rider.z) -
      Math.hypot(b.x - rider.x, b.z - rider.z))[0];
  const vx = target ? target.x - rider.x : DIR[rider.dir][0];
  const vz = target ? target.z - rider.z : DIR[rider.dir][1];
  const length = Math.hypot(vx, vz) || 1;
  const disc = { ownerId: id, x: rider.x, z: rider.z,
    vx: vx / length, vz: vz / length, age: 0, returning: false };
  round.shift.discs.push(disc);
  rider.discReadyAt = round.elapsed + DISC_COOLDOWN;
  round.events.push({ type: 'disc-throw', riderId: id });
  round.actionLog.push({ tick: round.tick, riderId: id, action: 'throw' });
  return true;
}
function shootBullet(round, rider) {
  if (!rider.alive || rider.form !== 'cycle' || round.outcome ||
      round.elapsed < rider.recoveryUntil || (rider.ammo <= 0 && !rider.rocketReady) ||
      round.elapsed + EPS < rider.nextShotAt) return false;
  const [vx, vz] = DIR[rider.dir];
  round.shift.bullets.push({ ownerId: rider.id, x: rider.x + vx * .8,
    z: rider.z + vz * .8, vx, vz, traveled: 0, rocket: rider.rocketReady });
  if (rider.rocketReady) rider.rocketReady = false;
  else rider.ammo--;
  rider.nextShotAt = round.elapsed + FIRE_INTERVAL;
  if (!rider.ammo) rider.fireHeld = false;
  round.events.push({ type: 'bullet-fire', riderId: rider.id, ammo: rider.ammo,
    rocket: round.shift.bullets.at(-1).rocket });
  round.actionLog.push({ tick: round.tick, riderId: rider.id, action: 'fire' });
  return true;
}
export function setShiftFire(round, id, held) {
  const rider = round.riders.find(r => r.id === id);
  if (!rider || !rider.alive || round.outcome) return false;
  rider.fireHeld = Boolean(held && rider.form === 'cycle' && (rider.ammo > 0 || rider.rocketReady));
  const fired = rider.fireHeld && shootBullet(round, rider);
  return Boolean(rider.fireHeld || fired || !held);
}
function cutTrail(round, segment, x, z) {
  const dx = segment.x2 - segment.x1, dz = segment.z2 - segment.z1;
  const length = Math.hypot(dx, dz);
  if (length < EPS) return;
  const along = Math.max(0, Math.min(length,
    ((x - segment.x1) * dx + (z - segment.z1) * dz) / length));
  const before = Math.max(0, along - BULLET_GAP);
  const after = Math.min(length, along + BULLET_GAP);
  const point = distance => ({ x: segment.x1 + dx * distance / length,
    z: segment.z1 + dz * distance / length });
  const firstEnd = point(before), lastStart = point(after);
  if (segment.active) {
    if (before > EPS) round.segments.push({ ...segment, id: round.nextSegmentId++,
      x2: firstEnd.x, z2: firstEnd.z, active: false, closedAt: round.elapsed });
    segment.x1 = lastStart.x; segment.z1 = lastStart.z;
  } else {
    if (after < length - EPS) round.segments.push({ ...segment,
      id: round.nextSegmentId++, x1: lastStart.x, z1: lastStart.z });
    if (before > EPS) {
      segment.x2 = firstEnd.x; segment.z2 = firstEnd.z;
    } else round.segments.splice(round.segments.indexOf(segment), 1);
  }
}
function moveBullet(round, bullet) {
  const travel = Math.min(SPEED.bullet * STEP, BULLET_RANGE - bullet.traveled);
  if (travel <= 0) return false;
  const dx = bullet.vx * travel, dz = bullet.vz * travel;
  const hit = worldHit(round, null, bullet.x, bullet.z, dx, dz, .12);
  const riderHit = round.riders.filter(r => r.id !== bullet.ownerId && r.alive)
    .map(target => boxHit(bullet.x, bullet.z, dx, dz,
      target.x - RADIUS[target.form] - .12, target.x + RADIUS[target.form] + .12,
      target.z - RADIUS[target.form] - .12, target.z + RADIUS[target.form] + .12))
    .filter(t => t !== null).sort((a, b) => a - b)[0];
  if (riderHit !== undefined && (!hit || riderHit < hit.t)) {
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
  if (bullet.rocket && bullet.traveled >= BULLET_RANGE - EPS) {
    explodeRocket(round, bullet, bullet.x, bullet.z);
    return false;
  }
  return bullet.traveled < BULLET_RANGE - EPS;
}
function explodeRocket(round, bullet, x, z) {
  for (const segment of [...round.segments]) {
    const px = Math.max(Math.min(x, Math.max(segment.x1, segment.x2)), Math.min(segment.x1, segment.x2));
    const pz = Math.max(Math.min(z, Math.max(segment.z1, segment.z2)), Math.min(segment.z1, segment.z2));
    if (Math.hypot(px - x, pz - z) <= 3) cutTrail(round, segment, px, pz);
  }
  for (const rider of round.riders) {
    if (!rider.alive || rider.id === bullet.ownerId || Math.hypot(rider.x - x, rider.z - z) > 3.2) continue;
    rider.hp -= 2;
    if (rider.hp <= 0) {
      rider.alive = false;
      closeTrail(round, rider);
      round.events.push({ type: 'crash', riderId: rider.id, reason: 'rocket',
        by: bullet.ownerId, x: rider.x, z: rider.z });
      if (!round.multiplayer && !rider.isPlayer) round.score += 250;
    }
  }
  round.events.push({ type: 'rocket-explode', riderId: bullet.ownerId, x, z });
}
function turn(round, rider, sign) {
  if (rider.segment && Math.hypot(rider.x - rider.segment.x1, rider.z - rider.segment.z1) < EPS) {
    round.segments.splice(round.segments.indexOf(rider.segment), 1);
  } else if (rider.segment) {
    closeTrail(round, rider);
    rider.ignoreSegmentId = rider.segment.id;
    rider.distanceSinceTurn = 0;
  }
  rider.dir = (rider.dir + sign + 4) % 4;
  rider.lastTurnAt = round.elapsed;
  if (!airborne(round, rider)) openTrail(round, rider);
  round.events.push({ type: 'turn', riderId: rider.id, sign });
}
function spaceAhead(round, rider, dir, length = 10) {
  const [vx, vz] = DIR[dir];
  const hit = worldHit(round, rider, rider.x, rider.z, vx * length, vz * length, RADIUS[rider.form]);
  return hit ? hit.t * length : length;
}
function aiDecision(round, rider) {
  const target = round.riders.find(r => r.isPlayer);
  if (!target?.alive || round.elapsed < rider.aiNextAt) return;
  const interval = { rookie: .43, standard: .29, expert: .19 }[round.difficulty];
  rider.aiNextAt = round.elapsed + interval + random(round) * .13;
  const dist = Math.hypot(target.x - rider.x, target.z - rider.z);
  const enemyDisc = round.shift.discs.find(d => d.ownerId !== rider.id && !d.returning);
  if (rider.form === 'cycle') {
    const [vx, vz] = DIR[rider.dir];
    const forwardHit = worldHit(round, rider, rider.x, rider.z, vx * 10, vz * 10, RADIUS.cycle);
    const front = forwardHit ? forwardHit.t * 10 : 10;
    const left = spaceAhead(round, rider, (rider.dir + 3) % 4, 10);
    const right = spaceAhead(round, rider, (rider.dir + 1) % 4, 10);
    if (forwardHit?.reason === 'trail' && front > 3.7 && front < 10 &&
        rider.ammo > 0 && setShiftFire(round, rider.id, true)) {
      round.shift.aiIntent = 'firing at a wall';
      return;
    }
    setShiftFire(round, rider.id, false);
    if (forwardHit?.reason === 'trail' && front > 1.1 && front < 3.6 &&
        left < front + 2 && right < front + 2 &&
        requestFlight(round, rider.id, round.elapsed >= rider.jetReadyAt ? 'jet' : 'sail')) {
      round.shift.aiIntent = 'lifting over a wall';
      return;
    }
    if (round.elapsed > rider.tactic.footAt + (rider.tactic.preferCycle ? 2.1 : 0) && dist < 29 &&
        (front < 8 || round.elapsed > rider.tactic.footAt + 1.3 ||
          random(round) < (target.form === 'foot' ? .55 : .38) + rider.tactic.aggression * .25) &&
        requestShiftForm(round, rider.id)) {
      round.shift.aiIntent = 'dismounting to throw';
      return;
    }
    if (front < 3.6 || (front < 7.5 && Math.max(left, right) > front + 2)) {
      const sign = left > right ? -1 : right > left ? 1 : random(round) < .5 ? -1 : 1;
      queueShiftTurn(round, rider.id, sign);
      round.shift.aiIntent = 'turning for open lane';
    } else round.shift.aiIntent = 'riding for position';
    return;
  }
  rider.fireHeld = false;
  rider.footTime += interval;
  if (!round.shift.discs.some(d => d.ownerId === rider.id) &&
      round.elapsed >= rider.discReadyAt && dist < 24 && random(round) < .58 + rider.tactic.aggression * .3) {
    throwShiftDisc(round, rider.id);
    round.shift.aiIntent = 'disc thrown';
  }
  if (rider.footTime > 4 + rider.tactic.aggression * 3 &&
      round.elapsed >= rider.formReadyAt && target.form === 'cycle' &&
      requestShiftForm(round, rider.id)) {
    round.shift.aiIntent = 'remounting';
    return;
  }
  const towardX = (target.x - rider.x) / (dist || 1);
  const towardZ = (target.z - rider.z) / (dist || 1);
  const candidates = [[towardX, towardZ], [-towardZ, towardX],
    [towardZ, -towardX], [-towardX, -towardZ], [0, 0]];
  let best = { score: -Infinity, x: 0, z: 0 };
  for (const [x, z] of candidates) {
    const probe = worldHit(round, rider, rider.x, rider.z, x * 2.7, z * 2.7, RADIUS.foot);
    const free = probe ? probe.t * 2.7 : 2.7;
    let score = free * 2 + (dist > 10 ? x * towardX + z * towardZ : -(x * towardX + z * towardZ) * .4);
    score += (x * -towardZ + z * towardX) * rider.tactic.strafe * .3;
    if (enemyDisc && Math.hypot(enemyDisc.x - rider.x, enemyDisc.z - rider.z) < 8) {
      const fromDiscX = rider.x - enemyDisc.x, fromDiscZ = rider.z - enemyDisc.z;
      score += (x * fromDiscX + z * fromDiscZ) * rider.tactic.dodge;
    }
    score += random(round) * .18;
    if (score > best.score) best = { score, x, z };
  }
  setShiftMove(round, rider.id, best.x, best.z);
  round.shift.aiIntent = enemyDisc ? 'dodging disc' : 'seeking a firing lane';
}
function moveDisc(round, disc) {
  const owner = round.riders.find(r => r.id === disc.ownerId);
  if (!owner) return false;
  disc.age += STEP;
  if (!disc.returning && disc.age >= 1.15) disc.returning = true;
  if (disc.returning) {
    const dx = owner.x - disc.x, dz = owner.z - disc.z;
    const length = Math.hypot(dx, dz);
    if (length < .55) {
      owner.discReadyAt = Math.max(owner.discReadyAt, round.elapsed + .3);
      round.events.push({ type: 'disc-return', riderId: owner.id });
      return false;
    }
    disc.vx = dx / length; disc.vz = dz / length;
  }
  const dx = disc.vx * SPEED.disc * STEP, dz = disc.vz * SPEED.disc * STEP;
  const impact = disc.returning ? null : worldHit(round, null, disc.x, disc.z, dx, dz, .16);
  const targetHit = disc.returning ? null : round.riders
    .filter(r => r !== owner && r.alive)
    .map(rider => ({ rider, t: boxHit(disc.x, disc.z, dx, dz,
      rider.x - RADIUS[rider.form] - .24, rider.x + RADIUS[rider.form] + .24,
      rider.z - RADIUS[rider.form] - .24, rider.z + RADIUS[rider.form] + .24) }))
    .filter(hit => hit.t !== null).sort((a, b) => a.t - b.t)[0];
  const target = targetHit && (!impact || targetHit.t <= impact.t + EPS)
    ? targetHit.rider : null;
  if (target) {
    target.hp--;
    disc.returning = true;
    round.events.push({ type: 'disc-hit', riderId: target.id, by: owner.id,
      hp: target.hp, x: target.x, z: target.z });
    if (owner.isPlayer) round.score += 150;
    if (target.hp <= 0) {
      target.alive = false;
      closeTrail(round, target);
      round.events.push({ type: 'crash', riderId: target.id, reason: 'disc',
        by: owner.id, x: target.x, z: target.z });
    }
  }
  if (impact) disc.returning = true;
  else { disc.x += dx; disc.z += dz; }
  return true;
}
export function stepShiftRound(round) {
  if (round.outcome) return round;
  updateArenaRules(round);
  for (const rider of round.riders) if (rider.alive) settleFlight(round, rider);
  const ai = round.multiplayer ? null : round.riders.find(r => !r.isPlayer);
  if (ai?.alive) aiDecision(round, ai);
  for (const rider of round.riders) {
    if (!rider.alive || rider.form !== 'cycle' || !rider.pendingTurn) continue;
    if (rider.pendingTurn.expiresAt < round.elapsed) rider.pendingTurn = null;
    else if (round.elapsed - rider.lastTurnAt >= .16) {
      const sign = rider.pendingTurn.sign;
      rider.pendingTurn = null;
      turn(round, rider, sign);
    }
  }
  for (const rider of round.riders) if (rider.fireHeld) shootBullet(round, rider);
  round.shift.bullets = round.shift.bullets.filter(bullet => moveBullet(round, bullet));
  const moves = round.riders.filter(r => r.alive).map(rider => {
    const direction = rider.form === 'cycle' ? DIR[rider.dir] : [rider.input.x, rider.input.z];
    if (rider.form === 'foot' && Math.hypot(...direction) > .15) {
      rider.dir = Math.abs(direction[0]) > Math.abs(direction[1])
        ? direction[0] > 0 ? 1 : 3 : direction[1] > 0 ? 2 : 0;
    }
    const canMove = rider.form !== 'cycle' || rider.autoDrive || rider.throttleHeld;
    const speed = round.elapsed < rider.recoveryUntil || !canMove ? 0
      : SPEED[rider.form] * (round.elapsed < rider.nitroUntil ? 1.5 : 1) *
        (airborne(round, rider) ? FLIGHT[rider.flightKind].speed : 1);
    const dx = direction[0] * speed * STEP, dz = direction[1] * speed * STEP;
    return { rider, dx, dz,
      hit: worldHit(round, rider, rider.x, rider.z, dx, dz, RADIUS[rider.form]) };
  });
  const actorCollisions = [];
  for (let a = 0; a < moves.length; a++)
    for (let b = a + 1; b < moves.length; b++) {
      const hit = actorHit(moves[a].rider, moves[b].rider,
        moves[a].dx, moves[a].dz, moves[b].dx, moves[b].dz);
      if (hit !== null && !airborne(round, moves[a].rider) && !airborne(round, moves[b].rider))
        actorCollisions.push({ a, b, t: hit });
    }
  actorCollisions.sort((a, b) => a.t - b.t);
  for (const { a, b, t } of actorCollisions)
    if ((!moves[a].hit || t <= moves[a].hit.t + EPS) &&
        (!moves[b].hit || t <= moves[b].hit.t + EPS)) {
      moves[a].hit = { t, reason: 'rider', by: moves[b].rider.id };
      moves[b].hit = { t, reason: 'rider', by: moves[a].rider.id };
    }
  for (const { rider, dx, dz, hit } of moves) {
    const fraction = hit ? Math.max(0, Math.min(1, hit.t)) : 1;
    rider.x += dx * fraction; rider.z += dz * fraction;
    rider.distanceSinceTurn += Math.hypot(dx, dz) * fraction;
    if (rider.form === 'cycle' && rider.segment) {
      rider.segment.x2 = rider.x; rider.segment.z2 = rider.z;
    }
    if (hit) {
      rider.alive = false;
      closeTrail(round, rider);
      round.events.push({ type: 'crash', riderId: rider.id,
        reason: hit.reason, by: hit.by, x: rider.x, z: rider.z });
      if (!rider.isPlayer) round.score += 250;
    }
  }
  round.shift.discs = round.shift.discs.filter(disc => moveDisc(round, disc));
  round.tick++;
  round.elapsed = round.tick * STEP;
  if (round.multiplayer) {
    const alive = round.riders.filter(r => r.alive);
    if (alive.length <= 1)
      round.outcome = { kind: alive.length ? 'won' : 'draw',
        winnerId: alive[0]?.id || null, reason: 'last-rider' };
    else if (round.elapsed >= TIME_LIMIT) {
      const best = Math.max(...alive.map(r => r.hp));
      const leaders = alive.filter(r => r.hp === best);
      round.outcome = { kind: leaders.length === 1 ? 'won' : 'draw',
        winnerId: leaders.length === 1 ? leaders[0].id : null, reason: 'timeout' };
    }
    if (round.outcome)
      round.events.push({ type: 'round-end', ...round.outcome, score: round.score });
    return round;
  }
  const player = round.riders.find(r => r.isPlayer);
  const rival = round.riders.find(r => !r.isPlayer);
  if (!player.alive && !rival.alive) round.outcome = { kind: 'draw', reason: 'mutual' };
  else if (!player.alive) {
    const crash = [...round.events].reverse().find(e => e.type === 'crash' && e.riderId === player.id);
    round.outcome = { kind: 'lost', reason: crash?.reason || 'unknown', by: crash?.by };
  } else if (!rival.alive) {
    round.score += 800 + Math.max(0, Math.floor((TIME_LIMIT - round.elapsed) * 5));
    round.outcome = { kind: 'won', reason: 'rival' };
  } else if (round.elapsed >= TIME_LIMIT) {
    const kind = player.hp > rival.hp ? 'won' : player.hp < rival.hp ? 'lost' : 'draw';
    if (kind === 'won') round.score += 500;
    round.outcome = { kind, reason: 'timeout' };
  }
  if (round.outcome) round.events.push({ type: 'round-end', ...round.outcome, score: round.score });
  return round;
}

