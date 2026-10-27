using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace HybridPipPlayer;

/// <summary>
/// 첫 번째 실행이 이름 있는 파이프로 다른 실행의 요청(열 파일 경로)을 받는다. 파일을 인자로 실행하면(연결 프로그램·더블클릭)
/// 이미 떠 있는 창으로 넘기고 새 창은 닫는다. 인자 없이 다시 실행하면 독립된 새 창으로 뜬다.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string Suffix = Environment.UserName.Replace('\\', '_');
    private static readonly string MutexName = "HybridPipPlayer.Single." + Suffix;
    private static readonly string PipeName = "HybridPipPlayer.Pipe." + Suffix;

    private Mutex? _mutex;
    private Thread? _thread;
    private volatile bool _stop;
    private Action<string>? _onRequest;

    /// <summary>이미 떠 있는 실행에 path 를 보낸다. 성공하면 true(이 프로세스는 종료해도 된다).</summary>
    public static bool TrySendToRunning(string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(400);
            var bytes = Encoding.UTF8.GetBytes(path + "\n");
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch (Exception) { return false; } // 떠 있는 실행이 없거나 응답이 없다
    }

    /// <summary>요청 수신을 시작한다. 이미 다른 실행이 수신 중이면(뮤텍스 선점) 조용히 건너뛴다.</summary>
    public void StartServer(Action<string> onRequest)
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew) { _mutex.Dispose(); _mutex = null; return; }
        _onRequest = onRequest;
        _thread = new Thread(ServerLoop) { IsBackground = true, Name = "single-instance-pipe" };
        _thread.Start();
    }

    private void ServerLoop()
    {
        while (!_stop)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.CurrentUserOnly);
                server.WaitForConnection();
                using var reader = new StreamReader(server, Encoding.UTF8);
                string? line = reader.ReadLine();
                if (line != null && !_stop) _onRequest?.Invoke(line.Trim());
            }
            catch (Exception) { Thread.Sleep(200); } // 연결이 중간에 끊기면 다시 대기
        }
    }

    public void Dispose()
    {
        _stop = true;
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
    }
}
