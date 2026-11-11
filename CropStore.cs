using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HybridPipPlayer;

/// <summary>Zoom is a multiplier; pan is a fraction of the scaled video, never pixels.</summary>
public sealed record CropValues(double Zoom = 1, double PanX = 0, double PanY = 0)
{
    public static CropValues Full => new();
    [JsonIgnore] public double PanLimit => (1 - 1 / Zoom) / 2;
    public CropValues Normalize()
    {
        double zoom = Math.Clamp(double.IsFinite(Zoom) ? Zoom : 1, 1, 4);
        double limit = (1 - 1 / zoom) / 2;
        return new(zoom, Math.Clamp(double.IsFinite(PanX) ? PanX : 0, -limit, limit),
            Math.Clamp(double.IsFinite(PanY) ? PanY : 0, -limit, limit));
    }
    [JsonIgnore] public double MpvZoom => Math.Log2(Normalize().Zoom);
}

public sealed record CropPreset(string Name, CropValues Values);

public sealed class CropStore
{
    private sealed class Document
    {
        public int Version { get; set; } = 1;
        public List<CropPreset> Presets { get; set; } = new();
    }
    private readonly string _path;
    private List<CropPreset> _presets = new();
    public IReadOnlyList<CropPreset> Presets => _presets.AsReadOnly();
    public string? LoadWarning { get; private set; }
    public CropStore(string path)
    {
        _path = path;
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("영역 파일 크기 초과");
            var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
            if (doc is null || doc.Version != 1 || doc.Presets is null || doc.Presets.Count > 50)
                throw new InvalidDataException("영역 파일 형식 오류");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in doc.Presets)
            {
                if (p is null || p.Values is null || !ValidName(p.Name) || !names.Add(p.Name.Trim()))
                    throw new InvalidDataException("영역 이름 오류");
                _presets.Add(new(p.Name.Trim(), p.Values.Normalize()));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { _presets.Clear(); LoadWarning = "저장된 영역을 읽지 못했습니다: " + ex.Message; }
    }
    public static CropStore LoadDefault() => new(Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "HybridPipPlayer", "crops.json"));

    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name)
        && name.Trim().Length <= 50 && !name.Any(char.IsControl);

    public bool Save(string name, CropValues values, out string error)
    {
        error = "";
        if (!ValidName(name)) { error = "이름을 1~50자로 입력하세요."; return false; }
        name = name.Trim();
        var candidate = _presets.ToList();
        int index = candidate.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 && candidate.Count >= 50) { error = "영역은 최대 50개까지 저장할 수 있습니다."; return false; }
        var preset = new CropPreset(name, values.Normalize());
        if (index >= 0) candidate[index] = preset; else candidate.Add(preset);
        return Persist(candidate, out error);
    }
    public bool Remove(string name, out string error) => Persist(_presets.Where(p =>
        !p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList(), out error);

    private bool Persist(List<CropPreset> values, out string error)
    {
        error = "";
        string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temp, JsonSerializer.Serialize(new Document { Presets = values },
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, true);
            _presets = values; LoadWarning = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { error = "영역 저장 실패: " + ex.Message; return false; }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
