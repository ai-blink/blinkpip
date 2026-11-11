namespace HybridPipPlayer;
public partial class App
{
    public static string? StartupError { get; private set; }
    private void PrepareStartupArgument(string[] arguments)
    {
        StartupError = null;
        StartupArg = arguments.FirstOrDefault(a => !a.StartsWith("--") && a.Trim().Length > 0);
        if (StartupArg == null) return;
        if (!SingleInstance.ValidRequest(StartupArg) || (HandoffParser.IsProtocol(StartupArg) && arguments.Length != 1))
        {
            StartupArg = null;
            StartupError = "열기 요청을 거절했습니다. 주소 형식·길이·로그인 정보 포함 여부를 확인하세요.";
        }
    }
}
