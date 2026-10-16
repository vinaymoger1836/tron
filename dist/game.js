import { createRound, createDuelRound, createArenaRound, createOperationRound, DUEL_RIVALS, DIFFICULTIES, drainEvents, queueTurn, stepRound, setCycleAutoDrive, setCycleThrottle, setCycleFire, STEP } from './simulation.js';
import { MAPS, getMap } from './maps.js';
import { createArenaMatch, recordArenaRound, nextArenaRound, arenaWon } from './arena.js';
import { createMatch, recordRound, nextRound, roundSeed } from './match.js';
import { OPERATIONS, JOURNEY, getOperation } from './operations.js';
import { createShiftRound, stepShiftRound, setShiftMove, queueShiftTurn, requestShiftForm, throwShiftDisc, setShiftFire } from './grid-shift.js';

const canvas = document.querySelector('#game');
const radar = document.querySelector('#radar');
const overlay = document.querySelector('#overlay');
const menuHub = document.querySelector('#menu-hub');
const menuHubOpen = document.querySelector('#menu-hub-open');
const menuTabs = [...document.querySelectorAll('.menu-tab')];
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
const operationSelect = document.querySelector('#operation-select');
const finalRivalInput = document.querySelector('#final-rival');
const skipBriefButton = document.querySelector('#skip-brief');
const trailLookSelect = document.querySelector('#trail-look');
const operationHud = document.querySelector('#operation-hud');
const shiftHud = document.querySelector('#shift-hud');
const shiftTouch = document.querySelector('.shift-touch');
const shiftKeys = new Set();
const objectiveTitle = document.querySelector('#objective-title');
const objectiveProgress = document.querySelector('#objective-progress');
const objectiveWarning = document.querySelector('#objective-warning');
const archiveList = document.querySelector('#archive-list');
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
const historyList = document.querySelector('#run-history-list');
const historyRecords = document.querySelector('#run-records');
const historyBadges = document.querySelector('#run-badges');
const storageStatus = document.querySelector('#storage-status');
const touchSizeSelect = document.querySelector('#touch-size');
const touchPlacementSelect = document.querySelector('#touch-placement');
const renderScaleSelect = document.querySelector('#render-scale');
const cameraMotionSelect = document.querySelector('#camera-motion');
const flashIntensitySelect = document.querySelector('#flash-intensity');
const gamepadInput = document.querySelector('#gamepad-enabled');
const bindingStatus = document.querySelector('#binding-status');
const captionsInput = document.querySelector('#captions-enabled');
const autoDriveInput = document.querySelector('#auto-drive');
const cueCaption = document.querySelector('#cue-caption');
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
const OPERATION_KEY = 'tron-grid-run-operation';
const FINAL_RIVAL_KEY = 'tron-grid-run-final-rival';
const OP_PROGRESS_KEY = 'tron-grid-run-operations:v1';
const LOOK_KEY = 'tron-grid-run-trail-look';
const VOLUME_KEY = 'tron-grid-run-volume';
const MUSIC_VOLUME_KEY = 'tron-grid-run-music-volume';
const CAMERA_KEY = 'tron-grid-run-camera';
const CAMERA_MODE_KEY = 'tron-grid-run-camera-mode';
const REDUCE_FLASHES_KEY = 'tron-grid-run-reduce-flashes';
const QUALITY_KEY = 'tron-grid-run-quality';
const HISTORY_KEY = 'tron-grid-run-history:v1';
const RECORDS_KEY = 'tron-grid-run-records:v1';
const BADGES_KEY = 'tron-grid-run-badges:v1';
const BINDINGS_KEY = 'tron-grid-run-bindings:v3';
const PREVIOUS_BINDINGS_KEY = 'tron-grid-run-bindings:v2';
const OLD_BINDINGS_KEY = 'tron-grid-run-bindings:v1';
const TOUCH_SIZE_KEY = 'tron-grid-run-touch-size';
const TOUCH_PLACEMENT_KEY = 'tron-grid-run-touch-placement';
const RENDER_SCALE_KEY = 'tron-grid-run-render-scale';
const CAMERA_MOTION_KEY = 'tron-grid-run-camera-motion';
const FLASH_INTENSITY_KEY = 'tron-grid-run-flash-intensity';
const GAMEPAD_KEY = 'tron-grid-run-gamepad';
const CAPTIONS_KEY = 'tron-grid-run-captions';
const AUTO_DRIVE_KEY = 'tron-grid-run-auto-drive';
const DAILY_RULES_VERSION = 3;
const DEFAULT_BINDINGS = Object.freeze({ left: 'a', right: 'd', up: 'w', down: 's', form: 'f', fire: 'e', disc: 'space', pause: 'p', sound: 'm' });
let storageAvailable = true;

function readLocal(key) {
  try { return localStorage.getItem(key); } catch { storageAvailable = false; return null; }
}
function writeLocal(key, value) {
  try { localStorage.setItem(key, value); storageAvailable = true; syncStorageStatus(); return true; }
  catch { storageAvailable = false; syncStorageStatus(); return false; }
}
function syncStorageStatus() {
  if (storageStatus) storageStatus.textContent = storageAvailable ? '' :
    'Local storage is unavailable or full. New settings and runs may not survive a reload.';
}
function optionValue(key, options, fallback) {
  const value = readLocal(key);
  return options.includes(value) ? value : fallback;
}
function validBinding(key) { return key === 'space' || /^[a-z0-9]$/.test(key); }
function readBindings() {
  let saved = {}, legacy = false;
  try {
    const current = readLocal(BINDINGS_KEY);
    legacy = !current;
    saved = JSON.parse(current || readLocal(PREVIOUS_BINDINGS_KEY) || readLocal(OLD_BINDINGS_KEY) || '{}');
  } catch { saved = {}; }
  if (!saved || typeof saved !== 'object' || Array.isArray(saved)) saved = {};
  const next = { ...DEFAULT_BINDINGS };
  if (saved.pulse && !saved.fire) saved.fire = saved.pulse;
  if (legacy && saved.form === 'e') {
    const used = new Set(Object.entries(saved).filter(([action]) => action !== 'form')
      .map(([, key]) => key));
    saved.form = ['f', 'q', 'r', 't', 'g'].find(key => !used.has(key)) || 'f';
  }
  for (const action of Object.keys(next)) {
    if (typeof saved[action] === 'string' && validBinding(saved[action]))
      next[action] = saved[action];
  }
  if (legacy && Object.keys(saved).length && !saved.fire) {
    const occupied = new Set(Object.entries(next).filter(([action]) => action !== 'fire')
      .map(([, key]) => key));
    next.fire = ['e', 'f', 'q', 'r'].find(key => !occupied.has(key)) || 'e';
  }
  if (new Set(Object.values(next)).size !== Object.keys(next).length)
    return { ...DEFAULT_BINDINGS };
  return next;
}
function readHistory() {
  let saved;
  try { saved = JSON.parse(readLocal(HISTORY_KEY) || '[]'); } catch { return []; }
  if (!Array.isArray(saved)) return [];
  return saved.filter(item => item && typeof item === 'object' &&
    ['duel','arena','operations','shift'].includes(item.mode) &&
    ['won','lost','draw'].includes(item.outcome) &&
    Number.isFinite(item.score) && item.score >= 0 &&
    typeof item.date === 'string' && typeof item.map === 'string' && MAPS[item.map])
    .slice(0, 8).map(item => ({
      ...item, highlight: Array.isArray(item.highlight?.frames) &&
        item.highlight.frames.length > 1 && item.highlight.frames.length <= 40 &&
        item.highlight.frames.every(frame => Array.isArray(frame.riders) &&
          Array.isArray(frame.segments) && frame.riders.length <= 4 &&
          frame.segments.length <= 300) ? item.highlight : null,
    }));
}
function readJsonObject(key) {
  try {
    const value = JSON.parse(readLocal(key) || '{}');
    return value && typeof value === 'object' && !Array.isArray(value) ? value : {};
  } catch { return {}; }
}
function readRecordBook() {
  const records = readJsonObject(RECORDS_KEY);
  try {
    for (let i = 0; i < localStorage.length; i++) {
      const key = localStorage.key(i);
      const match = /^tron-grid-run-best:v2:(duel|arena|shift):/.exec(key || '');
      if (!match) continue;
      const value = Number(localStorage.getItem(key));
      if (Number.isFinite(value) && value >= 0)
        records[match[1]] = Math.max(Number(records[match[1]]) || 0, value);
    }
  } catch { storageAvailable = false; }
  records.operations = Math.max(Number(records.operations) || 0,
    ...Object.values(operationProgress).map(entry => entry.best || 0));
  return records;
}
function dailyForDate(date = new Date()) {
  const day = date.toISOString().slice(0, 10);
  let seed = 2166136261;
  for (const char of 'grid-run:daily:v' + DAILY_RULES_VERSION + ':' + day) {
    seed = Math.imul(seed ^ char.charCodeAt(0), 16777619) >>> 0;
  }
  const maps = ['classic', 'solar', 'vault'];
  const rivals = ['hunter', 'survivor', 'trapper'];
  return { day, seed, version: DAILY_RULES_VERSION, map: maps[seed % maps.length],
    rival: rivals[(seed >>> 8) % rivals.length], difficulty: 'standard' };
}
const savedBest = Number(readLocal(SCORE_KEY));
const legacyBest = Number.isFinite(savedBest) && savedBest >= 0 ? savedBest : 0;
let best = 0;
let muted = readLocal(MUTE_KEY) === 'true';
let touchChoice = readLocal(TOUCH_KEY) || 'auto';
let sessionMode = 'duel';
let match = null;
let operationFlow = null;
let operationProgress = readOperationProgress();
let lookChoice = readLocal(LOOK_KEY) || 'default';
let lessonStage = 0;
const storedVolume = Number(readLocal(VOLUME_KEY) ?? 70);
let volume = Number.isFinite(storedVolume) ? Math.max(0, Math.min(100, storedVolume)) : 70;
const storedMusicVolume = Number(readLocal(MUSIC_VOLUME_KEY) ?? 45);
let musicVolume = Number.isFinite(storedMusicVolume) ? Math.max(0, Math.min(100, storedMusicVolume)) : 45;
let cameraDistance = ['close', 'standard', 'far'].includes(readLocal(CAMERA_KEY)) ? readLocal(CAMERA_KEY) : 'standard';
let cameraMode = ['chase', 'tactical'].includes(readLocal(CAMERA_MODE_KEY)) ? readLocal(CAMERA_MODE_KEY) : 'chase';
let reduceFlashes = readLocal(REDUCE_FLASHES_KEY) === 'true';
let graphicsQuality = ['low', 'high'].includes(readLocal(QUALITY_KEY)) ? readLocal(QUALITY_KEY) : 'high';
let runHistory = readHistory();
let recordBook = readRecordBook();
let earnedBadges = readJsonObject(BADGES_KEY);
let runBadgeEvents = new Set();
let bindings = readBindings();
let captureBinding = null;
let dailyChallenge = null;
let touchSize = optionValue(TOUCH_SIZE_KEY, ['normal', 'large'], 'normal');
let touchPlacement = optionValue(TOUCH_PLACEMENT_KEY, ['edge', 'compact', 'raised'], 'edge');
let renderScale = optionValue(RENDER_SCALE_KEY, ['auto', '0.75', '0.5'], 'auto');
let cameraMotion = optionValue(CAMERA_MOTION_KEY, ['cinematic', 'steady'], 'cinematic');
let flashIntensity = optionValue(FLASH_INTENSITY_KEY, ['full', 'soft', 'off'], 'full');
let gamepadEnabled = readLocal(GAMEPAD_KEY) === 'true';
let captionsEnabled = readLocal(CAPTIONS_KEY) !== 'false';
let autoDrive = readLocal(AUTO_DRIVE_KEY) !== 'false';
let captionToken = 0;
let gamepadMove = { x: 0, z: 0 };
let gamepadHeld = new Set();
const fireSources = new Set();
const throttleSources = new Set();
let round = createRound();
const onlineCode = new URLSearchParams(location.search).get('onlineRoom')?.toUpperCase();
const online = /^[A-Z0-9]{6}$/.test(onlineCode || '') ? { code: onlineCode, view: null,
  controller: null, retry: null, connected: false, heldFire: false, heldThrottle: false, lastMove: '',
  lastPhase: '', lastRound: 0 } : null;
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
function flashReduced() { return reduceFlashes || flashIntensity === 'off' || reducedFlashMedia.matches; }
function syncFlashSetting() {
  document.body.classList.toggle('reduce-flashes', flashReduced());
  document.body.classList.toggle('soft-flashes', !flashReduced() && flashIntensity === 'soft');
}
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
function cylinder(out, x, y, z, radius, height, color, rail) {
  const count = 20, top = y + height;
  for (let i = 0; i < count; i++) {
    const a = i * Math.PI * 2 / count, b = (i + 1) * Math.PI * 2 / count;
    const ax = x + Math.cos(a) * radius, az = z + Math.sin(a) * radius;
    const bx = x + Math.cos(b) * radius, bz = z + Math.sin(b) * radius;
    quad(out, [ax,y,az],[bx,y,bz],[bx,top,bz],[ax,top,az],color);
    vertex(out,[x,top,z],mixColor(color,1.2),0);
    vertex(out,[ax,top,az],mixColor(color,1.2),0);
    vertex(out,[bx,top,bz],mixColor(color,1.2),0);
    quad(out,[ax,top+.03,az],[bx,top+.03,bz],
      [bx,top+.10,bz],[ax,top+.10,az],rail,2);
  }
}
let staticData = [];
const staticBuffer = gl.createBuffer();
function rebuildStatic(map) {
  const LIMIT = map.limit;
  C.floor = map.palette.floor;
  C.wall = map.palette.wall;
  staticData = [];
quad(staticData,[-LIMIT-3,0,-LIMIT-3],[LIMIT+3,0,-LIMIT-3],
  [LIMIT+3,0,LIMIT+3],[-LIMIT-3,0,LIMIT+3],C.floor,1);
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
  if (obstacle.shape === 'circle') {
    cylinder(staticData, obstacle.x, 0,
      obstacle.z, obstacle.radius, 1.8, map.palette.obstacle, map.palette.rail);
  } else {
    const x=(obstacle.minX+obstacle.maxX)/2, z=(obstacle.minZ+obstacle.maxZ)/2;
    const sx=obstacle.maxX-obstacle.minX, sz=obstacle.maxZ-obstacle.minZ;
    box(staticData,x,.7,z,sx,1.4,sz,map.palette.obstacle);
    box(staticData,x,1.43,z,sx+.08,.07,sz+.08,map.palette.rail,2);
  }
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
  const scale = renderScale === 'auto' ? 1 : Number(renderScale);
  const dpr = Math.min(devicePixelRatio || 1, graphicsQuality === 'low' ? 1 : 1.75) * scale;
  const w = Math.max(1, Math.round(innerWidth * dpr));
  const h = Math.max(1, Math.round(innerHeight * dpr));
  if(canvas.width!==w||canvas.height!==h){canvas.width=w;canvas.height=h;gl.viewport(0,0,w,h);}
}


