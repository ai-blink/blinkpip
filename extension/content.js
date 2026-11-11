// Hybrid PiP Player 확장 — 시험판.
// PiP+ : Document PiP 창. 위 = 사이트의 <video> 요소를 옮김 / 가운데 = 재생 줄 / 아래(또는 영상 위) = 자막.
// 무테 : 기본 영상 PiP(타이틀바 없음). HTML을 못 넣으므로 자막을 캔버스에 그려 영상에 합성한다. DRM 영상은 안 된다.
// 설정 항목은 settings-fields.js, 재생 줄 UI는 bar-ui.js, 롤업 자막 병합은 subfeed.js 에 있다.
// 무테는 영상 창만 만들고, 재생 줄·설정은 background가 따로 여는 컨트롤 창(controls.html)이 맡는다.

'use strict';

let settings = { ...HPP_DEFAULTS };
const settingsListeners = new Set(); // 설정이 바뀔 때 열려 있는 창을 다시 맞추는 콜백
const fireSettings = () => settingsListeners.forEach(f => { try { f(); } catch (e) { console.warn('[Hybrid PiP]', e); } });
try {
  chrome.storage.local.get(HPP_DEFAULTS, v => { settings = { ...HPP_DEFAULTS, ...v }; fireSettings(); });
  chrome.storage.onChanged.addListener((ch, area) => {
    if (area !== 'local') return;
    let changed = false;
    for (const [k, c] of Object.entries(ch)) if (settings[k] !== c.newValue) { settings[k] = c.newValue; changed = true; }
    if (changed) fireSettings(); // 내가 저장한 값의 에코는 다시 적용하지 않는다(창 크기 떨림 방지)
  });
} catch { /* 저장소 없음 */ }
let saveTimer = 0;
const dirtyKeys = new Set(); // 이 창에서 바꾼 키만 저장한다(다른 창의 최신 값을 옛 값으로 덮어쓰지 않도록)
const saveSettings = () => {
  clearTimeout(saveTimer);
  saveTimer = setTimeout(() => {
    const o = {};
    dirtyKeys.forEach(k => { o[k] = settings[k]; });
    dirtyKeys.clear();
    try { chrome.storage.local.set(o); } catch { /* 무시 */ }
  }, 150);
};

// ---------- 자막 읽기 ----------
const norm = s => s.replace(/\s+/g, ' ').trim();
const decodeEnt = s => s.replace(/<[^>]+>/g, '').replace(/&nbsp;/g, ' ').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
const debugOn = () => { try { return localStorage.getItem('hppDebug') === '1'; } catch { return false; } };

// 표준 TextTrack. active = 켜진(disabled가 아닌) 자막 트랙이 있는지.
function readTrackLines(video) {
  let active = false;
  const lines = [];
  for (const tr of video.textTracks) {
    if ((tr.kind !== 'subtitles' && tr.kind !== 'captions') || tr.mode === 'disabled') continue;
    active = true;
    for (const c of tr.activeCues || []) {
      decodeEnt(c.text || '').split('\n').forEach(t => { t = norm(t); if (t) lines.push(t); });
    }
  }
  return { active, lines };
}

// 사이트가 직접 그리는 자막(DOM). 줄 단위 배열로 돌려준다.
let lastDbg = '';
function readDomLines() {
  const lines = [];
  const wins = document.querySelectorAll('.ytp-caption-window-container .caption-window'); // YouTube
  if (wins.length) {
    wins.forEach(w => {
      const vl = [...w.querySelectorAll('.caption-visual-line')].map(e => norm(e.textContent)).filter(Boolean);
      (vl.length ? vl : [norm(w.textContent)]).forEach(t => { if (t) lines.push(t); });
    });
    if (debugOn()) {
      const snap = [...wins].map(w => `${w.id || w.className}[${[...w.querySelectorAll('.caption-visual-line')].map(e => norm(e.textContent)).join(' | ')}]`).join(' ## ');
      if (snap !== lastDbg) { lastDbg = snap; console.log('[Hybrid PiP][자막 DOM]', snap); }
    }
    return lines;
  }
  // Netflix, video.js, JW Player, ArtPlayer(.art-subtitle: nooo38 등 한국 스트리밍 사이트 다수가 사용)
  document.querySelectorAll('.player-timedtext-text-container, .vjs-text-track-display div, .jw-text-track-cue, .art-subtitle').forEach(e => {
    (e.innerText || '').split('\n').forEach(t => { t = norm(t); if (t && !lines.includes(t)) lines.push(t); });
  });
  if (!lines.length) { // Disney+
    document.querySelector('timed-text-override-region')?.shadowRoot?.querySelectorAll('.hive-subtitle-renderer-cue-window')
      .forEach(e => { const t = norm(e.textContent); if (t) lines.push(t); });
  }
  return lines;
}

