using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfSeparator = System.Windows.Controls.Separator;
using WpfMouseArgs = System.Windows.Input.MouseEventArgs;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace HybridPipPlayer;

// 재생 줄, 열기, 목록, 자막/소리, 전체 화면, 설정 연결. 포맷 후 세션 기록에서 코드가 일부 사라져 다시 작성한 파일이다
// (원본과 동작이 같다고 보증하지 않는다). 창 모드(PIP/배경화면/클릭 투과)는 MainWindow.xaml.cs 가 맡는다.
public partial class MainWindow
{
    private AppSettings _settings = AppSettings.Load();
    private readonly ObservableCollection<Bookmark> _bookmarks = new(BookmarkStore.Load());
    private readonly Dictionary<string, ImageSource> _icoCache = new();
    private readonly DispatcherTimer _playTick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private SettingsWindow? _settingsWindow;
    private bool _muted, _isFullscreen, _updatingUi;
    private string? _resumePath;

    // mpv 화면이 보이는 동안만 엔진 A(재생 줄이 mpv 를 제어). 웹뷰가 보이면 엔진 B.
    private bool _engineA => MpvHost?.Visibility == Visibility.Visible; // XAML 로딩 중에는 아직 null

    private static string ShotDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "HybridPipPlayer");

    // ───────── 시작 ─────────

    private void InitPlayback()
    {
        ThemeManager.Apply(_settings.Theme);
        BookmarkBar.ItemsSource = _bookmarks;
        UpdateSeekButtons();
        ApplyLayoutSettings();
        RebuildExtras();

        if (_mpv != null)
        {
            _mpv.SetupScreenshots(ShotDir);
            ApplyPlayerSettings();
            _mpv.SetVolume(_settings.DefaultVolume);
            _mpv.SetSpeed(_settings.DefaultSpeed);
            _mpv.FileLoaded += () => Dispatcher.BeginInvoke(OnFileLoaded);
            _mpv.FileEnded += eof => Dispatcher.BeginInvoke(() => OnFileEnded(eof));
        }
        _updatingUi = true;
        VolumeSlider.Value = _settings.DefaultVolume;
        _updatingUi = false;

        _playTick.Tick += (_, _) => PlayTick();
        _playTick.Start();
        Closing += (_, _) => SaveResume();
    }

    // 컨트롤 줄 위치, 북마크 줄 표시 같은 화면 배치 설정
    private void ApplyLayoutSettings()
    {
        bool top = _settings.ControlBarPosition == "top";
        Grid.SetRow(ControlRow, top ? 1 : 3);
        ControlRow.BorderThickness = top ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0);
        BookmarkRowBorder.Visibility = _settings.ShowBookmarks ? Visibility.Visible : Visibility.Collapsed;
    }

    // mpv 쪽 설정(언어, 자막 크기, 장치, 화질, 이퀄라이저)
    private void ApplyPlayerSettings()
    {
        if (_mpv == null) return;
        _mpv.SetLangs(_settings.SubLangs, _settings.AudioLangs);
        _mpv.SetSubFontSize(_settings.SubFontSize);
        if (_settings.AudioDevice != "auto") _mpv.SetAudioDevice(_settings.AudioDevice);
        string[] props = { "brightness", "contrast", "saturation", "gamma" };
        for (int i = 0; i < props.Length; i++) _mpv.SetProperty(props[i], _settings.VideoAdjust[i].ToString());
        // 대역 중심 주파수(Hz)마다 이득(dB)을 거는 필터 체인. 전부 0 이면 필터를 끈다
        int[] freq = { 60, 230, 910, 3600, 14000 };
        var parts = new List<string>();
        for (int i = 0; i < freq.Length; i++)
            if (_settings.EqBands[i] != 0)
                parts.Add($"equalizer=f={freq[i]}:width_type=o:width=2:g={_settings.EqBands[i]}");
        _mpv.SetProperty("af", parts.Count == 0 ? "" : "lavfi=[" + string.Join(",", parts) + "]");
    }

    private ImageSource Ico(string name)
    {
        if (_icoCache.TryGetValue(name, out var cached)) return cached;
        ImageSource src;
        try { src = new BitmapImage(new Uri($"pack://application:,,,/Assets/icons/{name}.png")); }
        catch (Exception) { src = new DrawingImage(); } // 아이콘 파일이 없어도 창은 뜬다
        return _icoCache[name] = src;
    }

    // ───────── 재생 줄 갱신 ─────────

    private void PlayTick()
    {
        if (_closing || _mpv == null || !_engineA) return;
        double dur = _mpv.GetDuration() ?? 0;
        double pos = _mpv.GetTimePos() ?? 0;
        _updatingUi = true;
        try
        {
            if (!SeekSlider.IsMouseCaptureWithin) SeekSlider.Value = dur > 0 ? Math.Clamp(pos / dur, 0, 1) : 0;
            TimeText.Text = $"{Fmt(pos)} / {Fmt(dur)}";
            PlayImg.Source = Ico(_mpv.GetPaused() ? "play" : "pause");
            double sp = _mpv.GetSpeed() ?? 1;
            SpeedText.Text = sp.ToString("0.##") + "×";
            if (SpeedPopup.IsOpen) SpeedValueBtn.Content = sp.ToString("0.00") + "×";
            bool muted = _mpv.GetMuted();
            if (muted != _muted) { _muted = muted; MuteImg.Source = Ico(_muted ? "mute" : "volume"); }
        }
        finally { _updatingUi = false; }
    }

    private static string Fmt(double sec)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, sec));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    // ───────── 열기 ─────────

    // 외부(연결 프로그램, 두 번째 실행)에서 넘어온 파일/주소
    private void OpenExternal(string path)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        UrlInputBox.Text = path;
        RouteMedia(path);
    }

    private void ShowMpv()
    {
        DrmWebView.Visibility = Visibility.Collapsed;
        MpvHost.Visibility = Visibility.Visible;
    }

    // 로컬 파일들을 연다. 한 개면 같은 폴더의 미디어를 재생 목록으로 만든다(설정에서 끌 수 있음).
    private void OpenPaths(IReadOnlyList<string> paths, bool append = false)
    {
        if (_mpv == null || paths.Count == 0) return;
        var files = paths.ToList();
        if (files.Count == 1 && files[0].EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) || files.Count == 1 && files[0].EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            try { files = MediaFiles.ReadM3u(files[0]); }
            catch (Exception ex) { SetStatus("목록을 읽지 못했습니다: " + ex.Message); return; }
            if (files.Count == 0) { SetStatus("목록이 비어 있습니다"); return; }
        }
        ShowMpv();
        if (append && _mpv.GetPlaylistCount() > 0)
        {
            _mpv.PlaylistAppend(files);
            SetStatus($"{files.Count}개를 목록에 추가했습니다");
            return;
        }
        SaveResume();
        if (files.Count == 1 && _settings.AutoPlaylist && File.Exists(files[0]) && MediaFiles.FolderPlaylist(files[0]) is { } fp)
            _mpv.LoadList(fp.Files, fp.Index);
        else
            _mpv.LoadList(files, 0);
        SetStatus("");
    }

    private void OpenFilesDialog(bool append)
    {
        var dlg = new WpfOpenFileDialog { Multiselect = true, Filter = MediaFiles.OpenFilter, Title = "파일 열기" };
        if (dlg.ShowDialog(this) == true) OpenPaths(dlg.FileNames, append);
    }

    private void OpenFolderDialog(bool recursive, bool append)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = recursive ? "폴더 열기 (하위 폴더 포함)" : "폴더 열기" };
        if (dlg.ShowDialog(this) != true) return;
        var files = MediaFiles.FolderFiles(dlg.FolderName, recursive);
        if (files.Count == 0) { SetStatus("재생할 파일이 없습니다"); return; }
        OpenPaths(files, append);
    }

    private void OpenSubtitleFile()
    {
        if (_mpv == null) return;
        string filter = "자막 파일|" + string.Join(";", MediaFiles.SubtitleExt.Select(e => "*" + e)) + "|모든 파일|*.*";
        var dlg = new WpfOpenFileDialog { Filter = filter, Title = "자막 파일 열기" };
        if (dlg.ShowDialog(this) == true) { _mpv.AddSubtitle(dlg.FileName); SetStatus("자막을 추가했습니다"); }
    }

    // mpv 화면을 저장 경로를 직접 정해 찍는다(어떤 파일이 생겼는지 알아야 토스트에 썸네일과 "폴더 열기"를 걸 수 있다)
    private async void TakeScreenshot(string dir)
    {
        if (_mpv == null) return;
        try
        {
            Directory.CreateDirectory(dir);
            string? src = _mpv.GetPath();
            string name = string.IsNullOrEmpty(src) ? "capture" : src.Contains("://") ? "stream" : Path.GetFileNameWithoutExtension(src);
            foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
            var t = TimeSpan.FromSeconds(_mpv.GetTimePos() ?? 0);
            string file = Path.Combine(dir, $"{name}_{(int)t.TotalHours:00}.{t.Minutes:00}.{t.Seconds:00}_{DateTime.Now:yyMMdd-HHmmss}.png");
            _mpv.Command("screenshot-to-file", file, "subtitles");
            if (await WaitForFileAsync(file)) ShowCaptureToast(file);
            else SetStatus("스크린샷을 저장하지 못했습니다");
        }
        catch (Exception ex) { SetStatus("스크린샷 실패: " + ex.Message); }
    }

    // 파일이 만들어지고 크기가 0 보다 커질 때까지 최대 3초 기다린다
    private static async System.Threading.Tasks.Task<bool> WaitForFileAsync(string file)
    {
        for (int i = 0; i < 30; i++)
        {
            try { if (File.Exists(file) && new FileInfo(file).Length > 0) return true; } catch (IOException) { }
            await System.Threading.Tasks.Task.Delay(100);
        }
        return false;
    }

    // ───────── 스크린샷 토스트 ─────────

    private ToastWindow? _toast;

    private void ShowCaptureToast(string file) =>
        ShowToast("스크린샷을 저장했습니다", Path.GetFileName(file), file, "폴더 열기", () => OpenFolderSelecting(file));

    // 설정의 "미리 보기" 버튼: 지금 설정 값(투명도, 표시 시간)으로 알림을 한 번 띄운다
    private void ShowToastPreview() =>
        ShowToast("알림 미리 보기", $"{_settings.ToastSeconds:0}초 동안, 불투명도 {_settings.ToastOpacity * 100:0}%로 표시됩니다", null, "폴더 열기", () => OpenFolder(ShotDir));

    // 표시 시간과 투명도는 설정에서 읽는다(_settings.ToastSeconds, ToastOpacity)
    private void ShowToast(string title, string detail, string? thumbnailFile, string actionText, Action action)
    {
        if (_closing) return;
        _toast?.Dismiss(immediate: true);
        var toast = new ToastWindow(title, detail, thumbnailFile, actionText, action,
                                    TimeSpan.FromSeconds(_settings.ToastSeconds), _settings.ToastOpacity);
        toast.Closed += (_, _) => { if (ReferenceEquals(_toast, toast)) _toast = null; };
        _toast = toast;
        // 플레이어 창의 오른쪽 아래, 재생 줄 바로 위에 띄운다. 토스트는 별도 창이라 영상 위에도 올라간다.
        // 화면 구석은 접근성 도구 같은 다른 프로그램의 패널이 더 높은 층으로 가리는 일이 있어 피한다.
        NativeMethods.GetWindowRect(_hwnd, out var r);
        var wa = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea;
        double s = Scale;
        double right = Math.Min(r.Right, wa.Right) / s;
        double bottom = Math.Min(r.Bottom - 52 * s, wa.Bottom) / s;
        toast.Anchor(right + 8, bottom + 8); // 그림자 공간(바깥 여백 16)의 절반만큼 가장자리에 붙인다
        toast.Show();
    }

    // 탐색기에서 그 파일을 선택한 채로 폴더를 연다
    private void OpenFolderSelecting(string file)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus("폴더를 열지 못했습니다: " + ex.Message); }
    }

    private void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { SetStatus("폴더를 열지 못했습니다: " + ex.Message); }
    }

    private void OpenSettings()
    {
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        var win = new SettingsWindow(_settings, _bookmarks, () => _mpv?.GetAudioDevices() ?? new List<(string, string)>()) { Owner = this };
        win.SettingChanged += name => OnSettingChanged(name);
        win.ToastPreviewRequested += ShowToastPreview;
        win.Closed += (_, _) => { _settingsWindow = null; BookmarkStore.Save(_bookmarks); };
        _settingsWindow = win;
        win.Show();
    }

    private void OnSettingChanged(string? name = null)
    {
        _settings.Clamp();
        _settings.Save();
        // 건너뛰기 간격은 테마·레이아웃·mpv 속성과 무관하다. 전부 다시 적용하면 재생 중 소리가 끊기거나 화면이 깜박인다
        if (name == nameof(AppSettings.SeekSeconds)) { UpdateSeekButtons(); return; }
        ThemeManager.Apply(_settings.Theme);
        ApplyLayoutSettings();
        ApplyPlayerSettings();
        UpdateSeekButtons();
    }

    // ───────── 이어 보기 / 최근 ─────────

    private void OnFileLoaded()
    {
        if (_closing || _mpv == null) return;
        string? path = _mpv.GetPath();
        if (string.IsNullOrEmpty(path)) return;
        _resumePath = path;
        RecentStore.Add(path);
        if (!_autoPaused && _mpv.GetPaused()) _mpv.SetPause(false); // 새 파일은 일시정지가 이어지지 않고 재생으로 시작한다
        if (_listOpen) RefreshPlaylistRows(); // 다음 곡으로 넘어가면 현재 표시를 옮긴다
        if (_settings.ResumePlayback && ResumeStore.Get(path) is { } pos && pos > 5) _mpv.SeekTo(pos);
    }

    private void OnFileEnded(bool eof)
    {
        if (_closing) return;
        if (eof && _resumePath != null) ResumeStore.Remove(_resumePath); // 끝까지 봤으면 이어 보기 기록을 지운다
        _resumePath = null;
    }

    // 지금 보던 위치를 저장한다(끝 근처는 저장하지 않는다)
    private void SaveResume()
    {
        if (_mpv == null || _resumePath == null || !_settings.ResumePlayback) return;
        double pos = _mpv.GetTimePos() ?? 0, dur = _mpv.GetDuration() ?? 0;
        if (dur > 0 && pos > 5 && pos < dur - 10) ResumeStore.Set(_resumePath, pos); else ResumeStore.Remove(_resumePath);
    }

    // ───────── 재생 줄 버튼 ─────────

    private void PlayPauseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_engineA) { if (!string.IsNullOrEmpty(_mpv?.GetPath())) _mpv!.TogglePause(); }
        else RunOnVideo("v.paused ? v.play() : v.pause();");
    }

    // ───────── 재생 단축키 ─────────

    private void SeekBackBtn_Click(object sender, RoutedEventArgs e) => RunKeyAction("hpp-seekb");
    private void SeekFwdBtn_Click(object sender, RoutedEventArgs e) => RunKeyAction("hpp-seekf");

    // 건너뛰기 간격 팝업(속도 팝업과 같은 방식): -N / +N 버튼을 우클릭하면 열린다
    private void SeekBtn_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenSeekPopup();
        e.Handled = true;
    }

    private void SeekCfgBtn_Click(object sender, RoutedEventArgs e) => OpenSeekPopup();

    private void OpenSeekPopup()
    {
        SeekValueBtn.Content = _settings.SeekSeconds + "초";
        SeekPopup.IsOpen = true;
    }

    private void SeekStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string tag } || !int.TryParse(tag, out int d)) return;
        SetSeekSeconds(_settings.SeekSeconds + d);
    }

    private void SeekReset_Click(object sender, RoutedEventArgs e) => SetSeekSeconds(5);

    private void SetSeekSeconds(int sec)
    {
        _settings.SeekSeconds = sec;
        OnSettingChanged(nameof(AppSettings.SeekSeconds)); // 범위 보정, 저장, 버튼 글자 갱신
        SeekValueBtn.Content = _settings.SeekSeconds + "초";
        _settingsWindow?.SyncSeekSeconds();
    }

    // 탐색 버튼의 글자와 설명을 설정의 건너뛰기 간격에 맞춘다
    private void UpdateSeekButtons()
    {
        int n = _settings.SeekSeconds;
        SeekBackText.Text = n.ToString();
        SeekFwdText.Text = n.ToString();
        SeekBackBtn.ToolTip = $"{n}초 뒤로 (←, Shift+← 는 {n * 6}초) · 우클릭: 간격 변경";
        SeekFwdBtn.ToolTip = $"{n}초 앞으로 (→, Shift+→ 는 {n * 6}초) · 우클릭: 간격 변경";
    }

    // 글자를 입력하거나 목록·콤보를 키로 고르는 중, 또는 메뉴·팝업(별도 창)에 포커스가 있으면 단축키가 가로채지 않는다
    private bool FocusBlocksShortcuts()
    {
        var focused = System.Windows.Input.Keyboard.FocusedElement;
        if (focused is System.Windows.Controls.Primitives.TextBoxBase
            or System.Windows.Controls.ComboBox or System.Windows.Controls.ListBoxItem or System.Windows.Controls.ListBox) return true;
        return focused is System.Windows.Media.Visual fv
            && System.Windows.PresentationSource.FromVisual(fv) != System.Windows.PresentationSource.FromVisual(this);
    }

    // OSK(화상 키보드) 영문 모드는 글자를 키 입력이 아니라 유니코드 문자(VK_PACKET)로 보낸다. 이런 입력은 mpv 키 바인딩에도
    // WPF 의 Key.M 같은 이벤트에도 잡히지 않으므로, 이 창이 맨 앞일 때만 저수준 훅에서 직접 받아 같은 동작 id 로 처리한다.
    // 훅은 UI 스레드에서 불리므로 포커스 확인이 안전하다. 처리한 글자는 삼켜서 다른 곳에 이중 입력되지 않게 한다.
    private IntPtr _keyHook;
    private NativeMethods.LowLevelMouseProc? _keyProc; // GC 로 수거되지 않게 필드로 유지

    private void InstallTextKeyHook()
    {
        _keyProc = TextKeyHookProc;
        _keyHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyProc, NativeMethods.GetModuleHandle(null), 0);
        if (_keyHook == IntPtr.Zero) SetStatus($"키보드 훅 설치 실패(오류 {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}): 화상 키보드의 글자 단축키 불가");
    }

    // 한글 입력 상태의 글자 키는 창에는 VK_PROCESSKEY(IME 처리 중)로 전달돼 mpv·WPF 어디서도 M·S 같은 키로 보이지 않지만,
    // 저수준 훅에서는 원래 키 코드(VK_M 등)가 보인다. 그래서 물리 키도 같은 훅에서 받아 한/영과 무관하게 처리한다.
    private readonly HashSet<uint> _swallowedUp = new(); // 삼킨 키의 KeyUp 도 같이 삼키기 위한 기록(VK 또는 유니코드 문자)

    private IntPtr TextKeyHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            bool packet = k.vkCode == NativeMethods.VK_PACKET;
            // VK_PACKET 은 scanCode 가 UTF-16 문자, 물리 키는 vkCode. 두 값이 겹치지 않게 구분해 기록한다
            uint token = packet ? 0x10000u | k.scanCode : k.vkCode;
            if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                string? id = packet ? ShortcutIdForChar((char)k.scanCode) : ShortcutIdForVk(k.vkCode);
                bool repeat = _swallowedUp.Contains(token); // 이미 눌려 있는 키의 반복 입력
                if (id != null && TryShortcut(id, repeat)) { _swallowedUp.Add(token); return (IntPtr)1; }
            }
            else if (msg == NativeMethods.WM_KEYUP && _swallowedUp.Remove(token))
            {
                return (IntPtr)1;
            }
        }
        return NativeMethods.CallNextHookEx(_keyHook, nCode, wParam, lParam);
    }

    // 대소문자는 Shift/CapsLock 으로 갈리므로 구분하지 않는다. 한글 모드의 OSK 가 자모를 유니코드로 보내는 경우도 같은 키로 본다
    // (두벌식 자판 위치: m=ㅡ, s=ㄴ, l=ㅣ, f=ㄹ)
    private static string? ShortcutIdForChar(char c) => char.ToLowerInvariant(c) switch
    {
        'm' or 'ㅡ' => "hpp-mute",
        's' or 'ㄴ' => "hpp-shot",
        'l' or 'ㅣ' => "hpp-list",
        'f' or 'ㄹ' => "hpp-fullscreen",
        '[' => "hpp-slower", ']' => "hpp-faster", ',' => "hpp-fback", '.' => "hpp-fstep",
        _ => null,
    };

    private static string? ShortcutIdForVk(uint vk) => vk switch
    {
        0x4D => "hpp-mute", 0x53 => "hpp-shot", 0x4C => "hpp-list", 0x46 => "hpp-fullscreen", // M S L F
        0xDB => "hpp-slower", 0xDD => "hpp-faster",                                          // [ ]
        0xBC => "hpp-fback", 0xBE => "hpp-fstep",                                            // , .
        _ => null,
    };

    private bool TryShortcut(string id, bool repeat)
    {
        if (_closing || !_engineA || _mpv == null) return false;
        if (NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), NativeMethods.GA_ROOT) != _hwnd) return false;
        if (FocusBlocksShortcuts()) return false;
        if ((System.Windows.Input.Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return false;
        // 꾹 눌러 반복되는 것은 속도·프레임 이동만. 켜고 끄는 키는 한 번만 동작하고 반복은 삼키기만 한다
        bool repeatable = id is "hpp-slower" or "hpp-faster" or "hpp-fback" or "hpp-fstep";
        if (repeat && !repeatable) return true;
        Dispatcher.BeginInvoke(() =>
        {
            if (_closing) return;
            if (id == "hpp-fullscreen") ToggleFullscreen(); else RunKeyAction(id);
        });
        return true;
    }

    // 키보드 포커스가 WPF 쪽에 있을 때(버튼·슬라이더를 누른 직후 등)의 재생 단축키. 영상 창에 포커스가 있으면
    // MpvPlayer 의 keybind 가 같은 id 로 RunKeyAction 을 부른다. 웹뷰(OTT)는 OttKeyBridge 가 맡는다.
    private void ShortcutKeyBridge(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_engineA || _mpv == null || FocusBlocksShortcuts()) return;
        // 포커스된 버튼의 Space 는 재생 토글이 아니라 그 버튼을 누르는 동작이어야 한다
        if (e.Key == Key.Space && System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.Primitives.ButtonBase) return;
        var mods = System.Windows.Input.Keyboard.Modifiers;
        if ((mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return;
        bool shift = (mods & ModifierKeys.Shift) != 0;
        string? id = e.Key switch
        {
            Key.Space => "hpp-play",
            Key.Left => shift ? "hpp-seekbl" : "hpp-seekb",
            Key.Right => shift ? "hpp-seekfl" : "hpp-seekf",
            Key.Up => "hpp-volup",
            Key.Down => "hpp-voldn",
            Key.M => "hpp-mute",
            Key.PageUp => "hpp-prev",
            Key.PageDown => "hpp-next",
            Key.OemOpenBrackets => "hpp-slower",
            Key.OemCloseBrackets => "hpp-faster",
            Key.Back => "hpp-speed1",
            Key.OemComma => "hpp-fback",
            Key.OemPeriod => "hpp-fstep",
            Key.S => "hpp-shot",
            Key.L => "hpp-list",
            Key.F or Key.F11 => "hpp-fullscreen",
            Key.Escape when _isFullscreen => "hpp-escape",
            _ => null,
        };
        if (id == null) return;
        e.Handled = true;
        // 꾹 눌러 반복되는 것은 건너뛰기·볼륨·속도·프레임 이동만
        if (e.IsRepeat && id is "hpp-play" or "hpp-mute" or "hpp-prev" or "hpp-next" or "hpp-speed1" or "hpp-shot" or "hpp-list" or "hpp-fullscreen" or "hpp-escape") return;
        if (id == "hpp-fullscreen") ToggleFullscreen();
        else if (id == "hpp-escape") ExitFullscreen();
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

    private void SeekBy(double seconds)
    {
        if (_mpv == null || string.IsNullOrEmpty(_mpv.GetPath())) return;
        _mpv.SeekRelative(seconds);
        _mpv.Command("show-text", (seconds < 0 ? "-" : "+") + Math.Abs(seconds).ToString("0.#") + "초", "700");
        PlayTick();
    }

    private void StepVolume(double delta)
    {
        double v = Math.Clamp(VolumeSlider.Value + delta, 0, 100);
        VolumeSlider.Value = v; // ValueChanged 가 mpv 에 반영한다
        _mpv?.Command("show-text", "볼륨 " + v.ToString("0"), "700");
    }

    private void StepSpeed(double delta)
    {
        if (_mpv == null) return;
        double sp = Math.Clamp(Math.Round(((_mpv.GetSpeed() ?? 1) + delta) * 100) / 100, 0.25, 4.0);
        _mpv.SetSpeed(sp);
        PlayTick();
        _mpv.Command("show-text", "속도 " + sp.ToString("0.##") + "×", "800");
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_mpv == null || !_engineA) return;
        _mpv.SeekTo(0);
        _mpv.SetPause(true);
    }

    private void PrevBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_mpv == null || !_engineA) return;
        if (_mpv.GetPlaylistCount() > 1 && _mpv.GetPlaylistPos() > 0) _mpv.PlaylistPrev(); else _mpv.SeekTo(0);
    }

    private void NextBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_mpv == null || !_engineA) return;
        if (_mpv.GetPlaylistCount() > 1 && _mpv.GetPlaylistPos() < _mpv.GetPlaylistCount() - 1) _mpv.PlaylistNext();
        else SetStatus("다음 파일이 없습니다");
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi || !IsLoaded || _mpv == null || !_engineA) return;
        double dur = _mpv.GetDuration() ?? 0;
        if (dur > 0) _mpv.SeekTo(e.NewValue * dur);
    }

    private void SeekSlider_PreviewMouseMove(object sender, WpfMouseArgs e)
    {
        double dur = _mpv?.GetDuration() ?? 0;
        if (dur <= 0 || SeekSlider.ActualWidth <= 0) return;
        double ratio = Math.Clamp(e.GetPosition(SeekSlider).X / SeekSlider.ActualWidth, 0, 1);
        SeekSlider.ToolTip = Fmt(ratio * dur);
    }

    private void SpeedBtn_Click(object sender, RoutedEventArgs e)
    {
        SpeedValueBtn.Content = (_mpv?.GetSpeed() ?? 1).ToString("0.00") + "×";
        SpeedPopup.IsOpen = true;
    }

    private void SpeedStep_Click(object sender, RoutedEventArgs e)
    {
        if (_mpv == null || sender is not System.Windows.Controls.Button { Tag: string tag }) return;
        if (!double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)) return;
        _mpv.SetSpeed(Math.Round(((_mpv.GetSpeed() ?? 1) + d) * 100) / 100);
        PlayTick();
    }

    private void SpeedReset_Click(object sender, RoutedEventArgs e) { _mpv?.SetSpeed(1.0); PlayTick(); }

    private void MaxBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isPipMode || _isFullscreen) return;
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void FullscreenBtn_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void MuteBtn_Click(object sender, RoutedEventArgs e) => SetMuted(!_muted);

    private void SetMuted(bool on)
    {
        _muted = on;
        if (_engineA) _mpv?.SetMute(on); else RunOnVideo($"v.muted = {(on ? "true" : "false")};");
        MuteImg.Source = Ico(on ? "mute" : "volume");
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi || !IsLoaded) return;
        if (_engineA) _mpv?.SetVolume(e.NewValue);
        else RunOnVideo($"v.volume = {(e.NewValue / 100).ToString(System.Globalization.CultureInfo.InvariantCulture)};");
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e) => OpenSettings();

    // 재생 줄의 캡처 버튼. mpv 화면이면 mpv 스크린샷(원본 해상도), 웹뷰(넷플릭스·디즈니+)면 웹뷰 화면을 PNG 로 저장한다.
    // DRM 영상 구간은 보호돼 검게 찍힐 수 있다.
    private async void CaptureBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_engineA)
            {
                if (string.IsNullOrEmpty(_mpv?.GetPath())) { SetStatus("재생 중인 영상이 없습니다"); return; }
                TakeScreenshot(ShotDir);
                return;
            }
            if (DrmWebView.CoreWebView2 == null) { SetStatus("캡처할 화면이 없습니다"); return; }
            Directory.CreateDirectory(ShotDir);
            string file = Path.Combine(ShotDir, $"web-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            await using (var fs = File.Create(file))
                await DrmWebView.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, fs);
            ShowCaptureToast(file);
        }
        catch (Exception ex) { SetStatus("캡처 실패: " + ex.Message); }
    }

    // ───────── 메뉴 ─────────

    private static WpfMenuItem Item(string header, Action click, bool check = false)
    {
        var mi = new WpfMenuItem { Header = header, IsCheckable = false, IsChecked = check };
        mi.Click += (_, _) => click();
        return mi;
    }

    private static void ShowMenu(FrameworkElement anchor, WpfContextMenu menu)
    {
        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }

    private void OpenBtn_Click(object sender, RoutedEventArgs e)
    {
        var m = new WpfContextMenu();
        m.Items.Add(Item("파일 열기", () => OpenFilesDialog(false)));
        m.Items.Add(Item("폴더 열기", () => OpenFolderDialog(false, false)));
        m.Items.Add(Item("폴더 열기 (하위 폴더 포함)", () => OpenFolderDialog(true, false)));
        m.Items.Add(new WpfSeparator());
        m.Items.Add(Item("주소 입력", () => { UrlInputBox.Focus(); UrlInputBox.SelectAll(); }));
        m.Items.Add(Item("최근 재생", () => RecentBtn_Click(RecentBtn, new RoutedEventArgs())));
        ShowMenu((FrameworkElement)sender, m);
    }

    private void MyFile_Click(object sender, RoutedEventArgs e) => OpenFilesDialog(false);

    private void RecentBtn_Click(object sender, RoutedEventArgs e)
    {
        var m = new WpfContextMenu();
        var recent = RecentStore.Load();
        if (recent.Count == 0) m.Items.Add(new WpfMenuItem { Header = "최근 재생한 항목이 없습니다", IsEnabled = false });
        foreach (string r in recent.Take(15))
        {
            string target = r;
            string label = target.Contains("://") ? target : Path.GetFileName(target);
            m.Items.Add(Item(label.Length > 60 ? label[..60] + "…" : label, () => { UrlInputBox.Text = target; RouteMedia(target); }));
        }
        if (recent.Count > 0)
        {
            m.Items.Add(new WpfSeparator());
            m.Items.Add(Item("목록 지우기", RecentStore.Clear));
        }
        ShowMenu((FrameworkElement)sender, m);
    }

    // ───────── 재생 목록 슬라이딩 패널 (오른쪽) ─────────

    private const double ListPanelWidth = 300;
    private readonly ObservableCollection<PlaylistRow> _listRows = new();
    private bool _listOpen;

    public sealed record PlaylistRow(int Index, string Title, bool Current)
    {
        public string Number => (Index + 1).ToString();
    }

    private void ListBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_listOpen) CloseListPanel(); else OpenListPanel();
    }

    // 리모컨의 "목록 관리"도 같은 패널을 연다
    private void ShowListManageMenu() => OpenListPanel();

    private void OpenListPanel()
    {
        if (_isPipMode || _isFullscreen) { SetStatus("PIP·전체 화면에서는 목록 패널을 열 수 없습니다"); return; }
        ListItems.ItemsSource ??= _listRows;
        RefreshPlaylistRows();
        _listOpen = true;
        AnimateListPanel(ListPanelWidth, animate: true);
    }

    // 창 모드가 바뀌는 곳(PIP, 전체 화면)에서는 애니메이션 없이 닫는다
    private void CloseListPanel(bool animate = true)
    {
        if (!_listOpen) return;
        _listOpen = false;
        AnimateListPanel(0, animate);
    }

    private void AnimateListPanel(double to, bool animate)
    {
        double from = ListPanel.ActualWidth;
        ListPanel.BeginAnimation(WidthProperty, null);
        ListPanel.Width = to;
        if (!animate) return;
        var anim = new System.Windows.Media.Animation.DoubleAnimation(from, to, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };
        ListPanel.BeginAnimation(WidthProperty, anim);
    }

    // mpv 의 현재 재생 목록을 패널 항목으로 다시 채운다
    private void RefreshPlaylistRows()
    {
        var list = _mpv?.GetPlaylist() ?? new();
        _listRows.Clear();
        foreach (var (idx, name, current) in list)
        {
            string title = name.Contains("://") ? name : Path.GetFileName(name);
            _listRows.Add(new PlaylistRow(idx, title, current));
        }
        ListTitle.Text = list.Count > 0 ? $"재생 목록 ({list.Count})" : "재생 목록";
        ListEmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        string[] names = { "끔", "목록 전체", "한 곡" };
        ListRepeatBtn.Content = "반복: " + names[Math.Clamp(_mpv?.GetRepeat() ?? 0, 0, 2)];
        if (_listRows.FirstOrDefault(r => r.Current) is { } cur) ListItems.ScrollIntoView(cur);
    }

    private void ListClose_Click(object sender, RoutedEventArgs e) => CloseListPanel();

    private void ListRepeat_Click(object sender, RoutedEventArgs e) { RunFeature("repeat"); RefreshPlaylistRows(); }

    private void ListShuffle_Click(object sender, RoutedEventArgs e) { RunFeature("shuffle"); RefreshPlaylistRows(); }

    private void ListItems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ListItems.SelectedItem is PlaylistRow row) _mpv?.PlaylistPlay(row.Index);
    }

    private void ListItems_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ListItems.SelectedItem is PlaylistRow play) _mpv?.PlaylistPlay(play.Index);
        else if (e.Key == Key.Delete && ListItems.SelectedItem is PlaylistRow del) { _mpv?.PlaylistRemove(del.Index); RefreshPlaylistRows(); }
        else if (e.Key == Key.Escape) CloseListPanel();
    }

    private void ListRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: int index }) { _mpv?.PlaylistRemove(index); RefreshPlaylistRows(); }
        e.Handled = true;
    }

    private void ListAddFiles_Click(object sender, RoutedEventArgs e) { OpenFilesDialog(true); RefreshPlaylistRows(); }

    private void ListAddFolder_Click(object sender, RoutedEventArgs e) { OpenFolderDialog(false, true); RefreshPlaylistRows(); }

    private void ListSave_Click(object sender, RoutedEventArgs e) => SavePlaylistFile();

    private void ListClear_Click(object sender, RoutedEventArgs e) { _mpv?.PlaylistClear(); RefreshPlaylistRows(); }

    private void SavePlaylistFile()
    {
        var list = _mpv?.GetPlaylist();
        if (list == null || list.Count == 0) { SetStatus("저장할 재생 목록이 없습니다"); return; }
        var dlg = new WpfSaveFileDialog { Filter = "재생 목록|*.m3u8", DefaultExt = ".m3u8", Title = "재생 목록 저장" };
        if (dlg.ShowDialog(this) != true) return;
        try { MediaFiles.WriteM3u(dlg.FileName, list.Select(x => x.Name)); SetStatus("목록을 저장했습니다"); }
        catch (Exception ex) { SetStatus("저장 실패: " + ex.Message); }
    }

    private void LoadPlaylistFile()
    {
        var dlg = new WpfOpenFileDialog { Filter = "재생 목록|*.m3u;*.m3u8", Title = "재생 목록 불러오기" };
        if (dlg.ShowDialog(this) == true) OpenPaths(new[] { dlg.FileName });
    }

    private void SubBtn_Click(object sender, RoutedEventArgs e) => ShowTrackMenu((FrameworkElement)sender, "sub", "sid", "자막");

    private void AudioBtn_Click(object sender, RoutedEventArgs e) => ShowAudioMenu();

    private void ShowAudioMenu() => ShowTrackMenu(AudioBtn, "audio", "aid", "소리");

    private void ShowTrackMenu(FrameworkElement anchor, string type, string kind, string title)
    {
        var m = new WpfContextMenu();
        var tracks = _engineA && _mpv != null ? _mpv.GetTracks(type) : new List<MpvPlayer.TrackInfo>();
        if (tracks.Count == 0 && type == "audio") m.Items.Add(new WpfMenuItem { Header = "선택할 소리 트랙이 없습니다", IsEnabled = false });
        if (type == "sub") m.Items.Add(Item("자막 끄기", () => _mpv?.SetTrack(kind, null), tracks.All(t => !t.Selected)));
        foreach (var t in tracks)
        {
            int id = t.Id;
            m.Items.Add(Item(t.Label, () => _mpv?.SetTrack(kind, id), t.Selected));
        }
        if (type == "sub")
        {
            m.Items.Add(new WpfSeparator());
            m.Items.Add(Item("자막 파일 열기…", OpenSubtitleFile));
        }
        ShowMenu(anchor, m);
    }

    private void MoreBtn_Click(object sender, RoutedEventArgs e)
    {
        var m = new WpfContextMenu();
        m.Items.Add(Item("스크린샷", () => RunFeature("shot")));
        m.Items.Add(Item("스크린샷 폴더 열기", () => RunFeature("shotdir")));
        m.Items.Add(new WpfSeparator());
        m.Items.Add(Item("한 프레임 앞", () => RunFeature("fstep")));
        m.Items.Add(Item("한 프레임 뒤", () => RunFeature("fback")));
        m.Items.Add(Item("구간 반복", () => RunFeature("abloop")));
        ShowMenu((FrameworkElement)sender, m);
    }

    // ───────── 북마크 ─────────

    private void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: Bookmark b }) return;
        UrlInputBox.Text = b.Url;
        RouteMedia(b.Url);
    }

    private void BookmarkDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfMenuItem { Parent: WpfContextMenu { PlacementTarget: System.Windows.Controls.Button { Tag: Bookmark b } } })
        {
            _bookmarks.Remove(b);
            BookmarkStore.Save(_bookmarks);
        }
    }

    private void AddBookmark_Click(object sender, RoutedEventArgs e)
    {
        string url = UrlInputBox.Text.Trim();
        if (!Bookmark.IsWebUrl(url)) { SetStatus("주소창에 북마크할 웹 주소를 넣어 주세요"); return; }
        if (_bookmarks.Any(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase))) { SetStatus("이미 있는 북마크입니다"); return; }
        _bookmarks.Add(Bookmark.FromUrl(url));
        BookmarkStore.Save(_bookmarks);
    }

    // ───────── 끌어다 놓기 ─────────

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) || e.Data.GetDataPresent(System.Windows.DataFormats.Text);
        e.Effects = ok ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            if (files.Length == 1 && Directory.Exists(files[0])) OpenPaths(MediaFiles.FolderFiles(files[0], true));
            else OpenPaths(files.Where(f => MediaFiles.IsMedia(f) || f.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)).ToList());
        }
        else if (e.Data.GetData(System.Windows.DataFormats.Text) is string text && text.Trim().Length > 0)
        {
            UrlInputBox.Text = text.Trim();
            RouteMedia(text);
        }
        e.Handled = true;
    }

    // ───────── 전체 화면 ─────────

    private NativeMethods.RECT _fsRestore;
    private bool _fsWasMaximized;
    private DispatcherTimer? _fsTimer;
    private System.Drawing.Point _fsCursor;
    private DateTime _fsLastMove;

    private void ToggleFullscreen()
    {
        if (_isFullscreen) ExitFullscreen(); else EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        if (_isFullscreen || _isPipMode || _isWallpaperMode) return;
        CloseListPanel(animate: false);
        _fsWasMaximized = WindowState == WindowState.Maximized;
        if (_fsWasMaximized) WindowState = WindowState.Normal;
        NativeMethods.GetWindowRect(_hwnd, out _fsRestore);
        var bounds = System.Windows.Forms.Screen.FromHandle(_hwnd).Bounds;
        _isFullscreen = true;
        RootGrid.Margin = new Thickness(0);
        TopBar.Visibility = Visibility.Collapsed;
        Topmost = true;
        SetRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        _fsCursor = System.Windows.Forms.Cursor.Position;
        _fsLastMove = DateTime.UtcNow;
        _fsTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _fsTimer.Tick -= FullscreenTick;
        _fsTimer.Tick += FullscreenTick;
        _fsTimer.Start();
    }

    private void ExitFullscreen()
    {
        if (!_isFullscreen) return;
        _isFullscreen = false;
        _fsTimer?.Stop();
        Topmost = false;
        RootGrid.Margin = new Thickness(6);
        TopBar.Visibility = Visibility.Visible;
        ControlRow.Visibility = Visibility.Visible;
        RestoreRect(_fsRestore);
        if (_fsWasMaximized) WindowState = WindowState.Maximized;
    }

    // 커서가 움직이면 컨트롤 줄을 보이고, FullscreenHideSec 동안 가만히 있으면 숨긴다
    private void FullscreenTick(object? sender, EventArgs e)
    {
        var cur = System.Windows.Forms.Cursor.Position;
        if (cur != _fsCursor) { _fsCursor = cur; _fsLastMove = DateTime.UtcNow; ControlRow.Visibility = Visibility.Visible; return; }
        bool busy = SpeedPopup.IsOpen || SeekPopup.IsOpen || ControlRow.IsMouseOver;
        if (!busy && (DateTime.UtcNow - _fsLastMove).TotalSeconds > _settings.FullscreenHideSec)
            ControlRow.Visibility = Visibility.Collapsed;
    }
}
