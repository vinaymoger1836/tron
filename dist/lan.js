import { getMap } from './maps.js';

const $ = selector => document.querySelector(selector);
const canvas = $('#arena');
const ctx = canvas.getContext('2d');
const setup = $('#setup');
const panel = $('#panel');
const roomSection = $('#room');
const statusLive = $('#status-live');
const state = {
  code: null, selfId: null, view: null, online: false,
  baseUrl: location.origin + '/lan.html',
  streamAbort: null, reconnectTimer: null, reconnecting: false,
  heldFire: false, lastPhase: null, lastRound: 0, leaving: false,
};
function notice(message, error = false, target = '#room-notice') {
  const element = $(target);
  element.textContent = message;
  element.classList.toggle('error', error);
}
function announce(message) {
  if (statusLive.textContent !== message) statusLive.textContent = message;
}
async function api(url, options = {}) {
  const response = await fetch(url, { cache: 'no-store', credentials: 'same-origin', ...options });
  if (!response.ok) {
    let message = 'The room request failed.';
    try { message = (await response.json()).error || message; } catch {}
    const error = new Error(message);
    error.status = response.status;
    throw error;
  }
  return response.headers.get('content-type')?.includes('application/json')
    ? response.json() : response;
}
function post(url, body) {
  return api(url, { method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body) });
}
function roomPath(action = '') {
  return '/api/rooms/' + state.code + (action ? '/' + action : '');
}
function resetToSetup(message = '') {
  state.streamAbort?.abort();
  clearTimeout(state.reconnectTimer);
  state.code = null;
  state.selfId = null;
  state.view = null;
  state.heldFire = false;
  state.lastPhase = null;
  state.reconnecting = false;
  panel.hidden = false;
  setup.hidden = false;
  roomSection.hidden = true;
  $('#touch').hidden = true;
  $('#countdown').hidden = true;
  $('#room-code-top').textContent = 'NO ROOM';
  if (message) notice(message, true, '#setup-notice');
}
async function createRoom() {
  notice('', false, '#setup-notice');
  $('#create').disabled = true;
  try {
    const result = await post('/api/rooms', {
      name: $('#name').value, maxPlayers: Number($('#players').value), map: $('#map').value,
    });
    enterRoom(result);
  } catch (error) { notice(error.message, true, '#setup-notice'); }
  finally { $('#create').disabled = false; }
}
async function joinRoom() {
  notice('', false, '#setup-notice');
  const code = $('#join-code').value.trim().toUpperCase();
  if (!(state.online ? /^[0-9]{6}$/ : /^[A-Z2-9]{6}$/).test(code))
    return notice(state.online ? 'Enter the six-digit room code.' :
      'Enter the six-character room code.', true, '#setup-notice');
  $('#join').disabled = true;
  try {
    const result = await post('/api/rooms/' + code + '/join', { name: $('#name').value });
    enterRoom(result);
  } catch (error) { notice(error.message, true, '#setup-notice'); }
  finally { $('#join').disabled = false; }
}
function enterRoom(result) {
  state.code = result.code;
  state.selfId = result.playerId;
  state.leaving = false;
  history.replaceState(null, '', './lan.html?room=' + state.code);
  setup.hidden = true;
  roomSection.hidden = false;
  panel.hidden = false;
  connect();
}
async function connect() {
  if (!state.code || state.leaving) return;
  clearTimeout(state.reconnectTimer);
  state.streamAbort?.abort();
  const controller = new AbortController();
  state.streamAbort = controller;
  try {
    const response = await api(roomPath('stream'), { signal: controller.signal });
    state.reconnecting = false;
    notice('');
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let pending = '';
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      pending += decoder.decode(value, { stream: true });
      let end;
      while ((end = pending.indexOf('\n')) >= 0) {
        const line = pending.slice(0, end);
        pending = pending.slice(end + 1);
        if (line) receive(JSON.parse(line));
      }
    }
    if (!controller.signal.aborted) throw new Error('Room connection closed.');
  } catch (error) {
    if (controller.signal.aborted || state.leaving) return;
    if (error.status === 403 || error.status === 404) {
      resetToSetup(error.message);
      return;
    }
    state.reconnecting = true;
    panel.hidden = false;
    roomSection.hidden = false;
    $('#touch').hidden = true;
    notice('Connection interrupted. Reconnecting...', true);
    state.reconnectTimer = setTimeout(connect, 1000);
  }
}
function receive(view) {
  state.view = view;
  state.selfId = view.selfId;
  state.code = view.code;
  const key = view.phase + ':' + view.roundNumber;
  if (key !== state.lastPhase) {
    state.lastPhase = key;
    if (view.phase !== 'running') state.heldFire = false;
    if (view.phase === 'running') {
      announce('Heat ' + view.roundNumber + ' started. Turn into open lanes.');
      canvas.focus({ preventScroll: true });
    } else if (view.phase === 'round-result' || view.phase === 'match-result') {
      const who = view.result?.winnerId === view.selfId ? 'You won' :
        view.result?.winnerName ? view.result.winnerName + ' won' : 'The heat was a draw';
      announce(who + (view.phase === 'match-result' ? ' the match.' : ' the heat.'));
    } else if (view.phase === 'connection-wait') {
      announce('Connection interrupted. The match is waiting.');
    }
  }
  renderUi(view);
}
function renderUi(view) {
  document.body.dataset.phase = view.phase;
  document.body.dataset.round = String(view.roundNumber);
  const active = view.phase === 'running';
  const waiting = view.phase === 'connection-wait';
  const showPanel = !active && view.phase !== 'countdown';
  panel.hidden = !showPanel;
  setup.hidden = true;
  roomSection.hidden = !showPanel;
  $('#touch').hidden = !active;
  $('#countdown').hidden = view.phase !== 'countdown';
  if (view.phase === 'countdown')
    $('#countdown').textContent = String(Math.max(1, Math.ceil(view.countdownMs / 1000)));
  $('#room-code-top').textContent = 'ROOM ' + view.code;
  $('#room-code-large').value = view.code;
  const host = view.selfId === view.hostId;
  const full = view.players.length === view.maxPlayers && view.players.every(p => p.connected);
  $('#pause').hidden = !host || !['running', 'countdown', 'paused'].includes(view.phase);
  $('#pause').textContent = view.phase === 'paused' ? 'RESUME' : 'PAUSE';
  const labels = {
    lobby: 'WAITING ROOM', countdown: 'LAUNCH', running: 'LIVE HEAT',
    'round-result': 'HEAT COMPLETE', 'match-result': 'MATCH COMPLETE',
    paused: 'HOST PAUSED', 'connection-wait': 'CONNECTION HELD',
  };
  $('#room-phase').textContent = labels[view.phase] || 'GAME ROOM';
  $('#room-title').innerHTML = view.phase === 'match-result'
    ? 'GRID <span>SECURED</span>' : view.phase === 'round-result'
      ? 'HEAT <span>ENDED</span>' : 'GRID <span>OPEN</span>';
  const lastResult = view.result;
  let message;
  if (view.phase === 'lobby')
    message = view.players.length + '/' + view.maxPlayers +
      (state.online ? ' riders connected. Share the six-digit code, then the host starts the match.' :
        ' riders connected. Share the link, then the host starts the match.');
  else if (view.phase === 'round-result' || view.phase === 'match-result') {
    const who = lastResult?.winnerId === view.selfId ? 'You' :
      lastResult?.winnerName || 'No rider';
    const cause = lastResult?.crashes?.map(crash => {
      const rider = view.players.find(p => p.id === crash.riderId);
      return (rider?.name || crash.riderId) + ': ' + crash.reason;
    }).join(' / ');
    message = (lastResult?.winnerId ? who + ' won heat ' + view.roundNumber :
      'Heat ' + view.roundNumber + ' was a draw') + '. ' +
      (cause ? cause + '.' : 'All riders derezzed together.') +
      (view.phase === 'match-result' ? ' First to three complete.' : ' Prepare for the next heat.');
  } else if (view.phase === 'paused') message = 'The host paused the Grid. All riders are held.';
  else if (waiting) message = 'Waiting up to ' + Math.ceil(view.waitMs / 1000) +
    ' seconds for a rider to reconnect. The simulation is stopped.';
  else message = 'Heat ' + view.roundNumber + ' / ' + getMap(view.map).name + '.';
  $('#room-message').textContent = message;
  $('#share-link').value = state.baseUrl + '?room=' + view.code;
  const list = $('#roster');
  list.replaceChildren();
  for (const player of view.players) {
    const item = document.createElement('li');
    item.classList.toggle('offline', !player.connected);
    const label = document.createElement('span');
    label.textContent = player.name + (player.id === view.selfId ? ' / YOU' : '');
    const record = document.createElement('small');
    record.textContent = player.wins + ' WINS / ' +
      (player.connected ? 'CONNECTED' : 'OFFLINE');
    item.append(label, record);
    list.append(item);
  }
  const scores = $('#scorebar');
  scores.replaceChildren();
  for (const player of view.players) {
    const item = document.createElement('div');
    item.className = 'score' + (player.id === view.selfId ? ' self' : '') +
      (view.round?.riders.find(r => r.id === player.id)?.alive === false ? ' out' : '');
    const rider = view.round?.riders.find(r => r.id === player.id);
    item.textContent = player.name + ' ' + player.wins + '/3';
    if (rider) {
      const ammo = document.createElement('span');
      ammo.textContent = '  •  ' + (rider.alive ? rider.ammo + ' SHOTS' : 'DEREZZED');
      item.append(ammo);
    }
    scores.append(item);
  }
  $('#start').hidden = !host || !['lobby', 'round-result', 'match-result'].includes(view.phase);
  $('#start').disabled = !full;
  $('#start').textContent = view.phase === 'lobby' ? 'START MATCH' :
    view.phase === 'match-result' ? 'REMATCH' : 'NEXT HEAT';
  if (!full && host && !waiting) notice('Waiting for all ' + view.maxPlayers + ' riders.');
  else if (!state.reconnecting) notice('');
}
async function action(value) {
  if (!state.code) return;
  try { await post(roomPath('action'), value); }
  catch (error) {
    if (value.type === 'turn' || value.type === 'fire') return;
    notice(error.message, true);
  }
}
function turn(sign) {
  if (state.view?.phase === 'running' && !state.reconnecting) action({ type: 'turn', sign });
}
function fire(held) {
  if (state.heldFire === held) return;
  state.heldFire = held;
  if (state.view?.phase === 'running' && !state.reconnecting)
    action({ type: 'fire', held });
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
      ctx.fillStyle = '#ffe6a5';
      ctx.beginPath(); ctx.arc(x(bullet.x), y(bullet.z), 3, 0, Math.PI * 2); ctx.fill();
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
$('#join-code').addEventListener('keydown', event => { if (event.key === 'Enter') joinRoom(); });
$('#start').addEventListener('click', () => action({ type: 'start' }));
$('#pause').addEventListener('click', () => action({ type: 'pause' }));
$('#copy-code').addEventListener('click', async () => {
  const field = $('#room-code-large');
  try {
    if (navigator.clipboard?.writeText) await navigator.clipboard.writeText(field.value);
    else { field.select(); if (!document.execCommand('copy')) throw new Error('Copy unavailable'); }
    notice('Room code copied. Share it with your friends.');
  } catch { field.select(); notice('Select and copy the highlighted code.'); }
});
$('#copy-link').addEventListener('click', async () => {
  const link = $('#share-link');
  try {
    if (navigator.clipboard?.writeText) await navigator.clipboard.writeText(link.value);
    else { link.select(); if (!document.execCommand('copy')) throw new Error('Copy unavailable.'); }
    notice('Room link copied.');
  } catch { link.select(); notice('Select and copy the room link.'); }
});
$('#leave').addEventListener('click', async () => {
  state.leaving = true;
  fire(false);
  try { await post(roomPath('leave'), {}); } catch {}
  history.replaceState(null, '', './lan.html');
  resetToSetup('You left the room.');
});
$('#left').addEventListener('pointerdown', event => { event.preventDefault(); turn(-1); });
$('#right').addEventListener('pointerdown', event => { event.preventDefault(); turn(1); });
$('#fire').addEventListener('pointerdown', event => {
  event.preventDefault(); event.currentTarget.setPointerCapture(event.pointerId); fire(true);
});
for (const event of ['pointerup', 'pointercancel', 'lostpointercapture'])
  $('#fire').addEventListener(event, () => fire(false));
document.addEventListener('keydown', event => {
  if (interactiveFocus()) return;
  if (!event.repeat && ['ArrowLeft', 'a', 'A'].includes(event.key)) {
    event.preventDefault(); turn(-1);
  } else if (!event.repeat && ['ArrowRight', 'd', 'D'].includes(event.key)) {
    event.preventDefault(); turn(1);
  } else if (['e', 'E', ' '].includes(event.key)) {
    event.preventDefault(); fire(true);
  } else if (!event.repeat && ['p', 'P'].includes(event.key) &&
      state.view?.selfId === state.view?.hostId) {
    event.preventDefault(); action({ type: 'pause' });
  }
});
document.addEventListener('keyup', event => {
  if (['e', 'E', ' '].includes(event.key)) fire(false);
});
window.addEventListener('blur', () => fire(false));
document.addEventListener('visibilitychange', () => { if (document.hidden) fire(false); });
const roomFromUrl = new URLSearchParams(location.search).get('room')?.toUpperCase();
if (roomFromUrl && /^[A-Z0-9]{6}$/.test(roomFromUrl)) $('#join-code').value = roomFromUrl;
(async () => {
  try {
    const status = await api('/api/lan/status');
    state.baseUrl = status.baseUrl || state.baseUrl;
    state.online = !!status.online;
    $('#online-code').hidden = !state.online;
    $('#local-share').hidden = state.online;
    if (!state.online) {
      document.title = 'TRON: Grid Run / LAN Room';
      $('#room-tag').textContent = 'LOCAL LAN';
      $('#room-eyebrow').textContent = 'Friends on one network';
      $('#room-heading').innerHTML = 'LAN <span>ROOM</span>';
      $('#room-intro').textContent = 'Create a room on this computer, then share its LAN link or six-character code. All players use the same Wi-Fi or hotspot.';
      $('#setup-hint').textContent = 'The room host must run node lan-server.mjs. Solo play stays available from the logo above.';
      $('#join-code').inputMode = 'text';
      $('#join-code').placeholder = 'ABC234';
      $('#join-code').removeAttribute('pattern');
    }
    if (roomFromUrl && (state.online ? /^[0-9]{6}$/ : /^[A-Z2-9]{6}$/).test(roomFromUrl)) {
      state.code = roomFromUrl;
      connect();
    }
  } catch {
    $('#create').disabled = true;
    $('#join').disabled = true;
    notice('Room server unavailable. Open the online game server URL to create or join.',
      true, '#setup-notice');
  }
})();
canvas.tabIndex = 0;
requestAnimationFrame(frame);