// makeSubFeed(롤업 자막 병합기)는 subfeed.js 에 있다.

function currentLines(video, feed) {
  const t = readTrackLines(video);
  if (t.lines.length) feed.update(t.lines, true);
  else {
    const dom = readDomLines(); // 빈 hidden 트랙만 있는 플레이어(hls.js 등)에서도 DOM 자막을 읽는다
    if (dom.length) feed.update(dom, false); else feed.update([], t.active);
  }
  return feed.get(settings.lines);
}

// 확장 background에 메시지를 보낸다. 확장이 다시 로드되어 이 스크립트가 고아가 된 경우의 예외는 무시한다.
const bg = msg => { try { chrome.runtime.sendMessage(msg).catch(() => {}); } catch { /* 확장 컨텍스트 없음 */ } };

// ---------- 영상 조작(PiP+ 재생 줄과 컨트롤 창 명령이 같이 쓴다) ----------
const STATE_EVENTS = ['timeupdate', 'play', 'pause', 'volumechange', 'durationchange', 'ratechange'];
function makeLocalPlayer(video) {
  return {
    state: () => ({
      paused: video.paused, currentTime: video.currentTime, duration: video.duration,
      muted: video.muted, volume: video.volume, rate: video.playbackRate,
    }),
    toggle: () => (video.paused ? video.play() : video.pause()),
    seekBy: d => { video.currentTime = Math.min(isFinite(video.duration) ? video.duration : 1e9, Math.max(0, video.currentTime + d)); },
    seekTo: f => { if (isFinite(video.duration) && video.duration) video.currentTime = video.duration * f; },
    setRate: r => { video.playbackRate = r; },
    setVolume: v => { video.volume = v; video.muted = false; },
    toggleMute: () => { video.muted = !video.muted; },
    onChange: cb => {
      STATE_EVENTS.forEach(e => video.addEventListener(e, cb));
      return () => STATE_EVENTS.forEach(e => video.removeEventListener(e, cb));
    },
  };
}

// ---------- PiP+ (Document PiP) ----------
// Document PiP는 최상위 페이지에서만 열 수 있다. 영상이 iframe(내장 플레이어) 안에 있으면 PiP+는 불가하고 무테만 된다.
const IN_IFRAME = window !== window.top;
let pipWin = null;
let opening = false; // 열기 중 중복 호출을 막는다(더블클릭)

function toast(msg) {
  const t = document.createElement('div');
  t.textContent = msg;
  t.style.cssText = 'position:fixed;top:16px;left:50%;transform:translateX(-50%);z-index:2147483647;padding:8px 14px;' +
    'background:rgba(20,20,24,.92);color:#fff;border:1px solid #666;border-radius:6px;font:13px sans-serif;max-width:80vw';
  document.documentElement.append(t);
  setTimeout(() => t.remove(), 5000);
}

async function openPip(video) {
  if (!('documentPictureInPicture' in window)) { alert('이 브라우저는 Document PiP를 지원하지 않습니다(Chrome/Edge 116+).'); return; }
  if (IN_IFRAME) { toast('이 플레이어는 iframe 안에 있어 PiP+를 쓸 수 없습니다(크롬 제한). 무테를 사용하세요.'); return; }
  if (pipWin) { pipWin.close(); return; }
  if (opening) return;
  if (!video?.isConnected || !video.parentNode) return;
  if (frameless) { toast('무테 창을 먼저 닫아 주세요.'); return; } // 크롬은 PiP 창을 종류와 상관없이 하나만 허용한다
  opening = true;
  let win;
  try {
    const vw0 = video.videoWidth || video.clientWidth || 640, vh0 = video.videoHeight || video.clientHeight || 360;
    win = await documentPictureInPicture.requestWindow({ width: 480, height: Math.round(480 * vh0 / vw0) + 190 });
  } catch (err) {
    opening = false;
    console.warn('[Hybrid PiP] PiP 창을 열지 못했다:', err);
    alert('PiP 창을 열지 못했습니다: ' + (err?.message || err));
    return;
  }
  opening = false;
  pipWin = win;

  // 닫힘 처리를 가장 먼저 등록하고, 이후 단계가 던져도 같은 경로로 정리한다.
  const cleanups = [];
  win.addEventListener('pagehide', () => {
    cleanups.reverse().forEach(f => { try { f(); } catch (e) { console.warn('[Hybrid PiP] 정리 실패', e); } });
    if (pipWin === win) pipWin = null;
  });
  try {
    buildPip(win, video, cleanups);
  } catch (err) {
    console.warn('[Hybrid PiP] PiP 구성 실패:', err);
    win.close();
  }
}

