import { createRound, createDuelRound, createArenaRound, DUEL_RIVALS, DIFFICULTIES, drainEvents, queueTurn, stepRound, STEP } from './simulation.js';
import { MAPS, getMap } from './maps.js';
import { createArenaMatch, recordArenaRound, nextArenaRound, arenaWon } from './arena.js';
import { createMatch, recordRound, nextRound, roundSeed } from './match.js';

const canvas = document.querySelector('#game');
const radar = document.querySelector('#radar');
const overlay = document.querySelector('#overlay');
const countdown = document.querySelector('#countdown');
const statusLive = document.querySelector('#status-live');
const touchControls = document.querySelector('.touch-controls');
const touchToggle = document.querySelector('#touch-toggle');
const setup = document.querySelector('#setup');
const lessonHint = document.querySelector('#lesson-hint');
const skipLesson = document.querySelector('#skip-lesson');
const modeSelect = document.querySelector('#mode-select');
const rivalSelect = document.querySelector('#rival-select');
const difficultySelect = document.querySelector('#difficulty-select');
const mapSelect = document.querySelector('#map-select');
const ruleSelect = document.querySelector('#rule-select');
const mapPreview = document.querySelector('#map-preview');
const mapTip = document.querySelector('#map-tip');
const volumeInput = document.querySelector('#volume');
const musicVolumeInput = document.querySelector('#music-volume');
const cameraSelect = document.querySelector('#camera-distance');
const cameraModeSelect = document.querySelector('#camera-mode');
const reduceFlashesInput = document.querySelector('#reduce-flashes');
const highlightCard = document.querySelector('#highlight-card');
const highlightLabel = document.querySelector('#highlight-label');
const skipHighlightButton = document.querySelector('#skip-highlight');
const qualitySelect = document.querySelector('#graphics-quality');
const ui = Object.fromEntries(
  ['score', 'best', 'rivals', 'eyebrow', 'title', 'deck', 'start', 'toast', 'sound', 'pause', 'matchline', 'rival-intent', 'rival-preview', 'lesson-start', 'change-mode']
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
uniform float uFlash;
uniform vec3 uGridColor;
uniform vec3 uFogColor;
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
    color+=uGridColor*line*glow+uGridColor*majorLine*0.72;
  }
  if(vKind>1.5) color*=1.12+uFlash*0.12*sin(uTime*4.0+vWorld.x*0.7+vWorld.z*0.5);
  float fog=smoothstep(22.0,85.0,vDistance);
  color=mix(color,uFogColor,fog*0.75);
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
  camera: gl.getUniformLocation(program, 'uCamera'), time: gl.getUniformLocation(program, 'uTime'),
  flash: gl.getUniformLocation(program, 'uFlash'),
  gridColor: gl.getUniformLocation(program, 'uGridColor'),
  fogColor: gl.getUniformLocation(program, 'uFogColor')
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
const MODE_KEY = 'tron-grid-run-mode';
const RIVAL_KEY = 'tron-grid-run-rival';
const DIFFICULTY_KEY = 'tron-grid-run-difficulty';
const MAP_KEY = 'tron-grid-run-map';
const RULE_KEY = 'tron-grid-run-rule';
const VOLUME_KEY = 'tron-grid-run-volume';
const MUSIC_VOLUME_KEY = 'tron-grid-run-music-volume';
const CAMERA_KEY = 'tron-grid-run-camera';
const CAMERA_MODE_KEY = 'tron-grid-run-camera-mode';
const REDUCE_FLASHES_KEY = 'tron-grid-run-reduce-flashes';
const QUALITY_KEY = 'tron-grid-run-quality';

