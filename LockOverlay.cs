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

namespace HybridPipPlayer;

/// <summary>
/// PIP 왼쪽 위에 뜨는 작은 자물쇠 버튼. mpv/WebView2 는 자식 HWND 라 그 위에 WPF 요소를 겹칠 수 없으므로(Airspace)
/// 별도 최상위 창으로 만든다. 활성화되지 않는 도구 창이라 포커스를 빼앗지 않는다.
/// 위치·표시/숨김은 MainWindow 가 정한다.
/// </summary>
public sealed class LockOverlay : Window
{
    private readonly TextBlock _icon;
    private readonly Border _border;

    /// <summary>자물쇠 버튼을 눌렀을 때</summary>
    public event Action? Clicked;

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
        Width = Height = 32;

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
        Content = _border;
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
}
