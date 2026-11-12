using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace HybridPipPlayer;

// 설정 창의 북마크(상단 사이트 줄) 편집: 이름·주소 추가와 수정, 기본 사이트 다시 넣기.
// 순서 바꾸기·삭제는 SettingsWindow.xaml.cs 에 있다. 바뀐 목록은 메인 창과 같은 컬렉션이라 바로 반영된다.
public partial class SettingsWindow
{
    /// <summary>왼쪽 목록에서 "북마크 · 정보" 의 순서</summary>
    public const int BookmarkPage = 5;

    private Bookmark? _bmEditing; // 수정 중인 항목. null 이면 위 칸은 '추가'

    public void ShowPage(int index)
    {
        if (index >= 0 && index < Nav.Items.Count) Nav.SelectedIndex = index;
    }

    private void BmEdit_Click(object sender, RoutedEventArgs e)
    {
        if (Tagged(sender) is not { } b) return;
        _bmEditing = b;
        BmNameBox.Text = b.Name;
        BmUrlBox.Text = b.Url;
        BmSaveBtn.Content = "저장";
        BmCancelBtn.Visibility = Visibility.Visible;
        BmStatus.Text = $"'{b.Name}' 수정 중 — 고친 뒤 '저장'";
        BmNameBox.Focus();
        BmNameBox.SelectAll();
    }

    private void BmCancel_Click(object sender, RoutedEventArgs e) => ResetBmEditor("");

    private void BmUrlBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { BmSave_Click(sender, e); e.Handled = true; }
    }

    private void BmSave_Click(object sender, RoutedEventArgs e)
    {
        string url = BmUrlBox.Text.Trim();
        if (url.Length > 0 && !url.Contains("://")) url = "https://" + url; // "wavve.com" 처럼 적어도 받는다
        if (!Bookmark.IsWebUrl(url)) { BmStatus.Text = "주소가 올바르지 않습니다 (http:// 또는 https:// 웹 주소)"; return; }
        string name = BmNameBox.Text.Trim();

        bool dup = _bookmarks.Any(x => !ReferenceEquals(x, _bmEditing) && string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase));
        if (dup) { BmStatus.Text = "이미 있는 주소입니다"; return; }

        if (_bmEditing != null)
        {
            int i = _bookmarks.IndexOf(_bmEditing);
            if (i < 0) { ResetBmEditor("수정하던 항목이 삭제되어 저장하지 않았습니다"); return; }
            // 표시 글자·색은 그대로 두고 이름·주소만 바꾼다. 다른 사이트로 바꾼 경우에만 새로 정한다
            var old = _bmEditing;
            var auto = Bookmark.FromUrl(url);
            bool sameSite = Bookmark.HostKey(old.Url) == Bookmark.HostKey(url);
            _bookmarks[i] = new Bookmark(name.Length > 0 ? name : auto.Name, url,
                                         sameSite ? old.Mark : auto.Mark, sameSite ? old.Hex : auto.Hex);
            BookmarkStore.Save(_bookmarks);
            ResetBmEditor("저장했습니다");
        }
        else
        {
            var auto = Bookmark.FromUrl(url);
            _bookmarks.Add(name.Length > 0 ? new Bookmark(name, url, auto.Mark, auto.Hex) : auto);
            BookmarkStore.Save(_bookmarks);
            ResetBmEditor("추가했습니다");
        }
    }

    // 지금 목록에 같은 사이트(도메인)가 없는 기본 사이트만 끝에 더한다. 지운 사이트를 되살리거나 새 기본 사이트를 받을 때 쓴다
    private void BmRestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        var have = _bookmarks.Select(b => Bookmark.HostKey(b.Url)).ToHashSet();
        int added = 0;
        foreach (var d in Bookmark.Defaults())
        {
            if (!have.Add(Bookmark.HostKey(d.Url))) continue;
            _bookmarks.Add(d);
            added++;
        }
        if (added > 0) BookmarkStore.Save(_bookmarks);
        BmStatus.Text = added > 0 ? $"기본 사이트 {added}개를 더했습니다" : "기본 사이트가 이미 모두 있습니다";
    }

    private void ResetBmEditor(string message)
    {
        _bmEditing = null;
        BmNameBox.Text = "";
        BmUrlBox.Text = "";
        BmSaveBtn.Content = "추가";
        BmCancelBtn.Visibility = Visibility.Collapsed;
        if (message.Length > 0) BmStatus.Text = message;
    }
}
