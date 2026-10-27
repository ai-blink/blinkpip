using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HybridPipPlayer;

/// <summary>
/// 화면 모서리에 잠깐 떠 있는 알림. 제목, 한 줄 설명, 썸네일(선택), 버튼 하나를 가진다.
/// 마우스를 올려 두는 동안은 사라지지 않고, 벗어나면 처음부터 다시 센다. 활성화되지 않는 창이다.
/// </summary>
public partial class ToastWindow : Window
{
    private const int Margin = 16; // XAML 의 바깥 여백(그림자 공간)과 같은 값
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _raiseTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Action _action;
    private readonly double _targetOpacity;
    private double _anchorRight, _anchorBottom;
    private bool _closing;

    /// <param name="opacity">창 전체의 불투명도(0.3~1). 낮출수록 뒤의 영상이 비쳐 보인다</param>
    public ToastWindow(string title, string detail, string? thumbnailPath, string actionText, Action action, TimeSpan duration, double opacity = 1.0)
    {
        InitializeComponent();
        _targetOpacity = Math.Clamp(opacity, 0.3, 1.0);
        TitleText.Text = title;
        DetailText.Text = detail;
        DetailText.ToolTip = detail;
        ActionBtn.Content = actionText;
        _action = action;
        LoadThumbnail(thumbnailPath);

        _timer = new DispatcherTimer { Interval = duration };
        _timer.Tick += (_, _) => Dismiss();
        // 다른 항상 위 창이 활성화되면 그 창이 위로 올라와 알림을 가린다. 떠 있는 동안 맨 앞을 다시 잡는다.
        _raiseTimer.Tick += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
        };
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => { if (!_closing) { _timer.Stop(); _timer.Start(); } };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        };
        Loaded += (_, _) => SlideIn();
    }

    /// <summary>알림이 붙을 오른쪽 아래 모서리(화면 작업 영역, DIP)</summary>
    public void Anchor(double right, double bottom) { _anchorRight = right; _anchorBottom = bottom; }

    private void LoadThumbnail(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { ThumbBox.Visibility = Visibility.Collapsed; return; }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = 256;
            bmp.CacheOption = BitmapCacheOption.OnLoad; // 파일을 바로 놓아 준다(삭제·이동을 막지 않게)
            bmp.EndInit();
            bmp.Freeze();
            Thumb.Source = bmp;
        }
        catch (Exception) { ThumbBox.Visibility = Visibility.Collapsed; }
    }

    private void SlideIn()
    {
        double top = _anchorBottom - ActualHeight;
        Left = _anchorRight - ActualWidth;
        Top = top;
        BeginAnimation(TopProperty, new DoubleAnimation(top + 24, top, TimeSpan.FromMilliseconds(220)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, _targetOpacity, TimeSpan.FromMilliseconds(220)));
        _timer.Start();
        _raiseTimer.Start();
    }

    /// <summary>immediate 이면 애니메이션 없이 바로 닫는다(새 알림이 이 알림을 대신할 때)</summary>
    public void Dismiss(bool immediate = false)
    {
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        _raiseTimer.Stop();
        if (immediate) { Close(); return; }
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        _action();
        Dismiss();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Dismiss();
}
