import test from 'node:test';
import assert from 'node:assert/strict';
import { createRound, queueTurn, stepRound, STEP, SPEED } from '../dist/simulation.js';

function run(round, ticks) {
  for (let i = 0; i < ticks && !round.outcome; i++) stepRound(round);
}

function twoRiders(player, bot) {
  return createRound({
    seed: 42,
    riders: [
      { id: 'YOU', isPlayer: true, controller: 'none', ...player },
      { id: 'BOT', isPlayer: false, controller: 'none', ...bot },
    ],
  });
}

test('human and AI-controlled cycles use the same base speed', () => {
  const round = twoRiders(
    { x: -10, z: -10, dir: 1 },
    { x: 10, z: 10, dir: 3 },
  );
  stepRound(round);
  assert.equal(round.riders[0].x, -10 + SPEED * STEP);
  assert.equal(round.riders[1].x, 10 - SPEED * STEP);
  assert.equal(round.riders[0].alive, true);
  assert.equal(round.riders[1].alive, true);
});

test('a swept move hits a thin light wall between its endpoints', () => {
  const round = twoRiders(
    { x: -0.4, z: 0, dir: 1 },
    { x: 15, z: 15, dir: 0 },
  );
  round.speed = 120;
  round.segments.push({
    id: 99, ownerId: 'BOT', x1: 0, z1: -2, x2: 0, z2: 2,
    active: false, closedAt: 0,
  });
  stepRound(round);
  assert.equal(round.riders[0].alive, false);
  assert.equal(round.outcome.kind, 'lost');
  assert.equal(round.outcome.reason, 'trail');
});

test('a thin fixed barrier is also checked across the whole move', () => {
  const round = createRound({
    seed: 42,
    riders: [{ id: 'YOU', isPlayer: true, controller: 'none', x: -0.4, z: 0, dir: 1 }],
    obstacles: [{ id: 'GATE', minX: -0.02, maxX: 0.02, minZ: -1, maxZ: 1 }],
  });
  round.speed = 120;
  stepRound(round);
  assert.equal(round.outcome.kind, 'lost');
  assert.equal(round.outcome.reason, 'obstacle');
});

test('head-on riders crash on the same simulation tick', () => {
  const round = twoRiders(
    { x: -0.7, z: 0, dir: 1 },
    { x: 0.7, z: 0, dir: 3 },
  );
  round.speed = 90;
  stepRound(round);
  assert.equal(round.riders[0].alive, false);
  assert.equal(round.riders[1].alive, false);
  assert.equal(round.outcome.kind, 'lost');
  assert.equal(round.outcome.reason, 'rider');
  assert.equal(round.score, 250);
});

test('a legal turn clears the connected corner of its own trail', () => {
  const round = createRound({
    seed: 42,
    riders: [{ id: 'YOU', isPlayer: true, controller: 'none', x: 0, z: 0, dir: 0 }],
  });
  run(round, 20);
  assert.equal(queueTurn(round, 'YOU', 1), true);
  stepRound(round);
  assert.equal(round.riders[0].dir, 1);
  assert.equal(round.riders[0].alive, true);
  run(round, 15);
  assert.equal(round.riders[0].alive, true);
});

test('a turn requested during cooldown is buffered and then applied', () => {
  const round = createRound({
    seed: 42,
    riders: [{ id: 'YOU', isPlayer: true, controller: 'none', x: 0, z: 0, dir: 0 }],
  });
  assert.equal(queueTurn(round, 'YOU', 1), true);
  stepRound(round);
  assert.equal(round.riders[0].dir, 1);
  assert.equal(queueTurn(round, 'YOU', 1), true);
  stepRound(round);
  assert.equal(round.riders[0].dir, 1);
  run(round, 22);
  assert.equal(round.riders[0].dir, 2);
  assert.equal(round.riders[0].alive, true);
});

test('a seeded round replays the same AI decisions and state', () => {
  const a = createRound({ seed: 8675309 });
  const b = createRound({ seed: 8675309 });
  for (let tick = 0; tick < 180; tick++) {
    if (tick === 20 || tick === 75) {
      queueTurn(a, 'YOU', 1);
      queueTurn(b, 'YOU', 1);
    }
    stepRound(a);
    stepRound(b);
    assert.deepEqual(
      a.riders.map(r => [r.x, r.z, r.dir, r.alive]),
      b.riders.map(r => [r.x, r.z, r.dir, r.alive]),
    );
    if (a.outcome || b.outcome) break;
  }
  assert.deepEqual(a.outcome, b.outcome);
});

test('a prior crash does not cause a later phantom rider collision', () => {
  const round = createRound({
    seed: 42,
    riders: [
      { id: 'YOU', x: 0, z: 0, dir: 1, isPlayer: true, controller: 'none' },
      { id: 'B', x: 0.74, z: 0, dir: 3, controller: 'none' },
      { id: 'C', x: 0, z: 0.63, dir: 0, controller: 'none' },
    ],
  });
  stepRound(round);
  assert.equal(round.riders[0].alive, false);
  assert.equal(round.riders[1].alive, true);
  assert.equal(round.riders[2].alive, false);
  assert.equal(round.score, 250);
});