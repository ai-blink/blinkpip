using System;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace blinkpip;

// PIP 전용 부가 창 두 개:
//  · 재생 컨트롤(PipControls) — PIP 에서는 아래 재생 줄을 접고, 커서가 PIP 위에 있을 때만 영상 아래쪽 안에 띄운다.
//    엔진 A(mpv)만. 웹뷰(넷플릭스·디즈니+)는 사이트 자체 컨트롤이 같은 자리에 뜨므로 띄우지 않는다.
//  · 자막 줄(SubtitleStrip) — 설정 "PIP 자막을 창 아래에"가 켜져 있으면 영상 안 자막을 숨기고 PIP 바로 아래에 글자로 보여 준다.
//    mpv 는 sub-text, 디즈니+ 는 페이지의 자막 요소 글자를 읽어 온다(OttViewportScript → postMessage "hppsub:").
// 표시/숨김 판정은 OverlayTick(자물쇠 버튼과 같은 150ms 타이머)이 하고, 위치는 창 이동·크기 변경 때 다시 잡는다.
public partial class MainWindow
{
    private PipControls? _pipControls;
    private bool _pipControlsVisible;
    private SubtitleStrip? _subStrip;
    private bool _stripVisible;
    private string? _lastMpvSub; // mpv 이벤트 스레드에서만 쓴다 — 같은 글자로 UI 스레드를 계속 깨우지 않게

    private bool StripWanted => _isPipMode && _settings.PipSubBelow && !_isWallpaperMode && !_closing;

    private void InitPipExtras()
    {
        _pipControls = new PipControls { Owner = this };
        _pipControls.PlayPauseClicked += () => PlayPauseBtn_Click(PlayPauseBtn, new RoutedEventArgs());
        _pipControls.SeekRequested += sec => { if (_engineA) _mpv?.SeekTo(sec); };
        _pipControls.MuteClicked += () => SetMuted(!_muted);

        _subStrip = new SubtitleStrip { Owner = this };
        ApplyStripStyle();

        if (_mpv != null)
            _mpv.SubtitleSample += s =>
            {
                string text = s.Cue?.Text ?? "";
                if (text == _lastMpvSub) return;
                _lastMpvSub = text;
                Dispatcher.BeginInvoke(() => { if (_engineA) SetStripText(text); });
            };
    }

    // ───────── PIP 재생 컨트롤 ─────────

    private void ShowPipControls()
    {
        if (_pipControls == null || !_engineA || _pipLocked) { HidePipControls(); return; }
        if (!_pipControlsVisible) { _pipControls.Show(); _pipControlsVisible = true; }
        PositionPipControls();
        UpdatePipControls();
    }

    private void HidePipControls()
    {
        if (_pipControls == null || !_pipControlsVisible) return;
        _pipControls.Hide();
        _pipControlsVisible = false;
    }

    private void UpdatePipControls()
    {
        if (_pipControls == null || _mpv == null || !_pipControlsVisible) return;
        _pipControls.Update(_mpv.GetPaused(), _mpv.GetTimePos() ?? 0, _mpv.GetDuration() ?? 0, _mpv.GetMuted());
    }

    // 영상 아래쪽 안(가장자리 크기 조절 여백은 비움)에 픽셀 단위로 띄운다
    private void PositionPipControls()
    {
        if (_pipControls == null || _pipControls.Hwnd == IntPtr.Zero) return;
        NativeMethods.GetWindowRect(_hwnd, out var r);
        double s = Scale;
        int pad = (int)(14 * s), h = (int)(44 * s);
        int w = Math.Max((int)(200 * s), r.Right - r.Left - pad * 2);
        NativeMethods.SetWindowPos(_pipControls.Hwnd, NativeMethods.HWND_TOPMOST, r.Left + pad, r.Bottom - h - pad, w, h, NativeMethods.SWP_NOACTIVATE);
    }

    // ───────── PIP 아래 자막 줄 ─────────

    // PIP 진입·종료, 설정 변경 때 부른다: 영상 안 자막을 숨기거나 되살리고, 자막 줄을 켜고 끈다
    private void ApplyPipSubMode()
    {
        bool on = StripWanted;
        _mpv?.SetProperty("sub-visibility", on ? "no" : "yes");
        PushOttSubConfig();
        if (_subStrip == null) return;
        if (on)
        {
            if (!_stripVisible) { _subStrip.Show(); _stripVisible = true; }
            if (_engineA) SetStripText(_mpv?.CurrentSubtitleText);
            PositionStrip();
        }
        else
        {
            SetStripText("");
            if (_stripVisible) { _subStrip.Hide(); _stripVisible = false; }
        }
    }

    private void SetStripText(string? text)
    {
        if (_subStrip == null) return;
        _subStrip.SetText(StripWanted ? text : "");
    }

    private void ApplyStripStyle()
    {
        _subStrip?.SetStyle(_settings.SubFont, _settings.SubColor, _settings.PipSubSize);
        if (_stripVisible) PositionStrip();
    }

