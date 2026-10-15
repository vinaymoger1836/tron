// Collision, spawn, presentation and navigation data share these definitions.
export const MAPS = Object.freeze({
  classic: {
    id: 'classic', name: 'Classic Grid', limit: 29,
    cue: 'Open square. Read every wall and keep an exit in reserve.',
    palette: { floor: [0.008, 0.026, 0.045], wall: [0.018, 0.072, 0.10], rail: [0.06, 0.66, 0.82], obstacle: [0.03, 0.13, 0.17], radar: '#5edff3' },
    obstacles: [],
    spawns: {
      duel: [{ x: -5, z: 17, dir: 0 }, { x: 5, z: -17, dir: 2 }],
      arena: [{ x: 0, z: 16, dir: 0 }, { x: -17, z: -16, dir: 1 }, { x: 17, z: -14, dir: 3 }, { x: 16, z: 14, dir: 0 }],
    },
  },
  solar: {
    id: 'solar', name: 'Solar Circuit', limit: 30,
    cue: 'Long central sight lines; amber pylons reward early lane changes.',
    palette: { floor: [0.035, 0.022, 0.012], wall: [0.13, 0.068, 0.025], rail: [1, 0.54, 0.17], obstacle: [0.18, 0.084, 0.03], radar: '#ffaf59' },
    obstacles: [
      { id: 'WEST PYLON', minX: -15, maxX: -11, minZ: -5, maxZ: 5 },
      { id: 'EAST PYLON', minX: 11, maxX: 15, minZ: -5, maxZ: 5 },
    ],
    spawns: {
      duel: [{ x: -5, z: 19, dir: 0 }, { x: 5, z: -19, dir: 2 }],
      arena: [{ x: 0, z: 20, dir: 0 }, { x: -20, z: -19, dir: 1 }, { x: 20, z: -19, dir: 3 }, { x: 20, z: 19, dir: 0 }],
    },
  },
  vault: {
    id: 'vault', name: 'Data Vault', limit: 27,
    cue: 'Violet gates split the center. Plan your turn before each narrow passage.',
    palette: { floor: [0.018, 0.014, 0.044], wall: [0.075, 0.035, 0.13], rail: [0.67, 0.34, 1], obstacle: [0.11, 0.05, 0.19], radar: '#b891ff' },
    obstacles: [
      { id: 'NORTHWEST GATE', minX: -10, maxX: -7, minZ: -12, maxZ: -1 },
      { id: 'SOUTHEAST GATE', minX: 7, maxX: 10, minZ: 1, maxZ: 12 },
      { id: 'NORTHEAST CORE', minX: 3, maxX: 10, minZ: -5, maxZ: -2 },
      { id: 'SOUTHWEST CORE', minX: -10, maxX: -3, minZ: 2, maxZ: 5 },
    ],
    spawns: {
      duel: [{ x: -3, z: 18, dir: 0 }, { x: 3, z: -18, dir: 2 }],
      arena: [{ x: 0, z: 19, dir: 0 }, { x: -18, z: -18, dir: 1 }, { x: 18, z: -18, dir: 3 }, { x: 18, z: 18, dir: 0 }],
    },
  },
});

export function getMap(id) { return MAPS[id] || MAPS.classic; }

export function spawnClear(map, spawn, distance = 5) {
  const radius = 0.3;
  const direction = [[0, -1], [1, 0], [0, 1], [-1, 0]][spawn.dir];
  if (!direction) return false;
  for (let step = 0; step <= 20; step++) {
    const x = spawn.x + direction[0] * distance * step / 20;
    const z = spawn.z + direction[1] * distance * step / 20;
    if (Math.abs(x) + radius >= map.limit || Math.abs(z) + radius >= map.limit) return false;
    if (map.obstacles.some(o => x >= o.minX - radius && x <= o.maxX + radius &&
        z >= o.minZ - radius && z <= o.maxZ + radius)) return false;
  }
  return true;
}

export function validateMap(map) {
  if (!map || !(map.limit > 10) || !Array.isArray(map.obstacles)) throw new Error('Invalid map bounds');
  for (const o of map.obstacles) {
    if (!(o.minX < o.maxX && o.minZ < o.maxZ) ||
        Math.max(Math.abs(o.minX), Math.abs(o.maxX), Math.abs(o.minZ), Math.abs(o.maxZ)) >= map.limit - 1)
      throw new Error('Invalid obstacle ' + o.id);
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