function readLocal(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}
function writeLocal(key, value) {
  try { localStorage.setItem(key, value); } catch { /* Storage can be unavailable. */ }
}
const savedBest = Number(readLocal(SCORE_KEY));
const legacyBest = Number.isFinite(savedBest) && savedBest >= 0 ? savedBest : 0;
let best = 0;
let muted = readLocal(MUTE_KEY) === 'true';
let touchChoice = readLocal(TOUCH_KEY) || 'auto';
let sessionMode = 'duel';
let match = null;
let lessonStage = 0;
const storedVolume = Number(readLocal(VOLUME_KEY) ?? 70);
let volume = Number.isFinite(storedVolume) ? Math.max(0, Math.min(100, storedVolume)) : 70;
const storedMusicVolume = Number(readLocal(MUSIC_VOLUME_KEY) ?? 45);
let musicVolume = Number.isFinite(storedMusicVolume) ? Math.max(0, Math.min(100, storedMusicVolume)) : 45;
let cameraDistance = ['close', 'standard', 'far'].includes(readLocal(CAMERA_KEY)) ? readLocal(CAMERA_KEY) : 'standard';
let cameraMode = ['chase', 'tactical'].includes(readLocal(CAMERA_MODE_KEY)) ? readLocal(CAMERA_MODE_KEY) : 'chase';
let reduceFlashes = readLocal(REDUCE_FLASHES_KEY) === 'true';
let graphicsQuality = ['low', 'high'].includes(readLocal(QUALITY_KEY)) ? readLocal(QUALITY_KEY) : 'high';
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
let finishAge = 0;
let replayFrames = [];
let highlight = null;
let audio;
let effectBus;
let musicBus;
let nextMusicBeat = 0;
let musicStep = 0;
const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
const reducedFlashMedia = window.matchMedia('(prefers-reduced-motion: reduce)');
function flashReduced() { return reduceFlashes || reducedFlashMedia.matches; }
function syncFlashSetting() { document.body.classList.toggle('reduce-flashes', flashReduced()); }
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
let staticData = [];
const staticBuffer = gl.createBuffer();
function rebuildStatic(map) {
  const LIMIT = map.limit;
  C.floor = map.palette.floor;
  C.wall = map.palette.wall;
  staticData = [];
quad(staticData,[-LIMIT-3,0,-LIMIT-3],[LIMIT+3,0,-LIMIT-3],[LIMIT+3,0,LIMIT+3],[-LIMIT-3,0,LIMIT+3],C.floor,1);
for (const side of [-1, 1]) {
  // All scenery stays outside the playable bounds; the luminous top rail marks the real edge.
  box(staticData, side*LIMIT,.62,0,.34,1.24,LIMIT*2+.5,C.wall);
  box(staticData, 0,.62,side*LIMIT,LIMIT*2+.5,1.24,.34,C.wall);
  box(staticData, side*LIMIT,1.26,0,.18,.10,LIMIT*2+.5,map.palette.rail,2);
  box(staticData, 0,1.26,side*LIMIT,LIMIT*2+.5,.10,.18,map.palette.rail,2);
  for (let i=-3; i<=3; i++) {
    box(staticData,side*LIMIT,1.2,i*8,.48,2.4,.48,[.025,.18,.25]);
    box(staticData,i*8,1.2,side*LIMIT,.48,2.4,.48,[.025,.18,.25]);
    box(staticData,side*LIMIT,2.34,i*8,.5,.08,.5,[.13,.85,1],2);
    box(staticData,i*8,2.34,side*LIMIT,.5,.08,.5,[.13,.85,1],2);
  }
  // Three stepped spectator terraces frame the arena without obscuring its edge.
  for (let tier=0; tier<3; tier++) {
    const reach=LIMIT+1.5+tier*2.25;
    const height=1.05+tier*1.25;
    box(staticData,side*reach,height/2,0,1.9,height,LIMIT*2+9,[.012,.029,.048]);
    box(staticData,0,height/2,side*reach,LIMIT*2+9,height,1.9,[.012,.029,.048]);
    box(staticData,side*reach,height+.03,0,1.82,.06,LIMIT*2+9,[.025,.15,.20],2);
    box(staticData,0,height+.03,side*reach,LIMIT*2+9,.06,1.82,[.025,.15,.20],2);
  }
  // Original projection panels and sparse crowd lights, kept dimmer than live trails.
  for (let i=-2; i<=2; i++) {
    const z=i*11;
    box(staticData,side*(LIMIT+3.9),5.5,z,.30,4.4,7.0,[.025,.057,.077]);
    box(staticData,side*(LIMIT+3.70),5.5,z,.07,3.5,6.1,[.025,.14,.19],2);
    box(staticData,side*(LIMIT+3.60),5.5,z,.07,.12,5.6,[.08,.42,.52],2);
    for (let j=-2; j<=2; j++) {
      box(staticData,side*(LIMIT+2.45),3.0,z+j*1.2,.13,.10,.21,
        j%2 ? [.07,.23,.28] : [.10,.32,.38],2);
    }
  }
}
// Arena portal and upper architecture sit beyond the north collision boundary.
for (const side of [-1,1]) {
  box(staticData,side*5.1,3.5,-LIMIT-1.2,.95,7.0,1.05,[.025,.065,.09]);
  box(staticData,side*5.1,3.5,-LIMIT-.62,.20,6.4,.12,[.08,.57,.68],2);
  box(staticData,side*6.8,6.9,-LIMIT-2.2,1.2,9.5,1.2,[.012,.035,.06]);
}
box(staticData,0,7.1,-LIMIT-1.2,11.2,.85,1.05,[.023,.07,.10]);
box(staticData,0,6.76,-LIMIT-.65,10.4,.11,.12,[.10,.68,.79],2);
for (let i=-4; i<=4; i++) {
  box(staticData,i*2.7,8.9,-LIMIT-3.4,.09,2.7,.09,[.055,.29,.36],2);
}
for (const obstacle of map.obstacles) {
  const x=(obstacle.minX+obstacle.maxX)/2, z=(obstacle.minZ+obstacle.maxZ)/2;
  const sx=obstacle.maxX-obstacle.minX, sz=obstacle.maxZ-obstacle.minZ;
  box(staticData,x,.7,z,sx,1.4,sz,map.palette.obstacle);
  box(staticData,x,1.43,z,sx+.08,.07,sz+.08,map.palette.rail,2);
}
gl.bindBuffer(gl.ARRAY_BUFFER, staticBuffer);
gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(staticData), gl.STATIC_DRAW);
}
rebuildStatic(getMap('classic'));
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
  const dpr=Math.min(devicePixelRatio||1,graphicsQuality === 'low' ? 1 : 1.75),w=Math.round(innerWidth*dpr),h=Math.round(innerHeight*dpr);
  if(canvas.width!==w||canvas.height!==h){canvas.width=w;canvas.height=h;gl.viewport(0,0,w,h);}
}


function announce(message) {
  if (statusLive) statusLive.textContent = message;
}

