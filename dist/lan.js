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
  heldFire: false, moveKeys: new Set(), lastMove: '0,0',
  lastPhase: null, lastRound: 0, leaving: false, toastTimer: null,
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
  state.moveKeys.clear();
  state.lastMove = '0,0';
  state.lastMovePhase = null;
  clearTimeout(state.toastTimer);
  $('#action-toast').hidden = true;
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
      name: $('#name').value, maxPlayers: Number($('#players').value),
      map: $('#map').value,
      mode: $('#create-mode').value, rule: $('#create-rule').value,
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
  if (['countdown', 'running', 'paused', 'connection-wait'].includes(view.phase) && view.round) {
    location.replace('./index.html?onlineRoom=' + view.code);
    return;
  }
  state.view = view;
  state.selfId = view.selfId;
  state.code = view.code;
  const key = view.phase + ':' + view.roundNumber;
  if (key !== state.lastPhase) {
    state.lastPhase = key;
    if (view.phase !== 'running') state.heldFire = false;
    if (view.phase === 'running') {
      announce('Heat ' + view.roundNumber + ' started in ' + modeName(view.mode) + '.');
      canvas.focus({ preventScroll: true });
    } else if (view.phase === 'round-result' || view.phase === 'match-result') {
      const match = view.phase === 'match-result';
      const winningId = match ? view.result?.matchWinnerId : view.result?.winnerId;
      const winningName = match ? view.result?.matchWinnerName : view.result?.winnerName;
      const who = winningId === view.selfId ? 'You won' :
        winningName ? winningName + ' won' : 'The heat was a draw';
      announce(match && view.result?.matchDraw ? 'The match ended in a draw.' :
        who + (match ? ' the match.' : ' the heat.'));
    } else if (view.phase === 'connection-wait') {
      announce('Connection interrupted. The match is waiting.');
    }
  }
  renderUi(view);
  if (view.phase === 'running' && key !== state.lastMovePhase) {
    state.lastMovePhase = key;
    state.lastMove = '';
    syncMove();
  }
}
const MODE_DETAILS = {
  duel: { name: 'Duel', summary: 'First rider to win three heats takes the match.',
    rules: 'Duel: first to three heat wins. A/D or Left/Right turns; W or Up moves forward when Auto Drive is off. Hold E to fire.' },
  arena: { name: 'Arena', summary: 'Three heats. The most heat wins takes the match; a tie is a draw.',
    rules: 'Arena: exactly three heats. The rider with the most heat wins takes the match. A/D or Left/Right turns; W or Up moves forward when Auto Drive is off. Hold E to fire.' },
  shift: { name: 'Grid Shift', summary: 'First to three heat wins. The Grid begins shrinking after three minutes.',
    rules: 'Grid Shift: A/D or Left/Right turns the cycle; W or Up moves forward when Auto Drive is off. Hold E to fire, F changes form. On foot use WASD or arrows to move and Space to throw a disc at the nearest rider. Two disc hits derez a rider. The Grid shrinks after three minutes. At five minutes, the surviving rider with the most integrity wins; ties are draws.' },
};
function modeName(mode) { return MODE_DETAILS[mode]?.name || MODE_DETAILS.duel.name; }
function toast(message) {
  const element = $('#action-toast');
  element.textContent = message;
  element.hidden = !message;
  announce(message);
  clearTimeout(state.toastTimer);
  if (message) state.toastTimer = setTimeout(() => { element.hidden = true; }, 2200);
}
function renderUi(view) {
  document.body.dataset.phase = view.phase;
  document.body.dataset.mode = view.mode || 'duel';
  document.body.dataset.round = String(view.roundNumber);
  const active = view.phase === 'running';
  const mode = view.mode || 'duel';
  const shift = mode === 'shift';
  const selfRider = view.round?.riders.find(r => r.id === view.selfId);
  const waiting = view.phase === 'connection-wait';
  const showPanel = !active && view.phase !== 'countdown';
  panel.hidden = !showPanel;
  setup.hidden = true;
  roomSection.hidden = !showPanel;
  $('#touch').hidden = !active;
  $('#touch').classList.toggle('shift', shift);
  for (const id of ['up', 'down', 'form', 'disc']) $('#' + id).hidden = !shift;
  $('#fire').disabled = shift && selfRider?.form === 'foot';
  $('#disc').disabled = !shift || selfRider?.form !== 'foot';
  $('#countdown').hidden = view.phase !== 'countdown';
  if (view.phase === 'countdown')
    $('#countdown').textContent = String(Math.max(1, Math.ceil(view.countdownMs / 1000)));
  $('#room-code-top').textContent = 'ROOM ' + view.code;
  $('#room-code-large').value = view.code;
  const host = view.selfId === view.hostId;
  const full = view.players.length >= 2 && view.players.length === view.maxPlayers &&
    view.players.every(p => p.connected);
  $('#pause').hidden = !host || !['running', 'countdown', 'paused'].includes(view.phase);
  $('#pause').textContent = view.phase === 'paused' ? 'RESUME' : 'PAUSE';
  const labels = {
    lobby: 'WAITING ROOM', countdown: 'LAUNCH', running: 'LIVE HEAT',
    'round-result': 'HEAT COMPLETE', 'match-result': 'MATCH COMPLETE',
    paused: 'HOST PAUSED', 'connection-wait': 'CONNECTION HELD',
  };
  $('#room-phase').textContent = active ? 'LIVE ' + modeName(mode).toUpperCase()
    : labels[view.phase] || 'GAME ROOM';
  $('#host-mode-field').hidden = !host || view.phase !== 'lobby';
  $('#room-mode').value = mode;
  $('#room-map').value = view.map;
  $('#host-rule-field').hidden = !host || view.phase !== 'lobby' || mode !== 'arena';
  $('#room-rule').value = view.rule || 'classic';
  $('#mode-summary').textContent = modeName(mode) + ' / ' + MODE_DETAILS[mode].summary +
    ' Map: ' + getMap(view.map).name + '.' +
    (mode === 'arena' ? view.rule === 'decay' ? ' Walls fade after 10 seconds.' : ' Persistent walls.' : '');
  $('#room-rules').textContent = MODE_DETAILS[mode].rules +
    ' Touch controls appear below during play.';
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
      return (rider?.name || crash.name || crash.riderId) + ': ' + crash.reason;
    }).join(' / ');
    const finish = view.players.length < 2 ? ' Every other rider has left. Leave this room to start a new one.'
      : view.phase === 'match-result'
        ? lastResult?.matchDraw ? ' The match is a draw.'
          : ' ' + lastResult?.matchWinnerName + ' won the ' + modeName(mode) + ' match.'
        : ' Prepare for the next heat.';
    message = (lastResult?.winnerId ? who + ' won heat ' + view.roundNumber :
      'Heat ' + view.roundNumber + ' was a draw') + '.' +
      (cause ? ' ' + cause + '.' : '') + finish;
  } else if (view.phase === 'paused') message = 'The host paused the Grid. All riders are held.';
  else if (waiting) message = 'Waiting up to ' + Math.ceil(view.waitMs / 1000) +
    ' seconds for a rider to reconnect. The simulation is stopped.';
  else message = modeName(mode) + ' / Heat ' + view.roundNumber + ' / ' +
    getMap(view.map).name + (shift && view.round ?
      ' / ' + Math.max(0, Math.ceil(view.round.shift.timeLimit - view.round.elapsed)) +
      ' seconds left.' : '.');
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
    record.textContent = player.wins + (mode === 'arena' ? ' HEAT WINS / ' : ' WINS / ') +
      (player.connected ? 'CONNECTED' : 'OFFLINE');
    item.append(label, record);
    list.append(item);
  }
  const scores = $('#scorebar');
  scores.replaceChildren();
  if (shift && view.round) {
    const timer = document.createElement('div');
    timer.className = 'score timer';
    timer.textContent = Math.max(0, Math.ceil(view.round.shift.timeLimit -
      view.round.elapsed)) + 's LEFT';
    scores.append(timer);
  }
  for (const player of view.players) {
    const item = document.createElement('div');
    item.className = 'score' + (player.id === view.selfId ? ' self' : '') +
      (view.round?.riders.find(r => r.id === player.id)?.alive === false ? ' out' : '');
    const rider = view.round?.riders.find(r => r.id === player.id);
    item.textContent = player.name + ' ' + player.wins +
      (mode === 'arena' ? ' PT' : '/3');
    if (rider) {
      const status = document.createElement('span');
      const discOut = shift && view.round.shift.discs.some(d => d.ownerId === rider.id);
      const disc = discOut ? 'DISC OUT' :
        view.round.elapsed >= rider.discReadyAt ? 'DISC READY' : 'DISC RECHARGING';
      status.textContent = '  /  ' + (!rider.alive ? 'DEREZZED' : shift
        ? rider.form.toUpperCase() + ' / ' + rider.hp + ' HP / ' +
          (rider.form === 'cycle' ? rider.ammo + ' SHOTS' : disc)
        : rider.ammo + ' SHOTS');
      item.append(status);
    }
    scores.append(item);
  }
  $('#start').hidden = !host || !['lobby', 'round-result', 'match-result'].includes(view.phase);
  $('#start').disabled = !full;
  $('#start').textContent = view.phase === 'lobby' ? 'START ' + modeName(mode).toUpperCase() :
    view.phase === 'match-result' ? 'REMATCH' : 'NEXT HEAT';
  if (!full && host && !waiting && (view.phase === 'lobby' || view.players.length >= 2)) notice('Waiting for all ' + view.maxPlayers + ' riders.');
  else if (!state.reconnecting) notice('');
}
async function action(value) {
  if (!state.code) return;
  try { await post(roomPath('action'), value); }
  catch (error) {
    if (['turn', 'fire', 'move'].includes(value.type)) return;
    notice(error.message, true);
    toast(error.message);
  }
}
function ownRider() {
  return state.view?.round?.riders.find(r => r.id === state.selfId);
}
function turn(sign) {
  if (state.view?.phase !== 'running' || state.reconnecting) return;
  if (state.view.mode === 'shift' && ownRider()?.form !== 'cycle') return;
  action({ type: 'turn', sign });
}
function fire(held) {
  if (held && state.view?.mode === 'shift' && ownRider()?.form !== 'cycle') return;
  if (state.heldFire === held) return;
  state.heldFire = held;
  if (state.view?.phase === 'running' && !state.reconnecting)
    action({ type: 'fire', held });
}
function syncMove() {
  if (state.view?.mode !== 'shift' || state.view.phase !== 'running' ||
      state.reconnecting) return;
  const keys = state.moveKeys;
  const x = Number(keys.has('right')) - Number(keys.has('left'));
  const z = Number(keys.has('down')) - Number(keys.has('up'));
  const length = Math.hypot(x, z);
  const vector = length > 1 ? { x: x / length, z: z / length } : { x, z };
  const value = vector.x + ',' + vector.z;
  if (state.lastMove === value) return;
  state.lastMove = value;
  action({ type: 'move', ...vector });
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
    for (const disc of round.shift?.discs || []) {
      ctx.strokeStyle = disc.returning ? '#fff1c9' : '#ffcf8d';
      ctx.lineWidth = 3;
      ctx.beginPath(); ctx.arc(x(disc.x), y(disc.z), 6, 0, Math.PI * 2); ctx.stroke();
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
      if (rider.form === 'foot') {
        ctx.arc(0, 0, 7, 0, Math.PI * 2);
      } else {
        ctx.moveTo(0, -10); ctx.lineTo(7, 8); ctx.lineTo(0, 4); ctx.lineTo(-7, 8);
        ctx.closePath();
      }
      ctx.fill(); ctx.stroke();
      if (rider.form === 'foot') {
        ctx.fillStyle = '#041321';
        ctx.beginPath(); ctx.arc(0, 0, 2, 0, Math.PI * 2); ctx.fill();
      }
      ctx.restore();
      const label = view.players.find(p => p.id === rider.id);
      ctx.font = '700 12px Rajdhani, Arial, sans-serif';
      ctx.textAlign = 'center';
      ctx.fillStyle = '#edfcff';
      ctx.fillText((rider.id === view.selfId ? 'YOU' : label?.name || rider.id) +
        (rider.form === 'foot' ? ' / FOOT' : ''), px, py - 15);
    }
  }
  requestAnimationFrame(frame);
}
$('#create').addEventListener('click', createRoom);
$('#create-mode').addEventListener('change', () => {
  $('#create-rule-field').hidden = $('#create-mode').value !== 'arena';
});
$('#room-rule').addEventListener('change', event =>
  action({ type: 'rule', rule: event.currentTarget.value }));

