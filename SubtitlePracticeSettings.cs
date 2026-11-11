using System.IO;
using System.Text.Json;

namespace HybridPipPlayer;

public sealed class SubtitlePracticeSettings
{
    public bool AutoPause { get; set; }
    public bool Repeat { get; set; }
    public int Passes { get; set; } = 2;
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HybridPipPlayer", "subtitle-practice.json");
    public static SubtitlePracticeSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<SubtitlePracticeSettings>(File.ReadAllText(FilePath)) ?? new();
            settings.Passes = Math.Clamp(settings.Passes, 1, 5);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public bool Save()
    {
        try
        {
            Passes = Math.Clamp(Passes, 1, 5);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
