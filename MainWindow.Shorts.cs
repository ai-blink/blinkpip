using System;
using System.Windows;

namespace blinkpip;

// 유튜브 쇼츠 PIP(웹뷰 그대로, 세로 창). notes/mock/shorts-pip.html 시안 A.
//  · PIP 에서는 쇼츠 플레이어(#shorts-player)만 창에 꽉 채우고, 마우스를 움직이면 ▲▼💬⏭ 단추를 페이지 안에 띄운다.
//  · 💬 = 유튜브 자체 댓글 창을 창 오른쪽 칸(CommentsDip)에 붙이고, 호스트는 창을 그만큼 넓힌다("hppshorts:cm:1/0").
//  · ⏭ = 자동 넘기기. 쇼츠 영상은 loop 라서 끝 → 처음으로 되감기는 순간을 잡아 "다음 동영상" 단추를 누른다. 켜짐 여부는 페이지 localStorage.
// 유튜브 구조(2026-11-18 실측, 웹뷰): #shorts-player 조상 div 에 contain:layout 이 걸려 position:fixed 가 창 기준이 안 돼서 PIP 동안 contain 등을 푼다
// (유튜브가 칸의 style 을 다시 써서 매 틱 다시 푼다).
// 다음/이전 = #navigation-button-down|up button, 댓글 단추 = reel-action-bar-view-model 안 aria-label 에 "댓글"/"comment",
// 댓글 창 = ytd-engagement-panel-section-list-renderer[target-id=engagement-panel-comments-section] (visibility 속성으로 열림 판정, 닫기 = #visibility-button button).
// 구조가 바뀌면 넘기기·댓글만 안 되고 재생은 그대로다.
public partial class MainWindow
{
    private const int ShortsCommentsDip = 340;
    private bool _shortsComments; // 쇼츠 PIP 에서 댓글 칸이 열려 창이 넓어진 상태