const PIP_CSS = `
  html,body{margin:0;height:100%;background:#000;color:#eee;font:13px system-ui,sans-serif;overflow:hidden}
  #root{position:relative;display:flex;flex-direction:column;height:100%}
  #stage{position:relative;flex:0 1 auto;min-height:0;width:100%;background:#000;overflow:hidden}
  #stage video{display:block;width:100%!important;height:100%!important;object-fit:cover!important;position:static!important;transform:none!important}
  #subs{flex:none;overflow:hidden;box-sizing:border-box;padding:6px 10px;display:flex;align-items:flex-end;justify-content:center;background:#101014}
  #subs.ov{position:absolute;left:0;right:0;bottom:0;background:transparent;pointer-events:none}
  #subText{width:100%;text-align:center;white-space:pre-wrap;line-height:1.35}
  ${BAR_CSS}
  ${HPP_FORM_CSS}`;

function buildPip(win, video, cleanups) {
  const doc = win.document;

  // 스타일: 생성자 스타일시트(CSP·Trusted Types 영향 없음), 안 되면 <style>.
  try { const sh = new win.CSSStyleSheet(); sh.replaceSync(PIP_CSS); doc.adoptedStyleSheets = [sh]; }
  catch { doc.head.append(el(doc, 'style', { text: PIP_CSS })); }

  const cc = el(doc, 'button', { id: 'cc', text: '자막', title: '자막 설정' });
  const setPop = el(doc, 'div', { id: 'setPop', class: 'pop' });
  const sendApp = el(doc, 'button', { text: '앱으로', title: '현재 영상을 앱으로 보내기' });
  sendApp.addEventListener('click', e => {
    if (!e.isTrusted) return;
    e.stopPropagation(); e.preventDefault();
    HPP_HANDOFF_UI.open(video, () => location.href, doc);
  });
  const ui = buildBar(doc, makeLocalPlayer(video), {
    seekSeconds: () => settings.seekSeconds,
    extra: [cc, sendApp], pops: [setPop], popAbove: true,
    onOrig: () => { bg({ type: 'focusTab' }); win.close(); }, // 닫히면 pagehide 정리가 영상을 원래 자리로 되돌린다
  });
  cc.onclick = () => ui.openPop(setPop, cc);
  cleanups.push(ui.dispose);

  const stage = el(doc, 'div', { id: 'stage' });
  const subText = el(doc, 'div', { id: 'subText' });
  const subs = el(doc, 'div', { id: 'subs' }, subText);
  const form = buildSettingsForm(doc, settings, (k, v) => { settings[k] = v; dirtyKeys.add(k); fireSettings(); }, saveSettings);
  setPop.append(form.el);
  const root = el(doc, 'div', { id: 'root' }, stage, ui.bar, ui.ratePop, setPop);
  doc.body.append(root);

  // 원래 자리에 자리표시자를 두고 video를 옮긴다. 창이 닫히면 되돌린다.
  const holder = document.createComment('hybrid-pip-holder');
  video.parentNode.insertBefore(holder, video);
  const prev = { style: video.getAttribute('style'), controls: video.hasAttribute('controls') };
  const hiddenTracks = new Map(); // 우리가 숨긴 트랙 → 원래 mode
  const isCaption = tr => tr.kind === 'subtitles' || tr.kind === 'captions';
  const hideNativeTracks = () => {
    for (const tr of video.textTracks) if (isCaption(tr) && tr.mode === 'showing') { hiddenTracks.set(tr, tr.mode); tr.mode = 'hidden'; }
  };
  hideNativeTracks();
  cleanups.push(() => {
    if (prev.style === null) video.removeAttribute('style'); else video.setAttribute('style', prev.style);
    if (prev.controls) video.setAttribute('controls', '');
    // 우리가 숨긴 상태 그대로인 트랙만 되돌린다(그 사이 사이트가 바꾼 트랙은 건드리지 않음).
    hiddenTracks.forEach((mode, tr) => { if (tr.mode === 'hidden') tr.mode = mode; });
    if (holder.isConnected) holder.replaceWith(video); // 원래 자리로 복귀
    else video.remove();                              // 컨테이너가 사라졌으면 분리 트리에 두지 않는다
  });

  stage.append(video);
  video.removeAttribute('controls');
  video.style.cssText = 'display:block;width:100%;height:100%;object-fit:cover';

  // ----- 크기·배치: 영상 비율, 자막 칸 높이(줄 수 × 글자 크기), 자막 위치 -----
  const subsH = () => Math.ceil(settings.lines * settings.fontSize * 1.35 + 12);
  const placeSubs = () => {
    const overlay = settings.subPos === 'overlay';
    subs.classList.toggle('ov', overlay);
    if (overlay) { if (subs.parentNode !== stage) stage.append(subs); }
    else if (subs.parentNode !== root) root.insertBefore(subs, ui.ratePop);
    subs.style.height = subsH() + 'px';
    subs.style.background = overlay ? `rgba(0,0,0,${settings.bgAlpha})` : '#101014';
  };
  const fitStage = () => {
    const vw = video.videoWidth || video.clientWidth || 640, vh = video.videoHeight || video.clientHeight || 360;
    stage.style.aspectRatio = `${vw} / ${vh}`;
    return Math.round(win.innerWidth * vh / vw);
  };
  // 창 바깥 크기와 안쪽 크기의 차이(타이틀바)를 더해 안쪽이 딱 맞게 조절한다.
  const resizeToFit = () => {
    const stageH = fitStage();
    const inner = stageH + ui.bar.offsetHeight + (settings.subPos === 'overlay' ? 0 : subsH());
    try { win.resizeTo(win.outerWidth, inner + (win.outerHeight - win.innerHeight)); } catch { /* 크기 조절 거부 시 그대로 */ }
  };
  const onMeta = () => resizeToFit();
  video.addEventListener('loadedmetadata', onMeta);
  video.addEventListener('resize', onMeta);
  cleanups.push(() => { video.removeEventListener('loadedmetadata', onMeta); video.removeEventListener('resize', onMeta); });

  // ----- 자막 표시(PiP+ 창. 무테는 캔버스에 직접 그린다) -----
  const feed = makeSubFeed();
  const onSeeking = () => feed.reset();
  video.addEventListener('seeking', onSeeking);
  cleanups.push(() => video.removeEventListener('seeking', onSeeking));
  const applySub = () => {
    subText.style.fontFamily = settings.fontFamily;
    subText.style.fontSize = settings.fontSize + 'px';
    subText.style.textShadow = settings.outline ? '-1px -1px 0 #000,1px -1px 0 #000,-1px 1px 0 #000,1px 1px 0 #000' : 'none';
    subText.style.color = '#fff';
  };
  const onSet = () => { applySub(); ui.syncLabels(); placeSubs(); resizeToFit(); form.refresh(); };
  applySub(); placeSubs(); resizeToFit(); // 라벨을 채운 뒤에 재야 bar 줄바꿈 높이가 맞다
  settingsListeners.add(onSet);
  cleanups.push(() => settingsListeners.delete(onSet));

  const tick = () => {
    hideNativeTracks(); // 창이 열린 뒤 켜진 자막도 영상 위에 겹쳐 그려지지 않게 한다
    const t = currentLines(video, feed).join('\n');
    if (subText.textContent !== t) subText.textContent = t;
  };
  // 원래 탭이 가려져도 쓰로틀되지 않도록 PiP 창의 타이머를 쓴다.
  const subTimer = win.setInterval(tick, 100);
  cleanups.push(() => win.clearInterval(subTimer));
}

