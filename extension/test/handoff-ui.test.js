'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const h = require('../handoff.js');

// Minimal DOM/event double exercises public dialog behavior; no browser or external URL opens.
class Element {
  constructor(doc, tag) { this.doc = doc; this.tagName = tag.toUpperCase(); this.children = []; this.parentNode = null; this.events = new Map(); this.attrs = new Map(); this.style = {}; this.dataset = {}; this.isConnected = false; this.checked = false; this.disabled = false; this.textContent = ''; doc.nodes.push(this); }
  append(...items) { for (const item of items) { item.parentNode = this; item.connect?.(this.isConnected); this.children.push(item); } }
  connect(value) { this.isConnected = value; for (const child of this.children) child.connect?.(value); this.shadow?.connect(value); }
  remove() { if (this.parentNode) this.parentNode.children = this.parentNode.children.filter(x => x !== this); this.connect(false); }
  setAttribute(name, value) { this.attrs.set(name, String(value)); }
  getAttribute(name) { return this.attrs.get(name) ?? null; }
  removeAttribute(name) { this.attrs.delete(name); }
  set href(value) { this.setAttribute('href', value); }
  get href() { return this.getAttribute('href') || ''; }
  attachShadow() { this.shadow = new Element(this.doc, 'shadow-root'); this.shadow.parentNode = this; this.shadow.host = this; return this.shadow; }
  addEventListener(type, fn) { if (!this.events.has(type)) this.events.set(type, []); this.events.get(type).push(fn); }
  focus() { this.doc.activeElement = this; for (let p = this.parentNode; p; p = p.parentNode) if (p.host) { p.activeElement = this; this.doc.activeElement = p.host; } }
  querySelectorAll() { const out = []; const walk = node => { for (const c of node.children) { if ((c.tagName === 'INPUT' && !c.disabled) || (c.tagName === 'A' && c.href) || c.tagName === 'BUTTON') out.push(c); walk(c); } }; walk(this); return out; }
  fire(type, extra = {}) {
    const event = { target: this, isTrusted: true, defaultPrevented: false, stopped: false, preventDefault() { this.defaultPrevented = true; }, stopPropagation() { this.stopped = true; }, ...extra };
    for (let p = this; p; p = p.parentNode) { if (p.shadow) event.target = p; for (const fn of p.events.get(type) || []) fn(event); if (event.stopped) break; }
    return event;
  }
  set innerHTML(_) { throw new Error('page-controlled HTML must not be used'); }
}
function fixture(pageUrl = 'https://example.test/watch', currentSrc = 'https://example.test/video.mp4') {
  const doc = { nodes: [], createElement(tag) { return new Element(this, tag); }, createTextNode(text) { const n = new Element(this, 'text'); n.textContent = text; return n; }, activeElement: null };
  doc.documentElement = new Element(doc, 'html'); doc.documentElement.connect(true);
  const track = { mode: 'showing', kind: 'subtitles' };
  const video = { isConnected: true, currentSrc, currentTime: 12, playbackRate: 1.25, textTracks: [track], querySelectorAll: () => [{ track, src: 'https://example.test/ko.vtt' }], pause() { throw new Error('source must not be paused'); } };
  const context = { HPP_HANDOFF: h, document: doc };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '..', 'handoff-ui.js'), 'utf8') + '\nthis.api = HPP_HANDOFF_UI;', context);
  context.api.open(video, pageUrl, doc);
  const get = predicate => doc.nodes.find(n => n.isConnected && predicate(n));
  return { doc, video, api: context.api, get, option: key => get(n => n.dataset.option === key), launch: get(n => n.tagName === 'A'), panel: get(n => n.tagName === 'SECTION') };
}
let passed = 0;
function test(name, run) { run(); passed++; console.log('PASS ' + name); }
test('mock defaults send time only', () => {
  const f = fixture(); assert.equal(f.option('time').checked, true);
  for (const option of ['subtitle', 'speed', 'pip']) assert.equal(f.option(option).checked, false);
  assert.deepEqual([...new URL(f.launch.href).searchParams.keys()], ['v', 'url', 't']);
});
test('inner shadow click preserves dialog', () => { const f = fixture(); f.option('time').fire('click'); assert.equal(f.api.isOpen, true); });
test('untrusted final click cannot navigate', () => { const f = fixture(); assert.equal(f.launch.fire('click', { isTrusted: false }).defaultPrevented, true); });
test('trusted final click resamples time and leaves source playing', () => { const f = fixture(); f.video.currentTime = 44.5; assert.equal(f.launch.fire('click').defaultPrevented, false); assert.equal(new URL(f.launch.href).searchParams.get('t'), '44.5'); });
test('source replacement requires second reviewed click', () => { const f = fixture(); f.video.currentSrc = 'https://example.test/replaced.mp4'; assert.equal(f.launch.fire('click').defaultPrevented, true); assert.equal(f.launch.fire('click').defaultPrevented, false); });
test('SPA page changes with same blob require refreshed preview', () => {
  let page = 'https://example.test/watch/old';
  const f = fixture(() => page, 'blob:https://example.test/unchanged');
  assert.equal(new URL(f.launch.href).searchParams.get('url'), page);
  page = 'https://example.test/watch/new'; f.video.currentTime = 23;
  assert.equal(f.launch.fire('click').defaultPrevented, true);
  assert.equal(new URL(f.launch.href).searchParams.get('url'), page);
  assert.equal(f.launch.fire('click').defaultPrevented, false);
  const query = new URL(f.launch.href).searchParams;
  assert.equal(query.get('url'), page); assert.equal(query.get('t'), '23');
});
test('checked metadata reaches encoded request', () => { const f = fixture(); for (const key of ['subtitle', 'speed', 'pip']) { f.option(key).checked = true; f.option(key).fire('change'); } const q = new URL(f.launch.href).searchParams; assert.equal(q.get('sub'), 'https://example.test/ko.vtt'); assert.equal(q.get('speed'), '1.25'); assert.equal(q.get('pip'), '1'); });
test('disconnected video cannot launch', () => { const f = fixture(); f.video.isConnected = false; assert.equal(f.launch.fire('click').defaultPrevented, true); assert.equal(f.launch.href, ''); });
test('Space stays in dialog without preventing checkbox default', () => {
  const f = fixture(); let siteKeys = 0;
  f.doc.documentElement.addEventListener('keydown', () => siteKeys++);
  const event = f.option('time').fire('keydown', { key: ' ' });
  assert.equal(siteKeys, 0); assert.equal(event.stopped, true); assert.equal(event.defaultPrevented, false);
});
test('Escape closes owned dialog', () => { const f = fixture(); f.panel.fire('keydown', { key: 'Escape' }); assert.equal(f.api.isOpen, false); });
console.log(`PASS ${passed} handoff dialog behavior checks (DOM double, not browser verification)`);
