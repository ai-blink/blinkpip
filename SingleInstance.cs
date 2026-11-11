using System.IO;
using System.IO.Pipes;
using System.Text;
namespace HybridPipPlayer;

/// <summary>Bounded, current-user IPC; ordinary file opens and validated protocol requests share the receiver.</summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string Suffix = Environment.UserName.Replace('\\', '_');
    private static readonly string MutexName = "HybridPipPlayer.Single." + Suffix;
    private static readonly string PipeName = "HybridPipPlayer.Pipe." + Suffix;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly CancellationTokenSource _stop = new();
    private Mutex? _mutex;
    private Task? _serverTask;
    public static bool ValidRequest(string raw) => HandoffParser.SafeExternalArgument(raw) &&
        (!HandoffParser.IsProtocol(raw) || HandoffParser.TryParseProtocol(raw, out _, out _));
    public static bool TrySendToRunning(string path)
    {
        if (!ValidRequest(path)) return false;
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            client.ConnectAsync(400, timeout.Token).GetAwaiter().GetResult();
            byte[] bytes = StrictUtf8.GetBytes(path + "\n");
            client.WriteAsync(bytes, timeout.Token).AsTask().GetAwaiter().GetResult();
            client.FlushAsync(timeout.Token).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or TimeoutException or ArgumentException) { return false; }
    }
    public void StartServer(Action<string> onRequest)
    {
        if (_mutex != null || _stop.IsCancellationRequested) return;
        _mutex = new Mutex(true, MutexName, out bool created);
        if (!created) { _mutex.Dispose(); _mutex = null; return; }
        _serverTask = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_stop.Token);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    using var data = new MemoryStream();
                    byte[] buffer = new byte[512]; bool complete = false, invalid = false;
                    while (!complete && !invalid)
                    {
                        int count = await server.ReadAsync(buffer, timeout.Token);
                        if (count == 0) break;
                        for (int i = 0; i < count; i++)
                        {
                            if (buffer[i] == 10) { complete = true; break; }
                            if (data.Length >= HandoffParser.MaximumBytes) { invalid = true; break; }
                            data.WriteByte(buffer[i]);
                        }
                    }
                    if (complete && !invalid && !_stop.IsCancellationRequested)
                    {
                        string raw = StrictUtf8.GetString(data.ToArray());
                        if (ValidRequest(raw)) onRequest(raw);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or ArgumentException or ObjectDisposedException) { if (!_stop.IsCancellationRequested) await Task.Delay(100); }
            }
        });
    }
    public void Dispose()
    {
        _stop.Cancel();
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex?.Dispose(); _mutex = null;
        // The task owns its live pipe and observes cancellation; never wait on the UI thread.
    }
}