function announce(message) {
  if (statusLive) statusLive.textContent = message;
}

function readOperationProgress() {
  let saved = {};
  try { saved = JSON.parse(readLocal(OP_PROGRESS_KEY) || '{}') || {}; } catch { /* Invalid local data is ignored. */ }
  const result = {};
  for (const id of [...Object.keys(OPERATIONS), 'journey']) {
    const entry = saved[id] || {};
    result[id] = {
      completed: entry.completed === true,
      best: Number.isFinite(entry.best) && entry.best >= 0 ? Math.floor(entry.best) : 0,
    };
  }
  return result;
}
function saveOperationProgress(id, points) {
  const entry = operationProgress[id];
  entry.completed = true;
  entry.best = Math.max(entry.best, Math.max(0, Math.floor(points)));
  writeLocal(OP_PROGRESS_KEY, JSON.stringify(operationProgress));
  syncLookOptions();
  updateArchive();
}
function updateArchive() {
  const unlocked = Object.values(OPERATIONS).filter(op => operationProgress[op.id].completed);
  archiveList.textContent = unlocked.length
    ? unlocked.map(op => op.name + ': ' + op.reward.lore).join(' ')
    : 'Complete an operation to unlock its log fragment and a cosmetic trail look. No gameplay advantage.';
}
function syncLookOptions() {
  const unlocks = { default: true,
    amber: operationProgress.portal.completed,
    violet: operationProgress.sweep.completed,
    white: operationProgress.collapse.completed,
  };
  const labels = { default: 'Grid cyan', amber: 'Portal amber',
    violet: 'Scanner violet', white: 'Containment white' };
  for (const option of trailLookSelect.options) {
    option.disabled = !unlocks[option.value];
    option.textContent = labels[option.value] + (option.disabled ? ' (locked)' : '');
  }
  if (!unlocks[lookChoice]) lookChoice = 'default';
  trailLookSelect.value = lookChoice;
}
function applyPlayerLook() {
  const colors = { default: [0.15, 0.88, 1], amber: [1, 0.62, 0.18],
    violet: [0.78, 0.45, 1], white: [0.92, 0.98, 1] };
  const player = round.riders.find(r => r.isPlayer);
  if (player) player.color = [...colors[lookChoice]];
}
function scoreKey() {
  if (dailyChallenge) return 'tron-grid-run-daily:v' + dailyChallenge.version + ':' + dailyChallenge.day;
  return 'tron-grid-run-best:v2:' + sessionMode + ':' + round.map + ':' +
    round.difficulty + ':' + round.rule;
}
const BADGE_LABELS = {
  victor: 'GRID VICTOR', trap: 'CLEAN TRAP', escape: 'CLOSE ESCAPE',
  journey: 'JOURNEY CLEAR', shift: 'FORM MASTER', daily: 'DAILY CLEAR',
};
function renderHistory() {
  const best = mode => Number(recordBook[mode]) || 0;
  historyRecords.textContent = 'Personal records / Duel ' + best('duel') +
    ' / Arena ' + best('arena') + ' / Operations ' + best('operations') +
    ' / Grid Shift ' + best('shift') +
    (recordBook['daily:' + dailyForDate().day] !== undefined
      ? ' / Today ' + recordBook['daily:' + dailyForDate().day] : '');
  const badges = Object.keys(BADGE_LABELS).filter(id => earnedBadges[id] === true);
  historyBadges.textContent = badges.length ? 'Badges / ' +
    badges.map(id => BADGE_LABELS[id]).join(' / ') :
    'Badges / Win a match, escape closely, trap a rival, or clear a journey.';
  historyList.replaceChildren();
  if (!runHistory.length) {
    const empty = document.createElement('li');
    empty.textContent = 'No completed runs yet. Finish a match to save its last moments.';
    historyList.append(empty);
    return;
  }
  for (const item of runHistory) {
    const row = document.createElement('li');
    const summary = document.createElement('span');
    const label = item.challenge ? 'Daily ' + item.challenge.day :
      item.mode === 'operations' ? (item.operation || 'Operation') :
      item.mode === 'shift' ? 'Grid Shift' :
      item.mode.toUpperCase() + ' / ' + (getMap(item.map)?.name || item.map);
    summary.textContent = item.date.slice(0, 10) + ' / ' + label +
      ' / ' + item.outcome.toUpperCase() + ' / ' + item.score + ' pts';
    row.append(summary);
    if (Array.isArray(item.highlight?.frames) && item.highlight.frames.length > 1) {
      const watch = document.createElement('button');
      watch.className = 'secondary';
      watch.type = 'button';
      watch.textContent = 'WATCH HIGHLIGHT';
      watch.setAttribute('aria-label', 'Watch finish highlight for ' + label + ' on ' + item.date.slice(0, 10));
      watch.addEventListener('click', () => playHistoryHighlight(item));
      row.append(watch);
    }
    historyList.append(row);
  }
}
function recordRun(outcome, score) {
  const player = round.riders.find(r => r.isPlayer);
  const badges = [];
  if (outcome === 'won') badges.push('victor');
  if (runBadgeEvents.has('clean-trap')) badges.push('trap');
  if (runBadgeEvents.has('close-call')) badges.push('escape');
  if (sessionMode === 'operations' && operationFlow.kind === 'journey' && outcome === 'won') badges.push('journey');
  if (sessionMode === 'shift' && outcome === 'won' && runBadgeEvents.has('form-switch')) badges.push('shift');
  if (dailyChallenge && outcome === 'won') badges.push('daily');
  for (const badge of badges) earnedBadges[badge] = true;
  const frames = replayFrames.filter((_, index) => index % 2 === 0 || index === replayFrames.length - 1);
  const item = {
    date: new Date().toISOString(), mode: sessionMode, map: round.map,
    operation: round.operation?.id || null, difficulty: round.difficulty,
    outcome, score: Math.max(0, Math.floor(score)), seed: dailyChallenge?.seed ??
      operationFlow?.seed ?? match?.seed ?? round.seed,
    challenge: dailyChallenge ? { ...dailyChallenge } : null, badges,
    highlight: frames.length > 1 ? { frames,
      event: { x: player?.x ?? 0, z: player?.z ?? 0, riderId: 'YOU' } } : null,
  };
  runHistory.unshift(item);
  runHistory = runHistory.slice(0, 8);
  recordBook[sessionMode] = Math.max(Number(recordBook[sessionMode]) || 0, item.score);
  if (dailyChallenge) recordBook['daily:' + dailyChallenge.day] =
    Math.max(Number(recordBook['daily:' + dailyChallenge.day]) || 0, item.score);
  writeLocal(RECORDS_KEY, JSON.stringify(recordBook));
  writeLocal(BADGES_KEY, JSON.stringify(earnedBadges));
  if (!writeLocal(HISTORY_KEY, JSON.stringify(runHistory))) {
    // Keep the newest finish; older highlights are the first data to shed at quota.
    for (const older of runHistory.slice(1)) older.highlight = null;
    if (!writeLocal(HISTORY_KEY, JSON.stringify(runHistory))) {
      runHistory = runHistory.slice(0, 4).map(entry => ({ ...entry, highlight: null }));
      writeLocal(HISTORY_KEY, JSON.stringify(runHistory));
    }
  }
  renderHistory();
}
function playHistoryHighlight(item) {
  if (online) { showToast('RETURN TO SOLO TO WATCH SAVED RUNS'); return; }
  const frames = item.highlight?.frames;
  if (!Array.isArray(frames) || frames.length < 2 || !frames.every(frame =>
    Array.isArray(frame.riders) && Array.isArray(frame.segments))) return;
  generation++;
  match = null;
  operationFlow = null;
  dailyChallenge = null;
  sessionMode = item.mode;
  round = item.mode === 'operations' && OPERATIONS[item.operation]
    ? createOperationRound({ id: item.operation, seed: item.seed, difficulty: item.difficulty })
    : item.mode === 'shift'
    ? createShiftRound({ seed: item.seed, difficulty: item.difficulty })
    : createRound({ seed: item.seed, map: item.map });
  rebuildStatic(getMap(round.map));
  mode = 'highlight';
  particles = [];
  turnLean.clear();
  highlight = { history: true, frames, index: 0, elapsed: 0, duration: 6,
    event: item.highlight.event || { x: 0, z: 0, riderId: 'YOU' },
    impactShown: true };
  hideOverlay();
  setBackgroundInert(true);
  highlightLabel.textContent = 'RUN ARCHIVE / ' + item.outcome.toUpperCase() +
    ' / ' + item.score + ' POINTS';
  skipHighlightButton.textContent = 'EXIT HIGHLIGHT';
  highlightCard.hidden = false;
  skipHighlightButton.focus({ preventScroll: true });
  announce('Run archive highlight. Press Escape or Exit Highlight to return.');
}
function loadBest() {
  if (sessionMode === 'operations') {
    const key = operationFlow?.kind === 'journey' ? 'journey'
      : operationFlow?.sequence[0] || (operationSelect.value === 'journey' ? 'journey' : operationSelect.value);
    best = operationProgress[key]?.best || 0;
    return;
  }
  const value = Number(readLocal(scoreKey()));
  best = Number.isFinite(value) && value >= 0 ? value : 0;
}
function updateOperationHud() {
  if (sessionMode !== 'operations' || mode === 'ready') {
    operationHud.hidden = true;
    return;
  }
  operationHud.hidden = false;
  const mission = getOperation(round.operation.id);
  const op = round.operation;
  objectiveTitle.textContent = mission.name.toUpperCase() + ' / ' + mission.objective;
  const remaining = mission.timeLimit === null ? '' :
    ' / ' + Math.max(0, Math.ceil(mission.timeLimit - round.elapsed)) + 's left';
  if (mission.kind === 'checkpoints')
    objectiveProgress.textContent = 'Gate ' + Math.min(op.progress + 1, mission.gates.length) + '/' + mission.gates.length + remaining;
  else if (mission.kind === 'scanner')
    objectiveProgress.textContent = 'Sweeps ' + op.pulseIndex + '/' + mission.pulses.length + remaining;
  else if (mission.kind === 'collapse')
    objectiveProgress.textContent = 'Rings ' + op.collapseIndex + '/' + mission.steps.length + remaining;
  else objectiveProgress.textContent = 'Sentinel active / one rival';
  objectiveWarning.textContent = op.warning || mission.hudTip;
}
function updateShiftHud() {
  if (online) { updateOnlineShiftHud(); return; }
  const active = sessionMode === 'shift';
  document.body.classList.toggle('grid-shift-active', active);
  shiftHud.hidden = !active || mode === 'ready';
  if (!active || !round.shift) return;
  const player = round.riders.find(r => r.isPlayer);
  const rival = round.riders.find(r => !r.isPlayer);
  document.querySelector('#shift-title').textContent = player.form === 'cycle'
    ? 'CYCLE / WALL ACTIVE' : 'ON FOOT / DISC';
  const disc = round.shift.discs.some(d => d.ownerId === player.id);
  const cooldown = Math.max(0, player.discReadyAt - round.elapsed);
  document.querySelector('#shift-status').textContent =
    'You ' + player.hp + '/2 integrity / ' +
    (player.form === 'cycle' ? (player.rocketReady ? 'Rocket 1' : 'Gun ' + player.ammo + '/48 bullets') :
      'Disc ' + (disc ? 'returning' : cooldown > 0 ? cooldown.toFixed(1) + 's' : 'ready'));
  if (round.elapsed < player.nitroUntil)
    document.querySelector('#shift-status').textContent += ' / NITRO ' + Math.ceil(player.nitroUntil - round.elapsed) + 'S';
  document.querySelector('#shift-rival-status').textContent =
    'Echo ' + rival.hp + '/2 integrity / ' + rival.form.toUpperCase();
  shiftTouch.querySelector('[data-move="down"]').disabled = player.form === 'cycle';
  const weaponButton = document.querySelector('#shift-throw');
  weaponButton.textContent = player.form === 'cycle' ? (player.rocketReady ? 'ROCKET 1' : 'FIRE ' + player.ammo) : 'DISC';
  weaponButton.setAttribute('aria-label', player.form === 'cycle' ? (player.rocketReady ? 'Fire one rocket' : 'Fire wall gun') : 'Throw disc');
  weaponButton.disabled = mode !== 'running' || (player.form === 'cycle'
    ? (!player.ammo && !player.rocketReady) || round.elapsed < player.recoveryUntil : disc || cooldown > 0);
  document.querySelector('#shift-tip').textContent =
    player.form === 'cycle' ? keyLabel('fire') + ' hold to fire / ' + keyLabel('form') + ' dismount / ' +
      keyLabel('left') + '/' + keyLabel('right') + ' turn / ' + keyLabel('up') + ' forward / ' +
      Math.max(0, Math.ceil(round.shift.timeLimit - round.elapsed)) + 's'
      : [keyLabel('up'), keyLabel('left'), keyLabel('down'), keyLabel('right')].join(' ') +
        ' move / ' + keyLabel('disc') + ' throw / ' + keyLabel('form') + ' remount / ' +
      Math.max(0, Math.ceil(round.shift.timeLimit - round.elapsed)) + 's';
}
function updateHud() {
  if (online) { updateOnlineHud(); return; }
  const points = sessionMode === 'operations'
    ? (operationFlow?.points || 0) + round.score
    : match && sessionMode !== 'lesson'
      ? match.points + (match.resultRecorded ? 0 : round.score) : round.score;
  ui.score.textContent = String(points).padStart(5, '0');
  if (sessionMode === 'duel' && match) {
    ui.matchline.textContent = (dailyChallenge ? 'DAILY ' + dailyChallenge.day + ' / ' : 'DUEL / ') + 'YOU ' + match.wins.player + ' - ' + match.wins.rival +
      ' ' + DUEL_RIVALS[match.rival].id + ' / R' + match.roundNumber + ' / ' + getMap(round.map).name.toUpperCase();
  } else if (sessionMode === 'arena' && match) {
    ui.matchline.textContent = 'ARENA / HEAT ' + match.roundNumber + '/3 / SURVIVE 2 / ' +
      getMap(round.map).name.toUpperCase();
  } else if (sessionMode === 'shift') {
    ui.matchline.textContent = 'GRID SHIFT / ECHO / TWO FORMS';
  } else if (sessionMode === 'operations') {
    const stage = operationFlow?.index ?? 0;
    const total = operationFlow?.sequence.length ?? 1;
    ui.matchline.textContent = 'OPERATION / ' + getOperation(round.operation.id).name.toUpperCase() +
      (operationFlow?.kind === 'journey' ? ' / ' + (stage+1) + '/' + total : '');
  } else if (sessionMode === 'lesson') ui.matchline.textContent = 'LESSON / GRID BASICS';
  else ui.matchline.textContent = sessionMode === 'duel' ? 'DUEL / FIRST TO 3' : 'ARENA / SURVIVE 2 OF 3';
  document.querySelector('.radar-wrap .label').textContent =
    getMap(round.map).name.toUpperCase() + ' / ' + (match?.roundNumber || '01');
  ui.best.textContent = String(best).padStart(5, '0');
  document.querySelector('#rivals-label').textContent =
    sessionMode === 'operations' ? 'Progress' : sessionMode === 'shift' ? 'HP' : 'Rivals';
  ui.rivals.textContent = sessionMode === 'operations'
    ? String(round.operation.progress || round.operation.pulseIndex || round.operation.collapseIndex || 0).padStart(2, '0')
    : sessionMode === 'shift' ? String(round.riders.find(r => !r.isPlayer)?.hp ?? 0).padStart(2, '0')
    : String(round.riders.filter(r => !r.isPlayer && r.alive).length).padStart(2, '0');
  updateOperationHud();
  updateShiftHud();
  updateCycleWeaponHud();
}
function updateCycleWeaponHud() {
  const player = round.riders.find(r => r.isPlayer);
  const active = mode !== 'ready' && sessionMode !== 'shift' && player;
  const status = document.querySelector('#cycle-weapon-status');
  const button = document.querySelector('#cycle-fire');
  status.hidden = !active;
  if (active) status.textContent = (player.rocketReady ? 'ROCKET 1' : 'GUN ' + player.ammo + '/48') +
    (round.elapsed < player.nitroUntil ? ' / NITRO ' + Math.ceil(player.nitroUntil - round.elapsed) + 'S' : '');
  button.textContent = player?.rocketReady ? 'ROCKET 1' : 'FIRE ' + (player?.ammo ?? 24);
  button.setAttribute('aria-label', player?.rocketReady ? 'Fire one rocket' : 'Fire wall gun');
  button.disabled = mode !== 'running' || sessionMode === 'shift' || !(player?.ammo || player?.rocketReady);
}
function setBackgroundInert(inert) {
  canvas.inert = inert;
  document.querySelector('.hud').inert = inert;
  touchControls.inert = inert;
  shiftTouch.inert = inert;
}