// ---------- 무테 모드(기본 영상 PiP + 캔버스 합성) ----------
// 한계: DRM 영상은 캔버스에 검게 나온다. 소리는 원본 video가 계속 낸다. 창 안에는 UI를 못 넣는다.
// 재생 줄·설정은 background가 따로 여는 컨트롤 창(controls.html)이 맡는다. 크롬은 PiP 창을 하나만 허용해서
// Document PiP로는 컨트롤 창을 못 연다(열면 무테가 닫힌다).
let frameless = null;
const nativeOnly = new WeakSet(); // 합성(캔버스)이 막힌 영상: 다음부터 바로 원본 PiP로 연다

function wrapText(ctx, text, maxW) {
  const out = [];
  for (const para of text.split('\n')) {
    let line = '';
    for (const word of para.split(' ')) {
      const t = line ? line + ' ' + word : word;
      if (!line || ctx.measureText(t).width <= maxW) line = t; else { out.push(line); line = word; }
    }
    if (line) out.push(line);
  }
  return out;
}

// 컨트롤 창의 명령을 받아 영상을 조작하고, 영상 상태를 컨트롤 창에 밀어 준다. 돌려주는 함수가 연결을 끊는다.
function startControlBridge(video, getSubs, native = false) {
  const lp = makeLocalPlayer(video);
  const snap = () => {
    const s = lp.state();
    // Infinity는 JSON으로 못 보낸다. 자막 줄은 '영상 아래' 설정일 때 컨트롤 창이 그린다.
    return { ...s, duration: isFinite(s.duration) ? s.duration : null, title: document.title, subs: getSubs(), native };
  };
  let timer = 0;
  const send = () => bg({ type: 'state', state: snap() });
  const push = now => { // timeupdate가 잦아서 초당 4번으로 모아 보낸다. 자막이 바뀔 때(now=true)는 바로 보낸다
    if (now === true) { clearTimeout(timer); timer = 0; send(); return; }
    if (timer) return;
    timer = setTimeout(() => { timer = 0; send(); }, 250);
  };
  const off = lp.onChange(push);
  const onMsg = (msg, sender, respond) => {
    if (msg?.type !== 'cmd') return;
    const n = Number(msg.value);
    switch (msg.action) {
      case 'toggle': lp.toggle(); break;
      case 'seekBy': if (isFinite(n)) lp.seekBy(n); break;
      case 'seekTo': if (isFinite(n)) lp.seekTo(n); break;
      case 'rate': if (isFinite(n) && n > 0) lp.setRate(n); break;
      case 'volume': if (isFinite(n)) lp.setVolume(Math.min(1, Math.max(0, n))); break;
      case 'mute': lp.toggleMute(); break;
      case 'exit': document.exitPictureInPicture().catch(() => {}); break;
    }
    respond(snap());
  };
  chrome.runtime.onMessage.addListener(onMsg);
  return { push, stop: () => { off(); clearTimeout(timer); chrome.runtime.onMessage.removeListener(onMsg); } };
}

