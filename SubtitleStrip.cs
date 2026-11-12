using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
// WinForms 와 이름이 겹치는 타입은 WPF 쪽으로 고정
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;

namespace HybridPipPlayer;

/// <summary>
/// PIP 창 바로 아래(공간이 없으면 위)에 자막을 보여 주는 줄. 영상은 자식 HWND 라 WPF 를 겹칠 수 없어(Airspace)
/// 자물쇠 버튼처럼 별도 최상위 창으로 만든다. 활성화되지 않고 마우스도 통과시킨다(아래 창을 가리지 않게).
/// 글자는 MainWindow 가 mpv(sub-text) 또는 웹뷰(디즈니+ 자막 요소)에서 읽어 넘긴다. 위치·표시는 MainWindow 가 정한다.
/// </summary>
public sealed class SubtitleStrip : Window
{
    private readonly TextBlock _text;
    private readonly Border _box;

    public SubtitleStrip()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        IsHitTestVisible = false;

        _text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(10, 3, 10, 4),
        };
        _box = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x10, 0x10, 0x10)),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Child = _text,
            Visibility = Visibility.Collapsed,
        };
        Content = _box;

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            int ex = NativeMethods.GetWindowLong(h, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(h, NativeMethods.GWL_EXSTYLE,
                ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT);
        };
    }

    public IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    public string Text => _text.Text;

    public void SetText(string? text)
    {
        text = (text ?? "").Replace("\r", "").Trim();
        _text.Text = text;
        _box.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <param name="font">글꼴 이름(비면 맑은 고딕)</param>
    /// <param name="color">#RRGGBB</param>
    /// <param name="size">글자 크기(DIP)</param>
    public void SetStyle(string font, string color, double size)
    {
        _text.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(font) ? "Malgun Gothic" : font + ", Malgun Gothic");
        _text.FontSize = size;
        try { _text.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        catch (FormatException) { _text.Foreground = Brushes.White; }
    }

    /// <summary>두 줄을 담을 높이(DIP). 창 아래 공간을 이만큼 확보한다</summary>
    public static double ReserveHeight(double size) => Math.Ceiling(size * 1.35 * 2 + 7);
}
