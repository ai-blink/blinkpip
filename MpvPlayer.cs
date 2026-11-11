using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HybridPipPlayer;

public partial class MpvPlayer : IDisposable
{
    private const string DllName = "libmpv-2.dll";
    private const int EVENT_SHUTDOWN = 1, EVENT_LOG_MESSAGE = 2, EVENT_START_FILE = 6, EVENT_PLAYBACK_RESTART = 21, EVENT_END_FILE = 7, EVENT_FILE_LOADED = 8, EVENT_CLIENT_MESSAGE = 16, EVENT_VIDEO_RECONFIG = 17;
    private const int END_FILE_REASON_EOF = 0, END_FILE_REASON_ERROR = 4;

    private IntPtr _ctx;
    private Thread? _eventThread;
    private volatile bool _stop;
    private readonly string _logPath;

    /// <summary>mpv 가 error/fatal 로그나 재생 실패를 보고할 때 (이벤트 스레드에서 호출됨)</summary>
    public event Action<string>? LogError;

    /// <summary>영상 해상도/비율이 정해지거나 바뀔 때 표시 비율(가로/세로)을 알린다 (이벤트 스레드에서 호출됨)</summary>
    public event Action<double>? VideoAspectChanged;

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvEvent { public int EventId; public int Error; public ulong ReplyUserdata; public IntPtr Data; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvEventLogMessage { public IntPtr Prefix, Level, Text; public int LogLevel; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvEventEndFile { public int Reason; public int Error; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvEventClientMessage { public int NumArgs; public IntPtr Args; }

    /// <summary>영상 창에서 눌린 키를 알린다: "hpp-fullscreen"(F11·f) / "hpp-escape"(Esc) / "hpp-settings"(Ctrl+,). 이벤트 스레드에서 호출됨</summary>
    public event Action<string>? KeyAction;

    /// <summary>파일이 열려 재생 준비가 됐을 때(이벤트 스레드에서 호출됨)</summary>
    public event Action? FileLoaded;

    /// <summary>파일 재생이 끝났을 때. true 면 끝까지 재생(EOF)한 것 (이벤트 스레드에서 호출됨)</summary>
    public event Action<bool>? FileEnded;

    public event Action<string>? DiagnosticStarted;
    public event Action<string, string, string>? DiagnosticLog;
    public event Action<int, string>? DiagnosticEnded;
    public event Action<bool>? DiagnosticPlaybackStarted;
    public string Version => GetProperty("mpv-version") ?? "확인 필요";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mpv_create();
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_initialize(IntPtr ctx);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_set_option_string(IntPtr ctx, byte[] name, byte[] data);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_set_property_string(IntPtr ctx, byte[] name, byte[] data);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mpv_get_property_string(IntPtr ctx, byte[] name);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern void mpv_free(IntPtr data);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_command(IntPtr ctx, IntPtr[] args);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_request_log_messages(IntPtr ctx, byte[] minLevel);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern void mpv_wakeup(IntPtr ctx);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mpv_error_string(int error);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern void mpv_terminate_destroy(IntPtr ctx);

    public MpvPlayer(IntPtr renderHwnd)
    {
        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HybridPipPlayer");
        Directory.CreateDirectory(dataDir);
        _logPath = Path.Combine(dataDir, "mpv.log");

        _ctx = mpv_create();
        if (_ctx == IntPtr.Zero) throw new InvalidOperationException("mpv_create 가 실패했습니다.");

        SetOption("wid", renderHwnd.ToInt64().ToString());
        SetOption("vo", "gpu-next");              // 최신 고화질 렌더러
        SetOption("hwdec", "auto-safe");          // 하드웨어 가속
        SetOption("keep-open", "yes");
        SetOption("ytdl", "yes");                 // 유튜브 스트리밍 활성화
        SetOption("ytdl-format", "bestvideo[height<=?1440]+bestaudio/best");
        // wid 로 붙인 mpv 자식 창이 키 입력을 받으므로 기본 키 바인딩(스페이스·방향키 등)을 켠다
        SetOption("input-default-bindings", "yes");
        SetOption("input-vo-keyboard", "yes");
        // 자막: 기본(55)은 작아서 키우고 외곽선을 굵게. 같은 이름 자막 파일도 느슨하게 찾는다.
        SetOption("sub-font-size", "90");
        SetOption("sub-border-size", "4");
        SetOption("sub-ass-override", "scale"); // ASS 자막에도 sub-scale 을 적용(자체 크기를 쓰는 자막이 작게 나오는 문제)
        SetOption("sub-auto", "fuzzy");
        SetupSponsorBlock(dataDir);

        int rc = mpv_initialize(_ctx);
        if (rc < 0)
        {
            string msg = ErrorString(rc);
            mpv_terminate_destroy(_ctx);
            _ctx = IntPtr.Zero;
            throw new InvalidOperationException("mpv_initialize 실패: " + msg);
        }

        // 더블클릭 전체화면은 임베드된 창에서 의미가 없고 PIP 드래그와 겹쳐 오작동하므로 끈다
        Command("keybind", "MBTN_LEFT_DBL", "ignore");
        InitializeDefaultShortcuts();
        mpv_request_log_messages(_ctx, ToUtf8("v"));
        _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mpv-events" };
        _eventThread.Start();
    }

    // 영상 내부 스폰서·인트로·구독 요청 구간 자동 건너뛰기 (python 필요)
    private void SetupSponsorBlock(string dataDir)
    {
        string lua = Path.Combine(AppContext.BaseDirectory, "scripts", "sponsorblock.lua");
        if (!File.Exists(lua)) return;
        string? py = FindPython();
        if (py == null) { Log("python 을 찾지 못해 SponsorBlock 을 끕니다."); return; }
        try
        {
            // 카테고리 목록에 쉼표가 있어 script-opts 로는 못 넘긴다 → config-dir 의 conf 파일 사용
            // (설치 폴더가 읽기 전용일 수 있어 LocalAppData 에 쓴다)
            string cfg = Path.Combine(dataDir, "mpvcfg");
            Directory.CreateDirectory(Path.Combine(cfg, "script-opts"));
            File.WriteAllText(Path.Combine(cfg, "script-opts", "sponsorblock.conf"),
                $"python_path={py}\nskip_categories=sponsor,intro,outro,selfpromo,interaction\n");
            // sponsorblock.lua 는 mp.find_config_file("scripts") 로 sponsorblock_shared 를 찾으므로
            // 스크립트를 config-dir/scripts 로 복사하고 mpv 자동 로드에 맡긴다(uid 파일도 여기에 써진다)
            CopyDirectory(Path.GetDirectoryName(lua)!, Path.Combine(cfg, "scripts"));
            SetOption("config", "yes");
            SetOption("config-dir", cfg);
            SetOption("load-scripts", "yes");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log("SponsorBlock 설정 쓰기 실패: " + ex.Message);
        }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    private static string? FindPython()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                string p = Path.Combine(dir.Trim(), "python.exe");
                if (File.Exists(p) && !p.Contains("WindowsApps")) return p;
            }
            catch { }
        }
        foreach (var v in new[] { "314", "313", "312", "311" })
        {
            string p = $@"C:\Python{v}\python.exe";
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public void Load(string pathOrUrl) => Command("loadfile", pathOrUrl, "replace");
    public void TogglePause() => Command("cycle", "pause");
    public void SetPause(bool pause) => SetProperty("pause", pause ? "yes" : "no");
    public void SetLoop(bool on) => SetProperty("loop-file", on ? "inf" : "no"); // 배경화면 모드에서만 무한 반복
    // 사용자가 조절한 배율을 덮어쓰지 않도록 절대값이 아니라 곱한다 (PIP 진입 ×1.5, 해제 ×1/1.5)
    public void MultiplySubScale(double factor) => Command("multiply", "sub-scale", factor.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
    public void Stop() => Command("stop");

    // 재생 컨트롤용 읽기/쓰기. 파일이 없거나 값이 없으면 null(읽기).
    private double? GetDouble(string name) =>
        double.TryParse(GetProperty(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : null;
    private static string Inv(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    public double? GetTimePos() => GetDouble("time-pos");
    public double? GetDuration() => GetDouble("duration");
    public bool GetPaused() => GetProperty("pause") == "yes";
    public double? GetVolume() => GetDouble("volume");
    public void SeekTo(double seconds) => Command("seek", Inv(seconds), "absolute");
    // exact: 기본 상대 이동은 가까운 키프레임으로 붙어서 5초 단위가 어긋난다(키프레임 간격이 긴 영상일수록 심함)
    public void SeekRelative(double seconds) => Command("seek", Inv(seconds), "relative+exact");
    public void SetVolume(double percent) => SetProperty("volume", Inv(Math.Clamp(percent, 0, 100)));
    public void SetMute(bool mute) => SetProperty("mute", mute ? "yes" : "no");
    public bool GetMuted() => GetProperty("mute") == "yes";

    /// <summary>오디오 출력 장치 목록(mpv audio-device-list: JSON 배열 [{name, description}])</summary>
    public List<(string Name, string Description)> GetAudioDevices()
    {
        var list = new List<(string, string)>();
        string? json = GetProperty("audio-device-list");
        if (string.IsNullOrEmpty(json)) return list;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                string? name = e.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name)) continue;
                string desc = e.TryGetProperty("description", out var d) ? d.GetString() ?? name : name;
                list.Add((name, desc));
            }
        }
        catch (System.Text.Json.JsonException) { /* 형식을 못 읽으면 빈 목록 */ }
        return list;
    }

    public string GetAudioDevice() => GetProperty("audio-device") ?? "auto";
    public void SetAudioDevice(string name) => SetProperty("audio-device", name);
    public void SetSubFontSize(int size) => SetProperty("sub-font-size", size.ToString(System.Globalization.CultureInfo.InvariantCulture));
    /// <summary>선호 언어(쉼표 구분). 빈 문자열이면 건드리지 않는다. 다음에 여는 파일부터 적용된다.</summary>
    public void SetLangs(string subLangs, string audioLangs)
    {
        if (subLangs.Length > 0) SetProperty("slang", subLangs);
        if (audioLangs.Length > 0) SetProperty("alang", audioLangs);
    }
    public string? GetPath() => GetProperty("path");

    // ── 자막 파일 · 싱크 · 화면 도구 ──
    public void AddSubtitle(string path) => Command("sub-add", path, "select");
    public double GetSubDelay() => GetDouble("sub-delay") ?? 0;
    public void SetSubDelay(double sec) => SetProperty("sub-delay", Inv(Math.Clamp(sec, -60, 60)));
    public double GetAudioDelay() => GetDouble("audio-delay") ?? 0;
    public void SetAudioDelay(double sec) => SetProperty("audio-delay", Inv(Math.Clamp(sec, -60, 60)));
    /// <summary>영상 위에 잠깐 글자를 띄운다(mpv OSD)</summary>
    public void ShowText(string text) => Command("show-text", text, "1500");
    public void Screenshot() => Command("screenshot");
    public void FrameStep() => Command("frame-step");
    public void FrameBackStep() => Command("frame-back-step");
    public void ToggleAbLoop() => Command("ab-loop");
    /// <summary>A-B 반복 상태: 0 꺼짐, 1 A 지정됨(B 대기), 2 반복 중</summary>
    public int GetAbLoopState()
    {
        bool a = GetProperty("ab-loop-a") is { } x && x != "no";
        bool b = GetProperty("ab-loop-b") is { } y && y != "no";
        return a && b ? 2 : a ? 1 : 0;
    }
    public void SetupScreenshots(string dir)
    {
        SetProperty("screenshot-directory", dir);
        SetProperty("screenshot-format", "png");
        SetProperty("screenshot-template", "%F_%wH.%wM.%wS_%n");
    }

    // ── 재생 목록 ──
    /// <summary>files 를 재생 목록으로 올리고 index 번째부터 재생한다</summary>
    public void LoadList(IReadOnlyList<string> files, int index)
    {
        if (files.Count == 0) return;
        Load(files[0]);
        for (int i = 1; i < files.Count; i++) Command("loadfile", files[i], "append");
        if (index > 0) SetProperty("playlist-pos", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    public void PlaylistNext() => Command("playlist-next");
    public void PlaylistPrev() => Command("playlist-prev");
    public void PlaylistShuffle() => Command("playlist-shuffle");
    public void PlaylistPlay(int index) => SetProperty("playlist-pos", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public int GetPlaylistCount() => (int)(GetDouble("playlist-count") ?? 0);
    public int GetPlaylistPos() => (int)(GetDouble("playlist-pos") ?? -1);

    /// <summary>목록 끝에 붙인다. 목록이 비어 있으면 첫 항목부터 바로 재생한다.</summary>
    public void PlaylistAppend(IReadOnlyList<string> files)
    {
        if (GetPlaylistCount() == 0) { LoadList(files, 0); return; }
        foreach (string f in files) Command("loadfile", f, "append");
    }
    public void PlaylistRemove(int index) => Command("playlist-remove", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
    /// <summary>from 번 항목을 to 번 항목 앞으로 옮긴다(mpv 규칙: 맨 끝으로 보내려면 to = 개수)</summary>
    public void PlaylistMove(int from, int to) => Command("playlist-move", from.ToString(System.Globalization.CultureInfo.InvariantCulture), to.ToString(System.Globalization.CultureInfo.InvariantCulture));
    /// <summary>지금 재생 중인 항목만 남기고 비운다</summary>
    public void PlaylistClear() => Command("playlist-clear");

    public List<(int Index, string Name, bool Current)> GetPlaylist()
    {
        var list = new List<(int, string, bool)>();
        int n = GetPlaylistCount();
        for (int i = 0; i < n; i++)
            list.Add((i, GetProperty($"playlist/{i}/filename") ?? "", GetProperty($"playlist/{i}/current") == "yes"));
        return list;
    }

    /// <summary>반복 모드: 0 끔, 1 목록 전체, 2 한 곡</summary>
    public int GetRepeat() => GetProperty("loop-file") is "inf" or "yes" ? 2 : GetProperty("loop-playlist") is "inf" or "yes" ? 1 : 0;
    public void SetRepeat(int mode)
    {
        SetProperty("loop-file", mode == 2 ? "inf" : "no");
        SetProperty("loop-playlist", mode == 1 ? "inf" : "no");
    }

    public double? GetSpeed() => GetDouble("speed");
    public void SetSpeed(double speed) => SetProperty("speed", Inv(Math.Clamp(speed, 0.1, 4)));

    public sealed record TrackInfo(int Id, string Label, bool Selected);

    /// <summary>type = "sub" 또는 "audio". 현재 파일의 해당 트랙 목록(없으면 빈 목록).</summary>
    public List<TrackInfo> GetTracks(string type)
    {
        var list = new List<TrackInfo>();
        int n = (int)(GetDouble("track-list/count") ?? 0);
        for (int i = 0; i < n; i++)
        {
            if (GetProperty($"track-list/{i}/type") != type) continue;
            if (!int.TryParse(GetProperty($"track-list/{i}/id"), out int id)) continue;
            string lang = GetProperty($"track-list/{i}/lang") ?? "";
            string title = GetProperty($"track-list/{i}/title") ?? "";
            string label = $"{id}. {(lang.Length > 0 ? lang : "알 수 없음")}{(title.Length > 0 ? " · " + title : "")}";
            list.Add(new TrackInfo(id, label, GetProperty($"track-list/{i}/selected") == "yes"));
        }
        return list;
    }

    /// <summary>kind = "sid"(자막) 또는 "aid"(오디오). id 가 null 이면 끈다.</summary>
    public void SetTrack(string kind, int? id) => SetProperty(kind, id?.ToString() ?? "no");

    public void SetOption(string name, string value)
    {
        int rc = mpv_set_option_string(_ctx, ToUtf8(name), ToUtf8(value));
        if (rc < 0) Log($"옵션 실패 {name}={value}: {ErrorString(rc)}");
    }

    public int SetProperty(string name, string value)
    {
        if (_ctx == IntPtr.Zero) return -1;
        int rc = mpv_set_property_string(_ctx, ToUtf8(name), ToUtf8(value));
        if (rc < 0) Log($"속성 실패 {name}: {ErrorString(rc)}");
        return rc;
    }

    private string? GetProperty(string name)
    {
        if (_ctx == IntPtr.Zero) return null;
        IntPtr p = mpv_get_property_string(_ctx, ToUtf8(name));
        if (p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(p); }
        finally { mpv_free(p); }
    }

    // 앰비언트 글로우: 같은 프레임을 작게 줄여 블러한 뒤 확대해 배경으로 깔고, 원본을 중앙에 얹는다.
    //  - lavfi-complex 는 그래프에 연결되지 않은 스트림을 재생하지 않으므로 오디오가 있으면 [ao] 로 패스스루한다.
    //  - 복잡한 필터 그래프는 비복사 하드웨어 디코딩과 충돌하므로 켜는 동안 auto-copy 로 바꾼다.
    //  - 오디오 유무는 켜는 시점의 현재 파일 기준이다(다른 파일을 열면 다시 토글).
    public bool SetAmbient(bool on)
    {
        if (_ctx == IntPtr.Zero) return false;
        string graph = "";
        if (on)
        {
            bool hasAudio = GetProperty("current-tracks/audio/id") != null;
            graph = "[vid1]split=2[bg][fg];" +
                    "[bg]scale=320:180:force_original_aspect_ratio=increase,crop=320:180,gblur=sigma=6,scale=1920:1080[b];" +
                    "[fg]scale=1920:1080:force_original_aspect_ratio=decrease[f];" +
                    "[b][f]overlay=(W-w)/2:(H-h)/2[vo]" +
                    (hasAudio ? ";[aid1]anull[ao]" : "");
        }
        SetProperty("hwdec", on ? "auto-copy" : "auto-safe");
        return SetProperty("lavfi-complex", graph) >= 0;
    }

    public void Command(params string[] args)
    {
        if (_ctx == IntPtr.Zero) return;
        IntPtr[] ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                byte[] bytes = ToUtf8(args[i]);
                ptrs[i] = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, ptrs[i], bytes.Length);
            }
            ptrs[args.Length] = IntPtr.Zero;
            int rc = mpv_command(_ctx, ptrs);
            if (rc < 0) Report($"명령 실패 {args[0]}: {ErrorString(rc)}");
        }
        finally
        {
            for (int i = 0; i < args.Length; i++) if (ptrs[i] != IntPtr.Zero) Marshal.FreeHGlobal(ptrs[i]);
        }
    }

    private void EventLoop()
    {
        while (!_stop)
        {
            IntPtr ctx = _ctx;
            if (ctx == IntPtr.Zero) break;
            IntPtr evPtr = mpv_wait_event(ctx, 0.5);
            if (evPtr == IntPtr.Zero) continue;
            var ev = Marshal.PtrToStructure<MpvEvent>(evPtr);
            switch (ev.EventId)
            {
                case EVENT_SHUTDOWN:
                    return;
                case EVENT_LOG_MESSAGE when ev.Data != IntPtr.Zero:
                    var lm = Marshal.PtrToStructure<MpvEventLogMessage>(ev.Data);
                    string level = Marshal.PtrToStringUTF8(lm.Level) ?? "";
                    string text = (Marshal.PtrToStringUTF8(lm.Text) ?? "").TrimEnd();
                    string prefix = Marshal.PtrToStringUTF8(lm.Prefix) ?? "";
                    DiagnosticLog?.Invoke(prefix, level, text);
                    if (level is "warn" or "error" or "fatal") Log($"[{level}] {prefix}: {text}");
                    if (level is "error" or "fatal") LogError?.Invoke($"{prefix}: {text}");
                    break;
                case EVENT_CLIENT_MESSAGE when ev.Data != IntPtr.Zero:
                    var cm = Marshal.PtrToStructure<MpvEventClientMessage>(ev.Data);
                    if (cm.NumArgs >= 1 && cm.Args != IntPtr.Zero)
                    {
                        string? first = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(cm.Args));
                        if (first != null && first.StartsWith("hpp-", StringComparison.Ordinal)) KeyAction?.Invoke(first);
                    }
                    break;
                case EVENT_START_FILE:
                    DiagnosticStarted?.Invoke(GetPath() ?? "확인 필요");
                    break;
                case EVENT_PLAYBACK_RESTART:
                    DiagnosticPlaybackStarted?.Invoke(GetPaused());
                    break;
                case EVENT_FILE_LOADED:
                    FileLoaded?.Invoke();
                    break;
                case EVENT_VIDEO_RECONFIG:
                    // video-params/aspect = 디코딩된 영상의 표시 비율(비정방 픽셀 반영). 오디오 전용이면 없다.
                    if (double.TryParse(GetProperty("video-params/aspect"), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double asp) && asp > 0.1 && asp < 10)
                        VideoAspectChanged?.Invoke(asp);
                    break;
                case EVENT_END_FILE when ev.Data != IntPtr.Zero:
                    var ef = Marshal.PtrToStructure<MpvEventEndFile>(ev.Data);
                    DiagnosticEnded?.Invoke(ef.Reason, ef.Reason == END_FILE_REASON_ERROR ? ErrorString(ef.Error) : "");
                    if (ef.Reason == END_FILE_REASON_ERROR) Report("재생 실패: " + ErrorString(ef.Error));
                    FileEnded?.Invoke(ef.Reason == END_FILE_REASON_EOF);
                    break;
            }
        }
    }

    private void Report(string msg)
    {
        Log(msg);
        LogError?.Invoke(msg);
    }

    private readonly object _logLock = new();
    private void Log(string line)
    {
        try { lock (_logLock) File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss} {DiagInfo.Redact(line)}{Environment.NewLine}"); }
        catch { }
    }

    private static string ErrorString(int code) => Marshal.PtrToStringUTF8(mpv_error_string(code)) ?? code.ToString();

    private static byte[] ToUtf8(string s) => Encoding.UTF8.GetBytes(s + "\0");

    public void Dispose()
    {
        if (_ctx == IntPtr.Zero) return;
        IntPtr ctx = _ctx;
        _stop = true;
        mpv_wakeup(ctx);
        _eventThread?.Join(2000);
        _ctx = IntPtr.Zero;
        mpv_terminate_destroy(ctx);
    }
}