async function openFrameless(video) {
  if (frameless) { await document.exitPictureInPicture().catch(() => {}); return; }
  if (!video?.isConnected) return;
  if (pipWin) { toast('PiP+ 창을 먼저 닫아 주세요.'); return; }
  if (nativeOnly.has(video)) return openNative(video);

  let W = 1280, H = 720;
  const canvas = document.createElement('canvas');
  const ctx = canvas.getContext('2d');
  // 영상 크기를 모르던 채로 열었거나 해상도가 바뀌면 캔버스도 따라간다(비율이 일그러지지 않게).
  const sizeCanvas = () => {
    const vw = video.videoWidth || 1280, vh = video.videoHeight || 720;
    const w = Math.min(1280, vw), h = Math.round(w * vh / vw);
    if (w !== W || h !== H || canvas.width !== w) { W = w; H = h; canvas.width = w; canvas.height = h; }
  };
  sizeCanvas();
  video.addEventListener('loadedmetadata', sizeCanvas);
  video.addEventListener('resize', sizeCanvas);

  const out = document.createElement('video');
  out.muted = true;
  out.srcObject = canvas.captureStream(30);
  out.style.cssText = 'position:fixed;left:-9999px;top:0;width:2px;height:2px;opacity:0;pointer-events:none';
  document.documentElement.append(out);

  const feed = makeSubFeed();
  const onSeeking = () => feed.reset();
  video.addEventListener('seeking', onSeeking);
  let subLines = [];
  let subKey = '';
  const readSub = () => {
    subLines = currentLines(video, feed);
    const k = subLines.join('\n');
    if (k !== subKey) { subKey = k; bridge?.push(true); } // 컨트롤 창의 자막 칸을 갱신
  };

  let frames = 0, tainted = false;
  const draw = () => {
    // 바탕을 매번 칠한다. 영상이 아직 로드 전이거나 멈춰 있어도 캔버스가 갱신돼야 captureStream이 프레임을
    // 내보내고, 그래야 out.play()가 끝난다(안 그러면 무테가 조용히 먹통이 된다).
    ctx.fillStyle = '#000';
    ctx.fillRect(0, 0, W, H);
    try { ctx.drawImage(video, 0, 0, W, H); } catch { /* 아직 프레임 없음 */ }
    // 다른 출처 영상은 캔버스가 오염되어 스트림이 검게 나온다. 약 1초마다 검사해 알린다.
    if (!tainted && frames++ % 30 === 0) {
      try { ctx.getImageData(0, 0, 1, 1); }
      catch (e) { if (e.name === 'SecurityError') tainted = true; }
      if (tainted) {
        nativeOnly.add(video);
        if (drawTimer) { // PiP가 이미 열린 뒤에 알게 된 경우(영상이 늦게 로드됨): 닫고 다시 누르게 한다
          toast('이 영상은 다른 출처라 합성할 수 없습니다. 무테를 한 번 더 누르면 원본 PiP로 엽니다.');
          document.exitPictureInPicture().catch(() => {});
        }
        return;
      }
    }
    // 자막 위치가 '영상 위'일 때만 영상에 그린다. '영상 아래'는 컨트롤 창의 자막 칸이 보여 준다.
    if (!subLines.length || settings.subPos !== 'overlay') return;
    const fs = settings.fontSize * W / 480; // 창이 약 480px 너비일 때 설정값(px)과 같게 보이도록 환산
    ctx.font = `${fs}px ${settings.fontFamily}`;
    ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    const lines = wrapText(ctx, subLines.join('\n'), W * 0.92).slice(-settings.lines);
    const lh = fs * 1.3, pad = fs * 0.35, boxH = lines.length * lh + pad * 2;
    const boxW = Math.min(W, Math.max(...lines.map(l => ctx.measureText(l).width)) + pad * 4);
    const y0 = H - boxH - H * 0.04;
    if (settings.bgAlpha > 0) {
      ctx.fillStyle = `rgba(0,0,0,${settings.bgAlpha})`;
      ctx.fillRect((W - boxW) / 2, y0, boxW, boxH);
    }
    ctx.fillStyle = '#fff'; ctx.lineWidth = fs * 0.12; ctx.strokeStyle = '#000'; ctx.lineJoin = 'round';
    lines.forEach((l, i) => {
      const y = y0 + pad + lh * (i + 0.5);
      if (settings.outline) ctx.strokeText(l, W / 2, y);
      ctx.fillText(l, W / 2, y);
    });
  };

  // 재생/일시정지 양방향 동기화. 내가 일으킨 변화로 서로 다시 부르지 않도록 잠깐 무시 구간을 둔다.
  let quietUntil = 0;
  const quiet = () => { quietUntil = Date.now() + 400; };
  const onOutPlay = () => { if (Date.now() >= quietUntil && video.paused) video.play().catch(() => {}); };
  const onOutPause = () => { if (Date.now() >= quietUntil && !video.paused) video.pause(); };
  const syncOut = () => { // 원본 → 합성 영상
    if (video.paused === out.paused) return;
    quiet();
    if (video.paused) out.pause(); else out.play().catch(() => {});
  };
  const ms = navigator.mediaSession;
  let drawTimer = 0, subTimer = 0, bridge = null;
  frameless = { out };
  const leave = () => {
    clearInterval(drawTimer); clearInterval(subTimer);
    video.removeEventListener('seeking', onSeeking);
    video.removeEventListener('loadedmetadata', sizeCanvas); video.removeEventListener('resize', sizeCanvas);
    video.removeEventListener('play', syncOut); video.removeEventListener('pause', syncOut);
    out.removeEventListener('play', onOutPlay); out.removeEventListener('pause', onOutPause);
    out.srcObject?.getTracks().forEach(t => t.stop());
    out.remove();
    try { ms?.setActionHandler('seekbackward', null); ms?.setActionHandler('seekforward', null); } catch { /* 무시 */ }
    bridge?.stop(); bridge = null;
    frameless = null;
    bg({ type: 'controlsBye' }); // 컨트롤 창도 닫는다
  };
  out.addEventListener('leavepictureinpicture', leave, { once: true });

  draw();
  if (tainted) { leave(); return openNative(video); } // 열기 전에 이미 오염이면 원본 PiP로(같은 클릭 안이라 허가가 남아 있다)
  drawTimer = setInterval(draw, 33);
  subTimer = setInterval(readSub, 100);
  try {
    await out.play();
    await out.requestPictureInPicture();
    if (!frameless) return; // 여는 동안 이미 닫혔다
    // 리스너는 PiP가 열린 뒤에 건다. 먼저 걸면 out.play()의 play 이벤트가 일시정지 중인 원본을 재생시킨다.
    out.addEventListener('play', onOutPlay); out.addEventListener('pause', onOutPause);
    video.addEventListener('play', syncOut); video.addEventListener('pause', syncOut);
    syncOut(); // 일시정지 상태로 열었으면 합성 영상도 멈춘다
    try {
      ms?.setActionHandler('seekbackward', () => { video.currentTime = Math.max(0, video.currentTime - settings.seekSeconds); });
      ms?.setActionHandler('seekforward', () => {
        video.currentTime = Math.min(isFinite(video.duration) ? video.duration : 1e9, video.currentTime + settings.seekSeconds);
      });
    } catch { /* 일부 브라우저는 미지원 */ }
    bridge = startControlBridge(video, () => subLines);
    bg({ type: 'openControls' }); // 컨트롤 창은 background가 연다(클릭 허가가 필요 없다)
  } catch (err) {
    console.warn('[Hybrid PiP] 무테 PiP 실패:', err);
    leave();
    alert('무테 PiP를 열지 못했습니다: ' + (err?.message || err));
  }
}