$('#room-mode').addEventListener('change', event =>
  action({ type: 'mode', mode: event.currentTarget.value }));
$('#room-map').addEventListener('change', event =>
  action({ type: 'map', map: event.currentTarget.value }));
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
  state.moveKeys.clear();
  syncMove();
  try { await post(roomPath('leave'), {}); } catch {}
  history.replaceState(null, '', './lan.html');
  resetToSetup('You left the room.');
});
for (const [id, direction] of [['up', 'up'], ['left', 'left'],
  ['down', 'down'], ['right', 'right']]) {
  const button = $('#' + id);
  button.addEventListener('pointerdown', event => {
    event.preventDefault();
    button.setPointerCapture(event.pointerId);
    state.moveKeys.add(direction);
    syncMove();
    if (direction === 'left' || direction === 'right')
      turn(direction === 'left' ? -1 : 1);
  });
  const release = () => { state.moveKeys.delete(direction); syncMove(); };
  for (const event of ['pointerup', 'pointercancel', 'lostpointercapture'])
    button.addEventListener(event, release);
}
$('#form').addEventListener('click', () => action({ type: 'form' }));
$('#disc').addEventListener('click', () => action({ type: 'disc' }));
$('#fire').addEventListener('pointerdown', event => {
  event.preventDefault(); event.currentTarget.setPointerCapture(event.pointerId); fire(true);
});
for (const event of ['pointerup', 'pointercancel', 'lostpointercapture'])
  $('#fire').addEventListener(event, () => fire(false));