    private const string ShortsScript = @"
        (() => {
            // 맨 바깥 문서에서만(하위 프레임에는 __hppPip 가 없어 엉뚱한 ""cm:0"" 을 보낸다)
            if (window.top !== window || window.__hppShortsInit || !/(^|\.)youtube\.com$/.test(location.hostname)) return;
            window.__hppShortsInit = true;
            const CM = 340; // = ShortsCommentsDip
            const PANEL = 'ytd-engagement-panel-section-list-renderer[target-id=""engagement-panel-comments-section""]';
            const shorts = () => location.pathname.startsWith('/shorts/');
            const pip = () => !!window.__hppPip && shorts();
            const panel = () => document.querySelector(PANEL);
            const cmOpen = () => panel()?.getAttribute('visibility') === 'ENGAGEMENT_PANEL_VISIBILITY_EXPANDED';
            const post = m => window.chrome?.webview?.postMessage('hppshorts:' + m);
            const auto = () => { try { return localStorage.getItem('__hppShortsAuto') === '1'; } catch (e) { return false; } };
            const go = d => document.querySelector('#navigation-button-' + (d > 0 ? 'down' : 'up') + ' button')?.click();
            const cmButton = () => [...document.querySelectorAll('reel-action-bar-view-model button')]
                .filter(b => /댓글|comment/i.test(b.getAttribute('aria-label') || '') && b.offsetParent)
                .sort((a, b) => Math.abs(a.getBoundingClientRect().top) - Math.abs(b.getBoundingClientRect().top))[0];
            // 댓글을 열어 둔 상태(cmWanted). 다음 쇼츠로 넘어가면 유튜브가 댓글 창을 닫아서, 열어 둔 상태면 새 쇼츠에서 다시 연다.
            // 같은 쇼츠에서 유튜브 닫기(✕)를 누르면 닫은 것으로 본다. 칸 너비·창 넓히기는 cmWanted 를 따른다(다시 여는 사이 창이 줄었다 늘지 않게)
            // 댓글이 꺼진 쇼츠처럼 4초 안에 안 열리면 칸은 접는다(cmWanted 는 남겨 다음 쇼츠에서 다시 연다) — cmShown
            let cmWanted = false, cmSeen = false, cmUrl = location.href, urlAt = 0, reopenAt = 0, toggleAt = 0;
            const cmShown = () => cmWanted && (cmOpen() || Date.now() - Math.max(urlAt, toggleAt) < 4000);
            const toggleCm = () => {
                cmWanted = !cmWanted; toggleAt = Date.now();
                if (cmWanted && !cmOpen()) cmButton()?.click();
                if (!cmWanted && cmOpen()) panel()?.querySelector('#visibility-button button')?.click();
            };
            const syncCm = () => {
                const now = Date.now(), open = cmOpen();
                if (location.href !== cmUrl) { cmUrl = location.href; urlAt = now; }
                if (open !== cmSeen) { cmSeen = open; if (open || now - urlAt > 3000) cmWanted = open; }
                if (pip() && cmWanted && !open && now > reopenAt) { reopenAt = now + 1500; cmButton()?.click(); }
            };

            // PIP 단추(페이지 안). 마우스를 움직이면 2.5초 보인다
            let ui = null, hideAt = 0;
            const makeUi = () => {
                // 유튜브는 Trusted Types 를 강제해 innerHTML 대입이 예외를 던진다 → 요소를 하나씩 만든다
                const el = (tag, cls) => { const e = document.createElement(tag); if (cls) e.className = cls; return e; };
                ui = el('div'); ui.id = '__hpp_shorts_ui';
                const col = el('div', 'col');
                for (const [a, label, tip] of [['up', '▲', '이전 쇼츠'], ['down', '▼', '다음 쇼츠'], ['cm', '💬', '댓글 열기·닫기'], ['scroll', '⇣', '댓글 자동 스크롤: 누를 때마다 느림 → 보통 → 빠름 → 끔'], ['auto', '⏭', '자동 넘기기 켜기·끄기']]) {
                    const b = el('button'); b.dataset.a = a; b.textContent = label; b.title = tip; col.appendChild(b);
                }
                // 재생바: ⏯ + 막대 + 시간. 막대는 누른 자리로 옮긴다(끌기는 PIP 창 이동 훅과 겹쳐서 누르기만)
                const seek = el('div', 'seek'), play = el('button', 'play'), track = el('div', 'track'), fill = el('div', 'fill'), time = el('span', 'time');
                play.dataset.a = 'play'; play.title = '재생·일시정지';
                track.appendChild(fill); seek.append(play, track, time);
                track.addEventListener('click', e => {
                    const v = document.querySelector('#shorts-player video'), r = track.getBoundingClientRect();
                    if (v && v.duration > 0) v.currentTime = Math.min(1, Math.max(0, (e.clientX - r.left) / r.width)) * v.duration;
                    hideAt = Date.now() + 2500; e.stopPropagation();
                });
                ui.append(el('div', 'pill'), col, seek);
                ui.addEventListener('click', e => {
                    const a = e.target.closest('button')?.dataset.a;
                    if (a === 'play') { const v = document.querySelector('#shorts-player video'); if (v) v.paused ? v.play() : v.pause(); }
                    else if (a === 'up') go(-1); else if (a === 'down') go(1); else if (a === 'cm') toggleCm();
                    else if (a === 'auto') { try { localStorage.setItem('__hppShortsAuto', auto() ? '0' : '1'); } catch (x) { } }
                    else if (a === 'scroll') { try { localStorage.setItem('__hppShortsCmScroll', String((cmScroll() + 1) % SPEEDS.length)); } catch (x) { } }
                    hideAt = Date.now() + 2500; e.stopPropagation();
                });
                document.body.appendChild(ui);
            };
            let mouseX = -1;
            addEventListener('mousemove', e => { hideAt = Date.now() + 2500; mouseX = e.clientX; }, true);
            addEventListener('mouseout', e => { if (!e.relatedTarget) mouseX = -1; }, true); // 창 밖으로 나가면 댓글 스크롤 멈춤을 푼다

            // 댓글 자동 스크롤: 댓글 칸이 열려 있으면 초당 SPEEDS[단계] px 로 내린다. ⇣ 단추로 끔 → 느림 → 보통 → 빠름 → 끔.
            // 커서가 댓글 칸 위에 있으면 멈춘다(읽는 중). 끝까지 가면 유튜브가 다음 댓글을 이어 붙이고, 더 없으면 그 자리에 선다. 쇼츠가 바뀌면 맨 위부터.
            const SPEEDS = [0, 15, 30, 60], SPEED_NAMES = ['⇣', '느림', '보통', '빠름'];
            const cmScroll = () => { try { const n = parseInt(localStorage.getItem('__hppShortsCmScroll'), 10); return n > 0 && n < SPEEDS.length ? n : 0; } catch (e) { return 0; } };
            const scroller = () => [...(panel()?.querySelectorAll('*') || [])].find(e => e.scrollHeight > e.clientHeight + 20 && /auto|scroll/.test(getComputedStyle(e).overflowY));
            let scrollEl = null, scrollAcc = 0, scrollLast = 0, scrollUrl = '', scrollFindAt = 0;
            const scrollStep = now => {
                requestAnimationFrame(scrollStep);
                const dt = scrollLast ? Math.min(0.1, (now - scrollLast) / 1000) : 0; scrollLast = now;
                if (!(pip() && cmOpen() && cmScroll()) || (mouseX >= 0 && mouseX >= innerWidth - CM)) { scrollEl = null; return; }
                if (location.href !== scrollUrl) { scrollUrl = location.href; scrollEl = null; }
                if (!scrollEl || !scrollEl.isConnected) {
                    if (now < scrollFindAt) return; // 댓글을 아직 불러오는 중이면 0.5초마다만 찾는다
                    scrollFindAt = now + 500;
                    scrollEl = scroller(); scrollAcc = scrollEl ? scrollEl.scrollTop : 0;
                    if (!scrollEl) return;
                }
                scrollAcc = Math.min(scrollAcc + SPEEDS[cmScroll()] * dt, scrollEl.scrollHeight - scrollEl.clientHeight);
                scrollEl.scrollTop = scrollAcc;
            };
            requestAnimationFrame(scrollStep);

            const CSS_ID = '__hpp_shorts_css';
            const uncontained = new Set();
            // position:fixed 의 기준을 창이 아닌 조상으로 바꿔 버리는 속성들과 그 '없음' 값
            const FREE = [['contain', 'none'], ['transform', 'none'], ['will-change', 'auto'], ['filter', 'none'], ['perspective', 'none'], ['container-type', 'normal']];
            const layout = (on, cm) => {
                let s = document.getElementById(CSS_ID);
                if (!on) {
                    s?.remove(); ui?.remove(); ui = null;
                    uncontained.forEach(x => FREE.forEach(([k]) => x.style.removeProperty(k))); uncontained.clear();
                    return;
                }
                // 유튜브는 쇼츠 칸(div#0, #1 …)을 돌려쓰며 style 을 다시 써서 넣어 둔 값이 지워진다(2026-11-18 G21dv3Yp8c8 다음 쇼츠에서 재현:
                // div#7 contain:size layout 이 되살아나 플레이어가 그 칸에 갇히고 폭 0). 그래서 '이미 푼 칸'으로 건너뛰지 않고 매 틱 다시 확인한다.
                const p = document.querySelector('#shorts-player');
                for (let x = p?.parentElement; x; x = x.parentElement) {
                    const c = getComputedStyle(x);
                    for (const [k, off] of FREE)
                        if (c.getPropertyValue(k) !== off) { x.style.setProperty(k, off, 'important'); uncontained.add(x); }
                }
                const right = cm ? CM : 0;
                const css = '#shorts-player{position:fixed!important;top:0!important;left:0!important;bottom:0!important;right:' + right + 'px!important;width:auto!important;height:auto!important;z-index:2147483640!important;background:#000!important;border-radius:0!important}' +
                    '#shorts-player .html5-video-container{position:absolute!important;inset:0!important;width:100%!important;height:100%!important}' +
                    '#shorts-player video{position:absolute!important;left:0!important;top:0!important;width:100%!important;height:100%!important;object-fit:contain!important}' +
                    PANEL + '{position:fixed!important;top:0!important;right:0!important;left:auto!important;width:' + CM + 'px!important;height:100vh!important;max-height:none!important;z-index:2147483641!important;margin:0!important;border-radius:0!important}' +
                    'html,body{overflow:hidden!important}' +
                    '#__hpp_shorts_ui{position:fixed;top:0;left:0;bottom:0;right:' + right + 'px;z-index:2147483642;pointer-events:none;transition:opacity .15s;font:13px sans-serif}' +
                    '#__hpp_shorts_ui .pill{position:absolute;top:10px;left:10px;padding:3px 10px;border-radius:12px;background:rgba(20,20,24,.72);color:#fff}' +
                    '#__hpp_shorts_ui .pill.on{background:rgba(61,220,151,.25);color:#3ddc97}' +
                    '#__hpp_shorts_ui .col{position:absolute;right:10px;bottom:60px;display:flex;flex-direction:column;gap:10px}' +
                    '#__hpp_shorts_ui button{pointer-events:auto;width:38px;height:38px;border-radius:50%;border:none;background:rgba(20,20,24,.72);color:#fff;font-size:16px;cursor:pointer}' +
                    '#__hpp_shorts_ui.hidden *{pointer-events:none!important}' +
                    '#__hpp_shorts_ui button:hover{background:rgba(60,60,70,.9)}#__hpp_shorts_ui button.on{background:#ff3d5a}' +
                    // 재생바는 아래 가장자리 8px(창 크기 조절 자리) 위에 둔다
                    '#__hpp_shorts_ui .seek{position:absolute;left:10px;right:10px;bottom:14px;display:flex;align-items:center;gap:8px;padding:4px 10px 4px 4px;border-radius:20px;background:rgba(20,20,24,.72);color:#fff;pointer-events:auto}' +
                    '#__hpp_shorts_ui .seek .play{width:30px;height:30px;font-size:13px;flex:none;background:transparent}' +
                    '#__hpp_shorts_ui .track{flex:1;height:16px;display:flex;align-items:center;cursor:pointer}' +
                    '#__hpp_shorts_ui .track::before{content:"""";position:absolute;left:0;right:0;height:4px;border-radius:2px;background:rgba(255,255,255,.3)}' +
                    '#__hpp_shorts_ui .track{position:relative}#__hpp_shorts_ui .fill{position:relative;height:4px;border-radius:2px;background:#ff3d5a;width:0}' +
                    '#__hpp_shorts_ui .time{font-size:12px;flex:none;font-variant-numeric:tabular-nums}';
                if (!s) { s = document.createElement('style'); s.id = CSS_ID; document.head.appendChild(s); }
                if (s.textContent !== css) s.textContent = css;
                if (!ui && document.body) makeUi();
                if (ui) {
                    const shown = Date.now() < hideAt;
                    ui.style.opacity = shown ? '1' : '0';
                    ui.classList.toggle('hidden', !shown); // 숨긴 단추는 클릭도 안 받는다(창을 끌다 놓은 자리의 단추가 눌리지 않게)
                    ui.querySelector('[data-a=cm]').classList.toggle('on', cm);
                    ui.querySelector('[data-a=auto]').classList.toggle('on', auto());
                    const sb = ui.querySelector('[data-a=scroll]');
                    sb.style.display = cm ? '' : 'none'; // 댓글 칸이 열렸을 때만
                    sb.classList.toggle('on', cmScroll() > 0);
                    sb.textContent = SPEED_NAMES[cmScroll()];
                    sb.style.fontSize = cmScroll() ? '11px' : '';
                    const pill = ui.querySelector('.pill');
                    pill.textContent = auto() ? '자동 넘기기 켜짐' : '자동 넘기기 꺼짐';
                    pill.classList.toggle('on', auto());
                    const v = document.querySelector('#shorts-player video'), d = v?.duration > 0 ? v.duration : 0;
                    const mmss = s => Math.floor(s / 60) + ':' + String(Math.floor(s % 60)).padStart(2, '0');
                    ui.querySelector('.fill').style.width = d ? (v.currentTime / d * 100) + '%' : '0';
                    ui.querySelector('.time').textContent = d ? mmss(v.currentTime) + ' / ' + mmss(d) : '';
                    ui.querySelector('.play').textContent = v && !v.paused ? '❚❚' : '▶';
                }
            };

            // 자동 넘기기: 같은 영상에서 끝 근처 → 처음으로 되감기면 한 번 다 본 것
            let lastV = null, lastSrc = '', lastT = 0, lastCm = null;
            setInterval(() => {
                syncCm();
                const on = pip(), cm = on && cmShown();
                if (cm !== lastCm) { lastCm = cm; post('cm:' + (cm ? 1 : 0)); }
                layout(on, cm);
                const v = shorts() ? document.querySelector('#shorts-player video') : null;
                if (!v || v !== lastV || v.src !== lastSrc) { lastV = v; lastSrc = v?.src || ''; lastT = 0; return; }
                const t = v.currentTime, d = v.duration;
                if (auto() && d > 0 && lastT > d - 1.2 && t < 1) { lastT = 0; go(1); return; }
                lastT = t;
            }, 250);
        })();
    ";

