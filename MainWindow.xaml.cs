using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace blinkpip;

public partial class MainWindow : Window
{
    // 재생 화면(/watch /play /video)에서 스크롤바·overflow 를 숨긴다. 페이지 이동(SPA)에도 걸리도록
    // 문서 생성 시점에 한 번 심어 두고 1초마다 현재 경로를 보고 켜고 끈다. (video 확장은 하지 않음)
    private const string OttViewportScript = @"
        (() => {
            if (window.__hppInit) return;
            window.__hppInit = true;
            // 자막 확대(Disney+ 실측 구조): 자막은 <timed-text-override-region> 의 shadow root 안 .hive-subtitle-renderer-cue-window 에
            // 그려지고 글자 크기는 줄마다 인라인 px 로 박혀 있어(플레이어 폭에 비례) 바깥 CSS 가 닿지 않는다.
            // 그래서 shadow root 안에 <style> 을 넣어 창 단위로 scale 한다(하단 중앙 기준이라 위로 자란다).
            // 쿠팡플레이(video.js, 2026-11-13 실측): 자막은 .vjs-text-track-display 안 .vjs-text-track-cue 에 줄마다 인라인 font 로
            // 그려진다(shadow root 아님) — 같은 방식으로 문서 <style> 에 넣는다. 원래 크기가 적당해서 기본 배율은 1.0 이다.
            // 배율은 사이트(구조)마다 localStorage 에 따로 저장한다. 호스트의 Ctrl+Alt+↑/↓ 는 __hppSubStep(±1) 로 한 단계씩만 바꾼다.
            // 글꼴·색(__hppSubFont/__hppSubColor)도 호스트가 localStorage 에 넣는다(호스트에서 글자·숫자·공백·#hex 만 허용).
            // __hppSubBelow 가 켜지면(PIP + '창 아래 자막') 자막을 투명하게 감추고 글자만 호스트로 보낸다.
            // visibility:hidden 은 innerText 를 비게 만들어서 opacity:0 으로 감춘다.
            const CUE = '.hive-subtitle-renderer-cue-window';
            const VJS = '.vjs-text-track-display .vjs-text-track-cue';
            const SUBS = {
                disney: { key: '__hppSub2', def: 2.0, sel: CUE, root: () => document.querySelector('timed-text-override-region')?.shadowRoot },
                vjs: { key: '__hppSubVjs', def: 1.0, sel: VJS, root: () => document.querySelector('.vjs-text-track-display') ? document : null },
                // 웨이브(2026-11-13 실측): 글자 자막이 있는 작품만 .subtitle-wrap .subtitle-box > span(2.5vw)에 그린다.
                // 영상에 박힌 자막(외국 드라마 등)은 페이지에 글자가 없어 읽을 수 없다. 흔한 이름이라 웨이브 주소에서만 쓴다
                wavve: { key: '__hppSubWavve', def: 1.0, sel: '.subtitle-wrap .subtitle-box',
                         root: () => /(^|\.)wavve\.com$/.test(location.hostname) && document.querySelector('.subtitle-wrap') ? document : null },
            };
            const subKind = () => Object.keys(SUBS).find(k => SUBS[k].root());
            const zoomOf = k => parseFloat(localStorage.getItem(SUBS[k].key)) || SUBS[k].def;
            const applySub = () => {
                const k = subKind();
                if (!k) return;
                const { sel } = SUBS[k], root = SUBS[k].root(), z = zoomOf(k);
                const color = localStorage.getItem('__hppSubColor') || '';
                const font = localStorage.getItem('__hppSubFont') || '';
                let css = sel + '{transform:scale(' + z + ')!important;transform-origin:50% 100%!important' + (window.__hppSubBelow ? ';opacity:0!important' : '') + '}';
                if (/^#[0-9a-fA-F]{6}$/.test(color)) css += sel + ',' + sel + ' *{color:' + color + '!important}';
                if (/^[\p{L}\p{N} -]{1,60}$/u.test(font)) css += sel + ',' + sel + ' *{font-family:""' + font + '"",sans-serif!important}';
                const host = root === document ? document.head : root;
                if (!host) return;
                let t = host.querySelector('#__hpp_sub_shadow');
                if (!t) { t = document.createElement('style'); t.id = '__hpp_sub_shadow'; host.appendChild(t); }
                if (t.textContent !== css) t.textContent = css;
            };
            window.__hppApplySub = applySub;
            // 호스트가 부른다: 지금 사이트의 배율을 한 단계(0.2) 바꾸고 새 값을 돌려준다. 자막 구조를 못 찾으면 빈 문자열
            window.__hppSubStep = d => {
                const k = subKind();
                if (!k) return '';
                const z = Math.min(4, Math.max(0.6, Math.round((zoomOf(k) + d * 0.2) * 10) / 10));
                localStorage.setItem(SUBS[k].key, String(z));
                applySub();
                return z.toFixed(1);
            };
            let lastSub = null;
            setInterval(() => {
                if (!window.__hppSubBelow || !window.chrome?.webview) { lastSub = null; return; }
                const k = subKind(), root = k && SUBS[k].root();
                const text = root ? [...root.querySelectorAll(SUBS[k].sel)].map(e => (e.innerText || e.textContent || '').trim()).filter(Boolean).join('\n') : '';
                if (text !== lastSub) { lastSub = text; window.chrome.webview.postMessage('hppsub:' + text); }
            }, 200);
            // 가장 큰 video 의 원본 크기를 호스트에 알린다(PIP 창 높이를 영상 비율에 맞추는 데 쓴다). 바뀔 때만 보낸다.
            let lastAspect = null;
            const reportAspect = () => {
                if (!window.chrome?.webview) return;
                const v = [...document.querySelectorAll('video')].filter(x => x.videoWidth > 0 && x.videoHeight > 0)
                    .sort((a, b) => b.clientWidth * b.clientHeight - a.clientWidth * a.clientHeight)[0];
                const s = v ? v.videoWidth + 'x' + v.videoHeight : '0x0';
                if (s !== lastAspect) { lastAspect = s; window.chrome.webview.postMessage('hppaspect:' + s); }
            };
            const apply = () => {
                applySub();
                reportAspect();
                const watching = /\/(watch|play|video)(\/|$)/.test(location.pathname);
                let s = document.getElementById('__hpp_style');
                if (watching && !s && document.head) {
                    s = document.createElement('style');
                    s.id = '__hpp_style';
                    s.textContent = '::-webkit-scrollbar{display:none!important}html,body{overflow:hidden!important;background:#000!important}';
                    document.head.appendChild(s);
                } else if (!watching && s) { s.remove(); }
            };
            setInterval(apply, 1000);
            document.addEventListener('DOMContentLoaded', apply);
        })();
    ";

    // 가장 큰 video 를 골라 body 를 실행한다. 넷플릭스는 currentTime 직접 변경이 오류(M7375)를 내므로 자체 API 로 탐색한다.
    private const string VideoScriptPrefix = @"(() => {
        const v = [...document.querySelectorAll('video')].sort((a, b) => b.clientWidth * b.clientHeight - a.clientWidth * a.clientHeight)[0];
        if (!v) return;
        const seek = (d) => {
            try {
                if (location.host.includes('netflix.com')) {
                    const p = netflix.appContext.state.playerApp.getAPI().videoPlayer;
                    const s = p.getVideoPlayerBySessionId(p.getAllPlayerSessionIds()[0]);
                    s.seek(s.getCurrentTime() + d * 1000);
                    return;
                }
            } catch (e) { }
            v.currentTime += d;
        };
    ";

    private const int SnapPx = 25;           // 모니터 가장자리 자석 스냅 거리(px)
    private const int SnapMarginPx = 12;     // 스냅/PIP 배치 시 남기는 여백(px)
    private const int UrlRowHeight = 38, BookmarkRowHeight = 28; // DIP — MainWindow.xaml 의 UrlRowDef·BookmarkRowBorder 높이와 같아야 한다
    // 일반 창의 상단 바 높이: 주소 줄 + (켜져 있으면) 사이트 목록 줄. PIP·배경화면에서 돌아올 때와 설정을 바꿀 때 쓴다
    private double NormalBarHeight => UrlRowHeight + (_settings.ShowBookmarks ? BookmarkRowHeight : 0);
    private const int PipStripHeight = 0;    // PIP 상단 띠(DIP). 0 = 영상이 꽉 차게(이동·우클릭 복귀는 마우스 훅이 영상 위에서 처리)

    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private MpvPlayer? _mpv;
    private Task<bool>? _webInit;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(150) };