let menuHubReturnFocus = null;
function activateMenuTab(tab, focus = true) {
  for (const item of menuTabs) {
    const selected = item === tab;
    item.setAttribute('aria-selected', String(selected));
    item.tabIndex = selected ? 0 : -1;
    document.getElementById(item.getAttribute('aria-controls')).hidden = !selected;
  }
  menuHub.querySelector('.menu-hub-body').scrollTop = 0;
  if (focus) tab.focus({ preventScroll: true });
}
function openMenuHub() {
  if ((overlay.classList.contains('hidden') && !online) || !menuHub.hidden) return;
  menuHubReturnFocus = document.activeElement;
  if (online) {
    fireSources.clear(); throttleSources.clear(); shiftKeys.clear(); gamepadMove = { x: 0, z: 0 };
    syncFire(); syncThrottle(); syncShiftMove();
    setBackgroundInert(true);
  }
  overlay.inert = true;
  overlay.setAttribute('aria-hidden', 'true');
  menuHub.hidden = false;
  activateMenuTab(menuTabs.find(tab => tab.getAttribute('aria-selected') === 'true') || menuTabs[0]);
}
function closeMenuHub(restoreFocus = true) {
  if (menuHub.hidden) return;
  menuHub.hidden = true;
  if (captureBinding) {
    captureBinding = null;
    bindingStatus.textContent = 'Binding change cancelled.';
    syncBindingButtons();
  }
  const overlayHidden = overlay.classList.contains('hidden');
  overlay.inert = overlayHidden;
  overlay.setAttribute('aria-hidden', String(overlayHidden));
  if (online && overlayHidden) {
    setBackgroundInert(false);
    if (restoreFocus) canvas.focus({ preventScroll: true });
  } else if (restoreFocus && !overlayHidden) (menuHubReturnFocus || menuHubOpen).focus({ preventScroll: true });
  menuHubReturnFocus = null;
}
function setOverlay(eyebrow, title, deck, button) {
  closeMenuHub(false);
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
    const focusTarget = ui.start.disabled ? menuHubOpen : ui.start;
    focusTarget.focus({ preventScroll: true });
    if (document.activeElement !== focusTarget && ++focusAttempts < 90) {
      requestAnimationFrame(focusWhenVisible);
    }
  }
  requestAnimationFrame(focusWhenVisible);
}

