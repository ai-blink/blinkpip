using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace HybridPipPlayer;

// 리모컨(4×3 창)과 기능 id → 기존 핸들러 연결. 기존 메뉴·단축키는 그대로 두고 같은 동작을 부른다.
public partial class MainWindow
{
    private RemoteWindow? _remote;
    private DateTime _remoteClosedAt;

    private void RemoteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_remote != null) { _remote.Close(); return; }
        // 창 바깥(=이 버튼)을 눌러 방금 닫혔다면 같은 클릭으로 다시 열지 않는다
        if ((DateTime.UtcNow - _remoteClosedAt).TotalMilliseconds < 300) return;
        OpenRemote();
    }

    private void OpenRemote()
    {
        bool paused = _mpv?.GetPaused() ?? true;
        var win = new RemoteWindow(_settings.RemoteFavorites, _settings.MainExtras, Ico, FeatureState, paused, _muted) { Owner = this };
        win.Invoked += OnRemoteInvoked;
        win.Changed += () => { _settings.Save(); RebuildExtras(); };
        win.Closed += (_, _) => { _remote = null; _remoteClosedAt = DateTime.UtcNow; };
        _remote = win;
        win.Show();
        win.Activate();
    }

    // 재생 줄(전체 화면 옆)에 사용자가 올려 둔 기능 아이콘을 설정대로 다시 만든다
    private void RebuildExtras()
    {
        ExtraPanel.Children.Clear();
        foreach (string id in _settings.MainExtras)
        {
            var f = FeatureCatalog.ById(id);
            if (f == null) continue;
            var b = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("PicButton"), ToolTip = f.Name, Margin = new Thickness(0),
                Content = f.Icon != null
                    ? new System.Windows.Controls.Image { Source = Ico(f.Icon), Width = 20, Height = 20 }
                    : new System.Windows.Controls.TextBlock { Text = f.Glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 16 },
            };
            System.Windows.Automation.AutomationProperties.SetName(b, f.Name);
            string fid = f.Id;
            b.Click += (_, _) => RunFeature(fid);
            ExtraPanel.Children.Add(b);
        }
    }

    // 리모컨 칸 이름 아래에 보일 지금 상태. 읽을 수 없으면 null
    private string? FeatureState(string id)
    {
        bool mpv = _mpv != null && _engineA;
        return id switch
        {
            "repeat" => mpv ? new[] { "끔", "목록 전체", "한 곡" }[Math.Clamp(_mpv!.GetRepeat(), 0, 2)] : null,
            "abloop" => mpv ? new[] { "꺼짐", "A 지정됨", "반복 중" }[Math.Clamp(_mpv!.GetAbLoopState(), 0, 2)] : null,
            "crop" => mpv && _mpv!.HasCropVideo ? $"{_mpv.GetCrop().Zoom:0.##}×" : null,
            "pip" => _isPipMode ? "켜짐" : "꺼짐",
            "mute" => _muted ? "켜짐" : "꺼짐",
            "full" => _isFullscreen ? "켜짐" : "꺼짐",
            _ => null,
        };
    }

    private void OnRemoteInvoked(string id, bool basic, bool close)
    {
        // 창이 닫힌 뒤에 실행해야 메뉴·대화상자가 메인 창 기준으로 제대로 뜬다
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (basic) RunBasic(id); else RunFeature(id);
            if (!close) _remote?.Refresh(_mpv?.GetPaused() ?? true, _muted);
        });
    }

    private void RunBasic(string key)
    {
        switch (key)
        {
            case "prev": PrevBtn_Click(PrevBtn, new RoutedEventArgs()); break;
            case "play": PlayPauseBtn_Click(PlayPauseBtn, new RoutedEventArgs()); break;
            case "next": NextBtn_Click(NextBtn, new RoutedEventArgs()); break;
            case "mute": SetMuted(!_muted); break;
            case "full": ToggleFullscreen(); break;
        }
    }

    private void RunFeature(string id)
    {
        var f = FeatureCatalog.ById(id);
        if (f == null) return;
        if (f.NeedsMpv && (_mpv == null || !_engineA)) { SetStatus($"{f.Name}: 영상(mpv) 재생 중에만 쓸 수 있습니다"); return; }
        // 열린 영상이 있어야 하는 기능은 영상이 없을 때 mpv 오류 대신 안내만 한다
        if (id is "shot" or "fstep" or "fback" or "abloop" or "sfile" && string.IsNullOrEmpty(_mpv?.GetPath()))
        { SetStatus($"{f.Name}: 재생 중인 영상이 없습니다"); return; }
        string shotDir =Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "HybridPipPlayer");
        switch (id)
        {
            case "open": OpenFilesDialog(false); break;
            case "folder": OpenFolderDialog(false, false); break;
            case "folderr": OpenFolderDialog(true, false); break;
            case "recent": RecentBtn_Click(RecentBtn, new RoutedEventArgs()); break;
            case "url": UrlInputBox.Focus(); UrlInputBox.SelectAll(); break;
            case "bmadd": AddBookmark_Click(AddBookmarkBtn, new RoutedEventArgs()); break;
            case "plist": ListBtn_Click(SubBtn, new RoutedEventArgs()); break;
            case "pmg": ShowListManageMenu(); break;
            case "psave": SavePlaylistFile(); break;
            case "pload": LoadPlaylistFile(); break;
            case "repeat": { int r = _mpv!.GetRepeat(); _mpv.SetRepeat((r + 1) % 3); SetStatus("반복: " + new[] { "끔", "목록 전체", "한 곡" }[(r + 1) % 3]); break; }
            case "shuffle": _mpv!.PlaylistShuffle(); SetStatus("목록을 섞었습니다"); break;
            case "sub": SubBtn_Click(SubBtn, new RoutedEventArgs()); break;
            case "audio": ShowAudioMenu(); break;
            case "sfile": OpenSubtitleFile(); break;
            case "pip": TogglePipMode(); break;
            case "shot": TakeScreenshot(shotDir); break;
            case "shotdir": OpenFolder(shotDir); break;
            case "fstep": _mpv!.FrameStep(); break;
            case "fback": _mpv!.FrameBackStep(); break;
            case "practice": OpenSubtitlePractice(); break;
            case "abloop": ToggleAbLoopWithPracticeReset(); break;
            case "crop": OpenCrop(); break;
            case "settings": OpenSettings(); break;
        }
    }
}
