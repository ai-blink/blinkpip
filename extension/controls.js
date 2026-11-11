// 무테 PiP 옆에 따로 여는 컨트롤 창. 영상은 원래 탭에 있고, 여기서는 메시지로 조작하고 상태를 받는다.
// 열고 닫는 것은 background.js 가 맡는다(무테가 닫히면 이 창도 닫힌다).
'use strict';

const q = new URLSearchParams(location.search);
const tabId = +q.get('tab'), frameId = +q.get('frame');

document.getElementById('css').textContent = `
  html,body{margin:0;background:#101014;color:#eee;font:13px system-ui,sans-serif}
  #root{display:flex;flex-direction:column}
  #title{padding:8px 10px;font-size:12px;opacity:.75;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
  #formWrap{padding:10px 12px}
  #subs{box-sizing:border-box;padding:6px 10px;display:flex;align-items:flex-end;justify-content:center;background:#000;overflow:hidden}
  #subText{width:100%;text-align:center;white-space:pre-wrap;line-height:1.35;color:#fff}
  #bar{position:relative}
  ${BAR_CSS}
  ${HPP_FORM_CSS}`;

// ----- 원래 탭의 영상과 통신 -----
let st = { paused: true, currentTime: 0, duration: null, muted: false, volume: 1, rate: 1 };
const listeners = new Set();
const titleEl = document.getElementById('title');
let gone = false;
const markGone = () => {
  if (gone) return;
  gone = true;
  titleEl.textContent = '영상과 연결이 끊겼습니다. 곧 닫힙니다.';
  setTimeout(() => window.close(), 2000);
};
const apply = s => {
  if (!s) return;
  st = s;
  if (s.title) titleEl.textContent = s.title;
  listeners.forEach(f => f());
};
const send = (action, value) => chrome.tabs.sendMessage(tabId, { type: 'cmd', action, value }, { frameId })
  .then(apply).catch(markGone);

const player = {
  state: () => ({ ...st, duration: st.duration ?? Infinity }),
  toggle: () => send('toggle'),
  seekBy: d => send('seekBy', d),
  seekTo: f => send('seekTo', f),
  setRate: r => send('rate', r),
  setVolume: v => send('volume', v),
  toggleMute: () => send('mute'),
  onChange: cb => { listeners.add(cb); return () => listeners.delete(cb); },
};

// 영상 쪽이 상태를 밀어 준다(재생 위치는 약 4번/초).
chrome.runtime.onMessage.addListener((msg, sender) => {
  if (msg?.type === 'state' && sender.tab?.id === tabId && sender.frameId === frameId) apply(msg.state);
});
setInterval(() => { if (!gone) send('state'); }, 1500); // 안전망: 끊김 감지와 누락 보정

// ----- 화면 구성 -----
chrome.storage.local.get(HPP_DEFAULTS, v => {
  const settings = { ...HPP_DEFAULTS, ...v };
  const saver = makeSettingsSaver(settings);
  const ui = buildBar(document, player, {
    seekSeconds: () => settings.seekSeconds,
    popAbove: false, // 줄이 위에 있으니 속도 목록은 아래로 연다
    origTitle: 'PiP를 닫고 원래 탭으로',
    onOrig: () => {
      chrome.runtime.sendMessage({ type: 'focusTab', tabId });
      send('exit'); // 무테 PiP를 닫는다 → 이 창도 background가 닫는다
    },
  });
  // 자막 칸: 설정이 '영상 아래'일 때 여기에 보인다('영상 위'면 영상에 그려지므로 숨김). 높이는 줄 수 × 글자 크기.
  const subs = document.createElement('div');
  subs.id = 'subs';
  const subText = document.createElement('div');
  subText.id = 'subText';
  subs.append(subText);
  const root = document.getElementById('root');
  let fitTimer = 0;
  const fit = () => { // 내용 높이에 맞춰 창 높이를 조절한다(타이틀바 높이 포함)
    clearTimeout(fitTimer);
    fitTimer = setTimeout(async () => {
      try {
        const w = await chrome.windows.getCurrent();
        await chrome.windows.update(w.id, { height: Math.ceil(root.getBoundingClientRect().height + (window.outerHeight - window.innerHeight)) });
      } catch { /* 창 API 없음 */ }
    }, 150);
  };
  const applySubs = () => {
    const overlay = settings.subPos === 'overlay' && !st.native; // 원본 PiP(native)는 영상에 못 그리니 칸을 항상 보여 준다
    subs.style.display = overlay ? 'none' : 'flex';
    subs.style.height = Math.ceil(settings.lines * settings.fontSize * 1.35 + 12) + 'px';
    subText.style.fontFamily = settings.fontFamily;
    subText.style.fontSize = settings.fontSize + 'px';
    subText.style.textShadow = settings.outline ? '-1px -1px 0 #000,1px -1px 0 #000,-1px 1px 0 #000,1px 1px 0 #000' : 'none';
    const t = (st.subs || []).slice(-settings.lines).join('\n');
    if (subText.textContent !== t) subText.textContent = t;
  };
  listeners.add(applySubs); // 영상 쪽 상태가 올 때마다 자막도 갱신
  const form = buildSettingsForm(document, settings, (k, val) => { saver.change(k, val); ui.syncLabels(); applySubs(); fit(); }, saver.flush);
  const wrap = document.createElement('div');
  wrap.id = 'formWrap';
  wrap.append(form.el);
  root.append(ui.bar, subs, ui.ratePop, wrap);
  applySubs();
  fit();
  chrome.storage.onChanged.addListener((ch, area) => { // 다른 창에서 바꾼 설정 반영
    if (area !== 'local') return;
    for (const [k, c] of Object.entries(ch)) settings[k] = c.newValue;
    form.refresh(); ui.syncLabels(); applySubs(); fit();
  });
  window.addEventListener('pagehide', saver.flush);
  send('state');
});
