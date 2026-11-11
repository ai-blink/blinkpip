// Extension-owned handoff dialog. The final real link click preserves browser app-launch confirmation.
'use strict';
const HPP_HANDOFF_UI = (() => {
  let active = null;
  let dismissActive = null;
  function open(video, pageUrl, doc = document) {
    dismissActive?.();
    const host = doc.createElement('div');
    host.style.cssText = 'position:fixed;inset:0;z-index:2147483647;display:grid;place-items:center;background:#0009';
    const root = host.attachShadow({ mode: 'closed' });
    const style = doc.createElement('style');
    style.textContent = ':host{font:14px system-ui,sans-serif;color:#eee}*{box-sizing:border-box}section{background:#1b1b1f;border:1px solid #777;border-radius:12px;padding:20px;width:min(480px,92vw);max-height:88vh;overflow:auto}h2{font-size:18px;margin:0 0 12px}p{font-size:12px;line-height:1.5;color:#ccc}label{display:flex;align-items:center;gap:8px;margin:12px 0}input{width:18px;height:18px}pre{white-space:pre-wrap;overflow-wrap:anywhere;max-height:130px;overflow:auto;background:#111;padding:10px;font:12px system-ui}footer{display:flex;gap:10px;align-items:center;margin-top:14px}button,a{display:inline-block;background:#34343d;color:white;border:1px solid #777;border-radius:6px;padding:9px 12px;font:inherit;text-decoration:none;cursor:pointer}a[aria-disabled=true]{opacity:.5;cursor:not-allowed}#status{min-height:36px}';
    root.append(style);
    const mk = (tag, text) => { const e = doc.createElement(tag); if (text !== undefined) e.textContent = text; return e; };
    const panel = mk('section'); panel.setAttribute('role', 'dialog'); panel.setAttribute('aria-modal', 'true'); panel.setAttribute('aria-label', '앱으로 보내기');
    panel.append(mk('h2', '앱으로 보내기'), mk('p', '선택한 영상의 주소와 아래에서 고른 정보만 보냅니다. 브라우저 로그인 정보는 전달하지 않습니다.'));
    const options = {};
    for (const [key, label, checked] of [['time', '보던 위치', true], ['subtitle', '자막 파일 주소 (찾은 경우)', false], ['speed', '재생 속도', false], ['pip', 'PIP로 바로 시작', false]]) {
      const line = mk('label'); const input = mk('input'); input.type = 'checkbox'; input.checked = checked; input.dataset.option = key;
      line.append(input, doc.createTextNode(label)); panel.append(line); options[key] = input;
    }
    const preview = mk('pre'); preview.setAttribute('aria-label', '앱으로 보내는 정보'); panel.append(preview);
    const status = mk('p'); status.id = 'status'; status.setAttribute('role', 'status'); panel.append(status);
    const footer = mk('footer'); const launch = mk('a', '앱에서 열기'); launch.target = '_blank'; launch.rel = 'noopener noreferrer';
    const close = mk('button', '취소'); close.type = 'button'; footer.append(launch, close); panel.append(footer);
    panel.append(mk('p', '처음에는 앱의 설정 → 북마크·정보 → 확장에서 앱으로 열기에서 확장 연결 등록을 눌러 주세요. 브라우저의 외부 앱 확인 창에서 허용해야 할 수 있습니다. 원래 영상은 계속 재생됩니다.'));
    root.append(panel); doc.documentElement.append(host); active = host;
    panel.addEventListener('click', e => e.stopPropagation()); // closed shadow retargets inner clicks to host
    const focusBefore = doc.activeElement;
    const dismiss = (restoreFocus = true) => {
      doc.defaultView?.removeEventListener('pagehide', onPageHide);
      host.remove();
      if (active === host) { active = null; dismissActive = null; }
      if (restoreFocus && !doc.defaultView?.closed && focusBefore?.isConnected) focusBefore.focus?.();
    };
    const onPageHide = () => dismiss(false);
    dismissActive = () => dismiss();
    doc.defaultView?.addEventListener('pagehide', onPageHide, { once: true });
    close.addEventListener('click', () => dismiss());
    host.addEventListener('click', e => { if (e.target === host) dismiss(); });
    const chosen = () => Object.fromEntries(Object.entries(options).map(([key, input]) => [key, input.checked]));
    const snapshot = () => HPP_HANDOFF.videoSnapshot(video, typeof pageUrl === 'function' ? pageUrl() : pageUrl);
    let previewTarget = '';
    function refresh() {
      const snap = snapshot();
      const validTime = Number.isFinite(snap.currentTime) && snap.currentTime >= 0 && snap.currentTime <= HPP_HANDOFF.MAX_TIME;
      options.time.disabled = !validTime;
      if (!validTime) options.time.checked = false;
      options.subtitle.disabled = !HPP_HANDOFF.publicUrl(snap.subtitleSrc).ok;
      if (options.subtitle.disabled) options.subtitle.checked = false;
      const result = HPP_HANDOFF.fromSnapshot(snap, chosen());
      launch.setAttribute('aria-disabled', String(!result.ok));
      if (!result.ok) { launch.removeAttribute('href'); preview.textContent = ''; status.textContent = result.error; previewTarget = ''; return result; }
      launch.href = result.href;
      preview.textContent = [result.value.url,
        't' in result.value ? '위치: ' + result.value.t.toFixed(2) + '초' : '',
        'sub' in result.value ? '자막: ' + result.value.sub : '',
        'speed' in result.value ? '속도: ' + result.value.speed + '배' : '',
        result.value.pip ? 'PIP로 시작' : ''].filter(Boolean).join('\n');
      status.textContent = result.sourceKind === 'page' ? '페이지 주소로 보냅니다. 로그인·보호 영상은 앱에서 재생되지 않을 수 있습니다.' : '확인한 정보를 앱에서 엽니다.';
      previewTarget = JSON.stringify([result.value.url, result.value.sub || null]);
      return result;
    }
    Object.values(options).forEach(input => input.addEventListener('change', refresh));
    launch.addEventListener('click', e => {
      if (!e.isTrusted) { e.preventDefault(); return; }
      const shownTarget = previewTarget;
      const result = refresh(); // current time/rate/source are sampled again at the final trusted gesture
      if (!result.ok) { e.preventDefault(); return; }
      if (shownTarget !== previewTarget) { e.preventDefault(); status.textContent = '영상 또는 자막 주소가 바뀌었습니다. 미리보기를 확인하고 다시 눌러 주세요.'; return; }
      // Do not claim the browser confirmed the app opened; there is no protocol success ACK.
      status.textContent = '앱 열기를 요청했습니다. 응답이 없으면 앱의 확장 연결 등록을 확인해 주세요.';
    });
    // Middle-click navigation bypasses the click-time snapshot/preview check.
    launch.addEventListener('auxclick', e => { if (e.button === 1) { e.preventDefault(); e.stopPropagation(); } });
    panel.addEventListener('keydown', e => {
      e.stopPropagation(); // Keep dialog input out of the site's bubbling playback shortcuts.
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); dismiss(); }
      if (e.key === 'Tab') {
        const focusable = [...panel.querySelectorAll('input:not([disabled]),a[href],button')];
        const at = focusable.indexOf(root.activeElement);
        if (e.shiftKey && at <= 0) { e.preventDefault(); focusable.at(-1)?.focus(); }
        else if (!e.shiftKey && at === focusable.length - 1) { e.preventDefault(); focusable[0]?.focus(); }
      }
    });
    panel.addEventListener('keyup', e => e.stopPropagation());
    refresh(); options.time.disabled ? close.focus() : options.time.focus();
  }
  return { open, get isOpen() { return !!active?.isConnected; } };
})();