function hideOverlay() {
  closeMenuHub(false);
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
  const captions = { near: 'CLOSE CALL', crash: 'IMPACT', derez: 'RIDER DEREZZED', win: 'VICTORY' };
  if (captions[kind]) showCaption(captions[kind]);
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
  const stakes=!online && sessionMode==='duel' && match
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

function keyLabel(action) { return bindings[action] === 'space' ? 'SPACE' : bindings[action].toUpperCase(); }
function keyAction(key) {
  const normalized = key === ' ' ? 'space' : key.toLowerCase();
  const custom = Object.keys(bindings).find(action => bindings[action] === normalized);
  if (custom) return custom;
  return { arrowleft: 'left', arrowright: 'right', arrowup: 'up', arrowdown: 'down' }[normalized] || null;
}
function syncControlHints(shift = online ? sessionMode === 'shift' : modeSelect.value === 'shift') {
  const key = action => '<kbd>' + keyLabel(action) + '</kbd>';
  document.querySelector('#controls-hint').innerHTML =
    key('left') + key('right') + ' TURN ' + key('up') + ' FORWARD ' +
    key('fire') + ' FIRE ' + (shift ? key('form') + ' FORM ' + key('disc') + ' DISC ' : '') +
    key('pause') + ' PAUSE';
  document.querySelector('#panel-footer').innerHTML = shift
    ? 'CYCLE <span>' + keyLabel('left') + '/' + keyLabel('right') + ' TURN / ' +
      keyLabel('up') + ' FORWARD</span> &nbsp;&middot;&nbsp; FOOT <span>' +
      [keyLabel('up'),keyLabel('left'),keyLabel('down'),keyLabel('right')].join(' ') +
      ' MOVE</span> &nbsp;&middot;&nbsp; <span>' + keyLabel('form') + ' FORM / ' +
      keyLabel('fire') + ' FIRE / ' + keyLabel('disc') + ' DISC</span>'
    : 'KEYBOARD <span>' + keyLabel('left') + '/' + keyLabel('right') +
      ' TURN / ' + keyLabel('up') + ' FORWARD</span> &nbsp;&middot;&nbsp; TOUCH <span>TURN + FORWARD + FIRE</span>';
}
function syncBindingButtons() {
  for (const button of document.querySelectorAll('[data-bind]')) {
    const action = button.dataset.bind;
    button.textContent = action.toUpperCase() + ' / ' + keyLabel(action);
    button.setAttribute('aria-label', 'Remap ' + action + ', currently ' + keyLabel(action));
    button.setAttribute('aria-pressed', String(captureBinding === action));
  }
  syncControlHints();
}
function showCaption(message) {
  if (!captionsEnabled) return;
  const token = ++captionToken;
  cueCaption.textContent = message;
  cueCaption.hidden = false;
  setTimeout(() => { if (token === captionToken) cueCaption.hidden = true; }, 1400);
}
function syncTouchControls() {
  const coarse = window.matchMedia('(any-pointer: coarse)').matches;
  document.body.classList.toggle('touch-large', touchSize === 'large');
  document.body.classList.toggle('touch-compact', touchPlacement === 'compact');
  document.body.classList.toggle('touch-raised', touchPlacement === 'raised');
  touchControls.classList.remove('force-show', 'force-hide');
  if (touchChoice === 'show') touchControls.classList.add('force-show');
  if (touchChoice === 'hide') touchControls.classList.add('force-hide');
  shiftTouch.classList.toggle('force-show', touchChoice === 'show');
  shiftTouch.classList.toggle('force-hide', touchChoice === 'hide');
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
  if (online || mode !== 'countdown') return;
  mode = 'running';
  syncAudioMix();
  countdown.textContent = 'GO';
  countdown.classList.add('show');
  accumulator = 0;
  lastFrame = performance.now();
  beep(730, 0.2, 'triangle', 0.06);
  effectMotif('wall');
  announce(sessionMode === 'operations' ? 'Go. ' + getOperation(round.operation.id).objective : sessionMode === 'shift' ? 'Go. Derez Echo using your disc or light wall. Hold ' + keyLabel('fire') + ' to fire 24 mounted bullets.' : 'Go. Survive the Grid. Hold ' + keyLabel('fire') + ' to fire your 24 mounted bullets.');
  const token = generation;
  setTimeout(() => { if (token === generation && mode === 'running') hideCountdown(); }, 420);
}

function selectedOperationId() {
  return operationSelect.value === 'journey' ? 'portal' : operationSelect.value;
}
function drawMapPreview() {
  const operations = modeSelect.value === 'operations';
  const mission = operations ? getOperation(selectedOperationId()) : null;
  const map = getMap(mission?.map || mapSelect.value);
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
  for (const o of map.obstacles) {
    if (o.shape === 'circle') {
      ctx.beginPath(); ctx.arc(at(o.x),at(o.z),o.radius*scale,0,Math.PI*2); ctx.fill();
    } else ctx.fillRect(at(o.minX),at(o.minZ),(o.maxX-o.minX)*scale,(o.maxZ-o.minZ)*scale);
  }
  const spawns = operations ? [mission.spawn, ...(mission.rivalSpawn ? [mission.rivalSpawn] : [])]
    : modeSelect.value === 'shift' ? map.spawns.duel
      : map.spawns[modeSelect.value === 'daily' ? 'duel' : modeSelect.value];
  spawns.forEach((spawn, index) => {
    ctx.fillStyle = index === 0 ? '#73eaff' : ['#ffa353','#c78aff','#ffdf72'][index-1];
    ctx.beginPath(); ctx.arc(at(spawn.x),at(spawn.z),3.5,0,Math.PI*2); ctx.fill();
  });
  if (mission?.gates) {
    ctx.strokeStyle = '#ffe18b';
    for (const gate of mission.gates)
      ctx.strokeRect(at(gate.minX),at(gate.minZ),
        (gate.maxX-gate.minX)*scale,(gate.maxZ-gate.minZ)*scale);
  }
  ctx.fillStyle = '#b9dce6';
  ctx.font = '600 12px Rajdhani, sans-serif';
  ctx.fillText((mission?.name || map.name).toUpperCase(), size + 12, 23);
  ctx.font = '12px Rajdhani, sans-serif';
  ctx.fillText(map.obstacles.length ? map.obstacles.length + ' fixed barriers' : 'Open circuit', size + 12, 45);
  ctx.fillText('CYAN = YOU', size + 12, 68);
  mapPreview.setAttribute('aria-label', (mission?.name || map.name) + ' layout with ' +
    map.obstacles.length + ' fixed barriers and ' + spawns.length + ' spawn positions');
  mapTip.textContent = modeSelect.value === 'shift'
    ? map.cue + ' Hold fire while mounted to shoot up to 24 wall-breaking bullets; dismount to move freely and throw a returning disc. Two disc hits derez a rival.'
    : operations
    ? mission.objective + ' ' + mission.tip +
      (operationSelect.value === 'journey' ? ' The journey links all three operations.' : '')
    : map.cue + (ruleSelect.value === 'decay' && modeSelect.value === 'arena'
      ? ' Advanced: closed walls pulse before fading after 10 seconds.' : ' Light walls remain until the heat ends.') +
      (legacyBest ? ' Legacy best ' + legacyBest + ' is preserved.' : '');
}
function syncSetup() {
  const daily = modeSelect.value === 'daily';
  dailyChallenge = daily ? dailyForDate() : null;
  if (daily) {
    rivalSelect.value = dailyChallenge.rival;
    difficultySelect.value = dailyChallenge.difficulty;
    mapSelect.value = dailyChallenge.map;
  } else {
    rivalSelect.value = DUEL_RIVALS[readLocal(RIVAL_KEY)] ? readLocal(RIVAL_KEY) : 'hunter';
    difficultySelect.value = DIFFICULTIES[readLocal(DIFFICULTY_KEY)] ? readLocal(DIFFICULTY_KEY) : 'standard';
    mapSelect.value = MAPS[readLocal(MAP_KEY)] ? readLocal(MAP_KEY) : 'classic';
  }
  rivalSelect.disabled = daily;
  difficultySelect.disabled = daily;
  mapSelect.disabled = daily;
  const duel = modeSelect.value === 'duel' || daily;
  const operations = modeSelect.value === 'operations';
  const shift = modeSelect.value === 'shift';
  document.querySelector('#rival-field').hidden = !duel;
  document.querySelector('#rule-field').hidden = duel || operations || shift;
  document.querySelector('#map-field').hidden = operations;
  document.querySelector('#operation-field').hidden = !operations;
  document.querySelector('#final-rival-field').hidden = !operations || operationSelect.value !== 'journey';
  const profile = DUEL_RIVALS[rivalSelect.value];
  const difficultyHint = {
    rookie: 'Reacts later; speed stays equal.',
    standard: 'Balanced reactions; speed stays equal.',
    expert: 'Reacts sooner; speed stays equal.',
  };
  ui['rival-preview'].textContent = daily
    ? 'UTC ' + dailyChallenge.day + ' / rules v' + dailyChallenge.version + ' / seed ' + dailyChallenge.seed + '. Same map, rival, and Standard rules on every replay today.'
    : shift
    ? 'Echo has the same 24-bullet mounted gun, foot disc, and mounting rules. Read its form before closing distance.'
    : operations
    ? operationSelect.value === 'journey'
      ? 'Three linked operations with skippable briefings. Final Sentinel challenge is optional.'
      : getOperation(selectedOperationId()).briefing
    : duel
      ? profile.id + ' / ' + profile.style.toUpperCase() + ' / ' + profile.cue + '. ' + difficultyHint[difficultySelect.value]
      : 'Three different AI styles per heat. Survive at least 2 of 3 heats; each heat adds to your score.';
  ui.start.textContent = operations ? 'OPEN OPERATIONS' : shift ? 'ENTER GRID SHIFT'
    : daily ? 'START DAILY SIGNAL' : duel ? 'ENTER DUEL' : 'ENTER ARENA';
  document.querySelector('#mode-guide').textContent = daily
    ? 'Offline challenge / First to three. Your best for this UTC day is stored only in this browser.'
    : shift ? 'Two forms / Ride to build and cut walls, then dismount to dodge and throw. Your walls remain lethal.'
    : operations ? 'Solo mission / Follow the objective and watch its warning panel.'
    : duel ? 'First to three / Turn into open space and trap one rival.'
    : 'Three heats / Survive at least two against three rivals. Trails: solid you, dashed Hunter, dotted Survivor, paired Trapper.';
  if (gamepadEnabled) document.querySelector('#mode-guide').textContent +=
    ' Gamepad: left/right turns, up moves forward in manual drive, A changes form, B uses the current weapon, Start pauses.';
  canvas.setAttribute('aria-label', shift ? '3D Grid Shift cycle and disc arena' : '3D light cycle arena');
  syncControlHints(shift);
  drawMapPreview();
  updateArchive();
  if (mode === 'ready') {
    sessionMode = daily ? 'duel' : modeSelect.value;
    round = shift ? createShiftRound({ seed: 1, difficulty: difficultySelect.value, map: mapSelect.value })
      : operations
      ? createOperationRound({ id: selectedOperationId(), seed: 1, difficulty: difficultySelect.value })
      : duel
        ? createDuelRound({ seed: 1, rival: rivalSelect.value, difficulty: difficultySelect.value, map: mapSelect.value })
        : createArenaRound({ seed: 1, difficulty: difficultySelect.value, map: mapSelect.value, rule: ruleSelect.value });
    applyPlayerLook();
    rebuildStatic(getMap(round.map));
    loadBest();
    updateHud();
    drawRadar();
  }
}


function onlineAction(value) {
  if (!online?.connected || !online.view) return;
  fetch('/api/rooms/' + online.code + '/action', {
    method: 'POST', credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(value),
  }).then(async response => {
    if (response.ok || ['direction', 'turn', 'throttle', 'fire', 'move'].includes(value.type)) return;
    const body = await response.json().catch(() => ({}));
    showToast(body.error || 'Room action failed.');
  }).catch(() => {
    if (!['direction', 'turn', 'throttle', 'fire', 'move'].includes(value.type))
      showToast('Room connection interrupted.');
  });
}
function onlineRound(view) {
  const riders = view.round.riders.map(rider => ({
    ...rider, isPlayer: rider.id === view.selfId,
  }));
  riders.sort((a, b) => Number(b.isPlayer) - Number(a.isPlayer));
  const riderById = new Map(riders.map(rider => [rider.id, rider]));
  return {
    ...view.round, rule: view.round.rule || 'classic',
    trailLifetime: view.round.trailLifetime ?? Infinity, difficulty: 'standard',
    score: 0, scoreBreakdown: {}, riders,
    segments: view.round.segments.map(segment => ({
      ...segment, rider: riderById.get(segment.ownerId),
    })).filter(segment => segment.rider),
    shift: view.round.shift ? {
      ...view.round.shift, bullets: view.round.bullets, aiIntent: 'human riders',
    } : null,
  };
}
function updateOnlineShiftHud() {
  const active = sessionMode === 'shift' && !!online?.view?.round;
  document.body.classList.toggle('grid-shift-active', active);
  shiftHud.hidden = !active;
  if (!active) return;
  const player = round.riders[0];
  const others = round.riders.filter(r => !r.isPlayer);
  if (!player) return;
  document.querySelector('#shift-title').textContent = player.form === 'cycle'
    ? 'CYCLE / WALL ACTIVE' : 'ON FOOT / DISC';
  const discOut = round.shift.discs.some(disc => disc.ownerId === player.id);
  const cooldown = Math.max(0, player.discReadyAt - round.elapsed);
  document.querySelector('#shift-status').textContent =
    'You ' + player.hp + '/2 integrity / ' + (player.form === 'cycle'
      ? (player.rocketReady ? 'Rocket 1' : 'Gun ' + player.ammo + '/48 bullets')
      : 'Disc ' + (discOut ? 'returning' : cooldown > 0 ? cooldown.toFixed(1) + 's' : 'ready'));
  if (round.elapsed < player.nitroUntil)
    document.querySelector('#shift-status').textContent += ' / NITRO ' + Math.ceil(player.nitroUntil - round.elapsed) + 'S';
  document.querySelector('#shift-rival-status').textContent = others.map(rider => {
    const name = online.view.players.find(player => player.id === rider.id)?.name || rider.id;
    return name + ' ' + (rider.alive ? rider.hp + '/2 ' + rider.form.toUpperCase() : 'OUT');
  }).join(' / ');
  shiftTouch.querySelector('[data-move="down"]').disabled = player.form === 'cycle';
  const weaponButton = document.querySelector('#shift-throw');
  weaponButton.textContent = player.form === 'cycle' ? (player.rocketReady ? 'ROCKET 1' : 'FIRE ' + player.ammo) : 'DISC';
  weaponButton.setAttribute('aria-label', player.form === 'cycle' ? (player.rocketReady ? 'Fire one rocket' : 'Fire wall gun') : 'Throw disc');
  weaponButton.disabled = mode !== 'running' || !player.alive ||
    (player.form === 'cycle' ? (!player.ammo && !player.rocketReady) || round.elapsed < player.recoveryUntil
      : discOut || cooldown > 0);
  document.querySelector('#shift-tip').textContent = player.form === 'cycle'
    ? keyLabel('fire') + ' hold to fire / ' + keyLabel('form') + ' dismount / ' +
      keyLabel('left') + '/' + keyLabel('right') + ' turn / ' + keyLabel('up') + ' forward / ' +
      Math.max(0, Math.ceil(round.shift.timeLimit - round.elapsed)) + 's'
    : [keyLabel('up'), keyLabel('left'), keyLabel('down'), keyLabel('right')].join(' ') +
      ' move / ' + keyLabel('disc') + ' throw / ' + keyLabel('form') + ' remount / ' +
      Math.max(0, Math.ceil(round.shift.timeLimit - round.elapsed)) + 's';
}
function updateOnlineHud() {
  const view = online.view;
  if (!view?.round) return;
  const self = view.players.find(player => player.id === view.selfId);
  const player = round.riders[0];
  ui.score.previousElementSibling.textContent = 'WINS';
  ui.best.previousElementSibling.textContent = 'HEAT';
  ui.score.textContent = String(self?.wins || 0).padStart(2, '0');
  ui.best.textContent = String(view.roundNumber).padStart(2, '0');
  document.querySelector('#rivals-label').textContent = 'RIVALS';
  ui.rivals.textContent = String(round.riders.filter(r => !r.isPlayer && r.alive).length).padStart(2, '0');
  ui.matchline.textContent = 'ONLINE ' + view.mode.toUpperCase() + ' / ROOM ' + view.code +
    ' / HEAT ' + view.roundNumber + ' / ' + getMap(view.map).name.toUpperCase() +
    (view.mode === 'arena' && view.rule === 'decay' ? ' / 10S WALLS' : '');
  document.querySelector('.radar-wrap .label').textContent =
    getMap(view.map).name.toUpperCase() + ' / ' + view.roundNumber;
  const roster = document.querySelector('#online-roster');
  roster.hidden = false;
  roster.replaceChildren(...view.players.map(seat => {
    const item = document.createElement('span');
    const rider = round.riders.find(r => r.id === seat.id);
    item.className = (seat.id === view.selfId ? 'self ' : '') +
      (rider?.alive === false ? 'out' : '');
    item.textContent = seat.name + ' ' + seat.wins +
      (view.mode === 'shift' && rider ? ' / ' + rider.hp + 'HP' : '');
    return item;
  }));
  ui['rival-intent'].textContent = view.mode === 'shift'
    ? Math.max(0, Math.ceil(round.shift.timeLimit - round.elapsed)) + 'S / ' +
      (player?.form === 'foot' ? 'FOOT' : 'CYCLE')
    : view.players.map(seat => seat.name + ' ' + seat.wins).join(' / ');
  operationHud.hidden = true;
  updateOnlineShiftHud();
  updateCycleWeaponHud();
}
function onlineReceive(view) {
  if (!online || graphicsLost || view.code !== online.code) return;
  if (view.phase === 'lobby') {
    location.replace('./lan.html?room=' + online.code);
    return;
  }
  online.view = view;
  document.body.dataset.onlinePhase = view.phase;
  online.connected = true;
  if (view.round) {
    const oldMap = round.map;
    round = onlineRound(view);
    for (const effect of view.round.effects || []) {
      if (effect.id <= (online.lastEffectId || 0)) continue;
      online.lastEffectId = effect.id;
      if (effect.type === 'rocket-explode') {
        spawnImpact(effect);
        showCaption('ROCKET IMPACT');
      } else if (effect.type === 'arena-shrink') {
        showToast('GRID SHRINKING / ' + effect.limit);
        announce('Grid shrinking. New limit ' + effect.limit + '.');
      } else if (effect.type === 'pickup' && effect.riderId === view.selfId) {
        showToast(effect.pickup.toUpperCase() + ' PICKED UP');
        announce(effect.pickup + ' picked up.');
      }
    }
    sessionMode = view.mode;
    match = { roundNumber: view.roundNumber };
    if (oldMap !== round.map || online.lastRound !== view.roundNumber) {
      rebuildStatic(getMap(round.map));
      camera = [7, 13, 31];
      target = [0, 0, 0];
      online.lastRound = view.roundNumber;
    }
  }
  const key = view.phase + ':' + view.roundNumber;
  if (key !== online.lastPhase) {
    online.lastPhase = key;
    online.heldFire = false;
    online.heldThrottle = false;
    online.lastMove = '';
    fireSources.clear();
    throttleSources.clear();
    shiftKeys.clear();
    gamepadMove = { x: 0, z: 0 };
    syncControlHints(view.mode === 'shift');
    if (view.phase === 'running') {
      mode = 'running';
      onlineAction({ type: 'auto-drive', enabled: autoDrive });
      hideCountdown();
      hideOverlay();
      nextMusicBeat = 0;
      activateAudio();
      syncAudioMix();
      announce('Online ' + view.mode + ' heat ' + view.roundNumber + ' started.');
    } else if (view.phase === 'countdown') {
      mode = 'countdown';
      hideOverlay();
      countdown.classList.add('show');
      announce('Online heat starting.');
    } else if (view.phase === 'paused' || view.phase === 'connection-wait') {
      mode = 'paused';
      hideCountdown();
      setOverlay(view.phase === 'paused' ? 'HOST PAUSED' : 'CONNECTION HELD',
        'GRID <span>PAUSED</span>',
        view.phase === 'paused' ? 'The host paused the match.' :
          'Waiting for every rider to reconnect. The simulation is held.',
        view.selfId === view.hostId && view.phase === 'paused' ? 'RESUME' : 'WAITING');
      ui.start.disabled = view.selfId !== view.hostId || view.phase !== 'paused';
      ui['change-mode'].hidden = false;
      ui['change-mode'].textContent = 'ROOM';
      announce(view.phase === 'paused' ? 'Host paused the match.' : 'Waiting for a rider to reconnect.');
    } else if (view.phase === 'round-result' || view.phase === 'match-result') {
      const finished = view.phase === 'match-result';
      const winnerId = finished ? view.result?.matchWinnerId : view.result?.winnerId;
      const winnerName = finished ? view.result?.matchWinnerName : view.result?.winnerName;
      const result = view.result?.matchDraw ? 'The match is a draw.' :
        winnerId === view.selfId ? 'You won ' + (finished ? 'the match.' : 'the heat.') :
          winnerName ? winnerName + ' won ' + (finished ? 'the match.' : 'the heat.') :
            'The heat ended in a draw.';
      mode = winnerId === view.selfId ? 'won' : 'lost';
      hideCountdown();
      setOverlay(finished ? 'MATCH COMPLETE' : 'HEAT COMPLETE',
        winnerId === view.selfId ? 'GRID <span>SECURED</span>' : 'END OF <span>LINE</span>',
        result + ' ' + view.players.map(player => player.name + ' ' + player.wins).join(' / '),
        view.selfId === view.hostId ? finished ? 'REMATCH' : 'NEXT HEAT' : 'RETURN TO ROOM');
      ui.start.disabled = false;
      ui['change-mode'].hidden = false;
      ui['change-mode'].textContent = 'ROOM';
      announce(result);
    }
  }
  if (view.phase === 'countdown') {
    mode = 'countdown';
    online.countdownUntil = performance.now() + view.countdownMs;
    countdownRemaining = Math.max(0, view.countdownMs / 1000);
    countdown.textContent = String(Math.max(1, Math.ceil(countdownRemaining)));
  }
  ui.pause.hidden = view.selfId !== view.hostId;
  document.querySelector('#online-settings').hidden = false;
  updateOnlineHud();
}
function beginOnline() {
  setup.hidden = true;
  ui['lesson-start'].hidden = true;
  ui['change-mode'].hidden = false;
  ui['change-mode'].textContent = 'ROOM';
  ui.pause.hidden = true;
  document.querySelector('#online-settings').hidden = false;
  setOverlay('ONLINE ROOM ' + online.code, 'CONNECTING <span>TO GRID</span>',
    'Joining the live match. Your camera and control settings carry over.', 'WAITING');
  ui.start.disabled = true;
  onlineConnect();
}
async function onlineConnect() {
  if (!online) return;
  online.controller?.abort();
  const controller = new AbortController();
  online.controller = controller;
  try {
    const response = await fetch('/api/rooms/' + online.code + '/stream', {
      credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
    });
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      if (response.status === 403 || response.status === 404)
        throw Object.assign(new Error(body.error || 'Room seat unavailable.'), { fatal: true });
      throw new Error(body.error || 'Room connection failed.');
    }
    online.connected = true;
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let pending = '';
    while (true) {
      const { value, done } = await reader.read();
      if (done) throw new Error('Room connection closed.');
      pending += decoder.decode(value, { stream: true });
      let end;
      while ((end = pending.indexOf('\n')) >= 0) {
        const line = pending.slice(0, end);
        pending = pending.slice(end + 1);
        if (line) onlineReceive(JSON.parse(line));
      }
    }
  } catch (error) {
    if (controller.signal.aborted) return;
    online.connected = false;
    online.lastPhase = '';
    online.heldFire = false;
    fireSources.clear();
    mode = 'paused';
    hideCountdown();
    setOverlay('SIGNAL INTERRUPTED', 'GRID <span>OFFLINE</span>',
      error.fatal ? error.message : 'Connection interrupted. Reconnecting to the room.',
      error.fatal ? 'RETURN TO ROOM' : 'WAITING');
    ui.start.disabled = !error.fatal;
    ui['change-mode'].hidden = false;
    announce(error.message);
    if (!error.fatal) online.retry = setTimeout(onlineConnect, 1000);
  }
}

