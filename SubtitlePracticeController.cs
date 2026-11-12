using System.Globalization;
using System.Text.Json;

namespace blinkpip;

public static class SubtitleTime
{
    // One year exceeds practical media lengths and keeps formatting/arithmetic well within TimeSpan bounds.
    public const double MaximumSeconds = 366 * 24 * 60 * 60d;
    public static bool Supported(double value) => double.IsFinite(value) && value >= 0 && value <= MaximumSeconds;
    public static string Format(double value)
    {
        if (!Supported(value)) return "?";
        var time = TimeSpan.FromSeconds(value);
        return $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
    }
}

public sealed record SubtitleCue(double Start, double? End, string Text)
{
    public bool CanPractice => SubtitleTime.Supported(Start) && End is { } end && SubtitleTime.Supported(end) && end > Start && !string.IsNullOrWhiteSpace(Text);
    public string Label => $"{SubtitleTime.Format(Start)} → {(End is { } end ? SubtitleTime.Format(end) : "?")}   {Text.Replace('\n', ' ')}";
}

public static class SubtitleLineParser
{
    public static List<SubtitleCue> Parse(string? json)
    {
        var result = new List<SubtitleCue>();
        if (string.IsNullOrWhiteSpace(json) || json.Length > 8 * 1024 * 1024) return result;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var line in document.RootElement.EnumerateArray())
            {
                if (result.Count >= 10000) break;
                if (line.ValueKind != JsonValueKind.Object || !line.TryGetProperty("start", out var start) ||
                    start.ValueKind != JsonValueKind.Number || !start.TryGetDouble(out double seconds) || !SubtitleTime.Supported(seconds)) continue;
                if (!line.TryGetProperty("text", out var textValue) || textValue.ValueKind != JsonValueKind.String) continue;
                string text = textValue.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (text.Length > 4000) text = text[..4000] + "…";
                double? end = line.TryGetProperty("end", out var endValue) && endValue.ValueKind == JsonValueKind.Number &&
                    endValue.TryGetDouble(out double value) && SubtitleTime.Supported(value) ? value : null;
                result.Add(new(seconds, end, text));
            }
        }
        catch (JsonException) { }
        return result;
    }
}

public readonly record struct SubtitlePracticeAction(double? Seek, bool Pause, bool Resume, long Generation = 0)
{
    public static SubtitlePracticeAction None => default;
}

/// <summary>Pure, lock-protected cue state. Repeat count includes the first pass and pauses after the final pass.</summary>
public sealed class SubtitlePracticeController
{
    private readonly object _gate = new();
    private SubtitleCue? _target, _completedCue;
    private bool _autoPause, _repeat, _engineActive, _awaitingSeek;
    private int _passes = 1, _completedPasses;
    private double _delay;
    private long _generation;
    private string? _primaryId;

    public void Configure(bool autoPause, bool repeat, int passes)
    {
        lock (_gate)
        {
            _autoPause = autoPause; _repeat = repeat; _passes = Math.Clamp(passes, 1, 5);
            ResetCore();
        }
    }

    public void SetEngine(bool active)
    {
        lock (_gate) { _engineActive = active; ResetCore(); }
    }

    // Compare actual current context, not the arrival of an asynchronous notification.
    public bool UpdateContext(string? primaryId, double delay)
    {
        lock (_gate)
        {
            if (_primaryId == primaryId && Math.Abs(_delay - delay) < .001) return false;
            _primaryId = primaryId; _delay = delay; ResetCore(); return true;
        }
    }

    public void Reset()
    {
        lock (_gate) ResetCore();
    }

    private void ResetCore()
    {
        _generation++;
        _target = null; _completedCue = null; _awaitingSeek = false; _completedPasses = 0;
    }

    private static bool Same(SubtitleCue? a, SubtitleCue? b) => a != null && b != null &&
        Math.Abs(a.Start - b.Start) < .001 && a.End == b.End && a.Text == b.Text;

    private static bool Playable(SubtitleCue cue, double delay) => cue.CanPractice && double.IsFinite(delay)
        && SubtitleTime.Supported(Math.Max(0, cue.Start + delay)) && cue.End is { } end
        && SubtitleTime.Supported(end + delay) && end + delay > Math.Max(0, cue.Start + delay);

    // Called only after the matching internal SEEK has reached PLAYBACK_RESTART.
    public void AcknowledgeSeek()
    {
        lock (_gate) { if (_target != null) _awaitingSeek = false; }
    }

    public SubtitlePracticeAction Replay(SubtitleCue cue, double delay)
    {
        lock (_gate)
        {
            if (!_engineActive || !Playable(cue, delay)) return default;
            ResetCore();
            _delay = delay; _target = cue; _completedCue = null; _completedPasses = 0; _awaitingSeek = true;
            return new(Math.Max(0, cue.Start + delay), false, true, _generation);
        }
    }

    // Serialize the final validity check and mpv action with resets/new-media changes.
    public void ApplyIfCurrent(SubtitlePracticeAction action, Action<SubtitlePracticeAction> apply)
    {
        lock (_gate)
        {
            if (_engineActive && action != default && action.Generation == _generation) apply(action);
        }
    }

    public SubtitlePracticeAction Tick(double position, bool paused, SubtitleCue? cue, double delay)
    {
        lock (_gate)
        {
            if (!_engineActive || !double.IsFinite(position) || !double.IsFinite(delay)) return default;
            if (Math.Abs(delay - _delay) > .001) { ResetCore(); _delay = delay; }
            if (_target != null && _awaitingSeek) return default;
            // Process the retained cue BEFORE accepting a new cue. Some subtitles vanish at their exact end.
            if (_target?.End is { } end && !paused && !_awaitingSeek && position >= end + _delay - .015)
            {
                _completedPasses++;
                if (_repeat && _completedPasses < _passes)
                {
                    _awaitingSeek = true;
                    return new(Math.Max(0, _target.Start + _delay), false, true, _generation);
                }
                _completedCue = _target; _target = null;
                return new(null, true, false, _generation);
            }
            if (_target == null && (_autoPause || _repeat) && cue != null && Playable(cue, delay) && !Same(cue, _completedCue))
            {
                // Do not catch an expired cue on a stale property update after a seek.
                if (position >= Math.Max(0, cue.Start + delay) - .1 && position < cue.End!.Value + delay - .015)
                { _target = cue; _completedPasses = 0; }
            }
            return default;
        }
    }
}

public static class SubtitleSelectionGuard
{
    public static string TrackKey(string? primary, IEnumerable<(int Id, string Label)> tracks) =>
        JsonSerializer.Serialize(new { primary, tracks = tracks.Select(t => new { t.Id, t.Label }) });
    public static bool CanSelect(long shownSerial, long currentSerial, bool loaded, string? primary, int? requested, IEnumerable<int> currentIds) =>
        loaded && shownSerial == currentSerial && (requested == null || requested.Value.ToString(CultureInfo.InvariantCulture) != primary && currentIds.Contains(requested.Value));
}
