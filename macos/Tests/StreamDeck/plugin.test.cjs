const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const sent = [], requests = [];
let payload = '', fetchCount = 0, blockedFetch = null, onFetch = () => {};
const context = vm.createContext({
  AbortController, setTimeout, clearTimeout, setInterval, clearInterval, console,
  atob: s => Buffer.from(s, 'base64').toString('binary'),
  btoa: s => Buffer.from(s, 'binary').toString('base64'),
  fetch: async url => {
    requests.push(decodeURIComponent(new URL(url).pathname.slice(1)));
    onFetch(requests.at(-1));
    fetchCount++;
    if (blockedFetch) await blockedFetch;
    return {text: async () => payload};
  },
  capture: message => sent.push(JSON.parse(message)),
});
const plugin = path.join(__dirname, '../../StreamDeck/com.lv1.faderbank.sdPlugin/plugin.js');
vm.runInContext(fs.readFileSync(plugin, 'utf8'), context);
vm.runInContext("ws={readyState:1,send:capture};page='meters';keys.set('key1',{slot:0});", context);

function state(name, mono, withFlags = true) {
  const fields = ['METERS', '1'];
  for (let i = 0; i < 16; i++) {
    fields.push(Buffer.from(i === 0 ? name : `Channel ${i + 1}`).toString('base64'), '70', '80');
  }
  if (withFlags) fields.push(...Array(16).fill(mono ? '1' : '0'));
  return fields.join('|');
}
function lastSVG() {
  const image = sent.filter(e => e.event === 'setImage').at(-1);
  assert.equal(image.context, 'key1');
  return Buffer.from(image.payload.image.split(',')[1], 'base64').toString('utf8');
}