// ---------- 원본 PiP 모드(합성 없이 실제 영상 요소를 기본 PiP로) ----------
// 다른 출처 영상은 캔버스가 오염되어 합성(무테)을 못 쓴다. 이 모드는 video.requestPictureInPicture()를 그대로 부르므로
// 출처·DRM과 상관없이 된다(사이트 내장 PIP 버튼과 같은 방식). 영상 위에 자막을 그릴 수는 없어서
// 자막은 컨트롤 창의 자막 칸이 항상 보여 준다. 합성이 막히면 무테가 자동으로 이 모드로 넘어온다.
async function openNative(video) {
  if (frameless) { await document.exitPictureInPicture().catch(() => {}); return; }
  if (!video?.isConnected) return;
  if (pipWin) { toast('PiP+ 창을 먼저 닫아 주세요.'); return; }

  const feed = makeSubFeed();
  const onSeeking = () => feed.reset();
  video.addEventListener('seeking', onSeeking);
  let subLines = [], subKey = '', bridge = null, subTimer = 0;
  const readSub = () => {
    subLines = currentLines(video, feed);
    const k = subLines.join('\n');
    if (k !== subKey) { subKey = k; bridge?.push(true); }
  };
  frameless = { native: true };
  // 크롬은 켜진 <track> 자막을 PiP 영상에 직접 그린다. 컨트롤 창 자막 칸과 겹치지 않게 숨긴다(hidden이어도 activeCues는 읽힌다).
  const hiddenTracks = new Map();
  const hideTracks = () => {
    for (const tr of video.textTracks) {
      if ((tr.kind === 'subtitles' || tr.kind === 'captions') && tr.mode === 'showing') { hiddenTracks.set(tr, tr.mode); tr.mode = 'hidden'; }
    }
  };
  hideTracks();
  const leave = () => {
    clearInterval(subTimer);
    hiddenTracks.forEach((mode, tr) => { if (tr.mode === 'hidden') tr.mode = mode; });
    video.removeEventListener('seeking', onSeeking);
    bridge?.stop(); bridge = null;
    frameless = null;
    bg({ type: 'controlsBye' }); // 컨트롤 창도 닫는다
  };
  video.addEventListener('leavepictureinpicture', leave, { once: true });
  try {
    await video.requestPictureInPicture();
    if (!frameless) return; // 여는 동안 이미 닫혔다
    subTimer = setInterval(() => { hideTracks(); readSub(); }, 100); // 열린 뒤 켜진 자막도 숨긴다
    bridge = startControlBridge(video, () => subLines, true);
    bg({ type: 'openControls' });
  } catch (err) {
    console.warn('[Hybrid PiP] 원본 PiP 실패:', err);
    video.removeEventListener('leavepictureinpicture', leave);
    leave();
    alert('PiP를 열지 못했습니다: ' + (err?.message || err));
  }
}

