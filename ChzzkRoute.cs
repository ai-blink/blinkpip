using System.Text.RegularExpressions;

namespace blinkpip;

/// <summary>
/// 치지직(chzzk.naver.com) 주소를 엔진에 맞게 고른다. yt-dlp(2026.10.26)는 생방송 /live/&lt;채널&gt; 과 다시보기 /video/&lt;번호&gt; 만
/// 알아본다(2026-11-13 실측: 채널 홈 /&lt;채널&gt;·클립 /clips/… 는 "Unsupported URL").
///  · 채널 홈 → 그 채널의 생방송 주소로 바꿔 mpv 로 연다(방송 중이 아니면 yt-dlp 가 그렇게 알려 준다).
///  · 클립 등 나머지 치지직 페이지 → 웹뷰(엔진 B)로 연다. 치지직은 DRM 이 없어 웹 화면에서 그대로 재생된다.
///    웹뷰에서 생방송·다시보기 페이지를 보다가 PIP 를 켜면 MainWindow.Browse(HandChzzkToMpv)가 PlaysInMpv 로 보고 mpv 로 넘긴다.
/// </summary>
public static partial class ChzzkRoute
{
    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex ChannelId();

    private static bool IsChzzk(Uri uri) => uri.Host.Equals("chzzk.naver.com", StringComparison.OrdinalIgnoreCase);
    private static string[] Segments(Uri uri) => uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>yt-dlp 가 여는 치지직 주소(생방송·다시보기)인가</summary>
    public static bool PlaysInMpv(Uri uri)
    {
        if (!IsChzzk(uri)) return false;
        string[] seg = Segments(uri);
        return seg.Length >= 2 && seg[0] is "live" or "video";
    }

    /// <summary>치지직 주소면 target·useWeb 을 고친다. 다른 주소는 건드리지 않는다.</summary>
    public static void Apply(Uri uri, ref string target, ref bool useWeb)
    {
        if (!IsChzzk(uri) || PlaysInMpv(uri)) return; // 생방송·다시보기는 yt-dlp 가 연다
        string[] seg = Segments(uri);
        if (seg.Length == 1 && ChannelId().IsMatch(seg[0])) { target = "https://chzzk.naver.com/live/" + seg[0]; return; }
        useWeb = true;
    }
}
