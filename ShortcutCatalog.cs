using System.Windows.Input;

namespace HybridPipPlayer;

public sealed record ShortcutFeature(string Id, string Name, bool Repeat, bool NativeOnly, params string[] DefaultKeys);

public readonly record struct ShortcutChord(Key Key, ModifierKeys Modifiers)
{
    public override string ToString() =>
        ((Modifiers & ModifierKeys.Control) != 0 ? "Ctrl+" : "") +
        ((Modifiers & ModifierKeys.Alt) != 0 ? "Alt+" : "") +
        ((Modifiers & ModifierKeys.Shift) != 0 ? "Shift+" : "") + ShortcutCatalog.KeyName(Key);

    public string MpvKey =>
        ((Modifiers & ModifierKeys.Control) != 0 ? "Ctrl+" : "") +
        ((Modifiers & ModifierKeys.Alt) != 0 ? "Alt+" : "") +
        ((Modifiers & ModifierKeys.Shift) != 0 ? "Shift+" : "") + ShortcutCatalog.NativeKeyName(Key);
}

/// <summary>앱 소유 단축키의 단일 표. mpv 내장 키와 전역 키는 이 편집 표의 범위 밖이다.</summary>
public static class ShortcutCatalog
{
    public static readonly IReadOnlyList<ShortcutFeature> All = new ShortcutFeature[]
    {
        new("hpp-play", "재생 / 일시정지", false, false, "Space"),
        new("hpp-seekb", "뒤로 건너뛰기", true, false, "Left"),
        new("hpp-seekf", "앞으로 건너뛰기", true, false, "Right"),
        new("hpp-seekbl", "길게 뒤로 (6배)", true, false, "Shift+Left"),
        new("hpp-seekfl", "길게 앞으로 (6배)", true, false, "Shift+Right"),
        new("hpp-volup", "볼륨 +5", true, false, "Up"),
        new("hpp-voldn", "볼륨 -5", true, false, "Down"),
        new("hpp-mute", "음소거", false, false, "M"),
        new("hpp-prev", "이전 파일", false, false, "PageUp"),
        new("hpp-next", "다음 파일", false, false, "PageDown"),
        new("hpp-slower", "속도 -0.25", true, false, "["),
        new("hpp-faster", "속도 +0.25", true, false, "]"),
        new("hpp-speed1", "속도 1×", false, false, "Backspace"),
        new("hpp-fback", "한 프레임 뒤", true, false, ","),
        new("hpp-fstep", "한 프레임 앞", true, false, "."),
        new("hpp-shot", "스크린샷", false, false, "S"),
        new("hpp-list", "재생 목록", false, false, "L"),
        new("hpp-fullscreen", "전체 화면", false, false, "F11", "F"),
        new("hpp-escape", "전체 화면 나가기", false, false, "Escape"),
        new("hpp-settings", "설정 열기", false, true, "Ctrl+,"),
        new("hpp-subup", "자막 크기 +0.1", false, true, "Ctrl+Up"),
        new("hpp-subdown", "자막 크기 -0.1", false, true, "Ctrl+Down"),
    };

    public static readonly IReadOnlyList<(string Key, string Name)> Globals = new (string, string)[]
    {
        ("Ctrl+Alt+P", "PIP"), ("Ctrl+Alt+L", "PIP 잠금"),
        ("Ctrl+Alt+C", "클릭 투과"), ("Ctrl+Alt+B", "배경화면"),
        ("Ctrl+Alt+G", "앰비언트"), ("Ctrl+Alt+Up", "자막 크게"), ("Ctrl+Alt+Down", "자막 작게"),
    };

    public static ShortcutFeature? ById(string id) => All.FirstOrDefault(f => f.Id == id);

