import { createRound, drainEvents, queueTurn, stepRound, STEP, LIMIT } from './simulation.js';

const canvas = document.querySelector('#game');
const radar = document.querySelector('#radar');
const overlay = document.querySelector('#overlay');
const countdown = document.querySelector('#countdown');
const statusLive = document.querySelector('#status-live');
const touchControls = document.querySelector('.touch-controls');
const touchToggle = document.querySelector('#touch-toggle');
const ui = Object.fromEntries(
  ['score', 'best', 'rivals', 'eyebrow', 'title', 'deck', 'start', 'toast', 'sound', 'pause']
    .map(id => [id, document.getElementById(id)]),
);

function showWebglError(message) {
  overlay.inert = true;
  document.querySelector('.hud').inert = true;
  touchControls.inert = true;
  canvas.inert = true;
  const box = document.createElement('div');
  box.className = 'webgl-error';
  box.setAttribute('role', 'alert');
  const content = document.createElement('div');
  const description = document.createElement('p');
  description.textContent = message;
  const retry = document.createElement('button');
  retry.className = 'primary';
  retry.type = 'button';
  retry.textContent = 'RETRY';
  retry.addEventListener('click', () => location.reload());
  content.append(description, retry);
  box.append(content);
  document.body.append(box);
  retry.focus({ preventScroll: true });
}

