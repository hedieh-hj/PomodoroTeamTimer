const $ = id => document.getElementById(id);
const RS = String.fromCharCode(0x1e);
let access = JSON.parse(localStorage.getItem('roomAccess') || 'null');
let state = null, socket = null, timerHandle = null, heartbeatHandle = null, invocationId = 0;

document.addEventListener('DOMContentLoaded', async () => {
  $('createForm').addEventListener('submit', createRoom);
  $('joinForm').addEventListener('submit', joinRoom);
  $('notificationButton').addEventListener('click', requestNotifications);
  $('copyCode').addEventListener('click', copyCode);
  $('leaveButton').addEventListener('click', leaveRoom);
  for (const cmd of ['start','pause','resume','reset','end']) $(cmd+'Button').addEventListener('click', () => command(cmd));
  $('skipButton').addEventListener('click', () => command('skip-break'));
  if ('serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js').catch(()=>{});
  await loadStats();
  if (access?.code && access?.memberId) await enterRoom();
});

async function api(path, options={}) {
  const response = await fetch(path, { headers:{'Content-Type':'application/json'}, ...options });
  if (!response.ok) {
    let message = 'خطایی رخ داد؛ دوباره تلاش کنید.';
    try { message = (await response.json()).message || message; } catch {}
    throw new Error(message);
  }
  return response.status === 204 ? null : response.json();
}

async function createRoom(e) {
  e.preventDefault();
  const f = new FormData(e.currentTarget);
  try {
    access = await api('/api/rooms', {method:'POST', body:JSON.stringify({
      title:f.get('title'), hostName:f.get('hostName'), focusMinutes:+f.get('focusMinutes'),
      breakMinutes:+f.get('breakMinutes'), totalRounds:+f.get('totalRounds')
    })});
    persist(); await enterRoom();
  } catch (err) { toast(err.message); }
}

async function joinRoom(e) {
  e.preventDefault();
  const f = new FormData(e.currentTarget), code = toEnglishDigits(f.get('code')).trim();
  try {
    access = await api(`/api/rooms/${code}/join`, {method:'POST', body:JSON.stringify({name:f.get('name'), memberId:null})});
    persist(); await enterRoom();
  } catch (err) { toast(err.message); }
}

async function enterRoom() {
  try {
    state = await api(`/api/rooms/${access.code}`);
    $('landing').classList.add('hidden'); $('room').classList.remove('hidden');
    $('hostControls').classList.toggle('hidden', !access.hostToken);
    render(); connectLive(); startClock();
  } catch { leaveRoom(); toast('این اتاق دیگر در دسترس نیست.'); }
}

async function connectLive() {
  if (socket) try { socket.close(); } catch {}
  try {
    const n = await fetch('/hubs/room/negotiate?negotiateVersion=1', {method:'POST'}).then(r=>r.json());
    const scheme = location.protocol === 'https:' ? 'wss' : 'ws';
    socket = new WebSocket(`${scheme}://${location.host}/hubs/room?id=${encodeURIComponent(n.connectionToken)}`);
    socket.onopen = () => {
      socket.send(JSON.stringify({protocol:'json',version:1}) + RS);
      setTimeout(() => invoke('Subscribe', [access.code, access.memberId]), 30);
      clearInterval(heartbeatHandle);
      heartbeatHandle = setInterval(() => invoke('Heartbeat',[access.code,access.memberId]), 15000);
    };
    socket.onmessage = event => event.data.split(RS).filter(Boolean).forEach(handleSignalR);
    socket.onclose = () => { clearInterval(heartbeatHandle); if(access) setTimeout(connectLive, 2500); };
  } catch { if(access) setTimeout(connectLive, 2500); }
}

function handleSignalR(raw) {
  const msg = JSON.parse(raw);
  if (msg.type !== 1) return;
  if (msg.target === 'roomUpdated') { state = msg.arguments[0]; render(); }
  const alerts = {
    breakStarted:['وقت استراحت است','دور تمرکز تمام شد؛ کمی نفس تازه کنید.'],
    focusStarted:['زمان تمرکز','دور جدید شروع شد. آماده‌اید؟'],
    sessionCompleted:['جلسه تمام شد','خسته نباشید! جلسهٔ تیمی به پایان رسید.']
  };
  if (alerts[msg.target]) notify(...alerts[msg.target]);
}

function invoke(target,args) {
  if (socket?.readyState === WebSocket.OPEN) socket.send(JSON.stringify({type:1,invocationId:String(++invocationId),target,arguments:args})+RS);
}

async function command(name) {
  try { state = await api(`/api/rooms/${access.code}/${name}`, {method:'POST',body:JSON.stringify({hostToken:access.hostToken})}); render(); }
  catch(err) { toast(err.message); }
}

