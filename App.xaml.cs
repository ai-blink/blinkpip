using System;
using System.Linq;
using System.Windows;

namespace HybridPipPlayer;

public partial class App : System.Windows.Application
{
    /// <summary>실행 인자로 받은 파일·주소(없으면 null). MainWindow 가 준비되면 연다.</summary>
    public static string? StartupArg { get; private set; }

    public SingleInstance Instance { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // 첫 번째 인자(옵션이 아닌 것)를 열 대상으로 본다. 연결 프로그램은 "%1" 로 경로 하나를 넘긴다.
        StartupArg = e.Args.FirstOrDefault(a => !a.StartsWith("--") && a.Trim().Length > 0);
        if (StartupArg != null && SingleInstance.TrySendToRunning(StartupArg))
        {
            Shutdown(); // 이미 떠 있는 창이 받았다
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Instance.Dispose();
        base.OnExit(e);
    }
}