let gl;
try {
  gl = canvas.getContext('webgl', { antialias: true, alpha: false, powerPreference: 'high-performance' });
} catch {
  gl = null;
}
if (!gl) {
  showWebglError('This Grid needs WebGL. Enable graphics acceleration or try another browser.');
} else {
try {
const VERTEX = `
attribute vec3 aPosition;
attribute vec3 aColor;
attribute float aKind;
uniform mat4 uMatrix;
uniform vec3 uCamera;
varying vec3 vColor;
varying vec3 vWorld;
varying float vKind;
varying float vDistance;
void main(){
  vColor=aColor; vWorld=aPosition; vKind=aKind;
  vDistance=distance(aPosition,uCamera);
  gl_Position=uMatrix*vec4(aPosition,1.0);
}`;
const FRAGMENT = `
precision mediump float;
uniform float uTime;
varying vec3 vColor;
varying vec3 vWorld;
varying float vKind;
varying float vDistance;
void main(){
  vec3 color=vColor;
  if(vKind>0.5 && vKind<1.5){
    vec2 cell=abs(fract(vWorld.xz/4.0)-0.5);
    float line=1.0-smoothstep(0.475,0.495,max(cell.x,cell.y));
    vec2 major=abs(fract(vWorld.xz/16.0)-0.5);
    float majorLine=1.0-smoothstep(0.488,0.5,max(major.x,major.y));
    float glow=0.65+0.15*sin(uTime*1.4);
    color+=vec3(0.005,0.17,0.23)*line*glow+vec3(0.005,0.12,0.17)*majorLine;
  }
  if(vKind>1.5) color*=1.15+0.12*sin(uTime*4.0+vWorld.x*0.7+vWorld.z*0.5);
  float fog=smoothstep(22.0,85.0,vDistance);
  color=mix(color,vec3(0.006,0.025,0.045),fog*0.75);
  gl_FragColor=vec4(color,1.0);
}`;

function shader(type, source) {
  const result = gl.createShader(type);
  gl.shaderSource(result, source); gl.compileShader(result);
  if (!gl.getShaderParameter(result, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(result));
  return result;
}
const program = gl.createProgram();
gl.attachShader(program, shader(gl.VERTEX_SHADER, VERTEX));
gl.attachShader(program, shader(gl.FRAGMENT_SHADER, FRAGMENT));
gl.linkProgram(program);
if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
gl.useProgram(program);
const loc = {
  position: gl.getAttribLocation(program, 'aPosition'), color: gl.getAttribLocation(program, 'aColor'),
  kind: gl.getAttribLocation(program, 'aKind'), matrix: gl.getUniformLocation(program, 'uMatrix'),
  camera: gl.getUniformLocation(program, 'uCamera'), time: gl.getUniformLocation(program, 'uTime')
};
gl.enable(gl.DEPTH_TEST);
gl.clearColor(0.003, 0.013, 0.027, 1);


const C = {
  player: [0.15, 0.88, 1.0],
  orange: [1.0, 0.38, 0.09],
  violet: [0.75, 0.30, 1.0],
  gold: [1.0, 0.73, 0.20],
  floor: [0.008, 0.026, 0.045],
  dark: [0.015, 0.038, 0.064],
  wall: [0.018, 0.072, 0.10],
};
const DIR = [[0, -1], [1, 0], [0, 1], [-1, 0]];
const SCORE_KEY = 'tron-grid-run-best';
const MUTE_KEY = 'tron-grid-run-muted';
const TOUCH_KEY = 'tron-grid-run-touch';

function readLocal(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}
function writeLocal(key, value) {
  try { localStorage.setItem(key, value); } catch { /* Storage can be unavailable. */ }
}
const savedBest = Number(readLocal(SCORE_KEY));
let best = Number.isFinite(savedBest) && savedBest >= 0 ? savedBest : 0;
let muted = readLocal(MUTE_KEY) === 'true';
let touchChoice = readLocal(TOUCH_KEY) || 'auto';
let round = createRound();
let mode = 'ready';
let pausedFrom = 'running';
let visualTime = 0;
let lastFrame = 0;
let accumulator = 0;
let countdownRemaining = 0;
let countdownNumber = 0;
let generation = 0;
let toastSequence = 0;
let overlayFocusToken = 0;
let graphicsLost = false;
let camera = [7, 13, 31];
let target = [0, 0, 0];
let particles = [];
let audio;
const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
function mixColor(color, shade) { return color.map(v => Math.min(1, v * shade)); }
function vertex(out, p, color, kind) { out.push(p[0],p[1],p[2],color[0],color[1],color[2],kind); }
function quad(out, a,b,c,d,color,kind=0) {
  vertex(out,a,color,kind); vertex(out,b,color,kind); vertex(out,c,color,kind);
  vertex(out,a,color,kind); vertex(out,c,color,kind); vertex(out,d,color,kind);
}
function box(out,x,y,z,sx,sy,sz,color,kind=0) {
  const x0=x-sx/2,x1=x+sx/2,y0=y-sy/2,y1=y+sy/2,z0=z-sz/2,z1=z+sz/2;
  quad(out,[x0,y1,z0],[x1,y1,z0],[x1,y1,z1],[x0,y1,z1],mixColor(color,1.1),kind);
  quad(out,[x0,y0,z1],[x1,y0,z1],[x1,y0,z0],[x0,y0,z0],mixColor(color,.35),kind);
  quad(out,[x0,y0,z1],[x0,y1,z1],[x1,y1,z1],[x1,y0,z1],mixColor(color,.8),kind);
  quad(out,[x1,y0,z0],[x1,y1,z0],[x0,y1,z0],[x0,y0,z0],mixColor(color,.7),kind);
  quad(out,[x1,y0,z1],[x1,y1,z1],[x1,y1,z0],[x1,y0,z0],mixColor(color,.55),kind);
  quad(out,[x0,y0,z0],[x0,y1,z0],[x0,y1,z1],[x0,y0,z1],mixColor(color,.9),kind);
}
const staticData = [];
quad(staticData,[-32,0,-32],[32,0,-32],[32,0,32],[-32,0,32],C.floor,1);
for (const s of [-1,1]) {
  box(staticData, s*LIMIT,.62,0,.34,1.24,LIMIT*2+.5,C.wall);
  box(staticData, 0,.62,s*LIMIT,LIMIT*2+.5,1.24,.34,C.wall);
  box(staticData, s*LIMIT,1.26,0,.18,.10,LIMIT*2+.5,[.06,.66,.82],2);
  box(staticData, 0,1.26,s*LIMIT,LIMIT*2+.5,.10,.18,[.06,.66,.82],2);
  for(let i=-3;i<=3;i++) {
    box(staticData,s*LIMIT,1.2,i*8,.48,2.4,.48,[.025,.18,.25]);
    box(staticData,i*8,1.2,s*LIMIT,.48,2.4,.48,[.025,.18,.25]);
    box(staticData,s*LIMIT,2.34,i*8,.5,.08,.5,[.13,.85,1],2);
    box(staticData,i*8,2.34,s*LIMIT,.5,.08,.5,[.13,.85,1],2);
  }
}
const staticBuffer = gl.createBuffer();
gl.bindBuffer(gl.ARRAY_BUFFER, staticBuffer);
gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(staticData), gl.STATIC_DRAW);
const dynamicBuffer = gl.createBuffer();

function perspective(fov,aspect,near,far) {
  const f=1/Math.tan(fov/2), m=new Float32Array(16);
  m[0]=f/aspect;m[5]=f;m[10]=(far+near)/(near-far);m[11]=-1;m[14]=2*far*near/(near-far);
  return m;
}
function lookAt(eye,center) {
  const norm=v=>{const n=Math.hypot(...v)||1;return v.map(x=>x/n);};
  const cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
  const z=norm(eye.map((v,i)=>v-center[i]));
  const x=norm(cross([0,1,0],z)), y=cross(z,x);
  return new Float32Array([x[0],y[0],z[0],0,x[1],y[1],z[1],0,x[2],y[2],z[2],0,
    -x.reduce((s,v,i)=>s+v*eye[i],0),-y.reduce((s,v,i)=>s+v*eye[i],0),-z.reduce((s,v,i)=>s+v*eye[i],0),1]);
}
function multiply(a,b) {
  const m=new Float32Array(16);
  for(let col=0;col<4;col++)for(let row=0;row<4;row++)for(let k=0;k<4;k++)m[col*4+row]+=a[k*4+row]*b[col*4+k];
  return m;
}
function bind(buffer) {
  gl.bindBuffer(gl.ARRAY_BUFFER,buffer);
  const stride=7*4;
  gl.enableVertexAttribArray(loc.position);gl.vertexAttribPointer(loc.position,3,gl.FLOAT,false,stride,0);
  gl.enableVertexAttribArray(loc.color);gl.vertexAttribPointer(loc.color,3,gl.FLOAT,false,stride,12);
  gl.enableVertexAttribArray(loc.kind);gl.vertexAttribPointer(loc.kind,1,gl.FLOAT,false,stride,24);
}
function resize() {
  const dpr=Math.min(devicePixelRatio||1,1.75),w=Math.round(innerWidth*dpr),h=Math.round(innerHeight*dpr);
  if(canvas.width!==w||canvas.height!==h){canvas.width=w;canvas.height=h;gl.viewport(0,0,w,h);}
}


function announce(message) {
  if (statusLive) statusLive.textContent = message;
}

function updateHud() {
  ui.score.textContent = String(round.score).padStart(5, '0');
  ui.best.textContent = String(best).padStart(5, '0');
  ui.rivals.textContent = String(round.riders.filter(r => !r.isPlayer && r.alive).length).padStart(2, '0');
}

function setBackgroundInert(inert) {
  canvas.inert = inert;
  document.querySelector('.hud').inert = inert;
  touchControls.inert = inert;
}

function setOverlay(eyebrow, title, deck, button) {
  ui.eyebrow.textContent = eyebrow;
  ui.title.innerHTML = title;
  ui.deck.textContent = deck;
  ui.start.textContent = button;
  setBackgroundInert(true);
  overlay.inert = false;
  overlay.setAttribute('aria-hidden', 'false');
  overlay.classList.remove('hidden');
  const token = ++overlayFocusToken;
  let focusAttempts = 0;
  function focusWhenVisible() {
    if (token !== overlayFocusToken || overlay.classList.contains('hidden')) return;
    ui.start.focus({ preventScroll: true });
    if (document.activeElement !== ui.start && ++focusAttempts < 90) {
      requestAnimationFrame(focusWhenVisible);
    }
  }
  requestAnimationFrame(focusWhenVisible);
}

function hideOverlay() {
  ++overlayFocusToken;
  overlay.classList.add('hidden');
  overlay.setAttribute('aria-hidden', 'true');
  overlay.inert = true;
  setBackgroundInert(false);
  canvas.focus({ preventScroll: true });
}

function showToast(message) {
  const sequence = ++toastSequence;
  ui.toast.textContent = message;
  ui.toast.classList.add('show');
  setTimeout(() => {
    if (sequence === toastSequence) ui.toast.classList.remove('show');
  }, 1100);
}

function ensureAudio() {
  if (!audio) {
    try { audio = new (window.AudioContext || window.webkitAudioContext)(); }
    catch { muted = true; syncSoundButton(); }
  }
}

function beep(freq, duration = 0.11, type = 'sawtooth', gain = 0.035) {
  if (muted) return;
  ensureAudio();
  if (!audio) return;
  if (audio.state === 'suspended') audio.resume().catch(() => {});
  const oscillator = audio.createOscillator();
  const envelope = audio.createGain();
  const now = audio.currentTime;
  oscillator.type = type;
  oscillator.frequency.setValueAtTime(freq, now);
  oscillator.frequency.exponentialRampToValueAtTime(Math.max(50, freq * 0.57), now + duration);
  envelope.gain.setValueAtTime(gain, now);
  envelope.gain.exponentialRampToValueAtTime(0.0001, now + duration);
  oscillator.connect(envelope).connect(audio.destination);
  oscillator.start(now);
  oscillator.stop(now + duration + 0.01);
}

function syncSoundButton() {
  ui.sound.textContent = muted ? '♪̸' : '♫';
  ui.sound.setAttribute('aria-label', muted ? 'Unmute sound' : 'Mute sound');
}

function syncTouchControls() {
  const coarse = window.matchMedia('(any-pointer: coarse)').matches;
  touchControls.classList.remove('force-show', 'force-hide');
  if (touchChoice === 'show') touchControls.classList.add('force-show');
  if (touchChoice === 'hide') touchControls.classList.add('force-hide');
  const shown = touchChoice === 'show' || (touchChoice === 'auto' && coarse);
  if (touchToggle) {
    touchToggle.setAttribute('aria-pressed', String(shown));
    touchToggle.setAttribute('aria-label', shown ? 'Hide touch controls' : 'Show touch controls');
    touchToggle.title = shown ? 'Hide touch controls' : 'Show touch controls';
  }
}

function randomSeed() {
  const forced = new URLSearchParams(location.search);
  if (forced.has('seed')) {
    const value = Number(forced.get('seed'));
    if (Number.isSafeInteger(value) && value >= 0) return value >>> 0;
  }
  if (window.crypto?.getRandomValues) {
    const value = new Uint32Array(1);
    window.crypto.getRandomValues(value);
    return value[0];
  }
  return Math.floor(Math.random() * 0x100000000);
}

function hideCountdown() {
  countdown.classList.remove('show');
  countdown.textContent = '';
}

function launch() {
  if (mode !== 'countdown') return;
  mode = 'running';
  countdown.textContent = 'GO';
  countdown.classList.add('show');
  accumulator = 0;
  lastFrame = performance.now();
  beep(730, 0.2, 'triangle', 0.06);
  announce('Go. Survive the Grid.');
  const token = generation;
  setTimeout(() => { if (token === generation && mode === 'running') hideCountdown(); }, 420);
}

function start() {
  generation++;
  round = createRound({ seed: randomSeed() });
  mode = 'countdown';
  countdownRemaining = 3;
  countdownNumber = 0;
  accumulator = 0;
  lastFrame = performance.now();
  camera = [7, 13, 31];
  target = [0, 0, 0];
  particles = [];
  ++toastSequence;
  ui.toast.classList.remove('show');
  ui.pause.textContent = 'Ⅱ';
  ui.pause.setAttribute('aria-label', 'Pause game');
  updateHud();
  drawRadar();
  hideOverlay();
  countdown.classList.add('show');
  countdown.textContent = '3';
  countdownNumber = 3;
  beep(440, 0.08, 'triangle', 0.03);
  announce('Round starting. Three, two, one.');
}

function pause() {
  if (graphicsLost) return;
  if (mode === 'running' || mode === 'countdown') {
    pausedFrom = mode;
    mode = 'paused';
    accumulator = 0;
    countdown.classList.remove('show');
    ui.pause.textContent = '▶';
    ui.pause.setAttribute('aria-label', 'Resume game');
    setOverlay('CONNECTION HELD', 'GRID <span>PAUSED<small>01</small></span>',
      'The Grid is waiting. Resume when you are ready.', 'RESUME');
    announce('Game paused.');
  } else if (mode === 'paused') {
    mode = pausedFrom;
    lastFrame = performance.now();
    accumulator = 0;
    hideOverlay();
    if (mode === 'countdown') {
      countdown.textContent = String(Math.max(1, Math.ceil(countdownRemaining)));
      countdown.classList.add('show');
    } else {
      hideCountdown();
    }
    ui.pause.textContent = 'Ⅱ';
    ui.pause.setAttribute('aria-label', 'Pause game');
    announce('Game resumed.');
  }
}

function playerTurn(sign) {
  if (mode === 'running') queueTurn(round, 'YOU', sign);
}

function crashMessage(outcome) {
  if (outcome.reason === 'boundary') return 'You hit the arena edge.';
  if (outcome.reason === 'obstacle') return 'You hit a barrier.';
  if (outcome.reason === 'rider') return 'You collided with ' + (outcome.by || 'another rider') + '.';
  if (outcome.reason === 'trail') {
    return outcome.by === 'YOU'
      ? 'You crossed your own light wall.'
      : 'You crossed ' + (outcome.by || 'a rival') + '\'s light wall.';
  }
  return 'Your cycle was derezzed.';
}

function spawnDerez(event) {
  if (reducedMotion.matches) return;
  const rider = round.riders.find(r => r.id === event.riderId);
  const color = rider?.color || C.player;
  for (let i = 0; i < 12; i++) {
    const angle = Math.random() * Math.PI * 2;
    const speed = 1.5 + Math.random() * 3;
    particles.push({
      x: event.x, y: 0.55, z: event.z,
      vx: Math.cos(angle) * speed,
      vy: 1.2 + Math.random() * 2.8,
      vz: Math.sin(angle) * speed,
      age: 0,
      life: 0.35 + Math.random() * 0.35,
      color,
    });
  }
}

function updateParticles(dt) {
  if (reducedMotion.matches) { particles = []; return; }
  for (const particle of particles) {
    particle.age += dt;
    particle.x += particle.vx * dt;
    particle.y += particle.vy * dt;
    particle.z += particle.vz * dt;
    particle.vy -= 7 * dt;
  }
  particles = particles.filter(particle => particle.age < particle.life);
}

function finish(event) {
  mode = event.kind;
  accumulator = 0;
  if (round.score > best) {
    best = round.score;
    writeLocal(SCORE_KEY, String(best));
  }
  updateHud();
  const won = event.kind === 'won';
  if (won) {
    beep(740, 0.2, 'triangle', 0.06);
  } else {
    beep(140, 0.3, 'sawtooth', 0.07);
  }
  const reason = won
    ? 'All rivals derezzed. You scored ' + round.score.toLocaleString() + ' points.'
    : crashMessage(event) + ' You scored ' + round.score.toLocaleString() + ' points.';
  announce(won ? 'Round won. ' + reason : 'Round lost. ' + reason);
  const token = generation;
  setTimeout(() => {
    if (token !== generation || (mode !== 'won' && mode !== 'lost')) return;
    hideCountdown();
    setOverlay(
      won ? 'ALL RIVALS DEREZZED' : 'YOUR TRAIL ENDS HERE',
      won ? 'GRID <span>MASTER<small>01</small></span>' : 'END OF <span>LINE<small>01</small></span>',
      reason,
      'RUN AGAIN',
    );
  }, reducedMotion.matches ? 0 : 240);
}

function handleEvents() {
  for (const event of drainEvents(round)) {
    if (event.type === 'turn-queued' && event.riderId === 'YOU') {
      showToast(event.sign < 0 ? 'LEFT TURN QUEUED' : 'RIGHT TURN QUEUED');
      beep(340, 0.045, 'triangle', 0.015);
    } else if (event.type === 'turn' && event.riderId === 'YOU') {
      beep(event.sign < 0 ? 560 : 720, 0.08, 'triangle', 0.035);
      if (event.nearMiss) {
        showToast('CLOSE CALL');
        beep(980, 0.14, 'sine', 0.025);
      }
    } else if (event.type === 'crash') {
      spawnDerez(event);
      if (event.riderId !== 'YOU') {
        showToast(event.riderId + ' DEREZZED  +250');
        beep(190, 0.18, 'sawtooth', 0.038);
        updateHud();
      }
    } else if (event.type === 'round-end') {
      finish(event);
    }
  }
}
function trailMesh(out,s){
  const dx=s.x2-s.x1,dz=s.z2-s.z1,len=Math.hypot(dx,dz);
  if(len<.06)return;
  const x=(s.x1+s.x2)/2,z=(s.z1+s.z2)/2;
  const sx=Math.abs(dx)>.01?len:.18,sz=Math.abs(dz)>.01?len:.18;
  const c=s.rider.color;
  box(out,x,.62,z,sx,1.22,sz,mixColor(c,.23));
  box(out,x,1.26,z,sx+.03,.065,sz+.03,c,2);
  box(out,x,.09,z,sx+.04,.085,sz+.04,mixColor(c,.62),2);
}
function bikePart(out,r,forward,side,y,w,h,length,color,kind=0){
  const d=DIR[r.dir],x=r.x+d[0]*forward-d[1]*side,z=r.z+d[1]*forward+d[0]*side;
  box(out,x,y,z,r.dir%2?length:w,h,r.dir%2?w:length,color,kind);
}
function bikeMesh(out,r){
  const c=r.color;
  bikePart(out,r,0,0,.45,.62,.38,2.05,[.02,.055,.08]);
  bikePart(out,r,.30,0,.68,.52,.16,1.25,mixColor(c,.83),2);
  bikePart(out,r,.95,0,.46,.49,.13,.23,c,2);
  bikePart(out,r,-.89,0,.44,.48,.13,.23,c,2);
  bikePart(out,r,0,0,.89,.42,.28,.47,[.025,.075,.10]);
  bikePart(out,r,-.02,0,1.07,.26,.11,.32,mixColor(c,.62),2);
  for(const side of [-1,1]){
    bikePart(out,r,.57,side*.38,.35,.24,.53,.20,[.006,.016,.025]);
    bikePart(out,r,-.58,side*.38,.35,.24,.53,.20,[.006,.016,.025]);
    bikePart(out,r,.57,side*.52,.35,.08,.13,.12,c,2);
    bikePart(out,r,-.58,side*.52,.35,.08,.13,.12,c,2);
    bikePart(out,r,0,side*.35,.24,.10,.09,1.48,mixColor(c,.55),2);
  }
  bikePart(out,r,-1.0,0,.8,.20,.47,.11,c,2);
}

function draw(renderDt) {
  if (graphicsLost) return;
  resize();
  const player = round.riders.find(r => r.isPlayer);
  if (!player) return;
  const direction = DIR[player.dir];
  const desired = [
    player.x - direction[0] * 14 - direction[1] * 5,
    10.2,
    player.z - direction[1] * 14 + direction[0] * 5,
  ];
  const aim = [
    player.x + direction[0] * 5,
    0.55,
    player.z + direction[1] * 5,
  ];
  const alpha = reducedMotion.matches ? 1 : 1 - Math.exp(-(mode === 'ready' ? 2.8 : 6) * renderDt);
  camera = camera.map((value, i) => value + (desired[i] - value) * alpha);
  target = target.map((value, i) => value + (aim[i] - value) * alpha);
  const matrix = multiply(
    perspective(Math.PI / 3, canvas.width / canvas.height, 0.1, 145),
    lookAt(camera, target),
  );
  gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
  gl.uniformMatrix4fv(loc.matrix, false, matrix);
  gl.uniform3fv(loc.camera, camera);
  gl.uniform1f(loc.time, visualTime);
  bind(staticBuffer);
  gl.drawArrays(gl.TRIANGLES, 0, staticData.length / 7);
  const dynamic = [];
  for (const segment of round.segments) trailMesh(dynamic, segment);
  for (const rider of round.riders) if (rider.alive) bikeMesh(dynamic, rider);
  for (const particle of particles) {
    const size = Math.max(0.025, 0.13 * (1 - particle.age / particle.life));
    box(dynamic, particle.x, Math.max(0.1, particle.y), particle.z,
      size, size, size, particle.color, 2);
  }
  gl.bindBuffer(gl.ARRAY_BUFFER, dynamicBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(dynamic), gl.DYNAMIC_DRAW);
  bind(dynamicBuffer);
  gl.drawArrays(gl.TRIANGLES, 0, dynamic.length / 7);
}

function drawRadar() {
  const ctx = radar.getContext('2d');
  if (!ctx) return;
  const width = radar.width;
  const height = radar.height;
  const scale = width / 64;
  const at = value => (value + 32) * scale;
  ctx.clearRect(0, 0, width, height);
  ctx.fillStyle = '#07121d';
  ctx.fillRect(0, 0, width, height);
  ctx.strokeStyle = 'rgba(78,220,243,.12)';
  ctx.lineWidth = 1;
  for (let i = 0; i <= 8; i++) {
    const p = i * width / 8;
    ctx.beginPath();
    ctx.moveTo(p, 0); ctx.lineTo(p, height);
    ctx.moveTo(0, p); ctx.lineTo(width, p);
    ctx.stroke();
  }
  ctx.strokeStyle = 'rgba(94,223,243,.5)';
  ctx.lineWidth = 3;
  ctx.strokeRect(at(-LIMIT), at(-LIMIT), LIMIT * 2 * scale, LIMIT * 2 * scale);
  for (const segment of round.segments) {
    const color = segment.rider.color;
    ctx.strokeStyle = 'rgba(' +
      Math.round(color[0] * 255) + ',' +
      Math.round(color[1] * 255) + ',' +
      Math.round(color[2] * 255) + ',.7)';
    ctx.lineWidth = 2.5;
    ctx.beginPath();
    ctx.moveTo(at(segment.x1), at(segment.z1));
    ctx.lineTo(at(segment.x2), at(segment.z2));
    ctx.stroke();
  }
  for (const rider of round.riders) {
    if (!rider.alive) continue;
    const color = rider.color;
    ctx.fillStyle = 'rgb(' +
      Math.round(color[0] * 255) + ',' +
      Math.round(color[1] * 255) + ',' +
      Math.round(color[2] * 255) + ')';
    if (rider.isPlayer) {
      ctx.save();
      ctx.translate(at(rider.x), at(rider.z));
      ctx.rotate(rider.dir * Math.PI / 2);
      ctx.beginPath();
      ctx.moveTo(0, -7);
      ctx.lineTo(5, 5);
      ctx.lineTo(-5, 5);
      ctx.closePath();
      ctx.fill();
      ctx.strokeStyle = '#ffffff';
      ctx.lineWidth = 1.7;
      ctx.stroke();
      ctx.restore();
    } else {
      ctx.beginPath();
      ctx.arc(at(rider.x), at(rider.z), 3.5, 0, Math.PI * 2);
      ctx.fill();
      ctx.strokeStyle = '#ffffff';
      ctx.lineWidth = 1;
      ctx.stroke();
    }
  }
}

function frame(now) {
  const realDt = lastFrame ? Math.min(Math.max((now - lastFrame) / 1000, 0), 0.25) : 0;
  lastFrame = now;
  if (!reducedMotion.matches && mode !== 'paused') visualTime += realDt;
  let launchedThisFrame = false;
  if (mode === 'countdown') {
    countdownRemaining -= realDt;
    const nextNumber = Math.ceil(countdownRemaining);
    if (nextNumber <= 0) {
      launch();
      launchedThisFrame = true;
    } else if (nextNumber !== countdownNumber) {
      countdownNumber = nextNumber;
      countdown.textContent = String(nextNumber);
      beep(440 + (3 - nextNumber) * 95, 0.08, 'triangle', 0.03);
    }
  }
  if (mode === 'running' && !launchedThisFrame) {
    accumulator += realDt;
    let steps = 0;
    while (accumulator >= STEP && steps < 16 && mode === 'running') {
      stepRound(round);
      handleEvents();
      accumulator -= STEP;
      steps++;
    }
    accumulator = Math.min(accumulator, STEP * 16);
  }
  if (mode !== 'paused') updateParticles(realDt);
  if (!graphicsLost) {
    draw(realDt);
    drawRadar();
  }
  requestAnimationFrame(frame);
}

overlay.addEventListener('keydown', event => {
  if (event.key === 'Tab' && !overlay.classList.contains('hidden')) {
    event.preventDefault();
    ui.start.focus({ preventScroll: true });
  }
});
ui.start.addEventListener('click', () => {
  if (graphicsLost) location.reload();
  else if (mode === 'paused') pause();
  else start();
});
ui.pause.addEventListener('click', pause);
ui.sound.addEventListener('click', () => {
  muted = !muted;
  writeLocal(MUTE_KEY, String(muted));
  syncSoundButton();
  if (!muted) beep(650);
});
if (touchToggle) {
  touchToggle.addEventListener('click', () => {
    const shown = touchChoice === 'show' ||
      (touchChoice === 'auto' && window.matchMedia('(any-pointer: coarse)').matches);
    touchChoice = shown ? 'hide' : 'show';
    writeLocal(TOUCH_KEY, touchChoice);
    syncTouchControls();
  });
}
window.matchMedia('(any-pointer: coarse)').addEventListener?.('change', syncTouchControls);
document.getElementById('left').addEventListener('pointerdown', event => {
  event.preventDefault();
  playerTurn(-1);
});
document.getElementById('right').addEventListener('pointerdown', event => {
  event.preventDefault();
  playerTurn(1);
});
canvas.addEventListener('pointerdown', () => {
  if (mode === 'countdown') launch();
});
window.addEventListener('keydown', event => {
  if (event.repeat) return;
  const interactive = event.target instanceof Element &&
    event.target.closest('button, input, select, textarea, a');
  const editing = event.target instanceof Element &&
    event.target.closest('input, select, textarea, [contenteditable]');
  const key = event.key.toLowerCase();
  if (mode === 'running' && !editing && (event.key === 'ArrowLeft' || key === 'a')) {
    event.preventDefault();
    playerTurn(-1);
  } else if (mode === 'running' && !editing && (event.key === 'ArrowRight' || key === 'd')) {
    event.preventDefault();
    playerTurn(1);
  } else if (key === 'p' || event.key === 'Escape') {
    event.preventDefault();
    pause();
  } else if (key === 'm' && !editing) {
    ui.sound.click();
  } else if (!interactive && (event.key === 'Enter' || event.key === ' ') && mode === 'countdown') {
    event.preventDefault();
    launch();
  } else if (!interactive && (event.key === 'Enter' || event.key === ' ') &&
      (mode === 'ready' || mode === 'won' || mode === 'lost')) {
    event.preventDefault();
    start();
  }
});
document.addEventListener('visibilitychange', () => {
  if (document.hidden && (mode === 'running' || mode === 'countdown')) pause();
});
canvas.addEventListener('webglcontextlost', event => {
  event.preventDefault();
  graphicsLost = true;
  generation++;
  mode = 'paused';
  accumulator = 0;
  hideCountdown();
  setOverlay('SIGNAL INTERRUPTED', 'GRID <span>OFFLINE<small>01</small></span>',
    'The graphics connection was lost. Wait for recovery or reload the game.', 'RELOAD GAME');
  announce('Graphics connection lost. Reload the game.');
});
canvas.addEventListener('webglcontextrestored', () => {
  announce('Graphics restored. Reloading the game.');
  location.reload();
});
window.addEventListener('resize', resize);

syncSoundButton();
syncTouchControls();
updateHud();
drawRadar();
setBackgroundInert(true);
ui.start.focus({ preventScroll: true });
requestAnimationFrame(frame);
} catch (error) {
  console.error('Grid graphics initialization failed:', error);
  showWebglError('The Grid could not start graphics. Try reloading or another browser.');
}
}
