using System;
using System.Windows;
using System.Windows.Interop;

namespace blinkpip;

public partial class PipControls : Window
{
    private bool _updating; // 코드가 슬라이더 값을 바꾸는 중 — 되돌아가 mpv 를 탐색하지 않게

    public event Action? PlayPauseClicked;
    public event Action<double>? SeekRequested;
    public event Action? MuteClicked;
    public event Action? FullscreenClicked;

    public PipControls()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            int ex = NativeMethods.GetWindowLong(h, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(h, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        };
    }

    public IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    /// <summary>슬라이더를 끄는 중이면 true — 이때는 재생 위치로 덮어쓰지 않는다</summary>
    public bool IsSeeking => Seek.IsMouseCaptureWithin;

    public void ShowFullscreenButton(bool show) => FullBtn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    public void SetFullscreenState(bool isFullscreen) => FullBtn.Content = isFullscreen ? "" : "";

    public void Update(bool paused, double pos, double dur, bool muted)
    {
        _updating = true;
        try
        {
            PlayBtn.Content = paused ? "" : "";
            if (dur > 0)
            {
                if (Math.Abs(Seek.Maximum - dur) > 0.5) Seek.Maximum = dur;
                if (!IsSeeking) Seek.Value = Math.Min(pos, dur);
            }
            else { Seek.Maximum = 1; Seek.Value = 0; }
            TimeText.Text = $"{FormatTime(pos)} / {FormatTime(dur)}";
            MuteBtn.Content = muted ? "" : "";
        }
        finally { _updating = false; }
    }

    public static string FormatTime(double sec)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, sec));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    private void PlayBtn_Click(object sender, RoutedEventArgs e) => PlayPauseClicked?.Invoke();
    private void MuteBtn_Click(object sender, RoutedEventArgs e) => MuteClicked?.Invoke();
    private void FullBtn_Click(object sender, RoutedEventArgs e) => FullscreenClicked?.Invoke();

    private void Seek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        SeekRequested?.Invoke(e.NewValue);
        TimeText.Text = $"{FormatTime(e.NewValue)} / {FormatTime(Seek.Maximum)}";
    }
}
