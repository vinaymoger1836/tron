import { getMap } from './maps.js';
import { createLanRound, drainEvents, queueTurn, setCycleAutoDrive, setCycleThrottle, setCycleFire, stepRound, STEP } from './simulation.js';

const $ = selector => document.querySelector(selector);
const canvas = $('#arena');
const ctx = canvas.getContext('2d');
const panel = $('#panel');
const live = $('#status-live');
const CODE_CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
const AUTO_DRIVE_KEY = 'tron-grid-run-auto-drive';
let autoDrive = true;
try { autoDrive = localStorage.getItem(AUTO_DRIVE_KEY) !== 'false'; } catch {}
const state = {
  role: null, info: null, selfId: null, view: null, peer: null, channel: null,
  inviteNonce: null, peers: new Map(), room: null, timer: null, heldFire: false, heldThrottle: false,
  lastPhase: null, lastRound: 0,
};
function say(message) { if (live.textContent !== message) live.textContent = message; }
function notice(message, error = false, target = '#room-notice') {
  const el = $(target); el.textContent = message; el.classList.toggle('error', error);
}
function randomCode(size = 6) {
  const bytes = crypto.getRandomValues(new Uint8Array(size));
  return [...bytes].map(byte => CODE_CHARS[byte % CODE_CHARS.length]).join('');
}
function validName(value) {
  const name = String(value || '').trim();
  if (!name || name.length > 18 || /[\u0000-\u001f\u007f]/.test(name))
    throw new Error('Use a rider name of 1 to 18 characters.');
  return name;
}
function encode(value) {
  const bytes = new TextEncoder().encode(JSON.stringify(value));
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}
function decode(value) {
  const text = String(value || '').trim();
  if (!/^[A-Za-z0-9_-]{8,16000}$/.test(text)) throw new Error('Paste the complete connection code.');
  const binary = atob(text.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - text.length % 4) % 4));
  return JSON.parse(new TextDecoder('utf-8', { fatal: true })
    .decode(Uint8Array.from(binary, char => char.charCodeAt(0))));
}
function parseRoomLink(value) {
  let url;
  try { url = new URL(value, location.href); } catch { throw new Error('Paste a valid room link.'); }
  if (url.origin !== location.origin || url.pathname !== location.pathname)
    throw new Error('Open the room link on this same game site.');
  const payload = decode(new URLSearchParams(url.hash.slice(1)).get('room'));
  if (payload.v !== 1 || !/^[A-Z2-9]{6}$/.test(payload.code) ||
      !['classic', 'solar', 'vault'].includes(payload.map) ||
      !Number.isInteger(payload.maxPlayers) || payload.maxPlayers < 2 ||
      payload.maxPlayers > 4) throw new Error('This room link is invalid.');
  return { code: payload.code, map: payload.map, maxPlayers: payload.maxPlayers,
    hostName: validName(payload.hostName) };
}
function roomLink(info) {
  return location.origin + location.pathname + '#room=' + encode({
    v: 1, code: info.code, map: info.map, maxPlayers: info.maxPlayers,
    hostName: info.hostName,
  });
}
async function copyField(selector) {
  const field = $(selector);
  try {
    if (navigator.clipboard?.writeText) await navigator.clipboard.writeText(field.value);
    else { field.select(); if (!document.execCommand('copy')) throw new Error('Copy unavailable'); }
    notice('Code copied. Send it privately to your friend.');
  } catch { field.select(); notice('Select and copy the highlighted code.'); }
}
async function gather(pc, description) {
  await pc.setLocalDescription(description);
  if (pc.iceGatheringState !== 'complete') await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => { pc.removeEventListener('icegatheringstatechange', changed);
      reject(new Error('Local connection details timed out. Try again.')); }, 15000);
    function changed() {
      if (pc.iceGatheringState !== 'complete') return;
      clearTimeout(timeout); pc.removeEventListener('icegatheringstatechange', changed); resolve();
    }
    pc.addEventListener('icegatheringstatechange', changed);
  });
  return pc.localDescription.sdp;
}
function newPeer() {
  if (!window.RTCPeerConnection) throw new Error('This browser does not support peer connections.');
  return new RTCPeerConnection({ iceServers: [] });
}
function reset(message = '') {
  clearInterval(state.timer);
  state.timer = null;
  for (const peer of state.peers.values()) peer.pc.close();
  state.peers.clear();
  state.channel?.close();
  state.peer?.close();
  state.role = null; state.info = null; state.selfId = null; state.view = null;
  state.peer = null; state.channel = null; state.room = null;
  state.inviteNonce = null; state.heldFire = false; state.heldThrottle = false; state.lastPhase = null;
  $('#setup').hidden = false; $('#room').hidden = true; panel.hidden = false;
  $('#touch').hidden = true; $('#countdown').hidden = true; $('#pause').hidden = true;
  $('#scorebar').replaceChildren(); $('#room-code-top').textContent = 'NO ROOM';
  $('#response-block').hidden = true; $('#response-output').value = '';
  $('#invite-input').value = '';
  $('#response-input').value = ''; $('#invite-output').value = '';
  history.replaceState(null, '', location.pathname);
  if (message) notice(message, true, '#setup-notice');
}
function enterRoom() {
  $('#setup').hidden = true; $('#room').hidden = false;
  $('#room-code-top').textContent = 'ROOM ' + state.info.code;
  $('#host-connect').hidden = state.role !== 'host';
  $('#guest-connect').hidden = state.role !== 'guest';
  $('#share-link').value = roomLink(state.info);
  history.replaceState(null, '', roomLink(state.info));
  panel.hidden = false;
}
function createRoom() {
  try {
    state.info = { code: randomCode(), maxPlayers: Number($('#players').value),
      map: $('#map').value, hostName: validName($('#name').value) };
    state.role = 'host'; state.selfId = 'P1';
    state.room = {
      phase: 'lobby', players: [{ id: 'P1', name: state.info.hostName,
        wins: 0, connected: true }], round: null, roundNumber: 0, result: null,
      crashes: [], deadline: 0, accumulator: 0, lastAt: performance.now(),
      lastBroadcast: 0, beforePause: null,
    };
    enterRoom();
    state.timer = setInterval(() => tickRoom(performance.now()), 8);
    broadcast();
    say('Room created. Share the room link with your friends.');
  } catch (error) { notice(error.message, true, '#setup-notice'); }
}
async function joinRoom() {
  $('#join').disabled = true;
  let pc;
  try {
    state.info = parseRoomLink($('#join-link').value);
    state.role = 'guest'; state.selfId = null;
    state.inviteNonce = randomCode(12);
    pc = newPeer();
    state.peer = pc;
    const channel = pc.createDataChannel('grid-run', { ordered: true });
    state.channel = channel;
    channel.onopen = () => { $('#guest-connect').hidden = true; notice('Connected to the host.'); say('Connected to the room.'); };
    channel.onmessage = event => receiveGuestMessage(event.data);
    channel.onclose = () => {
      if (state.role === 'guest') {
        state.view = null;
        panel.hidden = false;
        $('#touch').hidden = true;
        $('#countdown').hidden = true;
        $('#guest-connect').hidden = true;
        $('#room-phase').textContent = 'CONNECTION LOST';
        $('#room-message').textContent = 'Leave the room, then open the room link to exchange new codes.';
        notice('Connection lost. Exchange new codes with the host.', true);
        say('Connection to the host was lost.');
      }
    };
    pc.onconnectionstatechange = () => {
      if (pc.connectionState === 'failed') channel.close();
    };
    const sdp = await gather(pc, await pc.createOffer());
    $('#invite-output').value = encode({ v: 1, kind: 'invite',
      code: state.info.code, nonce: state.inviteNonce,
      name: validName($('#name').value), sdp });
    enterRoom();
    $('#room-phase').textContent = 'EXCHANGE CODES';
    $('#room-title').textContent = 'CONNECT TO HOST';
    $('#room-message').textContent = 'Send your invite code to the host. Paste their response below.';
    $('#roster').replaceChildren();
    $('#start').hidden = true;
    say('Invite ready. Send it to your host.');
  } catch (error) {
    pc?.close();
    reset(error.message);
  } finally { $('#join').disabled = false; }
}
function receiveGuestMessage(raw) {
  let message;
  try { message = JSON.parse(raw); } catch { return; }
  if (message?.type !== 'snapshot' || message.view?.code !== state.info?.code) return;
  state.selfId = message.view.selfId;
  receiveView(message.view);
}
async function applyResponse() {
  $('#apply-response').disabled = true;
  try {
    if (!state.peer || state.role !== 'guest') throw new Error('Create a fresh invite first.');
    const data = decode($('#response-input').value);
    if (data.v !== 1 || data.kind !== 'response' || data.code !== state.info.code ||
        data.nonce !== state.inviteNonce || !/^P[2-4]$/.test(data.id) ||
        typeof data.sdp !== 'string' || data.sdp.length > 12000)
      throw new Error('This response does not match your invite.');
    await state.peer.setRemoteDescription({ type: 'answer', sdp: data.sdp });
    state.selfId = data.id;
    notice('Connecting to the host...');
    say('Connecting to the host.');
  } catch (error) { notice(error.message, true); }
  finally { $('#apply-response').disabled = false; }
}
async function acceptInvite() {
  $('#accept-invite').disabled = true;
  $('#response-output').value = '';
  $('#response-block').hidden = true;
  let pc, player, nonce;
  try {
    if (state.role !== 'host' || state.room.phase !== 'lobby')
      throw new Error('New riders can join before a match starts.');
    const data = decode($('#invite-input').value);
    nonce = data.nonce;
    if (data.v !== 1 || data.kind !== 'invite' || data.code !== state.info.code ||
        !/^[A-Z2-9]{12}$/.test(data.nonce) ||
        typeof data.sdp !== 'string' || data.sdp.length > 12000 ||
        state.peers.has(data.nonce)) throw new Error('This invite is invalid or already used.');
    if (state.room.players.length >= state.info.maxPlayers) throw new Error('Room is full.');
    const id = ['P2', 'P3', 'P4'].find(seat => !state.room.players.some(p => p.id === seat));
    player = { id, name: validName(data.name), wins: 0, connected: false };
    pc = newPeer();
    const peer = { pc, player, channel: null, connected: false };
    state.peers.set(data.nonce, peer);
    state.room.players.push(player);
    pc.ondatachannel = event => {
      const channel = event.channel;
      peer.channel = channel;
      channel.onopen = () => {
        peer.connected = true; player.connected = true;
        clearTimeout(peer.expiry);
        broadcast();
        say(player.name + ' connected.');
      };
      channel.onmessage = event => receiveHostMessage(peer, event.data);
      channel.onclose = () => dropPeer(data.nonce);
    };
    pc.onconnectionstatechange = () => {
      if (pc.connectionState === 'failed') dropPeer(data.nonce);
    };
    await pc.setRemoteDescription({ type: 'offer', sdp: data.sdp });
    const sdp = await gather(pc, await pc.createAnswer());
    $('#response-output').value = encode({ v: 1, kind: 'response',
      code: state.info.code, nonce: data.nonce, id, sdp });
    $('#response-block').hidden = false;
    $('#invite-input').value = '';
    peer.expiry = setTimeout(() => {
      if (!peer.connected) dropPeer(data.nonce);
    }, 120000);
    broadcast();
    notice('Response ready. Send it back to ' + player.name + '.');
  } catch (error) {
    if (player && state.room) state.room.players =
      state.room.players.filter(p => p !== player);
    if (nonce) state.peers.delete(nonce);
    if (pc) pc.close();
    notice(error.message, true);
  } finally { $('#accept-invite').disabled = false; }
}
function dropPeer(nonce) {
  const peer = state.peers.get(nonce);
  if (!peer) return;
  state.peers.delete(nonce);
  clearTimeout(peer.expiry);
  peer.pc.close();
  if (!state.room) return;
  state.room.players = state.room.players.filter(p => p !== peer.player);
  if (state.room.phase !== 'lobby') {
    state.room.phase = 'lobby'; state.room.round = null;
    state.room.result = null; state.room.roundNumber = 0;
    for (const p of state.room.players) p.wins = 0;
    notice(peer.player.name + ' disconnected. The match returned to the lobby.', true);
  } else notice(peer.player.name + ' disconnected. Invite them again.', true);
  broadcast();
}
function receiveHostMessage(peer, raw) {
  let message;
  try { message = JSON.parse(raw); } catch { return; }
  if (!peer.connected || state.room?.phase !== 'running' || message?.type !== 'input') return;
  const now = Math.floor(performance.now() / 1000);
  if (peer.inputSecond !== now) { peer.inputSecond = now; peer.inputCount = 0; }
  if (++peer.inputCount > 40) return;
  if (message.action === 'turn' && (message.sign === -1 || message.sign === 1))
    queueTurn(state.room.round, peer.player.id, message.sign);
  if (message.action === 'auto-drive' && typeof message.enabled === 'boolean')
    setCycleAutoDrive(state.room.round, peer.player.id, message.enabled);
  if (message.action === 'throttle' && typeof message.held === 'boolean')
    setCycleThrottle(state.room.round, peer.player.id, message.held);
  if (message.action === 'fire' && typeof message.held === 'boolean')
    setCycleFire(state.room.round, peer.player.id, message.held);
}
function snapshot(selfId) {
  const room = state.room, round = room.round;
  return {
    code: state.info.code, phase: room.phase, selfId, hostId: 'P1',
    maxPlayers: state.info.maxPlayers, map: state.info.map,
    roundNumber: room.roundNumber,
    countdownMs: room.phase === 'countdown' ? Math.max(0, room.deadline - performance.now()) : 0,
    result: room.result, players: room.players.map(p => ({
      id: p.id, name: p.name, connected: p.connected, wins: p.wins,
    })),
    round: round ? {
      tick: round.tick, elapsed: round.elapsed, limit: round.limit, map: round.map,
      pickups: round.pickups,
      riders: round.riders.map(r => ({ id: r.id, x: r.x, z: r.z, dir: r.dir,
        color: r.color, style: r.style, alive: r.alive, ammo: r.ammo,
        rocketReady: r.rocketReady, nitroUntil: r.nitroUntil,
        autoDrive: r.autoDrive, throttleHeld: r.throttleHeld })),
      segments: round.segments.map(s => ({ id: s.id, ownerId: s.ownerId,
        x1: s.x1, z1: s.z1, x2: s.x2, z2: s.z2, active: s.active })),
      bullets: round.bullets.map(b => ({ ownerId: b.ownerId, x: b.x, z: b.z, rocket: b.rocket })),
      crashes: room.crashes,
    } : null,
  };
}
function broadcast() {
  if (state.role !== 'host' || !state.room) return;
  receiveView(snapshot('P1'));
  for (const peer of state.peers.values()) {
    if (!peer.connected || peer.channel?.readyState !== 'open' ||
        peer.channel.bufferedAmount > 256000) continue;
    peer.channel.send(JSON.stringify({ type: 'snapshot', view: snapshot(peer.player.id) }));
  }
}
function nextRound() {
  const room = state.room;
  room.roundNumber++;
  room.round = createLanRound({ seed: crypto.getRandomValues(new Uint32Array(1))[0],
    playerIds: room.players.map(p => p.id), map: state.info.map,
    roundNumber: room.roundNumber });
  room.phase = 'countdown'; room.deadline = performance.now() + 3000;
  room.accumulator = 0; room.lastAt = performance.now();
  room.result = null; room.crashes = []; broadcast();
}
function finishRound() {
  const room = state.room;
  const winnerId = room.round.outcome?.winnerId || null;
  const winner = room.players.find(p => p.id === winnerId);
  if (winner) winner.wins++;
  room.result = { winnerId, winnerName: winner?.name || null,
    crashes: room.crashes, matchWinnerId: winner?.wins >= 3 ? winnerId : null };
  room.phase = room.result.matchWinnerId ? 'match-result' : 'round-result';
  room.round.riders.forEach(r => { r.fireHeld = false; r.throttleHeld = false; });
  broadcast();
}
function tickRoom(now) {
  if (state.role !== 'host' || !state.room) return;
  const room = state.room;
  if (room.phase === 'countdown') {
    if (now >= room.deadline) {
      room.phase = 'running'; room.lastAt = now; broadcast();
    } else if (now - room.lastBroadcast >= 50) { room.lastBroadcast = now; broadcast(); }
    return;
  }
  if (room.phase !== 'running') return;
  room.accumulator = Math.min(room.accumulator + Math.max(0, (now - room.lastAt) / 1000), STEP * 16);
  room.lastAt = now;
  let steps = 0;
  while (room.accumulator >= STEP && steps < 16 && !room.round.outcome) {
    stepRound(room.round);
    for (const event of drainEvents(room.round))
      if (event.type === 'crash') room.crashes.push({
        riderId: event.riderId, reason: event.reason, by: event.by || null });
    room.accumulator -= STEP; steps++;
  }
  if (room.round.outcome) finishRound();
  else if (now - room.lastBroadcast >= 50) { room.lastBroadcast = now; broadcast(); }
}
function localAction(value) {
  const room = state.room;
  if (value.type === 'start') {
    if (!['lobby', 'round-result', 'match-result'].includes(room.phase)) return;
    if (room.players.length !== state.info.maxPlayers ||
        !room.players.every(p => p.connected)) return notice('Wait for every rider to connect.', true);
    if (room.phase === 'match-result') {
      room.roundNumber = 0;
      room.players.forEach(p => { p.wins = 0; });
    }
    nextRound();
  } else if (value.type === 'pause') {
    if (room.phase === 'paused') {
      room.phase = room.beforePause;
      if (room.phase === 'countdown') room.deadline = performance.now() + room.remaining;
      room.lastAt = performance.now(); room.accumulator = 0;
    } else if (['running', 'countdown'].includes(room.phase)) {
      room.beforePause = room.phase;
      room.remaining = room.deadline - performance.now();
      room.phase = 'paused';
      if (room.round) room.round.riders.forEach(r => { r.fireHeld = false; r.throttleHeld = false; });
    }
    broadcast();
  } else if (room.phase === 'running') {
    if (value.type === 'turn') queueTurn(room.round, 'P1', value.sign);
    if (value.type === 'auto-drive') setCycleAutoDrive(room.round, 'P1', value.enabled);
    if (value.type === 'throttle') setCycleThrottle(room.round, 'P1', value.held);
    if (value.type === 'fire') setCycleFire(room.round, 'P1', value.held);
  }
}
function action(value) {
  if (!state.role) return;
  if (state.role === 'host') localAction(value);
  else if (state.channel?.readyState === 'open' &&
      ['turn', 'auto-drive', 'throttle', 'fire'].includes(value.type))
    state.channel.send(JSON.stringify({ type: 'input', action: value.type,
      sign: value.sign, enabled: value.enabled, held: value.held }));
}
function turn(sign) {
  if (state.view?.phase === 'running') action({ type: 'turn', sign });
}
function throttle(held) {
  if (state.heldThrottle === held) return;
  state.heldThrottle = held;
  if (state.view?.phase === 'running') action({ type: 'throttle', held });
}
function setAutoDrive(enabled) {
  autoDrive = enabled;
  $('#auto-drive').checked = enabled;
  $('#room-auto-drive').checked = enabled;
  try { localStorage.setItem(AUTO_DRIVE_KEY, String(enabled)); } catch {}
  if (state.view?.phase === 'running') action({ type: 'auto-drive', enabled });
}
function fire(held) {
  if (state.heldFire === held) return;
  state.heldFire = held;
  if (state.view?.phase === 'running') action({ type: 'fire', held });
}
function receiveView(view) {
  state.view = view;
  const key = view.phase + ':' + view.roundNumber;
  if (key !== state.lastPhase) {
    state.lastPhase = key;
    if (view.phase !== 'running') { state.heldFire = false; state.heldThrottle = false; }
    if (view.phase === 'running') {
      state.heldThrottle = false;
      action({ type: 'auto-drive', enabled: autoDrive });
      say('Heat ' + view.roundNumber + ' started.');
      canvas.focus({ preventScroll: true });
    } else if (view.phase === 'round-result' || view.phase === 'match-result') {
      const winner = view.result?.winnerId === view.selfId ? 'You won' :
        view.result?.winnerName ? view.result.winnerName + ' won' : 'The heat was a draw';
      say(winner + (view.phase === 'match-result' ? ' the match.' : ' the heat.'));
    }
  }
  renderUi(view);
}
function renderUi(view) {
  document.body.dataset.phase = view.phase;
  document.body.dataset.round = String(view.roundNumber);
  const active = view.phase === 'running';
  panel.hidden = active || view.phase === 'countdown';
  $('#setup').hidden = true;
  $('#room').hidden = false;
  $('#host-connect').hidden = state.role !== 'host' || view.phase !== 'lobby';
  $('#guest-connect').hidden = true;
  $('#touch').hidden = !active;
  $('#countdown').hidden = view.phase !== 'countdown';
  if (view.phase === 'countdown')
    $('#countdown').textContent = String(Math.max(1, Math.ceil(view.countdownMs / 1000)));
  $('#room-code-top').textContent = 'ROOM ' + view.code;
  $('#pause').hidden = state.role !== 'host' ||
    !['running', 'countdown', 'paused'].includes(view.phase);
  $('#pause').textContent = view.phase === 'paused' ? 'RESUME' : 'PAUSE';
  const labels = {
    lobby: 'WAITING ROOM', countdown: 'LAUNCH', running: 'LIVE HEAT',
    'round-result': 'HEAT COMPLETE', 'match-result': 'MATCH COMPLETE',
    paused: 'HOST PAUSED',
  };
  $('#room-phase').textContent = labels[view.phase] || 'PEER ROOM';
  const selfRider = view.round?.riders.find(rider => rider.id === view.selfId);
  $('#fire').textContent = selfRider?.rocketReady ? 'ROCKET 1' : 'FIRE';
  $('#fire').setAttribute('aria-label', selfRider?.rocketReady
    ? 'Fire one rocket' : 'Hold to fire wall gun');
  $('#room-title').innerHTML = view.phase === 'match-result'
    ? 'GRID <span>SECURED</span>' : view.phase === 'round-result'
      ? 'HEAT <span>ENDED</span>' : 'GRID <span>OPEN</span>';
  const result = view.result;
  let message;
  if (view.phase === 'lobby')
    message = view.players.filter(p => p.connected).length + '/' + view.maxPlayers +
      ' riders connected. ' + (state.role === 'host'
        ? 'Share the room link and exchange codes with each friend.'
        : 'Wait for the host to start.');
  else if (view.phase === 'round-result' || view.phase === 'match-result') {
    const who = result?.winnerId === view.selfId ? 'You' : result?.winnerName || 'No rider';
    message = (result?.winnerId ? who + ' won heat ' + view.roundNumber :
      'Heat ' + view.roundNumber + ' was a draw') + '. ' +
      (view.phase === 'match-result' ? 'First to three complete.' : 'Prepare for the next heat.');
  } else if (view.phase === 'paused')
    message = 'The host paused the Grid. All riders are held.';
  else message = 'Heat ' + view.roundNumber + ' / ' + getMap(view.map).name + '.';
  $('#room-message').textContent = message;
  const list = $('#roster');
  list.replaceChildren();
  for (const player of view.players) {
    const item = document.createElement('li');
    item.classList.toggle('offline', !player.connected);
    const label = document.createElement('span');
    label.textContent = player.name + (player.id === view.selfId ? ' / YOU' : '');
    const record = document.createElement('small');
    record.textContent = player.wins + ' WINS / ' +
      (player.connected ? 'CONNECTED' : 'CONNECTING');
    item.append(label, record); list.append(item);
  }
  const scores = $('#scorebar');
  scores.replaceChildren();
  for (const player of view.players) {
    const item = document.createElement('div');
    item.className = 'score' + (player.id === view.selfId ? ' self' : '') +
      (view.round?.riders.find(r => r.id === player.id)?.alive === false ? ' out' : '');
    item.textContent = player.name + ' ' + player.wins + '/3';
    const rider = view.round?.riders.find(r => r.id === player.id);
    if (rider) {
      const ammo = document.createElement('span');
      ammo.textContent = ' / ' + (rider.alive
        ? (rider.rocketReady ? 'ROCKET 1 + ' : '') + rider.ammo + ' SHOTS' +
          (rider.nitroUntil > view.round.elapsed ? ' / NITRO' : '')
        : 'DEREZZED');
      item.append(ammo);
    }
    scores.append(item);
  }
  $('#start').hidden = state.role !== 'host' ||
    !['lobby', 'round-result', 'match-result'].includes(view.phase);
  $('#start').disabled = view.players.length !== view.maxPlayers ||
    !view.players.every(p => p.connected);
  $('#start').textContent = view.phase === 'lobby' ? 'START MATCH' :
    view.phase === 'match-result' ? 'REMATCH' : 'NEXT HEAT';
}
function interactiveFocus() {
  return document.activeElement?.matches('input,select,button,a,textarea');
}
function rgb(color, alpha = 1) {
  return 'rgba(' + color.map(n => Math.round(n * 255)).join(',') + ',' + alpha + ')';
}
function frame() {
  const dpr = Math.min(devicePixelRatio || 1, 2);
  const width = innerWidth, height = innerHeight;
  if (canvas.width !== Math.round(width * dpr) || canvas.height !== Math.round(height * dpr)) {
    canvas.width = Math.round(width * dpr);
    canvas.height = Math.round(height * dpr);
  }
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, width, height);
  const view = state.view;
  const map = getMap(view?.map || $('#map').value);
  const round = view?.round;
  const limit = map.limit;
  const usableHeight = Math.max(240, height - 150);
  const scale = Math.min((width - 26) / (limit * 2 + 5), usableHeight / (limit * 2 + 5));
  const cx = width / 2, cy = height / 2 + (width < 640 ? 15 : 0);
  const x = value => cx + value * scale;
  const y = value => cy + value * scale;
  const size = limit * 2 * scale;
  ctx.fillStyle = rgb(map.palette.floor);
  ctx.fillRect(x(-limit), y(-limit), size, size);
  ctx.strokeStyle = rgb(map.palette.rail, .18);
  ctx.lineWidth = 1;
  for (let n = -limit; n <= limit; n += 4) {
    ctx.beginPath(); ctx.moveTo(x(n), y(-limit)); ctx.lineTo(x(n), y(limit)); ctx.stroke();
    ctx.beginPath(); ctx.moveTo(x(-limit), y(n)); ctx.lineTo(x(limit), y(n)); ctx.stroke();
  }
  ctx.strokeStyle = rgb(map.palette.rail, .9);
  ctx.lineWidth = 3;
  ctx.strokeRect(x(-limit), y(-limit), size, size);
  if (round?.limit < limit) {
    ctx.strokeStyle = '#ff6d53';
    ctx.lineWidth = 4;
    ctx.strokeRect(x(-round.limit), y(-round.limit), round.limit * 2 * scale, round.limit * 2 * scale);
  }
  ctx.fillStyle = rgb(map.palette.obstacle, .95);
  ctx.strokeStyle = rgb(map.palette.rail, .55);
  for (const obstacle of map.obstacles) {
    ctx.beginPath();
    if (obstacle.shape === 'circle')
      ctx.arc(x(obstacle.x), y(obstacle.z), obstacle.radius * scale, 0, Math.PI * 2);
    else ctx.rect(x(obstacle.minX), y(obstacle.minZ),
      (obstacle.maxX - obstacle.minX) * scale,
      (obstacle.maxZ - obstacle.minZ) * scale);
    ctx.fill(); ctx.stroke();
  }
  if (round) {
    for (const pickup of round.pickups || []) if (pickup.active) {
      ctx.fillStyle = { ammo: '#4ddcff', nitro: '#64ff83', rocket: '#ff9c4d' }[pickup.type];
      ctx.fillRect(x(pickup.x)-7, y(pickup.z)-7, 14, 14);
      ctx.fillStyle = '#06131b';
      ctx.font = 'bold 11px Orbitron, monospace';
      ctx.textAlign = 'center';
      ctx.fillText({ ammo: 'A', nitro: 'N', rocket: 'R' }[pickup.type],
        x(pickup.x), y(pickup.z) + 4);
    }
    const riders = new Map(round.riders.map(r => [r.id, r]));
    for (const segment of round.segments) {
      const rider = riders.get(segment.ownerId);
      if (!rider) continue;
      ctx.strokeStyle = rgb(rider.color, segment.active ? .95 : .72);
      ctx.lineWidth = Math.max(3, scale * .22);
      ctx.setLineDash(rider.style === 'hunter' ? [10, 4] :
        rider.style === 'survivor' ? [2, 4] :
          rider.style === 'trapper' ? [9, 3, 3, 3] : []);
      ctx.beginPath();
      ctx.moveTo(x(segment.x1), y(segment.z1));
      ctx.lineTo(x(segment.x2), y(segment.z2));
      ctx.stroke();
      ctx.setLineDash([]);
    }
    for (const bullet of round.bullets) {
      ctx.fillStyle = bullet.rocket ? '#ff9c4d' : '#ffe6a5';
      ctx.beginPath(); ctx.arc(x(bullet.x), y(bullet.z), bullet.rocket ? 5 : 3, 0, Math.PI * 2); ctx.fill();
    }
    for (const rider of round.riders) {
      if (!rider.alive) continue;
      const px = x(rider.x), py = y(rider.z);
      const direction = rider.dir * Math.PI / 2;
      ctx.save();
      ctx.translate(px, py);
      ctx.rotate(direction);
      ctx.fillStyle = rgb(rider.color);
      ctx.strokeStyle = rider.id === view.selfId ? '#fff' : '#aacbd0';
      ctx.lineWidth = rider.id === view.selfId ? 3 : 1.5;
      ctx.beginPath();
      ctx.moveTo(0, -10); ctx.lineTo(7, 8); ctx.lineTo(0, 4); ctx.lineTo(-7, 8);
      ctx.closePath(); ctx.fill(); ctx.stroke();
      ctx.restore();
      const label = view.players.find(p => p.id === rider.id);
      ctx.font = '700 12px Rajdhani, Arial, sans-serif';
      ctx.textAlign = 'center';
      ctx.fillStyle = '#edfcff';
      ctx.fillText(rider.id === view.selfId ? 'YOU' : label?.name || rider.id, px, py - 15);
    }
  }
  requestAnimationFrame(frame);
}
$('#create').addEventListener('click', createRoom);
$('#join').addEventListener('click', joinRoom);
$('#join-link').addEventListener('keydown', event => {
  if (event.key === 'Enter') joinRoom();
});
$('#accept-invite').addEventListener('click', acceptInvite);
$('#apply-response').addEventListener('click', applyResponse);
$('#copy-link').addEventListener('click', () => copyField('#share-link'));
$('#copy-invite').addEventListener('click', () => copyField('#invite-output'));
$('#copy-response').addEventListener('click', () => copyField('#response-output'));
$('#start').addEventListener('click', () => action({ type: 'start' }));
$('#pause').addEventListener('click', () => action({ type: 'pause' }));
$('#leave').addEventListener('click', () => reset('You left the room.'));
for (const [id, sign] of [['left', -1], ['right', 1]])
  $('#' + id).addEventListener('pointerdown', event => {
    event.preventDefault(); turn(sign);
  });