function showMenu() {
  generation++;
  match = null;
  operationFlow = null;
  sessionMode = modeSelect.value === 'daily' ? 'duel' : modeSelect.value;
  mode = 'ready';
  ui.pause.textContent = 'II';
  ui.pause.setAttribute('aria-label', 'Pause game');
  syncAudioMix();
  round = createRound({ seed: 1 });
  replayFrames=[];
  highlight=null;
  highlightCard.hidden=true;
  particles = [];
  turnLean.clear();
  shiftKeys.clear();
  fireSources.clear();
  hideCountdown();
  lessonHint.hidden = true;
  skipLesson.hidden = true;
  setup.hidden = false;
  ui['lesson-start'].hidden = false;
  skipBriefButton.hidden = true;
  ui['change-mode'].hidden = true;
  ui['rival-intent'].textContent = '';
  syncSetup();
  renderHistory();
  setOverlay('CHOOSE YOUR CIRCUIT', 'ENTER THE <span>GRID<small>01</small></span>',
    'Choose Duel, Arena, Operations, Grid Shift, or the offline Daily Signal. Grid Shift lets you dismount, throw a disc, and remount in clear space.', ui.start.textContent);
  announce('Choose a mode, including the offline Daily Signal. A short playable cycle lesson is available.');
}

function showLessonStep(message) {
  lessonHint.hidden = false;
  lessonHint.textContent = message;
  announce(message);
}

function operationStageSeed() {
  return (operationFlow.seed ^ Math.imul(operationFlow.index + 1, 0x9e3779b9)) >>> 0;
}
function showOperationBrief() {
  generation++;
  const id = operationFlow.sequence[operationFlow.index];
  const mission = getOperation(id);
  round = createOperationRound({ id, seed: operationStageSeed(), difficulty: operationFlow.difficulty });
  applyPlayerLook();
  rebuildStatic(getMap(round.map));
  loadBest();
  mode = 'operation-brief';
  syncAudioMix();
  setup.hidden = true;
  ui['lesson-start'].hidden = true;
  skipBriefButton.hidden = false;
  ui['change-mode'].hidden = false;
  highlightCard.hidden = true;
  hideCountdown();
  ui['rival-intent'].textContent = '';
  updateHud();
  const count = operationFlow.kind === 'journey'
    ? (operationFlow.index + 1) + '/' + operationFlow.sequence.length : 'SOLO';
  setOverlay('OPERATION ' + count, mission.name.toUpperCase().replace(' ', '<br>'),
    mission.briefing + ' Objective: ' + mission.objective + ' ' + mission.tip,
    'LAUNCH');
  announce(mission.name + '. ' + mission.objective + ' Press Launch or Skip Story to begin.');
}
function startOperationFlow() {
  const journey = operationSelect.value === 'journey';
  runBadgeEvents = new Set();
  operationFlow = {
    kind: journey ? 'journey' : 'single',
    sequence: journey
      ? [...JOURNEY, ...(finalRivalInput.checked ? ['sentinel'] : [])]
      : [operationSelect.value],
    index: 0, points: 0, seed: randomSeed(),
    difficulty: difficultySelect.value, status: 'brief',
  };
  sessionMode = 'operations';
  showOperationBrief();
}

function showShiftBrief() {
  mode = 'shift-brief';
  setup.hidden = true;
  ui['lesson-start'].hidden = true;
  ui['change-mode'].hidden = false;
  skipBriefButton.hidden = true;
  setOverlay('GRID SHIFT / TRAINING', 'TWO <span>FORMS</span>',
    'Derez Echo before the 90-second lockout. Cycle: ' + keyLabel('left') + '/' +
    keyLabel('right') + ' or Left/Right turns and leaves a lethal wall. ' +
    keyLabel('fire') + ' held fires up to 24 wall-breaking bullets; ' +
    keyLabel('form') + ' dismounts into clear space. On foot: movement keys or arrows move freely, ' +
    keyLabel('disc') + ' throws a returning disc, ' + keyLabel('form') +
    ' remounts when clear. Two disc hits derez either rider. Your own walls remain lethal.',
    'START GRID SHIFT');
  announce('Grid Shift briefing. On the cycle, hold the gun trigger with ' + keyLabel('fire') + ' and dismount with ' + keyLabel('form') + '. On foot, throw the disc with ' + keyLabel('disc') + '.');
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
  fireSources.clear();
  if (sessionMode === 'shift') runBadgeEvents = new Set();
  round = sessionMode === 'shift' ? createShiftRound({ seed: randomSeed(), difficulty: difficultySelect.value, map: mapSelect.value })
    : sessionMode === 'operations'
    ? createOperationRound({ id: operationFlow.sequence[operationFlow.index],
        seed: operationStageSeed(), difficulty: operationFlow.difficulty })
    : sessionMode === 'duel'
    ? createDuelRound({ seed: roundSeed(match), rival: match.rival, difficulty: match.difficulty, map: match.map })
    : sessionMode === 'lesson'
      ? createRound({ seed: randomSeed(), riders: [{ id: 'YOU', x: 0, z: 16, dir: 0, isPlayer: true, controller: 'none' }] })
      : createArenaRound({ seed: roundSeed(match), difficulty: match.difficulty, map: match.map, rule: match.rule });
  syncAutoDrive();
  rebuildStatic(getMap(round.map));
  applyPlayerLook();
  loadBest();
  replayFrames=[];
  highlight=null;
  highlightCard.hidden=true;
  captureReplayFrame(true);
  mode = 'countdown';
  countdownRemaining = sessionMode === 'operations' && operationFlow.index > 0 ? 1.5 : match && match.roundNumber > 1 ? 1.5 : 3;
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
  ui.pause.textContent = 'II';
  ui.pause.setAttribute('aria-label', 'Pause game');
  setup.hidden = true;
  ui['lesson-start'].hidden = true;
  skipBriefButton.hidden = true;
  ui['change-mode'].hidden = true;
  ui['rival-intent'].textContent = sessionMode === 'shift' ? 'ECHO / READING LANES' : sessionMode === 'duel' ? DUEL_RIVALS[match.rival].cue :
    sessionMode === 'arena' ? getMap(round.map).cue : '';
  lessonHint.hidden = sessionMode !== 'lesson' && sessionMode !== 'shift';
  skipLesson.hidden = sessionMode !== 'lesson';
  if (sessionMode === 'lesson') showLessonStep('Step 1 / Turn left or right to carve a light wall.');
  if (sessionMode === 'shift') showLessonStep('Grid Shift / ' + keyLabel('fire') + ' cuts a wall; ' + keyLabel('form') + ' dismounts when clear.');
  updateHud();
  drawRadar();
  hideOverlay();
  countdown.classList.add('show');
  countdown.textContent = String(countdownNumber);
  activateAudio();
  beep(440, 0.08, 'triangle', 0.03);
  syncAudioMix();
  announce(sessionMode === 'lesson' ? 'Lesson starting. Turn left or right.' : sessionMode === 'shift' ? 'Grid Shift starting. Cut walls with ' + keyLabel('fire') + ', dismount with ' + keyLabel('form') + ', and throw with ' + keyLabel('disc') + '.' : sessionMode === 'operations' ? getOperation(round.operation.id).name + '. ' + getOperation(round.operation.id).objective + ' Starting in three, two, one.' : 'Round starting. Three, two, one.');
}

