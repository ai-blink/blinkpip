using System.Text.Json;

namespace HybridPipPlayer;

public partial class MpvPlayer
{
    private readonly HashSet<string> _defaultShortcutKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _previousCustomCommands = new(StringComparer.Ordinal);

    public void StepShortcutSubtitleScale(int direction)
    {
        Command("add", "sub-scale", direction > 0 ? "0.1" : "-0.1");
        Command("show-text", "자막 크기 " + (GetProperty("sub-scale") ?? "?"));
    }

    private void InitializeDefaultShortcuts()
    {
        foreach (var feature in ShortcutCatalog.All)
            foreach (string text in feature.DefaultKeys)
                if (ShortcutCatalog.TryParse(text, out var chord))
                {
                    _defaultShortcutKeys.Add(chord.MpvKey);
                    BindShortcut(feature, chord);
                }
    }

    public void ApplyShortcuts(ShortcutStore shortcuts)
    {
        var bindings = shortcuts.Bindings().ToList();
        var current = bindings.Select(b => b.Chord.MpvKey).ToHashSet(StringComparer.Ordinal);
        // Nondefault custom keys return to their captured underlying binding on remove/reset,
        // matching restart behavior without persisting edit history.
        foreach (string key in _previousCustomCommands.Keys.Where(k => !current.Contains(k)).ToArray())
        {
            Command("keybind", key, _previousCustomCommands[key]);
            _previousCustomCommands.Remove(key);
        }
        foreach (var (_, chord) in bindings)
            if (!_defaultShortcutKeys.Contains(chord.MpvKey) && !_previousCustomCommands.ContainsKey(chord.MpvKey))
                _previousCustomCommands[chord.MpvKey] = UnderlyingShortcutCommand(GetProperty("input-bindings"), chord);
        // Vacated managed defaults must not fall back to mpv's default play/seek bindings.
        foreach (string key in _defaultShortcutKeys) Command("keybind", key, "ignore");
        foreach (var (feature, chord) in bindings)
        {
            BindShortcut(feature, chord);
        }
    }

    internal static string UnderlyingShortcutCommand(string? json, ShortcutChord chord)
    {
        if (string.IsNullOrEmpty(json)) return "ignore";
        try
        {
            using var document = JsonDocument.Parse(json);
            string command = "ignore";
            long highest = -1;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("key", out var key) || key.GetString() is not { } text
                    || !ShortcutCatalog.TryParseNative(text, out var normalized) || normalized != chord
                    || !item.TryGetProperty("priority", out var priority) || !priority.TryGetInt64(out long rank)
                    || rank < 0 || rank < highest || !item.TryGetProperty("cmd", out var cmd)) continue;
                command = cmd.GetString() ?? "ignore";
                highest = rank;
            }
            return command;
        }
        catch (JsonException) { return "ignore"; }
    }

    private void BindShortcut(ShortcutFeature feature, ShortcutChord chord)
    {
        string command = feature.Id switch
        {
            "hpp-subup" => "add sub-scale 0.1 ; show-text \"자막 크기 ${sub-scale}\"",
            "hpp-subdown" => "add sub-scale -0.1 ; show-text \"자막 크기 ${sub-scale}\"",
            _ => (feature.Repeat ? "repeatable " : "") + "script-message " + feature.Id,
        };
        Command("keybind", chord.MpvKey, command);
    }
}
