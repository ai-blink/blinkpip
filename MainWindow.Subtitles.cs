using System.Windows;
using System.Windows.Threading;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfSeparator = System.Windows.Controls.Separator;

namespace HybridPipPlayer;

public partial class MainWindow
{
    private readonly SubtitlePracticeController _practice = new();
    private readonly SubtitlePracticeSettings _practiceSettings = SubtitlePracticeSettings.Load();
    private readonly object _practiceSync = new();
    private SubtitlePracticeWindow? _practiceWindow;
    private readonly DispatcherTimer _practiceUiTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private SubtitlePlaybackSample? _latestSubtitleSample;
    private bool _practiceEngineActive, _practiceMediaLoaded, _practiceSeekInFlight;
    private int _practiceSeekPending, _practiceListTick;
    private long _practiceMediaSerial, _practiceWindowSerial = -1, _practiceMinimumNativeSerial, _practiceLoadedNativeSerial;
    private string? _practicePrimaryId;

    private void InitSubtitlePractice(MpvPlayer player)
    {
        _practice.Configure(_practiceSettings.AutoPause, _practiceSettings.Repeat, _practiceSettings.Passes);
        SetPracticeEngine(_engineA);
        player.SubtitleSample += sample =>
        {
            lock (_practiceSync)
            {
                if (_closing || !_practiceEngineActive || !_practiceMediaLoaded || sample.MediaSerial != _practiceLoadedNativeSerial || player.SubtitleMediaSerial != _practiceLoadedNativeSerial) return;
                double currentDelay = RefreshPracticeContext(player);
                if (sample.PrimaryId != _practicePrimaryId || Math.Abs(sample.Delay - currentDelay) > .001) return;
                if (!sample.TimingSupported) _practice.Reset();
                _latestSubtitleSample = sample;
                if (!sample.TimingSupported) return;
                ApplyPracticeAction(player, _practice.Tick(sample.Position, sample.Paused, sample.Cue, sample.Delay));
            }
        };
        player.DiagnosticStarted += _ => ResetSubtitleMedia(false);
        player.FileEnded += _ => ResetSubtitleMedia(false);
        player.FileLoaded += () =>
        {
            lock (_practiceSync)
            {
                if (!_practiceEngineActive || player.SubtitleMediaSerial < _practiceMinimumNativeSerial) return;
                _practiceLoadedNativeSerial = player.SubtitleMediaSerial;
                _practiceMediaLoaded = true; _practice.Reset();
            }
        };
        player.SubtitleUserSeekRequested += () =>
        {
            lock (_practiceSync) { _practice.Reset(); _practiceSeekPending = 0; _practiceSeekInFlight = false; _latestSubtitleSample = null; }
        };
        player.DiagnosticPlaybackStarted += _ =>
        {
            lock (_practiceSync)
            {
                if (_practiceSeekInFlight) { _practiceSeekInFlight = false; _practice.AcknowledgeSeek(); }
            }
        };
        player.SubtitleContextChanged += () =>
        {
            lock (_practiceSync) { if (!_closing) RefreshPracticeContext(player); }
        };
        player.SubtitleSeekStarted += () =>
        {
            lock (_practiceSync)
            {
                if (_practiceSeekPending != 0) { _practiceSeekPending = 0; _practiceSeekInFlight = true; }
                else { _practiceSeekInFlight = false; _practice.Reset(); _latestSubtitleSample = null; }
            }
        };
        _practiceUiTimer.Tick += (_, _) => RefreshPracticeWindow();
        Closed += (_, _) =>
        {
            _practiceUiTimer.Stop(); SetPracticeEngine(false); _practiceWindow?.Close();
        };
    }

    private void SetPracticeEngine(bool active)
    {
        lock (_practiceSync)
        {
            _practiceEngineActive = active; _practice.SetEngine(active);
            _latestSubtitleSample = null; _practiceSeekPending = 0; _practiceSeekInFlight = false; _practiceMediaSerial++;
            _practiceMediaLoaded = false;
            _practiceMinimumNativeSerial = (_mpv?.SubtitleMediaSerial ?? 0) + 1;
            if (active && (_practiceSettings.AutoPause || _practiceSettings.Repeat)) _mpv?.ClearAbLoop();
        }
    }

    private void ResumePracticeForExistingMedia()
    {
        lock (_practiceSync)
        {
            _practiceEngineActive = true; _practice.SetEngine(true);
            _practiceMinimumNativeSerial = _practiceLoadedNativeSerial = _mpv?.SubtitleMediaSerial ?? 0;
            _practiceMediaLoaded = !string.IsNullOrEmpty(_mpv?.GetPath());
            _latestSubtitleSample = null; _practiceSeekPending = 0; _practiceSeekInFlight = false; _practiceMediaSerial++;
        }
    }

    private void ResetSubtitleMedia(bool loaded)
    {
        lock (_practiceSync)
        {
            _practice.Reset(); _latestSubtitleSample = null; _practiceSeekPending = 0; _practiceSeekInFlight = false;
            _practiceMediaLoaded = loaded; _practiceMediaSerial++;
        }
    }

    // Called under _practiceSync. Read NOW: queued sid/delay notifications can precede an internally requested seek.
    private double RefreshPracticeContext(MpvPlayer player)
    {
        string? primary = player.PrimarySubtitleId;
        double delay = player.GetSubDelay();
        if (_practice.UpdateContext(primary, delay))
        {
            _latestSubtitleSample = null; _practiceSeekPending = 0; _practiceSeekInFlight = false;
            if (_practicePrimaryId != primary) _practiceMediaSerial++;
        }
        _practicePrimaryId = primary;
        return delay;
    }

