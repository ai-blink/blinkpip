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
        UpdateSubStyleLabels();
    }

    private void UpdateSubStyleLabels()
    {
        SubFontPick.Content = (_s.SubFont.Length == 0 ? "기본" : _s.SubFont) + "  ▾";
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
