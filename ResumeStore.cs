using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace blinkpip;

/// <summary>이어 보기 위치 저장소(최대 300개, 최근에 저장한 것이 앞). LocalAppData\blinkpip\resume.json</summary>
public static class ResumeStore
{
    private const int Max = 300;
    private sealed record Entry(string Path, double Pos);

    private static readonly string PathJson = AppPaths.DataFile("resume.json");

    private static List<Entry>? _cache; // 5초마다 저장하므로 읽기는 메모리에서 한다

    private static List<Entry> Load()
    {
        if (_cache != null) return _cache;
        try
        {
            _cache = (JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(PathJson)) ?? new())
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Path) && double.IsFinite(e.Pos) && e.Pos > 0).ToList();
        }
        catch { _cache = new(); }
        return _cache;
    }

    private static string Key(string path) => path.Trim();

    public static double? Get(string path)
    {
        var e = Load().FirstOrDefault(x => string.Equals(x.Path, Key(path), StringComparison.OrdinalIgnoreCase));
        return e?.Pos;
    }

    public static void Set(string path, double pos)
    {
        var list = Load();
        list.RemoveAll(x => string.Equals(x.Path, Key(path), StringComparison.OrdinalIgnoreCase));
        list.Insert(0, new Entry(Key(path), Math.Round(pos, 1)));
        if (list.Count > Max) list.RemoveRange(Max, list.Count - Max);
        Save(list);
    }

    public static void Remove(string path)
    {
        var list = Load();
        if (list.RemoveAll(x => string.Equals(x.Path, Key(path), StringComparison.OrdinalIgnoreCase)) > 0) Save(list);
    }

    public static void Clear() { _cache = new(); Save(_cache); }

    private static void Save(List<Entry> list)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathJson)!);
            File.WriteAllText(PathJson, JsonSerializer.Serialize(list));
        }
        catch { /* 저장 실패는 재생에 영향 없음 */ }
    }
}
