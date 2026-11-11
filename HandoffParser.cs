using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HybridPipPlayer;

public sealed record HandoffRequest(string Url, double? Time = null, string? Subtitle = null, double? Speed = null, bool? Pip = null);

public static class HandoffParser
{
    public const int MaximumBytes = 16384;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal) { "v", "url", "t", "sub", "speed", "pip" };
    private static readonly HashSet<string> Secrets = new(StringComparer.Ordinal) {
        "auth", "authorization", "authtoken", "token", "accesstoken", "idtoken", "refreshtoken", "password", "passwd", "pwd",
        "cookie", "session", "sessionid", "jwt", "bearer", "secret", "clientsecret", "apikey", "accesskey", "credential", "key",
        "code", "state", "signature", "sig", "signed", "policy", "keypairid", "ossaccesskeyid", "hdnts", "hdnea" };
    public static bool IsProtocol(string raw) => raw.TrimStart().StartsWith("hybridpip:", StringComparison.OrdinalIgnoreCase);
    public static bool SafeExternalArgument(string raw) => !string.IsNullOrWhiteSpace(raw) && Encoding.UTF8.GetByteCount(raw) <= MaximumBytes && !raw.Any(char.IsControl);
    private static bool BadCharacters(string raw) => raw.Any(c => char.IsControl(c) || c == '\\');
    private static bool ValidEscapes(string value) => !Regex.IsMatch(value, "%(?![0-9a-fA-F]{2})");
    private static string Decode(string value)
    {
        if (!ValidEscapes(value)) throw new FormatException();
        using var bytes = new System.IO.MemoryStream();
        for (int i = 0; i < value.Length;)
        {
            if (value[i] == '%') { bytes.WriteByte(byte.Parse(value.AsSpan(i + 1, 2), NumberStyles.HexNumber)); i += 3; }
            else
            {
                int next = value.IndexOf('%', i); if (next < 0) next = value.Length;
                var data = StrictUtf8.GetBytes(value[i..next].Replace('+', ' ')); bytes.Write(data); i = next;
            }
        }
        return StrictUtf8.GetString(bytes.ToArray());
    }
    public static bool TryPublicUrl(string? raw, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(raw) || raw != raw.Trim() || Encoding.UTF8.GetByteCount(raw) > 4096 || BadCharacters(raw) || raw.Contains('#') || !ValidEscapes(raw)) return false;
        try
        {
            if (BadCharacters(Decode(raw))) return false;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0) return false;
            // Also reject empty userinfo (https://@host), which Uri normalizes away.
            int authorityStart = raw.IndexOf("://", StringComparison.Ordinal); if (authorityStart < 0) return false;
            string authority = raw[(authorityStart + 3)..].Split('/', '?')[0]; if (authority.Contains('@')) return false;
            foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var queryParts = pair.Split('=', 2);
                string name = Decode(queryParts[0]);
                string queryValue = queryParts.Length == 2 ? Decode(queryParts[1]).TrimStart() : "";
                if (queryValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(queryValue, @"^eyJ[A-Za-z0-9_-]*\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$")) return false;
                if (name.Contains('%') || BadCharacters(name)) return false;
                string normalized = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]", "");
                if (Secrets.Contains(normalized) || normalized == "credentials" || normalized.StartsWith("auth") || normalized.StartsWith("cookie") || normalized.StartsWith("session") || normalized.EndsWith("token") || normalized.StartsWith("xamz") || normalized.StartsWith("xgoog") || normalized.StartsWith("xoss")) return false;
            }
            url = uri.AbsoluteUri;
            return Encoding.UTF8.GetByteCount(url) <= 4096;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { return false; }
    }
    private static bool Numeric(string text, double min, double max, out double number)
    {
        number = 0;
        return Regex.IsMatch(text, @"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$") && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number) && number >= min && number <= max;
    }
    public static bool TryParseProtocol(string raw, out HandoffRequest? request, out string error)
    {
        request = null; error = "앱으로 넘긴 주소 형식이 올바르지 않습니다.";
        string prefix = raw.StartsWith("hybridpip://play/?", StringComparison.OrdinalIgnoreCase) ? "hybridpip://play/?" : "hybridpip://play?";
        if (!SafeExternalArgument(raw) || raw != raw.Trim() || BadCharacters(raw) || raw.Contains('#') || raw.Contains('"') || !raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string pair in raw[prefix.Length..].Split('&'))
            {
                var parts = pair.Split('=', 2); if (parts.Length != 2) return false;
                string key = Decode(parts[0]), value = Decode(parts[1]);
                if (!Fields.Contains(key) || !values.TryAdd(key, value)) return false;
            }
            if (!values.TryGetValue("v", out string? version) || version != "1" || !values.TryGetValue("url", out string? rawUrl) || !TryPublicUrl(rawUrl, out string url)) return false;
            double? time = null, speed = null; string? sub = null; bool? pip = null;
            if (values.TryGetValue("t", out string? t)) { if (!Numeric(t, 0, 31536000, out double n)) return false; time = n; }
            if (values.TryGetValue("speed", out string? s)) { if (!Numeric(s, .25, 4, out double n)) return false; speed = n; }
            if (values.TryGetValue("sub", out string? subtitle)) { if (!TryPublicUrl(subtitle, out string validSub)) return false; sub = validSub; }
            if (values.TryGetValue("pip", out string? p)) { if (p is not ("0" or "1")) return false; pip = p == "1"; }
            request = new(url, time, sub, speed, pip); error = ""; return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { return false; }
    }
    // Typed shared-contract checks also serve consumers that construct a request before protocol encoding.
    public static bool TryParseJson(string raw, out HandoffRequest? request)
    {
        request = null;
        if (Encoding.UTF8.GetByteCount(raw) > MaximumBytes) return false;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in doc.RootElement.EnumerateObject())
                if (!Fields.Contains(property.Name) || !values.TryAdd(property.Name, property.Value)) return false;
            if (!values.TryGetValue("v", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int v) || v != 1 || !values.TryGetValue("url", out var url) || url.ValueKind != JsonValueKind.String || !TryPublicUrl(url.GetString(), out string target)) return false;
            double? time = null, speed = null; string? sub = null; bool? pip = null;
            if (values.TryGetValue("t", out var t)) { if (t.ValueKind != JsonValueKind.Number || !t.TryGetDouble(out double n) || !double.IsFinite(n) || n < 0 || n > 31536000) return false; time = n; }
            if (values.TryGetValue("speed", out var s)) { if (s.ValueKind != JsonValueKind.Number || !s.TryGetDouble(out double n) || !double.IsFinite(n) || n < .25 || n > 4) return false; speed = n; }
            if (values.TryGetValue("sub", out var st)) { if (st.ValueKind != JsonValueKind.String || !TryPublicUrl(st.GetString(), out string valid)) return false; sub = valid; }
            if (values.TryGetValue("pip", out var p)) { if (p.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false; pip = p.GetBoolean(); }
            request = new(target, time, sub, speed, pip); return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException) { return false; }
    }
}
