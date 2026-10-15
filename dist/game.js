const canvas = document.querySelector('#game');
const radar = document.querySelector('#radar');
const overlay = document.querySelector('#overlay');
const ui = Object.fromEntries(['score','best','rivals','eyebrow','title','deck','start','toast','sound','pause'].map(id => [id, document.getElementById(id)]));
const gl = canvas.getContext('webgl', { antialias: true, alpha: false, powerPreference: 'high-performance' });
if (!gl) {
  document.body.insertAdjacentHTML('beforeend', '<div class="webgl-error">This Grid needs WebGL. Please enable hardware acceleration or try another browser.</div>');
  throw new Error('WebGL unavailable');
}

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
  player: [0.15, 0.88, 1.0], orange: [1.0, 0.38, 0.09], violet: [0.75, 0.30, 1.0], gold: [1.0, 0.73, 0.20],
  floor: [0.008, 0.026, 0.045], dark: [0.015, 0.038, 0.064], wall: [0.018, 0.072, 0.10]
};
const DIR = [[0,-1],[1,0],[0,1],[-1,0]];
const LIMIT = 29;
const SCORE_KEY = 'tron-grid-run-best';
let best = Number(localStorage.getItem(SCORE_KEY) || 0);
let score = 0, mode = 'ready', elapsed = 0, visualTime = 0, lastFrame = 0, lastTurn = -9, toastTimer = 0;
let riders = [], segments = [], camera = [7, 13, 30], target = [0, 0, 0], muted = false, audio;

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
  box(staticData, s*30.5,.62,0,.34,1.24,61,C.wall);
  box(staticData, 0,.62,s*30.5,61,1.24,.34,C.wall);
  box(staticData, s*30.5,1.26,0,.18,.10,61,[.06,.66,.82],2);
  box(staticData, 0,1.26,s*30.5,61,.10,.18,[.06,.66,.82],2);
  for(let i=-3;i<=3;i++) {
    box(staticData,s*30.5,1.2,i*8,.48,2.4,.48,[.025,.18,.25]);
    box(staticData,i*8,1.2,s*30.5,.48,2.4,.48,[.025,.18,.25]);
    box(staticData,s*30.5,2.34,i*8,.5,.08,.5,[.13,.85,1],2);
    box(staticData,i*8,2.34,s*30.5,.5,.08,.5,[.13,.85,1],2);
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

function addRider(id,x,z,dir,color,isPlayer=false) {
  const rider={id,x,z,dir,color,isPlayer,alive:true,nextDecision:0};
  riders.push(rider);
  rider.segment={rider,x1:x,z1:z,x2:x,z2:z,active:true,closedAt:-1};
  segments.push(rider.segment);
}
function reset() {
  riders=[];segments=[];score=0;elapsed=0;lastTurn=-9;
  addRider('YOU',0,16,0,C.player,true);
  addRider('RINZLER',-17,-16,1,C.orange);
  addRider('SIREN',17,-14,3,C.violet);
  addRider('GOLD',16,14,0,C.gold);
  camera=[7,13,31];target=[0,0,0];
  updateHud();drawRadar();
}
function updateHud() {
  ui.score.textContent=String(score).padStart(5,'0');
  ui.best.textContent=String(best).padStart(5,'0');
  ui.rivals.textContent=String(riders.filter(r=>!r.isPlayer&&r.alive).length).padStart(2,'0');
}
function setOverlay(eyebrow,title,deck,button) {
  ui.eyebrow.textContent=eyebrow;ui.title.innerHTML=title;ui.deck.textContent=deck;ui.start.textContent=button;
  overlay.classList.remove('hidden');
}
function hideOverlay(){overlay.classList.add('hidden');}
function showToast(text) {
  ui.toast.textContent=text;ui.toast.classList.add('show');
  clearTimeout(toastTimer);toastTimer=setTimeout(()=>ui.toast.classList.remove('show'),1500);
}
function ensureAudio(){if(!audio)try{audio=new (window.AudioContext||window.webkitAudioContext)();}catch{muted=true;}}
function beep(freq,duration=.11,type='sawtooth',gain=.035){
  if(muted)return;ensureAudio();if(!audio)return;
  if(audio.state==='suspended')audio.resume();
  const o=audio.createOscillator(),g=audio.createGain(),t=audio.currentTime;
  o.type=type;o.frequency.setValueAtTime(freq,t);o.frequency.exponentialRampToValueAtTime(Math.max(50,freq*.57),t+duration);
  g.gain.setValueAtTime(gain,t);g.gain.exponentialRampToValueAtTime(.0001,t+duration);
  o.connect(g).connect(audio.destination);o.start(t);o.stop(t+duration+.01);
}
function finish(won){
  if(mode!=='running')return;
  mode=won?'won':'lost';
  if(won){score+=1000;beep(700,.22,'triangle',.07);setTimeout(()=>beep(1100,.35,'triangle',.06),150);}
  else beep(120,.5,'sawtooth',.08);
  if(score>best){best=score;localStorage.setItem(SCORE_KEY,String(best));}
  updateHud();
  setTimeout(()=>setOverlay(won?'ALL RIVALS DEREZZED':'YOUR TRAIL ENDS HERE',won?'GRID <span>MASTER<small>01</small></span>':'END OF <span>LINE<small>01</small></span>',won?`You ruled the arena with ${score.toLocaleString()} points. A fresh Grid is waiting.`:`You scored ${score.toLocaleString()} points. Read the trails, time your turns, and run it again.`,'RUN AGAIN'),550);
}
function start(){
  reset();mode='running';hideOverlay();ui.pause.textContent='Ⅱ';ui.pause.setAttribute('aria-label','Pause game');
  beep(440,.1,'triangle');setTimeout(()=>beep(690,.15,'triangle'),110);
  showToast('SURVIVE THE GRID');
}
function pause(){
  if(mode==='running'){
    mode='paused';ui.pause.textContent='▶';ui.pause.setAttribute('aria-label','Resume game');
    setOverlay('CONNECTION HELD','GRID <span>PAUSED<small>01</small></span>','Your light trail is waiting. Return when you are ready.','RESUME');
  } else if(mode==='paused'){
    mode='running';hideOverlay();ui.pause.textContent='Ⅱ';ui.pause.setAttribute('aria-label','Pause game');
    lastFrame=performance.now();
  }
}
function turn(r,sign){
  if(!r.alive)return;
  r.segment.active=false;r.segment.closedAt=elapsed;
  r.dir=(r.dir+sign+4)%4;
  r.segment={rider:r,x1:r.x,z1:r.z,x2:r.x,z2:r.z,active:true,closedAt:-1};
  segments.push(r.segment);
  if(r.isPlayer){lastTurn=elapsed;beep(sign<0?560:720,.09,'triangle',.04);}
}
function playerTurn(sign){if(mode==='running'&&elapsed-lastTurn>.16)turn(riders[0],sign);}
function collision(r,x,z,look=false){
  if(Math.abs(x)>LIMIT||Math.abs(z)>LIMIT)return true;
  for(const s of segments){
    if(s===r.segment)continue;
    if(s.rider===r&&elapsed-s.closedAt<.43)continue;
    const minx=Math.min(s.x1,s.x2)-.23,maxx=Math.max(s.x1,s.x2)+.23;
    const minz=Math.min(s.z1,s.z2)-.23,maxz=Math.max(s.z1,s.z2)+.23;
    if(x>=minx&&x<=maxx&&z>=minz&&z<=maxz)return true;
  }
  if(!look)for(const other of riders){
    if(other!==r&&other.alive&&Math.hypot(x-other.x,z-other.z)<.7)return true;
  }
  return false;
}
function freeDistance(r,dir){
  const d=DIR[dir];
  for(let a=.7;a<=10;a+=.55)if(collision(r,r.x+d[0]*a,r.z+d[1]*a,true))return a;
  return 10;
}
function aiTurn(r){
  if(elapsed<r.nextDecision)return;
  r.nextDecision=elapsed+.16+Math.random()*.10;
  const ahead=freeDistance(r,r.dir);
  if(ahead<4.5){
    const left=freeDistance(r,(r.dir+3)%4),right=freeDistance(r,(r.dir+1)%4);
    if(left>1.4||right>1.4)turn(r,left>right?-1:1);
  } else if(ahead<8&&Math.random()<.035){
    const sign=Math.random()<.5?-1:1;
    if(freeDistance(r,(r.dir+sign+4)%4)>7)turn(r,sign);
  }
}
function update(dt){
  elapsed+=dt;
  for(const r of riders){
    if(!r.alive)continue;
    if(!r.isPlayer)aiTurn(r);
    const d=DIR[r.dir],speed=r.isPlayer?10.6:8.8+(.4*Math.sin(elapsed*.8+r.x));
    const nx=r.x+d[0]*speed*dt,nz=r.z+d[1]*speed*dt;
    if(collision(r,nx,nz)){
      r.alive=false;r.segment.active=false;r.segment.closedAt=elapsed;
      if(r.isPlayer){finish(false);break;}
      score+=250;beep(185,.22,'sawtooth',.045);showToast(`${r.id} DEREZZED  +250`);updateHud();
    }else{r.x=nx;r.z=nz;r.segment.x2=nx;r.segment.z2=nz;}
  }
  if(mode==='running'&&riders.slice(1).every(r=>!r.alive))finish(true);
  drawRadar();
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
function draw(){
  resize();
  const p=riders[0],d=DIR[p.dir],desired=[p.x-d[0]*14-d[1]*5,10.2,p.z-d[1]*14+d[0]*5];
  const aim=[p.x+d[0]*5,.55,p.z+d[1]*5];
  const smooth=mode==='ready'?.045:.095;
  camera=camera.map((v,i)=>v+(desired[i]-v)*smooth);
  target=target.map((v,i)=>v+(aim[i]-v)*smooth);
  const matrix=multiply(perspective(Math.PI/3,canvas.width/canvas.height,.1,145),lookAt(camera,target));
  gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);
  gl.uniformMatrix4fv(loc.matrix,false,matrix);gl.uniform3fv(loc.camera,camera);gl.uniform1f(loc.time,visualTime);
  bind(staticBuffer);gl.drawArrays(gl.TRIANGLES,0,staticData.length/7);
  const dynamic=[];
  for(const s of segments)trailMesh(dynamic,s);
  for(const r of riders)if(r.alive)bikeMesh(dynamic,r);
  gl.bindBuffer(gl.ARRAY_BUFFER,dynamicBuffer);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(dynamic),gl.DYNAMIC_DRAW);
  bind(dynamicBuffer);gl.drawArrays(gl.TRIANGLES,0,dynamic.length/7);
}
function drawRadar(){
  const ctx=radar.getContext('2d'),w=radar.width,h=radar.height,k=w/64,at=v=>(v+32)*k;
  ctx.clearRect(0,0,w,h);ctx.fillStyle='#07121d';ctx.fillRect(0,0,w,h);
  ctx.strokeStyle='rgba(78,220,243,.12)';ctx.lineWidth=1;
  for(let i=0;i<=8;i++){let p=i*w/8;ctx.beginPath();ctx.moveTo(p,0);ctx.lineTo(p,h);ctx.moveTo(0,p);ctx.lineTo(w,p);ctx.stroke();}
  ctx.strokeStyle='rgba(94,223,243,.5)';ctx.lineWidth=3;ctx.strokeRect(at(-29),at(-29),58*k,58*k);
  for(const s of segments){
    const c=s.rider.color;ctx.strokeStyle=`rgba(${Math.round(c[0]*255)},${Math.round(c[1]*255)},${Math.round(c[2]*255)},.7)`;
    ctx.lineWidth=2.5;ctx.beginPath();ctx.moveTo(at(s.x1),at(s.z1));ctx.lineTo(at(s.x2),at(s.z2));ctx.stroke();
  }
  for(const r of riders)if(r.alive){
    const c=r.color;ctx.fillStyle=`rgb(${Math.round(c[0]*255)},${Math.round(c[1]*255)},${Math.round(c[2]*255)})`;
    ctx.beginPath();ctx.arc(at(r.x),at(r.z),r.isPlayer?5:3.5,0,Math.PI*2);ctx.fill();
  }
}
function frame(now){
  const dt=Math.min((now-(lastFrame||now))/1000,.05);lastFrame=now;visualTime+=dt;
  if(mode==='running')update(dt);
  draw();requestAnimationFrame(frame);
}

