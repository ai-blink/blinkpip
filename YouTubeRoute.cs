namespace blinkpip;

/// <summary>
/// 유튜브 주소를 엔진에 맞게 고른다. yt-dlp(2026.10.26)는 홈·/feed/… 에서 빈 목록을 돌려줘 mpv 가
/// "Failed to recognize file format" 으로 끝난다(2026-11-17 실측: 로그인 없이 보면 홈·구독·기록 모두 영상 0개).
///  · 영상·생방송·재생목록 주소 → 지금처럼 mpv 가 연다.
///  · 그 밖의 유튜브 페이지(홈·피드·채널·검색·쇼츠 등) → 웹뷰(엔진 B)로 열어 그 안에서 영상을 고르게 한다.
///    웹뷰에서 영상 페이지를 보다가 PIP 를 켜면 MainWindow.Browse(HandChzzkToMpv)가 PlaysInMpv 로 보고 mpv 로 넘긴다.
/// </summary>
public static class YouTubeRoute
{
    private static bool IsYouTube(Uri uri)
    {
        string h = uri.Host.ToLowerInvariant();
        return h == "youtube.com" || h.EndsWith(".youtube.com");
    }

    private static string[] Segments(Uri uri) => uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>쇼츠 페이지인가. 쇼츠는 피드 넘기기·댓글을 쓰도록 웹뷰에서 열고, PIP 도 웹뷰 그대로 세로로 띄운다(MainWindow.Shorts.cs)</summary>
    public static bool IsShorts(Uri uri) => IsYouTube(uri) && Segments(uri) is ["shorts", ..];

    /// <summary>yt-dlp 가 영상으로 여는 유튜브 주소인가(youtu.be 짧은 주소 포함). 쇼츠는 빠진다(IsShorts)</summary>
    public static bool PlaysInMpv(Uri uri)
    {
        if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)) return Segments(uri).Length >= 1;
        if (!IsYouTube(uri)) return false;
        string[] seg = Segments(uri);
        if (seg.Length == 0) return false;
        string q = uri.Query;
        return seg[0] switch
        {
            "watch" => q.Contains("v=") || q.Contains("list="),
            "playlist" => q.Contains("list="),
            "live" or "embed" or "v" => seg.Length >= 2,
            _ => seg.Length >= 2 && seg[^1] == "live", // /@채널/live 등 채널 생방송
        };
    }

    /// <summary>영상이 아닌 유튜브 페이지면 useWeb 을 켠다. 다른 주소는 건드리지 않는다.</summary>
    public static void Apply(Uri uri, ref bool useWeb)
    {
        if (IsYouTube(uri) && !PlaysInMpv(uri)) useWeb = true;
    }
}
