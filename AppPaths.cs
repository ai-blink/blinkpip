using System;
using System.IO;

namespace blinkpip;

/// <summary>
/// 앱이 쓰는 폴더를 한곳에서 정한다. 0.6.0 까지는 예전 이름(HybridPipPlayer)이었으므로, 처음 켤 때 예전 폴더를 새 이름으로 옮긴다
/// (설정·북마크·이어 보기, WebView2 프로필 = OTT 로그인, 스크린샷). 옮기지 못하면(다른 프로그램이 잡고 있음 등) 예전 폴더를 그대로 쓴다.
/// </summary>
public static class AppPaths
{
    private const string LegacyName = "HybridPipPlayer";
    private const string Name = "blinkpip";

    private static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string Pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    /// <summary>설정·북마크·기록 json, mpv.log (%LOCALAPPDATA%\blinkpip)</summary>
    public static string DataDir { get; private set; } = Path.Combine(Local, Name);

    /// <summary>WebView2 사용자 데이터(OTT 로그인 유지)</summary>
    public static string WebViewDir { get; private set; } = Path.Combine(Local, Name + "_WebView2");

    /// <summary>스크린샷 저장 폴더(내 사진\blinkpip)</summary>
    public static string ShotDir { get; private set; } = Path.Combine(Pictures, Name);

    public static string DataFile(string fileName) => Path.Combine(DataDir, fileName);

    /// <summary>App.OnStartup 에서 창을 만들기 전에 한 번 부른다(단일 실행이 확인된 첫 인스턴스만).</summary>
    public static void MigrateLegacy()
    {
        DataDir = MoveOrKeep(Path.Combine(Local, LegacyName), DataDir);
        WebViewDir = MoveOrKeep(Path.Combine(Local, LegacyName + "_WebView2"), WebViewDir);
        ShotDir = MoveOrKeep(Path.Combine(Pictures, LegacyName), ShotDir);
    }

    // 새 폴더가 없고 예전 폴더만 있으면 이름을 바꾼다. 실패하면 예전 폴더 경로를 돌려줘 데이터를 잃지 않게 한다.
    private static string MoveOrKeep(string legacy, string current)
    {
        if (Directory.Exists(current) || !Directory.Exists(legacy)) return current;
        try
        {
            Directory.Move(legacy, current);
            return current;
        }
        catch (Exception) { return legacy; }
    }
}
