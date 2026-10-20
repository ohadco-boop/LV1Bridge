const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const sent = [], requests = [];
let payload = '', fetchCount = 0, blockedFetch = null;
const context = vm.createContext({
  AbortController, setTimeout, clearTimeout,
  atob: s => Buffer.from(s, 'base64').toString('binary'),
  btoa: s => Buffer.from(s, 'binary').toString('base64'),
  fetch: async url => {
    requests.push(decodeURIComponent(new URL(url).pathname.slice(1)));
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
  const controlState = (aux, user) => ['STATE', '-1',
    ...Array.from({length: 24}, (_, i) => Buffer.from(i ? `MON ${i + 1}` : aux).toString('base64')),
    ...Array.from({length: 16}, (_, i) => Buffer.from(i ? `USER ${i + 1}` : user).toString('base64'))
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
  console.log('Stream Deck: live MON/USER/VU labels, meter rendering, serialized polling, and one command per press/release passed.');
})().catch(error => { console.error(error); process.exitCode = 1; });