    private static readonly Dictionary<string, Key> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = Key.Space, ["Left"] = Key.Left, ["Right"] = Key.Right,
        ["Up"] = Key.Up, ["Down"] = Key.Down, ["PageUp"] = Key.PageUp, ["PageDown"] = Key.PageDown,
        ["Backspace"] = Key.Back, ["Back"] = Key.Back, ["Escape"] = Key.Escape, ["Esc"] = Key.Escape,
        ["Enter"] = Key.Return, ["Tab"] = Key.Tab, ["Home"] = Key.Home, ["End"] = Key.End,
        ["Insert"] = Key.Insert, ["Delete"] = Key.Delete,
        ["["] = Key.OemOpenBrackets, ["]"] = Key.OemCloseBrackets, [","] = Key.OemComma,
        ["."] = Key.OemPeriod, [";"] = Key.OemSemicolon, ["'"] = Key.OemQuotes,
        ["/"] = Key.OemQuestion, ["\\"] = Key.OemPipe, ["-"] = Key.OemMinus,
        ["="] = Key.OemPlus, ["`"] = Key.OemTilde,
    };

    public static bool TryParse(string? text, out ShortcutChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 60) return false;
        var parts = text.Trim().Split('+', StringSplitOptions.TrimEntries);
        ModifierKeys mods = ModifierKeys.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var mod = parts[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "alt" => ModifierKeys.Alt, "shift" => ModifierKeys.Shift, _ => ModifierKeys.None,
            };
            if (mod == ModifierKeys.None || (mods & mod) != 0) return false;
            mods |= mod;
        }
        string name = parts[^1];
        Key key;
        if (name.Length == 1 && char.IsAsciiLetter(name[0])) key = Key.A + (char.ToUpperInvariant(name[0]) - 'A');
        else if (name.Length == 1 && char.IsAsciiDigit(name[0])) key = Key.D0 + (name[0] - '0');
        else if (name.StartsWith('F') || name.StartsWith('f'))
        {
            if (!int.TryParse(name[1..], out int n) || n < 1 || n > 24) return false;
            key = Key.F1 + n - 1;
        }
        else if (!Names.TryGetValue(name, out key)) return false;
        chord = new(key, mods);
        return true;
    }

    public static string KeyName(Key key) => key switch
    {
        Key.Back => "Backspace", Key.Return => "Enter", Key.PageUp => "PageUp", Key.PageDown => "PageDown",
        Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]", Key.OemComma => ",", Key.OemPeriod => ".",
        Key.OemSemicolon => ";", Key.OemQuotes => "'", Key.OemQuestion => "/", Key.OemPipe => "\\",
        Key.OemMinus => "-", Key.OemPlus => "=", Key.OemTilde => "`",
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(), _ => key.ToString(),
    };

    public static string NativeKeyName(Key key) => key switch
    {
        Key.Space => "SPACE", Key.Left => "LEFT", Key.Right => "RIGHT", Key.Up => "UP", Key.Down => "DOWN",
        Key.PageUp => "PGUP", Key.PageDown => "PGDWN", Key.Back => "BS", Key.Escape => "ESC",
        Key.Return => "ENTER", Key.Tab => "TAB", Key.Home => "HOME", Key.End => "END",
        Key.Insert => "INS", Key.Delete => "DEL", >= Key.A and <= Key.Z => key.ToString().ToLowerInvariant(),
        _ => KeyName(key),
    };

    public static bool IsTextKey(Key key) => key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9
        or Key.OemOpenBrackets or Key.OemCloseBrackets or Key.OemComma or Key.OemPeriod
        or Key.OemSemicolon or Key.OemQuotes or Key.OemQuestion or Key.OemPipe or Key.OemMinus or Key.OemPlus or Key.OemTilde;

    public static bool TryCapture(Key key, ModifierKeys mods, out ShortcutChord chord)
    {
        chord = default;
        if ((mods & ModifierKeys.Windows) != 0) return false;
        var candidate = new ShortcutChord(key, mods);
        return TryParse(candidate.ToString(), out chord) && chord == candidate;
    }

    public static bool TryParseNative(string text, out ShortcutChord chord)
    {
        // mpv renders Shift+q as Q, and shifted punctuation as its resulting symbol.
        string prefix = "", name = text;
        int separator = text.LastIndexOf('+');
        if (text.EndsWith('+')) separator = text.Length - 2;
        if (separator >= 0) { prefix = text[..(separator + 1)]; name = text[(separator + 1)..]; }
        bool shift = name.Length == 1 && char.IsAsciiLetterUpper(name[0]);
        const string shifted = "!@#$%^&*(){}:<>?\"|_+~";
        const string plain = "1234567890[];,./'\\-=`";
        if (name.Length == 1 && shifted.IndexOf(name[0]) is var index && index >= 0)
        { name = plain[index].ToString(); shift = true; }
        name = name.ToUpperInvariant() switch
        {
            "PGUP" => "PageUp", "PGDWN" => "PageDown", "BS" => "Backspace", "ESC" => "Escape",
            "INS" => "Insert", "DEL" => "Delete", _ => name,
        };
        if (shift && !prefix.Split('+').Contains("Shift", StringComparer.OrdinalIgnoreCase)) prefix += "Shift+";
        return TryParse(prefix + name, out chord);
    }

    public static Key KeyForCharacter(char c)
    {
        c = char.ToLowerInvariant(c);
        // 두벌식 위치 별칭: IME/OSK 입력에도 현재 키 배정을 적용한다.
        const string korean = "ㅁㅠㅊㅇㄷㄹㅎㅗㅑㅓㅏㅣㅡㅜㅐㅔㅂㄱㄴㅅㅕㅍㅈㅌㅛㅋ";
        int index = korean.IndexOf(c);
        if (index >= 0) return Key.A + index;
        c = c switch { 'ㅃ' => 'q', 'ㅉ' => 'w', 'ㄸ' => 'e', 'ㄲ' => 'r', 'ㅆ' => 't', 'ㅒ' => 'o', 'ㅖ' => 'p', _ => c };
        if (c is >= 'a' and <= 'z') return Key.A + c - 'a';
        if (c is >= '0' and <= '9') return Key.D0 + c - '0';
        return Names.TryGetValue(c.ToString(), out var key) ? key : Key.None;
    }

    public static bool UsesLegacyShift(ShortcutFeature feature, ShortcutChord chord) => !feature.NativeOnly
        && chord.Modifiers == ModifierKeys.None && chord.Key is not (Key.Left or Key.Right)
        && feature.DefaultKeys.Any(k => TryParse(k, out var original) && original == chord);

    public static bool IsManagedTextFallback(Key key, ModifierKeys mods) => IsTextKey(key)
        && mods is ModifierKeys.None or ModifierKeys.Shift
        && All.Any(feature => !feature.NativeOnly && feature.DefaultKeys.Any(text =>
            TryParse(text, out var chord) && chord.Key == key && chord.Modifiers == ModifierKeys.None));
}
