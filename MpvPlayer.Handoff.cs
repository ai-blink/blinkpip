using System.Globalization;
using System.Runtime.InteropServices;
namespace HybridPipPlayer;
public partial class MpvPlayer
{
    private readonly object _handoffSubtitleLock = new();
    private ulong _handoffSubtitleSequence = 0x4850500000000000;
    private ulong _handoffSubtitleCommand;
    private long _handoffSubtitleEntry = -1;
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_command_async(IntPtr ctx, ulong replyUserdata, IntPtr[] args);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_abort_async_command(IntPtr ctx, ulong replyUserdata);

    private long _currentEntryId = -1;
    public event Action<long>? HandoffEntryEnded;
    public long CurrentEntryId => Interlocked.Read(ref _currentEntryId);
    private void TrackHandoffEntry(MpvEvent ev)
    {
        if (ev.EventId == EVENT_START_FILE && ev.Data != IntPtr.Zero)
        {
            Interlocked.Exchange(ref _currentEntryId, Marshal.ReadInt64(ev.Data));
            lock (_handoffSubtitleLock)
                if (_handoffSubtitleEntry != CurrentEntryId) CancelHandoffSubtitleLocked();
        }
        if (ev.EventId == EVENT_END_FILE && ev.Data != IntPtr.Zero)
        {
            long entry = Marshal.ReadInt64(ev.Data, 8);
            lock (_handoffSubtitleLock)
                if (_handoffSubtitleEntry == entry) CancelHandoffSubtitleLocked();
            HandoffEntryEnded?.Invoke(entry);
        }
        if (ev.EventId == 5) // MPV_EVENT_COMMAND_REPLY; property observers use a different event type.
        {
            bool report = false;
            lock (_handoffSubtitleLock)
            {
                if (_handoffSubtitleCommand != 0 && ev.ReplyUserdata == _handoffSubtitleCommand)
                {
                    report = ev.Error < 0 && _handoffSubtitleEntry == CurrentEntryId && !_stop;
                    _handoffSubtitleCommand = 0;
                    _handoffSubtitleEntry = -1;
                }
            }
            if (report) Report("명령 실패 sub-add: " + ErrorString(ev.Error));
        }
    }

    /// <summary>Queues remote subtitle IO without blocking the UI; binds completion to the loaded entry.</summary>
    public void AddHandoffSubtitle(string path, long expectedEntry)
    {
        int result;
        lock (_handoffSubtitleLock)
        {
            if (_stop || _ctx == IntPtr.Zero || expectedEntry < 0 || expectedEntry != CurrentEntryId) return;
            CancelHandoffSubtitleLocked();
            ulong command = ++_handoffSubtitleSequence;
            _handoffSubtitleCommand = command;
            _handoffSubtitleEntry = expectedEntry;
            var args = new[] { "sub-add", path, "select" };
            var pointers = new IntPtr[args.Length + 1];
            try
            {
                for (int i = 0; i < args.Length; i++) pointers[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
                result = mpv_command_async(_ctx, command, pointers);
                if (result < 0) { _handoffSubtitleCommand = 0; _handoffSubtitleEntry = -1; }
            }
            finally
            {
                foreach (IntPtr pointer in pointers) if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
            }
        }
        if (result < 0) Report("명령 실패 sub-add: " + ErrorString(result));
    }

    public void CancelHandoffSubtitle()
    {
        lock (_handoffSubtitleLock) CancelHandoffSubtitleLocked();
    }
    private void CancelHandoffSubtitleLocked()
    {
        ulong command = _handoffSubtitleCommand;
        _handoffSubtitleCommand = 0;
        _handoffSubtitleEntry = -1; // Clear first: cancellation replies must not become current-media failures.
        if (command != 0 && _ctx != IntPtr.Zero) mpv_abort_async_command(_ctx, command);
    }
    public long LoadHandoff(string target)
    {
        string? previous = GetProperty("playlist/0/id");
        Load(target);
        if (GetProperty("playlist/0/id") == previous) return -1;
        return long.TryParse(GetProperty("playlist/0/id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : -1;
    }
}
