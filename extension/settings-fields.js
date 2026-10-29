// 설정 항목 정의와 폼 생성기. content script(PiP 창 안 패널)와 툴바 팝업이 같이 쓴다(항목이 갈라지지 않게 한 곳에 둔다).
'use strict';

const HPP_DEFAULTS = {
  fontFamily: 'sans-serif', fontSize: 28, outline: true, seekSeconds: 5,
  lines: 2,        // 표시할 자막 줄 수(1~5)
  bgAlpha: 0.5,    // 자막 배경 불투명도(0 = 완전 투명)
  subPos: 'below', // 'below' = 영상 아래 칸, 'overlay' = 영상 위
};

const HPP_FIELDS = [
  { key: 'fontFamily', label: '글꼴', type: 'font' },
  { key: 'fontSize', label: '글자 크기', type: 'range', min: 12, max: 72, step: 1, unit: 'px' },
  { key: 'lines', label: '자막 줄 수', type: 'range', min: 1, max: 5, step: 1, unit: '줄' },
  { key: 'bgAlpha', label: '배경 불투명도', type: 'range', min: 0, max: 1, step: 0.05, pct: true },
  { key: 'outline', label: '외곽선', type: 'check' },
  { key: 'subPos', label: '자막 위치', type: 'select', options: [['below', '영상 아래'], ['overlay', '영상 위']] },
  { key: 'seekSeconds', label: '건너뛰기(초)', type: 'range', min: 1, max: 60, step: 1, unit: '초' },
];

const HPP_FORM_CSS = `
  .hpp-form{display:grid;grid-template-columns:auto 1fr auto;gap:8px 10px;align-items:center;font-size:12px}
  .hpp-form .hpp-name{white-space:nowrap}
  .hpp-form input[type=range]{width:130px;margin:0}
  .hpp-form input[type=text]{width:130px;background:#2c2c33;color:#eee;border:1px solid #444;border-radius:4px;padding:2px 4px}
  .hpp-form select,.hpp-form button{background:#2c2c33;color:#eee;border:1px solid #444;border-radius:4px;padding:2px 6px;cursor:pointer}
  .hpp-form .hpp-val{min-width:38px;text-align:right;opacity:.8}`;

// 바뀐 키만 모아서 저장한다(다른 창의 최신 값을 옛 전체 값으로 덮어쓰지 않도록). 팝업·컨트롤 창 같은 확장 페이지용.
// 슬라이더를 끄는 동안은 짧게 모아 쓰고, flush()로 즉시 비울 수 있다(창이 닫히기 전에 호출).
function makeSettingsSaver(settings, delay = 60) {
  const pending = {};
  let timer = 0;
  const flush = () => {
    clearTimeout(timer);
    if (Object.keys(pending).length) chrome.storage.local.set({ ...pending });
    for (const k of Object.keys(pending)) delete pending[k];
  };
  return {
    change(k, v) { settings[k] = v; pending[k] = v; clearTimeout(timer); timer = setTimeout(flush, delay); },
    flush,
  };
}

// onChange(key, value): 값이 바뀌는 즉시. onCommit(): 조작이 끝났을 때(저장 시점).
function buildSettingsForm(doc, settings, onChange, onCommit) {
  const root = doc.createElement('div');
  root.className = 'hpp-form';
  const refreshers = [];
  const mk = (tag, props = {}) => Object.assign(doc.createElement(tag), props);

  for (const f of HPP_FIELDS) {
    const name = mk('span', { className: 'hpp-name', textContent: f.label });
    const val = mk('span', { className: 'hpp-val' });
    let ctl;
    if (f.type === 'range') {
      ctl = mk('input', { type: 'range', min: f.min, max: f.max, step: f.step });
      const fmtv = v => (f.pct ? Math.round(v * 100) + '%' : v + (f.unit || ''));
      ctl.oninput = () => { val.textContent = fmtv(+ctl.value); onChange(f.key, +ctl.value); };
      ctl.onchange = () => onCommit();
      refreshers.push(() => { ctl.value = settings[f.key]; val.textContent = fmtv(+settings[f.key]); });
    } else if (f.type === 'check') {
      ctl = mk('input', { type: 'checkbox' });
      ctl.onchange = () => { onChange(f.key, ctl.checked); onCommit(); };
      refreshers.push(() => { ctl.checked = !!settings[f.key]; });
    } else if (f.type === 'select') {
      ctl = mk('select');
      f.options.forEach(([v, t]) => ctl.append(mk('option', { value: v, textContent: t })));
      ctl.onchange = () => { onChange(f.key, ctl.value); onCommit(); };
      refreshers.push(() => { ctl.value = settings[f.key]; });
    } else { // font: 직접 입력 + 시스템 글꼴 목록 불러오기(사용자 클릭이 있어야 권한 요청이 된다)
      ctl = mk('input', { type: 'text' });
      ctl.setAttribute('list', 'hpp-fonts'); // list는 읽기 전용 속성이라 대입하면 예외가 난다
      const list = mk('datalist', { id: 'hpp-fonts' });
      const load = mk('button', { type: 'button', textContent: '목록', title: '설치된 글꼴 목록 불러오기' });
      load.onclick = () => {
        const w = doc.defaultView;
        if (!w.queryLocalFonts) return;
        w.queryLocalFonts().then(fs => {
          list.replaceChildren(...[...new Set(fs.map(x => x.family))].sort().map(n => mk('option', { value: n })));
        }).catch(() => { /* 권한 거부 시 직접 입력만 */ });
      };
      ctl.onchange = () => { onChange(f.key, ctl.value || 'sans-serif'); onCommit(); };
      refreshers.push(() => { ctl.value = settings[f.key]; });
      root.append(name, ctl, load, list);
      continue;
    }
    root.append(name, ctl, val);
  }
  const refresh = () => refreshers.forEach(r => r());
  refresh();
  return { el: root, refresh };
}
