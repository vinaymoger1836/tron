import { obstacleContains } from './maps.js';

export const SHRINK_START = 180;
export const SHRINK_INTERVAL = 15;
export const ROUND_END = 300;
const TYPES = ['ammo', 'nitro', 'rocket'];
const FIRST_PICKUP_AT = 4;
const PICKUP_INTERVAL = 8;
const PICKUP_LIFETIME = 14;
const DIR = [[0, -1], [1, 0], [0, 1], [-1, 0]];

function random(round) {
  let x = round.randomState;
  x ^= x << 13; x ^= x >>> 17; x ^= x << 5;
  round.randomState = x >>> 0;
  return round.randomState / 0x100000000;
}

export function placePickup(round, pickup) {
  const riders = round.riders.filter(rider => rider.alive);
  if (!riders.length) return false;
  const target = riders[Math.floor(random(round) * riders.length)];
  const [forwardX, forwardZ] = DIR[target.dir];
  for (let attempt = 0; attempt < 100; attempt++) {
    let x, z;
    if (attempt < 70 || round.operation) {
      const forward = 5 + random(round) * 10;
      const side = (random(round) * 2 - 1) * 9;
      x = Math.round(target.x + forwardX * forward - forwardZ * side);
      z = Math.round(target.z + forwardZ * forward + forwardX * side);
    } else {
      const range = Math.min(round.limit - 3, 30);
      x = Math.round((random(round) * 2 - 1) * range);
      z = Math.round((random(round) * 2 - 1) * range);
    }
    if (Math.max(Math.abs(x), Math.abs(z)) >= round.limit - 2 ||
        (round.lastPickupPoint && Math.hypot(x - round.lastPickupPoint.x,
          z - round.lastPickupPoint.z) < 6) ||
        round.segments.some(segment =>
          x >= Math.min(segment.x1, segment.x2) - 1.2 &&
          x <= Math.max(segment.x1, segment.x2) + 1.2 &&
          z >= Math.min(segment.z1, segment.z2) - 1.2 &&
          z <= Math.max(segment.z1, segment.z2) + 1.2) ||
        [...round.obstacles, ...round.dynamicObstacles]
          .some(obstacle => obstacleContains(obstacle, x, z, 1.2)) ||
        riders.some(rider => Math.hypot(rider.x - x, rider.z - z) < 4) ||
        round.pickups.some(other => Math.hypot(other.x - x, other.z - z) < 5))
      continue;
    pickup.x = x;
    pickup.z = z;
    pickup.active = true;
    pickup.expiresAt = round.elapsed + PICKUP_LIFETIME;
    round.lastPickupPoint = { x, z };
    return true;
  }
  return false;
}

export function initArenaRules(round) {
  round.baseLimit = round.limit;
  round.pickups = [];
  round.pickupTypes = [...TYPES];
  for (let index = round.pickupTypes.length - 1; index > 0; index--) {
    const other = Math.floor(random(round) * (index + 1));
    [round.pickupTypes[index], round.pickupTypes[other]] =
      [round.pickupTypes[other], round.pickupTypes[index]];
  }
  round.nextPickupAt = FIRST_PICKUP_AT;
  round.nextPickupId = 1;
  round.pickupSerial = 0;
  round.lastPickupPoint = null;
  for (const rider of round.riders) {
    rider.nitroUntil = 0;
    rider.rocketReady = false;
  }
}

export function updateArenaRules(round) {
  if (round.outcome) return;
  const stage = Math.floor(Math.max(0, round.elapsed - SHRINK_START) / SHRINK_INTERVAL) + 1;
  if (round.elapsed >= SHRINK_START) {
    const next = Math.max(4, round.baseLimit - stage * 5);
    if (next < round.limit) {
      round.limit = next;
      round.events.push({ type: 'arena-shrink', limit: next });
    }
  }

  round.pickups = round.pickups.filter(pickup => pickup.active &&
    round.elapsed < pickup.expiresAt &&
    Math.max(Math.abs(pickup.x), Math.abs(pickup.z)) < round.limit - 1);

  if (round.elapsed >= round.nextPickupAt) {
    const type = round.pickupTypes[round.pickupSerial % round.pickupTypes.length];
    const pickup = { id: round.nextPickupId++, type, active: false, x: 0, z: 0,
      expiresAt: 0 };
    if (placePickup(round, pickup)) {
      round.pickups.push(pickup);
      round.pickupSerial++;
      round.nextPickupAt = round.elapsed + PICKUP_INTERVAL;
      round.events.push({ type: 'pickup-spawn', pickup: type,
        x: pickup.x, z: pickup.z });
    } else round.nextPickupAt = round.elapsed + 2;
  }

  for (const pickup of round.pickups) {
    for (const rider of round.riders) {
      if (!rider.alive || Math.hypot(rider.x - pickup.x, rider.z - pickup.z) > .85) continue;
      if (pickup.type === 'ammo') rider.ammo = Math.min(48, rider.ammo + 12);
      if (pickup.type === 'nitro') rider.nitroUntil = round.elapsed + 7;
      if (pickup.type === 'rocket') rider.rocketReady = true;
      pickup.active = false;
      round.events.push({ type: 'pickup', riderId: rider.id, pickup: pickup.type });
      break;
    }
  }
}
