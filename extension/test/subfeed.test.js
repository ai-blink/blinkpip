// node extension/test/subfeed.test.js — makeSubFeed 시나리오 시험(브라우저 불필요).
'use strict';
const assert = require('node:assert/strict');
const { makeSubFeed } = require('../subfeed.js');

let t = 0; // 가짜 시계(ms)
// 스냅샷은 두 번 연속 같아야 반영되므로 두 번 넣는다.
const push = (f, lines, dt = 100) => { t += dt; f.update(lines, false, t); t += dt; f.update(lines, false, t); };
const cases = [];
const test = (name, fn) => cases.push([name, fn]);

test('롤업: 위부터 갱신되는 중간 상태가 섞이지 않는다', () => {
  const f = makeSubFeed();
  push(f, ['A1', 'A2']);
  push(f, ['C1', 'A2']); // 위 줄만 먼저 바뀜
  push(f, ['C1', 'C2']);
  assert.deepEqual(f.get(5), ['A1', 'A2', 'C1', 'C2']);
});

test('불안정한 중간 상태(한 번만 보임)는 무시된다', () => {
  const f = makeSubFeed();
  push(f, ['A1', 'A2']);
  t += 100; f.update(['C1', 'A2'], false, t); // 한 번만
  push(f, ['C1', 'C2']);
  assert.deepEqual(f.get(5), ['C1', 'C2']);   // 겹침이 없으니 새 문장으로 시작
});

test('리플로우: 같은 내용이 줄바꿈만 달라지면 중복되지 않는다', () => {
  const f = makeSubFeed();
  push(f, ['hello world foo']);
  push(f, ['hello world', 'foo']);
  assert.deepEqual(f.get(5), ['hello world', 'foo']);
});

test('단어 단위로 길어지는 줄은 제자리 교체', () => {
  const f = makeSubFeed();
  push(f, ['D1 hello']);
  push(f, ['D1 hello world']);
  push(f, ['D1 hello world again']);
  assert.deepEqual(f.get(5), ['D1 hello world again']);
});

test('짧은 접두사("Well")는 새 문장과 섞이지 않는다', () => {
  const f = makeSubFeed();
  push(f, ['Well']);
  push(f, ["Well, that's the whole thing"]);
  assert.deepEqual(f.get(5), ["Well, that's the whole thing"]);
});

test('겹침 없는 새 문장은 기록을 비운다', () => {
  const f = makeSubFeed();
  push(f, ['첫 문장 첫 줄', '첫 문장 둘째 줄']);
  push(f, ['전혀 다른 문장']);
  assert.deepEqual(f.get(5), ['전혀 다른 문장']);
});

test('반복되는 줄("Yeah.")이 버려지지 않는다', () => {
  const f = makeSubFeed();
  push(f, ['Yeah.']);
  push(f, ['Something else entirely']);
  push(f, ['Yeah.']);
  assert.deepEqual(f.get(1), ['Yeah.']);
});

test('잠깐 비어도 롤업이 이어진다(2초 이내)', () => {
  const f = makeSubFeed();
  push(f, ['A1', 'A2']);
  push(f, []);                 // 약 0.4초 빈 화면
  push(f, ['A2', 'B1']);       // A2가 이어짐 → 롤업 유지
  assert.deepEqual(f.get(5), ['A1', 'A2', 'B1']);
});

test('2초 넘게 비면 기록이 지워진다', () => {
  const f = makeSubFeed();
  push(f, ['A1']);
  push(f, [], 1200);
  push(f, [], 1200); // 빈 상태가 안정된 시점부터 2초 넘게 유지
  assert.deepEqual(f.get(5), []);
});

test('표준 자막(TextTrack)은 현재 큐를 그대로, 빈 큐는 즉시 비운다', () => {
  const f = makeSubFeed();
  f.update(['큐 하나'], true);
  assert.deepEqual(f.get(5), ['큐 하나']);
  f.update([], true);
  assert.deepEqual(f.get(5), []);
});

test('줄 수 N만큼만 돌려준다(가장 최근 N줄)', () => {
  const f = makeSubFeed();
  push(f, ['1', '2']);
  push(f, ['2', '3']);
  push(f, ['3', '4']);
  assert.deepEqual(f.get(2), ['3', '4']);
  assert.deepEqual(f.get(1), ['4']);
});

test('seek 후 reset 하면 처음부터', () => {
  const f = makeSubFeed();
  push(f, ['A1']);
  f.reset();
  assert.deepEqual(f.get(5), []);
});

let fail = 0;
for (const [name, fn] of cases) {
  try { fn(); console.log('통과 ·', name); } catch (e) { fail++; console.log('실패 ·', name, '\n   ', e.message.split('\n').join('\n    ')); }
}
console.log(fail ? `\n${fail}건 실패` : `\n전체 ${cases.length}건 통과`);
process.exit(fail ? 1 : 0);
