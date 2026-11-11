using System.Windows;
using System.Windows.Threading;

namespace HybridPipPlayer;

public partial class MainWindow
{
    private CropWindow? _cropWindow;
    private CropStore? _cropStore;

    // Moved from Playback so the discoverable menu can grow without growing that file.
    private void MoreBtn_Click(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        menu.Items.Add(Item("스크린샷", () => RunFeature("shot")));
        menu.Items.Add(Item("스크린샷 폴더 열기", () => RunFeature("shotdir")));
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(Item("한 프레임 앞", () => RunFeature("fstep")));
        menu.Items.Add(Item("한 프레임 뒤", () => RunFeature("fback")));
        menu.Items.Add(Item("구간 반복", () => RunFeature("abloop")));
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(Item("영역 · 화면 자르기…", () => RunFeature("crop")));
        ShowMenu((FrameworkElement)sender, menu);
    }

    private void OpenCrop()
    {
        if (_cropWindow != null) { _cropWindow.Activate(); return; }
        if (_mpv == null || !_engineA || !_mpv.HasCropVideo)
        { SetStatus("영역: 재생 중인 엔진 A 영상에서만 쓸 수 있습니다. OTT는 지원하지 않습니다."); return; }
        var current = _mpv.GetCrop();
        var win = new CropWindow(_cropStore ??= CropStore.LoadDefault(), current, ApplyCrop) { Owner = this };
        _cropWindow = win;
        var refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        refresh.Tick += (_, _) => win.SetAvailable(!_closing && _engineA && _mpv?.HasCropVideo == true);
        win.Closed += (_, _) => { refresh.Stop(); _cropWindow = null; };
        refresh.Start(); win.Show();
    }
    private bool ApplyCrop(CropValues values)
    {
        if (_closing || _mpv == null || !_engineA || !_mpv.HasCropVideo)
        { _cropWindow?.SetAvailable(false); return false; }
        var v = values.Normalize();
        if (_mpv.ApplyCrop(v)) { SetStatus($"영역: {v.Zoom:0.00}× 확대"); return true; }
        else
        {
            _cropWindow?.SynchronizeCrop(_mpv.GetCrop());
            SetStatus("영역 적용 실패: 영상 출력 상태를 확인하세요.");
            return false;
        }
    }
}
