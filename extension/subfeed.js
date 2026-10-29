// 롤업 자막 병합기. content script에서 쓰고, test/subfeed.test.js 가 node에서 그대로 불러 검증한다.
'use strict';

// 줄이 위에서부터 부분 갱신되는 자막(YouTube 자동 자막 등)에서 옛 줄과 새 줄이 섞여 보이는 것을 막는다.
// - 한 틱 동안 안정된 스냅샷만 받는다(바뀌는 중간 상태는 건너뜀).
// - 이전 스냅샷에 없던 줄만 아래에 이어 붙인다(이미 있던 줄은 위치만 밀림).
// - 같은 줄이 길어지거나(단어 단위) 줄바꿈이 다시 나뉘면(리플로우) 끝부분을 통째로 교체한다.
// - 이전과 겹치는 줄이 하나도 없으면 새 문장으로 보고 기록을 비운다.
function makeSubFeed() {
  let hist = [], prev = [], pend = null, emptySince = 0;
  // p가 l로 "길어진" 것인가. 짧은 접두사("Well" → "Well, that's…")는 우연이므로 제외한다.
  const grows = (p, l) => l.length > p.length && l.startsWith(p) && p.length >= Math.max(4, l.length * 0.4);
  const tailEq = () => prev.length > 0 && hist.length >= prev.length &&
    prev.every((p, i) => hist[hist.length - prev.length + i] === p);
  return {
    reset() { hist = []; prev = []; pend = null; emptySince = 0; },
    // now는 시험용으로 바꿀 수 있게 인자로 받는다.
    update(lines, isTrack, now = Date.now()) {
      if (isTrack) { hist = lines.slice(); prev = []; pend = null; return; } // 표준 자막은 현재 큐 그대로
      const key = lines.join('\n');
      if (key !== pend) { pend = key; return; }                 // 바뀌는 중간 상태는 건너뛴다
      if (!lines.length) {
        if (!emptySince) emptySince = now;
        if (now - emptySince > 2000) { hist = []; prev = []; }  // 잠깐 비는 동안은 롤업을 이어 간다
        return;
      }
      emptySince = 0;
      if (prev.length && key === prev.join('\n')) return;
      const jp = prev.join(' '), jn = lines.join(' ');
      if (tailEq() && (jn === jp || (jn.startsWith(jp) && jp.length >= Math.max(4, jn.length * 0.4)))) {
        hist = hist.slice(0, hist.length - prev.length).concat(lines); // 리플로우·단어 단위 성장: 끝부분 교체
      } else {
        const continuing = lines.some(l => prev.includes(l) || prev.some(p => grows(p, l)));
        if (!continuing) hist = [];
        const known = continuing ? prev : [];
        for (const l of lines) {
          if (known.includes(l)) continue;                      // 이미 보여 준 줄
          const gi = known.findIndex(p => grows(p, l));
          const idx = gi >= 0 ? hist.lastIndexOf(known[gi]) : -1;
          if (idx >= 0) hist[idx] = l; else hist.push(l);       // 길어진 줄은 제자리 교체, 새 줄은 추가
        }
      }
      prev = lines;
      if (hist.length > 20) hist = hist.slice(-20);
    },
    get(n) { return hist.slice(-n); },
  };
}

if (typeof module !== 'undefined') module.exports = { makeSubFeed };
