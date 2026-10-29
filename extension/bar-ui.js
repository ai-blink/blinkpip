// 재생 줄 UI 공용 모듈(PiP+ 창, 무테용 컨트롤 창이 같이 쓴다).
// player 인터페이스만 알아서, 영상이 같은 페이지에 있든(PiP+) 다른 창에 있든(컨트롤 창) 같은 코드로 그린다.
//   player.state()        → { paused, currentTime, duration(라이브면 Infinity), muted, volume, rate }
//   player.toggle() / seekBy(초) / seekTo(0~1) / setRate(r) / setVolume(v) / toggleMute()
//   player.onChange(cb)   → 상태가 바뀔 때 cb 호출, 해제 함수를 돌려줌
'use strict';

// DOM API로만 만든다(Trusted Types를 강제하는 사이트에서 innerHTML이 막히기 때문).
function el(doc, tag, props = {}, ...kids) {
  const e = doc.createElement(tag);
  for (const [k, v] of Object.entries(props)) {
    if (k === 'text') e.textContent = v; else if (k === 'style') e.style.cssText = v; else e.setAttribute(k, v);
  }
  kids.forEach(k => e.append(k));
  return e;
}

const fmt = s => {
  if (!isFinite(s)) return '0:00';
  s = Math.floor(s);
  const h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), ss = String(s % 60).padStart(2, '0');
  return h ? `${h}:${String(m).padStart(2, '0')}:${ss}` : `${m}:${ss}`;
};

const BAR_CSS = `
  *{scrollbar-width:none}
  *::-webkit-scrollbar{display:none}
  #bar{flex:none;display:flex;align-items:center;gap:5px;padding:5px 8px;background:#1b1b1f;flex-wrap:wrap}
  #bar button{background:#2c2c33;color:#eee;border:1px solid #444;border-radius:4px;padding:3px 7px;cursor:pointer;white-space:nowrap}
  #bar button:hover{background:#3a3a44}
  #seek{flex:1 1 100%;margin:0}
  #time{min-width:84px;text-align:center}
  #rate{min-width:0;padding:3px 6px}
  .pop{position:fixed;display:none;z-index:9;background:#1b1b1fee;border:1px solid #555;border-radius:6px;padding:6px 8px}
  .pop.open{display:block}
  #ratePop{padding:2px}
  #ratePop button{display:block;width:100%;text-align:center;background:transparent;color:#eee;border:0;border-radius:3px;padding:2px 10px;cursor:pointer;font-size:12px}
  #ratePop button:hover{background:#3a3a44}
  #ratePop button.cur{background:#2a5db0}`;

