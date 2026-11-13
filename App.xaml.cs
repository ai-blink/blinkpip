using System;
using System.Linq;
using System.Windows;

namespace blinkpip;

public partial class App : System.Windows.Application
{
    /// <summary>실행 인자로 받은 파일·주소(없으면 null). MainWindow 가 준비되면 연다.</summary>
    public static string? StartupArg { get; private set; }

    public SingleInstance Instance { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // 첫 번째 인자(옵션이 아닌 것)를 열 대상으로 본다. 연결 프로그램은 "%1" 로 경로 하나를 넘긴다.
        PrepareStartupArgument(e.Args);
        if (StartupArg != null && SingleInstance.TrySendToRunning(StartupArg))
        {
            Shutdown(); // 이미 떠 있는 창이 받았다
            return;
        }
        AppPaths.MigrateLegacy(); // 예전 이름(HybridPipPlayer) 저장 폴더를 blinkpip 으로 옮긴다 — 창(설정 읽기)보다 먼저
        FileAssociation.RepairStale(); // 예전·사라진 실행 파일을 가리키는 확장 연결·연결 프로그램 등록을 지금 실행 파일로
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Instance.Dispose();
        base.OnExit(e);
    }
}
