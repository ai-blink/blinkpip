using System;
using Windows.Media;

namespace blinkpip;

/// <summary>
/// Windows 미디어 세션(SMTC) 연결: 키보드의 미디어 키(재생/일시정지·다음·이전·정지)와 볼륨 플라이아웃의 미디어 컨트롤이
/// 이 앱으로 오게 한다. 전역 핫키로 키를 가로채지 않으므로 다른 미디어 앱과 충돌하지 않고, 윈도우가 마지막으로 재생한 앱에 보낸다.
/// </summary>
public sealed class MediaSession
{
    private readonly SystemMediaTransportControls _c;
    private MediaPlaybackStatus _status = MediaPlaybackStatus.Closed;
    private string _title = "";

    /// <summary>"play" | "pause" | "next" | "prev" | "stop" (UI 스레드가 아닌 곳에서 호출됨)</summary>
    public event Action<string>? Button;

    private MediaSession(IntPtr hwnd)
    {
        _c = SystemMediaTransportControlsInterop.GetForWindow(hwnd);
        _c.IsEnabled = true;
        _c.IsPlayEnabled = _c.IsPauseEnabled = _c.IsNextEnabled = _c.IsPreviousEnabled = _c.IsStopEnabled = true;
        _c.ButtonPressed += (_, e) =>
        {
            string? name = e.Button switch
            {
                SystemMediaTransportControlsButton.Play => "play",
                SystemMediaTransportControlsButton.Pause => "pause",
                SystemMediaTransportControlsButton.Next => "next",
                SystemMediaTransportControlsButton.Previous => "prev",
                SystemMediaTransportControlsButton.Stop => "stop",
                _ => null,
            };
            if (name != null) Button?.Invoke(name);
        };
    }

    /// <summary>만들지 못하는 환경(구형 윈도우 등)이면 null — 그 경우 미디어 키 없이 동작한다</summary>
    public static MediaSession? TryCreate(IntPtr hwnd)
    {
        try { return new MediaSession(hwnd); }
        catch (Exception) { return null; }
    }

    /// <summary>재생 상태와 제목을 알린다. 값이 바뀔 때만 윈도우에 전달한다.</summary>
    public void Update(bool hasMedia, bool paused, string title)
    {
        var status = !hasMedia ? MediaPlaybackStatus.Stopped : paused ? MediaPlaybackStatus.Paused : MediaPlaybackStatus.Playing;
        try
        {
            if (status != _status) { _c.PlaybackStatus = status; _status = status; }
            if (title != _title)
            {
                _title = title;
                _c.DisplayUpdater.Type = MediaPlaybackType.Video;
                _c.DisplayUpdater.VideoProperties.Title = title;
                _c.DisplayUpdater.Update();
            }
        }
        catch (Exception) { /* 세션 갱신 실패는 재생에 영향 없음 */ }
    }
}
