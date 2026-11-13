using System;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace blinkpip;

// 웹 화면(엔진 B) 기본 브라우징: 뒤로·앞으로·새로 고침 버튼, 주소창을 지금 페이지 주소로 맞추기,
// 북마크 줄의 "편집" 칩. 버튼은 웹뷰가 보일 때만 켜진다(mpv 재생 중에는 꺼짐).
public partial class MainWindow
{
    // PIP 를 켤 때 부른다: 웹 화면이 치지직 생방송·다시보기면 그 주소를 mpv 로 넘긴다 — 웹 페이지째 PIP 에 들어가 영상이 작게 보이지 않게.
    // 둘러보는 동안에는 넘기지 않는다(채팅·다른 방송을 보려고 웹 화면에 머무는 것을 막지 않게)
    private void HandChzzkToMpv()
    {
        _webReturn = null;
        if (DrmWebView.Visibility != Visibility.Visible || DrmWebView.CoreWebView2 is not { } core) return;
        string url = core.Source;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var src) || !ChzzkRoute.PlaysInMpv(src)) return;
        UrlInputBox.Text = url;
        RouteMedia(url); // mpv 쪽 열기는 기다리는 단계 없이 끝나서, 아래 회차 번호가 이 열기의 것이다
        _webReturn = (url, _diagnosticRouteRevision);
    }

    // PIP 때문에 mpv 로 넘긴 웹 페이지와 그때의 열기 회차. PIP 를 나오면 그 페이지로 돌아간다(둘러보기를 이어 가게)
    private (string Url, int Revision)? _webReturn;

    // PIP 를 나온 직후 부른다. PIP 중에 다른 영상을 열었으면(회차가 바뀜) 돌아가지 않는다
    private async void ReturnToWebAfterPip()
    {
        if (_webReturn is not { } back) return;
        _webReturn = null;
        if (back.Revision != _diagnosticRouteRevision) return;
        UrlInputBox.Text = back.Url;
        try { await RouteMediaAsync(back.Url, forceWeb: true); }
        catch (Exception ex) { _diagnostics.Fail("열기", ex.Message); }
    }

    // InitWebViewAsync 에서 웹뷰가 준비된 직후 한 번 부른다
    private void InitBrowse(CoreWebView2 core)
    {
        core.HistoryChanged += (_, _) => UpdateBrowseButtons();
        core.SourceChanged += (_, _) =>
        {
            // mpv 로 넘어갈 때 about:blank 로 비우는 이동은 주소창에 쓰지 않는다
            if (_closing || DrmWebView.Visibility != Visibility.Visible) return;
            if (Bookmark.IsWebUrl(core.Source) && !UrlInputBox.IsKeyboardFocused) UrlInputBox.Text = core.Source;
        };
        DrmWebView.IsVisibleChanged += (_, _) => UpdateBrowseButtons();
        UpdateBrowseButtons();
    }

    private CoreWebView2? WebCore =>
        DrmWebView.Visibility == Visibility.Visible ? DrmWebView.CoreWebView2 : null;

    private void UpdateBrowseButtons()
    {
        var core = WebCore;
        WebBackBtn.IsEnabled = core?.CanGoBack == true;
        WebForwardBtn.IsEnabled = core?.CanGoForward == true;
        WebReloadBtn.IsEnabled = core != null;
    }

    private void WebBackBtn_Click(object sender, RoutedEventArgs e)
    {
        if (WebCore is { CanGoBack: true } core) core.GoBack();
    }

    private void WebForwardBtn_Click(object sender, RoutedEventArgs e)
    {
        if (WebCore is { CanGoForward: true } core) core.GoForward();
    }

    private void WebReloadBtn_Click(object sender, RoutedEventArgs e) => WebCore?.Reload();

    // 사이트 칩이 줄을 넘치면 휠(세로)을 가로 스크롤로 바꾼다
    private void BookmarkScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        BookmarkScroll.ScrollToHorizontalOffset(BookmarkScroll.HorizontalOffset - e.Delta / 2.0);
        e.Handled = true;
    }

    // 북마크 줄 "편집" 칩: 설정 창의 북마크 쪽을 연다
    private void EditBookmarks_Click(object sender, RoutedEventArgs e) => OpenSettings(SettingsWindow.BookmarkPage);
}