    private bool _isPipMode, _isClickThrough, _isWallpaperMode, _isAmbient;
    private bool _autoPaused, _dodged, _closing, _inMoveLoop, _programmatic;
    private NativeMethods.RECT _pipRestore, _wallRestore; // 상태별 복원 위치(px) — 서로 덮어쓰지 않도록 분리
    private int _wallStyleBackup;
    private LockOverlay? _lockOverlay;
    private bool _pipLocked, _overlayVisible;
    private DateTime _lastHover;
    private IntPtr _mouseHook;
    private NativeMethods.LowLevelMouseProc? _mouseProc; // GC 로 수거되지 않게 필드로 유지

    public MainWindow()
    {
        InitializeComponent();
        InitDiagnostics();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        _tick.Tick += (_, _) => { AutoDodgeTick(); WallpaperPauseTick(); OverlayTick(); };
        PreviewKeyDown += OttKeyBridge;
        PreviewKeyDown += ShortcutKeyBridge;
        LocationChanged += (_, _) => PositionPipExtras();
        SizeChanged += (_, _) => PositionPipExtras();
    }

    // ───────── 시작 ─────────

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RebuildExtras(); // 사용자가 재생 줄에 올려 둔 기능 아이콘
        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.ApplyWin11RoundedCorners(_hwnd);

