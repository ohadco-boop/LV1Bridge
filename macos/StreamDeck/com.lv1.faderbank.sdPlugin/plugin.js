let ws, uuid, page = "aux", activeAux = -1, fontSize = 21;
const keys = new Map(), auxNames = Array.from({length:24},(_,i)=>`MON ${i+1}`);
const userNames = Array.from({length:16},(_,i)=>`USER ${i+1}`), meterNames = Array(16).fill("-");
const fxNames = Array.from({length:8},(_,i)=>`FX ${i+1}`);
const meterL = Array(16).fill(0), meterR = Array(16).fill(0);
const meterMono = Array(16).fill(false);
const rendered = new Map();
let refreshing = false, pollTimer, commandQueue = Promise.resolve();

function connectElgatoStreamDeckSocket(port, pluginUUID, registerEvent) {
  uuid = pluginUUID; ws = new WebSocket(`ws://127.0.0.1:${port}`);
  ws.onopen = () => {
    ws.send(JSON.stringify({event:registerEvent, uuid}));
    refresh();
    clearInterval(pollTimer);
    pollTimer = setInterval(refresh, 250);
  };
  ws.onmessage = e => handle(JSON.parse(e.data));
  ws.onclose = () => { clearInterval(pollTimer); };
  ws.onerror = error => console.error('Stream Deck connection error', error.message);
}

function handle(e) {
  if (!['willAppear','willDisappear','keyDown'].includes(e.event)) return;
  if (e.event === 'willDisappear') { keys.delete(e.context); rendered.delete(e.context); return; }
  const c=e.payload.coordinates||{row:0,column:0}, slot=c.row*8+c.column;
  keys.set(e.context,{slot});
  if(e.event==='keyDown') press(slot);
  else { rendered.delete(e.context); draw(e.context,slot); }
}

async function bridge(command) {
  const controller = new AbortController(), timeout = setTimeout(() => controller.abort(), 3000);
  try { return await (await fetch(`http://127.0.0.1:49631/${encodeURIComponent(command)}`,{cache:'no-store',signal:controller.signal})).text(); }
  catch(_) { return 'ERR'; }
  finally { clearTimeout(timeout); }
}

async function press(slot) {
  if(slot===28){page=page==='meters'?'aux':'meters'; redraw(); return;}
  if(slot===29){page=page==='fx'?'aux':'fx'; redraw(); refresh(); return;}
  if(slot===25){page=page==='aux'?'user':'aux'; redraw(); return;}
  if(slot===26||slot===27){fontSize=Math.max(14,Math.min(40,fontSize+(slot===27?2:-2)));redraw();return;}
  let command;
  if(slot===24) command='MIX';
  else if(page==='aux'&&slot<24) command=`AUX ${slot+1}`;
  else if(page==='user'&&slot<16) command=`USER ${slot+1}`;
  else if(page==='fx'&&slot<8) command=`FX ${slot+1}`;
  if(!command) return;
  // Preserve physical press order even if two HTTP requests have different latency.
  commandQueue = commandQueue.then(async () => { await bridge(command); await refresh(); });
  await commandQueue;
}

async function refresh() {
  // A delayed older poll must not overwrite names from a more recent bank.
  if(refreshing) return;
  refreshing = true;
  try {
    const command=page==='meters'?'METERS':'STATE', parts=(await bridge(command)).split('|');
    if(parts[0]==='STATE'&&parts.length>=42){activeAux=parseInt(parts[1]);for(let i=0;i<24;i++)auxNames[i]=decode(parts[2+i]);for(let i=0;i<16;i++)userNames[i]=decode(parts[26+i]);}
    if(parts[0]==='STATE'&&parts.length>=50){for(let i=0;i<8;i++)fxNames[i]=decode(parts[42+i]);}
    if(parts[0]==='METERS'&&parts.length>=50){for(let i=0;i<16;i++){meterNames[i]=decode(parts[2+i*3]);meterL[i]=+parts[3+i*3];meterR[i]=+parts[4+i*3];meterMono[i]=parts.length>=66&&parts[50+i]==='1';}}
    redraw();
  } finally { refreshing = false; }
}
function decode(s){try{return decodeURIComponent(escape(atob(s)))}catch(_){return '-'}}
function redraw(){for(const [context,k] of keys)draw(context,k.slot)}

