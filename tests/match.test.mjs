import test from 'node:test';
import assert from 'node:assert/strict';
import { createMatch, recordRound, nextRound, roundSeed } from '../dist/match.js';

// Failure modes: a draw awards an undeserved point; an old result is counted twice;
// rematch seed/order differs even though the recorded match seed is unchanged.
test('Duel is first to three with draws replayed and results counted once', () => {
  const match = createMatch({ seed: 94, rival: 'trapper', difficulty: 'expert' });
  assert.equal(match.roundNumber, 1);
  assert.equal(recordRound(match, 'draw', 0), 'next');
  nextRound(match);
  assert.deepEqual(match.wins, { player: 0, rival: 0 });
  for (let i = 0; i < 2; i++) {
    assert.equal(recordRound(match, 'won', 1250), 'next');
    assert.equal(recordRound(match, 'won', 1250), 'next');
    nextRound(match);
  }
  assert.equal(recordRound(match, 'won', 1250), 'complete');
  assert.deepEqual(match.wins, { player: 3, rival: 0 });
  assert.equal(match.points, 3750);
  assert.equal(match.completed, true);
  assert.equal(recordRound(match, 'lost', 0), 'complete');
  assert.deepEqual(match.wins, { player: 3, rival: 0 });
});

test('a recorded match seed reproduces round seeds and distinct openings', () => {
  const a = createMatch({ seed: 12345 });
  const b = createMatch({ seed: 12345 });
  const seeds = [];
  for (let i = 0; i < 5; i++) {
    seeds.push(roundSeed(a));
    assert.equal(roundSeed(a), roundSeed(b));
    recordRound(a, i % 2 ? 'won' : 'lost', 0);
    recordRound(b, i % 2 ? 'won' : 'lost', 0);
    nextRound(a); nextRound(b);
  }
  assert.equal(new Set(seeds).size, 5);
});

test('recorded Duel seed and player actions replay the same AI decisions', async () => {
  const { createDuelRound, queueTurn, stepRound } = await import('../dist/simulation.js');
  const match = createMatch({ seed: 2, rival: 'hunter', difficulty: 'standard' });
  const seed = roundSeed(match);
  const inputs = new Map([[77, 1], [418, -1], [589, -1], [777, 1], [945, 1]]);
  const play = (round, recorded) => {
    for (let tick = 0; tick < 2500 && !round.outcome; tick++) {
      const sign = recorded ? recorded.get(round.tick) : inputs.get(round.tick);
      if (sign) queueTurn(round, 'YOU', sign);
      stepRound(round);
    }
  };
  const original = createDuelRound({ seed, rival: match.rival, difficulty: match.difficulty });
  play(original);
  recordRound(match, original.outcome.kind, original.score, original.actionLog);
  const history = match.history[0];
  const replayInputs = new Map(history.actions.filter(a => a.riderId === 'YOU').map(a => [a.tick, a.sign]));
  const replay = createDuelRound({ seed: history.seed, rival: match.rival, difficulty: match.difficulty });
  play(replay, replayInputs);
  assert.deepEqual(replay.outcome, original.outcome);
  assert.deepEqual(replay.actionLog, history.actions);
});
