import { obstacleContains } from './maps.js';

export const SHRINK_START = 180;
export const SHRINK_INTERVAL = 15;
export const ROUND_END = 300;
const TYPES = ['ammo', 'nitro', 'rocket'];

function random(round) {
  let x = round.randomState;
  x ^= x << 13; x ^= x >>> 17; x ^= x << 5;
  round.randomState = x >>> 0;
  return round.randomState / 0x100000000;
}

export function placePickup(round, pickup) {
  for (let attempt = 0; attempt < 100; attempt++) {
    const limit = Math.min(round.limit - 3, 30);
    const x = Math.round((random(round) * 2 - 1) * limit);
    const z = Math.round((random(round) * 2 - 1) * limit);
    if (round.segments.some(segment =>
      x >= Math.min(segment.x1, segment.x2) - 1 && x <= Math.max(segment.x1, segment.x2) + 1 &&
      z >= Math.min(segment.z1, segment.z2) - 1 && z <= Math.max(segment.z1, segment.z2) + 1) ||
        round.obstacles.some(o => obstacleContains(o, x, z, 1.2)) ||
        round.riders.some(r => r.alive && Math.hypot(r.x - x, r.z - z) < 4) ||
        round.pickups.some(p => p !== pickup && p.active && Math.hypot(p.x - x, p.z - z) < 4)) continue;
    pickup.x = x; pickup.z = z; pickup.active = true;
    return;
  }
  pickup.respawnAt = round.elapsed + 5;
}

export function initArenaRules(round) {
  round.baseLimit = round.limit;
  round.pickups = TYPES.map((type, index) => ({ id: index, type,
    active: false, x: 0, z: 0, respawnAt: 0 }));
  for (const pickup of round.pickups) placePickup(round, pickup);
  for (const rider of round.riders) {
    rider.nitroUntil = 0;
    rider.rocketReady = false;
  }
}

export function updateArenaRules(round) {
  const stage = Math.floor(Math.max(0, round.elapsed - SHRINK_START) / SHRINK_INTERVAL) + 1;
  if (round.elapsed >= SHRINK_START) {
    const next = Math.max(4, round.baseLimit - stage * 5);
    if (next < round.limit) {
      round.limit = next;
      round.events.push({ type: 'arena-shrink', limit: next });
    }
  }
  for (const pickup of round.pickups) {
    if (!pickup.active && round.elapsed >= pickup.respawnAt) placePickup(round, pickup);
    if (!pickup.active) continue;
    if (Math.max(Math.abs(pickup.x), Math.abs(pickup.z)) >= round.limit - 1) {
      pickup.active = false; pickup.respawnAt = round.elapsed + 5;
      continue;
    }
    for (const rider of round.riders) {
      if (!rider.alive || Math.hypot(rider.x - pickup.x, rider.z - pickup.z) > .85) continue;
      if (pickup.type === 'ammo') rider.ammo = Math.min(48, rider.ammo + 12);
      if (pickup.type === 'nitro') rider.nitroUntil = round.elapsed + 7;
      if (pickup.type === 'rocket') rider.rocketReady = true;
      pickup.active = false;
      pickup.respawnAt = round.elapsed + 20;
      round.events.push({ type: 'pickup', riderId: rider.id, pickup: pickup.type });
      break;
    }
  }
}
