// Collision, spawn, presentation and navigation data share these definitions.
export const MAPS = Object.freeze({
  classic: {
    id: 'classic', name: 'Classic Grid', limit: 34,
    cue: 'Open square. Read every wall and keep an exit in reserve.',
    palette: { floor: [0.008, 0.026, 0.045], wall: [0.018, 0.072, 0.10], rail: [0.06, 0.66, 0.82], obstacle: [0.03, 0.13, 0.17], radar: '#5edff3' },
    obstacles: [],
    spawns: {
      duel: [{ x: -5, z: 17, dir: 0 }, { x: 5, z: -17, dir: 2 }],
      arena: [{ x: 0, z: 16, dir: 0 }, { x: -17, z: -16, dir: 1 }, { x: 17, z: -14, dir: 3 }, { x: 16, z: 14, dir: 0 }],
    },
  },
  solar: {
    id: 'solar', name: 'Solar Circuit', limit: 38,
    cue: 'Long central sight lines; amber pylons reward early lane changes.',
    palette: { floor: [0.035, 0.022, 0.012], wall: [0.13, 0.068, 0.025], rail: [1, 0.54, 0.17], obstacle: [0.18, 0.084, 0.03], radar: '#ffaf59' },
    obstacles: [
      { id: 'WEST REACTOR', shape: 'circle', x: -14, z: 0, radius: 3.1 },
      { id: 'EAST REACTOR', shape: 'circle', x: 14, z: 0, radius: 3.1 },
      { id: 'NORTH SPARK', shape: 'circle', x: 0, z: -12, radius: 1.8 },
      { id: 'SOUTH SPARK', shape: 'circle', x: 0, z: 12, radius: 1.8 },
    ],
    spawns: {
      duel: [{ x: -5, z: 19, dir: 0 }, { x: 5, z: -19, dir: 2 }],
      arena: [{ x: 0, z: 20, dir: 0 }, { x: -20, z: -19, dir: 1 }, { x: 20, z: -19, dir: 3 }, { x: 20, z: 19, dir: 0 }],
    },
  },
  vault: {
    id: 'vault', name: 'Data Vault', limit: 36,
    cue: 'Round violet nodes split the center. Plan your turn before each narrow passage.',
    palette: { floor: [0.018, 0.014, 0.044], wall: [0.075, 0.035, 0.13], rail: [0.67, 0.34, 1], obstacle: [0.11, 0.05, 0.19], radar: '#b891ff' },
    obstacles: [
      { id: 'NORTHWEST NODE', shape: 'circle', x: -11, z: -9, radius: 2.8 },
      { id: 'SOUTHEAST NODE', shape: 'circle', x: 11, z: 9, radius: 2.8 },
      { id: 'NORTHEAST NODE', shape: 'circle', x: 11, z: -9, radius: 2.8 },
      { id: 'SOUTHWEST NODE', shape: 'circle', x: -11, z: 9, radius: 2.8 },
      { id: 'VAULT CORE', shape: 'circle', x: 0, z: 0, radius: 3.3 },
    ],
    spawns: {
      duel: [{ x: -3, z: 18, dir: 0 }, { x: 3, z: -18, dir: 2 }],
      arena: [{ x: 0, z: 19, dir: 0 }, { x: -18, z: -18, dir: 1 }, { x: 18, z: -18, dir: 3 }, { x: 18, z: 18, dir: 0 }],
    },
  },
});

export function getMap(id) { return MAPS[id] || MAPS.classic; }
export function obstacleContains(obstacle, x, z, radius = 0) {
  return obstacle.shape === 'circle'
    ? Math.hypot(x - obstacle.x, z - obstacle.z) <= obstacle.radius + radius
    : x >= obstacle.minX - radius && x <= obstacle.maxX + radius &&
      z >= obstacle.minZ - radius && z <= obstacle.maxZ + radius;
}

export function spawnClear(map, spawn, distance = 5) {
  const radius = 0.3;
  const direction = [[0, -1], [1, 0], [0, 1], [-1, 0]][spawn.dir];
  if (!direction) return false;
  for (let step = 0; step <= 20; step++) {
    const x = spawn.x + direction[0] * distance * step / 20;
    const z = spawn.z + direction[1] * distance * step / 20;
    if (Math.abs(x) + radius >= map.limit || Math.abs(z) + radius >= map.limit) return false;
    if (map.obstacles.some(o => obstacleContains(o, x, z, radius))) return false;
  }
  return true;
}

export function validateMap(map) {
  if (!map || !(map.limit > 10) || !Array.isArray(map.obstacles)) throw new Error('Invalid map bounds');
  for (const o of map.obstacles) {
    const valid = o.shape === 'circle'
      ? o.radius > 0 && Math.max(Math.abs(o.x), Math.abs(o.z)) + o.radius < map.limit - 1
      : o.minX < o.maxX && o.minZ < o.maxZ &&
        Math.max(Math.abs(o.minX), Math.abs(o.maxX), Math.abs(o.minZ), Math.abs(o.maxZ)) < map.limit - 1;
    if (!valid) throw new Error('Invalid obstacle ' + o.id);
  }
  for (const [mode, spawns] of Object.entries(map.spawns)) {
    if (!spawns.every(s => spawnClear(map, s))) throw new Error('Blocked ' + mode + ' spawn on ' + map.id);
    for (let i = 0; i < spawns.length; i++)
      for (let j = i + 1; j < spawns.length; j++)
        if (Math.hypot(spawns[i].x - spawns[j].x, spawns[i].z - spawns[j].z) < 7)
          throw new Error('Crowded ' + mode + ' spawn on ' + map.id);
  }
  return true;
}

Object.values(MAPS).forEach(validateMap);
