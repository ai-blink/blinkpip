namespace blinkpip;

// 자막 읽어 주기(설정 → 오디오·자막, PIP 왼쪽 위 목소리 버튼). 자막 글자는 PIP 아래 자막 줄과 같은 길로 받는다:
//  · mpv = SubtitleSample(재생 중에만 읽고, 일시정지하면 말을 멈춘다)
//  · 웹뷰 = 페이지의 "hppsub:" 메시지(SubtitleReadHosts). PIP 가 아니어도 읽도록 window.__hppSubRead 를 켠다.
public partial class MainWindow
{
    private SubtitleSpeaker? _speaker;
    private string? _lastTtsMpv;   // mpv 이벤트 스레드에서만 쓴다
    private bool _ttsMpvPaused;    // mpv 이벤트 스레드에서만 쓴다

    private bool TtsWanted => _settings.SubTts && !_closing;

    private void InitSubtitleTts()
    {
        SubtitleSpeaker.GpuWanted = _settings.SubTtsGpu; // 모델을 처음 불러올 때 CPU·GPU 판을 고른다
        if (_mpv != null)
            _mpv.SubtitleSample += s =>
            {
                if (s.Paused != _ttsMpvPaused)
                {
                    _ttsMpvPaused = s.Paused;
                    if (s.Paused) { _lastTtsMpv = null; Dispatcher.BeginInvoke(() => _speaker?.Stop()); }
                }
                if (s.Paused) return; // 다시 재생하면 지금 줄부터 읽는다
                string text = s.Cue?.Text ?? "";
                if (text == _lastTtsMpv) return;
                _lastTtsMpv = text;
                Dispatcher.BeginInvoke(() => { if (_engineA) SpeakSubtitle(text); });
            };
        ApplySubtitleTts();
    }

    private void SpeakSubtitle(string? text)
    {
        if (!TtsWanted) return;
        if (_speaker == null) { _speaker = new SubtitleSpeaker(); ConfigureSpeaker(); }
        _speaker.Speak(text);
    }

    private void ConfigureSpeaker() =>
        _speaker?.Configure(_settings.SubTtsVoice, _settings.SubTtsRate, _settings.SubTtsPitch, _settings.SubTtsVolume);

    // 설정이 바뀌었을 때: 끄면 말을 멈추고, 목소리·속도·높낮이·크기를 다시 맞추고, 웹 페이지에 자막 읽기 여부를 넘긴다
    private void ApplySubtitleTts()
    {
        if (!_settings.SubTts) _speaker?.Stop();
        else
        {
            _speaker ??= new SubtitleSpeaker();
            ConfigureSpeaker();
            EnsureTtsModel();
            EnsureGpuFiles();
            _speaker.Warm(); // 첫 줄을 기다리지 않게 모델을 미리 불러 둔다
        }
        _lastTtsMpv = null; // 켜는 순간 지금 줄부터 읽게
        _lockOverlay?.SetSpeech(_settings.SubTts);
        PushOttSubConfig();
    }

    // 여자·남자(Supertonic) 목소리인데 모델이 없으면 알아서 받는다. 받는 동안은 윈도우 목소리로 읽고, 다 받으면 다음 줄부터 바뀐다
    private async void EnsureTtsModel()
    {
        if (!_settings.SubTtsVoice.StartsWith("st:", StringComparison.Ordinal) || SubtitleSpeaker.ModelReady) return;
        try { await SubtitleSpeaker.EnsureModelAsync(); _speaker?.Warm(); }
        catch (Exception) { /* 설정 창에 실패 문구가 뜬다. 다음에 켜거나 목소리를 고를 때 다시 시도 */ }
    }

    // «GPU로 합성»이 켜져 있는데 GPU 파일이 없으면 받는다(약 1.2GB). 모델을 아직 안 불렀으면 다 받은 뒤 바로 GPU 로 읽고,
    // 이미 CPU 로 불렀으면 앱을 다시 켤 때부터 GPU 로 읽는다. 진행·실패 문구는 설정 창에 보인다
    private async void EnsureGpuFiles()
    {
        if (!_settings.SubTtsGpu || SubtitleSpeaker.GpuFilesReady || !SubtitleSpeaker.NvidiaDriverPresent) return;
        try { await SubtitleSpeaker.EnsureGpuFilesAsync(); }
        catch (Exception) { /* 설정 창에 실패 문구가 뜬다. 다음에 켜거나 스위치를 다시 켤 때 다시 시도 */ }
    }

    // PIP 왼쪽 위 목소리 버튼
    private void ToggleSubTts()
    {
        _settings.SubTts = !_settings.SubTts;
        _settings.Save();
        ApplySubtitleTts();
        _settingsWindow?.SyncSubTts();
    }
}