function render() {
  if (!state) return;
  const phases = {ready:'آماده برای شروع',focus:'زمان تمرکز',break:'زمان استراحت',completed:'جلسه پایان یافت'};
  $('phaseLabel').textContent = phases[state.phase]; $('roomTitle').textContent = state.title; $('roomCode').textContent = state.code;
  $('roundText').textContent = `دور ${Math.min(state.currentRound,state.totalRounds)} از ${state.totalRounds}`;
  $('roundDots').innerHTML = Array.from({length:state.totalRounds},(_,i)=>`<i class="dot ${i<state.currentRound?'active':''}"></i>`).join('');
  $('timerCard').classList.toggle('break',state.phase==='break');
  $('timerHint').textContent = state.phase==='ready' ? 'منتظر شروع میزبان' : state.phase==='completed' ? 'خسته نباشید!' : state.isRunning ? (state.phase==='focus'?'روی یک کار بمانید':'کمی استراحت کنید') : 'تایمر متوقف شده است';
  const online = state.members.filter(x=>x.online).length;
  $('onlineCount').textContent = `${toFa(online)} نفر آنلاین`;
  $('members').innerHTML = state.members.map(m=>`<div class="member"><div class="avatar">${escapeHtml(m.name.slice(0,1))}</div><div class="member-info"><strong>${escapeHtml(m.name)}${m.id===access.memberId?' (شما)':''}</strong><small>${toFa(m.completedPomodoros)} پومودورو تکمیل‌شده</small></div><i class="status ${m.online?'online':''}"></i></div>`).join('');
  const host = !!access.hostToken;
  if(host){
    $('startButton').classList.toggle('hidden',state.phase!=='ready');
    $('pauseButton').classList.toggle('hidden',!state.isRunning || state.phase==='completed');
    $('resumeButton').classList.toggle('hidden',state.isRunning || !['focus','break'].includes(state.phase));
    $('skipButton').classList.toggle('hidden',state.phase!=='break');
    $('endButton').classList.toggle('hidden',state.phase==='completed');
  }
  updateClock();
}

function startClock(){ clearInterval(timerHandle); timerHandle=setInterval(updateClock,250); updateClock(); }
function updateClock(){
  if(!state)return;
  let seconds=state.remainingSeconds;
  if(state.isRunning && state.endsAtUtc) seconds=Math.max(0,Math.ceil((new Date(state.endsAtUtc)-Date.now())/1000));
  $('timer').textContent=`${String(Math.floor(seconds/60)).padStart(2,'0')}:${String(seconds%60).padStart(2,'0')}`;
  document.title=`${$('timer').textContent} · ${state.title}`;
}

async function requestNotifications(){
  if(!('Notification' in window)) return toast('مرورگر شما اعلان را پشتیبانی نمی‌کند.');
  const result=await Notification.requestPermission(); toast(result==='granted'?'اعلان‌ها فعال شدند.':'اجازهٔ اعلان داده نشد.');
}
function notify(title,body){
  playBell();
  if('Notification' in window && Notification.permission==='granted') new Notification(title,{body,tag:'pomodoro-phase'});
}
function playBell(){
  try{const c=new(window.AudioContext||window.webkitAudioContext)(),o=c.createOscillator(),g=c.createGain();o.connect(g);g.connect(c.destination);o.frequency.setValueAtTime(660,c.currentTime);o.frequency.setValueAtTime(880,c.currentTime+.18);g.gain.setValueAtTime(.12,c.currentTime);g.gain.exponentialRampToValueAtTime(.001,c.currentTime+.55);o.start();o.stop(c.currentTime+.55)}catch{}
}
async function loadStats(){try{const s=await api('/api/stats/today');$('todayStats').textContent=`امروز: ${toFa(s.completedPomodoros)} پومودورو · ${toFa(Math.round(s.focusSeconds/60))} دقیقه تمرکز · ${toFa(s.sessions)} جلسه`;}catch{}}
function copyCode(){navigator.clipboard?.writeText(state.code);toast('کد اتاق کپی شد.');}
function leaveRoom(){access=null;state=null;persist();clearInterval(timerHandle);clearInterval(heartbeatHandle);if(socket)socket.close();location.href='/';}
function persist(){access?localStorage.setItem('roomAccess',JSON.stringify(access)):localStorage.removeItem('roomAccess');}
function toast(text){const el=$('toast');el.textContent=text;el.classList.add('show');setTimeout(()=>el.classList.remove('show'),2600)}
function toEnglishDigits(v){return String(v).replace(/[۰-۹]/g,d=>'۰۱۲۳۴۵۶۷۸۹'.indexOf(d)).replace(/[٠-٩]/g,d=>'٠١٢٣٤٥٦٧٨٩'.indexOf(d))}
function toFa(v){return String(v).replace(/\d/g,d=>'۰۱۲۳۴۵۶۷۸۹'[d])}
function escapeHtml(v){const d=document.createElement('div');d.textContent=v;return d.innerHTML}
