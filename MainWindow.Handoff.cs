using System.Windows;
namespace blinkpip;
public partial class MainWindow
{
    private sealed record PendingHandoff(HandoffRequest Request, int Revision, long EntryId);
    private PendingHandoff? _pendingHandoff;
    private void InitHandoff(MpvPlayer player)
    {
        player.HandoffEntryEnded += entry =>
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(() => { if (_pendingHandoff?.EntryId == entry) _pendingHandoff = null; }));
        };
    }

    private void OpenExternal(string raw)
    {
        if (!HandoffParser.SafeExternalArgument(raw)) { SetStatus("열기 요청의 길이 또는 형식이 올바르지 않습니다."); return; }
        HandoffRequest? request = null;
        if (HandoffParser.IsProtocol(raw) && !HandoffParser.TryParseProtocol(raw, out request, out string error))
        { SetStatus(error); return; }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (request == null) { UrlInputBox.Text = raw; RouteMedia(raw); }
        else { UrlInputBox.Text = request.Url; OpenHandoff(request); }
    }
    private async void OpenHandoff(HandoffRequest request)
    {
        try { await RouteMediaAsync(request.Url, request); }
        catch (Exception ex) { _pendingHandoff = null; _diagnostics.Fail("열기", ex.Message); }
    }
    private int BeginMediaRoute() { _mpv?.CancelHandoffSubtitle(); _pendingHandoff = null; return ++_diagnosticRouteRevision; }
    private void LoadHandoffMedia(string target, HandoffRequest? request, int revision)
    {
        if (_mpv == null) return;
        if (request == null) { _mpv.Load(target); return; }
        // The synchronous load command exposes its new playlist ID before queued FILE_LOADED reaches the UI.
        long entry = _mpv.LoadHandoff(target);
        _pendingHandoff = entry >= 0 ? new(request, revision, entry) : null;
        SetStatus(entry >= 0 ? "확장에서 받은 영상을 여는 중입니다." : "영상을 요청했지만 추가 재생 정보를 연결하지 못했습니다.");
    }
    private void FinishWebHandoff(HandoffRequest? request)
    {
        if (request == null) return;
        SetHandoffPip(request.Pip);
        SetStatus(request.Time != null || request.Speed != null || request.Subtitle != null
            ? "OTT 페이지를 열었습니다. 보던 위치·속도·자막 적용은 지원하지 않습니다. 보호 영상 재생·자막 읽기: 확인 필요."
            : "OTT 페이지를 열었습니다. 보호 영상 재생 시작은 확인 필요합니다.");
    }
    private void SetHandoffPip(bool? desired)
    {
        if (desired.HasValue && _isPipMode != desired.Value) TogglePipMode();
    }
    private void QueueFileLoaded()
    {
        var player = _mpv;
        if (player == null || Dispatcher.HasShutdownStarted) return;
        long entry = player.CurrentEntryId;
        string? path = player.GetPath();
        Dispatcher.BeginInvoke(new Action(() => OnFileLoaded(entry, path)));
    }
    private void OnFileLoaded(long entry, string? path)
    {
        if (_closing || _mpv == null || entry != _mpv.CurrentEntryId || string.IsNullOrEmpty(path) || path != _mpv.GetPath()) return;
        var pending = _pendingHandoff;
        HandoffRequest? request = pending != null && pending.EntryId == entry && pending.Revision == _diagnosticRouteRevision && pending.Request.Url == path ? pending.Request : null;
        _resumePath = path;
        RecentStore.Add(path);
        if (!_autoPaused && _mpv.GetPaused()) _mpv.SetPause(false);
        if (_listOpen) RefreshPlaylistRows();
        if (request?.Time is { } requestedTime)
        {
            double duration = _mpv.GetDuration() ?? 0;
            _mpv.SeekTo(duration > 0 ? Math.Min(requestedTime, Math.Max(0, duration - .05)) : requestedTime);
        }
        else if (_settings.ResumePlayback && ResumeStore.Get(path) is { } pos && pos > 5) _mpv.SeekTo(pos);
        if (request == null) return;
        _pendingHandoff = null;
        if (request.Speed is { } speed) _mpv.SetSpeed(speed);
        if (request.Subtitle is { } subtitle) _mpv.AddHandoffSubtitle(subtitle, entry);
        SetHandoffPip(request.Pip);
        SetStatus("확장 요청을 적용했습니다." + (request.Subtitle != null ? " 자막 파일 불러오기를 요청했습니다." : ""));
    }
}