function draw(context,slot){
  let title='',active=false,bg='#172554';
  if(slot===24){title='MAIN\nMIX';active=activeAux<0;bg='#713f12'}
  else if(slot===25){title=page==='aux'?'USER\nKEYS':'AUX\nSENDS';bg='#164e63'}
  else if(slot===26){title=`A−\n${fontSize}`;bg='#164e63'}
  else if(slot===27){title=`A+\n${fontSize}`;bg='#164e63'}
  else if(slot===28){title=page==='meters'?'AUX\nSENDS':'VU\nMETERS';bg='#164e63'}
  else if(slot===29){title=page==='fx'?'AUX\nSENDS':'FX\n1–8';bg='#164e63'}
  else if(page==='meters'&&slot<16){setImage(context,meterSVG(slot));return}
  else if(page==='user'&&slot<16){title=userNames[slot];bg='#3b1d5c'}
  else if(page==='aux'&&slot<24){title=auxNames[slot];active=activeAux===slot}
  else if(page==='fx'&&slot<8){title=fxNames[slot];active=activeAux===24+slot}
  setImage(context,textSVG(title,bg,active));
}
function send(event,context,payload){if(ws&&ws.readyState===1)ws.send(JSON.stringify({event,context,payload}))}
function setImage(context,svg){if(rendered.get(context)===svg)return;rendered.set(context,svg);send('setTitle',context,{title:'',target:0});send('setImage',context,{image:'data:image/svg+xml;base64,'+btoa(unescape(encodeURIComponent(svg))),target:0})}
function esc(s){return String(s||'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&apos;'}[c]))}
function textSVG(title,bg,active){const lines=String(title).split('\n');return `<svg xmlns="http://www.w3.org/2000/svg" width="144" height="144"><rect width="144" height="144" rx="18" fill="${active?'#14532d':bg}"/><rect x="7" y="7" width="130" height="130" rx="14" fill="none" stroke="${active?'#4ade80':'#38bdf8'}" stroke-width="4"/>${lines.map((x,i)=>`<text x="72" y="${70+(i-(lines.length-1)/2)*fontSize}" text-anchor="middle" fill="white" font-family="Arial" font-size="${fontSize}" font-weight="bold">${esc(x)}</text>`).join('')}</svg>`}
function meterSVG(i){
  let bars='', empty=meterNames[i]==='—', mono=meterMono[i];
  for(let lane=0;lane<(empty?0:mono?1:2);lane++)for(let n=0;n<20;n++){
    const on=n<Math.ceil((lane?meterR[i]:meterL[i])*20/127),color=on?(n>=18?'#ef4444':n>=14?'#facc15':'#22c55e'):'#1b2935';
    bars+=`<rect x="${mono?48:40+lane*34}" y="${108-n*4}" width="${mono?48:27}" height="3" fill="${color}"/>`;
  }
  const footer=empty?'':mono?`MONO · ${i+1}`:`L · ${i+1} · R`;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="144" height="144"><rect width="144" height="144" rx="12" fill="#080f18"/><text x="72" y="24" text-anchor="middle" fill="white" font-family="Arial" font-size="18">${esc(meterNames[i])}</text>${bars}<text x="72" y="132" text-anchor="middle" fill="#94a3b8" font-family="Arial" font-size="13">${footer}</text></svg>`;
}

// Run in Stream Deck's Node.js runtime. HTML plugin timers can be suspended
// while their Chromium page is hidden, leaving selection feedback one press behind.
if (typeof require !== 'undefined' && require.main === module) {
  const args = new Map();
  for (let i=2; i<process.argv.length; i+=2) args.set(process.argv[i],process.argv[i+1]);
  const port=args.get('-port'), id=args.get('-pluginUUID'), event=args.get('-registerEvent');
  if(!/^\d+$/.test(port||'') || !id || !event) {
    console.error('Missing Stream Deck registration arguments');
    process.exitCode=1;
  } else connectElgatoStreamDeckSocket(port,id,event);
}
