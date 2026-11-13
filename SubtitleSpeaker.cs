using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SherpaOnnx;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace blinkpip;

/// <summary>
/// 자막 한 줄을 소리로 읽는다. 목소리 id 가 "st:<화자>" 면 Supertonic 3(sherpa-onnx, CPU), 그 밖이면 윈도우 음성 합성.
/// Supertonic 모델이 없으면 윈도우 기본 목소리로 대신 읽는다.
/// 읽던 줄은 끝까지 읽고, 그동안 온 줄은 가장 최근 것 하나만 빠르게 이어 읽는다(아래 «줄 차례»).
/// </summary>
public sealed class SubtitleSpeaker : IDisposable
{
    // Supertonic 3 int8(sherpa-onnx 변환본). 모델 라이선스가 OpenRAIL-M 이라 앱 zip 에 넣지 않고 처음 쓸 때 받는다
    public const string ModelName = "sherpa-onnx-supertonic-3-tts-int8-2026-05-11";
    private const string ModelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/" + ModelName + ".tar.bz2";
    public static string ModelRoot => AppPaths.DataFile("tts");
    public static string ModelDir => Path.Combine(ModelRoot, ModelName);
    private static readonly string[] ModelFiles =
        { "duration_predictor.int8.onnx", "text_encoder.int8.onnx", "vector_estimator.int8.onnx", "vocoder.int8.onnx", "tts.json", "unicode_indexer.bin", "voice.bin" };
    public static bool ModelReady => ModelFiles.All(f => File.Exists(Path.Combine(ModelDir, f)));

    // 사용자가 2026-11-14 비교 청취로 고른 화자(화자 번호 0~9 중)
    public static readonly (string Id, string Name)[] SupertonicVoices =
        { ("st:0", "여자 1"), ("st:1", "여자 2"), ("st:5", "남자 1"), ("st:6", "남자 2") };
    public const string DefaultVoice = "st:0";

    private readonly SpeechSynthesizer _synth = new();
    private readonly MediaPlayer _player = new();
    private readonly object _stLock = new(); // OfflineTts 는 한 번에 한 줄만 합성한다
    private OfflineTts? _st;
    private int _gen;          // Stop 하면 올라가서 진행 중인 합성·차례를 모두 버린다
    private string _last = ""; // 같은 줄을 되풀이해 읽지 않게
    private string _voice = DefaultVoice;
    private double _rate = 1, _pitch = 1, _volume = 1;

    public SubtitleSpeaker()
    {
        // 재생 스레드에서 온다. 기다리는 Pump 는 UI 스레드에서 이어 간다
        _player.MediaEnded += (_, _) => _playDone?.TrySetResult();
        _player.MediaFailed += (_, _) => _playDone?.TrySetResult();
    }

    public static IReadOnlyList<string> WindowsVoiceNames() => SpeechSynthesizer.AllVoices.Select(v => v.DisplayName).ToList();

    public static string VoiceLabel(string id) =>
        SupertonicVoices.FirstOrDefault(v => v.Id == id).Name ?? (id.Length == 0 ? "윈도우 기본" : id);

    /// <param name="rate">말 속도 배율</param><param name="pitch">높낮이 배율(1 = 원래)</param><param name="volume">크기(1 = 100%)</param>
    public void Configure(string voice, double rate, double pitch, double volume)
    {
        _voice = voice;
        _rate = rate; _pitch = pitch; _volume = volume;
        var v = SpeechSynthesizer.AllVoices.FirstOrDefault(x => x.DisplayName == voice);
        if (v != null && _synth.Voice?.Id != v.Id) _synth.Voice = v;
        _synth.Options.SpeakingRate = Math.Clamp(rate, 0.5, 3.0);
        _synth.Options.AudioPitch = Math.Clamp(pitch, 0.0, 2.0);
        _synth.Options.AudioVolume = Math.Clamp(volume, 0.0, 1.0); // 윈도우 목소리는 100% 가 최대
        _last = ""; // 설정을 바꾼 뒤 같은 줄이 다시 오면 새 설정으로 읽는다
    }

    // ── 줄 차례 ──
    // 읽던 줄은 끝까지 읽는다. 읽는 동안 온 다음 줄은 바로 합성해 두었다가(밀린 줄은 CatchUp 배 빠르게) 앞 줄이 끝나면 이어 읽는다.
    // 그사이 또 바뀌면 가장 최근 줄만 남긴다 — 영상과 한 줄 이상 벌어지지 않게. 모든 상태는 UI 스레드에서만 만진다.
    private const double CatchUp = 1.25;
    private Task<MediaSource?>? _next;   // 다음에 읽을 줄(합성 중이거나 끝남)
    private bool _pumping;
    private TaskCompletionSource? _playDone;