    private int StripReservePx => (int)(SubtitleStrip.ReserveHeight(_settings.PipSubSize) * Scale) + (int)(4 * Scale);

    // PIP 바로 아래 바깥. 아래 작업 영역이 모자라면 PIP 바로 위로 올린다.
    private void PositionStrip()
    {
        if (_subStrip == null || _subStrip.Hwnd == IntPtr.Zero) return;
        NativeMethods.GetWindowRect(_hwnd, out var r);
        var wa = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        int gap = (int)(4 * Scale), h = StripReservePx - gap;
        int y = r.Bottom + gap + h <= wa.Bottom ? r.Bottom + gap : r.Top - gap - h;
        NativeMethods.SetWindowPos(_subStrip.Hwnd, NativeMethods.HWND_TOPMOST, r.Left, y, r.Right - r.Left, h, NativeMethods.SWP_NOACTIVATE);
    }

    // PIP 에 들어간 직후: 우하단 기본 위치면 자막 줄이 들어갈 만큼 창을 위로 올린다
    private void EnsureStripRoom()
    {
        if (!StripWanted) return;
        NativeMethods.GetWindowRect(_hwnd, out var r);
        var wa = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        int over = r.Bottom + StripReservePx + SnapMarginPx - wa.Bottom;
        if (over > 0) SetRect(r.Left, Math.Max(wa.Top, r.Top - over), r.Right - r.Left, r.Bottom - r.Top);
    }

    private void PositionPipExtras()
    {
        if (_overlayVisible) PositionOverlay();
        if (_pipControlsVisible) PositionPipControls();
        if (_stripVisible) PositionStrip();
    }

    // ───────── 웹뷰(디즈니+) 자막 설정 전달 ─────────

    // 자막 구조를 실측해 OttViewportScript 가 읽을 수 있게 한 사이트
    private static readonly string[] SubtitleReadHosts = { "disneyplus.com", "coupangplay.com", "wavve.com" };
    // 영상 원본 크기를 받아 PIP 높이를 맞추는 사이트(재생 화면에서 가장 큰 video 가 본편임을 실측)
    private static readonly string[] AspectHosts = { "netflix.com", "disneyplus.com", "coupangplay.com", "wavve.com" };

    // 페이지(OttViewportScript)가 보내는 메시지: "hppsub:<자막 글자>"(SubtitleReadHosts), "hppaspect:<W>x<H>"(AspectHosts 영상 원본 크기)
    private void HookOttMessages(CoreWebView2 web)
    {
        web.NavigationCompleted += (_, _) => PushOttSubConfig(); // 새 문서는 window 값이 비어 있다
        web.WebMessageReceived += (_, e) =>
        {
            string? msg;
            try { msg = e.TryGetWebMessageAsString(); } catch (ArgumentException) { return; }
            if (msg == null || _engineA || !Uri.TryCreate(e.Source, UriKind.Absolute, out var src)) return;
            if (msg.StartsWith("hppsub:", StringComparison.Ordinal) && SubtitleReadHosts.Any(d => HostIs(src, d)))
            {
                string text = msg.Substring(7);
                if (text.Length > 500) text = text.Substring(0, 500);
                SetStripText(text);
            }
            else if (msg.StartsWith("hppaspect:", StringComparison.Ordinal) && AspectHosts.Any(d => HostIs(src, d)))
                OnWebVideoSize(msg.Substring(10));
        };
    }

    // ───────── 웹 영상 비율 → PIP 창 높이 ─────────

    private double _webVideoAspect; // 0 = 모름(재생 화면이 아님)

    private void OnWebVideoSize(string size)
    {
        var parts = size.Split('x');
        double a = parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w > 0 && h > 0 ? (double)w / h : 0;
        if (a is < 0.3 or > 4) a = 0; // 이상한 값은 무시
        if (Math.Abs(a - _webVideoAspect) < 0.01) return;
        _webVideoAspect = a;
        if (_isPipMode && a > 0)
        {
            FitToVideoAspect(); // 너비는 그대로, 높이를 영상 비율에 맞춘다
            EnsureStripRoom();
        }
    }

    // 글꼴·색은 페이지 localStorage 에(다음 방문에도 유지), 창 아래 표시 여부는 window 값으로 넘긴다
    private async void PushOttSubConfig()
    {
        if (_closing || _webInit is not { IsCompletedSuccessfully: true, Result: true } || DrmWebView.CoreWebView2 == null) return;
        string js = "try{localStorage.setItem('__hppSubColor'," + JsonSerializer.Serialize(_settings.SubColor) + ");" +
                    "localStorage.setItem('__hppSubFont'," + JsonSerializer.Serialize(_settings.SubFont) + ");}catch(e){}" +
                    "window.__hppSubBelow=" + (StripWanted ? "true" : "false") + ";window.__hppApplySub&&window.__hppApplySub();";
        try { await DrmWebView.CoreWebView2.ExecuteScriptAsync(js); }
        catch (Exception) { /* 페이지 전환 중 — NavigationCompleted 에서 다시 보낸다 */ }
    }
}
