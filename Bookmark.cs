using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
// WinForms(System.Drawing)와 이름이 겹치는 타입은 WPF 쪽으로 고정
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace HybridPipPlayer;

/// <summary>북마크 줄의 칩 하나. 저장은 이름·주소·표시 글자·색(#RRGGBB)만 한다.</summary>
public sealed class Bookmark
{
    public string Name { get; }
    public string Url { get; }
    public string Mark { get; }
    public string Hex { get; }
    public Brush Brush { get; }

    public Bookmark(string name, string url, string mark, string hex)
    {
        Color c;
        try { c = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex); }
        catch { c = Color.FromRgb(0x6B, 0x72, 0x80); hex = "#6B7280"; } // 색 값이 깨졌으면 회색으로 — 북마크 전체를 잃지 않게
        Name = name;
        Url = url;
        Mark = mark;
        Hex = hex;
        // 아이콘 칩: 위는 밝게(흰색 28% 섞기), 아래는 어둡게(×0.75) → 볼록한 느낌
        static byte Mix(byte v) => (byte)(v + (255 - v) * 0.28);
        static byte Dark(byte v) => (byte)(v * 0.75);
        var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B)), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(c.R, c.G, c.B), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(Dark(c.R), Dark(c.G), Dark(c.B)), 1));
        brush.Freeze();
        Brush = brush;
    }

    /// <summary>http/https 절대 주소(호스트 있음)만 참. javascript:·file:·상대 경로·"httpd.log" 같은 이름은 거른다.</summary>
    public static bool IsWebUrl(string? s) =>
        Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) && u.Host.Length > 0;

    /// <summary>주소만으로 칩을 만든다: 이름은 도메인, 글자는 첫 글자, 색은 도메인 해시로 정한 고정 팔레트 중 하나.</summary>
    public static Bookmark FromUrl(string url)
    {
        string host = new Uri(url).Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        string[] palette = { "#E6521F", "#2E86DE", "#8E44AD", "#16A085", "#C0392B", "#D4A017", "#5D6D7E" };
        int h = 0;
        foreach (char ch in host) h = (h * 31 + ch) & 0x7FFFFFFF;
        return new Bookmark(host, url, char.ToUpperInvariant(host[0]).ToString(), palette[h % palette.Length]);
    }

    public static List<Bookmark> Defaults() => new()
    {
        new("유튜브", "https://www.youtube.com", "▶", "#E62117"),
        new("넷플릭스", "https://www.netflix.com", "N", "#E50914"),
        new("디즈니+", "https://www.disneyplus.com", "D+", "#0A4FD1"),
        new("치지직", "https://chzzk.naver.com", "C", "#05B86A"),
        new("티빙", "https://www.tving.com", "T", "#E0245E"),
        new("쿠팡플레이", "https://www.coupangplay.com", "C", "#1E66F5"),
        new("웨이브", "https://www.wavve.com", "W", "#1351F9"),
        new("왓챠", "https://watcha.com", "W", "#FF0558"),
        new("라프텔", "https://laftel.net", "L", "#816BFF"),
        new("Apple TV", "https://tv.apple.com/kr", "tv", "#3A3A3C"),
        new("프라임 비디오", "https://www.primevideo.com", "P", "#00A8E1"),
    };

    /// <summary>주소의 호스트(앞 www. 제외, 소문자). 같은 사이트인지 비교할 때 쓴다.</summary>
    public static string HostKey(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return "";
        string h = u.Host.ToLowerInvariant();
        return h.StartsWith("www.") ? h[4..] : h;
    }
}

/// <summary>북마크 저장소: LocalAppData\HybridPipPlayer\bookmarks.json. 파일이 없으면 기본 목록을 쓴다.</summary>
public static class BookmarkStore
{
    private sealed record Dto(string Name, string Url, string Mark, string Hex);

    private static readonly string PathJson = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HybridPipPlayer", "bookmarks.json");

    public static List<Bookmark> Load()
    {
        try
        {
            var dtos = JsonSerializer.Deserialize<List<Dto>>(File.ReadAllText(PathJson));
            if (dtos != null)
            {
                // 항목별로 검증한다: 웹 주소가 아니거나 깨진 항목만 버리고 나머지는 살린다
                var list = new List<Bookmark>();
                foreach (var d in dtos)
                {
                    if (d == null || !Bookmark.IsWebUrl(d.Url)) continue;
                    try { list.Add(new Bookmark(d.Name ?? d.Url, d.Url, d.Mark ?? "", d.Hex ?? "")); }
                    catch { /* 이 항목만 건너뜀 */ }
                }
                return list;
            }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch { /* 깨진 파일이면 기본 목록으로 시작 — 다음 저장 때 덮어쓴다 */ }
        return Bookmark.Defaults();
    }

    public static void Save(IEnumerable<Bookmark> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathJson)!);
            File.WriteAllText(PathJson, JsonSerializer.Serialize(items.Select(b => new Dto(b.Name, b.Url, b.Mark, b.Hex))));
        }
        catch { /* 저장 실패는 재생에 영향 없음 */ }
    }
}