// ---------- 영상 위 "PiP+" / "무테" 버튼 ----------
const btnCss = 'position:fixed;z-index:2147483647;display:none;padding:4px 8px;font:12px sans-serif;' +
  'background:rgba(20,20,24,.85);color:#fff;border:1px solid #666;border-radius:4px;cursor:pointer';
const btn = document.createElement('button');
btn.textContent = 'PiP+';
btn.style.cssText = btnCss;
const btn2 = document.createElement('button');
btn2.textContent = '무테';
btn2.style.cssText = btnCss;
const appBtn = document.createElement('button');
appBtn.textContent = '앱으로';
appBtn.title = '이 영상을 Hybrid PIP Player 앱으로 보내기';
appBtn.style.cssText = btnCss;
document.documentElement.append(btn, btn2, appBtn);
let target = null, hideTimer = 0;
const hideBtns = () => { btn.style.display = 'none'; btn2.style.display = 'none'; appBtn.style.display = 'none'; };

const bigVideoAt = (x, y) => [...document.querySelectorAll('video')].find(v => {
  const r = v.getBoundingClientRect();
  return r.width >= 160 && r.height >= 90 && x >= r.left && x <= r.right && y >= r.top && y <= r.bottom;
});

let lastMove = 0;
document.addEventListener('mousemove', e => {
  if (pipWin || frameless || HPP_HANDOFF_UI.isOpen) return;
  if (e.timeStamp - lastMove < 100) return; // 전수 검사 비용을 줄인다
  lastMove = e.timeStamp;
  const onBtn = e.target === btn || e.target === btn2 || e.target === appBtn;
  const v = onBtn ? target : bigVideoAt(e.clientX, e.clientY);
  if (v) {
    target = v;
    const r = v.getBoundingClientRect();
    btn2.style.left = Math.max(0, r.right - (IN_IFRAME ? 62 : 62 + 52)) + 'px'; // iframe에서는 PiP+ 자리까지 무테가 쓴다
    btn.style.left = Math.max(0, r.right - 62) + 'px';
    appBtn.style.left = Math.max(0, r.right - (IN_IFRAME ? 136 : 188)) + 'px';
    btn.style.top = btn2.style.top = appBtn.style.top = Math.max(0, r.top + 8) + 'px';
    appBtn.style.display = 'block';
    btn2.style.display = 'block';
    btn.style.display = IN_IFRAME ? 'none' : 'block'; // iframe 안에서는 PiP+ 버튼을 숨긴다
    clearTimeout(hideTimer);
    hideTimer = setTimeout(hideBtns, 2500);
  } else if (!onBtn) hideBtns();
}, true);

btn.addEventListener('click', e => { if (!e.isTrusted) return; e.stopPropagation(); e.preventDefault(); if (target) openPip(target); hideBtns(); });
btn2.addEventListener('click', e => { if (!e.isTrusted) return; e.stopPropagation(); e.preventDefault(); if (target) openFrameless(target); hideBtns(); });

appBtn.addEventListener('click', e => {
  if (!e.isTrusted) return;
  e.stopPropagation(); e.preventDefault();
  if (target) HPP_HANDOFF_UI.open(target, () => location.href);
  hideBtns();
});

const pickVideo = () => (target?.isConnected && target) || [...document.querySelectorAll('video')].find(x => !x.paused) || document.querySelector('video');
document.addEventListener('keydown', e => {
  if (HPP_HANDOFF_UI.isOpen || !e.altKey || !e.shiftKey) return;
  if (e.code === 'KeyP') { const v = pickVideo(); if (v) openPip(v); }
  else if (e.code === 'KeyO') { const v = pickVideo(); if (v) openFrameless(v); } // 무테(합성 PiP)
  else if (e.code === 'KeyN') { const v = pickVideo(); if (v) openNative(v); } // 원본 PiP(합성 없음, DRM·다른 출처 영상용)
}, true);