    public void Speak(string? text)
    {
        text = Clean(text);
        if (text.Length == 0) { _last = ""; return; } // 자막이 사라져도 읽던 말은 끝까지
        if (text == _last) return;
        _last = text;
        _next = SynthesizeAsync(text, _pumping ? CatchUp : 1.0, _gen);
        if (!_pumping) Pump(_gen);
    }

    private async void Pump(int g)
    {
        _pumping = true;
        try
        {
            while (_next != null && g == _gen)
            {
                var job = _next;
                var src = await job;
                if (g != _gen) break;
                if (_next != job) continue; // 합성하는 동안 더 새 줄이 왔다 — 이 줄은 늦었으니 건너뛴다
                _next = null;
                if (src == null) continue;
                _playDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _player.Source = src;
                _player.Play();
                await _playDone.Task;
            }
        }
        catch (Exception ex) { Debug.WriteLine("자막 읽기 실패: " + ex.Message); }
        finally { if (g == _gen) _pumping = false; }
    }

    private async Task<MediaSource?> SynthesizeAsync(string text, double boost, int g)
    {
        try
        {
            if (_voice.StartsWith("st:", StringComparison.Ordinal) && int.TryParse(_voice.AsSpan(3), out int sid) && ModelReady)
            {
                byte[]? wav = await Task.Run(() => SynthesizeSupertonic(text, sid, boost, g));
                return wav == null ? null : MediaSource.CreateFromStream(new MemoryStream(wav).AsRandomAccessStream(), "audio/wav");
            }
            _synth.Options.SpeakingRate = Math.Clamp(_rate * boost, 0.5, 6.0);
            var stream = await _synth.SynthesizeTextToStreamAsync(text);
            return MediaSource.CreateFromStream(stream, stream.ContentType);
        }
        catch (Exception ex) { Debug.WriteLine("자막 합성 실패: " + ex.Message); return null; } // 이 줄만 건너뛴다
    }

    // 높낮이는 재생 표본율을 pitch 배로 바꿔서 올리고 내린다. 그러면 빠르기도 pitch 배가 되므로 합성 속도를 rate/pitch 로 미리 맞춘다
    private byte[]? SynthesizeSupertonic(string text, int sid, double boost, int g)
    {
        lock (_stLock)
        {
            if (g != _gen) return null;
            _st ??= LoadSupertonic();
            var cfg = new OfflineTtsGenerationConfig
            {
                Sid = sid,
                NumSteps = 8,
                Speed = (float)Math.Clamp(_rate * boost / _pitch, 0.5, 4.0),
            };
            cfg.Extra["lang"] = Lang(text);
            // 멈추면(Stop) 합성 도중에 그만둔다(0 = 멈춤)
            var audio = _st.GenerateWithConfig(text, cfg, (IntPtr _, int _, float _, IntPtr _) => g == _gen ? 1 : 0);
            try
            {
                if (g != _gen) return null;
                return ToWav(audio.Samples, (int)Math.Round(audio.SampleRate * _pitch), (float)_volume);
            }
            finally { audio.Dispose(); } // 네이티브 버퍼를 놓는다(IDisposable 을 선언하지 않았지만 Dispose 가 있다)
        }
    }

    private static OfflineTts LoadSupertonic()
    {
        var c = new OfflineTtsConfig();
        string d = ModelDir;
        c.Model.Supertonic.DurationPredictor = Path.Combine(d, "duration_predictor.int8.onnx");
        c.Model.Supertonic.TextEncoder = Path.Combine(d, "text_encoder.int8.onnx");
        c.Model.Supertonic.VectorEstimator = Path.Combine(d, "vector_estimator.int8.onnx");
        c.Model.Supertonic.Vocoder = Path.Combine(d, "vocoder.int8.onnx");
        c.Model.Supertonic.TtsJson = Path.Combine(d, "tts.json");
        c.Model.Supertonic.UnicodeIndexer = Path.Combine(d, "unicode_indexer.bin");
        c.Model.Supertonic.VoiceStyle = Path.Combine(d, "voice.bin");
        c.Model.NumThreads = 4;
        c.Model.Provider = "cpu";
        return new OfflineTts(c);
    }

