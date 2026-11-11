// Shared by content scripts and Node checks. No network or browser state reads.
'use strict';
const HPP_HANDOFF = (() => {
  const MAX_URL_BYTES = 4096, MAX_ENVELOPE_BYTES = 16384, MAX_TIME = 31536000;
  const bytes = text => new TextEncoder().encode(text).length;
  const own = (o, k) => Object.prototype.hasOwnProperty.call(o, k);
  const fail = message => ({ ok: false, error: message });
  const secretNames = new Set([
    'auth', 'authorization', 'authtoken', 'token', 'accesstoken', 'idtoken', 'refreshtoken', 'csrftoken',
    'password', 'passwd', 'pwd', 'cookie', 'session', 'sessionid', 'jwt', 'bearer', 'secret', 'clientsecret',
    'apikey', 'accesskey', 'credential', 'credentials', 'key', 'code', 'state', 'signature', 'sig', 'signed',
    'policy', 'keypairid', 'ossaccesskeyid', 'hdnts', 'hdnea',
  ]);
  function secretKey(key) {
    const k = key.toLowerCase().replace(/[^a-z0-9]/g, '');
    return secretNames.has(k) || /^(?:xamz|xgoog|xoss|auth|cookie|session)/.test(k) || k.endsWith('token');
  }
  function publicUrl(raw) {
    if (typeof raw !== 'string' || !raw || raw !== raw.trim() || bytes(raw) > MAX_URL_BYTES)
      return fail('주소가 비어 있거나 너무 깁니다.');
    // URL/TextEncoder replace lone UTF-16 surrogates; reject them instead of silently
    // changing the request (the C# UTF-8/parser boundary also rejects these strings).
    try { encodeURIComponent(raw); } catch { return fail('주소의 문자 인코딩이 올바르지 않습니다.'); }
    if (!/^https?:\/\/[^/?#]/i.test(raw) || /[\u0000-\u001f\u007f-\u009f\\]/.test(raw) || /%(?![0-9a-f]{2})/i.test(raw)) return fail('주소 형식이 올바르지 않습니다.');
    let decoded, url;
    try { decoded = decodeURIComponent(raw); url = new URL(raw); }
    catch { return fail('절대 HTTP(S) 주소만 보낼 수 있습니다.'); }
    if (/[\u0000-\u001f\u007f-\u009f\\]/.test(decoded)) return fail('주소에 허용하지 않는 문자가 있습니다.');
    if (!['http:', 'https:'].includes(url.protocol) || !url.hostname) return fail('HTTP(S) 주소만 보낼 수 있습니다.');
    if (url.username || url.password || /^https?:\/\/[^/?#]*@/i.test(raw) || raw.includes('#'))
      return fail('로그인 정보나 URL 조각이 있는 주소는 보내지 않습니다.');
    for (const [key, value] of url.searchParams) {
      const credentialValue = value.trimStart();
      if (key.includes('%') || secretKey(key) || /^Bearer /i.test(credentialValue) || /^eyJ[A-Za-z0-9_-]*\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/.test(credentialValue))
        return fail('인증·서명 값이 있는 주소는 보내지 않습니다.');
    }
    if (bytes(url.href) > MAX_URL_BYTES) return fail('주소가 너무 깁니다.');
    return { ok: true, value: url.href };
  }
  function validate(input) {
    if (!input || typeof input !== 'object' || Array.isArray(input)) return fail('요청 형식이 올바르지 않습니다.');
    const allowed = new Set(['v', 'url', 't', 'sub', 'speed', 'pip']);
    if (Reflect.ownKeys(input).some(key => typeof key !== 'string' || !allowed.has(key))) return fail('허용하지 않는 요청 항목입니다.');
    if (!own(input, 'v') || input.v !== 1 || !own(input, 'url')) return fail('지원하지 않는 요청 버전입니다.');
    const target = publicUrl(input.url);
    if (!target.ok) return target;
    const value = { v: 1, url: target.value };
    if (own(input, 't')) {
      if (typeof input.t !== 'number' || !Number.isFinite(input.t) || input.t < 0 || input.t > MAX_TIME)
        return fail('재생 위치가 범위를 벗어났습니다.');
      value.t = input.t;
    }
    if (own(input, 'sub')) {
      const subtitle = publicUrl(input.sub);
      if (!subtitle.ok) return fail('보낼 수 있는 외부 자막 주소가 없습니다.');
      value.sub = subtitle.value;
    }
    if (own(input, 'speed')) {
      if (typeof input.speed !== 'number' || !Number.isFinite(input.speed) || input.speed < .25 || input.speed > 4)
        return fail('속도는 0.25~4배 범위여야 합니다.');
      value.speed = input.speed;
    }
    if (own(input, 'pip')) {
      if (typeof input.pip !== 'boolean') return fail('PIP 값은 참/거짓이어야 합니다.');
      value.pip = input.pip;
    }
    if (bytes(JSON.stringify(value)) > MAX_ENVELOPE_BYTES) return fail('요청이 너무 깁니다.');
    return { ok: true, value };
  }
  function protocol(input) {
    const checked = validate(input);
    if (!checked.ok) return checked;
    const dto = checked.value;
    const params = new URLSearchParams({ v: '1', url: dto.url });
    if (own(dto, 't')) params.set('t', String(dto.t));
    if (own(dto, 'sub')) params.set('sub', dto.sub);
    if (own(dto, 'speed')) params.set('speed', String(dto.speed));
    if (own(dto, 'pip')) params.set('pip', dto.pip ? '1' : '0');
    const href = 'hybridpip://play/?' + params.toString();
    if (bytes(href) > MAX_ENVELOPE_BYTES) return fail('앱으로 보낼 주소가 너무 깁니다.');
    return { ok: true, value: dto, href };
  }
  function youtubePage(raw) {
    const checked = publicUrl(raw);
    if (!checked.ok) return null;
    const url = new URL(checked.value);
    let id = null;
    if (['youtube.com', 'www.youtube.com', 'm.youtube.com'].includes(url.hostname)) {
      if (url.pathname === '/watch') id = url.searchParams.get('v');
      else id = /^\/(?:embed|shorts)\/([A-Za-z0-9_-]{11})(?:\/|$)/.exec(url.pathname)?.[1];
    } else if (url.hostname === 'youtu.be') id = /^\/([A-Za-z0-9_-]{11})(?:\/|$)/.exec(url.pathname)?.[1];
    return id && /^[A-Za-z0-9_-]{11}$/.test(id) ? 'https://www.youtube.com/watch?v=' + id : null;
  }
  function fromSnapshot(snapshot, include = {}) {
    if (!snapshot?.connected) return fail('선택한 영상이 페이지에서 사라졌습니다.');
    const page = publicUrl(snapshot.pageUrl);
    const direct = !snapshot.protected && !snapshot.hasSourceObject ? publicUrl(snapshot.currentSrc) : fail('전용 영상 소스');
    const target = youtubePage(snapshot.pageUrl) || (direct.ok ? direct.value : page.ok ? page.value : null);
    if (!target) return fail('앱으로 보낼 안전한 영상·페이지 주소를 찾지 못했습니다.');
    const dto = { v: 1, url: target };
    if (include.time) dto.t = snapshot.currentTime;
    if (include.speed) dto.speed = snapshot.playbackRate;
    if (include.subtitle) dto.sub = snapshot.subtitleSrc;
    if (include.pip) dto.pip = true;
    const result = protocol(dto);
    if (result.ok) result.sourceKind = target === direct.value ? 'video' : 'page';
    return result;
  }
  function videoSnapshot(video, pageUrl) {
    const tracks = [...(video?.textTracks || [])].filter(t => ['subtitles', 'captions'].includes(t.kind) && t.mode !== 'disabled');
    const showing = tracks.filter(t => t.mode === 'showing');
    const selected = showing.length === 1 ? showing[0] : tracks.length === 1 ? tracks[0] : null;
    const external = selected && [...video.querySelectorAll('track')].find(element => element.track === selected);
    return {
      connected: !!video?.isConnected, pageUrl,
      currentSrc: video?.currentSrc || video?.src || '',
      currentTime: video?.currentTime, playbackRate: video?.playbackRate,
      subtitleSrc: external?.src || undefined,
      protected: !!video?.mediaKeys, hasSourceObject: !!video?.srcObject,
    };
  }
  return { MAX_URL_BYTES, MAX_ENVELOPE_BYTES, MAX_TIME, publicUrl, validate, protocol, fromSnapshot, videoSnapshot };
})();
if (typeof module !== 'undefined') module.exports = HPP_HANDOFF;