// opts: seekSeconds() → 건너뛰기 초 / onOrig() → '원본' 버튼 / extra: 속도 버튼 뒤에 넣을 요소들 /
//       pops: 같은 방식으로 여닫을 추가 팝업 / popAbove: 팝업을 버튼 위로 열지(true) 아래로 열지
function buildBar(doc, player, opts) {
  const win = doc.defaultView;
  const { seekSeconds, onOrig, extra = [], pops = [], popAbove = true } = opts;
  const mk = (tag, props, ...kids) => el(doc, tag, props, ...kids);
  const seek = mk('input', { id: 'seek', type: 'range', min: '0', max: '1000', value: '0' });
  const back = mk('button', { id: 'back' }), play = mk('button', { id: 'play', text: '▶' }), fwd = mk('button', { id: 'fwd' });
  const time = mk('span', { id: 'time', text: '0:00 / 0:00' });
  const rate = mk('button', { id: 'rate', text: '1x', title: '재생 속도' });
  const mute = mk('button', { id: 'mute', text: '🔊' });
  const vol = mk('input', { id: 'vol', type: 'range', min: '0', max: '1', step: '0.05', style: 'width:64px;margin:0' });
  const orig = mk('button', { id: 'orig', text: '↩ 원본', title: opts.origTitle || '원래 탭으로 영상 되돌리기' });
  const bar = mk('div', { id: 'bar' }, seek, back, play, fwd, time, rate, ...extra, mute, vol, orig);
  const ratePop = mk('div', { id: 'ratePop', class: 'pop' });
  const allPops = [ratePop, ...pops];

  const closePops = () => allPops.forEach(p => p.classList.remove('open'));
  const openPop = (pop, anchor) => {
    const wasOpen = pop.classList.contains('open');
    closePops();
    if (wasOpen) return;
    pop.classList.add('open');
    const r = anchor.getBoundingClientRect();
    pop.style.left = Math.max(4, Math.min(r.left, win.innerWidth - pop.offsetWidth - 4)) + 'px';
    if (popAbove) { pop.style.bottom = (win.innerHeight - r.top + 4) + 'px'; pop.style.top = 'auto'; }
    else { pop.style.top = (r.bottom + 4) + 'px'; pop.style.bottom = 'auto'; }
  };

  [0.5, 0.75, 1, 1.2, 1.5, 2].forEach(r => ratePop.append(mk('button', { 'data-r': String(r), text: r + 'x' })));
  ratePop.onclick = e => { const r = e.target.dataset?.r; if (r) { player.setRate(+r); closePops(); } };
  rate.onclick = () => openPop(ratePop, rate);
  doc.addEventListener('pointerdown', e => {
    if (!e.target.closest('.pop') && !extra.includes(e.target) && e.target !== rate) closePops();
  });

  const syncLabels = () => { back.textContent = `‹ ${seekSeconds()}`; fwd.textContent = `${seekSeconds()} ›`; };
  syncLabels();
  back.onclick = () => player.seekBy(-seekSeconds());
  fwd.onclick = () => player.seekBy(seekSeconds());
  play.onclick = () => player.toggle();
  mute.onclick = () => player.toggleMute();
  vol.oninput = () => player.setVolume(+vol.value);
  orig.onclick = () => onOrig();
  let dragging = false;
  seek.addEventListener('pointerdown', () => { dragging = true; });
  win.addEventListener('pointerup', () => { dragging = false; });
  seek.oninput = () => player.seekTo(seek.value / 1000);

  const refresh = () => {
    const s = player.state();
    play.textContent = s.paused ? '▶' : '⏸';
    time.textContent = `${fmt(s.currentTime)} / ${fmt(s.duration)}`;
    if (!dragging && isFinite(s.duration) && s.duration) seek.value = s.currentTime / s.duration * 1000;
    seek.disabled = !isFinite(s.duration); // 라이브는 탐색 불가
    mute.textContent = s.muted || !s.volume ? '🔇' : '🔊';
    vol.value = s.volume;
    const r = +(+s.rate).toFixed(2);
    rate.textContent = r + 'x';
    [...ratePop.children].forEach(b => b.classList.toggle('cur', +b.dataset.r === r));
  };
  const off = player.onChange(refresh);
  refresh();

  // 재생 키(창에 포커스가 있을 때). 한글 입력 상태에서도 되도록 물리 키(code)로 받는다.
  doc.addEventListener('keydown', e => {
    const t = e.target.tagName;
    if (t === 'INPUT' || t === 'SELECT') return;
    const c = e.code;
    if (c === 'Space') { e.preventDefault(); play.click(); }
    else if (c === 'ArrowLeft') back.click();
    else if (c === 'ArrowRight') fwd.click();
    else if (c === 'KeyM') mute.click();
    else if (c === 'BracketLeft') player.setRate(Math.max(0.25, player.state().rate - 0.25));
    else if (c === 'BracketRight') player.setRate(Math.min(4, player.state().rate + 0.25));
  });

  return { bar, ratePop, openPop, closePops, refresh, syncLabels, dispose: off };
}

if (typeof module !== 'undefined') module.exports = { buildBar, el, fmt, BAR_CSS };
