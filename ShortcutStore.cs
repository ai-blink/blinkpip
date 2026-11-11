using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace HybridPipPlayer;

/// <summary>전체 맵 검증 → changed-only 파일 교체 → 메모리 반영. 저장 실패 시 기존 맵 유지.</summary>
public sealed class ShortcutStore
{
    private sealed class Document
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, string[]> Overrides { get; set; } = new();
    }

    private Dictionary<string, string[]> _overrides = new();
    private readonly string _path;
    public string? LoadWarning { get; private set; }
    public event Action? Changed;

    public ShortcutStore(string path)
    {
        _path = path;
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("파일 크기 초과");
            var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
            if (doc is null || doc.Version != 1 || doc.Overrides is null) throw new InvalidDataException("형식 오류");
            if (!TryValidate(doc.Overrides, out var valid, out string error)) throw new InvalidDataException(error);
            _overrides = valid;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            LoadWarning = "단축키 파일을 읽지 못해 기본값을 사용합니다: " + ex.Message;
        }
    }

    public static ShortcutStore LoadDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HybridPipPlayer", "shortcuts.json"));

    public IReadOnlyList<string> Keys(string id) => _overrides.TryGetValue(id, out var keys)
        ? keys.ToArray() : ShortcutCatalog.ById(id)?.DefaultKeys.ToArray() ?? Array.Empty<string>();
    public bool IsChanged(string id) => _overrides.ContainsKey(id);

    public IEnumerable<(ShortcutFeature Feature, ShortcutChord Chord)> Bindings()
    {
        foreach (var feature in ShortcutCatalog.All)
            foreach (string key in Keys(feature.Id))
                if (ShortcutCatalog.TryParse(key, out var chord)) yield return (feature, chord);
    }

    public ShortcutFeature? Resolve(Key key, ModifierKeys mods, bool nativeFocus = false)
    {
        var chord = new ShortcutChord(key, mods);
        foreach (var (feature, binding) in Bindings())
        {
            if (feature.NativeOnly && !nativeFocus) continue;
            if (binding == chord || ((mods & ModifierKeys.Shift) != 0
                && binding == chord with { Modifiers = mods & ~ModifierKeys.Shift }
                && ShortcutCatalog.UsesLegacyShift(feature, binding))) return feature;
        }
        return null;
    }

    public bool TrySet(string id, IEnumerable<string> keys, out string error)
    {
        var candidate = _overrides.ToDictionary(p => p.Key, p => p.Value.ToArray());
        candidate[id] = keys.ToArray();
        return TrySave(candidate, out error);
    }

    public bool TryReset(string? id, out string error)
    {
        var candidate = _overrides.ToDictionary(p => p.Key, p => p.Value.ToArray());
        if (id == null) candidate.Clear(); else candidate.Remove(id);
        return TrySave(candidate, out error);
    }

    private bool TrySave(Dictionary<string, string[]> candidate, out string error)
    {
        if (!TryValidate(candidate, out var valid, out error)) return false;
        string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temp, JsonSerializer.Serialize(new Document { Overrides = valid }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "단축키 저장 실패: " + ex.Message;
            return false;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        _overrides = valid;
        LoadWarning = null;
        Changed?.Invoke();
        return true;
    }

    public static bool TryValidate(IReadOnlyDictionary<string, string[]> candidate,
        out Dictionary<string, string[]> valid, out string error)
    {
        valid = new();
        error = "";
        foreach (var pair in candidate)
        {
            var feature = ShortcutCatalog.ById(pair.Key);
            if (feature == null || pair.Value == null || pair.Value.Length > 8)
            { error = "알 수 없는 기능 또는 너무 많은 키입니다: " + pair.Key; return false; }
            var normalized = new List<string>();
            foreach (var text in pair.Value)
            {
                if (!ShortcutCatalog.TryParse(text, out var chord))
                { error = "지원하지 않는 키입니다: " + text; return false; }
                string key = chord.ToString();
                if (normalized.Contains(key)) { error = "같은 키가 중복되었습니다: " + key; return false; }
                if (ShortcutCatalog.Globals.Any(g => ShortcutCatalog.TryParse(g.Key, out var reserved) && reserved == chord))
                { error = "변경할 수 없는 전역 단축키입니다: " + key; return false; }
                normalized.Add(key);
            }
            if (!normalized.SequenceEqual(feature.DefaultKeys)) valid[pair.Key] = normalized.ToArray();
        }
        var used = new Dictionary<ShortcutChord, string>();
        foreach (var feature in ShortcutCatalog.All)
        {
            var keys = valid.TryGetValue(feature.Id, out var changed) ? changed : feature.DefaultKeys;
            foreach (string text in keys)
            {
                ShortcutCatalog.TryParse(text, out var chord);
                var coverage = new List<ShortcutChord> { chord };
                if (ShortcutCatalog.UsesLegacyShift(feature, chord)) coverage.Add(chord with { Modifiers = ModifierKeys.Shift });
                foreach (var effective in coverage)
                {
                    if (used.TryGetValue(effective, out string? other))
                    { error = $"{effective}: '{other}'와 '{feature.Name}'의 키가 겹칩니다."; return false; }
                    used.Add(effective, feature.Name);
                }
            }
        }
        return true;
    }
}