ui.start.addEventListener('click',()=>mode==='paused'?pause():start());
ui.pause.addEventListener('click',pause);
ui.sound.addEventListener('click',()=>{muted=!muted;ui.sound.textContent=muted?'♪̸':'♫';ui.sound.setAttribute('aria-label',muted?'Unmute sound':'Mute sound');if(!muted)beep(650);});
document.getElementById('left').addEventListener('pointerdown',e=>{e.preventDefault();playerTurn(-1);});
document.getElementById('right').addEventListener('pointerdown',e=>{e.preventDefault();playerTurn(1);});
window.addEventListener('keydown',e=>{
  if(['ArrowLeft','ArrowRight',' ','ArrowUp','ArrowDown'].includes(e.key))e.preventDefault();
  if(e.repeat)return;
  if(e.key==='ArrowLeft'||e.key.toLowerCase()==='a')playerTurn(-1);
  else if(e.key==='ArrowRight'||e.key.toLowerCase()==='d')playerTurn(1);
  else if(e.key.toLowerCase()==='p'||e.key==='Escape')pause();
  else if(e.key.toLowerCase()==='m')ui.sound.click();
  else if((e.key==='Enter'||e.key===' ')&&mode!=='running')mode==='paused'?pause():start();
});
document.addEventListener('visibilitychange',()=>{if(document.hidden&&mode==='running')pause();});
window.addEventListener('resize',resize);
reset();requestAnimationFrame(frame);
