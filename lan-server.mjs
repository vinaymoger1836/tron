import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { readFile } from 'node:fs/promises';
import { randomBytes, randomInt } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { createLanRound, drainEvents, queueTurn, setCycleFire, stepRound, STEP } from './dist/simulation.js';
import { MAPS } from './dist/maps.js';
import { createShiftRound, queueShiftTurn, requestShiftForm, setShiftFire,
  setShiftMove, stepShiftRound, throwShiftDisc } from './dist/grid-shift.js';

const DIST = path.join(path.dirname(fileURLToPath(import.meta.url)), 'dist');
const STATIC = new Set(['index.html', 'lan.html', 'lan.js', 'peer.html', 'peer.js', 'game.js', 'simulation.js',
  'maps.js', 'match.js', 'arena.js', 'operations.js', 'grid-shift.js']);
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8' };
const ROOM_TTL = 60 * 60 * 1000;
const RECONNECT_MS = 15000;
const SNAPSHOT_MS = 50;
const ROOM_LIMIT = 24;
const CODE_CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
const DIGITS = '0123456789';
const MODES = new Set(['duel', 'arena', 'shift']);
const rooms = new Map();
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
  } while (rooms.has(value));
  return value;
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
function playerFor(room, req) {
  const provided = String(req.headers.cookie || '').split('; ')
    .find(part => part.startsWith('tron_lan_session='))
    ?.slice('tron_lan_session='.length);
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
      rule: round.rule, trailLifetime: Number.isFinite(round.trailLifetime) ? round.trailLifetime : null,
      riders: round.riders.map(r => ({ id: r.id, x: r.x, z: r.z, dir: r.dir,
        color: r.color, style: r.style, alive: r.alive, ammo: r.ammo,
        form: r.form || 'cycle', hp: r.hp ?? null,
        formReadyAt: r.formReadyAt ?? null, discReadyAt: r.discReadyAt ?? null,
        recoveryUntil: r.recoveryUntil ?? 0, formChangedAt: r.formChangedAt ?? -10 })),
      segments: round.segments.map(s => ({ id: s.id, ownerId: s.ownerId,
        x1: s.x1, z1: s.z1, x2: s.x2, z2: s.z2, active: s.active,
        closedAt: s.closedAt ?? -1 })),
      bullets: (round.shift?.bullets || round.bullets)
        .map(b => ({ ownerId: b.ownerId, x: b.x, z: b.z })),
      shift: round.shift ? { timeLimit: round.shift.timeLimit,
        discs: round.shift.discs.map(d => ({ ownerId: d.ownerId, x: d.x, z: d.z,
          returning: d.returning })) } : null,
      crashes: room.crashes,
    } : null,
  };
}
function send(room, player) {
  if (!player.stream || player.stream.destroyed) return;
  try { player.stream.write(JSON.stringify(snapshot(room, player.id)) + '\n'); }
  catch { player.stream = null; player.connected = false; }
}
function broadcast(room) {
  room.touched = Date.now();
  for (const player of room.players) send(room, player);
}
function nextRound(room) {
  room.roundNumber++;
  const options = { seed: randomInt(0x100000000),
    playerIds: room.players.map(p => p.id), roundNumber: room.roundNumber, map: room.map,
    rule: room.mode === 'arena' ? room.rule : 'classic' };
  room.round = room.mode === 'shift' ? createShiftRound(options)
    : createLanRound({ ...options, map: room.map });
  room.crashes = [];
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
  if (room.phase === 'running' || room.phase === 'countdown' || room.phase === 'paused') {
    if (room.round) room.round.riders.forEach(r => {
      r.fireHeld = false;
      if (r.input) r.input = { x: 0, z: 0 };
    });
    room.beforeWait = room.phase;
    room.deadlineBeforeWait = room.deadline;
    room.phase = 'connection-wait';
    room.deadline = Date.now() + RECONNECT_MS;
    room.waitStarted = Date.now();
  }
  broadcast(room);
}
function tickRoom(room, now) {
  if (room.phase === 'connection-wait') {
    if (room.players.every(p => p.connected)) {
      room.phase = room.beforeWait;
      room.deadline = room.deadlineBeforeWait;
      if (room.phase === 'countdown') room.deadline += Date.now() - room.waitStarted;
      room.lastTickAt = now;
      room.accumulator = 0;
      broadcast(room);
    } else if (Date.now() >= room.deadline) {
      if (!room.round) { room.phase = 'lobby'; broadcast(room); return; }
      for (const p of room.players.filter(p => !p.connected)) {
        const rider = room.round.riders.find(r => r.id === p.id);
        if (rider?.alive) {
          rider.alive = false;
          if (rider.segment) rider.segment.active = false;
          rider.fireHeld = false;
          room.crashes.push({ riderId: p.id, reason: 'disconnect' });
        }
      }
      const alive = room.round.riders.filter(r => r.alive);
      room.round.outcome = alive.length <= 1
        ? { kind: alive.length ? 'won' : 'draw', winnerId: alive[0]?.id || null } : null;
      room.phase = 'running';
      room.lastTickAt = now;
      room.accumulator = 0;
      if (room.round.outcome) finishRound(room);
      else broadcast(room);
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
    for (const event of drainEvents(room.round))
      if (event.type === 'crash') room.crashes.push({
        riderId: event.riderId, reason: event.reason, by: event.by || null });
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
  if (!room) { fail(res, 404, 'Room not found.'); return null; }
  const player = playerFor(room, req);
  if (!player) { fail(res, onlineOrigin() ? 404 : 403,
    onlineOrigin() ? 'Room not found or seat unavailable.' :
      'This seat is unavailable. Rejoin the room.'); return null; }
  return { room, player };
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
    return json(res, 200, { available: true, maxPlayers: 4,
      baseUrl: lanBaseUrl(req), online: !!onlineOrigin() });
  if (req.method === 'POST' && pathname === '/api/rooms') {
    if (!allowRoomAttempt(req)) return fail(res, 429, 'Too many room requests. Try again shortly.');
    if (rooms.size >= ROOM_LIMIT) return fail(res, 503, 'Too many open rooms.');
    const body = await readJson(req);
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
      result: null, crashes: [], touched: Date.now(), lastBroadcast: 0,
      lastTickAt: performance.now(), accumulator: 0 };
    rooms.set(room.code, room);
    seatCookie(res, host);
    return json(res, 201, { code: room.code, playerId: host.id });
  }
  const route = /^\/api\/rooms\/([A-Z0-9]{6})(?:\/(join|stream|action|leave))?$/.exec(pathname);
  if (!route) return fail(res, 404, 'Unknown room action.');
  if (route[2] === 'join' && req.method === 'POST' && !allowRoomAttempt(req))
    return fail(res, 429, 'Too many room requests. Try again shortly.');
  const room = rooms.get(route[1]);
  if (!room) return fail(res, 404, 'Room not found.');
  if (route[2] === 'join' && req.method === 'POST') {
    if (room.phase !== 'lobby') return fail(res, 409, 'This match has already started.');
    if (room.players.length >= room.maxPlayers) return fail(res, 409, 'Room is full.');
    const body = await readJson(req);
    const player = createPlayer('P' + (room.players.length + 1), body.name);
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
    if (room.phase !== 'running') return fail(res, 409, 'Wait for the round to begin.');
    const second = Math.floor(Date.now() / 1000);
    if (player.lastInputAt !== second) { player.lastInputAt = second; player.inputCount = 0; }
    if (++player.inputCount > 40) return fail(res, 429, 'Too many inputs.');
    if (body.type === 'turn' && (body.sign === -1 || body.sign === 1)) {
      if (room.mode === 'shift') queueShiftTurn(room.round, player.id, body.sign);
      else queueTurn(room.round, player.id, body.sign);
    } else if (body.type === 'fire' && typeof body.held === 'boolean') {
      if (room.mode === 'shift') setShiftFire(room.round, player.id, body.held);
      else setCycleFire(room.round, player.id, body.held);
    } else if (room.mode === 'shift' && body.type === 'move' &&
        Number.isFinite(body.x) && Number.isFinite(body.z) &&
        Math.abs(body.x) <= 1 && Math.abs(body.z) <= 1) {
      setShiftMove(room.round, player.id, body.x, body.z);
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
    if (player.stream) player.stream.end();
    if (player.id === room.hostId) {
      for (const p of room.players) if (p.stream) p.stream.end();
      rooms.delete(room.code);
    } else if (room.phase === 'lobby') {
      room.players = room.players.filter(p => p !== player);
      broadcast(room);
    } else handleDisconnect(room, player);
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
        rooms.delete(room.code);
        continue;
      }
      tickRoom(room, now);
    }
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