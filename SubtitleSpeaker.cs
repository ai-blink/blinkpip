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
/// 읽던 줄은 끝까지 읽고, 그동안 온 줄은 차례대로 조금 빠르게 이어 읽는다(아래 «줄 차례»).
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

    // ── GPU(CUDA) ── 설정 «GPU로 합성». GpuDir 에 sherpa-onnx CUDA 판과 CUDA 13·cuDNN 9 DLL 이 있어야 한다(앱 zip 에는 없다, 약 1.9GB).
    // 앱 폴더의 CPU 판과 DLL 이름이 같아서, sherpa-onnx 를 처음 부르기 전에(모델을 처음 불러올 때) 한 번만 GPU 판을 먼저 올려 두는 방식이다(바꾸면 앱을 다시 켜야 한다).
    // GPU 준비 도중 앱이 죽을 수 있어서(CUDA 를 못 쓰는 PC·드라이버) 준비 전에 GpuTryMarker 를 만들고 첫 합성이 끝나면 지운다.
    // 다음에 켤 때 표시 파일이 남아 있으면 GPU 를 건너뛰고 CPU 로 읽는다. 설정의 GPU 스위치를 다시 켜면 표시 파일을 지워 다시 시도한다.
    // 2026-11-14 측정(RTX 5090, 남자 1, 단계 8, 11줄): 합성 중간값 CPU 401ms → GPU 189ms
    public static string GpuDir => AppPaths.DataFile("tts-gpu");
    private static readonly string[] GpuFiles =
        { "sherpa-onnx-c-api.dll", "onnxruntime.dll", "onnxruntime_providers_cuda.dll", "onnxruntime_providers_shared.dll",
          "cudart64_13.dll", "cublas64_13.dll", "cublasLt64_13.dll", "cufft64_12.dll", "cudnn64_9.dll", "cudnn_ops64_9.dll", "cudnn_cnn64_9.dll", "cudnn_graph64_9.dll" };
    public static bool GpuFilesReady => GpuFiles.All(f => File.Exists(Path.Combine(GpuDir, f)));
    private static string GpuTryMarker => AppPaths.DataFile("tts-gpu-trying");
    public static bool GpuWanted { get; set; }        // 설정 SubTtsGpu. 모델을 처음 불러오기 전에만 효과가 있다
    public static bool GpuActive { get; private set; }
    public static bool GpuFailed { get; private set; } // GPU 를 원했지만 못 올렸거나 지난번 준비 중 앱이 끝났다
    public static bool NativeChosen => _nativeChosen;
    private static volatile bool _nativeChosen;
    private static bool _gpuConfirmed;

    /// <summary>설정에서 GPU 스위치를 켤 때 부른다 — 지난번 실패 표시를 지워 다음에 다시 시도하게</summary>
    public static void ResetGpuTry()
    {
        try { File.Delete(GpuTryMarker); } catch (Exception) { }
    }

    // sherpa-onnx 를 처음 부르기 직전(LoadSupertonic)에 한 번. GpuWanted 이고 파일이 다 있고 지난번에 죽지 않았으면 GPU 판을 쓴다
    private static void ChooseNative()
    {
        if (_nativeChosen) return;
        _nativeChosen = true;
        if (!GpuWanted || !GpuFilesReady) return;
        if (File.Exists(GpuTryMarker)) { GpuFailed = true; Log("지난번 GPU 준비 중 앱이 끝나 이번에는 CPU 로 읽습니다"); return; }
        IntPtr ort = IntPtr.Zero, api = IntPtr.Zero;
        try
        {
            File.WriteAllText(GpuTryMarker, DateTime.Now.ToString("s"));
            // CUDA DLL 은 onnxruntime 이 이름으로 찾는다 — 이 프로세스의 PATH 앞에 둔다
            Environment.SetEnvironmentVariable("PATH", GpuDir + ";" + Environment.GetEnvironmentVariable("PATH"));
            ort = NativeLibrary.Load(Path.Combine(GpuDir, "onnxruntime.dll")); // 앱 폴더의 같은 이름 CPU 판보다 먼저 올린다
            api = NativeLibrary.Load(Path.Combine(GpuDir, "sherpa-onnx-c-api.dll"));
            IntPtr h = api;
            NativeLibrary.SetDllImportResolver(typeof(OfflineTts).Assembly,
                (name, _, _) => name.StartsWith("sherpa-onnx-c-api", StringComparison.OrdinalIgnoreCase) ? h : IntPtr.Zero);
            GpuActive = true;
        }
        catch (Exception ex)
        {
            // 반만 올린 GPU 판을 내린다 — 남겨 두면 CPU 판 sherpa 가 같은 이름의 GPU 판 onnxruntime 에 붙는다
            if (api != IntPtr.Zero) NativeLibrary.Free(api);
            if (ort != IntPtr.Zero) NativeLibrary.Free(ort);
            ResetGpuTry();
            GpuFailed = true;
            Log("GPU 파일을 못 올려 CPU 로 읽습니다: " + ex.Message);
        }
    }

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
        // _last 는 그대로 둔다 — 막대를 움직일 때마다 지금 줄이 다시 쌓여 되풀이해 읽던 문제(2026-11-15 리뷰). 새 설정은 다음 줄부터
    }

    // ── 줄 차례 ──
    // 온 줄은 버리지 않고 차례대로 읽는다. 앞 줄을 읽는 동안 온 줄은 바로 합성해 두었다가 이어 읽는다.
    // 빠르기 자동 맞춤: 줄마다 «최근 자막 간격 × Fill» 안에 끝나도록 빠르기를 정한다(앞에 남은 말이 끝날 때부터 셈).
    //  · 가장 느리게 = 설정의 말 속도, 가장 빠르게 = MaxSpeed. 한 글자에 걸리는 시간(_spc)은 합성 결과로 계속 다시 잰다.
    //  · 간격을 아직 모르면(처음 몇 줄) 밀린 줄 수로 정한다. MaxBacklog 줄보다 많이 밀리면 가장 오래된 줄부터 버린다.
    // 모든 상태는 UI 스레드에서만 만진다.
    private const int MaxBacklog = 3;
    // MaxSpeed: 1.5 는 대사가 빽빽한 드라마에서 줄 대부분이 1.5 로 읽혀 말이 끊겨 들렸다 → 같은 대사 비교 청취로 1.25 를 골랐다(2026-11-15)
    private const double MaxSpeed = 1.25, Fill = 0.9, SynthLead = 0.3; // SynthLead = 합성에 걸리는 대략 시간(초)
    private double _spc = 0.22;     // 1배속에서 한 글자(공백 뺌)에 걸리는 초 — 2026-11-14 기록 기준 시작값
    private double _gap;            // 최근 자막 사이 간격(초, 지수 평균). 0 = 아직 모름
    private DateTime _lastArrival = DateTime.MinValue, _busyUntil = DateTime.MinValue; // _busyUntil = 쌓인 말이 다 끝날 예상 시각
    private readonly Queue<Job> _queue = new(); // 읽을 줄(합성 중이거나 끝남), 온 순서대로
    private sealed class Job
    {
        public Task<MediaSource?> Synth = null!;
        public volatile bool Dropped; // 밀려서 버림 — 합성 중이면 그만둔다
        public double Seconds;        // 예상 말 길이(_busyUntil 에서 되돌릴 값)
    }
    private bool _pumping;
    private TaskCompletionSource? _playDone;

    public void Speak(string? text)
    {
        text = Clean(text);
        if (text.Length == 0) { _last = ""; return; } // 자막이 사라져도 읽던 말은 끝까지
        if (text == _last) return;
        _last = text;
        int id = ++_seq;
        int behind = _queue.Count + (_pumping ? 1 : 0);  // 이 줄 앞에 남은 줄 수
        var now = DateTime.Now;
        double since = (now - _lastArrival).TotalSeconds;
        if (since is > 0.3 and < 8) _gap = _gap == 0 ? since : _gap * 0.7 + since * 0.3; // 장면 사이 긴 침묵은 빼고 잰다
        _lastArrival = now;
        double d1 = text.Count(c => !char.IsWhiteSpace(c)) * _spc;          // 1배속으로 읽으면 걸릴 초
        var start = _pumping && _busyUntil > now.AddSeconds(SynthLead) ? _busyUntil : now.AddSeconds(SynthLead);
        double top = Math.Max(_rate, MaxSpeed), speed;
        if (_gap == 0) speed = _rate * (behind switch { 0 => 1.0, 1 => 1.1, 2 => 1.2, _ => 1.25 });
        else
        {
            double budget = (now - start).TotalSeconds + _gap * Fill;    // 다음 자막 전까지 남는 시간
            speed = budget <= 0.1 ? top : d1 / budget;
        }
        speed = Math.Clamp(speed, _rate, top);
        _busyUntil = start.AddSeconds(d1 / speed);
        double boost = speed / _rate;
        Log($"#{id} 받음 (앞에 {behind}줄, x{speed:0.00}, 간격 {_gap:0.0}s, 1배속 {d1:0.0}s) {text}");
        var job = new Job { Seconds = d1 / speed };
        job.Synth = SynthesizeAsync(text, boost, _gen, id, job);
        _queue.Enqueue(job);
        while (_queue.Count > MaxBacklog) // 너무 밀렸다 — 오래된 줄을 버린다(합성도 그만두고, 예상 끝 시각에서도 뺀다)
        {
            var old = _queue.Dequeue();
            old.Dropped = true;
            _busyUntil = _busyUntil.AddSeconds(-old.Seconds);
            _ = old.Synth.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result?.Dispose(); }, TaskScheduler.Default);
            Log("밀려서 가장 오래된 줄 버림");
        }
        if (!_pumping) Pump(_gen);
    }

    private async void Pump(int g)
    {
        _pumping = true;
        try
        {
            while (_queue.Count > 0 && g == _gen)
            {
                var src = await _queue.Dequeue().Synth;
                if (g != _gen) break;
                if (src == null) { Log("소리 없음 — 건너뜀"); continue; }
                _playDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _player.Source = src;
                _player.Play();
                var sw = Stopwatch.StartNew();
                Log("재생 시작");
                // 끝 신호(MediaEnded·MediaFailed)를 못 받으면(오디오 장치가 바뀌는 등) 차례가 영영 멈춘다 — 30초면 넘어간다
                if (await Task.WhenAny(_playDone.Task, Task.Delay(TimeSpan.FromSeconds(30))) != _playDone.Task)
                {
                    Log("재생 끝 신호가 없어 다음 줄로 넘어감");
                    _player.Pause();
                    continue;
                }
                Log($"재생 끝 {sw.ElapsedMilliseconds}ms");
            }
        }
        catch (Exception ex) { Log("자막 읽기 실패: " + ex.Message); }
        finally { if (g == _gen) { _pumping = false; _busyUntil = DateTime.MinValue; } } // 다 읽었다 — 예상이 틀렸어도 여기서 맞춘다
    }

    private async Task<MediaSource?> SynthesizeAsync(string text, double boost, int g, int id, Job job)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (_voice.StartsWith("st:", StringComparison.Ordinal) && int.TryParse(_voice.AsSpan(3), out int sid) && ModelReady)
            {
                byte[]? wav = await Task.Run(() => SynthesizeSupertonic(text, sid, boost, () => g == _gen && !job.Dropped));
                Log($"#{id} 합성 {sw.ElapsedMilliseconds}ms " + (wav == null ? "취소됨" : $"{(wav.Length - 44) / 2}표본"));
                if (wav != null) { LearnPace(text, wav, _rate * boost); KeepClip(id, text, wav); } // await 뒤라 UI 스레드다
                return wav == null ? null : MediaSource.CreateFromStream(new MemoryStream(wav).AsRandomAccessStream(), "audio/wav");
            }
            _synth.Options.SpeakingRate = Math.Clamp(_rate * boost, 0.5, 6.0);
            var stream = await _synth.SynthesizeTextToStreamAsync(text);
            return MediaSource.CreateFromStream(stream, stream.ContentType);
        }
        catch (Exception ex) { Log($"#{id} 합성 실패 {sw.ElapsedMilliseconds}ms: {ex.Message}"); return null; } // 이 줄만 건너뛴다
    }

    // 실제로 만든 소리 길이로 한 글자당 시간을 다시 잰다(짧은 줄은 앞뒤 여백 비중이 커서 뺀다)
    private void LearnPace(string text, byte[] wav, double speed)
    {
        int chars = text.Count(c => !char.IsWhiteSpace(c));
        int sr = BitConverter.ToInt32(wav, 24);
        if (chars < 4 || sr <= 0) return;
        double played = (wav.Length - 44) / 2.0 / sr;
        _spc = _spc * 0.8 + played * speed / chars * 0.2;
    }

    // 높낮이는 재생 표본율을 pitch 배로 바꿔서 올리고 내린다. 그러면 빠르기도 pitch 배가 되므로 합성 속도를 rate/pitch 로 미리 맞춘다
    /// <param name="alive">false 가 되면(멈춤·버림) 합성을 그만두고 null</param>
    private byte[]? SynthesizeSupertonic(string text, int sid, double boost, Func<bool> alive)
    {
        lock (_stLock)
        {
            if (!alive()) return null;
            _st ??= LoadSupertonic();
            var cfg = new OfflineTtsGenerationConfig
            {
                Sid = sid,
                NumSteps = 8, // 6·7 은 읽다가 끊김이 잦았다(2026-11-14 사용자 청취) — 줄이지 않는다
                Speed = (float)Math.Clamp(_rate * boost / _pitch, 0.5, 4.0),
            };
            cfg.Extra["lang"] = Lang(text);
            // 멈추거나 버리면 합성 도중에 그만둔다(0 = 멈춤)
            var audio = _st.GenerateWithConfig(text, cfg, (IntPtr _, int _, float _, IntPtr _) => alive() ? 1 : 0);
            if (GpuActive && !_gpuConfirmed) { _gpuConfirmed = true; ResetGpuTry(); } // GPU 로 한 줄 만들었다 — 이제 안전
            try
            {
                if (!alive()) return null;
                return ToWav(audio.Samples, (int)Math.Round(audio.SampleRate * _pitch), (float)_volume);
            }
            finally { audio.Dispose(); } // 네이티브 버퍼를 놓는다(IDisposable 을 선언하지 않았지만 Dispose 가 있다)
        }
    }

    /// <summary>Supertonic 모델을 미리 불러 짧은 말을 한 번 합성해 둔다 — 첫 줄이 늦지 않게. 모델이 없거나 윈도우 목소리면 아무것도 안 한다</summary>
    private volatile bool _warming;
    public void Warm()
    {
        // 이미 불렀거나 부르는 중 — 설정 막대를 움직일 때마다 작업이 쌓여 첫 줄 합성이 밀리지 않게(2026-11-15 리뷰)
        if (_warming || _st != null) return;
        if (!_voice.StartsWith("st:", StringComparison.Ordinal) || !int.TryParse(_voice.AsSpan(3), out int sid) || !ModelReady) return;
        _warming = true;
        int g = _gen;
        Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            try { SynthesizeSupertonic("네", sid, 1.0, () => g == _gen); Log($"미리 불러 둠 {sw.ElapsedMilliseconds}ms"); }
            catch (Exception ex) { Log("미리 불러 두기 실패: " + ex.Message); _warming = false; }
        });
    }

    private static OfflineTts LoadSupertonic()
    {
        ChooseNative(); // sherpa-onnx 네이티브를 처음 부르기 전에 CPU·GPU 판을 고른다
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
        c.Model.Provider = GpuActive ? "cuda" : "cpu";
        var sw = Stopwatch.StartNew();
        var tts = new OfflineTts(c);
        Log($"모델 불러옴 ({(GpuActive ? "GPU" : "CPU")}) {sw.ElapsedMilliseconds}ms");
        return tts;
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
        Log("멈춤 (쌓인 줄 비움)");
        _gen++;
        _last = "";
        _queue.Clear();
        _pumping = false;
        _busyUntil = _lastArrival = DateTime.MinValue;
        _player.Pause();
        _playDone?.TrySetResult();
    }

    // 최근에 읽은 소리를 ClipDir 에 남긴다(파일 이름 = 앱 시작 시각_줄 번호, 옆 .txt 에 글자). 끊겨 들릴 때 소리 자체가 잘렸는지
    // 재생에서 끊겼는지 가르려고(2026-11-15 — 기록상 모두 끝까지 재생됐는데 사용자에게는 끊겨 들림). 가장 최근 KeepClips 개만 둔다
    private const int KeepClips = 30;
    private static readonly string ClipDir = AppPaths.DataFile("tts-clips");
    private static readonly string RunTag = DateTime.Now.ToString("HHmmss");
    private static void KeepClip(int id, string text, byte[] wav)
    {
        try
        {
            Directory.CreateDirectory(ClipDir);
            string b = Path.Combine(ClipDir, $"{RunTag}_{id:0000}");
            File.WriteAllBytes(b + ".wav", wav);
            File.WriteAllText(b + ".txt", DateTime.Now.ToString("HH:mm:ss.fff") + " " + text);
            foreach (var old in new DirectoryInfo(ClipDir).GetFiles("*.wav").OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeepClips))
            {
                old.Delete();
                File.Delete(Path.ChangeExtension(old.FullName, ".txt"));
            }
        }
        catch (Exception) { /* 진단용이라 실패해도 읽기는 계속 */ }
    }

    // ── 진단 기록 ── 자막이 빠지는 원인을 찾으려고 남긴다(%LOCALAPPDATA%/blinkpip/tts-log.txt, 512KB 넘으면 새로 시작)
    private int _seq;
    private static readonly object _logLock = new();
    private static void Log(string msg)
    {
        try
        {
            lock (_logLock)
            {
                string f = AppPaths.DataFile("tts-log.txt");
                if (File.Exists(f) && new FileInfo(f).Length > 512 * 1024) File.Delete(f);
                File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine);
            }
        }
        catch (Exception) { /* 기록 실패는 무시 */ }
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