    private void ApplyPracticeAction(MpvPlayer player, SubtitlePracticeAction action)
    {
        lock (_practiceSync)
        {
            if (_closing || !_practiceEngineActive || !_practiceMediaLoaded) return;
            RefreshPracticeContext(player); // invalidates action if context changed since it was produced
            _practice.ApplyIfCurrent(action, current =>
            {
                if (current.Seek is { } seconds) { _practiceSeekPending = 1; player.SeekSubtitlePractice(seconds); }
                if (current.Pause) player.SetPause(true);
                else if (current.Resume) player.SetPause(false);
            });
        }
    }

    private void OpenSubtitlePractice()
    {
        if (_mpv == null || !_engineA) { SetStatus("자막 연습은 엔진 A에서 사용할 수 있습니다"); return; }
        if (_practiceWindow != null) { _practiceWindow.Activate(); return; }
        var window = new SubtitlePracticeWindow(_practiceSettings) { Owner = this };
        _practiceWindow = window; _practiceWindowSerial = -1;
        window.OptionsChanged += (auto, repeat, count) =>
        {
            lock (_practiceSync)
            {
                _practiceSettings.AutoPause = auto; _practiceSettings.Repeat = repeat; _practiceSettings.Passes = count;
                _practice.Configure(auto, repeat, count); _practiceSeekPending = 0; _practiceSeekInFlight = false;
                if (auto || repeat) _mpv?.ClearAbLoop();
                if (!_practiceSettings.Save()) SetStatus("자막 연습 설정을 저장하지 못했습니다");
            }
        };
        window.ReplayRequested += cue =>
        {
            lock (_practiceSync)
            {
                if (!_engineA || _mpv == null || !_practiceMediaLoaded || !_mpv.SubtitleTimingSupported) return;
                double delay = RefreshPracticeContext(_mpv); // capture actual context BEFORE replay sets its pending seek
                if (_practiceWindowSerial != _practiceMediaSerial) return; // stale cue list after file/primary-track change
                _mpv.ClearAbLoop();
                ApplyPracticeAction(_mpv, _practice.Replay(cue, delay));
            }
        };
        window.SecondaryChanged += id =>
        {
            lock (_practiceSync)
            {
                if (!_engineA || _mpv == null) return;
                RefreshPracticeContext(_mpv);
                var tracks = _mpv.GetTracks("sub");
                if (!window.MatchesTrackContext(tracks, _mpv.PrimarySubtitleId) ||
                    !SubtitleSelectionGuard.CanSelect(_practiceWindowSerial, _practiceMediaSerial, _practiceMediaLoaded,
                    _mpv.PrimarySubtitleId, id, tracks.Select(t => t.Id))) return;
                _mpv.SetSecondarySubtitle(id);
            }
        };
        window.Closed += (_, _) => { _practiceUiTimer.Stop(); _practiceWindow = null; };
        _practiceListTick = 0; RefreshPracticeWindow(); _practiceUiTimer.Start(); window.Show();
    }

    private void RefreshPracticeWindow()
    {
        if (_closing || _practiceWindow == null) return;
        lock (_practiceSync)
        {
            if (_practiceWindowSerial != _practiceMediaSerial)
            {
                _practiceWindow.ResetContext();
                _practiceWindow.UpdateSample(null, _engineA);
                _practiceWindow.UpdateLines(Array.Empty<SubtitleCue>());
                _practiceWindowSerial = _practiceMediaSerial; _practiceListTick = 0;
            }
            _practiceWindow.UpdateSample(_latestSubtitleSample, _engineA && _mpv != null);
            if (_practiceListTick++ % 5 != 0 || _mpv == null || !_engineA || !_practiceMediaLoaded) return;
            _practiceWindow.UpdateLines(_mpv.GetSubtitleLines());
            _practiceWindow.UpdateTracks(_mpv.GetTracks("sub"), _mpv.PrimarySubtitleId, _mpv.SecondarySubtitleId);
        }
    }

    private void ToggleAbLoopWithPracticeReset()
    {
        lock (_practiceSync)
        {
            _practice.Configure(false, false, _practiceSettings.Passes); _practiceSeekPending = 0; _practiceSeekInFlight = false;
            _practiceSettings.AutoPause = _practiceSettings.Repeat = false; _practiceSettings.Save();
            if (_practiceWindow != null) _practiceWindow.Close();
            _mpv?.ToggleAbLoop();
        }
    }

    private void ShowTrackMenu(FrameworkElement anchor, string type, string kind, string title)
    {
        var m = new WpfContextMenu();
        var tracks = _engineA && _mpv != null ? _mpv.GetTracks(type) : new List<MpvPlayer.TrackInfo>();
        if (tracks.Count == 0 && type == "audio") m.Items.Add(new WpfMenuItem { Header = "선택할 소리 트랙이 없습니다", IsEnabled = false });
        if (type == "sub") m.Items.Add(Item("자막 끄기", () => _mpv?.SetTrack(kind, null), tracks.All(t => !t.Selected)));
        foreach (var t in tracks)
        {
            int id = t.Id;
            m.Items.Add(Item(t.Label, () => _mpv?.SetTrack(kind, id), t.Selected));
        }
        if (type == "sub")
        {
            m.Items.Add(new WpfSeparator());
            m.Items.Add(Item("자막 파일 열기…", OpenSubtitleFile));
            m.Items.Add(Item("자막 연습…", OpenSubtitlePractice));
        }
        ShowMenu(anchor, m);
    }

}
