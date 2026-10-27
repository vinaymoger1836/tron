import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { readFile } from 'node:fs/promises';
import { randomBytes, randomInt } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { createLanRound, drainEvents, queueDirection, queueTurn, setCycleAutoDrive, setCycleThrottle, setCycleFire, requestFlight, stepRound, STEP } from './dist/simulation.js';
import { MAPS } from './dist/maps.js';
import { createShiftRound, queueShiftDirection, queueShiftTurn, requestShiftForm, setShiftFire,
  setShiftMove, stepShiftRound, throwShiftDisc } from './dist/grid-shift.js';

const DIST = path.join(path.dirname(fileURLToPath(import.meta.url)), 'dist');
const STATIC = new Set(['index.html', 'lan.html', 'lan.js', 'peer.html', 'peer.js', 'game.js', 'simulation.js',
  'maps.js', 'match.js', 'arena.js', 'operations.js', 'grid-shift.js', 'arena-rules.js',
  'pickup-icons.js']);
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8' };
const ROOM_TTL = 60 * 60 * 1000;
// How long a dropped rider's seat is held (a phone call or app switch). TRON_RECONNECT_MS shortens it
// for verification runs.
const RECONNECT_MS = Math.max(1000, Number.parseInt(process.env.TRON_RECONNECT_MS, 10) || 30000);
// Idle streams get a blank line this often, so clients can tell a silent server from a dead connection.
// Every client skips blank lines.
const HEARTBEAT_MS = 2000;
const CLOSED_TTL = 10 * 60 * 1000;
const LIVE_PHASES = new Set(['countdown', 'running', 'paused', 'connection-wait']);
const SNAPSHOT_MS = 50;
const ROOM_LIMIT = 24;
const CODE_CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
const DIGITS = '0123456789';
const MODES = new Set(['duel', 'arena', 'shift']);
// Online protocol version. Raise PROTOCOL when a change would break installed app builds, and set
// TRON_MIN_PROTOCOL to turn older apps away with an update message. Clients that send no version
// (browser pages from this server and the first app builds) count as protocol 1.
const PROTOCOL = 1;
const MIN_PROTOCOL = Math.max(1, Math.min(PROTOCOL, Number.parseInt(process.env.TRON_MIN_PROTOCOL, 10) || 1));
const rooms = new Map();
// Recently closed room codes and why they closed, so riders still in them get a clear message.
const closedRooms = new Map();
const roomAttempts = new Map();
let lastAttemptPrune = 0;

function onlineOrigin() {
  return process.env.TRON_PUBLIC_ORIGIN ? new URL(process.env.TRON_PUBLIC_ORIGIN) : null;
}
function allowRoomAttempt(req) {
  if (!onlineOrigin()) return true;
  const now = Date.now();
  if (now - lastAttemptPrune > 60000) {
    for (const [address, record] of roomAttempts)
      if (now - record.started > 60000) roomAttempts.delete(address);
    lastAttemptPrune = now;
  }
  const address = req.socket.remoteAddress || 'unknown';
  let record = roomAttempts.get(address);
  if (!record || now - record.started > 60000) {
    record = { started: now, count: 0 }; roomAttempts.set(address, record);
  }
  return ++record.count <= 60;
}

