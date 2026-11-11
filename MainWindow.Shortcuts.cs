using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace HybridPipPlayer;

public partial class MainWindow
{
    private readonly ShortcutStore _shortcuts = ShortcutStore.LoadDefault();
    private IntPtr _keyHook;
    private NativeMethods.LowLevelMouseProc? _keyProc;
    private readonly HashSet<uint> _swallowedUp = new();

    private void InitializeShortcuts()
    {
        ApplyShortcutChanges();
        _shortcuts.Changed += ApplyShortcutChanges;
        Closed += (_, _) => _shortcuts.Changed -= ApplyShortcutChanges;
        if (_shortcuts.LoadWarning is { } warning) SetStatus(warning);
    }

    private void ApplyShortcutChanges()
    {
        _mpv?.ApplyShortcuts(_shortcuts);
        RefreshShortcutHints();
    }

    private void RefreshShortcutHints()
    {
        string Keys(string id) => _shortcuts.Keys(id) is { Count: > 0 } keys ? string.Join(" / ", keys) : "키 없음";
        int seconds = _settings.SeekSeconds;
        SettingsBtn.ToolTip = $"설정 ({Keys("hpp-settings")} · 영상 창)";
        FullscreenBtn.ToolTip = $"전체 화면 ({Keys("hpp-fullscreen")}) · 해제: {Keys("hpp-escape")}";
        SeekBackBtn.ToolTip = $"{seconds}초 뒤로 ({Keys("hpp-seekb")}) · {seconds * 6}초: {Keys("hpp-seekbl")} · 우클릭: 간격 변경";
        SeekFwdBtn.ToolTip = $"{seconds}초 앞으로 ({Keys("hpp-seekf")}) · {seconds * 6}초: {Keys("hpp-seekfl")} · 우클릭: 간격 변경";
    }

    // 글자 입력, 목록 선택, 메뉴·팝업의 기존 키 조작을 보존한다.
    private bool FocusBlocksShortcuts()
    {
        var focused = Keyboard.FocusedElement;
        if (focused is System.Windows.Controls.Primitives.TextBoxBase
            or System.Windows.Controls.ComboBox or System.Windows.Controls.ListBoxItem or System.Windows.Controls.ListBox) return true;
        return focused is System.Windows.Media.Visual visual
            && PresentationSource.FromVisual(visual) != PresentationSource.FromVisual(this);
    }

    private void InstallTextKeyHook()
    {
        _keyProc = TextKeyHookProc;
        _keyHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyProc, NativeMethods.GetModuleHandle(null), 0);
        if (_keyHook == IntPtr.Zero) SetStatus($"키보드 훅 설치 실패(오류 {Marshal.GetLastWin32Error()}): 화상 키보드의 글자 단축키 불가");
    }

    private IntPtr TextKeyHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            bool packet = k.vkCode == NativeMethods.VK_PACKET;
            uint token = packet ? 0x10000u | k.scanCode : k.vkCode;
            if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                Key key = packet ? ShortcutCatalog.KeyForCharacter((char)k.scanCode) : KeyInterop.KeyFromVirtualKey((int)k.vkCode);
                if (ShortcutCatalog.IsTextKey(key) && TryTextShortcut(key, _swallowedUp.Contains(token)))
                { _swallowedUp.Add(token); return (IntPtr)1; }
            }
            // Alt 조합도 편집할 수 있으므로 WM_SYSKEYUP(0x105)도 짝지어 소비한다.
            else if ((msg == NativeMethods.WM_KEYUP || msg == 0x0105) && _swallowedUp.Remove(token)) return (IntPtr)1;
        }
        return NativeMethods.CallNextHookEx(_keyHook, nCode, wParam, lParam);
    }

    private bool TryTextShortcut(Key key, bool repeat)
    {
        if (_closing || !_engineA || _mpv == null) return false;
        if (NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), NativeMethods.GA_ROOT) != _hwnd) return false;
        if (FocusBlocksShortcuts()) return false;
        var feature = _shortcuts.Resolve(key, Keyboard.Modifiers, MpvRenderPanel.ContainsFocus);
        // Before remapping, this hook also owned shifted letters/punctuation. Keep vacated
        // defaults consumed so mpv's inherited '{' speed key etc. cannot resurrect them.
        if (feature == null) return ShortcutCatalog.IsManagedTextFallback(key, Keyboard.Modifiers);
        if (repeat && !feature.Repeat) return true;
        Dispatcher.BeginInvoke(() => { if (!_closing) DispatchShortcut(feature.Id); });
        return true;
    }

    private void ShortcutKeyBridge(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Handled || !_engineA || _mpv == null || FocusBlocksShortcuts()) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Space && Keyboard.FocusedElement is System.Windows.Controls.Primitives.ButtonBase) return;
        var feature = _shortcuts.Resolve(key, Keyboard.Modifiers);
        if (feature == null || (feature.Id == "hpp-escape" && !_isFullscreen)) return;
        e.Handled = true;
        if (e.IsRepeat && !feature.Repeat) return;
        DispatchShortcut(feature.Id);
    }

    private void DispatchShortcut(string id)
    {
        if (_closing || !_engineA || _mpv == null) return;
        if (id == "hpp-fullscreen") ToggleFullscreen();
        else if (id == "hpp-escape") { if (_isFullscreen) ExitFullscreen(); }
        else if (id == "hpp-settings") OpenSettings();
        else if (id is "hpp-subup" or "hpp-subdown")
        {
            _mpv.StepShortcutSubtitleScale(id == "hpp-subup" ? 1 : -1);
        }
        else RunKeyAction(id);
    }

    private void RunKeyAction(string id)
    {
        if (_mpv == null || !_engineA) return;
        switch (id)
        {
            case "hpp-play": PlayPauseBtn_Click(PlayPauseBtn, new RoutedEventArgs()); break;
            case "hpp-seekb": SeekBy(-_settings.SeekSeconds); break;
            case "hpp-seekf": SeekBy(_settings.SeekSeconds); break;
            case "hpp-seekbl": SeekBy(-_settings.SeekSeconds * 6); break;
            case "hpp-seekfl": SeekBy(_settings.SeekSeconds * 6); break;
            case "hpp-volup": StepVolume(5); break;
            case "hpp-voldn": StepVolume(-5); break;
            case "hpp-mute": SetMuted(!_muted); break;
            case "hpp-prev": PrevBtn_Click(PrevBtn, new RoutedEventArgs()); break;
            case "hpp-next": NextBtn_Click(NextBtn, new RoutedEventArgs()); break;
            case "hpp-slower": StepSpeed(-0.25); break;
            case "hpp-faster": StepSpeed(0.25); break;
            case "hpp-speed1": _mpv.SetSpeed(1.0); PlayTick(); _mpv.Command("show-text", "속도 1×", "800"); break;
            case "hpp-fback": RunFeature("fback"); break;
            case "hpp-fstep": RunFeature("fstep"); break;
            case "hpp-shot": RunFeature("shot"); break;
            case "hpp-list": ListBtn_Click(ListBtn, new RoutedEventArgs()); break;
        }
    }
}
