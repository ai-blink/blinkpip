using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
// WinForms 와 이름이 겹치는 타입은 WPF 쪽으로 고정
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;

namespace blinkpip;

/// <summary>
/// PIP 왼쪽 위에 뜨는 작은 버튼 줄: 자물쇠 + PIP 나가기 + 자막 읽어 주기. mpv/WebView2 는 자식 HWND 라 그 위에 WPF 요소를 겹칠 수 없으므로(Airspace)
/// 별도 최상위 창으로 만든다. 활성화되지 않는 도구 창이라 포커스를 빼앗지 않는다.
/// 위치·표시/숨김은 MainWindow 가 정한다.
/// </summary>
public sealed class LockOverlay : Window
{
    /// <summary>버튼 한 칸 크기와 칸 사이 간격(DIP). MainWindow.PositionOverlay 가 창 크기를 잡을 때 쓴다</summary>
    public const int ButtonSize = 32, ButtonGap = 4;
    public const int TotalWidth = ButtonSize * 3 + ButtonGap * 2;

    private readonly TextBlock _icon;
    private readonly Border _border;
    private readonly Border _speech;
    private readonly TextBlock _speechIcon;

    /// <summary>자막 읽어 주기 버튼을 눌렀을 때</summary>
    public event Action? SpeechClicked;

    /// <summary>자물쇠 버튼을 눌렀을 때</summary>
    public event Action? Clicked;

    /// <summary>PIP 나가기 버튼을 눌렀을 때</summary>
    public event Action? ExitClicked;

    public LockOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        Width = TotalWidth;
        Height = ButtonSize;

        _icon = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 16,
            Foreground = Brushes.White,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        _border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x1E, 0x1E, 0x1E)),
            CornerRadius = new CornerRadius(8),
            Cursor = Cursors.Hand,
            Child = _icon,
        };
        _border.MouseLeftButtonUp += (_, _) => Clicked?.Invoke();
        _border.Width = ButtonSize;

        // 창으로 돌아가기(BackToWindow, U+E73F): 윈도우 11 Segoe Fluent Icons, 10 은 Segoe MDL2 Assets 에 같은 글자가 있다
        var exit = new Border
        {
            Width = ButtonSize,
            Margin = new Thickness(ButtonGap, 0, 0, 0),
            Background = _border.Background,
            CornerRadius = _border.CornerRadius,
            Cursor = Cursors.Hand,
            ToolTip = "PIP 나가기 (Ctrl+Alt+P · 영상 우클릭)",
            Child = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 15,
                Foreground = Brushes.White,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            },
        };
        exit.MouseLeftButtonUp += (_, _) => ExitClicked?.Invoke();

        // 자막 읽어 주기 켜기/끄기: 소리(Volume, U+E767) / 음소거(Mute, U+E74F)
        _speechIcon = new TextBlock
        {
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 15,
            Foreground = Brushes.White,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        _speech = new Border
        {
            Width = ButtonSize,
            Margin = new Thickness(ButtonGap, 0, 0, 0),
            Background = _border.Background,
            CornerRadius = _border.CornerRadius,
            Cursor = Cursors.Hand,
            Child = _speechIcon,
        };
        _speech.MouseLeftButtonUp += (_, _) => SpeechClicked?.Invoke();

        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        row.Children.Add(_border);
        row.Children.Add(exit);
        row.Children.Add(_speech);
        SetSpeech(false);
        Content = row;
        SetLocked(false);

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            int ex = NativeMethods.GetWindowLong(h, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(h, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        };
    }

    public IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    public void SetLocked(bool locked)
    {
        _icon.Text = locked ? "🔒" : "🔓";
        _border.ToolTip = locked ? "잠금 해제" : "잠그기 (이동·크기 조절·마우스 UI 차단)";
    }

    public void SetSpeech(bool on)
    {
        _speechIcon.Text = on ? "" : "";
        _speechIcon.Opacity = on ? 1.0 : 0.6;
        _speech.ToolTip = on ? "자막 읽어 주기 끄기" : "자막 읽어 주기 켜기";
    }
}
