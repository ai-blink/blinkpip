using System;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace HybridPipPlayer;

// 웹 화면(엔진 B) 기본 브라우징: 뒤로·앞으로·새로 고침 버튼, 주소창을 지금 페이지 주소로 맞추기,
// 북마크 줄의 "편집" 칩. 버튼은 웹뷰가 보일 때만 켜진다(mpv 재생 중에는 꺼짐).
public partial class MainWindow
{
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
