export function createMatch({ seed = 1, rival = 'hunter', difficulty = 'standard' } = {}) {
  return {
    seed: seed >>> 0,
    rival, difficulty,
    roundNumber: 1,
    wins: { player: 0, rival: 0 },
    points: 0,
    history: [],
    completed: false,
    resultRecorded: false,
  };
}

export function roundSeed(match) {
  let value = (match.seed ^ Math.imul(match.roundNumber, 0x9e3779b9)) >>> 0;
  value ^= value >>> 16;
  value = Math.imul(value, 0x7feb352d);
  value ^= value >>> 15;
  value = Math.imul(value, 0x846ca68b);
  return (value ^ (value >>> 16)) >>> 0;
}

export function recordRound(match, outcome, points = 0, actions = []) {
  if (match.resultRecorded || match.completed) return match.completed ? 'complete' : 'next';
  if (!['won', 'lost', 'draw'].includes(outcome)) throw new TypeError('Invalid round outcome');
  match.resultRecorded = true;
  match.points += Math.max(0, Number(points) || 0);
  if (outcome === 'won') match.wins.player++;
  if (outcome === 'lost') match.wins.rival++;
  match.history.push({ round: match.roundNumber, seed: roundSeed(match), outcome, points, actions: actions.map(action => ({ ...action })) });
  match.completed = match.wins.player >= 3 || match.wins.rival >= 3;
  return match.completed ? 'complete' : 'next';
}

export function nextRound(match) {
  if (!match.resultRecorded || match.completed) return false;
  match.roundNumber++;
  match.resultRecorded = false;
  return true;
}
