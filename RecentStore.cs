using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HybridPipPlayer;

/// <summary>최근 재생한 파일·주소 목록(최대 15개, 최신이 앞). LocalAppData\HybridPipPlayer\recent.json 에 저장.</summary>
public static class RecentStore
{
    private const int Max = 15;
    private static readonly string PathJson = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HybridPipPlayer", "recent.json");

    public static List<string> Load()
    {
        try
        {
            // 손상된 파일(null·빈 항목)이 메뉴 만들 때 예외를 내지 않게 걸러낸다
            return (JsonSerializer.Deserialize<List<string>>(File.ReadAllText(PathJson)) ?? new())
                .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }
        catch { return new(); } // 파일이 없거나 깨졌으면 빈 목록
    }

    public static void Add(string target)
    {
        var list = Load();
        list.RemoveAll(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, target);
        Save(list.Take(Max).ToList());
    }

    public static void Clear() => Save(new());

    private static void Save(List<string> list)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathJson)!);
            File.WriteAllText(PathJson, JsonSerializer.Serialize(list));
        }
        catch { /* 저장 실패는 재생에 영향 없음 */ }
    }
}