const forwardButton = $('#up');
forwardButton.addEventListener('pointerdown', event => {
  event.preventDefault();
  forwardButton.setPointerCapture(event.pointerId);
  throttle(true);
});
for (const type of ['pointerup', 'pointercancel', 'lostpointercapture'])
  forwardButton.addEventListener(type, () => throttle(false));
$('#fire').addEventListener('pointerdown', event => {
  event.preventDefault();
  event.currentTarget.setPointerCapture(event.pointerId);
  fire(true);
});
for (const type of ['pointerup', 'pointercancel', 'lostpointercapture'])
  $('#fire').addEventListener(type, () => fire(false));
document.addEventListener('keydown', event => {
  if (interactiveFocus()) return;
  if (['w', 'W', 'ArrowUp'].includes(event.key)) {
    event.preventDefault(); throttle(true);
  } else if (!event.repeat && ['a', 'A', 'ArrowLeft'].includes(event.key)) {
    event.preventDefault(); turn(-1);
  } else if (!event.repeat && ['d', 'D', 'ArrowRight'].includes(event.key)) {
    event.preventDefault(); turn(1);
  } else if (['e', 'E', ' '].includes(event.key)) {
    event.preventDefault(); fire(true);
  } else if (!event.repeat && ['p', 'P'].includes(event.key) &&
      state.role === 'host') {
    event.preventDefault(); action({ type: 'pause' });
  }
});
document.addEventListener('keyup', event => {
  if (['w', 'W', 'ArrowUp'].includes(event.key)) throttle(false);
  if (['e', 'E', ' '].includes(event.key)) fire(false);
});
window.addEventListener('blur', () => { fire(false); throttle(false); });
document.addEventListener('visibilitychange', () => {
  if (document.hidden) { fire(false); throttle(false); }
});
$('#auto-drive').checked = autoDrive;
$('#room-auto-drive').checked = autoDrive;
for (const id of ['auto-drive', 'room-auto-drive'])
  $('#' + id).addEventListener('change', event => setAutoDrive(event.target.checked));
function fillRoomLink() {
  if (location.hash.startsWith('#room=')) $('#join-link').value = location.href;
}
window.addEventListener('hashchange', fillRoomLink);
fillRoomLink();
canvas.tabIndex = 0;
requestAnimationFrame(frame);