    // 한글이 있으면 ko, 가나가 있으면 ja, 그 밖은 en (Supertonic 3 는 언어를 알려 줘야 발음이 맞는다)
    private static string Lang(string s)
    {
        if (s.Any(ch => ch is >= '가' and <= '힣' or >= 'ㄱ' and <= 'ㆎ')) return "ko";
        if (s.Any(ch => ch is >= '぀' and <= 'ヿ')) return "ja";
        return "en";
    }

    // 16비트 PCM 한 채널 WAV. 크기 배율을 곱하고 넘치는 값은 자른다
    private static byte[] ToWav(float[] samples, int sampleRate, float gain)
    {
        var ms = new MemoryStream(44 + samples.Length * 2);
        using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8); w.Write(36 + samples.Length * 2); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(samples.Length * 2);
            foreach (float f in samples) w.Write((short)Math.Clamp(f * gain * 32767f, -32768f, 32767f));
        }
        return ms.ToArray();
    }

    public void Stop()
    {
        _gen++;
        _last = "";
        _next = null;
        _pumping = false;
        _player.Pause();
        _playDone?.TrySetResult();
    }

    // 줄바꿈은 띄어쓰기로, 대화 표시 '-' 와 음악 기호는 뺀다
    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var lines = text.Replace("\r", "").Split('\n').Select(l => l.Trim().TrimStart('-', '‐', '–').Trim());
        string s = string.Join(" ", lines.Where(l => l.Length > 0)).Replace("♪", "").Trim();
        return s.Length > 300 ? s.Substring(0, 300) : s;
    }

    // ── 모델 자동 받기 ── 한 번에 하나만 받는다. UI 스레드에서 부르면 진행 소식(ModelStatus)도 UI 스레드로 온다
    private static Task? _download;
    public static event Action<string>? ModelStatus;
    public static bool ModelDownloading => _download is { IsCompleted: false };

    /// <summary>모델이 없으면 받는다(이미 받는 중이면 그 작업을 돌려준다). 실패하면 다음 호출 때 다시 시도한다</summary>
    public static Task EnsureModelAsync()
    {
        if (ModelReady) return Task.CompletedTask;
        return _download is { IsCompleted: false } ? _download : _download = Run();

        static async Task Run()
        {
            try
            {
                await DownloadModelAsync(new Progress<string>(m => ModelStatus?.Invoke(m)));
                ModelStatus?.Invoke("받아 둠");
            }
            catch (Exception ex) { ModelStatus?.Invoke("받지 못했습니다: " + ex.Message); throw; }
        }
    }

    /// <summary>Supertonic 3 모델(약 129MB)을 받아 ModelRoot 에 푼다. 윈도우 10 이상에 들어 있는 tar.exe 로 bz2 를 푼다</summary>
    public static async Task DownloadModelAsync(IProgress<string> progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ModelRoot);
        string stage = Path.Combine(ModelRoot, "_extract"); // 다 푼 뒤 한 번에 옮긴다 — 반쯤 풀린 파일을 합성기가 읽지 않게
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
        Directory.CreateDirectory(stage);
        string tmp = Path.Combine(ModelRoot, ModelName + ".tar.bz2.part");
        using (var http = new HttpClient())
        using (var resp = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tmp);
            var buf = new byte[1 << 16];
            long got = 0; int n, lastPct = -1;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                got += n;
                int pct = total > 0 ? (int)(got * 100 / total) : -1;
                if (pct != lastPct) { lastPct = pct; progress.Report(pct >= 0 ? $"받는 중 {pct}%" : $"받는 중 {got >> 20}MB"); }
            }
        }
        progress.Report("압축 푸는 중…");
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "tar.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var a in new[] { "-xjf", tmp, "-C", stage }) psi.ArgumentList.Add(a);
        using (var p = Process.Start(psi)!)
        {
            string err = await p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            if (p.ExitCode != 0) throw new IOException("압축 풀기 실패: " + err.Trim());
        }
        File.Delete(tmp);
        if (Directory.Exists(ModelDir)) Directory.Delete(ModelDir, true); // 일부만 남은 예전 폴더
        Directory.Move(Path.Combine(stage, ModelName), ModelDir);
        Directory.Delete(stage, true);
        if (!ModelReady) throw new IOException("받은 파일에 모델이 없습니다.");
    }

    public void Dispose()
    {
        _player.Dispose();
        _synth.Dispose();
        lock (_stLock) _st?.Dispose();
    }
}