function scoreKey() {
  return 'tron-grid-run-best:v2:' + sessionMode + ':' + round.map + ':' +
    round.difficulty + ':' + round.rule;
}
function loadBest() {
  const value = Number(readLocal(scoreKey()));
  best = Number.isFinite(value) && value >= 0 ? value : 0;
}
function updateHud() {
  const points = match && sessionMode !== 'lesson'
    ? match.points + (match.resultRecorded ? 0 : round.score) : round.score;
  ui.score.textContent = String(points).padStart(5, '0');
  if (sessionMode === 'duel' && match) {
    ui.matchline.textContent = 'DUEL / YOU ' + match.wins.player + ' ? ' + match.wins.rival +
      ' ' + DUEL_RIVALS[match.rival].id + ' / R' + match.roundNumber + ' / ' + getMap(round.map).name.toUpperCase();
  } else if (sessionMode === 'arena' && match) {
    ui.matchline.textContent = 'ARENA / HEAT ' + match.roundNumber + '/3 / SURVIVE 2 / ' +
      getMap(round.map).name.toUpperCase();
  } else if (sessionMode === 'lesson') ui.matchline.textContent = 'LESSON / GRID BASICS';
  else ui.matchline.textContent = sessionMode === 'duel' ? 'DUEL / FIRST TO 3' : 'ARENA / SURVIVE 2 OF 3';
  document.querySelector('.radar-wrap .label').textContent =
    getMap(round.map).name.toUpperCase() + ' / ' + (match?.roundNumber || '01');
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

function syncAudioMix() {
  if (!audio) return;
  const at=audio.currentTime;
  effectBus.gain.setTargetAtTime(muted ? 0 : volume/100,at,.025);
  const active=mode==='running' || mode==='countdown';
  musicBus.gain.setTargetAtTime(muted || !active ? 0 : musicVolume/100,at,.07);
}
function ensureAudio() {
  if (!audio) {
    try {
      audio = new (window.AudioContext || window.webkitAudioContext)();
      effectBus=audio.createGain();
      musicBus=audio.createGain();
      effectBus.connect(audio.destination);
      musicBus.connect(audio.destination);
      syncAudioMix();
    } catch {
      audio=null;
      muted=true;
      syncSoundButton();
    }
  }
}
function activateAudio() {
  if (muted || (volume===0 && musicVolume===0)) return;
  ensureAudio();
  if (audio?.state==='suspended') audio.resume().catch(()=>{});
}
function synth(bus,freq,at,duration,type='triangle',gain=.025,finishFreq=freq) {
  if (!audio || !bus || gain<=0) return;
  const oscillator=audio.createOscillator();
  const envelope=audio.createGain();
  oscillator.type=type;
  oscillator.frequency.setValueAtTime(freq,at);
  oscillator.frequency.exponentialRampToValueAtTime(Math.max(40,finishFreq),at+duration);
  envelope.gain.setValueAtTime(.0001,at);
  envelope.gain.exponentialRampToValueAtTime(Math.max(.0002,gain),at+.012);
  envelope.gain.exponentialRampToValueAtTime(.0001,at+duration);
  oscillator.connect(envelope).connect(bus);
  oscillator.start(at);
  oscillator.stop(at+duration+.012);
}
function beep(freq,duration=.11,type='sawtooth',gain=.035) {
  if (muted || volume===0) return;
  ensureAudio();
  if (!audio) return;
  if (audio.state==='suspended') audio.resume().catch(()=>{});
  synth(effectBus,freq,audio.currentTime,duration,type,gain,freq*.57);
}
function effectMotif(kind) {
  if (muted || volume===0) return;
  ensureAudio();
  if (!audio) return;
  if (audio.state==='suspended') audio.resume().catch(()=>{});
  const at=audio.currentTime;
  if(kind==='turn') {
    synth(effectBus,520,at,.08,'triangle',.025,780);
  } else if(kind==='wall') {
    synth(effectBus,180,at,.15,'sine',.017,110);
  } else if(kind==='near') {
    synth(effectBus,760,at,.13,'sine',.028,1080);
    synth(effectBus,1020,at+.09,.16,'triangle',.020,1350);
  } else if(kind==='crash') {
    synth(effectBus,170,at,.24,'sawtooth',.047,55);
  } else if(kind==='derez') {
    synth(effectBus,710,at,.27,'triangle',.026,70);
  } else if(kind==='win') {
    [392,523,659].forEach((note,i)=>synth(effectBus,note,at+i*.085,.19,'triangle',.021,note));
  }
}
function musicIntensity() {
  if(mode==='countdown') return .22;
  const player=round.riders.find(r=>r.isPlayer);
  const nearest=player ? Math.min(20,...round.riders.filter(r=>!r.isPlayer&&r.alive)
    .map(r=>Math.hypot(r.x-player.x,r.z-player.z))) : 20;
  const pressure=Math.max(0,1-nearest/16);
  const duration=Math.min(1,round.elapsed/38);
  const stakes=sessionMode==='duel' && match
    ? Math.min(1,(match.wins.player+match.wins.rival)/4) : 0;
  return Math.min(1,.25+duration*.20+pressure*.35+stakes*.20);
}
function updateMusic() {
  if (!audio || audio.state!=='running' || muted || musicVolume===0 ||
      (mode!=='running' && mode!=='countdown')) return;
  const at=audio.currentTime;
  if(nextMusicBeat<at-.15) nextMusicBeat=at+.025;
  let queued=0;
  while(nextMusicBeat<at+.14 && queued++<3) {
    const intensity=musicIntensity();
    const step=musicStep++%16;
    const bass=[55,55,65.41,49][Math.floor(step/4)];
    const beatLength=60/(100+intensity*34)/2;
    // An original, sparse sequencer: sub pulse, minor arpeggio and airy upper tone.
    if(step%2===0) synth(musicBus,bass,nextMusicBeat,.27,'sine',.066+intensity*.025,bass*.88);
    if(step%4===2) synth(musicBus,bass*2,nextMusicBeat,.16,'triangle',.015+intensity*.013,bass*2.04);
    if(step%4===0 || (intensity>.65 && step%4===3)) {
      const notes=[220,261.63,329.63,293.66];
      const note=notes[Math.floor(step/4)];
      synth(musicBus,note,nextMusicBeat+.015,.19,'triangle',.012+intensity*.014,note*1.02);
    }
    if(step===0 || step===8) synth(musicBus,440,nextMusicBeat,.75,'sine',.006+intensity*.006,392);
    nextMusicBeat+=beatLength;
  }
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
  syncAudioMix();
  countdown.textContent = 'GO';
  countdown.classList.add('show');
  accumulator = 0;
  lastFrame = performance.now();
  beep(730, 0.2, 'triangle', 0.06);
  effectMotif('wall');
  announce('Go. Survive the Grid.');
  const token = generation;
  setTimeout(() => { if (token === generation && mode === 'running') hideCountdown(); }, 420);
}

function drawMapPreview() {
  const map = getMap(mapSelect.value);
  const ctx = mapPreview.getContext('2d');
  const size = mapPreview.height;
  const scale = (size - 14) / (map.limit * 2);
  const at = value => size / 2 + value * scale;
  ctx.fillStyle = '#06131d';
  ctx.fillRect(0, 0, mapPreview.width, size);
  ctx.strokeStyle = map.palette.radar;
  ctx.lineWidth = 2;
  ctx.strokeRect(at(-map.limit), at(-map.limit), map.limit * 2 * scale, map.limit * 2 * scale);
  ctx.fillStyle = map.palette.radar;
  for (const o of map.obstacles)
    ctx.fillRect(at(o.minX), at(o.minZ), (o.maxX-o.minX)*scale, (o.maxZ-o.minZ)*scale);
  map.spawns.arena.forEach((spawn, index) => {
    ctx.fillStyle = index === 0 ? '#73eaff' : ['#ffa353','#c78aff','#ffdf72'][index-1];
    ctx.beginPath(); ctx.arc(at(spawn.x),at(spawn.z),3.5,0,Math.PI*2); ctx.fill();
  });
  ctx.fillStyle = '#b9dce6';
  ctx.font = '600 12px Rajdhani, sans-serif';
  ctx.fillText(map.name.toUpperCase(), size + 12, 23);
  ctx.font = '12px Rajdhani, sans-serif';
  ctx.fillText(map.obstacles.length ? map.obstacles.length + ' fixed barriers' : 'Open circuit', size + 12, 45);
  ctx.fillText('CYAN = YOU', size + 12, 68);
  mapTip.textContent = map.cue + (ruleSelect.value === 'decay' && modeSelect.value === 'arena'
    ? ' Advanced: closed walls pulse before fading after 10 seconds.' : ' Light walls remain until the heat ends.') +
    (legacyBest ? ' Legacy best ' + legacyBest + ' is preserved.' : '');
}
function syncSetup() {
  const duel = modeSelect.value === 'duel';
  document.querySelector('#rival-field').hidden = !duel;
  document.querySelector('#rule-field').hidden = duel;
  const profile = DUEL_RIVALS[rivalSelect.value];
  const difficultyHint = {
    rookie: 'Reacts later; speed stays equal.',
    standard: 'Balanced reactions; speed stays equal.',
    expert: 'Reacts sooner; speed stays equal.',
  };
  ui['rival-preview'].textContent = duel
    ? profile.id + ' / ' + profile.style.toUpperCase() + ' ? ' + profile.cue + '. ' + difficultyHint[difficultySelect.value]
    : 'Three different AI styles per heat. Survive at least 2 of 3 heats; each heat adds to your score.';
  ui.start.textContent = duel ? 'ENTER DUEL' : 'ENTER ARENA';
  drawMapPreview();
  if (mode === 'ready') {
    sessionMode = modeSelect.value;
    round = duel
      ? createDuelRound({ seed: 1, rival: rivalSelect.value, difficulty: difficultySelect.value, map: mapSelect.value })
      : createArenaRound({ seed: 1, difficulty: difficultySelect.value, map: mapSelect.value, rule: ruleSelect.value });
    rebuildStatic(getMap(round.map));
    loadBest();
    updateHud();
    drawRadar();
  }
}

function showMenu() {
  generation++;
  match = null;
  sessionMode = modeSelect.value;
  mode = 'ready';
  syncAudioMix();
  round = createRound({ seed: 1 });
  replayFrames=[];
  highlight=null;
  highlightCard.hidden=true;
  particles = [];
  turnLean.clear();
  hideCountdown();
  lessonHint.hidden = true;
  skipLesson.hidden = true;
  setup.hidden = false;
  ui['lesson-start'].hidden = false;
  ui['change-mode'].hidden = true;
  ui['rival-intent'].textContent = '';
  updateHud();
  syncSetup();
  setOverlay('CHOOSE YOUR CIRCUIT', 'ENTER THE <span>GRID<small>01</small></span>',
    'Duel is first to three against one rival. Arena is three scored heats: survive at least two. Every turn leaves a lethal wall.', ui.start.textContent);
  announce('Choose Duel or Arena, then enter the Grid. A short playable lesson is available.');
}

function showLessonStep(message) {
  lessonHint.hidden = false;
  lessonHint.textContent = message;
  announce(message);
}

function startLesson() {
  generation++;
  sessionMode = 'lesson';
  match = null;
  lessonStage = 0;
  beginRound();
}

function beginRound() {
  generation++;
  round = sessionMode === 'duel'
    ? createDuelRound({ seed: roundSeed(match), rival: match.rival, difficulty: match.difficulty, map: match.map })
    : sessionMode === 'lesson'
      ? createRound({ seed: randomSeed(), riders: [{ id: 'YOU', x: 0, z: 16, dir: 0, isPlayer: true, controller: 'none' }] })
      : createArenaRound({ seed: roundSeed(match), difficulty: match.difficulty, map: match.map, rule: match.rule });
  rebuildStatic(getMap(round.map));
  loadBest();
  replayFrames=[];
  highlight=null;
  highlightCard.hidden=true;
  captureReplayFrame(true);
  mode = 'countdown';
  countdownRemaining = match && match.roundNumber > 1 ? 1.5 : 3;
  countdownNumber = Math.ceil(countdownRemaining);
  accumulator = 0;
  lastFrame = performance.now();
  camera = [7, 13, 31];
  target = [0, 0, 0];
  particles = [];
  turnLean.clear();
  nextMusicBeat=0;
  ++toastSequence;
  ui.toast.classList.remove('show');
  ui.pause.textContent = '?';
  ui.pause.setAttribute('aria-label', 'Pause game');
  setup.hidden = true;
  ui['lesson-start'].hidden = true;
  ui['change-mode'].hidden = true;
  ui['rival-intent'].textContent = sessionMode === 'duel' ? DUEL_RIVALS[match.rival].cue :
    sessionMode === 'arena' ? getMap(round.map).cue : '';
  lessonHint.hidden = sessionMode !== 'lesson';
  skipLesson.hidden = sessionMode !== 'lesson';
  if (sessionMode === 'lesson') showLessonStep('Step 1 / Turn left or right to carve a light wall.');
  updateHud();
  drawRadar();
  hideOverlay();
  countdown.classList.add('show');
  countdown.textContent = String(countdownNumber);
  activateAudio();
  beep(440, 0.08, 'triangle', 0.03);
  syncAudioMix();
  announce(sessionMode === 'lesson' ? 'Lesson starting. Turn left or right.' : 'Round starting. Three, two, one.');
}

function start() {
  if (mode === 'ready' || match?.completed) {
    sessionMode = modeSelect.value;
    match = sessionMode === 'duel'
      ? createMatch({ seed: randomSeed(), rival: rivalSelect.value, difficulty: difficultySelect.value })
      : createArenaMatch({ seed: randomSeed(), map: mapSelect.value,
          difficulty: difficultySelect.value, rule: ruleSelect.value });
    if (sessionMode === 'duel') match.map = mapSelect.value;
  } else if (match?.resultRecorded) {
    if (sessionMode === 'duel') nextRound(match);
    else nextArenaRound(match);
  }
  beginRound();
}
function pause() {
  if (graphicsLost) return;
  if (mode === 'running' || mode === 'countdown') {
    pausedFrom = mode;
    mode = 'paused';
    accumulator = 0;
    syncAudioMix();
    countdown.classList.remove('show');
    ui.pause.textContent = '▶';
    ui.pause.setAttribute('aria-label', 'Resume game');
    setOverlay('CONNECTION HELD', 'GRID <span>PAUSED<small>01</small></span>',
      'The Grid is waiting. Resume when you are ready.', 'RESUME');
    announce('Game paused.');
  } else if (mode === 'paused') {
    mode = pausedFrom;
    nextMusicBeat=0;
    syncAudioMix();
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

function spawnImpact(event) {
  if(reducedMotion.matches || flashReduced()) return;
  for(let i=0;i<9;i++) {
    const angle=i*Math.PI*2/9;
    particles.push({
      x:event.x,y:.48,z:event.z,
      vx:Math.cos(angle)*3.4,vy:2.5+(i%3)*.45,vz:Math.sin(angle)*3.4,
      age:0,life:.24+(i%3)*.055,color:[1,.85,.45],
    });
  }
}
function spawnDerez(event) {
  if (reducedMotion.matches) return;
  const rider = round.riders.find(r => r.id === event.riderId);
  const color = rider?.color || C.player;
  for (let i = 0; i < (flashReduced() ? 6 : 15); i++) {
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

function scoreExplanation() {
  const labels = { derez: 'rival derez', closeCall: 'close escape', trap: 'clean trap', win: 'round win' };
  const parts = Object.entries(round.scoreBreakdown)
    .filter(([, points]) => points > 0)
    .map(([kind, points]) => labels[kind] + ' +' + points);
  return parts.length ? parts.join(' · ') : 'No score this round';
}

function finishLesson(success, outcome) {
  generation++;
  mode = success ? 'lesson-complete' : 'lesson-lost';
  syncAudioMix();
  accumulator = 0;
  lessonHint.hidden = true;
  skipLesson.hidden = true;
  ui['change-mode'].hidden = false;
  const message = success
    ? 'You turned, built a light wall, and stayed clear of it. You are ready for Duel.'
    : crashMessage(outcome || round.outcome) + ' Your own trail stays dangerous. Try another route.';
  setOverlay(success ? 'LESSON COMPLETE' : 'LESSON RESET',
    success ? 'READY FOR <span>THE GRID</span>' : 'STAY <span>ALIVE</span>',
    message, success ? 'PLAY AGAIN' : 'TRY LESSON');
  announce(message);
}

function captureReplayFrame(force=false) {
  if(!force && round.tick%12!==0) return;
  const riderMap=new Map(round.riders.map(r=>[r.id,{
    id:r.id,x:r.x,z:r.z,dir:r.dir,alive:r.alive,isPlayer:r.isPlayer,
    style:r.style,color:r.color,
  }]));
  replayFrames.push({
    time:round.elapsed,
    riders:[...riderMap.values()],
    segments:round.segments.map(segment=>({
      id:segment.id,x1:segment.x1,z1:segment.z1,x2:segment.x2,z2:segment.z2,
      active:segment.active,closedAt:segment.closedAt,rider:riderMap.get(segment.ownerId),
    })),
  });
  while(replayFrames.length>2 && replayFrames[0].time<round.elapsed-6)
    replayFrames.shift();
}
function showRoundResult(result) {
  mode=result.event.kind;
  highlight=null;
  highlightCard.hidden=true;
  hideCountdown();
  setup.hidden=true;
  ui['change-mode'].hidden=false;
  const complete=match.completed;
  const success=complete ? result.matchWon : result.won;
  setOverlay(
    complete ? success ? (result.duel ? 'DUEL WON' : 'ARENA CLEARED') : (result.duel ? 'DUEL LOST' : 'ARENA FAILED')
      : result.won ? 'HEAT WON' : 'HEAT LOST',
    complete ? success ? 'GRID <span>MASTER</span>' : 'END OF <span>LINE</span>'
      : result.won ? 'ROUND <span>SECURED</span>' : 'ROUND <span>LOST</span>',
    result.deck,
    complete ? 'FAST RETRY' : 'NEXT ROUND',
  );
}
function completeHighlight() {
  if(mode!=='highlight' || !highlight) return;
  const result=highlight.result;
  showRoundResult(result);
}
function finish(event) {
  if (sessionMode === 'lesson') { finishLesson(false, event); return; }
  mode=event.kind;
  syncAudioMix();
  accumulator=0;
  finishAge=0;
  captureReplayFrame(true);
  const duel=sessionMode==='duel';
  ui['rival-intent'].textContent='';
  if(duel) recordRound(match,event.kind,round.score,round.actionLog);
  else recordArenaRound(match,event.kind,round.score,round.actionLog);
  const total=match.points;
  if(total>best) {
    best=total;
    writeLocal(scoreKey(),String(best));
  } else if (match.completed && readLocal(scoreKey()) === null) {
    writeLocal(scoreKey(), String(total));
  }
  updateHud();
  const won=event.kind==='won';
  if(won) effectMotif('win');
  const cause=won
    ? duel ? DUEL_RIVALS[match.rival].id+' derezzed.' : 'All rivals derezzed.'
    : crashMessage(event);
  const scoreText='Heat +'+round.score+' ('+scoreExplanation()+').';
  const scoreline=duel
    ? ' Duel '+match.wins.player+'?'+match.wins.rival+'.'
    : ' Arena '+match.wins.player+' of '+match.roundNumber+' heats survived.';
  const summary=match.completed
    ? duel ? ' Match total '+total+' points.'
      : ' Three-heat total '+total+' points. '+(arenaWon(match) ? 'Objective complete.' : 'Survive two heats to clear Arena.')
    : '';
  const tip=won ? 'Your route held.' : event.reason==='trail'
    ? 'Look for open space before your next turn.' : 'Leave room to escape at the arena edge.';
  const deck=cause+' '+scoreText+scoreline+summary+' '+tip;
  const result={event,deck,won,duel,matchWon:duel ? match.wins.player>=3 : arenaWon(match)};
  announce((won ? 'Round won. ' : 'Round lost. ')+deck);
  if(!won && !reducedMotion.matches && replayFrames.length>2) {
    highlight={
      frames:replayFrames.slice(),index:0,elapsed:0,duration:6,
      event:{...event,x:round.riders.find(r=>r.isPlayer)?.x??0,
        z:round.riders.find(r=>r.isPlayer)?.z??0},
      result,impactShown:false,
    };
    particles=[];
    turnLean.clear();
    mode='highlight';
    highlightLabel.textContent='CRASH REPLAY / '+cause;
    highlightCard.hidden=false;
    setBackgroundInert(true);
    skipHighlightButton.focus({preventScroll:true});
    announce('Crash replay. '+cause+' Skip replay to continue.');
  } else {
    const token=generation;
    setTimeout(()=>{
      if(token===generation && mode===event.kind) showRoundResult(result);
    },reducedMotion.matches ? 0 : 650);
  }
}
function handleEvents() {
  for (const event of drainEvents(round)) {
    if (event.type==='turn') turnLean.set(event.riderId,{value:event.sign*.27,age:0});
    if (event.type === 'turn-queued' && event.riderId === 'YOU') {
      showToast(event.sign < 0 ? 'LEFT TURN QUEUED' : 'RIGHT TURN QUEUED');
      beep(340, 0.045, 'triangle', 0.015);
    } else if (event.type === 'turn' && event.riderId === 'YOU') {
      effectMotif('turn');
      effectMotif('wall');
      if (event.nearMiss) {
        showToast('CLOSE CALL');
        effectMotif('near');
      }
      if (sessionMode === 'lesson' && lessonStage < 2) {
        lessonStage++;
        showLessonStep(lessonStage === 1
          ? 'Step 2 / Your glowing wall stays behind you. Turn once more.'
          : 'Step 3 / Keep clear of every wall until the lesson ends.');
      }
    } else if (event.type === 'ai-intent' && sessionMode === 'duel') {
      const side = event.sign < 0 ? 'left' : 'right';
      ui['rival-intent'].textContent = event.riderId + ' / ' + event.style.toUpperCase() + ' / turning ' + side;
    } else if (event.type === 'skill') {
      showToast(event.kind === 'clean-trap' ? 'CLEAN TRAP +' + event.points : 'CLOSE ESCAPE +' + event.points);
      updateHud();
    } else if (event.type === 'crash') {
      spawnImpact(event);
      spawnDerez(event);
      effectMotif('crash');
      effectMotif('derez');
      if (event.riderId !== 'YOU') {
        showToast(event.riderId + ' DEREZZED +250');
        updateHud();
      }
    } else if (event.type === 'round-end') {
      finish(event);
    }
  }
}function trailMesh(out,s,elapsed){
  const dx=s.x2-s.x1,dz=s.z2-s.z1,len=Math.hypot(dx,dz);
  if(len<.06)return;
  const x=(s.x1+s.x2)/2,z=(s.z1+s.z2)/2;
  const sx=Math.abs(dx)>.01?len:.18,sz=Math.abs(dz)>.01?len:.18;
  const remaining=round.rule==='decay' && !s.active
    ? s.closedAt + round.trailLifetime - elapsed : Infinity;
  const warning=remaining<=2;
  const c=warning ? [1,.91,.48] : s.rider.color;
  if (warning && Math.floor(elapsed*4)%2===0) {
    box(out,x,.62,z,sx,1.22,sz,mixColor(c,.10));
    box(out,x,1.26,z,sx+.03,.065,sz+.03,c,2);
    return;
  }
  box(out,x,.62,z,sx,1.22,sz,mixColor(c,.23));
  box(out,x,1.26,z,sx+.03,.065,sz+.03,c,2);
  box(out,x,.09,z,sx+.04,.085,sz+.04,mixColor(c,.62),2);
  if (s.rider.style === 'hunter') box(out,x,.84,z,sx+.02,.045,sz+.02,c,2);
  if (s.rider.style === 'survivor') box(out,x,.55,z,sx+.03,.055,sz+.03,mixColor(c,.7),2);
  if (s.rider.style === 'trapper') {
    box(out,x,.4,z,sx+.03,.045,sz+.03,mixColor(c,.8),2);
    box(out,x,.9,z,sx+.03,.045,sz+.03,mixColor(c,.8),2);
  }
  if(s.active) {
    // The bright advancing edge makes wall formation visible without a screen flash.
    box(out,s.x2,.68,s.z2,.28,1.34,.28,mixColor(c,.88),2);
  }
}
const turnLean = new Map();
function bikePoint(r, forward, side, y, lean=0) {
  const d=DIR[r.dir];
  const shifted=side+(y>.48 ? lean*(y-.48) : 0);
  return [r.x+d[0]*forward-d[1]*shifted,y,r.z+d[1]*forward+d[0]*shifted];
}
function bikeFace(out,r,points,color,kind=0,lean=0) {
  const world=points.map(p=>bikePoint(r,p[0],p[1],p[2],lean));
  for(let i=1;i<world.length-1;i++) {
    vertex(out,world[0],color,kind);
    vertex(out,world[i],color,kind);
    vertex(out,world[i+1],color,kind);
  }
}
function bikeWheel(out,r,forward,color) {
  const segments=10, radius=.35, centerY=.39;
  const dark=[.006,.018,.029];
  for(let i=0;i<segments;i++) {
    const a=i*Math.PI*2/segments,b=(i+1)*Math.PI*2/segments;
    const edge=(angle,side,rad)=>[forward+Math.sin(angle)*rad,side,centerY+Math.cos(angle)*rad];
    // Hollow luminous ring and dark recessed hub; the wheel reads in silhouette.
    for(const side of [-.22,.22]) {
      bikeFace(out,r,[edge(a,side,radius),edge(b,side,radius),
        edge(b,side,radius-.075),edge(a,side,radius-.075)],color,2);
      bikeFace(out,r,[edge(a,side,radius-.075),edge(b,side,radius-.075),
        edge(b,side,.12),edge(a,side,.12)],dark);
    }
    bikeFace(out,r,[edge(a,-.22,radius),edge(a,.22,radius),
      edge(b,.22,radius),edge(b,-.22,radius)],mixColor(color,.38),2);
  }
}
function bikeMesh(out,r) {
  const c=r.color, lean=reducedMotion.matches ? 0 : turnLean.get(r.id)?.value||0;
  const graphite=[.025,.050,.070], armor=[.045,.105,.135];
  // A small ground marker corresponds to the simulation's 0.3-unit rider radius.
  for(let i=0;i<8;i++) {
    const a=i*Math.PI/4,b=(i+1)*Math.PI/4;
    bikeFace(out,r,[[Math.cos(a)*.30,Math.sin(a)*.30,.025],
      [Math.cos(b)*.30,Math.sin(b)*.30,.025],
      [Math.cos(b)*.23,Math.sin(b)*.23,.025],
      [Math.cos(a)*.23,Math.sin(a)*.23,.025]],mixColor(c,.33),2);
  }
  bikeWheel(out,r,.82,c);
  bikeWheel(out,r,-.78,c);
  // Angular, tapered cycle shell; light seams identify its actual heading.
  bikeFace(out,r,[[-1.12,-.31,.43],[.80,-.34,.45],[1.29,0,.53],
    [.80,.34,.45],[-1.12,.31,.43]],graphite);
  bikeFace(out,r,[[-1.12,-.31,.43],[-.79,-.28,.79],[.65,-.29,.76],
    [1.29,0,.53],[.80,-.34,.45]],armor);
  bikeFace(out,r,[[-1.12,.31,.43],[-.79,.28,.79],[.65,.29,.76],
    [1.29,0,.53],[.80,.34,.45]],armor);
  bikeFace(out,r,[[-.79,-.28,.79],[-.31,-.23,.92],[.58,0,.84],
    [-.31,.23,.92],[-.79,.28,.79]],mixColor(c,.26));
  for(const side of [-1,1]) {
    bikeFace(out,r,[[-1.04,side*.32,.50],[.60,side*.30,.78],
      [.93,side*.19,.62],[-.76,side*.33,.59]],mixColor(c,.72),2);
    bikeFace(out,r,[[-.93,side*.34,.40],[.72,side*.35,.43],
      [.98,side*.17,.54],[-.68,side*.35,.51]],mixColor(c,.35),2);
  }
  bikeFace(out,r,[[.72,-.18,.83],[1.33,0,.58],[.72,.18,.83]],c,2);
  bikeFace(out,r,[[.96,-.075,.80],[1.27,0,.69],[.96,.075,.80]],
    [.91,.98,1],2);
  // Rider: faceted torso, helmet, visor, and luminous suit lines.
  const torso=[-.37,0,1.30], waist=[-.55,0,.88];
  bikeFace(out,r,[[waist[0],-.18,waist[2]],[-.25,-.23,1.17],
    [-.03,-.16,1.40],[-.22,0,1.48],[-.52,0,1.15]],graphite,0,lean);
  bikeFace(out,r,[[waist[0],.18,waist[2]],[-.25,.23,1.17],
    [-.03,.16,1.40],[-.22,0,1.48],[-.52,0,1.15]],graphite,0,lean);
  bikeFace(out,r,[[-.25,-.23,1.17],[-.03,-.16,1.40],
    [-.03,.16,1.40],[-.25,.23,1.17]],mixColor(c,.16),0,lean);
  bikeFace(out,r,[[-.27,0,1.51],[-.13,-.18,1.47],
    [.05,-.15,1.63],[.10,0,1.69],[.05,.15,1.63],
    [-.13,.18,1.47]],armor,0,lean);
  bikeFace(out,r,[[.03,-.15,1.60],[.12,0,1.62],[.03,.15,1.60],
    [-.02,0,1.55]],mixColor(c,.85),2,lean);
  for(const side of [-1,1]) {
    bikeFace(out,r,[[-.44,side*.19,.96],[-.19,side*.24,1.30],
      [-.14,side*.24,1.34],[-.36,side*.20,1.02]],c,2,lean);
    bikeFace(out,r,[[-.13,side*.20,1.32],[.28,side*.24,1.03],
      [.37,side*.23,.99],[.02,side*.19,1.37]],graphite,0,lean);
    bikeFace(out,r,[[-.07,side*.23,1.33],[.29,side*.26,1.05],
      [.23,side*.26,1.00],[-.12,side*.22,1.27]],mixColor(c,.65),2,lean);
  }
  // Silhouette language: Vanta forks forward, Aegis shields the rider,
  // Knot carries split tail vanes, and the player has a single spine.
  if(r.style==='hunter') for(const side of [-1,1]) {
    bikeFace(out,r,[[.55,side*.34,.64],[1.37,side*.44,.65],
      [1.13,side*.33,.93],[.56,side*.30,.79]],c,2);
  }
  else if(r.style==='survivor') {
    bikeFace(out,r,[[.30,-.55,.85],[.72,-.47,1.02],
      [.72,.47,1.02],[.30,.55,.85]],mixColor(c,.66),2);
    bikeFace(out,r,[[.30,-.55,.85],[.30,-.40,1.14],
      [.72,-.31,1.23],[.72,-.47,1.02]],armor);
    bikeFace(out,r,[[.30,.55,.85],[.30,.40,1.14],
      [.72,.31,1.23],[.72,.47,1.02]],armor);
  }
  else if(r.style==='trapper') for(const side of [-1,1]) {
    bikeFace(out,r,[[-1.23,side*.46,.36],[-.52,side*.31,.72],
      [-1.05,side*.36,1.08],[-1.38,side*.48,.86]],c,2);
  }
  else bikeFace(out,r,[[-1.18,0,.75],[-.30,0,.99],
    [.36,0,1.03],[.91,0,.84]],c,2);
}
function gateMesh(out) {
  const duration=match?.roundNumber>1 ? 1.5 : 3;
  const progress=mode==='ready' ? 0 : reducedMotion.matches ? 1 : mode==='countdown'
    ? Math.max(0,Math.min(1,1-countdownRemaining/duration)) : 1;
  const LIMIT=round.limit;
  for(const side of [-1,1]) {
    const x=side*(1.48+progress*2.30);
    box(out,x,3.35,-LIMIT-1.15,2.8,5.45,.28,[.016,.056,.073]);
    box(out,x-side*1.32,3.35,-LIMIT-.94,.075,5.20,.08,getMap(round.map).palette.rail,2);
    box(out,x,5.89,-LIMIT-.94,2.55,.07,.08,getMap(round.map).palette.rail,2);
  }
}
function displayRound() {
  return mode === 'highlight' && highlight
    ? highlight.frames[highlight.index] : round;
}
function cameraPose(player) {
  const direction=DIR[player.dir];
  const distance=cameraDistance==='close' ? 11 : cameraDistance==='far' ? 18 : 14;
  let desired, aim;
  if(cameraMode==='tactical' || mode==='highlight') {
    desired=[player.x-direction[0]*8-direction[1]*2,
      cameraDistance==='far' ? 30 : cameraDistance==='close' ? 23 : 27,
      player.z-direction[1]*8+direction[0]*2];
    aim=[player.x+direction[0]*3,.2,player.z+direction[1]*3];
  } else {
    desired=[player.x-direction[0]*distance-direction[1]*5,10.2,
      player.z-direction[1]*distance+direction[0]*5];
    aim=[player.x+direction[0]*5,.55,player.z+direction[1]*5];
  }
  if(mode==='countdown' && !reducedMotion.matches) {
    const duration=match?.roundNumber>1 ? 1.5 : 3;
    const intro=Math.max(0,Math.min(1,(countdownRemaining-.45)/(duration-.45)));
    const side=[player.x-direction[0]*7-direction[1]*15,8.3,
      player.z-direction[1]*7+direction[0]*15];
    desired=desired.map((value,i)=>value*(1-intro)+side[i]*intro);
    aim=aim.map((value,i)=>value*(1-intro)+[player.x,1,player.z][i]*intro);
  }
  if((mode==='won'||mode==='lost') && !reducedMotion.matches) {
    const framing=Math.max(0,1-finishAge/1.2);
    desired=[desired[0]-direction[1]*4*framing,desired[1]+2*framing,
      desired[2]+direction[0]*4*framing];
    aim=[player.x,.65,player.z];
  }
  return {desired,aim};
}
function draw(renderDt) {
  if (graphicsLost) return;
  resize();
  const scene=displayRound();
  const player=scene.riders.find(r=>r.isPlayer);
  if(!player) return;
  const {desired,aim}=cameraPose(player);
  const alpha=reducedMotion.matches ? 1 : 1-Math.exp(-(mode==='ready'?2.8:6)*renderDt);
  camera=camera.map((value,i)=>value+(desired[i]-value)*alpha);
  target=target.map((value,i)=>value+(aim[i]-value)*alpha);
  const matrix = multiply(
    perspective(Math.PI / 3, canvas.width / canvas.height, 0.1, 145),
    lookAt(camera, target),
  );
  gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
  gl.uniformMatrix4fv(loc.matrix, false, matrix);
  gl.uniform3fv(loc.camera, camera);
  gl.uniform1f(loc.time, visualTime);
  gl.uniform1f(loc.flash, flashReduced() || graphicsQuality === 'low' ? 0 : 1);
  const palette=getMap(round.map).palette;
  gl.uniform3fv(loc.gridColor,palette.rail.map(value=>value*.23));
  gl.uniform3fv(loc.fogColor,palette.floor);
  bind(staticBuffer);
  gl.drawArrays(gl.TRIANGLES, 0, staticData.length / 7);
  const dynamic = [];
  gateMesh(dynamic);
  for (const segment of scene.segments) trailMesh(dynamic, segment, scene.time ?? scene.elapsed);
  for (const rider of scene.riders) if (rider.alive) bikeMesh(dynamic, rider);
  if(mode==='highlight' && highlight && highlight.elapsed>highlight.duration-1.15) {
    const x=highlight.event.x,z=highlight.event.z;
    box(dynamic,x,.035,z,.8,.07,.11,[1,.86,.48],2);
    box(dynamic,x,.035,z,.11,.07,.8,[1,.86,.48],2);
  }
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
  const scene=displayRound();
  const ctx = radar.getContext('2d');
  if (!ctx) return;
  const width = radar.width;
  const height = radar.height;
  const map=getMap(round.map);
  const limit=map.limit;
  const scale = width / (limit*2+6);
  const at = value => (value + limit + 3) * scale;
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
  ctx.strokeStyle = map.palette.radar;
  ctx.lineWidth = 3;
  ctx.strokeRect(at(-limit), at(-limit), limit * 2 * scale, limit * 2 * scale);
  ctx.fillStyle = map.palette.radar;
  for (const o of map.obstacles)
    ctx.fillRect(at(o.minX),at(o.minZ),(o.maxX-o.minX)*scale,(o.maxZ-o.minZ)*scale);
  for (const segment of scene.segments) {
    const color = segment.rider.color;
    ctx.strokeStyle = 'rgba(' +
      Math.round(color[0] * 255) + ',' +
      Math.round(color[1] * 255) + ',' +
      Math.round(color[2] * 255) + ',.7)';
    const remaining=round.rule==='decay' && !segment.active
      ? segment.closedAt+round.trailLifetime-(scene.time ?? scene.elapsed) : Infinity;
    if(remaining<=2) {
      ctx.strokeStyle='#ffe879';
      ctx.setLineDash([5,4]);
    } else ctx.setLineDash([]);
    ctx.lineWidth = 2.5;
    ctx.beginPath();
    ctx.moveTo(at(segment.x1), at(segment.z1));
    ctx.lineTo(at(segment.x2), at(segment.z2));
    ctx.stroke();
    ctx.setLineDash([]);
  }
  for (const rider of scene.riders) {
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
      ctx.save();
      ctx.translate(at(rider.x), at(rider.z));
      ctx.rotate(rider.dir * Math.PI / 2);
      ctx.beginPath();
      if (rider.style === 'hunter') {
        ctx.moveTo(0, -6); ctx.lineTo(5, 5); ctx.lineTo(-5, 5);
      } else if (rider.style === 'trapper') {
        ctx.moveTo(0, -6); ctx.lineTo(5, 0); ctx.lineTo(0, 6); ctx.lineTo(-5, 0);
      } else {
        ctx.rect(-4, -4, 8, 8);
      }
      ctx.closePath();
      ctx.fill();
      ctx.strokeStyle = '#ffffff';
      ctx.lineWidth = 1;
      ctx.stroke();
      ctx.restore();
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
      captureReplayFrame();
      handleEvents();
      accumulator -= STEP;
      steps++;
    }
    accumulator = Math.min(accumulator, STEP * 16);
  }
  if(mode==='won' || mode==='lost') finishAge+=realDt;
  if(mode==='highlight' && highlight) {
    highlight.elapsed+=realDt;
    const progress=Math.min(1,highlight.elapsed/(highlight.duration-.45));
    highlight.index=Math.min(highlight.frames.length-1,
      Math.floor(progress*(highlight.frames.length-1)));
    if(highlight.index===highlight.frames.length-1 && !highlight.impactShown) {
      highlight.impactShown=true;
      spawnImpact(highlight.event);
      spawnDerez({...highlight.event,riderId:'YOU'});
    }
    if(highlight.elapsed>=highlight.duration) completeHighlight();
  }
  if (mode !== 'paused') {
    updateParticles(realDt);
    for (const [id,lean] of turnLean) {
      lean.age+=realDt;
      lean.value*=Math.exp(-realDt*8);
      if(lean.age>.45) turnLean.delete(id);
    }
  }
  updateMusic();
  if (!graphicsLost) {
    draw(realDt);
    drawRadar();
  }
  requestAnimationFrame(frame);
}

overlay.addEventListener('keydown', event => {
  if (event.key !== 'Tab' || overlay.classList.contains('hidden')) return;
  const items = [...overlay.querySelectorAll('button, select, input, summary')]
    .filter(item => !item.hidden && !item.closest('[hidden]') && item.getClientRects().length > 0 && getComputedStyle(item).visibility === 'visible');
  if (!items.length) return;
  const first = items[0], last = items[items.length - 1];
  if ((event.shiftKey && document.activeElement === first) ||
      (!event.shiftKey && document.activeElement === last)) {
    event.preventDefault();
    (event.shiftKey ? last : first).focus({ preventScroll: true });
  }
});
ui.start.addEventListener('click', () => {
  if (graphicsLost) location.reload();
  else if (mode === 'paused') pause();
  else start();
});
ui['lesson-start'].addEventListener('click', startLesson);
ui['change-mode'].addEventListener('click', showMenu);
skipLesson.addEventListener('click', showMenu);
skipHighlightButton.addEventListener('click', completeHighlight);
modeSelect.addEventListener('change', () => { writeLocal(MODE_KEY, modeSelect.value); syncSetup(); });
rivalSelect.addEventListener('change', () => { writeLocal(RIVAL_KEY, rivalSelect.value); syncSetup(); });
difficultySelect.addEventListener('change', () => { writeLocal(DIFFICULTY_KEY, difficultySelect.value); syncSetup(); });
mapSelect.addEventListener('change', () => { writeLocal(MAP_KEY, mapSelect.value); syncSetup(); });
ruleSelect.addEventListener('change', () => { writeLocal(RULE_KEY, ruleSelect.value); syncSetup(); });
volumeInput.addEventListener('input', () => {
  volume = Number(volumeInput.value);
  writeLocal(VOLUME_KEY, String(volume));
  activateAudio();
  syncAudioMix();
});
musicVolumeInput.addEventListener('input', () => {
  musicVolume=Number(musicVolumeInput.value);
  writeLocal(MUSIC_VOLUME_KEY,String(musicVolume));
  activateAudio();
  syncAudioMix();
});
cameraSelect.addEventListener('change', () => {
  cameraDistance = cameraSelect.value;
  writeLocal(CAMERA_KEY, cameraDistance);
});
cameraModeSelect.addEventListener('change', () => {
  cameraMode = cameraModeSelect.value;
  writeLocal(CAMERA_MODE_KEY, cameraMode);
});
reduceFlashesInput.addEventListener('change', () => {
  reduceFlashes = reduceFlashesInput.checked;
  writeLocal(REDUCE_FLASHES_KEY, String(reduceFlashes));
  syncFlashSetting();
});
qualitySelect.addEventListener('change', () => {
  graphicsQuality = qualitySelect.value;
  writeLocal(QUALITY_KEY, graphicsQuality);
});ui.pause.addEventListener('click', pause);
ui.sound.addEventListener('click', () => {
  muted = !muted;
  writeLocal(MUTE_KEY, String(muted));
  activateAudio();
  syncAudioMix();
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
  if (mode === 'highlight' && (event.key === 'Escape' || event.key === 'Enter' || event.key === ' ')) {
    event.preventDefault();
    completeHighlight();
    return;
  }
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
  syncAudioMix();
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

modeSelect.value = ['duel', 'arena'].includes(readLocal(MODE_KEY)) ? readLocal(MODE_KEY) : 'duel';
rivalSelect.value = DUEL_RIVALS[readLocal(RIVAL_KEY)] ? readLocal(RIVAL_KEY) : 'hunter';
difficultySelect.value = DIFFICULTIES[readLocal(DIFFICULTY_KEY)] ? readLocal(DIFFICULTY_KEY) : 'standard';
mapSelect.value = MAPS[readLocal(MAP_KEY)] ? readLocal(MAP_KEY) : 'classic';
ruleSelect.value = readLocal(RULE_KEY) === 'decay' ? 'decay' : 'classic';
volumeInput.value = String(volume);
musicVolumeInput.value=String(musicVolume);
cameraSelect.value = cameraDistance;
cameraModeSelect.value = cameraMode;
reduceFlashesInput.checked = reduceFlashes;
syncFlashSetting();
qualitySelect.value = graphicsQuality;
syncSoundButton();
syncTouchControls();
showMenu();
drawRadar();
requestAnimationFrame(frame);
} catch (error) {
  console.error('Grid graphics initialization failed:', error);
  showWebglError('The Grid could not start graphics. Try reloading or another browser.');
}
}