    // 쇼츠 PIP 인가: 웹뷰가 유튜브 쇼츠 페이지를 띄운 채 PIP
    private bool ShortsPip => _isPipMode && !_engineA && DrmWebView.CoreWebView2 is { } core &&
        Uri.TryCreate(core.Source, UriKind.Absolute, out var u) && YouTubeRoute.IsShorts(u);

    // 댓글 칸이 열려 있으면 그 너비(px)도 영상 밖 여백으로 친다(Chrome() — 비율 맞춤·크기 조절이 영상 부분만 비율을 지킨다)
    private double ShortsCommentsPx => _shortsComments ? ShortsCommentsDip * Scale : 0;

    // 페이지가 알린 댓글 칸 열림/닫힘에 맞춰 창 너비를 늘리고 줄인다. 화면 오른쪽 절반에 있으면 오른쪽 변을 고정한다
    private void OnShortsComments(bool open)
    {
        if (!_isPipMode || open == _shortsComments || !NativeMethods.GetWindowRect(_hwnd, out var r)) return;
        _shortsComments = open;
        var wa = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        int dw = (int)(ShortsCommentsDip * Scale) * (open ? 1 : -1);
        int w = r.Right - r.Left + dw, h = r.Bottom - r.Top;
        bool keepRight = (r.Left + r.Right) / 2 > (wa.Left + wa.Right) / 2;
        int x = keepRight ? r.Right - w : r.Left;
        x = Math.Max(wa.Left, Math.Min(x, wa.Right - w));
        SetRect(x, r.Top, w, h);
        PositionPipExtras();
    }
}
