using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RadioButton = System.Windows.Controls.RadioButton;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace HybridPipPlayer;

/// <summary>왼쪽 사이드바 + 오른쪽 내용 형식의 설정 창. 값을 바꾸면 바로 저장하고 SettingChanged 로 알린다(적용은 MainWindow 가 한다).</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _s;
    private readonly ObservableCollection<Bookmark> _bookmarks;
    private readonly Func<List<(string Name, string Description)>> _getDevices;
    private bool _loading = true; // 컨트롤 초기값을 채우는 동안 ValueChanged 등이 설정을 덮어쓰지 않게

    /// <summary>바뀐 설정 이름(AppSettings 속성 이름, 언어 두 칸은 "Langs")</summary>
    public event Action<string>? SettingChanged;

    /// <summary>"미리 보기" 버튼. 지금 설정으로 알림을 한 번 띄워 달라는 요청</summary>
    public event Action? ToastPreviewRequested;

    private static readonly double[] Speeds = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };

    public SettingsWindow(AppSettings settings, ObservableCollection<Bookmark> bookmarks,
                          Func<List<(string Name, string Description)>> getDevices, DiagInfo? diagnostics = null)
    {
        InitializeComponent();
        Nav_SelectionChanged(Nav, null!);
        _s = settings;
        _bookmarks = bookmarks;
        _getDevices = getDevices;

        // 테마 후보 라디오 (ThemeManager 의 팔레트에서 만든다)
        foreach (var (key, pal) in ThemeManager.Palettes)
        {
            var rb = new RadioButton { Content = pal.Label, GroupName = "theme", Tag = key, Style = (Style)FindResource("SegmentRadio"), Margin = new Thickness(0, 0, 0, 6), HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left };
            rb.Checked += Theme_Checked;
            ThemePanel.Children.Add(rb);
        }

        PosBottom.IsChecked = _s.ControlBarPosition != "top";
        PosTop.IsChecked = _s.ControlBarPosition == "top";
        VolSlider.Value = _s.DefaultVolume;
        FsHideSlider.Value = _s.FullscreenHideSec;
        SeekSecSlider.Value = _s.SeekSeconds;
        PipWidthSlider.Value = _s.PipWidth;
        PipHideSlider.Value = _s.PipHideSec;
        SubSizeSlider.Value = _s.SubFontSize;
        ToastOpacitySlider.Value = Math.Round(_s.ToastOpacity * 100);
        ToastSecondsSlider.Value = Math.Round(_s.ToastSeconds);
        BookmarksToggle.IsChecked = _s.ShowBookmarks;
        ResumeToggle.IsChecked = _s.ResumePlayback;
        AutoPlaylistToggle.IsChecked = _s.AutoPlaylist;
        UpdateAssocLabel();
        BuildAdjustRows();
        SubLangBox.Text = _s.SubLangs;
        AudioLangBox.Text = _s.AudioLangs;
        foreach (RadioButton rb in ThemePanel.Children) rb.IsChecked = (string)rb.Tag == _s.Theme;
        UpdateValueTexts();
        UpdateSpeedLabel();
        UpdateDeviceLabel();
        BookmarkList.ItemsSource = _bookmarks;

        var v = typeof(SettingsWindow).Assembly.GetName().Version;
        InfoText.Text = $"버전 {v?.ToString(3) ?? "?"} · 설정 파일: %LOCALAPPDATA%\\HybridPipPlayer\\settings.json";
        InitDiagnostics(diagnostics);
        RefreshHandoffRegistration();
        _loading = false;
    }

    private void Changed(string name)
    {
        _s.Save();
        SettingChanged?.Invoke(name);
    }

    private void UpdateValueTexts()
    {
        VolText.Text = $"{VolSlider.Value:0}";
        FsHideText.Text = $"{FsHideSlider.Value:0.#}초";
        SeekSecText.Text = $"{SeekSecSlider.Value:0}초";
        PipWidthText.Text = $"{PipWidthSlider.Value:0}";
        PipHideText.Text = $"{PipHideSlider.Value:0.#}초";
        SubSizeText.Text = $"{SubSizeSlider.Value:0}";
        ToastOpacityText.Text = $"{ToastOpacitySlider.Value:0}%";
        ToastSecondsText.Text = $"{ToastSecondsSlider.Value:0}초";
    }

    private void ToastOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return; // XAML 파싱 중 Minimum 보정으로 먼저 호출될 수 있다
        UpdateValueTexts();
        _s.ToastOpacity = ToastOpacitySlider.Value / 100;
        Changed(nameof(AppSettings.ToastOpacity));
    }

    private void ToastSeconds_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        UpdateValueTexts();
        _s.ToastSeconds = ToastSecondsSlider.Value;
        Changed(nameof(AppSettings.ToastSeconds));
    }

    private void ToastPreview_Click(object sender, RoutedEventArgs e) => ToastPreviewRequested?.Invoke();

    private void UpdateSpeedLabel() => SpeedPick.Content = $"{_s.DefaultSpeed:0.##}×  ▾";

    private void UpdateDeviceLabel()
    {
        var devs = _getDevices();
        string label = devs.FirstOrDefault(d => d.Name == _s.AudioDevice).Description ?? (_s.AudioDevice == "auto" ? "자동 선택" : _s.AudioDevice);
        DevicePick.Content = label + "  ▾";
    }

    // ───────── 사이드바 / 제목 줄 ─────────

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Pane4 == null || PaneAdjust == null) return; // 첫 항목 선택이 XAML 파싱 중에 먼저 올 수 있다
        var panes = new UIElement[] { Pane0, Pane1, Pane2, Pane3, PaneAdjust, Pane4 };
        for (int i = 0; i < panes.Length; i++) panes[i].Visibility = i == Nav.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    // ───────── 재생 ─────────

    private void Pos_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.ControlBarPosition = PosTop.IsChecked == true ? "top" : "bottom";
        Changed(nameof(AppSettings.ControlBarPosition));
    }

    private void Vol_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return; // XAML 파싱 중 Minimum 보정으로 먼저 호출될 수 있다(아직 없는 요소를 건드리지 않게)
        UpdateValueTexts();
        _s.DefaultVolume = (int)Math.Round(VolSlider.Value);
        Changed(nameof(AppSettings.DefaultVolume));
    }

    private void FsHide_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return; // XAML 파싱 중 Minimum 보정으로 먼저 호출될 수 있다(아직 없는 요소를 건드리지 않게)
        UpdateValueTexts();
        _s.FullscreenHideSec = FsHideSlider.Value;
        Changed(nameof(AppSettings.FullscreenHideSec));
    }

    // 재생 줄 팝업에서 간격을 바꿨을 때 슬라이더를 맞춘다
    public void SyncSeekSeconds()
    {
        _loading = true;
        SeekSecSlider.Value = _s.SeekSeconds;
        UpdateValueTexts();
        _loading = false;
    }

    private void SeekSec_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return; // XAML 파싱 중 Minimum 보정으로 먼저 호출될 수 있다(아직 없는 요소를 건드리지 않게)
        UpdateValueTexts();
        _s.SeekSeconds = (int)Math.Round(SeekSecSlider.Value);
        Changed(nameof(AppSettings.SeekSeconds));
    }

    private void SpeedPick_Click(object sender, RoutedEventArgs e) =>
        ShowMenu(SpeedPick, Speeds.Select(v => ($"{v:0.##}×", Math.Abs(v - _s.DefaultSpeed) < 0.001, (Action)(() =>
        {
            _s.DefaultSpeed = v;
            UpdateSpeedLabel();
            Changed(nameof(AppSettings.DefaultSpeed));
        }))));

    private void Resume_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.ResumePlayback = ResumeToggle.IsChecked == true;
        Changed(nameof(AppSettings.ResumePlayback));
    }

    private void AutoPlaylist_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.AutoPlaylist = AutoPlaylistToggle.IsChecked == true;
        Changed(nameof(AppSettings.AutoPlaylist));
    }

    private void ClearResume_Click(object sender, RoutedEventArgs e) => ResumeStore.Clear();

    // ───────── 모양 ─────────

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Theme = (string)((RadioButton)sender).Tag;
        Changed(nameof(AppSettings.Theme));
    }

    private void BookmarksToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.ShowBookmarks = BookmarksToggle.IsChecked == true;
        Changed(nameof(AppSettings.ShowBookmarks));
    }

    // ───────── PIP ─────────

    private void PipWidth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return; // XAML 파싱 중 Minimum 보정으로 먼저 호출될 수 있다(아직 없는 요소를 건드리지 않게)
        UpdateValueTexts();
        _s.PipWidth = (int)Math.Round(PipWidthSlider.Value);
        Changed(nameof(AppSettings.PipWidth));
    }

    private void PipHide_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return; // XAML 파싱 중 Minimum 보정으로 먼저 호출될 수 있다(아직 없는 요소를 건드리지 않게)
        UpdateValueTexts();
        _s.PipHideSec = PipHideSlider.Value;
        Changed(nameof(AppSettings.PipHideSec));
    }

    // ───────── 오디오 · 자막 ─────────

    private void DevicePick_Click(object sender, RoutedEventArgs e)
    {
        var devs = _getDevices();
        if (devs.Count == 0) devs.Add(("auto", "자동 선택"));
        ShowMenu(DevicePick, devs.Select(d => (d.Description, d.Name == _s.AudioDevice, (Action)(() =>
        {
            _s.AudioDevice = d.Name;
            UpdateDeviceLabel();
            Changed(nameof(AppSettings.AudioDevice));
        }))));
    }

    private void SubSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return; // XAML 파싱 중 Minimum 보정으로 먼저 호출될 수 있다(아직 없는 요소를 건드리지 않게)
        UpdateValueTexts();
        _s.SubFontSize = (int)Math.Round(SubSizeSlider.Value);
        Changed(nameof(AppSettings.SubFontSize));
    }

    private void Langs_LostFocus(object sender, RoutedEventArgs e) => CommitLangs();

    private void Langs_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitLangs(); Keyboard.ClearFocus(); }
    }

    private void CommitLangs()
    {
        if (_loading) return;
        string sub = _s.SubLangs, audio = _s.AudioLangs;
        _s.SubLangs = SubLangBox.Text;
        _s.AudioLangs = AudioLangBox.Text;
        _s.Clamp(); // 영문자·쉼표·하이픈만 남긴다
        SubLangBox.Text = _s.SubLangs;
        AudioLangBox.Text = _s.AudioLangs;
        if (sub != _s.SubLangs || audio != _s.AudioLangs) Changed("Langs");
    }

    // ───────── 영상 · 소리 보정 ─────────

    private readonly List<(Slider Slider, TextBlock Text, Action<int> Set, Func<int> Get, string Unit)> _adjustRows = new();

    // 이름·슬라이더·값 텍스트 한 줄을 만들어 목록에 올린다
    private void AddAdjustRow(System.Windows.Controls.Panel parent, string label, int min, int max, Func<int> get, Action<int> set, string unit)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = (System.Windows.Media.Brush)FindResource("SubTextBrush") };
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = 1, IsSnapToTickEnabled = true, Style = (Style)FindResource("PlayerSlider"), VerticalAlignment = VerticalAlignment.Center };
        var value = new TextBlock { Style = (Style)FindResource("ValueText"), MinWidth = 0, Margin = new Thickness(0) };
        Grid.SetColumn(slider, 1); Grid.SetColumn(value, 2);
        grid.Children.Add(name); grid.Children.Add(slider); grid.Children.Add(value);
        parent.Children.Add(grid);
        slider.Value = get();
        value.Text = $"{get():+0;-0;0}{unit}";
        slider.ValueChanged += (_, _) =>
        {
            int v = (int)Math.Round(slider.Value);
            value.Text = $"{v:+0;-0;0}{unit}";
            if (_loading) return;
            set(v);
            Changed("Adjust");
        };
        _adjustRows.Add((slider, value, set, get, unit));
    }

    private void BuildAdjustRows()
    {
        string[] vNames = { "밝기", "대비", "채도", "감마" };
        for (int i = 0; i < 4; i++) { int k = i; AddAdjustRow(VideoRows, vNames[i], -100, 100, () => _s.VideoAdjust[k], v => _s.VideoAdjust[k] = v, ""); }
        string[] eNames = { "60 Hz", "230 Hz", "910 Hz", "3.6 kHz", "14 kHz" };
        for (int i = 0; i < 5; i++) { int k = i; AddAdjustRow(EqRows, eNames[i], -12, 12, () => _s.EqBands[k], v => _s.EqBands[k] = v, " dB"); }
    }

    private void ResetAdjust_Click(object sender, RoutedEventArgs e)
    {
        Array.Clear(_s.VideoAdjust); Array.Clear(_s.EqBands);
        _loading = true;
        foreach (var r in _adjustRows) r.Slider.Value = r.Get();
        _loading = false;
        Changed("Adjust");
    }

    // ───────── 파일 연결 ─────────

    private void UpdateAssocLabel()
    {
        bool on = FileAssociation.IsRegistered();
        AssocBtn.Content = on ? "연결 프로그램에서 빼기" : "연결 프로그램에 등록";
        AssocStatus.Text = on ? "등록됨: 영상·음악 파일 우클릭 → 연결 프로그램에 'Hybrid PIP Player' 가 나옵니다."
                              : "등록 안 됨. 현재 사용자 계정(HKCU)에만 쓰이고, 언제든 뺄 수 있습니다.";
    }

    private void Assoc_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (FileAssociation.IsRegistered()) FileAssociation.Unregister(); else FileAssociation.Register();
        }
        catch (Exception ex) { AssocStatus.Text = "실패: " + ex.Message; return; }
        UpdateAssocLabel();
    }

    private void OpenDefaultApps_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }); }
        catch (Exception ex) { AssocStatus.Text = "설정을 열지 못했습니다: " + ex.Message; }
    }

    // ───────── 북마크 ─────────

    private Bookmark? Tagged(object sender) => (sender as Button)?.Tag as Bookmark;

    private void BmUp_Click(object sender, RoutedEventArgs e)
    {
        if (Tagged(sender) is not { } b) return;
        int i = _bookmarks.IndexOf(b);
        if (i > 0) { _bookmarks.Move(i, i - 1); BookmarkStore.Save(_bookmarks); }
    }

    private void BmDown_Click(object sender, RoutedEventArgs e)
    {
        if (Tagged(sender) is not { } b) return;
        int i = _bookmarks.IndexOf(b);
        if (i >= 0 && i < _bookmarks.Count - 1) { _bookmarks.Move(i, i + 1); BookmarkStore.Save(_bookmarks); }
    }

    private void BmDelete_Click(object sender, RoutedEventArgs e)
    {
        if (Tagged(sender) is not { } b) return;
        _bookmarks.Remove(b);
        BookmarkStore.Save(_bookmarks);
    }

    // ───────── 메뉴 도우미 ─────────

    private static void ShowMenu(Button anchor, IEnumerable<(string Header, bool Checked, Action Act)> items)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (header, isChecked, act) in items)
        {
            var mi = new MenuItem { Header = header, IsChecked = isChecked };
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
        }
        if (menu.Items.Count > 0) menu.IsOpen = true;
    }
}
