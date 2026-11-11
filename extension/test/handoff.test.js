'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const h = require('../handoff.js');
const fixtures = JSON.parse(fs.readFileSync(require('node:path').join(__dirname, 'fixtures', 'handoff-contract.json'), 'utf8'));
let passed = 0;
function test(name, run) { run(); passed++; console.log('PASS ' + name); }
for (const item of fixtures.dtoCases) test('DTO ' + item.id, () => assert.equal(h.validate(item.input).ok, item.accept));
for (const item of fixtures.urlCases) test('URL ' + item.id, () => assert.equal(h.publicUrl(item.input).ok, item.accept));
for (const item of fixtures.snapshotCases) test('snapshot ' + item.id, () => {
  const actual = h.fromSnapshot(item.snapshot, item.include);
  if (item.reject) assert.equal(actual.ok, false);
  else { assert.equal(actual.ok, true, actual.error); assert.deepEqual(actual.value, item.expected); }
});
for (const item of fixtures.rawEnvelopeCases.filter(item => item.id.includes('surrogate')))
  test('raw JSON ' + item.id, () => assert.equal(h.validate(JSON.parse(item.input)).ok, false));
test('well-formed Unicode retained and lone surrogates rejected', () => {
  assert.equal(h.publicUrl('https://example.test/😀.mp4').ok, true);
  for (const surrogate of ['\uD800', '\uDBFF', '\uDC00', '\uDFFF']) {
    assert.equal(h.publicUrl('https://example.test/' + surrogate).ok, false);
    assert.equal(h.validate({ v: 1, url: 'https://example.test/v', sub: 'https://example.test/' + surrogate }).ok, false);
  }
});
test('once-encoded protocol and exact false', () => {
  const dto = { v: 1, url: 'https://example.test/watch?id=a%2Fb&lang=ko', t: 0, sub: 'https://example.test/ko.vtt', speed: 1.25, pip: false };
  const result = h.protocol(dto); assert.equal(result.ok, true);
  const wire = new URL(result.href);
  assert.equal(wire.protocol, 'hybridpip:'); assert.equal(wire.hostname, 'play');
  assert.equal(wire.searchParams.get('url'), dto.url);
  assert.equal(wire.searchParams.get('sub'), dto.sub);
  assert.equal(wire.searchParams.get('pip'), '0'); assert.equal(wire.searchParams.get('t'), '0');
  assert.deepEqual([...wire.searchParams.keys()], ['v', 'url', 't', 'sub', 'speed', 'pip']);
});
test('omitted metadata stays omitted', () => assert.deepEqual([...new URL(h.protocol({ v: 1, url: 'https://example.test/v' }).href).searchParams.keys()], ['v', 'url']));
test('exact URL byte bound', () => { const base = 'https://example.test/'; assert.equal(h.publicUrl(base + 'a'.repeat(4096 - base.length)).ok, true); assert.equal(h.publicUrl(base + 'a'.repeat(4097 - base.length)).ok, false); });
test('Unicode UTF8 URL bound', () => assert.equal(h.publicUrl('https://example.test/' + '한'.repeat(1500)).ok, false));
test('encoded envelope bound independent of URLs', () => {
  // '#' is prohibited; a path of '[' is valid and encodes threefold in URLSearchParams.
  const url = 'https://example.test/' + '['.repeat(4000);
  assert.equal(h.publicUrl(url).ok, true);
  assert.equal(h.validate({ v: 1, url, sub: url }).ok, true);
  assert.equal(h.protocol({ v: 1, url, sub: url }).ok, false);
});
for (const value of [NaN, Infinity, -Infinity]) test('nonfinite numbers ' + value, () => {
  assert.equal(h.validate({ v: 1, url: 'https://example.test/v', t: value }).ok, false);
  assert.equal(h.validate({ v: 1, url: 'https://example.test/v', speed: value }).ok, false);
});
test('own prototype-key rejected', () => { const dto = Object.create(null); dto.v = 1; dto.url = 'https://example.test/v'; dto.__proto__ = {}; assert.equal(h.validate(dto).ok, false); });
test('raw actual controls rejected before URL cleanup', () => { for (const c of ['\r', '\n', '\0', '\t']) assert.equal(h.publicUrl('https://example.test/v' + c).ok, false); });
test('protected video uses page rather than transport source', () => {
  const result = h.fromSnapshot({ connected: true, protected: true, currentSrc: 'https://example.test/encrypted.mp4', pageUrl: 'https://example.test/watch/1', currentTime: 10, playbackRate: 1 }, { time: true });
  assert.equal(result.value.url, 'https://example.test/watch/1'); assert.equal(result.sourceKind, 'page');
});
test('safe external track is matched by object identity', () => {
  const selected = { kind: 'subtitles', mode: 'showing' }, disabled = { kind: 'subtitles', mode: 'disabled' };
  const video = { isConnected: true, currentSrc: 'https://example.test/v.mp4', currentTime: 4, playbackRate: 1.5, textTracks: [disabled, selected], querySelectorAll: () => [{ track: disabled, src: 'https://example.test/wrong.vtt' }, { track: selected, src: 'https://example.test/right.vtt' }] };
  assert.equal(h.videoSnapshot(video, 'https://example.test/page').subtitleSrc, 'https://example.test/right.vtt');
});
test('ambiguous hidden tracks are not guessed', () => {
  const a = { kind: 'subtitles', mode: 'hidden' }, b = { kind: 'captions', mode: 'hidden' };
  const video = { isConnected: true, textTracks: [a, b], querySelectorAll: () => [{ track: a, src: 'https://example.test/a.vtt' }] };
  assert.equal(h.videoSnapshot(video, 'https://example.test/page').subtitleSrc, undefined);
});
console.log(`PASS ${passed} handoff preparation checks (pure JS; no browser launch)`);
