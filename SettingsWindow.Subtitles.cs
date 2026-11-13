using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace blinkpip;

// 설정 창의 자막 모양(글꼴·색)과 PIP 아래 자막 줄 항목
public partial class SettingsWindow
{
    // 한글이 들어가는 글꼴 위주로, 이 PC 에 설치된 것만 목록에 보인다
    private static readonly string[] FontCandidates =
    {
        "Malgun Gothic", "NanumGothic", "NanumBarunGothic", "NanumSquare", "Noto Sans KR", "Pretendard",
        "Gulim", "Dotum", "Batang", "Gungsuh", "Segoe UI", "Arial", "Verdana", "Tahoma",
    };

    private static readonly (string Name, string Hex)[] ColorCandidates =
    {
        ("흰색", "#FFFFFF"), ("노란색", "#FFE14D"), ("하늘색", "#8FD3FF"), ("연두색", "#B8F28A"), ("분홍색", "#FFB3D1"), ("회색", "#C8C8C8"),
    };

    // 생성자에서 _loading 이 켜져 있을 때 부른다
    private void InitSubtitleStyle()
    {
        PipSubBelowToggle.IsChecked = _s.PipSubBelow;
        PipSubSizeSlider.Value = _s.PipSubSize;
        PipSubSizeText.Text = $"{_s.PipSubSize}";
        SubTtsToggle.IsChecked = _s.SubTts;
        SubTtsRateSlider.Value = _s.SubTtsRate;
        SubTtsRateText.Text = $"{_s.SubTtsRate:0.0}×";
        SubTtsPitchSlider.Value = _s.SubTtsPitch;
        SubTtsPitchText.Text = $"{_s.SubTtsPitch:0.00}";
        SubTtsVolumeSlider.Value = _s.SubTtsVolume;
        SubTtsVolumeText.Text = $"{_s.SubTtsVolume * 100:0}%";
        UpdateSubTtsModel();
        HookSubTtsModel();
        UpdateSubStyleLabels();
    }

    /// <summary>PIP 의 목소리 버튼으로 켜고 끈 것을 토글에 반영한다</summary>
    public void SyncSubTts()
    {
        _loading = true;
        SubTtsToggle.IsChecked = _s.SubTts;
        _loading = false;
    }

    private void UpdateSubTtsModel()
    {
        bool ready = SubtitleSpeaker.ModelReady;
        if (!SubtitleSpeaker.ModelDownloading)
            SubTtsModelText.Text = ready ? "받아 둠 — 여자·남자 목소리를 쓸 수 있습니다." : "없음 — 자막 읽기를 켜면 자동으로 받습니다(Supertonic 3, OpenRAIL-M 라이선스).";
        SubTtsModelBtn.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        SubTtsModelBtn.IsEnabled = !SubtitleSpeaker.ModelDownloading;
    }

    // 자동 받기(MainWindow.EnsureTtsModel)와 같은 작업의 진행 소식을 받는다. 창이 닫히면 끊는다
    private void HookSubTtsModel()
    {
        Action<string> onStatus = m => { SubTtsModelText.Text = m; UpdateSubTtsModel(); };
        SubtitleSpeaker.ModelStatus += onStatus;
        Closed += (_, _) => SubtitleSpeaker.ModelStatus -= onStatus;
    }

    private async void SubTtsModelBtn_Click(object sender, RoutedEventArgs e)
    {
        var job = SubtitleSpeaker.EnsureModelAsync();
        UpdateSubTtsModel();
        try { await job; } catch (Exception) { /* 실패 문구는 ModelStatus 로 온다 */ }
        UpdateSubTtsModel();
    }

    private void SubTtsPitch_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _s.SubTtsPitch = Math.Round(SubTtsPitchSlider.Value, 2);
        SubTtsPitchText.Text = $"{_s.SubTtsPitch:0.00}";
        Changed(nameof(AppSettings.SubTtsPitch));
    }

    private void SubTtsVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _s.SubTtsVolume = Math.Round(SubTtsVolumeSlider.Value, 2);
        SubTtsVolumeText.Text = $"{_s.SubTtsVolume * 100:0}%";
        Changed(nameof(AppSettings.SubTtsVolume));
    }

    private void SubTts_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.SubTts = SubTtsToggle.IsChecked == true;
        Changed(nameof(AppSettings.SubTts));
    }

    private void SubTtsRate_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _s.SubTtsRate = Math.Round(SubTtsRateSlider.Value, 1);
        SubTtsRateText.Text = $"{_s.SubTtsRate:0.0}×";
        Changed(nameof(AppSettings.SubTtsRate));
    }

    private void SubTtsVoicePick_Click(object sender, RoutedEventArgs e)
    {
        var names = SubtitleSpeaker.SupertonicVoices.Select(v => v.Id).Append("").Concat(SubtitleSpeaker.WindowsVoiceNames());
        ShowMenu(SubTtsVoicePick, names.Select(n => (SubtitleSpeaker.VoiceLabel(n), n == _s.SubTtsVoice, (Action)(() =>
        {
            _s.SubTtsVoice = n;
            UpdateSubStyleLabels();
            Changed(nameof(AppSettings.SubTtsVoice));
        }))));
    }

    private void UpdateSubStyleLabels()
    {
        SubFontPick.Content = (_s.SubFont.Length == 0 ? "기본" : _s.SubFont) + "  ▾";
        SubTtsVoicePick.Content = SubtitleSpeaker.VoiceLabel(_s.SubTtsVoice) + "  ▾";
        SubColorPick.Content = (ColorCandidates.FirstOrDefault(c => c.Hex.Equals(_s.SubColor, StringComparison.OrdinalIgnoreCase)).Name ?? _s.SubColor) + "  ▾";
    }

    /// <summary>메인 창에서 Ctrl+Alt+↑/↓ 로 바꾼 크기를 슬라이더에 반영한다</summary>
    public void SyncPipSubSize()
    {
        _loading = true;
        PipSubSizeSlider.Value = _s.PipSubSize;
        PipSubSizeText.Text = $"{_s.PipSubSize}";
        _loading = false;
    }

    private void PipSubBelow_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.PipSubBelow = PipSubBelowToggle.IsChecked == true;
        Changed(nameof(AppSettings.PipSubBelow));
    }

    private void PipSubSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return; // XAML 파싱 중 Minimum 보정으로 먼저 호출될 수 있다
        _s.PipSubSize = (int)Math.Round(PipSubSizeSlider.Value);
        PipSubSizeText.Text = $"{_s.PipSubSize}";
        Changed(nameof(AppSettings.PipSubSize));
    }

    private void SubFontPick_Click(object sender, RoutedEventArgs e)
    {
        var installed = Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = new[] { "" }.Concat(FontCandidates.Where(installed.Contains));
        if (_s.SubFont.Length > 0 && !names.Contains(_s.SubFont)) names = names.Append(_s.SubFont); // 직접 고친 값도 보이게
        ShowMenu(SubFontPick, names.Select(n => (n.Length == 0 ? "기본" : n, n == _s.SubFont, (Action)(() =>
        {
            _s.SubFont = n;
            UpdateSubStyleLabels();
            Changed(nameof(AppSettings.SubFont));
        }))));
    }

    private void SubColorPick_Click(object sender, RoutedEventArgs e)
    {
        ShowMenu(SubColorPick, ColorCandidates.Select(c => (c.Name, c.Hex.Equals(_s.SubColor, StringComparison.OrdinalIgnoreCase), (Action)(() =>
        {
            _s.SubColor = c.Hex;
            UpdateSubStyleLabels();
            Changed(nameof(AppSettings.SubColor));
        }))));
    }
}
