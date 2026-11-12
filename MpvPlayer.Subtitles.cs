using System.Runtime.InteropServices;

namespace HybridPipPlayer;

public sealed record SubtitlePlaybackSample(double Position, bool Paused, SubtitleCue? Cue, double Delay, string? PrimaryId, bool TimingSupported = true, long MediaSerial = 0);

public partial class MpvPlayer
{
    private long _subtitleMediaSerial;
    public long SubtitleMediaSerial => Interlocked.Read(ref _subtitleMediaSerial);
    public event Action? SubtitleUserSeekRequested;
    public void SeekSubtitlePractice(double seconds) => Command("seek", Inv(seconds), "absolute+exact");

    private const int EVENT_PROPERTY_CHANGE = 22, EVENT_SEEK = 20;
    private const int FORMAT_NONE = 0, FORMAT_STRING = 1, FORMAT_FLAG = 3, FORMAT_DOUBLE = 5;
    private const ulong SubtitleObserveBase = 0x48505000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvEventProperty { public IntPtr Name; public int Format; public IntPtr Data; }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_observe_property(IntPtr ctx, ulong replyUserdata, byte[] name, int format);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_unobserve_property(IntPtr ctx, ulong replyUserdata);

    public event Action<SubtitlePlaybackSample>? SubtitleSample;
    public event Action? SubtitleContextChanged;
    public event Action? SubtitleSeekStarted;

    // Hook after initialize, before starting EventLoop. Values are consumed in the event thread, never retained pointers.
    private void ObserveSubtitleProperties()
    {
        string[] names = { "time-pos", "sub-start/full", "sub-end/full", "sub-text", "sid", "sub-delay", "pause" };
        int[] formats = { FORMAT_DOUBLE, FORMAT_DOUBLE, FORMAT_DOUBLE, FORMAT_STRING, FORMAT_STRING, FORMAT_DOUBLE, FORMAT_FLAG };
        for (int i = 0; i < names.Length; i++)
        {
            int rc = mpv_observe_property(_ctx, SubtitleObserveBase + (ulong)i, ToUtf8(names[i]), formats[i]);
            if (rc < 0) Log("자막 상태 관찰 실패: " + names[i] + " " + ErrorString(rc));
        }
    }

    // Called from EventLoop for 22 and 20. All native strings/data are copied before next mpv_wait_event.
    private void HandleSubtitleEvent(MpvEvent ev)
    {
        if (ev.EventId == EVENT_START_FILE) { Interlocked.Increment(ref _subtitleMediaSerial); return; }
        if (ev.EventId == EVENT_SEEK) { SubtitleSeekStarted?.Invoke(); return; }
        if (ev.EventId != EVENT_PROPERTY_CHANGE || ev.ReplyUserdata < SubtitleObserveBase || ev.ReplyUserdata >= SubtitleObserveBase + 7 || ev.Data == IntPtr.Zero) return;
        var property = Marshal.PtrToStructure<MpvEventProperty>(ev.Data);
        string name = Marshal.PtrToStringUTF8(property.Name) ?? "";
        // Demonstrates the exact pointer ABI; reading all cue fields below produces one coherent snapshot.
        double? number = property.Format == FORMAT_DOUBLE && property.Data != IntPtr.Zero ? Marshal.PtrToStructure<double>(property.Data) : null;
        string? text = property.Format == FORMAT_STRING && property.Data != IntPtr.Zero ? Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(property.Data)) : null;
        if (name is "sid" or "sub-delay") SubtitleContextChanged?.Invoke();
        if (name != "time-pos" && name != "pause" && name != "sub-text") return;
        var position = GetTimePos(); // queued notifications may refer to a pre-seek position
        if (position == null) return;
        double? start = GetDouble("sub-start/full"), end = GetDouble("sub-end/full");
        string? cueText = GetProperty("sub-text"); // avoid mixing a stale event string with current cue times
        var cue = start != null && !string.IsNullOrWhiteSpace(cueText) ? new SubtitleCue(start.Value, end, cueText) : null;
        SubtitleSample?.Invoke(new(position.Value, GetPaused(), cue, GetSubDelay(), GetProperty("sid"), SubtitleTimingSupported, SubtitleMediaSerial));
    }

    public List<SubtitleCue> GetSubtitleLines() => SubtitleLineParser.Parse(GetProperty("sub-lines"));

    public bool SubtitleTimingSupported => Math.Abs((GetDouble("sub-speed") ?? 1) - 1) < .0001 && (GetDouble("sub-fps") ?? 0) == 0;
    public string? PrimarySubtitleId => GetProperty("sid");
    public string? CurrentSubtitleText => GetProperty("sub-text"); // PIP 아래 자막 줄이 켜질 때 지금 문장을 바로 채운다
    public string? SecondarySubtitleId => GetProperty("secondary-sid");
    public void SetSecondarySubtitle(int? id) => SetProperty("secondary-sid", id?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "no");
    public void ClearAbLoop() { SetProperty("ab-loop-a", "no"); SetProperty("ab-loop-b", "no"); }
}