function code() {
  let value;
  do {
    const chars = onlineOrigin() ? DIGITS : CODE_CHARS;
    value = Array.from({ length: 6 }, () => chars[randomInt(chars.length)]).join('');
  } while (rooms.has(value) || closedRooms.has(value));
  return value;
}
function missingRoom(id) {
  return closedRooms.get(id)?.message ||
    'This room is no longer on the server. The host may have left, or the server restarted.';
}
function closeRoom(room, message) {
  rooms.delete(room.code);
  closedRooms.set(room.code, { message, at: Date.now() });
  for (const p of room.players) {
    const stream = p.stream;
    p.stream = null;
    p.connected = false;
    stream?.end();
  }
}
function token() { return randomBytes(24).toString('base64url'); }
function name(value) {
  const clean = String(value || '').trim().replace(/[\u0000-\u001f\u007f]/g, '');
  if (!clean || clean.length > 18) throw new Error('Use a name of 1 to 18 characters.');
  return clean;
}
function json(res, status, value) {
  const body = JSON.stringify(value);
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': Buffer.byteLength(body), 'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff' });
  res.end(body);
}
function fail(res, status, message) { json(res, status, { error: message }); }
async function readJson(req) {
  if (!String(req.headers['content-type'] || '').startsWith('application/json'))
    throw new Error('Send JSON.');
  let body = '';
  for await (const part of req) {
    body += part;
    if (body.length > 2048) throw new Error('Request is too large.');
  }
  try { return JSON.parse(body || '{}'); } catch { throw new Error('Invalid JSON.'); }
}
function hostAllowed(req) {
  if (onlineOrigin()) return req.headers.host === onlineOrigin().host;
  try {
    const hostname = new URL('http://' + req.headers.host).hostname;
    if (hostname === 'localhost' || hostname === '127.0.0.1') return true;
    return Object.values(os.networkInterfaces()).flat()
      .some(address => address?.family === 'IPv4' && address.address === hostname);
  } catch { return false; }
}
function originAllowed(req) {
  if (!req.headers.origin) return true;
  if (onlineOrigin()) return req.headers.origin === onlineOrigin().origin;
  try {
    const origin = new URL(req.headers.origin);
    return origin.host === req.headers.host && origin.protocol === 'http:';
  } catch { return false; }
}
function sessionToken(req) {
  return String(req.headers.cookie || '').split('; ')
    .find(part => part.startsWith('tron_lan_session='))
    ?.slice('tron_lan_session='.length);
}
function playerFor(room, req) {
  const provided = sessionToken(req);
  return room.players.find(player => player.token === provided);
}
function seatCookie(res, player) {
  res.setHeader('Set-Cookie', 'tron_lan_session=' + player.token +
    '; Path=/api; HttpOnly; SameSite=Strict; Max-Age=3600' +
    (onlineOrigin()?.protocol === 'https:' ? '; Secure' : ''));
}
function snapshot(room, selfId) {
  const round = room.round;
  return {
    code: room.code, phase: room.phase, selfId, hostId: room.hostId,
    maxPlayers: room.maxPlayers, map: room.map,
    mode: room.mode, rule: room.mode === 'arena' ? room.rule : 'classic', roundNumber: room.roundNumber,
    countdownMs: room.phase === 'countdown' ? Math.max(0, room.deadline - Date.now()) : 0,
    waitMs: room.phase === 'connection-wait' ? Math.max(0, room.deadline - Date.now()) : 0,
    result: room.result, players: room.players.map(p => ({
      id: p.id, name: p.name, connected: p.connected, wins: p.wins,
    })),
    round: round ? {
      tick: round.tick, elapsed: round.elapsed, limit: round.limit, map: round.map,
      pickups: round.pickups,
      rule: round.rule, trailLifetime: Number.isFinite(round.trailLifetime) ? round.trailLifetime : null,
      riders: round.riders.map(r => ({ id: r.id, x: r.x, z: r.z, dir: r.dir,
        color: r.color, style: r.style, alive: r.alive, ammo: r.ammo,
        form: r.form || 'cycle', hp: r.hp ?? null,
        nitroUntil: r.nitroUntil, rocketReady: r.rocketReady,
        flightKind: r.flightKind, flightStartedAt: r.flightStartedAt,
        flightUntil: r.flightUntil, jetReadyAt: r.jetReadyAt, sailReadyAt: r.sailReadyAt,
        autoDrive: r.autoDrive, throttleHeld: r.throttleHeld,
        formReadyAt: r.formReadyAt ?? null, discReadyAt: r.discReadyAt ?? null,
        recoveryUntil: r.recoveryUntil ?? 0, formChangedAt: r.formChangedAt ?? -10 })),
      segments: round.segments.map(s => ({ id: s.id, ownerId: s.ownerId,
        x1: s.x1, z1: s.z1, x2: s.x2, z2: s.z2, active: s.active,
        closedAt: s.closedAt ?? -1 })),
      bullets: (round.shift?.bullets || round.bullets)
        .map(b => ({ ownerId: b.ownerId, x: b.x, z: b.z, vx: b.vx, vz: b.vz, rocket: b.rocket })),
      shift: round.shift ? { timeLimit: round.shift.timeLimit,
        discs: round.shift.discs.map(d => ({ ownerId: d.ownerId, x: d.x, z: d.z,
          returning: d.returning })) } : null,
      crashes: room.crashes,
      effects: room.effects.filter(effect => Date.now() - effect.at < 1100),
    } : null,
  };
}
function send(room, player) {
  if (!player.stream || player.stream.destroyed) return;
  try { player.stream.write(JSON.stringify(snapshot(room, player.id)) + '\n'); }
  catch { player.stream = null; player.connected = false; }
}
function broadcast(room) {
  room.touched = room.lastSent = Date.now();
  for (const player of room.players) send(room, player);
}
function heartbeat(room) {
  room.lastSent = Date.now();
  for (const player of room.players) {
    if (!player.stream || player.stream.destroyed) continue;
    try { player.stream.write('\n'); } catch { player.stream = null; player.connected = false; }
  }
}
function nextRound(room) {
  room.roundNumber++;
  const options = { seed: randomInt(0x100000000),
    playerIds: room.players.map(p => p.id), roundNumber: room.roundNumber, map: room.map,
    rule: room.mode === 'arena' ? room.rule : 'classic' };
  room.round = room.mode === 'shift' ? createShiftRound(options)
    : createLanRound({ ...options, map: room.map });
  room.crashes = [];
  room.effects = [];
  room.result = null;
  room.phase = 'countdown';
  room.deadline = Date.now() + 3000;
  room.accumulator = 0;
  room.lastTickAt = performance.now();
  broadcast(room);
}
function finishRound(room) {
  const winnerId = room.round.outcome?.winnerId || null;
  const winner = room.players.find(p => p.id === winnerId);
  if (winner) winner.wins++;
  const matchComplete = room.mode === 'arena' ? room.roundNumber >= 3 : winner?.wins >= 3;
  const best = Math.max(...room.players.map(p => p.wins));
  const leaders = room.players.filter(p => p.wins === best);
  const champion = matchComplete && (room.mode !== 'arena' || leaders.length === 1)
    ? (room.mode === 'arena' ? leaders[0] : winner) : null;
  room.result = {
    winnerId, winnerName: winner?.name || null, crashes: room.crashes,
    matchWinnerId: champion?.id || null, matchWinnerName: champion?.name || null,
    matchDraw: Boolean(matchComplete && !champion),
  };
  room.phase = matchComplete ? 'match-result' : 'round-result';
  room.round.riders.forEach(r => { r.fireHeld = false; });
  broadcast(room);
}
function handleDisconnect(room, player) {
  player.connected = false;
  player.stream = null;
  player.disconnectedAt = Date.now();
  if (room.phase === 'running' || room.phase === 'countdown' || room.phase === 'paused') {
    if (room.round) room.round.riders.forEach(r => {
      r.fireHeld = false;
      r.throttleHeld = false;
      if (r.input) r.input = { x: 0, z: 0 };
    });
    room.beforeWait = room.phase;
    room.deadlineBeforeWait = room.deadline;
    room.phase = 'connection-wait';
    room.deadline = Date.now() + RECONNECT_MS;
    room.waitStarted = Date.now();
    room.lastWaitBroadcast = Date.now();
  }
  broadcast(room);
}
// A guest who leaves mid-match, or stays away past the hold, gives up the seat: their rider is out
// of the heat and later heats run without them. The room keeps at least two seats, so a lone host
// sees an unfilled room rather than a one-rider match.
function releaseSeat(room, player, reason) {
  room.players = room.players.filter(p => p !== player);
  room.maxPlayers = Math.max(2, room.players.length);
  room.released.add(player.token);
  const stream = player.stream;
  player.stream = null;
  player.connected = false;
  stream?.end();
  const rider = room.round?.riders.find(r => r.id === player.id);
  if (rider?.alive && !room.round.outcome && LIVE_PHASES.has(room.phase)) {
    rider.alive = false;
    if (rider.segment) rider.segment.active = false;
    rider.fireHeld = false;
    room.crashes.push({ riderId: player.id, reason, name: player.name });
  }
}
// Ends a live heat that releases left with one rider or none; returns whether it ended.
function settleRound(room) {
  if (!room.round || room.round.outcome || !LIVE_PHASES.has(room.phase)) return false;
  const alive = room.round.riders.filter(r => r.alive);
  if (alive.length > 1) return false;
  room.round.outcome = { kind: alive.length ? 'won' : 'draw', winnerId: alive[0]?.id || null };
  finishRound(room);
  return true;
}
function endWait(room, now) {
  room.phase = room.beforeWait;
  room.deadline = room.deadlineBeforeWait;
  if (room.phase === 'countdown') room.deadline += Date.now() - room.waitStarted;
  room.lastTickAt = now;
  room.accumulator = 0;
}
function tickRoom(room, now) {
  if (room.phase === 'connection-wait') {
    if (room.players.every(p => p.connected)) {
      endWait(room, now);
      broadcast(room);
    } else if (Date.now() >= room.deadline) {
      if (!room.round) { room.phase = 'lobby'; broadcast(room); return; }
      // The host keeps their seat (the room is theirs); their rider is out of this heat.
      for (const p of room.players.filter(p => !p.connected)) {
        if (p.id !== room.hostId) { releaseSeat(room, p, 'disconnect'); continue; }
        const rider = room.round.riders.find(r => r.id === p.id);
        if (rider?.alive) {
          rider.alive = false;
          if (rider.segment) rider.segment.active = false;
          rider.fireHeld = false;
          room.crashes.push({ riderId: p.id, reason: 'disconnect', name: p.name });
        }
      }
      endWait(room, now);
      if (!settleRound(room)) broadcast(room);
    } else if (Date.now() - room.lastWaitBroadcast >= 1000) {
      // Keeps every client's hold countdown current.
      room.lastWaitBroadcast = Date.now();
      broadcast(room);
    }
    return;
  }
  if (room.phase === 'round-result' || room.phase === 'match-result') {
    const gone = room.players.filter(p => !p.connected && p.id !== room.hostId &&
      Date.now() - (p.disconnectedAt || 0) >= RECONNECT_MS);
    if (gone.length) {
      for (const p of gone) releaseSeat(room, p, 'disconnect');
      broadcast(room);
    }
    return;
  }
  if (room.phase === 'countdown') {
    if (Date.now() >= room.deadline) {
      room.phase = 'running';
      room.lastTickAt = now;
      broadcast(room);
    }
    return;
  }
  if (room.phase !== 'running') return;
  room.accumulator = Math.min(room.accumulator + Math.max(0, (now - room.lastTickAt) / 1000), STEP * 16);
  room.lastTickAt = now;
  let steps = 0;
  while (room.accumulator >= STEP && steps < 16 && !room.round.outcome) {
    if (room.mode === 'shift') stepShiftRound(room.round);
    else stepRound(room.round);
    for (const event of drainEvents(room.round)) {
      if (event.type === 'crash') room.crashes.push({
        riderId: event.riderId, reason: event.reason, by: event.by || null });
      if (['pickup', 'pickup-spawn', 'rocket-explode', 'arena-shrink'].includes(event.type)) {
        room.effects.push({ ...event, id: ++room.effectSeq, at: Date.now() });
        if (room.effects.length > 16) room.effects.shift();
      }
    }
    room.accumulator -= STEP;
    steps++;
  }
  if (room.round.outcome) finishRound(room);
  else if (Date.now() - room.lastBroadcast >= SNAPSHOT_MS) {
    room.lastBroadcast = Date.now();
    broadcast(room);
  }
}
function authorizedRoom(id, req, res) {
  const room = rooms.get(id);
  if (!room) { fail(res, 404, missingRoom(id)); return null; }
  const player = playerFor(room, req);
  if (!player) {
    const released = room.released.has(sessionToken(req));
    fail(res, onlineOrigin() ? 404 : 403, released ? 'You were away too long, so your seat was released.' :
      onlineOrigin() ? 'Room not found or seat unavailable.' : 'This seat is unavailable. Rejoin the room.');
    return null;
  }
  return { room, player };
}
function protocolError(body) {
  const version = body.protocol ?? 1;
  if (!Number.isInteger(version) || version < MIN_PROTOCOL)
    return 'This app is too old for this game server. Update Grid Run to play online.';
  if (version > PROTOCOL)
    return 'This game server is older than your app. Try again after the server is updated.';
  return null;
}
function createPlayer(id, value) {
  return { id, name: name(value), token: token(), connected: false,
    stream: null, wins: 0, lastInputAt: 0, inputCount: 0 };
}
function lanBaseUrl(req) {
  if (onlineOrigin()) return onlineOrigin().origin + '/lan.html';
  let address = req.socket.localAddress;
  if (!address || address === '127.0.0.1' || address === '0.0.0.0') {
    address = Object.values(os.networkInterfaces()).flat()
      .find(item => item?.family === 'IPv4' && !item.internal &&
        (/^10\./.test(item.address) || /^192\.168\./.test(item.address) ||
          /^172\.(1[6-9]|2[0-9]|3[01])\./.test(item.address)))?.address || 'localhost';
  }
  return 'http://' + address + ':' + req.socket.localPort + '/lan.html';
}
async function handleApi(req, res, pathname) {
  if (!originAllowed(req)) return fail(res, 403, 'Open the room from this LAN server.');
  if (req.method === 'GET' && pathname === '/api/lan/status')
    return json(res, 200, { available: true, maxPlayers: 4, protocol: PROTOCOL, minProtocol: MIN_PROTOCOL,
      heartbeatMs: HEARTBEAT_MS, reconnectMs: RECONNECT_MS, baseUrl: lanBaseUrl(req), online: !!onlineOrigin() });
  if (req.method === 'POST' && pathname === '/api/rooms') {
    if (!allowRoomAttempt(req)) return fail(res, 429, 'Too many room requests. Try again shortly.');
    if (rooms.size >= ROOM_LIMIT) return fail(res, 503, 'Too many open rooms.');
    const body = await readJson(req);
    const outdated = protocolError(body);
    if (outdated) return fail(res, 426, outdated);
    const maxPlayers = Number(body.maxPlayers);
    if (!Number.isInteger(maxPlayers) || maxPlayers < 2 || maxPlayers > 4)
      return fail(res, 400, 'Choose 2, 3, or 4 players.');
    if (!Object.hasOwn(MAPS, body.map)) return fail(res, 400, 'Unknown map.');
    const mode = body.mode ?? 'duel';
    if (!MODES.has(mode)) return fail(res, 400, 'Unknown game mode.');
    const rule = body.rule ?? 'classic';
    if (!['classic', 'decay'].includes(rule)) return fail(res, 400, 'Unknown wall rule.');
    const host = createPlayer('P1', body.name);
    const room = { code: code(), map: body.map, mode, rule, maxPlayers, hostId: host.id,
      players: [host], phase: 'lobby', round: null, roundNumber: 0,
      result: null, crashes: [], effects: [], effectSeq: 0, touched: Date.now(), lastBroadcast: 0,
      lastSent: Date.now(), released: new Set(), lastTickAt: performance.now(), accumulator: 0 };
    rooms.set(room.code, room);
    seatCookie(res, host);
    return json(res, 201, { code: room.code, playerId: host.id });
  }
  const route = /^\/api\/rooms\/([A-Z0-9]{6})(?:\/(join|stream|action|leave))?$/.exec(pathname);
  if (!route) return fail(res, 404, 'Unknown room action.');
  if (route[2] === 'join' && req.method === 'POST' && !allowRoomAttempt(req))
    return fail(res, 429, 'Too many room requests. Try again shortly.');
  const room = rooms.get(route[1]);
  if (!room) return fail(res, 404, route[2] === 'join' ? 'Room not found.' : missingRoom(route[1]));
  if (route[2] === 'join' && req.method === 'POST') {
    const body = await readJson(req);
    const outdated = protocolError(body);
    if (outdated) return fail(res, 426, outdated);
    if (room.phase !== 'lobby') return fail(res, 409, 'This match has already started.');
    if (room.players.length >= room.maxPlayers) return fail(res, 409, 'Room is full.');
    // A lobby departure leaves a gap, so take the lowest free seat rather than the count.
    let seat = 1;
    while (room.players.some(p => p.id === 'P' + seat)) seat++;
    const player = createPlayer('P' + seat, body.name);
    room.players.push(player);
    broadcast(room);
    seatCookie(res, player);
    return json(res, 201, { code: room.code, playerId: player.id });
  }
  const auth = authorizedRoom(route[1], req, res);
  if (!auth) return;
  const { player } = auth;
  if (route[2] === 'stream' && req.method === 'GET') {
    if (player.stream && player.stream !== res) player.stream.end();
    res.writeHead(200, { 'Content-Type': 'application/x-ndjson; charset=utf-8',
      'Cache-Control': 'no-cache, no-transform', 'Connection': 'keep-alive',
      'X-Content-Type-Options': 'nosniff' });
    player.stream = res;
    player.connected = true;
    res.on('close', () => { if (player.stream === res) handleDisconnect(room, player); });
    broadcast(room);
    return;
  }
  if (route[2] === 'action' && req.method === 'POST') {
    const body = await readJson(req);
    if (body.type === 'mode') {
      if (player.id !== room.hostId) return fail(res, 403, 'Only the host selects the mode.');
      if (room.phase !== 'lobby') return fail(res, 409, 'Choose a mode before starting.');
      if (!MODES.has(body.mode)) return fail(res, 400, 'Unknown game mode.');
      room.mode = body.mode;
      broadcast(room);
      return json(res, 200, { ok: true });
    }
    if (body.type === 'rule') {
      if (player.id !== room.hostId) return fail(res, 403, 'Only the host selects the wall rule.');
      if (room.phase !== 'lobby' || room.mode !== 'arena')
        return fail(res, 409, 'Choose an Arena wall rule before starting.');
      if (!['classic', 'decay'].includes(body.rule)) return fail(res, 400, 'Unknown wall rule.');
      room.rule = body.rule;
      broadcast(room);
      return json(res, 200, { ok: true });
    }
    if (body.type === 'map') {
      if (player.id !== room.hostId) return fail(res, 403, 'Only the host selects the map.');
      if (room.phase !== 'lobby') return fail(res, 409, 'Choose a map before starting.');
      if (!Object.hasOwn(MAPS, body.map)) return fail(res, 400, 'Unknown map.');
      room.map = body.map;
      broadcast(room);
      return json(res, 200, { ok: true });
    }
    if (body.type === 'pause') {
      if (player.id !== room.hostId) return fail(res, 403, 'Only the host can pause.');
      if (room.phase === 'paused') {
        if (!room.players.every(p => p.connected))
          return fail(res, 409, 'Wait for every player to reconnect.');
        room.phase = room.beforePause;
        if (room.phase === 'countdown') room.deadline += Date.now() - room.pauseStarted;
        room.lastTickAt = performance.now();
        room.accumulator = 0;
      } else if (room.phase === 'running' || room.phase === 'countdown') {
        room.beforePause = room.phase;
        room.phase = 'paused';
        room.pauseStarted = Date.now();
        if (room.round) room.round.riders.forEach(r => {
          r.fireHeld = false;
          r.throttleHeld = false;
          if (r.input) r.input = { x: 0, z: 0 };
        });
      } else return fail(res, 409, 'No active round to pause.');
      broadcast(room);
      return json(res, 200, { ok: true });
    }
    if (body.type === 'start') {
      if (player.id !== room.hostId) return fail(res, 403, 'Only the host starts a round.');
      if (!['lobby', 'round-result', 'match-result'].includes(room.phase))
        return fail(res, 409, 'Round already in progress.');
      if (room.players.length !== room.maxPlayers || !room.players.every(p => p.connected))
        return fail(res, 409, 'Wait for every player to connect.');
      if (room.phase === 'match-result') {
        room.roundNumber = 0;
        room.players.forEach(p => { p.wins = 0; });
      }
      nextRound(room);
      return json(res, 200, { ok: true });
    }
    if (room.round && ['countdown', 'paused', 'connection-wait'].includes(room.phase) &&
        body.type === 'auto-drive' && typeof body.enabled === 'boolean') {
      setCycleAutoDrive(room.round, player.id, body.enabled);
      return json(res, 200, { ok: true });
    }
    if (room.phase !== 'running') return fail(res, 409, 'Wait for the round to begin.');
    const second = Math.floor(Date.now() / 1000);
    if (player.lastInputAt !== second) { player.lastInputAt = second; player.inputCount = 0; }
    if (++player.inputCount > 40) return fail(res, 429, 'Too many inputs.');
    if (body.type === 'direction' && Number.isInteger(body.direction) &&
        body.direction >= 0 && body.direction <= 3) {
      if (room.mode === 'shift') queueShiftDirection(room.round, player.id, body.direction);
      else queueDirection(room.round, player.id, body.direction);
    } else if (body.type === 'turn' && (body.sign === -1 || body.sign === 1)) {
      if (room.mode === 'shift') queueShiftTurn(room.round, player.id, body.sign);
      else queueTurn(room.round, player.id, body.sign);
    } else if (body.type === 'auto-drive' && typeof body.enabled === 'boolean') {
      setCycleAutoDrive(room.round, player.id, body.enabled);
    } else if (body.type === 'throttle' && typeof body.held === 'boolean') {
      setCycleThrottle(room.round, player.id, body.held);
    } else if (body.type === 'fire' && typeof body.held === 'boolean') {
      if (room.mode === 'shift') setShiftFire(room.round, player.id, body.held);
      else setCycleFire(room.round, player.id, body.held);
    } else if (room.mode === 'shift' && body.type === 'move' &&
        Number.isFinite(body.x) && Number.isFinite(body.z) &&
        Math.abs(body.x) <= 1 && Math.abs(body.z) <= 1) {
      setShiftMove(room.round, player.id, body.x, body.z);
    } else if (body.type === 'flight' && ['jet', 'sail'].includes(body.kind)) {
      requestFlight(room.round, player.id, body.kind);
    } else if (room.mode === 'shift' && body.type === 'form') {
      if (!requestShiftForm(room.round, player.id))
        return fail(res, 409, room.round.events.at(-1)?.reason || 'Form unavailable.');
      broadcast(room);
    } else if (room.mode === 'shift' && body.type === 'disc') {
      if (!throwShiftDisc(room.round, player.id))
        return fail(res, 409, 'Dismount or wait for your disc to return.');
      broadcast(room);
    } else return fail(res, 400, 'Unknown input.');
    return json(res, 200, { ok: true });
  }
  if (route[2] === 'leave' && req.method === 'POST') {
    if (player.id === room.hostId) closeRoom(room, 'The host closed the room.');
    else if (room.phase === 'lobby') {
      if (player.stream) player.stream.end();
      room.players = room.players.filter(p => p !== player);
      broadcast(room);
    } else {
      // Leaving on purpose forfeits at once; holding the heat is for riders who lost connection.
      releaseSeat(room, player, 'left');
      if (room.phase === 'connection-wait' && room.players.every(p => p.connected)) endWait(room, performance.now());
      if (!settleRound(room)) broadcast(room);
    }
    return json(res, 200, { ok: true });
  }
  return fail(res, 405, 'Method unavailable.');
}
async function handle(req, res) {
  if (req.method === 'GET' && req.url === '/healthz')
    return json(res, 200, { healthy: true });
  if (!hostAllowed(req)) return fail(res, 403, 'Use the local LAN address.');
  try {
    const url = new URL(req.url, 'http://' + req.headers.host);
    const pathname = decodeURIComponent(url.pathname);
    if (pathname.startsWith('/api/')) return await handleApi(req, res, pathname);
    if (req.method !== 'GET' && req.method !== 'HEAD')
      return fail(res, 405, 'Method unavailable.');
    const file = pathname === '/' ? 'index.html' : pathname.slice(1);
    if (!STATIC.has(file)) return fail(res, 404, 'File not found.');
    const body = await readFile(path.join(DIST, file));
    res.writeHead(200, { 'Content-Type': TYPES[path.extname(file)],
      'Content-Length': body.length, 'Cache-Control': 'no-store',
      'X-Content-Type-Options': 'nosniff' });
    res.end(req.method === 'HEAD' ? undefined : body);
  } catch (error) {
    fail(res, error.message === 'Request is too large.' ? 413 : 400, error.message);
  }
}
export function createLanServer() {
  const server = http.createServer(handle);
  const clock = setInterval(() => {
    const now = performance.now();
    for (const room of rooms.values()) {
      if (Date.now() - room.touched > ROOM_TTL && !room.players.some(p => p.connected)) {
        closeRoom(room, 'This room closed after an hour without play.');
        continue;
      }
      tickRoom(room, now);
      if (rooms.has(room.code) && Date.now() - room.lastSent >= HEARTBEAT_MS) heartbeat(room);
    }
    for (const [id, closed] of closedRooms)
      if (Date.now() - closed.at > CLOSED_TTL) closedRooms.delete(id);
  }, 8);
  clock.unref();
  server.on('close', () => clearInterval(clock));
  return server;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const port = Number(process.env.PORT || 8000);
  if (!Number.isInteger(port) || port < 1 || port > 65535)
    throw new Error('PORT must be 1 to 65535.');
  const server = createLanServer();
  server.listen(port, '0.0.0.0', () => {
    console.log('TRON LAN room host is running on port ' + port + '.');
    console.log('On this computer: http://localhost:' + port + '/lan.html');
    for (const group of Object.values(os.networkInterfaces()))
      for (const address of group || [])
        if (address.family === 'IPv4' && !address.internal)
          console.log('On your LAN: http://' + address.address + ':' + port + '/lan.html');
  });
}