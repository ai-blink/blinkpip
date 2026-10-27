using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HybridPipPlayer;

/// <summary>설정. LocalAppData\HybridPipPlayer\settings.json 에 저장하고, 없거나 깨졌으면 기본값으로 시작한다.</summary>
public sealed class AppSettings
{
    // 재생
    public string ControlBarPosition { get; set; } = "bottom"; // "bottom"(영상 아래, 기본) | "top"(주소·북마크 바 아래)
    public int DefaultVolume { get; set; } = 100;              // 0~100
    public double DefaultSpeed { get; set; } = 1.0;            // 0.5~2.0
    public double FullscreenHideSec { get; set; } = 2.5;       // 전체 화면에서 컨트롤이 사라질 때까지(초)
    public bool ResumePlayback { get; set; } = true;           // 이어 보기: 다시 열면 마지막 위치부터
    public bool AutoPlaylist { get; set; } = true;             // 로컬 파일을 열면 같은 폴더의 미디어를 재생 목록으로
    // 모양
    public string Theme { get; set; } = "B";                   // A | B | C (ThemeManager)
    public bool ShowBookmarks { get; set; } = true;
    // PIP
    public int PipWidth { get; set; } = 480;                   // 320~960 (px 아님, DIP)
    public double PipHideSec { get; set; } = 2.0;              // PIP 에서 커서가 벗어난 뒤 컨트롤·자물쇠가 사라질 때까지(초)
    // 오디오 장치 · 자막
    public string AudioDevice { get; set; } = "auto";          // mpv audio-device 이름
    public string SubLangs { get; set; } = "";                 // 선호 자막 언어(쉼표, 예: ko,kor,en). 비면 mpv 기본
    public string AudioLangs { get; set; } = "";               // 선호 오디오 언어
    public int SubFontSize { get; set; } = 90;                 // mpv sub-font-size (기본 90)
    // 스크린샷 알림(토스트)
    public double ToastOpacity { get; set; } = 0.9;            // 0.3~1.0 (1 이면 불투명)
    public double ToastSeconds { get; set; } = 8;              // 떠 있는 시간(초) 2~30
    // 화면 · 소리 조절
    public int[] VideoAdjust { get; set; } = new int[4];       // 밝기, 대비, 채도, 감마 (-100~100)
    public int[] EqBands { get; set; } = new int[5];           // 60Hz, 230Hz, 910Hz, 3.6kHz, 14kHz (-12~12 dB)
    // 리모컨 · 재생 줄
    public string[] RemoteFavorites { get; set; } = FeatureCatalog.DefaultFavorites.ToArray(); // 리모컨 즐겨찾기 칸(id, 빈 문자열은 빈 칸)
    public string[] MainExtras { get; set; } = Array.Empty<string>();                         // 재생 줄에 올려 둔 기능 id

    private static readonly string PathJson = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HybridPipPlayer", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathJson)) ?? new AppSettings();
            s.Clamp();
            return s;
        }
        catch { return new AppSettings(); } // 파일이 없거나 깨졌으면 기본값 — 다음 저장 때 덮어쓴다
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathJson)!);
            File.WriteAllText(PathJson, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 저장 실패는 재생에 영향 없음 */ }
    }

    /// <summary>손으로 고친 값이 범위를 벗어나도 앱이 깨지지 않게 보정한다</summary>
    public void Clamp()
    {
        if (ControlBarPosition != "top") ControlBarPosition = "bottom";
        DefaultVolume = Math.Clamp(DefaultVolume, 0, 100);
        DefaultSpeed = Math.Clamp(double.IsFinite(DefaultSpeed) ? DefaultSpeed : 1.0, 0.5, 2.0);
        FullscreenHideSec = Math.Clamp(double.IsFinite(FullscreenHideSec) ? FullscreenHideSec : 2.5, 1.0, 10.0);
        if (!ThemeManager.Palettes.ContainsKey(Theme ?? "")) Theme = "B";
        PipWidth = Math.Clamp(PipWidth, 320, 960);
        PipHideSec = Math.Clamp(double.IsFinite(PipHideSec) ? PipHideSec : 2.0, 0.5, 10.0);
        AudioDevice = string.IsNullOrWhiteSpace(AudioDevice) ? "auto" : AudioDevice;
        SubLangs = SanitizeLangs(SubLangs);
        AudioLangs = SanitizeLangs(AudioLangs);
        SubFontSize = Math.Clamp(SubFontSize, 30, 150);
        ToastOpacity = Math.Clamp(double.IsFinite(ToastOpacity) ? ToastOpacity : 0.9, 0.3, 1.0);
        ToastSeconds = Math.Clamp(double.IsFinite(ToastSeconds) ? ToastSeconds : 8, 2, 30);
        VideoAdjust = FixArray(VideoAdjust, 4, -100, 100);
        EqBands = FixArray(EqBands, 5, -12, 12);
        var fav = (RemoteFavorites ?? Array.Empty<string>()).Take(FeatureCatalog.FavoriteSlots).Select(id => id ?? "").ToList();
        while (fav.Count < FeatureCatalog.FavoriteSlots) fav.Add("");
        RemoteFavorites = fav.ToArray();
        MainExtras = (MainExtras ?? Array.Empty<string>()).Where(id => FeatureCatalog.ById(id) != null).Distinct().Take(4).ToArray();
    }

    private static int[] FixArray(int[]? src, int len, int min, int max)
    {
        var a = new int[len];
        if (src != null) for (int i = 0; i < len && i < src.Length; i++) a[i] = Math.Clamp(src[i], min, max);
        return a;
    }

    // 언어 코드 목록은 영문자·쉼표·하이픈만 허용(mpv 옵션 문자열에 그대로 들어가므로)
    private static string SanitizeLangs(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "";
        var chars = v.Where(c => char.IsAsciiLetter(c) || c == ',' || c == '-').ToArray();
        return new string(chars).Trim(',');
    }
}
