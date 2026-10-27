using System;
using System.Collections.Generic;
using System.Linq;

namespace HybridPipPlayer;

/// <summary>리모컨에 올릴 수 있는 기능 목록. 실행은 MainWindow.Remote.cs 의 RunFeature 가 id 로 기존 핸들러에 잇는다.
/// Glyph 는 Segoe MDL2 문자, Icon 은 Assets/icons 그림 이름(있으면 그림을 우선 쓴다).</summary>
public sealed record Feature(string Id, string Name, string Glyph, string? Icon, string Category, bool NeedsMpv, string? Shortcut = null);

public static class FeatureCatalog
{
    public static readonly (string Id, string Name)[] Categories =
    {
        ("nav", "탐색"), ("list", "목록"), ("av", "소리·자막"), ("view", "화면"), ("app", "앱"),
    };

    public static readonly IReadOnlyList<Feature> All = new Feature[]
    {
        new("open",    "파일 열기",       "", "open", "nav",  false),
        new("folder",  "폴더 열기",       "", null,   "nav",  false),
        new("folderr", "폴더(하위 포함)", "", null,   "nav",  false),
        new("recent",  "최근 재생",       "", null,   "nav",  false),
        new("url",     "주소 입력",       "", null,   "nav",  false),
        new("bmadd",   "북마크 추가",     "", null,   "nav",  false),
        new("plist",   "재생 목록",       "", null,   "list", true),
        new("pmg",     "목록 관리",       "", null,   "list", true),
        new("psave",   "목록 저장",       "", null,   "list", true),
        new("pload",   "목록 불러오기",   "", null,   "list", false),
        new("repeat",  "반복",            "", null,   "list", true),
        new("shuffle", "섞기",            "", null,   "list", true),
        new("sub",     "자막 선택",       "",       "subtitle", "av", true),
        new("audio",   "소리 선택",       "",       "audio",    "av", true),
        new("sfile",   "자막 파일 열기",  "", null,   "av",   true),
        new("pip",     "PIP",             "", null,   "view", false, "Ctrl+Alt+P"),
        new("shot",    "스크린샷",        "", "camera", "view", true),
        new("shotdir", "스크린샷 폴더",   "", null,   "view", false),
        new("fstep",   "한 프레임 앞",    "", null,   "view", true),
        new("fback",   "한 프레임 뒤",    "", null,   "view", true),
        new("abloop",  "구간 반복",       "", null,   "view", true),
        new("settings","설정",            "", null,   "app",  false, "Ctrl+,"),
    };

    private static readonly Dictionary<string, Feature> Map = All.ToDictionary(f => f.Id);

    public static Feature? ById(string? id) => id != null && Map.TryGetValue(id, out var f) ? f : null;

    public const int FavoriteSlots = 6;
    public static readonly string[] DefaultFavorites = { "recent", "plist", "shot", "pip", "pmg", "settings" };
}