function start() {
  if (online) {
    if (!online.connected || !online.view) { location.href = './lan.html?room=' + online.code; return; }
    if (online.view?.selfId === online.view?.hostId && online.view.phase === 'paused')
      onlineAction({ type: 'pause' });
    else if (online.view?.selfId === online.view?.hostId &&
        ['round-result', 'match-result', 'lobby'].includes(online.view.phase))
      onlineAction({ type: 'start' });
    else if (online.view?.selfId !== online.view?.hostId || !online.view)
      location.href = './lan.html?room=' + online.code;
    return;
  }
  if (modeSelect.value === 'shift' && (mode === 'ready' || mode === 'shift-brief' || sessionMode === 'shift')) {
    sessionMode = 'shift';
    if (mode === 'ready') showShiftBrief();
    else beginRound();
    return;
  }
  if (modeSelect.value === 'operations' && (mode === 'ready' || sessionMode === 'operations')) {
    if (mode === 'ready' || !operationFlow || operationFlow.status === 'complete') {
      startOperationFlow();
    } else if (mode === 'operation-brief' || operationFlow.status === 'retry') {
      operationFlow.status = 'running';
      beginRound();
    } else if (operationFlow.status === 'next') {
      operationFlow.index++;
      operationFlow.status = 'brief';
      showOperationBrief();
    }
    return;
  }
  if (mode === 'ready' || match?.completed) {
    dailyChallenge = modeSelect.value === 'daily' ? dailyForDate() : null;
    sessionMode = dailyChallenge ? 'duel' : modeSelect.value;
    runBadgeEvents = new Set();
    match = sessionMode === 'duel'
      ? createMatch({ seed: dailyChallenge?.seed ?? randomSeed(),
          rival: dailyChallenge?.rival ?? rivalSelect.value,
          difficulty: dailyChallenge?.difficulty ?? difficultySelect.value })
      : createArenaMatch({ seed: randomSeed(), map: mapSelect.value,
          difficulty: difficultySelect.value, rule: ruleSelect.value });
    if (sessionMode === 'duel') match.map = dailyChallenge?.map ?? mapSelect.value;
  } else if (match?.resultRecorded) {
    if (sessionMode === 'duel') nextRound(match);
    else nextArenaRound(match);
  }
  beginRound();
}
function pause() {
  if (online) {
    if (!online.connected || !online.view) return;
    if (online.view?.selfId === online.view?.hostId &&
      ['running', 'countdown', 'paused'].includes(online.view.phase))
      onlineAction({ type: 'pause' });
    return;
  }
  if (graphicsLost) return;
  fireSources.clear();
  throttleSources.clear();
  syncFire();
  syncThrottle();
  if (mode === 'running' || mode === 'countdown') {
    pausedFrom = mode;
    mode = 'paused';
    accumulator = 0;
    if (sessionMode === 'shift') {
      shiftKeys.clear();
      gamepadMove = { x: 0, z: 0 };
      setShiftMove(round, 'YOU', 0, 0);
    }
    syncAudioMix();
    countdown.classList.remove('show');
    ui.pause.textContent = '▶';
    ui.pause.setAttribute('aria-label', 'Resume game');
    ui['change-mode'].hidden = false;
    setOverlay('CONNECTION HELD', 'GRID <span>PAUSED<small>01</small></span>',
      'The Grid is waiting. Resume when you are ready.', 'RESUME');
    announce('Game paused.');
  } else if (mode === 'paused') {
    mode = pausedFrom;
    nextMusicBeat=0;
    syncAudioMix();
    lastFrame = performance.now();
    accumulator = 0;
    ui['change-mode'].hidden = true;
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
  if (mode !== 'running') return;
  if (online) {
    if (sessionMode !== 'shift' || round.riders[0]?.form === 'cycle')
      onlineAction({ type: 'turn', sign });
    return;
  }
  if (sessionMode === 'shift') queueShiftTurn(round, 'YOU', sign);
  else queueTurn(round, 'YOU', sign);
}
function syncThrottle() {
  const held = mode === 'running' && throttleSources.size > 0;
  if (online) {
    if (online.heldThrottle !== held) {
      online.heldThrottle = held;
      onlineAction({ type: 'throttle', held });
    }
  } else setCycleThrottle(round, 'YOU', held);
}
function setThrottleSource(source, held) {
  if (held) throttleSources.add(source);
  else throttleSources.delete(source);
  syncThrottle();
}
function syncAutoDrive() {
  if (online) {
    if (mode === 'running') onlineAction({ type: 'auto-drive', enabled: autoDrive });
  } else setCycleAutoDrive(round, 'YOU', autoDrive);
  syncControlHints();
}
function syncFire() {
  const held = mode === 'running' && fireSources.size > 0;
  if (online) {
    if (online.heldFire !== held) { online.heldFire = held; onlineAction({ type: 'fire', held }); }
    updateShiftHud(); updateCycleWeaponHud();
    return;
  }
  if (sessionMode === 'shift') setShiftFire(round, 'YOU', held);
  else setCycleFire(round, 'YOU', held);
  updateShiftHud();
  updateCycleWeaponHud();
}
function setFireSource(source, held) {
  if (held) fireSources.add(source);
  else fireSources.delete(source);
  syncFire();
  if (!online) handleEvents();
}
function syncShiftMove() {
  if (sessionMode !== 'shift' || mode !== 'running') return;
  const x = Number(shiftKeys.has('right')) - Number(shiftKeys.has('left')) + gamepadMove.x;
  const z = Number(shiftKeys.has('down')) - Number(shiftKeys.has('up')) + gamepadMove.z;
  const dx = Math.max(-1, Math.min(1, x)), dz = Math.max(-1, Math.min(1, z));
  if (online) {
    const length = Math.hypot(dx, dz);
    const move = length > 1 ? { x: dx / length, z: dz / length } : { x: dx, z: dz };
    const value = move.x + ',' + move.z;
    if (online.lastMove !== value) { online.lastMove = value; onlineAction({ type: 'move', ...move }); }
    return;
  }
  setShiftMove(round, 'YOU', dx, dz);
}

function crashMessage(outcome) {
  if (outcome.reason === 'boundary') return 'You hit the arena edge.';
  if (outcome.reason === 'collapse') return 'The collapse ring caught you.';
  if (outcome.reason === 'hazard') return (outcome.by || 'A hazard') + ' struck your cycle.';
  if (outcome.reason === 'timeout') return 'Time ran out before ' + (outcome.by || 'the objective') + '.';
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
  const labels = { derez: 'rival derez', closeCall: 'close escape', trap: 'clean trap', checkpoint: 'gates', win: 'completion' };
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
    style:r.style,color:r.color,form:r.form,formChangedAt:r.formChangedAt,
  }]));
  replayFrames.push({
    time:round.elapsed, elapsed:round.elapsed, limit:round.limit,
    pickups:round.pickups.map(pickup=>({ ...pickup })),
    operation:round.operation ? { ...round.operation,
      beam:round.operation.beam ? { ...round.operation.beam } : null } : null,
    shift:round.shift ? { discs:round.shift.discs.map(disc=>({ ...disc })),
      bullets:round.shift.bullets.map(bullet=>({ ...bullet })) } : null,
    bullets:round.bullets.map(bullet=>({ ...bullet })),
    riders:[...riderMap.values()],
    segments:round.segments.map(segment=>({
      id:segment.id,x1:segment.x1,z1:segment.z1,x2:segment.x2,z2:segment.z2,
      active:segment.active,closedAt:segment.closedAt,
      rider:{ color:riderMap.get(segment.ownerId)?.color || C.player,
        style:riderMap.get(segment.ownerId)?.style },
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
  if (highlight.history) {
    skipHighlightButton.textContent = 'SKIP REPLAY';
    showMenu();
    return;
  }
  const result=highlight.result;
  showRoundResult(result);
}
function finishOperation(event) {
  mode = event.kind;
  syncAudioMix();
  accumulator = 0;
  finishAge = 0;
  captureReplayFrame(true);
  hideCountdown();
  skipBriefButton.hidden = true;
  ui['change-mode'].hidden = false;
  const mission = getOperation(round.operation.id);
  const won = event.kind === 'won';
  if (won) {
    effectMotif('win');
    operationFlow.points += round.score;
    saveOperationProgress(mission.id, round.score);
    operationFlow.status = operationFlow.index + 1 < operationFlow.sequence.length ? 'next' : 'complete';
    if (operationFlow.status === 'complete' && operationFlow.kind === 'journey')
      saveOperationProgress('journey', operationFlow.points);
  } else {
    operationFlow.status = 'retry';
    effectMotif('crash');
  }
  loadBest();
  updateHud();
  if (operationFlow.kind === 'single' || operationFlow.status === 'complete' || !won)
    recordRun(won ? 'won' : 'lost', operationFlow.points + (won ? 0 : round.score));
  const reason = won ? mission.objective + ' Complete.' : crashMessage(event);
  const result = won
    ? 'Stage +' + round.score + ' points. ' +
      (mission.reward.look ? mission.reward.look + ' trail look and archive fragment unlocked. ' : 'Archive fragment unlocked. ') +
      (operationFlow.status === 'next' ? 'Next: ' + getOperation(operationFlow.sequence[operationFlow.index + 1]).name + '.'
        : operationFlow.kind === 'journey' ? 'Journey complete. Total ' + operationFlow.points + ' points.' : 'Best ' + best + ' points.')
    : 'Retry this operation or return to selection. ' + mission.tip;
  setOverlay(won ? 'OPERATION COMPLETE' : 'OPERATION FAILED',
    won ? 'ROUTE <span>SECURED</span>' : 'SIGNAL <span>LOST</span>',
    reason + ' ' + result,
    won ? operationFlow.status === 'next' ? 'NEXT OPERATION' : 'NEW RUN' : 'RETRY OPERATION');
  announce((won ? 'Operation complete. ' : 'Operation failed. ') + reason + ' ' + result);
}
function finishShift(event) {
  mode = event.kind;
  syncAudioMix();
  accumulator = 0;
  finishAge = 0;
  captureReplayFrame(true);
  hideCountdown();
  lessonHint.hidden = true;
  ui['rival-intent'].textContent = '';
  ui['change-mode'].hidden = false;
  if (round.score > best) {
    best = round.score;
    writeLocal(scoreKey(), String(best));
  }
  updateHud();
  recordRun(event.kind, round.score);
  const cause = event.reason === 'disc' ? 'A disc derezzed the rider.'
    : event.reason === 'timeout' ? 'The 90-second lockout ended.'
    : event.reason === 'mutual' ? 'Both riders derezzed together.'
    : event.kind === 'won' ? 'Echo derezzed.'
    : crashMessage(event);
  const result = event.kind === 'won' ? 'MATCH WON' : event.kind === 'draw' ? 'DRAW' : 'MATCH LOST';
  const deck = cause + ' You ' + round.riders[0].hp + '/2, Echo ' +
    round.riders[1].hp + '/2 integrity. Score ' + round.score +
    '. Rematch or return to mode selection.';
  setOverlay(result, event.kind === 'won' ? 'GRID <span>SECURED</span>'
    : event.kind === 'draw' ? 'GRID <span>LOCKED</span>' : 'END OF <span>LINE</span>',
    deck, 'REMATCH');
  announce(result + '. ' + deck);
  effectMotif(event.kind === 'won' ? 'win' : 'crash');
}
function finish(event) {
  if (sessionMode === 'shift') { finishShift(event); return; }
  if (sessionMode === 'operations') { finishOperation(event); return; }
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
  if (match.completed) recordRun(
    (duel ? match.wins.player >= 3 : arenaWon(match)) ? 'won' : 'lost', total);
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
    ? ' Duel '+match.wins.player+'-'+match.wins.rival+'.'
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
    } else if (event.type === 'shift-form') {
      showToast(event.riderId + (event.form === 'foot' ? ' DISMOUNTED' : ' REMOUNTED'));
      beep(event.riderId === 'YOU' ? 650 : 460, .18, 'triangle', .03);
      if (event.riderId === 'YOU') {
        runBadgeEvents.add('form-switch');
        effectMotif('turn');
        showLessonStep(event.form === 'foot'
          ? 'On foot / Movement keys steer in arena directions. Your old wall is behind you. ' +
            keyLabel('disc') + ' throws your disc.'
          : 'Cycle / Your trail is live again. ' + keyLabel('form') + ' dismounts when clear.');
        syncShiftMove();
      }
      announce(event.riderId + (event.form === 'foot' ? ' on foot.' : ' on cycle.'));
      updateShiftHud();
    } else if (event.type === 'shift-denied' && event.riderId === 'YOU') {
      showToast(event.reason.toUpperCase());
      announce(event.reason + '.');
    } else if (event.type === 'pickup') {
      if (event.riderId === 'YOU') {
        showToast(event.pickup.toUpperCase() + ' PICKED UP');
        announce(event.pickup + ' picked up.');
        updateHud();
      }
    } else if (event.type === 'arena-shrink') {
      showToast('GRID SHRINKING / ' + event.limit);
      announce('Grid shrinking. New limit ' + event.limit + '.');
    } else if (event.type === 'rocket-explode') {
      spawnImpact(event);
      showCaption('ROCKET IMPACT');
    } else if (event.type === 'bullet-fire') {
      beep(event.riderId === 'YOU' ? 930 : 690, .1, 'sawtooth', .028);
      if (event.riderId === 'YOU') announce(event.rocket ? 'Rocket fired.' : 'Bullet fired. ' + event.ammo + ' left.');
    } else if (event.type === 'bullet-cut') {
      showToast(event.riderId + ' CUT A WALL');
      effectMotif('wall');
      spawnImpact(event);
      showCaption('LIGHT WALL CUT');
      announce(event.riderId + ' cut a gap in ' + event.by + ' light wall.');
      updateHud();
    } else if (event.type === 'bullet-blocked' && event.riderId === 'YOU') {
      showToast('BULLET BLOCKED / ' + event.reason.toUpperCase());
      announce('Bullet blocked by ' + event.reason + '.');
    } else if (event.type === 'disc-throw') {
      showToast(event.riderId + ' DISC OUT');
      beep(event.riderId === 'YOU' ? 780 : 580, .13, 'sawtooth', .035);
      if (event.riderId === 'YOU') showLessonStep('Disc away / Dodge walls and wait for it to return before remounting.');
      announce(event.riderId + ' threw a disc.');
    } else if (event.type === 'disc-hit') {
      showToast(event.riderId + ' HIT / ' + event.hp + ' INTEGRITY');
      effectMotif('near');
      announce(event.riderId + ' hit by disc. ' + event.hp + ' integrity left.');
      updateHud();
    } else if (event.type === 'disc-return' && event.riderId === 'YOU') {
      showToast('DISC RETURNED');
      announce('Disc returned. You can throw again after cooldown.');
    } else if (event.type === 'operation-warning') {
      showToast(event.message);
      announce(event.message);
      effectMotif('wall');
    } else if (event.type === 'operation-checkpoint') {
      showToast(event.name.toUpperCase() + ' / ' + event.progress + '/' + event.total);
      announce(event.name + ' crossed. ' + event.progress + ' of ' + event.total + '.');
      beep(760 + event.progress * 90, 0.12, 'triangle', 0.045);
      updateHud();
    } else if (event.type === 'skill') {
      runBadgeEvents.add(event.kind);
      showToast(event.kind === 'clean-trap' ? 'CLEAN TRAP +' + event.points : 'CLOSE ESCAPE +' + event.points);
      updateHud();
    } else if (event.type === 'crash') {
      spawnImpact(event);
      spawnDerez(event);
      effectMotif('crash');
      effectMotif('derez');
      if (event.riderId !== 'YOU') {
        showToast(event.riderId + ' DEREZZED' + (sessionMode === 'shift' ? '' : ' +250'));
        updateHud();
      }
    } else if (event.type === 'round-end') {
      finish(event);
    }
  }
}

function trailMesh(out,s,elapsed){
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
function footMesh(out, rider) {
  const c = rider.color, dark = [.025, .05, .07];
  const x = rider.x, z = rider.z, y = 0;
  box(out, x, y + .47, z, .42, .75, .34, dark);
  box(out, x, y + .96, z, .38, .28, .36, mixColor(c, .38));
  box(out, x, y + 1.04, z - .17, .23, .10, .04, c, 2);
  for (const side of [-1, 1]) {
    box(out, x + side * .16, y + .16, z, .12, .32, .13, mixColor(c, .65), 2);
    box(out, x + side * .28, y + .65, z, .10, .36, .12, mixColor(c, .48), 2);
  }
  box(out, x, y + .02, z, .56, .04, .56, c, 2);
}
function shiftFormRing(out, rider, age) {
  const radius = .5 + age * 1.2;
  const c = rider.color, y = .05;
  box(out, rider.x, y, rider.z - radius, radius * 2, .045, .055, c, 2);
  box(out, rider.x, y, rider.z + radius, radius * 2, .045, .055, c, 2);
  box(out, rider.x - radius, y, rider.z, .055, .045, radius * 2, c, 2);
  box(out, rider.x + radius, y, rider.z, .055, .045, radius * 2, c, 2);
}
function discMesh(out, disc) {
  const c = round.riders.find(r => r.id === disc.ownerId)?.isPlayer ? [.48, .96, 1] : [1, .62, .28];
  const y = 0;
  box(out, disc.x, y + .75, disc.z, .52, .08, .52, c, 2);
  box(out, disc.x, y + .75, disc.z, .19, .11, .19, [.02, .06, .08]);
  box(out, disc.x, y + .03, disc.z, .58, .03, .58, mixColor(c, .48), 2);
}
function bulletMesh(out, bullet) {
  const c = bullet.rocket ? [1, .48, .13] :
    round.riders.find(r => r.id === bullet.ownerId)?.isPlayer ? [.56, 1, .96] : [1, .72, .36];
  const y = 0;
  const size = bullet.rocket ? .42 : .18;
  box(out, bullet.x, y + .54, bullet.z, size, .15, size, c, 2);
  box(out, bullet.x, y + .04, bullet.z, size + .1, .035, size + .1, mixColor(c, .4), 2);
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
function boundaryMesh(out, limit, color, height = .08) {
  for (const side of [-1,1]) {
    box(out,side*limit,height/2,0,.18,height,limit*2+.25,color,2);
    box(out,0,height/2,side*limit,limit*2+.25,height,.18,color,2);
  }
}
function operationMesh(out, scene) {
  const op = scene.operation || round.operation;
  if (!op) return;
  const mission=getOperation(op.id);
  const elapsed=scene.time ?? scene.elapsed;
  if (mission.kind === 'checkpoints') {
    mission.gates.forEach((gate,index)=>{
      const cx=(gate.minX+gate.maxX)/2, cz=(gate.minZ+gate.maxZ)/2;
      const sx=gate.maxX-gate.minX, sz=gate.maxZ-gate.minZ;
      const open=elapsed>=gate.openFrom && elapsed<gate.openUntil;
      const color=index<op.progress ? [.08,.22,.27] : open ? [.12,.95,.66] : [1,.41,.18];
      // The closed slab is the same rectangle used by swept collision.
      if(!open) box(out,cx,.55,cz,sx,1.1,sz,mixColor(color,.48));
      for(const x of [gate.minX,gate.maxX])
        box(out,x,1.2,cz,.16,2.4,sz+.12,color,2);
      for(const z of [gate.minZ,gate.maxZ])
        box(out,cx,.06,z,sx+.12,.12,.16,color,2);
      box(out,cx,2.42,cz,sx+.12,.1,sz+.12,color,2);
    });
  } else if (mission.kind === 'scanner') {
    const lane=op.beam?.x;
    const craftX=lane ?? -19 + ((elapsed*5)%38);
    // A suspended, angular search craft built from simple original geometry.
    box(out,craftX,4.6,0,3.8,.55,3.2,[.025,.095,.14]);
    box(out,craftX,5.0,0,1.3,1.0,1.3,[.12,.47,.55],2);
    for(const side of [-1,1]) {
      box(out,craftX+side*2.5,4.4,0,2.2,.24,.75,[.05,.2,.26]);
      box(out,craftX+side*3.1,3.2,0,.18,2.5,.18,[.15,.59,.64],2);
    }
    if(op.beam) {
      const color=op.beam.active ? [1,.20,.12] : [1,.74,.20];
      box(out,lane,.035,0,1.3,.07,48,color,2);
      if(op.beam.active) box(out,lane,.65,0,1.3,1.3,48,mixColor(color,.7),2);
    }
  } else if (mission.kind === 'collapse') {
    if(scene.limit < getMap(round.map).limit)
      boundaryMesh(out,scene.limit,[1,.26,.16],1.2);
    if(op.nextLimit) boundaryMesh(out,op.nextLimit,[1,.76,.23],.12);
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
  if ((sessionMode === 'shift' && player.form === 'foot') ||
      cameraMotion === 'steady' || reducedMotion.matches) {
    // A fixed north-up view avoids camera rotation during directional input.
    desired=[player.x, cameraDistance==='far' ? 30 : cameraDistance==='close' ? 23 : 27,
      player.z+12];
    aim=[player.x,.2,player.z-2];
  } else if(cameraMode==='tactical' || mode==='highlight') {
    desired=[player.x-direction[0]*8-direction[1]*2,
      cameraDistance==='far' ? 30 : cameraDistance==='close' ? 23 : 27,
      player.z-direction[1]*8+direction[0]*2];
    aim=[player.x+direction[0]*3,.2,player.z+direction[1]*3];
  } else {
    desired=[player.x-direction[0]*distance-direction[1]*5,10.2,
      player.z-direction[1]*distance+direction[0]*5];
    aim=[player.x+direction[0]*5,.55,player.z+direction[1]*5];
  }
  if(mode==='countdown' && !reducedMotion.matches && cameraMotion === 'cinematic') {
    const duration=match?.roundNumber>1 ? 1.5 : 3;
    const intro=Math.max(0,Math.min(1,(countdownRemaining-.45)/(duration-.45)));
    const side=[player.x-direction[0]*7-direction[1]*15,8.3,
      player.z-direction[1]*7+direction[0]*15];
    desired=desired.map((value,i)=>value*(1-intro)+side[i]*intro);
    aim=aim.map((value,i)=>value*(1-intro)+[player.x,1,player.z][i]*intro);
  }
  if((mode==='won'||mode==='lost') && !reducedMotion.matches && cameraMotion === 'cinematic') {
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
  const alpha=reducedMotion.matches || cameraMotion === 'steady' ? 1 : 1-Math.exp(-(mode==='ready'?2.8:6)*renderDt);
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
  gl.uniform1f(loc.flash, flashReduced() || graphicsQuality === 'low' ? 0 : flashIntensity === 'soft' ? .35 : 1);
  const palette=getMap(round.map).palette;
  gl.uniform3fv(loc.gridColor,palette.rail.map(value=>value*.23));
  gl.uniform3fv(loc.fogColor,palette.floor);
  bind(staticBuffer);
  gl.drawArrays(gl.TRIANGLES, 0, staticData.length / 7);
  const dynamic = [];
  gateMesh(dynamic);
  operationMesh(dynamic, scene);
  if (scene.limit < getMap(round.map).limit) boundaryMesh(dynamic, scene.limit, [1, .28, .12], 1.2);
  for (const pickup of scene.pickups || []) if (pickup.active) {
    const color = { ammo: [.25, .85, 1], nitro: [.25, 1, .4], rocket: [1, .54, .18] }[pickup.type];
    const shape = pickup.type === 'nitro' ? [.46, 1.1, .46]
      : pickup.type === 'rocket' ? [1.1, .48, .54] : [.78, .48, .78];
    box(dynamic, pickup.x, shape[1] / 2 + .08, pickup.z,
      shape[0], shape[1], shape[2], color, 2);
    box(dynamic, pickup.x, .04, pickup.z, 1.25, .08, 1.25, color, 2);
  }
  for (const segment of scene.segments) trailMesh(dynamic, segment, scene.time ?? scene.elapsed);
  for (const rider of scene.riders) if (rider.alive) {
    if (scene.shift && rider.form === 'foot') footMesh(dynamic, rider);
    else bikeMesh(dynamic, rider);
    if (scene.shift && !reducedMotion.matches && scene.elapsed - rider.formChangedAt < .55)
      shiftFormRing(dynamic, rider, scene.elapsed - rider.formChangedAt);
  }
  if (scene.shift) for (const disc of scene.shift.discs) discMesh(dynamic, disc);
  for (const bullet of scene.shift?.bullets || scene.bullets || []) bulletMesh(dynamic, bullet);
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
  const playLimit=scene.limit ?? round.limit;
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
  ctx.strokeStyle = playLimit < limit ? 'rgba(120,155,165,.3)' : map.palette.radar;
  ctx.lineWidth = 3;
  ctx.strokeRect(at(-limit), at(-limit), limit * 2 * scale, limit * 2 * scale);
  if(playLimit < limit) {
    ctx.strokeStyle='#ff6d53';
    ctx.strokeRect(at(-playLimit),at(-playLimit),playLimit*2*scale,playLimit*2*scale);
  }
  for (const pickup of scene.pickups || []) if (pickup.active) {
    ctx.fillStyle = { ammo: '#4ddcff', nitro: '#64ff83', rocket: '#ff9c4d' }[pickup.type];
    ctx.font = 'bold 12px Orbitron, monospace';
    ctx.textAlign = 'center';
    ctx.fillText({ ammo: 'A', nitro: 'N', rocket: 'R' }[pickup.type],
      at(pickup.x), at(pickup.z) + 4);
  }
  ctx.fillStyle = map.palette.radar;
  for (const o of map.obstacles) {
    if (o.shape === 'circle') {
      ctx.beginPath(); ctx.arc(at(o.x),at(o.z),o.radius*scale,0,Math.PI*2); ctx.fill();
    } else ctx.fillRect(at(o.minX),at(o.minZ),(o.maxX-o.minX)*scale,(o.maxZ-o.minZ)*scale);
  }
  if(scene.operation || round.operation) {
    const op=scene.operation || round.operation, mission=getOperation(op.id);
    if(mission.kind==='checkpoints') {
      mission.gates.forEach((gate,index)=>{
        const open=(scene.time ?? scene.elapsed)>=gate.openFrom && (scene.time ?? scene.elapsed)<gate.openUntil;
        ctx.strokeStyle=index<op.progress ? '#537a83' : open ? '#5ff7ab' : '#ff7b53';
        ctx.lineWidth=index===op.progress ? 3 : 2;
        ctx.strokeRect(at(gate.minX),at(gate.minZ),
          (gate.maxX-gate.minX)*scale,(gate.maxZ-gate.minZ)*scale);
        ctx.fillStyle=ctx.strokeStyle;
        ctx.font='bold 13px Rajdhani, sans-serif';
        ctx.fillText(String(index+1),at(gate.minX),at(gate.minZ)-3);
      });
    } else if(mission.kind==='scanner' && op.beam) {
      ctx.fillStyle=op.beam.active ? '#ff4c37' : '#ffd05c';
      ctx.fillRect(at(op.beam.x-.65),at(-24),1.3*scale,48*scale);
    } else if(mission.kind==='collapse' && op.nextLimit) {
      ctx.strokeStyle='#ffcc55';ctx.lineWidth=2;ctx.setLineDash([5,4]);
      ctx.strokeRect(at(-op.nextLimit),at(-op.nextLimit),
        op.nextLimit*2*scale,op.nextLimit*2*scale);
      ctx.setLineDash([]);
    }
  }
  for (const segment of scene.segments) {
    const color = segment.rider.color;
    ctx.strokeStyle = 'rgba(' +
      Math.round(color[0] * 255) + ',' +
      Math.round(color[1] * 255) + ',' +
      Math.round(color[2] * 255) + ',.7)';
    const remaining=round.rule==='decay' && !segment.active
      ? segment.closedAt+round.trailLifetime-(scene.time ?? scene.elapsed) : Infinity;
    if(remaining<=2) ctx.strokeStyle='#ffe879';
    const pattern = segment.rider.style === 'hunter' ? [9,4] :
      segment.rider.style === 'survivor' ? [2,4] :
      segment.rider.style === 'trapper' ? [8,3,2,3] : [];
    ctx.setLineDash(remaining<=2 ? [5,4] : pattern);
    ctx.lineWidth = segment.rider.style === 'trapper' ? 3.5 : 2.5;
    ctx.beginPath();
    ctx.moveTo(at(segment.x1), at(segment.z1));
    ctx.lineTo(at(segment.x2), at(segment.z2));
    ctx.stroke();
    ctx.setLineDash([]);
  }
  if (scene.shift) for (const disc of scene.shift.discs) {
    ctx.fillStyle = scene.riders.find(r => r.id === disc.ownerId)?.isPlayer ? '#b7faff' : '#ffbc7b';
    ctx.fillRect(at(disc.x)-2, at(disc.z)-2, 4, 4);
  }
  for (const bullet of scene.shift?.bullets || scene.bullets || []) {
    ctx.fillStyle = scene.riders.find(r => r.id === bullet.ownerId)?.isPlayer ? '#b7fff7' : '#ffd29a';
    ctx.fillRect(at(bullet.x)-2, at(bullet.z)-2, 4, 4);
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

function pollGamepad() {
  let pad = null;
  if (gamepadEnabled) {
    try {
      pad = Array.from(navigator.getGamepads?.() || [])
        .find(item => item?.connected && item.mapping === 'standard') || null;
    } catch { pad = null; }
  }
  if (!pad) {
    if (fireSources.delete('gamepad')) syncFire();
    if (throttleSources.delete('gamepad')) syncThrottle();
    if (gamepadMove.x || gamepadMove.z) {
      gamepadMove = { x: 0, z: 0 };
      syncShiftMove();
    }
    gamepadHeld.clear();
    return;
  }
  const button = index => !!pad.buttons[index]?.pressed;
  const axisX = pad.axes[0] || 0, axisZ = pad.axes[1] || 0;
  const active = new Set();
  if (axisX < -.55 || button(14) || button(4)) active.add('left');
  if (axisX > .55 || button(15) || button(5)) active.add('right');
  if (axisZ < -.4 || button(12)) active.add('up');
  if (axisZ > .4 || button(13)) active.add('down');
  if (button(0)) active.add('confirm');
  if (button(1)) active.add('disc');
  if (button(9)) active.add('pause');
  if (!menuHub.hidden) {
    if (fireSources.delete('gamepad')) syncFire();
    if (throttleSources.delete('gamepad')) syncThrottle();
    gamepadMove = { x: 0, z: 0 };
    gamepadHeld = active;
    return;
  }
  const fresh = action => active.has(action) && !gamepadHeld.has(action);
  if (mode === 'running') {
    if (sessionMode === 'shift') {
      gamepadMove = {
        x: Number(active.has('right')) - Number(active.has('left')),
        z: Number(active.has('down')) - Number(active.has('up')),
      };
      syncShiftMove();
      if (round.riders[0].form === 'cycle') {
        if (fresh('left')) playerTurn(-1);
        if (fresh('right')) playerTurn(1);
      }
      setThrottleSource('gamepad', round.riders[0].form === 'cycle' && active.has('up'));
      if (fresh('confirm')) { if (online) onlineAction({ type: 'form' });
        else { requestShiftForm(round, 'YOU'); handleEvents(); } }
      if (round.riders[0].form === 'foot' && fresh('disc')) {
        if (online) onlineAction({ type: 'disc' });
        else { throwShiftDisc(round, 'YOU'); handleEvents(); }
      }
      if (round.riders[0].form === 'cycle' && active.has('disc') !== fireSources.has('gamepad'))
        setFireSource('gamepad', active.has('disc'));
      else if (round.riders[0].form !== 'cycle' && fireSources.has('gamepad'))
        setFireSource('gamepad', false);
    } else {
      if (fresh('left')) playerTurn(-1);
      if (fresh('right')) playerTurn(1);
      setThrottleSource('gamepad', active.has('up'));
      if (active.has('disc') !== fireSources.has('gamepad'))
        setFireSource('gamepad', active.has('disc'));
    }
  } else {
    if (fireSources.has('gamepad')) setFireSource('gamepad', false);
    if (throttleSources.has('gamepad')) setThrottleSource('gamepad', false);
  }
  if (fresh('confirm') && ['ready', 'operation-brief', 'shift-brief', 'won', 'lost', 'paused'].includes(mode)) {
    ui.start.click();
  } else if (!online && fresh('confirm') && mode === 'countdown') launch();
  if (fresh('pause') && (mode === 'running' || mode === 'countdown' || mode === 'paused')) pause();
  gamepadHeld = active;
}
function frame(now) {
  const realDt = lastFrame ? Math.min(Math.max((now - lastFrame) / 1000, 0), 0.25) : 0;
  lastFrame = now;
  if (!reducedMotion.matches && mode !== 'paused') visualTime += realDt;
  pollGamepad();
  let launchedThisFrame = false;
  if (online && mode === 'countdown') {
    countdownRemaining = Math.max(0, (online.countdownUntil - now) / 1000);
    countdown.textContent = String(Math.max(1, Math.ceil(countdownRemaining)));
  }
  if (!online && mode === 'countdown') {
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
  if (!online && mode === 'running' && !launchedThisFrame) {
    accumulator += realDt;
    let steps = 0;
    while (accumulator >= STEP && steps < 16 && mode === 'running') {
      if (sessionMode === 'shift') stepShiftRound(round);
      else stepRound(round);
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
  if (sessionMode === 'operations' && mode === 'running') updateOperationHud();
  if (mode === 'running') updateCycleWeaponHud();
  if (sessionMode === 'shift' && mode === 'running') {
    updateShiftHud();
    if (!online) ui['rival-intent'].textContent = 'ECHO / ' + round.shift.aiIntent.toUpperCase();
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

menuHubOpen.addEventListener('click', openMenuHub);
document.querySelector('#online-settings').addEventListener('click', openMenuHub);
document.querySelector('#menu-hub-close').addEventListener('click', () => closeMenuHub());
document.querySelector('#menu-hub-backdrop').addEventListener('click', () => closeMenuHub());
for (const tab of menuTabs) tab.addEventListener('click', () => activateMenuTab(tab, false));
menuHub.addEventListener('keydown', event => {
  if (event.key === 'Escape' && !captureBinding) {
    event.preventDefault();
    event.stopPropagation();
    closeMenuHub();
    return;
  }
  const index = menuTabs.indexOf(event.target);
  if (index >= 0 && ['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
    event.preventDefault();
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? menuTabs.length - 1
      : (index + (event.key === 'ArrowRight' ? 1 : -1) + menuTabs.length) % menuTabs.length;
    activateMenuTab(menuTabs[next]);
    return;
  }
  if (event.key !== 'Tab') return;
  const items = [...menuHub.querySelectorAll('button, select, input, a, [tabindex]')]
    .filter(item => item.tabIndex >= 0 && !item.disabled && !item.closest('[hidden]') &&
      item.getClientRects().length > 0 && getComputedStyle(item).visibility === 'visible');
  const first = items[0], last = items[items.length - 1];
  if (!first) return;
  if ((event.shiftKey && document.activeElement === first) ||
      (!event.shiftKey && document.activeElement === last)) {
    event.preventDefault();
    (event.shiftKey ? last : first).focus({ preventScroll: true });
  }
});
overlay.addEventListener('keydown', event => {
  if (event.key !== 'Tab' || overlay.classList.contains('hidden')) return;
  const items = [...overlay.querySelectorAll('button, select, input, summary, a')]
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
skipBriefButton.addEventListener('click', () => { if (mode === 'operation-brief') start(); });
ui['change-mode'].addEventListener('click', () => {
  if (online) location.href = './lan.html?room=' + online.code;
  else showMenu();
});
skipLesson.addEventListener('click', showMenu);
skipHighlightButton.addEventListener('click', completeHighlight);
modeSelect.addEventListener('change', () => { writeLocal(MODE_KEY, modeSelect.value); syncSetup(); });
rivalSelect.addEventListener('change', () => { writeLocal(RIVAL_KEY, rivalSelect.value); syncSetup(); });
difficultySelect.addEventListener('change', () => { writeLocal(DIFFICULTY_KEY, difficultySelect.value); syncSetup(); });
mapSelect.addEventListener('change', () => { writeLocal(MAP_KEY, mapSelect.value); syncSetup(); });
ruleSelect.addEventListener('change', () => { writeLocal(RULE_KEY, ruleSelect.value); syncSetup(); });
operationSelect.addEventListener('change', () => { writeLocal(OPERATION_KEY, operationSelect.value); syncSetup(); });
finalRivalInput.addEventListener('change', () => { writeLocal(FINAL_RIVAL_KEY, String(finalRivalInput.checked)); syncSetup(); });
trailLookSelect.addEventListener('change', () => { lookChoice = trailLookSelect.value; writeLocal(LOOK_KEY, lookChoice); if (mode === 'ready') { applyPlayerLook(); drawRadar(); } });
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
});
renderScaleSelect.addEventListener('change', () => {
  renderScale = renderScaleSelect.value;
  writeLocal(RENDER_SCALE_KEY, renderScale);
  resize();
});
cameraMotionSelect.addEventListener('change', () => {
  cameraMotion = cameraMotionSelect.value;
  writeLocal(CAMERA_MOTION_KEY, cameraMotion);
});
flashIntensitySelect.addEventListener('change', () => {
  flashIntensity = flashIntensitySelect.value;
  writeLocal(FLASH_INTENSITY_KEY, flashIntensity);
  syncFlashSetting();
});
touchSizeSelect.addEventListener('change', () => {
  touchSize = touchSizeSelect.value;
  writeLocal(TOUCH_SIZE_KEY, touchSize);
  syncTouchControls();
});
touchPlacementSelect.addEventListener('change', () => {
  touchPlacement = touchPlacementSelect.value;
  writeLocal(TOUCH_PLACEMENT_KEY, touchPlacement);
  syncTouchControls();
});
gamepadInput.addEventListener('change', () => {
  gamepadEnabled = gamepadInput.checked;
  writeLocal(GAMEPAD_KEY, String(gamepadEnabled));
  if (!gamepadEnabled) {
    gamepadMove = { x: 0, z: 0 };
    gamepadHeld.clear();
    fireSources.delete('gamepad');
    throttleSources.delete('gamepad');
    syncFire();
    syncThrottle();
    syncShiftMove();
  }
});
autoDriveInput.addEventListener('change', () => {
  autoDrive = autoDriveInput.checked;
  writeLocal(AUTO_DRIVE_KEY, String(autoDrive));
  syncAutoDrive();
});
captionsInput.addEventListener('change', () => {
  captionsEnabled = captionsInput.checked;
  writeLocal(CAPTIONS_KEY, String(captionsEnabled));
  if (!captionsEnabled) cueCaption.hidden = true;
});
for (const button of document.querySelectorAll('[data-bind]')) {
  button.addEventListener('click', () => {
    captureBinding = button.dataset.bind;
    bindingStatus.textContent = 'Press a new key for ' + captureBinding.toUpperCase() + '. Escape cancels.';
    syncBindingButtons();
  });
}
document.querySelector('#reset-settings').addEventListener('click', () => {
  if (!confirm('Reset all controls and comfort settings to their defaults?')) return;
  try {
    for (const key of [MUTE_KEY, TOUCH_KEY, LOOK_KEY, VOLUME_KEY, MUSIC_VOLUME_KEY,
      CAMERA_KEY, CAMERA_MODE_KEY, REDUCE_FLASHES_KEY, QUALITY_KEY, BINDINGS_KEY,
      OLD_BINDINGS_KEY, PREVIOUS_BINDINGS_KEY,
      TOUCH_SIZE_KEY, TOUCH_PLACEMENT_KEY, RENDER_SCALE_KEY, CAMERA_MOTION_KEY,
      FLASH_INTENSITY_KEY, GAMEPAD_KEY, CAPTIONS_KEY, AUTO_DRIVE_KEY]) localStorage.removeItem(key);
    location.reload();
  } catch {
    storageAvailable = false;
    syncStorageStatus();
  }
});
document.querySelector('#clear-data').addEventListener('click', () => {
  if (!confirm('Delete this browser\'s Grid Run scores, run history, badges, unlocks, and settings?')) return;
  try {
    for (const key of Object.keys(localStorage))
      if (key.startsWith('tron-grid-run-')) localStorage.removeItem(key);
    location.reload();
  } catch {
    storageAvailable = false;
    syncStorageStatus();
  }
});
ui.pause.addEventListener('click', pause);
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
const forwardTouch = document.getElementById('up');
forwardTouch.addEventListener('pointerdown', event => {
  event.preventDefault();
  forwardTouch.setPointerCapture(event.pointerId);
  setThrottleSource('touch-cycle', true);
});
for (const type of ['pointerup', 'pointercancel', 'lostpointercapture'])
  forwardTouch.addEventListener(type, () => setThrottleSource('touch-cycle', false));
canvas.addEventListener('pointerdown', () => {
  if (!online && mode === 'countdown') launch();
});
window.addEventListener('keydown', event => {
  const key = event.key.toLowerCase();
  if (captureBinding) {
    event.preventDefault();
    if (key === 'escape') {
      captureBinding = null;
      bindingStatus.textContent = 'Binding change cancelled.';
    } else {
      const next = key === ' ' ? 'space' : key;
      if (!validBinding(next)) bindingStatus.textContent = 'Use a letter, number, or Space. Arrows remain movement shortcuts.';
      else if (Object.entries(bindings).some(([action, value]) => action !== captureBinding && value === next))
        bindingStatus.textContent = 'That key is already assigned. Choose another key.';
      else {
        bindings[captureBinding] = next;
        writeLocal(BINDINGS_KEY, JSON.stringify(bindings));
        bindingStatus.textContent = captureBinding.toUpperCase() + ' is now ' + keyLabel(captureBinding) + '.';
        captureBinding = null;
      }
    }
    syncBindingButtons();
    return;
  }
  if (!menuHub.hidden) return;
  const action = keyAction(event.key);
  const shiftDirection = ['up', 'left', 'down', 'right'].includes(action);
  if (event.repeat && !(sessionMode === 'shift' && mode === 'running' && shiftDirection)) return;
  const interactive = event.target instanceof Element &&
    event.target.closest('button, input, select, textarea, a, summary');
  const editing = event.target instanceof Element &&
    event.target.closest('input, select, textarea, [contenteditable]');
  if (mode === 'highlight' && (event.key === 'Escape' || event.key === 'Enter' || event.key === ' ')) {
    event.preventDefault();
    completeHighlight();
    return;
  }
  if (sessionMode === 'shift' && mode === 'running' && !interactive) {
    if (action === 'form') {
      event.preventDefault();
      if (online) onlineAction({ type: 'form' });
      else { requestShiftForm(round, 'YOU'); handleEvents(); }
      return;
    }
    if (action === 'fire') {
      event.preventDefault();
      if (round.riders[0].form === 'cycle') setFireSource('keyboard', true);
      return;
    }
    if (action === 'disc') {
      event.preventDefault();
      if (online) onlineAction({ type: 'disc' });
      else {
        if (!throwShiftDisc(round, 'YOU'))
          showToast(round.riders[0].form === 'cycle' ? 'DISMOUNT TO THROW' : 'DISC NOT READY');
        handleEvents();
      }
      return;
    }
    if (shiftDirection) {
      event.preventDefault();
      if (round.riders[0].form === 'cycle') {
        if (action === 'up') setThrottleSource('keyboard', true);
        else if (!event.repeat && action === 'left') playerTurn(-1);
        else if (!event.repeat && action === 'right') playerTurn(1);
      } else {
        shiftKeys.add(action);
        syncShiftMove();
      }
      return;
    }
  }
  if (mode === 'running' && !interactive && action === 'fire') {
    event.preventDefault();
    setFireSource('keyboard', true);
  } else if (mode === 'running' && !interactive && shiftDirection) {
    event.preventDefault();
    if (action === 'up') setThrottleSource('keyboard', true);
    else if (action === 'left') playerTurn(-1);
    else if (action === 'right') playerTurn(1);
  } else if ((action === 'pause' || event.key === 'Escape') && !editing) {
    event.preventDefault();
    pause();
  } else if (action === 'sound' && !interactive) {
    ui.sound.click();
  } else if (!interactive && (event.key === 'Enter' || event.key === ' ') && mode === 'countdown') {
    event.preventDefault();
    if (!online) launch();
  } else if (!interactive && (event.key === 'Enter' || event.key === ' ') &&
      (mode === 'ready' || mode === 'operation-brief' || mode === 'shift-brief' ||
       mode === 'won' || mode === 'lost')) {
    event.preventDefault();
    start();
  }
});
window.addEventListener('keyup', event => {
  const action = keyAction(event.key);
  if (action) shiftKeys.delete(action);
  if (action === 'fire') setFireSource('keyboard', false);
  if (action === 'up') setThrottleSource('keyboard', false);
  syncShiftMove();
});
window.addEventListener('blur', () => {
  shiftKeys.clear();
  gamepadMove = { x: 0, z: 0 };
  gamepadHeld.clear();
  fireSources.clear();
  throttleSources.clear();
  syncFire();
  syncThrottle();
  syncShiftMove();
});
for (const button of shiftTouch.querySelectorAll('[data-move]')) {
  const move = button.dataset.move;
  button.addEventListener('pointerdown', event => {
    event.preventDefault();
    if (sessionMode !== 'shift' || mode !== 'running') return;
    button.setPointerCapture(event.pointerId);
    if (round.riders[0].form === 'cycle') {
      if (move === 'up') setThrottleSource('touch-shift', true);
      else if (move === 'left') playerTurn(-1);
      else if (move === 'right') playerTurn(1);
    } else {
      shiftKeys.add(move);
      syncShiftMove();
    }
  });
  const release = () => {
    shiftKeys.delete(move);
    if (move === 'up') setThrottleSource('touch-shift', false);
    syncShiftMove();
  };
  button.addEventListener('pointerup', release);
  button.addEventListener('pointercancel', release);
  button.addEventListener('lostpointercapture', release);
}
document.querySelector('#shift-transform').addEventListener('pointerdown', event => {
  event.preventDefault();
  if (sessionMode === 'shift' && mode === 'running') {
    if (online) onlineAction({ type: 'form' });
    else { requestShiftForm(round, 'YOU'); handleEvents(); }
  }
});
for (const [id, source] of [['cycle-fire', 'touch-cycle'], ['shift-throw', 'touch-shift']]) {
  const button = document.querySelector('#' + id);
  button.addEventListener('pointerdown', event => {
    event.preventDefault();
    if (mode !== 'running') return;
    if (id === 'shift-throw' && round.riders[0].form === 'foot') {
      if (online) onlineAction({ type: 'disc' });
      else { throwShiftDisc(round, 'YOU'); handleEvents(); }
      return;
    }
    if (id === 'cycle-fire' && sessionMode === 'shift') return;
    setFireSource(source, true);
    button.setPointerCapture?.(event.pointerId);
  });
  const release = () => setFireSource(source, false);
  button.addEventListener('pointerup', release);
  button.addEventListener('pointercancel', release);
  button.addEventListener('lostpointercapture', release);
}
document.addEventListener('visibilitychange', () => {
  if (document.hidden && !online && (mode === 'running' || mode === 'countdown')) pause();
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

modeSelect.value = ['duel', 'arena', 'operations', 'shift', 'daily'].includes(readLocal(MODE_KEY)) ? readLocal(MODE_KEY) : 'duel';
operationSelect.value = ['portal', 'sweep', 'collapse', 'journey'].includes(readLocal(OPERATION_KEY)) ? readLocal(OPERATION_KEY) : 'portal';
finalRivalInput.checked = readLocal(FINAL_RIVAL_KEY) !== 'false';
syncLookOptions();
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
renderScaleSelect.value = renderScale;
cameraMotionSelect.value = cameraMotion;
flashIntensitySelect.value = flashIntensity;
touchSizeSelect.value = touchSize;
touchPlacementSelect.value = touchPlacement;
gamepadInput.checked = gamepadEnabled;
autoDriveInput.checked = autoDrive;
captionsInput.checked = captionsEnabled;
syncBindingButtons();
syncStorageStatus();
syncSoundButton();
syncTouchControls();
showMenu();
if (online) beginOnline();
drawRadar();
requestAnimationFrame(frame);
} catch (error) {
  console.error('Grid graphics initialization failed:', error);
  showWebglError('The Grid could not start graphics. Try reloading or another browser.');
}
}
