import { roundSeed } from './match.js';

export function createArenaMatch({ seed = 1, map = 'classic', difficulty = 'standard', rule = 'classic' } = {}) {
  return {
    seed: seed >>> 0, map, difficulty, rule,
    roundNumber: 1, wins: { player: 0, rival: 0 }, points: 0,
    history: [], completed: false, resultRecorded: false,
  };
}

export function recordArenaRound(match, outcome, points = 0, actions = []) {
  if (match.resultRecorded || match.completed) return match.completed ? 'complete' : 'next';
  if (!['won', 'lost'].includes(outcome)) throw new TypeError('Invalid Arena outcome');
  match.resultRecorded = true;
  match.points += Math.max(0, Number(points) || 0);
  match.wins[outcome === 'won' ? 'player' : 'rival']++;
  match.history.push({
    round: match.roundNumber, seed: roundSeed(match), outcome, points,
    actions: actions.map(action => ({ ...action })),
  });
  match.completed = match.roundNumber === 3;
  return match.completed ? 'complete' : 'next';
}

export function nextArenaRound(match) {
  if (!match.resultRecorded || match.completed) return false;
  match.roundNumber++;
  match.resultRecorded = false;
  return true;
}

export function arenaWon(match) { return match.completed && match.wins.player >= 2; }