(async () => {
  payload = state('Original', false);
  await vm.runInContext('refresh()', context);
  assert.match(lastSVG(), />Original</);
  assert.equal((lastSVG().match(/height="3"/g) || []).length, 40);

  payload = state('שירה | <Lead> & "Mic"', true);
  await vm.runInContext('refresh()', context);
  assert.match(lastSVG(), /שירה \| &lt;Lead&gt; &amp; &quot;Mic&quot;/);
  assert.match(lastSVG(), /MONO · 1/);
  assert.equal((lastSVG().match(/height="3"/g) || []).length, 20);
  assert.doesNotMatch(lastSVG(), />Original</);

  payload = state('Older bridge', true, false);
  await vm.runInContext('refresh()', context);
  assert.match(lastSVG(), /L · 1 · R/);

  payload = state('—', false);
  await vm.runInContext('refresh()', context);
  assert.equal((lastSVG().match(/height="3"/g) || []).length, 0);

  let finish;
  blockedFetch = new Promise(resolve => { finish = resolve; });
  const before = fetchCount;
  const pending = vm.runInContext('refresh()', context);
  await vm.runInContext('refresh()', context);
  assert.equal(fetchCount, before + 1, 'Only one metadata poll may be in flight');
  payload = state('Next bank', false);
  blockedFetch = null;
  finish();
  await pending;
  assert.match(lastSVG(), />Next bank</);

  // Monitor and user-key labels refresh through STATE, independently of VU.
  const controlState = (aux, user, active = -1) => ['STATE', String(active),
    ...Array.from({length: 24}, (_, i) => Buffer.from(i ? `MON ${i + 1}` : aux).toString('base64')),
    ...Array.from({length: 16}, (_, i) => Buffer.from(i ? `USER ${i + 1}` : user).toString('base64')),
    ...Array.from({length: 8}, (_, i) => Buffer.from(i ? `FX ${i + 1}` : 'Reverb').toString('base64'))
  ].join('|');
  vm.runInContext("page='aux';", context);
  payload = controlState('OHAD', 'Talkback');
  await vm.runInContext('refresh()', context);
  assert.match(lastSVG(), />OHAD</);
  payload = controlState('שירה & Monitor', 'מקרופון <Talk>');
  await vm.runInContext('refresh()', context);
  assert.match(lastSVG(), />שירה &amp; Monitor</);
  vm.runInContext("page='user';", context);
  await vm.runInContext('refresh()', context);
  assert.match(lastSVG(), />מקרופון &lt;Talk&gt;</);
  // One press/release is one command; profile display events and meter-only
  // keys must not perform mixer actions.
  requests.length = 0;
  vm.runInContext("page='aux';", context);
  for (const event of ['willAppear', 'keyDown', 'keyUp']) {
    vm.runInContext(`handle(${JSON.stringify({
      event, context: 'aux3', payload: {coordinates: {row: 0, column: 2}}
    })})`, context);
  }
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(requests.filter(x => /^(AUX |USER |MIX$)/.test(x)), ['AUX 3']);

  requests.length = 0;
  vm.runInContext("page='user';", context);
  for (const event of ['keyDown', 'keyUp']) {
    vm.runInContext(`handle(${JSON.stringify({
      event, context: 'user16', payload: {coordinates: {row: 1, column: 7}}
    })})`, context);
  }
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(requests.filter(x => /^(AUX |USER |MIX$)/.test(x)), ['USER 16']);

  requests.length = 0;
  vm.runInContext("page='meters';", context);
  await vm.runInContext('press(0)', context);
  assert.deepEqual(requests.filter(x => /^(AUX |USER |MIX$)/.test(x)), []);

  // FX page uses 8 distinct sends, without changing MON numbering.
  vm.runInContext("keys.clear();keys.set('key1',{slot:0});page='aux';", context);
  payload = controlState('OHAD', 'Talkback', 24);
  await vm.runInContext('press(29)', context);
  await new Promise(resolve => setImmediate(resolve));
  assert.match(lastSVG(), />Reverb</);
  assert.match(lastSVG(), /#14532d/);
  requests.length = 0;
  for(let i=0;i<8;i++) await vm.runInContext(`press(${i})`, context);
  await vm.runInContext('press(8)', context);
  assert.deepEqual(requests.filter(x=>/^FX /.test(x)), Array.from({length:8},(_,i)=>`FX ${i+1}`));
  assert.equal(requests.filter(x=>/^(AUX |USER |MIX$)/.test(x)).length,0);

  // Real Node timers must retrieve state that arrives after the command's ACK,
  // without another button press. This regresses the hidden HTML runtime bug.
  const manifest = JSON.parse(fs.readFileSync(path.join(path.dirname(plugin),'manifest.json')));
  assert.equal(manifest.CodePath,'plugin.js');
  assert.equal(manifest.Nodejs.Version,'24');
  context.WebSocket = class {
    constructor() { this.readyState=1; }
    send(message) { sent.push(JSON.parse(message)); }
  };
  payload = controlState('OHAD','Talkback');
  requests.length=0;
  vm.runInContext("keys.clear();keys.set('key1',{slot:2});page='aux';connectElgatoStreamDeckSocket('1234','test','registerPlugin');ws.onopen();",context);
  await new Promise(resolve=>setImmediate(resolve));
  onFetch = command => {
    if(command==='AUX 3') setTimeout(()=>{payload=controlState('OHAD','Talkback',2)},120);
  };
  await vm.runInContext('press(2)',context);
  await new Promise(resolve=>setTimeout(resolve,650));
  vm.runInContext('ws.onclose()',context);
  assert.match(lastSVG(), /#14532d/, 'Selected key updates after delayed backend completion');
  assert.ok(requests.filter(x=>x==='STATE').length>=3, 'Polling continues without a key event');
  assert.deepEqual(requests.filter(x=>/^AUX /.test(x)),['AUX 3']);
  console.log('Stream Deck: delayed selection feedback, Node polling, FX page, names, meters and one command per press passed.');
})().catch(error => { console.error(error); process.exitCode = 1; });
