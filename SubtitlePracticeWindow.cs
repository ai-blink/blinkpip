using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace blinkpip;

/// <summary>Engine A only. Subtitle tracks are selected, not translated by an online service.</summary>
public sealed class SubtitlePracticeWindow : Window
{
    private readonly TextBlock _current = new() { TextWrapping = TextWrapping.Wrap, FontSize = 18, Margin = new(0, 10, 0, 10) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
    private readonly CheckBox _auto = new() { Content = "문장 끝에서 멈춤", Margin = new(0, 0, 14, 8) };
    private readonly CheckBox _repeat = new() { Content = "문장 반복 후 멈춤", Margin = new(0, 0, 8, 8) };
    private readonly ComboBox _passes = new() { Width = 64, Margin = new(0, 0, 0, 8) };
    private readonly ComboBox _secondary = new() { MinWidth = 260, Margin = new(8, 0, 0, 8) };
    private readonly ListBox _lines = new() { DisplayMemberPath = nameof(SubtitleCue.Label), MinHeight = 96, MaxHeight = 240, Margin = new(0, 8, 0, 8) };
    private readonly Button _replayCurrent = new() { Content = "현재 / 마지막 문장 다시 듣기", Focusable = true, Margin = new(0, 0, 8, 0), Padding = new(12, 6, 12, 6) };
    private readonly Button _replaySelected = new() { Content = "선택 문장 다시 듣기", Focusable = true, Padding = new(12, 6, 12, 6) };
    private bool _updating, _canReplay;
    private string _linesIdentity = "", _trackIdentity = "";
    private SubtitleCue? _cue;
    public event Action<bool, bool, int>? OptionsChanged;
    public event Action<SubtitleCue>? ReplayRequested;
    public event Action<int?>? SecondaryChanged;
    private sealed record SubtitleChoice(int? Id, string Label);

    public SubtitlePracticeWindow(SubtitlePracticeSettings settings)
    {
        Title = "자막 연습 · 엔진 A"; Width = 760; Height = 600; MinWidth = 600; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Application.Current.TryFindResource("BarBrush") as Brush ?? Brushes.White;
        Foreground = System.Windows.Application.Current.TryFindResource("TextBrush") as Brush ?? Brushes.Black;
        _auto.Foreground = _repeat.Foreground = Foreground;
        _lines.Background = System.Windows.Application.Current.TryFindResource("FieldBrush") as Brush ?? Brushes.White;
        _lines.Foreground = Foreground;
        _passes.Foreground = _secondary.Foreground = Brushes.Black;
        var root = new Grid { Margin = new(20), Height = 500 };
        for (int i = 0; i < 6; i++) root.RowDefinitions.Add(new() { Height = i == 4 ? new(1, GridUnitType.Star) : GridLength.Auto });
        var title = new TextBlock { Text = "자막 연습", FontSize = 22, FontWeight = FontWeights.SemiBold };
        root.Children.Add(title);
        var options = new WrapPanel { Margin = new(0, 14, 0, 0) };
        options.Children.Add(_auto); options.Children.Add(_repeat); options.Children.Add(_passes);
        options.Children.Add(new TextBlock { Text = "회 (첫 재생 포함)", Margin = new(5, 3, 0, 0) });
        Grid.SetRow(options, 1); root.Children.Add(options);
        _passes.ItemsSource = Enumerable.Range(1, 5).ToArray();
        var currentPanel = new StackPanel(); currentPanel.Children.Add(new ScrollViewer { Content = _current, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); currentPanel.Children.Add(_status);
        Grid.SetRow(currentPanel, 2); root.Children.Add(currentPanel);
        var tracks = new WrapPanel(); tracks.Children.Add(new TextBlock { Text = "두 번째 자막", Margin = new(0, 4, 0, 0) }); tracks.Children.Add(_secondary);
        Grid.SetRow(tracks, 3); root.Children.Add(tracks);
        _secondary.DisplayMemberPath = nameof(SubtitleChoice.Label);
        Grid.SetRow(_lines, 4); root.Children.Add(_lines);
        var footer = new StackPanel();
        footer.Children.Add(new TextBlock { Text = "목록은 현재 메모리에 읽힌 텍스트 자막입니다. 방송·웹 영상에서는 일부만 보일 수 있습니다.\n두 번째 자막은 기존 트랙을 표시합니다. OTT 보호 영상 자막은 지원하지 않습니다.\n문장을 선택한 뒤 Enter·두 번 클릭 또는 다시 듣기 버튼으로 이동합니다.", FontSize = 12, LineHeight = 17, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) });
        var actions = new WrapPanel(); actions.Children.Add(_replayCurrent); actions.Children.Add(_replaySelected); footer.Children.Add(actions);
        Grid.SetRow(footer, 5); root.Children.Add(footer);
        var outer = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        // Keep the star row finite even though a ScrollViewer measures its child with infinite height.
        // Only genuinely small viewports scroll the whole panel; long cue lists scroll within the ListBox.
        outer.SizeChanged += (_, e) => root.Height = Math.Max(460, e.NewSize.Height - root.Margin.Top - root.Margin.Bottom);
        Content = outer;
        System.Windows.Automation.AutomationProperties.SetAutomationId(_current, "PracticeCurrentCue");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_lines, "PracticeSubtitleLines");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_auto, "PracticeAutoPause");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_repeat, "PracticeRepeat");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_passes, "PracticePasses");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_secondary, "PracticeSecondaryTrack");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_replayCurrent, "PracticeReplayCurrent");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_replaySelected, "PracticeReplaySelected");
        _updating = true; _auto.IsChecked = settings.AutoPause; _repeat.IsChecked = settings.Repeat; _passes.SelectedItem = settings.Passes; _updating = false;
        _auto.Checked += ChangeOptions; _auto.Unchecked += ChangeOptions;
        _repeat.Checked += ChangeOptions; _repeat.Unchecked += ChangeOptions;
        _passes.SelectionChanged += ChangeOptions;
        _secondary.SelectionChanged += (_, _) => { if (!_updating && _secondary.SelectedItem is SubtitleChoice choice) SecondaryChanged?.Invoke(choice.Id); };
        _replayCurrent.Click += (_, _) => { if (_canReplay && _cue?.CanPractice == true) ReplayRequested?.Invoke(_cue); };
        _replaySelected.Click += (_, _) => ReplaySelected();
        _lines.SelectionChanged += (_, _) => UpdateReplayButtons();
        _lines.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && ReplaySelected()) e.Handled = true;
        };
        _lines.MouseDoubleClick += (_, e) =>
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left &&
                e.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(_lines, source) is ListBoxItem && ReplaySelected()) e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); }
        };
        UpdateReplayButtons();
    }

    private bool ReplaySelected()
    {
        if (!_canReplay || !_lines.IsEnabled || _lines.SelectedItem is not SubtitleCue cue || !cue.CanPractice) return false;
        ReplayRequested?.Invoke(cue);
        return true;
    }

    private void UpdateReplayButtons()
    {
        _replayCurrent.IsEnabled = _canReplay && _cue?.CanPractice == true;
        _replaySelected.IsEnabled = _canReplay && _lines.SelectedItem is SubtitleCue selected && selected.CanPractice;
    }

    private void ChangeOptions(object sender, RoutedEventArgs e)
    {
        if (!_updating) OptionsChanged?.Invoke(_auto.IsChecked == true, _repeat.IsChecked == true, _passes.SelectedItem is int count ? count : 1);
    }

    public void UpdateSample(SubtitlePlaybackSample? sample, bool engineA)
    {
        _cue = engineA && sample != null ? sample.Cue ?? _cue : null;
        _current.Text = sample?.Cue?.Text ?? (engineA ? "현재 표시되는 텍스트 자막이 없습니다." : "자막 연습은 엔진 A에서만 사용할 수 있습니다.");
        _status.Text = engineA && sample != null ? $"재생 위치 {SubtitleTime.Format(sample.Position)} · 자막 지연 {sample.Delay:+0.###;-0.###;0}초" : "OTT 보호 영상 시작 감지·자막 읽기: 확인 필요";
        _auto.IsEnabled = _repeat.IsEnabled = _passes.IsEnabled = _secondary.IsEnabled = _lines.IsEnabled = engineA;
        bool timing = sample?.TimingSupported != false;
        if (!timing) _status.Text = "자막 속도/프레임 변환이 설정되어 있습니다. 문장 연습은 sub-speed=1, sub-fps=0에서 지원합니다.";
        _auto.IsEnabled = _repeat.IsEnabled = _passes.IsEnabled = engineA && timing;
        _canReplay = engineA && timing;
        UpdateReplayButtons();
    }

    public void UpdateLines(IReadOnlyList<SubtitleCue> lines)
    {
        string identity = string.Join("\n", lines.Select(c => c.Label));
        if (identity == _linesIdentity) return;
        _linesIdentity = identity;
        var previous = _lines.SelectedItem as SubtitleCue;
        _lines.ItemsSource = lines;
        if (previous != null) _lines.SelectedItem = lines.FirstOrDefault(c => c.Start == previous.Start && c.Text == previous.Text);
    }

    public void ResetContext()
    {
        _updating = true;
        try { _secondary.IsDropDownOpen = false; _secondary.ItemsSource = null; _secondary.SelectedItem = null; }
        finally { _updating = false; }
        _trackIdentity = ""; _cue = null; _canReplay = false; UpdateReplayButtons();
    }

    public bool MatchesTrackContext(IReadOnlyList<MpvPlayer.TrackInfo> tracks, string? primary) =>
        _trackIdentity == SubtitleSelectionGuard.TrackKey(primary, tracks.Select(t => (t.Id, t.Label)));

    public void UpdateTracks(IReadOnlyList<MpvPlayer.TrackInfo> tracks, string? primary, string? secondary)
    {
        string identity = SubtitleSelectionGuard.TrackKey(primary, tracks.Select(t => (t.Id, t.Label)));
        if (_secondary.IsDropDownOpen && identity == _trackIdentity) return;
        _updating = true;
        try
        {
            if (identity != _trackIdentity) _secondary.IsDropDownOpen = false;
            _trackIdentity = identity;
            var choices = new List<SubtitleChoice> { new(null, "표시 안 함") };
            choices.AddRange(tracks.Where(t => t.Id.ToString() != primary).Select(t => new SubtitleChoice(t.Id, t.Label)));
            _secondary.ItemsSource = choices;
            _secondary.SelectedItem = choices.FirstOrDefault(c => c.Id?.ToString() == secondary) ?? choices[0];
        }
        finally { _updating = false; }
    }
}
