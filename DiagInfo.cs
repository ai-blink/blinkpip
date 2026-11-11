using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace HybridPipPlayer;

/// <summary>Thread-safe, bounded, privacy-filtered playback diagnostics. Never stores raw URLs or logs.</summary>
public sealed class DiagInfo : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<string> _messages = new();
    private readonly CancellationTokenSource _lifetime = new();
    private string _engine = "선택 전", _stage = "대기", _source = "없음", _failure = "없음";
    private string _mpvVersion = "초기화 전", _ytdlpVersion = "확인 중", _webVersion = "초기화 전";
    private bool _disposed, _nativeActive;
    public event Action? Changed;
    public event Action<string>? Failed;

    public void Begin(string source, bool web)
    {
        lock (_gate)
        {
            _engine = web ? "B · WebView2" : "A · libmpv";
            _nativeActive = !web;
            _source = Redact(source);
            _stage = "엔진 선택";
            _failure = "없음";
            _messages.Clear();
        }
        Notify();
    }

    public void SetEngineVersion(string version, bool web = false)
    {
        lock (_gate) { if (web) _webVersion = Redact(version); else _mpvVersion = Redact(version); }
        Notify();
    }

    public void OpeningWeb()
    {
        lock (_gate) _stage = "페이지 열기 · 보호 영상 재생 시작 및 자막 읽기: 확인 필요";
        Notify();
    }

    public void WebNavigation(bool success, string error)
    {
        lock (_gate) { if (_nativeActive || _disposed) return; }
        if (!success) { Fail("페이지 열기", error); return; }
        lock (_gate) _stage = "페이지 로드 완료 · 보호 영상 재생 시작 및 자막 읽기: 확인 필요";
        Notify();
    }

    public void NativeStarted(string path)
    {
        lock (_gate)
        {
            if (!_nativeActive || _disposed) return;
            _source = Redact(path);
            _stage = Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                ? "주소 풀기 / 미디어 확인" : "파일 열기";
            _failure = "없음";
            _messages.Clear();
        }
        Notify();
    }

    public void NativeLog(string prefix, string level, string message)
    {
        lock (_gate)
        {
            if (!_nativeActive || _disposed) return;
            bool resolver = prefix.Contains("ytdl", StringComparison.OrdinalIgnoreCase);
            if (resolver && _failure == "없음" && (_stage is "엔진 선택" or "주소 풀기 / 미디어 확인"))
                _stage = "주소 풀기 (yt-dlp)";
            if (level is "warn" or "error" or "fatal")
            {
                _messages.Enqueue(Redact($"[{level}] {prefix}: {message}"));
                while (_messages.Count > 12) _messages.Dequeue();
            }
            else if (!resolver) return;
        }
        Notify();
    }

    public void NativeLoaded()
    {
        lock (_gate) { if (!_nativeActive || _disposed) return; _stage = "열기 완료 · 재생 시작 대기"; }
        Notify();
    }

    public void NativePlaying(bool paused)
    {
        lock (_gate)
        {
            if (!_nativeActive || _disposed || _failure != "없음") return;
            _stage = paused ? "재생 준비 완료 (일시정지)" : "재생 시작 확인";
        }
        Notify();
    }

    public void NativeEnded(int reason, string error)
    {
        string stage;
        lock (_gate) { if (!_nativeActive || _disposed) return; stage = _stage; }
        if (reason == 4) { Fail(stage, error); return; }
        // Redirect is followed by another start-file; do not label it as a failure.
        lock (_gate) _stage = reason switch { 0 => "재생 종료 (EOF)", 2 => "중지 / 파일 전환", 3 => "종료", 5 => "주소 전환", _ => $"재생 종료 ({reason})" };
        Notify();
    }

    public void Fail(string stage, string error)
    {
        string summary = Redact($"{stage}: {error}");
        lock (_gate)
        {
            if (_disposed) return;
            _stage = stage + " · 실패";
            _failure = summary;
        }
        Notify();
        Failed?.Invoke(summary);
    }

    public string Snapshot()
    {
        lock (_gate)
        {
            var text = new StringBuilder();
            text.AppendLine($"Hybrid PIP Player {typeof(DiagInfo).Assembly.GetName().Version?.ToString(3)}");
            text.AppendLine($"엔진: {_engine}\n단계: {_stage}\n대상: {_source}\n최근 실패: {_failure}");
            text.AppendLine($"libmpv: {_mpvVersion}\nyt-dlp: {_ytdlpVersion}\nWebView2: {_webVersion}");
            text.AppendLine("흐름: 엔진 선택 → 주소 풀기(웹 주소) → 열기 → 재생 시작");
            text.AppendLine("OTT 보호 영상 시작 감지·자막 읽기: 확인 필요");
            text.AppendLine("설정: %LOCALAPPDATA%\\HybridPipPlayer\\settings.json");
            if (_messages.Count > 0) text.AppendLine("최근 경고 / 오류:\n" + string.Join("\n", _messages));
            return text.ToString().TrimEnd();
        }
    }

    public async Task ProbeYtdlpAsync()
    {
        string result;
        try
        {
            string? path = FindYtdlp();
            if (path == null) result = "찾지 못함 (앱 폴더 / PATH 확인)";
            else
            {
                using var process = new Process { StartInfo = new ProcessStartInfo(path, "--version")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                process.Start();
                // Drain both streams before waiting; only a validated version string is retained.
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    string version = (await stdout.ConfigureAwait(false)).Trim();
                    await stderr.ConfigureAwait(false);
                    result = process.ExitCode == 0 && Regex.IsMatch(version, @"^\d{4}\.\d{2}\.\d{2}(?:[.\w+-]{0,60})?$")
                        ? version + " (앱 폴더 / PATH 탐색 결과)" : "버전 확인 실패";
                }
                catch (OperationCanceledException)
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                    try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); } catch { }
                    result = "버전 확인 시간 초과";
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { result = "버전 확인 실패 (실행 파일 / 접근 권한 확인)"; }
        lock (_gate) { if (_disposed) return; _ytdlpVersion = result; }
        Notify();
    }

    private static string? FindYtdlp()
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
        if (File.Exists(bundled)) return bundled;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try { var candidate = Path.Combine(dir.Trim().Trim('"'), "yt-dlp.exe"); if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate)) return candidate; }
            catch (ArgumentException) { }
        }
        return null;
    }

    /// <summary>Privacy first: retain URL hosts only, never credentials, query values, fragments or local paths.</summary>
    public static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value)) return "없음";
        // Never echo header/cookie dumps, including unrecognised cookie names and spaced bearer values.
        if (Regex.IsMatch(value, @"(?i)\b(cookie|authorization|password|passwd|secret|token|access[_-]?token|refresh[_-]?token|signature|sig|session|username|login|api[_-]?key)\b"))
            return "[인증 관련 내용 가림]";
        // Apostrophes are legal URL path characters; stopping there would expose the remaining path.
        string clean = Regex.Replace(value, @"(?i)\b(?:https?|ftp)://[^\s<>""]+", m =>
            Uri.TryCreate(m.Value, UriKind.Absolute, out var u) ? $"{u.Scheme}://{u.Host}/[주소 가림]" : "[주소 가림]");
        clean = Regex.Replace(clean, @"(?i)(?:file:///|(?<![A-Za-z0-9])[a-z]:[\\/]|\\\\)[^\r\n""<>|]*", "[로컬 경로 가림]");
        clean = Regex.Replace(clean, @"[?#][^\s]*", "[주소 값 가림]");
        clean = Regex.Replace(clean, @"(?i)\b(?:sig(?:nature)?|token|key|jwt)\s*[:=]\s*\S+", "[인증 값 가림]");
        clean = Regex.Replace(clean, @"\b[A-Za-z0-9_+/=-]{40,}\b", "[긴 값 가림]");
        clean = clean.Replace('\r', ' ').Replace('\n', ' ');
        return clean.Length > 500 ? clean[..500] + "…" : clean;
    }

    private void Notify() { lock (_gate) { if (_disposed) return; } Changed?.Invoke(); }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lifetime.Cancel();
        // The running probe owns its linked source until it has completed.
        Changed = null;
        Failed = null;
    }
}