const directionKeys = {
  ArrowUp: 'up', w: 'up', W: 'up', ArrowLeft: 'left', a: 'left', A: 'left',
  ArrowDown: 'down', s: 'down', S: 'down', ArrowRight: 'right', d: 'right', D: 'right',
};
document.addEventListener('keydown', event => {
  if (interactiveFocus()) return;
  const shift = state.view?.mode === 'shift' && state.view.phase === 'running';
  const direction = directionKeys[event.key];
  if (shift && direction) {
    event.preventDefault();
    state.moveKeys.add(direction);
    syncMove();
    if (!event.repeat && (direction === 'left' || direction === 'right'))
      turn(direction === 'left' ? -1 : 1);
  } else if (shift && !event.repeat && ['f', 'F'].includes(event.key)) {
    event.preventDefault(); action({ type: 'form' });
  } else if (shift && !event.repeat && event.key === ' ') {
    event.preventDefault(); action({ type: 'disc' });
  } else if (!event.repeat && direction === 'left') {
    event.preventDefault(); turn(-1);
  } else if (!event.repeat && direction === 'right') {
    event.preventDefault(); turn(1);
  } else if (['e', 'E'].includes(event.key) || (!shift && event.key === ' ')) {
    event.preventDefault(); fire(true);
  } else if (!event.repeat && ['p', 'P'].includes(event.key) &&
      state.view?.selfId === state.view?.hostId) {
    event.preventDefault(); action({ type: 'pause' });
  }
});
document.addEventListener('keyup', event => {
  const direction = directionKeys[event.key];
  if (direction) { state.moveKeys.delete(direction); syncMove(); }
  if (['e', 'E'].includes(event.key) ||
      (state.view?.mode !== 'shift' && event.key === ' ')) fire(false);
});
function releaseControls() {
  fire(false);
  state.moveKeys.clear();
  syncMove();
}
window.addEventListener('blur', releaseControls);
document.addEventListener('visibilitychange', () => {
  if (document.hidden) releaseControls();
});
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