        // 전역 단축키 등록 (투과 상태에서도 언제든 제어 가능). 실패(다른 앱과 충돌)는 사용자에게 알린다.
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);
        var failed = new List<string>();
        uint mods = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;
        void Reg(int id, uint vk, string name)
        {
            if (!NativeMethods.RegisterHotKey(_hwnd, id, mods, vk))
                failed.Add($"{name}(오류 {Marshal.GetLastWin32Error()})");
        }
        Reg(NativeMethods.HOTKEY_ID_CLICKTHROUGH, 0x43, "Ctrl+Alt+C"); // Ctrl+Alt+T 는 다른 프로그램이 선점(오류 1409)
        Reg(NativeMethods.HOTKEY_ID_PIP, 0x50, "Ctrl+Alt+P");
        Reg(NativeMethods.HOTKEY_ID_WALLPAPER, 0x42, "Ctrl+Alt+B");
        Reg(NativeMethods.HOTKEY_ID_AMBIENT, 0x47, "Ctrl+Alt+G");
        Reg(NativeMethods.HOTKEY_ID_SUB_UP, 0x26, "Ctrl+Alt+↑");
        Reg(NativeMethods.HOTKEY_ID_SUB_DOWN, 0x28, "Ctrl+Alt+↓");
        Reg(NativeMethods.HOTKEY_ID_LOCK, 0x4C, "Ctrl+Alt+L");
        if (failed.Count > 0) SetStatus("단축키 등록 실패: " + string.Join(", ", failed));

        _mouseProc = MouseHookProc;
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc,
            NativeMethods.GetModuleHandle(null), 0);
        if (_mouseHook == IntPtr.Zero) SetStatus($"마우스 훅 설치 실패(오류 {Marshal.GetLastWin32Error()}): PIP 영상 드래그 불가");

        InstallTextKeyHook(); // MainWindow.Playback.cs

        _lockOverlay = new LockOverlay { Owner = this };
        _lockOverlay.Clicked += ToggleLock;
        _lockOverlay.ExitClicked += () => { if (_isPipMode) ExitPip(); };

        // libmpv 초기화 (WinForms Panel 핸들 바인딩)
        try
        {
            _mpv = new MpvPlayer(MpvRenderPanel.Handle);
            AttachDiagnostics(_mpv);
            InitSubtitlePractice(_mpv);
            InitHandoff(_mpv);
            _mpv.KeyAction += a => Dispatcher.BeginInvoke(() => DispatchShortcut(a));
            _mpv.VideoAspectChanged += a => Dispatcher.BeginInvoke(() =>
            {
                if (_closing) return;
                _videoAspect = a;
                FitToVideoAspect();
            });
        }
        catch (Exception ex)
        {
            _diagnostics.Fail("엔진 초기화", ex.Message);
            string hint = ex is DllNotFoundException or BadImageFormatException
                ? "libmpv-2.dll(64비트)을 실행 파일 폴더에 넣어 주세요.\n\n" : "";
            System.Windows.MessageBox.Show(hint + ex.Message, "libmpv 초기화 실패",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        InitPipExtras(); // PIP 재생 컨트롤·아래 자막 줄 (MainWindow.PipExtras.cs)
        InitializeShortcuts();
        InitPlayback(); // 설정·북마크·재생 줄 (MainWindow.Playback.cs)

        // 다른 실행(연결 프로그램·더블클릭)이 넘기는 파일을 받고, 실행 인자로 받은 파일이 있으면 연다
        ((App)System.Windows.Application.Current).Instance.StartServer(path => Dispatcher.BeginInvoke(() => OpenExternal(path)));
        if (App.StartupArg is { } arg) OpenExternal(arg);
        if (App.StartupError is { } startupError) SetStatus(startupError);
    }

    private void SetStatus(string msg) => StatusText.Text = msg;

    // ───────── 라우팅 ─────────

    private static bool HostIs(Uri uri, string domain) =>
        uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    // 입력 정리: 앞뒤 공백·따옴표 제거, 스킴 없는 "도메인/경로" 는 https:// 를 붙인다. 실패하면 false.
    private static bool TryNormalize(string raw, out string target, out bool isOtt)
    {
        isOtt = false;
        target = raw.Trim().Trim('"').Trim();
        if (target.Length == 0) return false;
        if (File.Exists(target) || Directory.Exists(target) || Path.IsPathFullyQualified(target)) return true;
        if (Uri.TryCreate(target, UriKind.Absolute, out var localUri) && localUri.IsFile)
        { target = localUri.LocalPath; return true; }

        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme == Uri.UriSchemeFile)
        {
            if (target.Contains(' ') || !target.Contains('.') ||
                !Uri.TryCreate("https://" + target, UriKind.Absolute, out uri)) return false;
            target = uri.AbsoluteUri;
        }
        isOtt = (uri.Scheme is "http" or "https") && WebEngineHosts.Any(d => HostIs(uri, d));
        return true;
    }

    // 웹뷰(엔진 B)로 여는 사이트: DRM 이 걸려 mpv(yt-dlp)로는 못 여는 OTT. 그 밖의 주소는 mpv 가 연다.
    // 사이트 안에서 링크를 따라가는 이동은 이 목록과 상관없이 웹뷰 안에 머문다.
    private static readonly string[] WebEngineHosts =
    {
        "netflix.com", "disneyplus.com", "tving.com", "tv.apple.com", "watcha.com",
        "coupangplay.com", "wavve.com", "laftel.net", "primevideo.com",
    };

    private Task<bool> EnsureWebViewAsync() => _webInit ??= InitWebViewAsync();

    private async Task<bool> InitWebViewAsync()
    {
        try
        {
            // 영구 프로필로 OTT 로그인 유지. HWND 는 Visible 상태여야 만들어지므로 호출 전에 보이게 해 둔다.
            string dir = AppPaths.WebViewDir;
            var env = await CoreWebView2Environment.CreateAsync(null, dir);
            await DrmWebView.EnsureCoreWebView2Async(env);
            await DrmWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(OttViewportScript);
            HookOttMessages(DrmWebView.CoreWebView2);
            InitBrowse(DrmWebView.CoreWebView2); // MainWindow.Browse.cs
            _diagnostics.SetEngineVersion(env.BrowserVersionString, web: true);
            DrmWebView.CoreWebView2.NavigationStarting += (_, e) => _diagnosticNavigation = e.NavigationId;
            DrmWebView.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                if (!_closing && e.NavigationId == _diagnosticNavigation && DrmWebView.Visibility == Visibility.Visible)
                    _diagnostics.WebNavigation(e.IsSuccess, e.WebErrorStatus.ToString());
            };
            return true;
        }
        catch (Exception ex)
        {
            _webInit = null; // 다음 시도에서 재시도 가능
            System.Windows.MessageBox.Show("WebView2 초기화에 실패했습니다. WebView2 런타임이 설치돼 있는지 확인하세요.\n\n" + ex.Message,
                "WebView2 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private async Task RouteMediaAsync(string raw, HandoffRequest? handoff = null)
    {
        int revision = BeginMediaRoute();
        if (!TryNormalize(raw, out string target, out bool isOtt))
        {
            _diagnostics.Begin(raw, false);
            _diagnostics.Fail("엔진 선택", "열 수 없는 입력입니다: 파일 경로 또는 URL 을 확인하세요");
            return;
        }
        SetStatus("");
        _diagnostics.Begin(target, isOtt);
        if (isOtt || (!File.Exists(target) && !Directory.Exists(target))) SetPracticeEngine(!isOtt);

        if (isOtt)
        {
            _mpv?.Stop();
            MpvHost.Visibility = Visibility.Collapsed;
            DrmWebView.Visibility = Visibility.Visible;
            bool ready = await EnsureWebViewAsync();
            if (_closing || revision != _diagnosticRouteRevision) return;
            if (!ready)
            {
                DrmWebView.Visibility = Visibility.Collapsed;
                MpvHost.Visibility = Visibility.Visible;
                _diagnostics.Fail("엔진 초기화", "WebView2 초기화 실패");
                return;
            }
            if (_closing) return;
            _diagnostics.OpeningWeb();
            DrmWebView.CoreWebView2.Navigate(target);
            FinishWebHandoff(handoff);
        }
        else
        {
            if (_webInit is { IsCompletedSuccessfully: true, Result: true }) DrmWebView.CoreWebView2.Navigate("about:blank");
            DrmWebView.Visibility = Visibility.Collapsed;
            MpvHost.Visibility = Visibility.Visible;
            if (_mpv == null) { _diagnostics.Fail("엔진 선택", "libmpv 초기화 상태를 확인하세요"); return; }
            // 로컬 파일·폴더는 같은 폴더의 영상으로 목록을 만든다(설정에서 끌 수 있음). 주소는 그대로 연다.
            if (File.Exists(target)) OpenPaths(new[] { target });
            else if (Directory.Exists(target)) OpenPaths(MediaFiles.FolderFiles(target, true));
            else LoadHandoffMedia(target, handoff, revision);
        }
    }

    // async void 핸들러에서 예외가 앱을 죽이지 않게 한다
    private async void RouteMedia(string raw)
    {
        try { await RouteMediaAsync(raw); }
        catch (Exception ex) { _diagnostics.Fail("열기", ex.Message); }
    }

    // ───────── 창 상태: 위치 도우미 ─────────

    private double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    // 모든 위치 지정은 픽셀 단위 SetWindowPos 로 통일(DIP/픽셀 혼용 방지). 스냅 로직은 건너뛴다.
    private void SetRect(int x, int y, int w, int h, uint extraFlags = 0)
    {
        _programmatic = true;
        try { NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | extraFlags); }
        finally { _programmatic = false; }
    }

    private void RestoreRect(NativeMethods.RECT r, uint extraFlags = 0) =>
        SetRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, extraFlags);

    private void UpdateTimer()
    {
        if (_isClickThrough || _isWallpaperMode || _isPipMode) _tick.Start(); else _tick.Stop();
    }

    // ───────── PIP 잠금 / 자물쇠 버튼 ─────────

    private void ToggleLock() => SetLocked(!_pipLocked);

    // 잠그면: 영상·웹뷰 HWND 를 비활성화해 마우스가 닿지 않게 하고(플레이어 UI 가 안 뜬다),
    // 드래그 이동·크기 조절·우클릭 복귀를 막는다. Ctrl+Alt+P 로 PIP 를 나가면 자동으로 풀린다.
    private void SetLocked(bool on)
    {
        if (on && !_isPipMode) return;
        _pipLocked = on;
        _lockOverlay?.SetLocked(on);
        if (on) HidePipControls(); // 잠금은 마우스 UI 를 막는다
        NativeMethods.EnumChildWindows(_hwnd, (h, _) => { NativeMethods.EnableWindow(h, !on); return true; }, IntPtr.Zero);
        SetStatus(on ? "PIP 잠금 (Ctrl+Alt+L 또는 자물쇠로 해제)" : "");
    }

    // 커서가 PIP(또는 자물쇠·PIP 컨트롤) 위에 있으면 자물쇠와 재생 컨트롤을 보이고, 벗어난 뒤 설정의 PipHideSec 지나면 숨긴다
    private void OverlayTick()
    {
        if (_lockOverlay == null) return;
        if (!_isPipMode || _isClickThrough || _isWallpaperMode || _closing) { HideOverlay(); HidePipControls(); return; }

        NativeMethods.GetCursorPos(out var pt);
        NativeMethods.GetWindowRect(_hwnd, out var r);
        // 버튼이 PIP 바깥에 있으므로 "PIP 위" 또는 "버튼 위"면 호버로 본다(PIP→버튼으로 이동하는 동안 사라지지 않게).
        // 다른 창이 가리고 있으면 호버로 치지 않는다. 비활성화된 자식 창은 WindowFromPoint 가 건너뛰어 부모가 잡힌다.
        var root = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(pt), NativeMethods.GA_ROOT);
        bool inPip = pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom && root == _hwnd;
        bool onButton = _overlayVisible && root == _lockOverlay.Hwnd;
        bool onControls = _pipControlsVisible && (root == _pipControls?.Hwnd || _pipControls?.IsSeeking == true);
        bool inside = inPip || onButton || onControls;

        if (inside)
        {
            _lastHover = DateTime.UtcNow;
            ShowOverlay();
            ShowPipControls();
        }
        else if ((DateTime.UtcNow - _lastHover).TotalSeconds >= _settings.PipHideSec)
        {
            HideOverlay();
            HidePipControls();
        }
        else UpdatePipControls();
    }

    private void ShowOverlay()
    {
        if (_lockOverlay == null) return;
        if (!_overlayVisible) { _lockOverlay.Show(); _overlayVisible = true; }
        PositionOverlay();
    }

    private void HideOverlay()
    {
        if (_lockOverlay == null || !_overlayVisible) return;
        _lockOverlay.Hide();
        _overlayVisible = false;
    }

    // PIP 창 바깥 왼쪽 위 모서리의 바로 위(영상·자막을 가리지 않게)에 픽셀 단위로 배치한다.
    // 위쪽 작업 영역이 모자라면 아래쪽으로, 아래도 모자라면 창 안쪽 왼쪽 위로 내린다.
    private void PositionOverlay()
    {
        if (_lockOverlay == null || _hwnd == IntPtr.Zero || _lockOverlay.Hwnd == IntPtr.Zero) return;
        NativeMethods.GetWindowRect(_hwnd, out var r);
        var wa = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        double s = Scale;
        int size = (int)(LockOverlay.ButtonSize * s), width = (int)(LockOverlay.TotalWidth * s); // 자물쇠 + PIP 나가기 두 칸
        int gap = (int)(4 * s);
        int x = Math.Clamp(r.Left, wa.Left, Math.Max(wa.Left, wa.Right - width));
        int y;
        if (r.Top - gap - size >= wa.Top) y = r.Top - gap - size;                 // 위쪽 바깥
        else if (r.Bottom + gap + size <= wa.Bottom) y = r.Bottom + gap;           // 아래쪽 바깥
        else { x = r.Left + (int)(12 * s); y = r.Top + (int)((6 + PipStripHeight + 6) * s); } // 바깥 공간이 없으면 안쪽
        NativeMethods.SetWindowPos(_lockOverlay.Hwnd, NativeMethods.HWND_TOPMOST, x, y, width, size, NativeMethods.SWP_NOACTIVATE);
    }

    // ───────── PIP ─────────

    private void TogglePipMode()
    {
        if (_isPipMode) ExitPip(); else EnterPip();
    }

    private void EnterPip()
    {
        CloseListPanel(animate: false);
        if (_isWallpaperMode) ExitWallpaper();
        if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
        NativeMethods.GetWindowRect(_hwnd, out _pipRestore);

        _isPipMode = true;
        ControlBarRow.Height = new GridLength(PipStripHeight);
        BarContent.Visibility = Visibility.Collapsed;
        ControlRow.Visibility = Visibility.Collapsed; // 재생 줄은 접고, 커서가 올라오면 PipControls 를 띄운다
        Background = System.Windows.Media.Brushes.Black; // 영상이 비율 맞춤 전 잠깐 남기는 틈도 검게
        RootGrid.Margin = new Thickness(0);              // 테두리 여백 없이 꽉 차게(가장자리 크기 조절은 마우스 훅이 맡는다)
        Topmost = true;

        var wa = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        double s = Scale;
        // 설정의 PIP 너비(마지막으로 끌어서 바꾼 크기가 저장된다). 높이는 일단 16:9, 아래 FitToVideoAspect 가 실제 비율로 맞춘다
        int w = (int)(_settings.PipWidth * s), h = (int)((_settings.PipWidth * 9 / 16.0 + PipStripHeight) * s);
        SetRect(wa.Right - w - SnapMarginPx, wa.Bottom - h - SnapMarginPx, w, h);
        _mpv?.MultiplySubScale(1.5); // 작은 창에서 자막이 묻히지 않게
        FitToVideoAspect();     // 너비는 유지하고 높이를 영상 비율에 맞춘다
        EnsureStripRoom();      // 창 아래 자막 줄이 화면 밖으로 나가지 않게
        ApplyPipSubMode();
        UpdateTimer();          // 자물쇠 버튼 호버 감지
    }

    private void ExitPip()
    {
        if (_pipLocked) SetLocked(false); // 잠금은 PIP 전용 — 나가기 전에 풀어 자식 창을 다시 활성화한다
        _isPipMode = false;
        HideOverlay();
        HidePipControls();
        UpdateTimer();
        if (_isClickThrough) SetClickThroughMode(false);
        ControlBarRow.Height = new GridLength(NormalBarHeight);
        BarContent.Visibility = Visibility.Visible;
        ControlRow.Visibility = Visibility.Visible;
        SetResourceReference(BackgroundProperty, "BarBrush");
        RootGrid.Margin = new Thickness(6);
        _resizeEdge = 0;
        Topmost = false;
        RestoreRect(_pipRestore);
        _mpv?.MultiplySubScale(1.0 / 1.5);
        ApplyPipSubMode();
    }

    // PIP 에서 영상 위 좌클릭 드래그 = 창 이동, 우클릭 = PIP 해제.
    // mpv/WebView2 자식 창이 마우스를 가져가 WPF 이벤트가 오지 않으므로 저수준 훅으로 가로챈다(클릭 자체는 통과시킴).
    // 시스템 이동 루프(WM_NCLBUTTONDOWN)는 mpv 가 클릭 직후 마우스를 캡처해 버려 끊기므로,
    // 훅에서 마우스 이동량을 직접 따라가 SetWindowPos 로 옮긴다(스냅도 여기서 적용).
    private bool _pipDragging;
    private int _resizeEdge; // PIP 가장자리 크기 조절 중인 변: 1 좌, 2 우, 4 위, 8 아래(모서리는 합)
    private NativeMethods.POINT _dragStart;
    private NativeMethods.RECT _dragRect;

    // PIP 크기를 끌어서 바꾸면 그 너비(DIP)를 설정에 저장해 다음 PIP 도 같은 크기로 연다
    private void RememberPipWidth()
    {
        if (!_isPipMode || _closing || !NativeMethods.GetWindowRect(_hwnd, out var r)) return;
        int w = Math.Clamp((int)Math.Round((r.Right - r.Left) / Scale), 320, 960);
        if (w == _settings.PipWidth) return;
        _settings.PipWidth = w;
        _settings.Save();
        _settingsWindow?.SyncPipWidth();
    }

    // 누른 변의 반대쪽을 고정하고 영상 비율을 지키며 창 크기를 바꾼다. 좌우 변이 끼면 너비 기준, 위아래 변만이면 높이 기준.
    private void ResizePipFromHook(NativeMethods.POINT pt)
    {
        int dx = pt.X - _dragStart.X, dy = pt.Y - _dragStart.Y;
        int w0 = _dragRect.Right - _dragRect.Left, h0 = _dragRect.Bottom - _dragRect.Top;
        var (mx, my) = Chrome();
        double va = TargetVideoAspect;
        double ratio = va > 0 ? va : (w0 - mx) / Math.Max(1, h0 - my);
        int minW = (int)(240 * Scale);
        var wa = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        int w, h;
        if ((_resizeEdge & 3) != 0)
        {
            w = Math.Clamp(w0 + ((_resizeEdge & 1) != 0 ? -dx : dx), minW, wa.Width);
            h = (int)Math.Round((w - mx) / ratio + my);
        }
        else
        {
            h = h0 + ((_resizeEdge & 4) != 0 ? -dy : dy);
            w = Math.Clamp((int)Math.Round((h - my) * ratio + mx), minW, wa.Width);
            h = (int)Math.Round((w - mx) / ratio + my);
        }
        if (h > wa.Height) { h = wa.Height; w = (int)Math.Round((h - my) * ratio + mx); }
        int x = (_resizeEdge & 1) != 0 ? _dragRect.Right - w : _dragRect.Left;
        int y = (_resizeEdge & 4) != 0 ? _dragRect.Bottom - h : _dragRect.Top;
        SetRect(x, y, w, h);
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        int msg = (int)wParam;
        if (nCode >= 0 && !_closing && (_pipDragging || (_isPipMode && !_isClickThrough)))
        {
            var pt = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam).pt;
            switch (msg)
            {
                case NativeMethods.WM_LBUTTONDOWN when _isPipMode && !_isClickThrough && !_pipLocked:
                    // 우리 창 위(다른 창이 가리지 않은 곳)이고, 크기 조절 테두리가 아닌 안쪽일 때만
                    if (NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(pt), NativeMethods.GA_ROOT) == _hwnd &&
                        NativeMethods.GetWindowRect(_hwnd, out var r))
                    {
                        int edge = (int)(8 * Scale);
                        _dragStart = pt;
                        _dragRect = r;
                        if (pt.X > r.Left + edge && pt.X < r.Right - edge && pt.Y > r.Top + edge && pt.Y < r.Bottom - edge)
                            _pipDragging = true;
                        else
                        {
                            // 가장자리 8px: PIP 는 테두리 여백이 없어 자식 창이 가장자리까지 덮으므로 크기 조절도 훅에서 직접 한다.
                            // 클릭이 영상(사이트 재생/일시정지)으로 넘어가지 않게 이 누름은 삼킨다.
                            _resizeEdge = (pt.X <= r.Left + edge ? 1 : pt.X >= r.Right - edge ? 2 : 0) |
                                          (pt.Y <= r.Top + edge ? 4 : pt.Y >= r.Bottom - edge ? 8 : 0);
                            return (IntPtr)1;
                        }
                    }
                    break;

                case NativeMethods.WM_MOUSEMOVE when _resizeEdge != 0:
                    ResizePipFromHook(pt);
                    break;

                case NativeMethods.WM_LBUTTONUP when _resizeEdge != 0:
                    _resizeEdge = 0;
                    Dispatcher.BeginInvoke(RememberPipWidth); // 파일 저장은 훅 밖에서
                    return (IntPtr)1;

                case NativeMethods.WM_MOUSEMOVE when _pipDragging:
                    {
                        int w = _dragRect.Right - _dragRect.Left, h = _dragRect.Bottom - _dragRect.Top;
                        var (x, y) = SnapPoint(_dragRect.Left + pt.X - _dragStart.X, _dragRect.Top + pt.Y - _dragStart.Y, w, h);
                        _programmatic = true;
                        try
                        {
                            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0,
                                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                        }
                        finally { _programmatic = false; }
                        break;
                    }

                case NativeMethods.WM_LBUTTONUP when _pipDragging:
                    _pipDragging = false;
                    break;

                case NativeMethods.WM_RBUTTONUP when _isPipMode && !_isClickThrough && !_pipLocked:
                    if (NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(pt), NativeMethods.GA_ROOT) == _hwnd)
                        Dispatcher.BeginInvoke(TogglePipMode);
                    break;
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    // ───────── 클릭 투과 ─────────

    private void SetClickThroughMode(bool on)
    {
        if (on && !_isPipMode) EnterPip();
        _isClickThrough = on;
        _dodged = false;
        NativeMethods.SetClickThrough(_hwnd, on, alpha: 185);
        UpdateTimer();
    }

    // 투과 모드에서 커서가 창 위에 오면 반대쪽 모서리로 피한다 (모두 픽셀 좌표)
    private void AutoDodgeTick()
    {
        if (!_isClickThrough || _isWallpaperMode || _hwnd == IntPtr.Zero) return;
        NativeMethods.GetCursorPos(out var c);
        NativeMethods.GetWindowRect(_hwnd, out var r);
        bool inside = c.X >= r.Left && c.X <= r.Right && c.Y >= r.Top && c.Y <= r.Bottom;
        if (!inside) { _dodged = false; return; }
        if (_dodged) return;
        var wa = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        int w = r.Right - r.Left;
        bool onRight = (r.Left + r.Right) / 2 > (wa.Left + wa.Right) / 2;
        int x = onRight ? wa.Left + SnapMarginPx : wa.Right - w - SnapMarginPx;
        _programmatic = true;
        try
        {
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, x, r.Top, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
        finally { _programmatic = false; }
        _dodged = true;
    }

    // ───────── 배경화면 ─────────

    private void ToggleWallpaperMode()
    {
        if (_isWallpaperMode) ExitWallpaper(); else EnterWallpaper();
    }

    private void EnterWallpaper()
    {
        if (_isPipMode) ExitPip();
        if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;

        var (parent, insertAfter) = NativeMethods.GetWallpaperParent();
        if (parent == IntPtr.Zero)
        {
            SetStatus("바탕화면 레이어(Progman/WorkerW)를 찾지 못했습니다");
            return;
        }

        NativeMethods.GetWindowRect(_hwnd, out _wallRestore);
        var mon = System.Windows.Forms.Screen.FromHandle(_hwnd).Bounds;

        // WPF 창을 자식 창으로 바꾼 뒤 모니터 전체 크기로 직접 배치한다 (Maximize 는 부모 좌표계에서 어긋남)
        _wallStyleBackup = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_STYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_STYLE,
            (_wallStyleBackup & ~NativeMethods.WS_POPUP) | NativeMethods.WS_CHILD);
        NativeMethods.SetParent(_hwnd, parent);

        var pt = new NativeMethods.POINT { X = mon.Left, Y = mon.Top };
        NativeMethods.MapWindowPoints(IntPtr.Zero, parent, ref pt, 1);
        uint flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED |
                     (insertAfter == IntPtr.Zero ? NativeMethods.SWP_NOZORDER : 0);
        _programmatic = true;
        try { NativeMethods.SetWindowPos(_hwnd, insertAfter, pt.X, pt.Y, mon.Width, mon.Height, flags); }
        finally { _programmatic = false; }

        _isWallpaperMode = true;
        RootGrid.Margin = new Thickness(0); // 전체 화면을 채운다
        ControlBarRow.Height = new GridLength(0);
        Topmost = false;
        _mpv?.SetLoop(true);
        UpdateTimer();
    }

    private void ExitWallpaper()
    {
        _isWallpaperMode = false;
        NativeMethods.SetParent(_hwnd, IntPtr.Zero);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_STYLE, _wallStyleBackup);
        RootGrid.Margin = new Thickness(6);
        ControlBarRow.Height = new GridLength(NormalBarHeight);
        RestoreRect(_wallRestore, NativeMethods.SWP_FRAMECHANGED);
        _mpv?.SetLoop(false);
        if (_autoPaused) { _autoPaused = false; _mpv?.SetPause(false); } // 풀스크린 앱 때문에 멈춘 채로 남지 않게
        UpdateTimer();
    }

    // 배경화면 모드에서 다른 앱이 전체화면이면 일시정지, 끝나면 재생
    private void WallpaperPauseTick()
    {
        if (!_isWallpaperMode || _mpv == null) return;
        var bounds = System.Windows.Forms.Screen.FromHandle(_hwnd).Bounds;
        bool cover = NativeMethods.IsOtherFullscreenAppForeground(_hwnd, bounds);
        if (cover != _autoPaused) { _autoPaused = cover; _mpv.SetPause(cover); }
    }

    // ───────── 자석 스냅 ─────────

    // 창을 "이동"하는 동안에만(크기 변경·최대화·프로그램 배치 제외) 가장자리 25px 이내면 여백을 두고 붙인다
    private void SnapWindowPos(IntPtr lParam)
    {
        if (_programmatic || !_inMoveLoop || _isWallpaperMode || NativeMethods.IsZoomed(_hwnd)) return;
        var wp = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
        if ((wp.flags & NativeMethods.SWP_NOMOVE) != 0) return;

        NativeMethods.GetWindowRect(_hwnd, out var cur);
        int cw = cur.Right - cur.Left, ch = cur.Bottom - cur.Top;
        int w = (wp.flags & NativeMethods.SWP_NOSIZE) != 0 ? cw : wp.cx;
        int h = (wp.flags & NativeMethods.SWP_NOSIZE) != 0 ? ch : wp.cy;
        if (w != cw || h != ch) return; // 크기가 바뀌는 중(리사이즈)

        (wp.x, wp.y) = SnapPoint(wp.x, wp.y, w, h);
        Marshal.StructureToPtr(wp, lParam, false);
    }

    // 작업 영역 가장자리 SnapPx 이내면 SnapMarginPx 여백을 두고 붙은 좌표를 돌려준다
    private (int x, int y) SnapPoint(int x, int y, int w, int h)
    {
        var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x + w / 2, y + h / 2)).WorkingArea;
        if (Math.Abs(x - wa.Left) < SnapPx) x = wa.Left + SnapMarginPx;
        else if (Math.Abs(x + w - wa.Right) < SnapPx) x = wa.Right - w - SnapMarginPx;
        if (Math.Abs(y - wa.Top) < SnapPx) y = wa.Top + SnapMarginPx;
        else if (Math.Abs(y + h - wa.Bottom) < SnapPx) y = wa.Bottom - h - SnapMarginPx;
        return (x, y);
    }

    // 드래그 중인 모서리/가장자리에 맞춰 반대 축을 비율에 맞게 조정한다 (WMSZ_*: 1좌 2우 3상 4좌상 5우상 6하 7좌하 8우하)
    private double _aspect;       // 영상 비율을 모를 때(웹뷰·로딩 전) 쓰는, 크기 조절 시작 시점의 창 비율
    private double _videoAspect;  // mpv 가 알려 준 실제 영상 표시 비율

    // 창 비율을 맞출 영상 비율. mpv 화면이 보이고 비율을 알 때만 유효(0 이면 _aspect 로 대체).
    // 앰비언트가 켜져 있으면 출력 캔버스가 16:9 로 고정이라 16:9 를 쓴다.
    // 웹뷰는 PIP 에서만 페이지가 알려 준 영상 비율을 쓴다(일반 창은 사이트 목록 화면도 보여 주므로 건드리지 않는다).
    private double TargetVideoAspect =>
        MpvHost.Visibility == Visibility.Visible ? (_isAmbient ? 16.0 / 9 : _videoAspect)
        : _isPipMode ? _webVideoAspect : 0;

    // 영상 영역 밖의 창 구성(좌우·상하 여백 — 평소 6px, PIP 0 — + 상단 바)을 px 로. 창 비율 = (영상너비+mx) : (영상높이+my)
    private (double mx, double my) Chrome()
    {
        double s = Scale, m = RootGrid.Margin.Left * 2 * s;
        return (m, m + ControlBarRow.Height.Value * s);
    }

    // 새 영상이 열리거나 앰비언트가 바뀌면 현재 너비를 유지한 채 영상 비율에 맞게 높이를 조정한다
    private void FitToVideoAspect()
    {
        double va = TargetVideoAspect;
        if (va <= 0 || _isWallpaperMode || NativeMethods.IsZoomed(_hwnd)) return;
        NativeMethods.GetWindowRect(_hwnd, out var r);
        var (mx, my) = Chrome();
        var wa = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        int w = r.Right - r.Left;
        int h = (int)Math.Round((w - mx) / va + my);
        int maxH = (int)(wa.Height * 0.9);
        if (h > maxH) { h = maxH; w = (int)Math.Round((h - my) * va + mx); } // 세로 영상 등 너무 커지면 줄인다
        int y = Math.Min(r.Top, wa.Bottom - h);                              // 화면 밖으로 나가면 위로 올린다
        SetRect(r.Left, Math.Max(wa.Top, y), w, h);
    }

    private void KeepAspect(int edge, IntPtr lParam)
    {
        double va = TargetVideoAspect;
        if ((va <= 0 && _aspect <= 0) || _isWallpaperMode || NativeMethods.IsZoomed(_hwnd)) return;
        var (mx, my) = Chrome();
        int HFromW(int w) => va > 0 ? (int)Math.Round((w - mx) / va + my) : (int)Math.Round(w / _aspect);
        int WFromH(int h) => va > 0 ? (int)Math.Round((h - my) * va + mx) : (int)Math.Round(h * _aspect);

        var r = Marshal.PtrToStructure<NativeMethods.RECT>(lParam);
        int cw = r.Right - r.Left, ch = r.Bottom - r.Top;
        switch (edge)
        {
            case 1: case 2: r.Bottom = r.Top + HFromW(cw); break;   // 좌·우: 높이를 맞춘다
            case 3: case 6: r.Right = r.Left + WFromH(ch); break;   // 상·하: 너비를 맞춘다
            case 4: case 5: r.Top = r.Bottom - HFromW(cw); break;   // 위쪽 모서리: 너비 기준, 위를 조정
            case 7: case 8: r.Bottom = r.Top + HFromW(cw); break;   // 아래쪽 모서리: 너비 기준, 아래를 조정
        }
        Marshal.StructureToPtr(r, lParam, false);
    }

    // ───────── 자막 크기 ─────────

    // 현재 보이는 화면에 맞춰 자막 크기를 바꾼다: mpv 는 sub-scale ±0.1, 웹뷰는 페이지가 사이트별로 기억하는 배율 ±0.2
    private async void AdjustSubtitle(int dir)
    {
        if (StripWanted) // PIP 아래 자막 줄이 켜져 있으면 그 글자 크기를 바꾼다(±2)
        {
            _settings.PipSubSize += dir * 2;
            OnSettingChanged(nameof(AppSettings.PipSubSize));
            SetStatus($"PIP 자막 크기 {_settings.PipSubSize}");
            _settingsWindow?.SyncPipSubSize();
            return;
        }
        if (DrmWebView.Visibility == Visibility.Visible)
        {
            if (_closing || DrmWebView.CoreWebView2 == null) return;
            try
            {
                string json = await DrmWebView.CoreWebView2.ExecuteScriptAsync(
                    $"window.__hppSubStep ? window.__hppSubStep({(dir > 0 ? 1 : -1)}) : ''");
                string z = JsonSerializer.Deserialize<string>(json) ?? "";
                SetStatus(z.Length > 0 ? $"자막 크기 ×{z}" : "이 화면에서는 자막 크기를 바꿀 수 없습니다");
            }
            catch (Exception) { /* 페이지 전환 중 */ }
        }
        else
        {
            _mpv?.Command("add", "sub-scale", dir > 0 ? "0.1" : "-0.1");
            _mpv?.Command("show-text", "자막 크기 ${sub-scale}");
        }
    }

    // ───────── 앰비언트 ─────────

    private void ToggleAmbient()
    {
        if (_mpv == null) return;
        bool want = !_isAmbient;
        if (_mpv.SetAmbient(want)) _isAmbient = want;
        else { _isAmbient = false; _mpv.SetAmbient(false); SetStatus("앰비언트 필터를 적용하지 못했습니다 (로그: LocalAppData\\blinkpip\\mpv.log)"); }
        FitToVideoAspect(); // 앰비언트는 16:9 캔버스, 끄면 원래 영상 비율
    }

    // ───────── OTT 키 브릿지 ─────────

    private async void RunOnVideo(string body)
    {
        if (_closing || DrmWebView.CoreWebView2 == null) return;
        try { await DrmWebView.CoreWebView2.ExecuteScriptAsync(VideoScriptPrefix + body + "})();"); }
        catch (Exception) { /* 창 종료 중이거나 페이지 전환 중 — 무시 */ }
    }

    // 웹뷰에 포커스가 없을 때(예: 넷플릭스 버튼 클릭 직후) 호스트가 받은 키를 video 로 전달한다.
    // WebView2 는 HwndHost 라 휠 이벤트는 WPF 로 오지 않아 볼륨 휠 브릿지는 두지 않는다(웹이 직접 처리).
    private void OttKeyBridge(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DrmWebView.Visibility != Visibility.Visible || UrlInputBox.IsKeyboardFocusWithin || DrmWebView.IsKeyboardFocusWithin) return;
        switch (e.Key)
        {
            case Key.Space: RunOnVideo("v.paused ? v.play() : v.pause();"); e.Handled = true; break;
            case Key.Left: RunOnVideo("seek(-5);"); e.Handled = true; break;
            case Key.Right: RunOnVideo("seek(5);"); e.Handled = true; break;
        }
    }

    // ───────── 메시지 / 이벤트 ─────────

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_ENTERSIZEMOVE:
                _inMoveLoop = true;
                // 크기 조절을 시작하는 순간의 가로세로 비율을 기억해 끌어도 유지한다
                if (NativeMethods.GetWindowRect(_hwnd, out var cur) && cur.Bottom > cur.Top)
                    _aspect = (double)(cur.Right - cur.Left) / (cur.Bottom - cur.Top);
                break;
            case NativeMethods.WM_NCHITTEST when _pipLocked:
                handled = true;       // 잠금: 가장자리를 크기 조절 영역으로 판정하지 않는다(커서도 안 바뀜)
                return (IntPtr)1;     // HTCLIENT
            case NativeMethods.WM_SIZING:
                if (_pipLocked)
                {
                    // 훅 순서상 위 NCHITTEST 가 안 먹는 경우를 대비해 크기 변경을 현재 크기로 되돌린다
                    NativeMethods.GetWindowRect(_hwnd, out var keep);
                    Marshal.StructureToPtr(keep, lParam, false);
                }
                else KeepAspect(wParam.ToInt32(), lParam);
                handled = true;
                return (IntPtr)1;
            case NativeMethods.WM_EXITSIZEMOVE: _inMoveLoop = false; RememberPipWidth(); break;
            case NativeMethods.WM_WINDOWPOSCHANGING: SnapWindowPos(lParam); break;
            case NativeMethods.WM_HOTKEY:
                switch (wParam.ToInt32())
                {
                    case NativeMethods.HOTKEY_ID_CLICKTHROUGH: SetClickThroughMode(!_isClickThrough); handled = true; break;
                    case NativeMethods.HOTKEY_ID_PIP: TogglePipMode(); handled = true; break;
                    case NativeMethods.HOTKEY_ID_WALLPAPER: ToggleWallpaperMode(); handled = true; break;
                    case NativeMethods.HOTKEY_ID_AMBIENT: ToggleAmbient(); handled = true; break;
                    case NativeMethods.HOTKEY_ID_SUB_UP: AdjustSubtitle(+1); handled = true; break;
                    case NativeMethods.HOTKEY_ID_SUB_DOWN: AdjustSubtitle(-1); handled = true; break;
                    case NativeMethods.HOTKEY_ID_LOCK: ToggleLock(); handled = true; break;
                }
                break;
        }
        return IntPtr.Zero;
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isWallpaperMode || _pipLocked) return;
        if (e.ClickCount == 2 && !_isPipMode) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void TopBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 북마크 칩의 우클릭은 삭제 메뉴용이라 PIP 전환으로 번지지 않게 한다
        if (e.OriginalSource is DependencyObject src && FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(src) != null) return;
        if (!_pipLocked) TogglePipMode();
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    private void PlayBtn_Click(object sender, RoutedEventArgs e) => RouteMedia(UrlInputBox.Text);
    private void UrlInputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Enter) RouteMedia(UrlInputBox.Text); }
    private void NetflixBtn_Click(object sender, RoutedEventArgs e) { UrlInputBox.Text = "https://www.netflix.com"; RouteMedia(UrlInputBox.Text); }
    private void DisneyBtn_Click(object sender, RoutedEventArgs e) { UrlInputBox.Text = "https://www.disneyplus.com"; RouteMedia(UrlInputBox.Text); }
    private void PipBtn_Click(object sender, RoutedEventArgs e) => TogglePipMode();
    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _tick.Stop();
        _hwndSource?.RemoveHook(WndProc);
        if (_mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        if (_keyHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_keyHook); _keyHook = IntPtr.Zero; }
        foreach (int id in new[] { NativeMethods.HOTKEY_ID_CLICKTHROUGH, NativeMethods.HOTKEY_ID_PIP,
                                   NativeMethods.HOTKEY_ID_WALLPAPER, NativeMethods.HOTKEY_ID_AMBIENT,
                                   NativeMethods.HOTKEY_ID_SUB_UP, NativeMethods.HOTKEY_ID_SUB_DOWN, NativeMethods.HOTKEY_ID_LOCK })
            NativeMethods.UnregisterHotKey(_hwnd, id);
        if (_isWallpaperMode) ExitWallpaper(); // 부모가 바탕화면인 채로 파괴되지 않게
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        // mpv 파괴는 VO 스레드와 얽혀 UI 스레드를 막을 수 있어 백그라운드에서 하고 최대 1.5초만 기다린다.
        // 스레드풀은 백그라운드 스레드라 끝나지 않아도 프로세스는 종료된다.
        var mpv = _mpv;
        _mpv = null;
        if (mpv != null) Task.Run(mpv.Dispose).Wait(1500);
        try { DrmWebView.Dispose(); } catch { }
    }
}
