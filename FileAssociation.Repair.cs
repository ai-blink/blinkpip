using System.IO;
using Microsoft.Win32;
namespace blinkpip;

/// <summary>
/// 켤 때 낡은 연결 등록(확장 연결 주소·"연결 프로그램")을 현재 실행 파일로 고친다. 낡은 등록 = 0.6.0 실행 파일(HybridPipPlayer.exe)이나
/// 지워졌거나 옮긴 blinkpip.exe 를 가리키는 것. 다른 곳에 살아 있는 blinkpip.exe 를 가리키면 사용자가 그 사본으로 등록한 것이라 두고,
/// 등록하지 않은 사용자에게 새로 만들지 않는다(개발 빌드와 배포본을 번갈아 켜도 등록을 서로 뺏지 않게).
/// </summary>
public static partial class FileAssociation
{
    private const string LegacyAppKey = @"Software\Classes\Applications\HybridPipPlayer.exe";

    public static void RepairStale()
    {
        string? exe = Environment.ProcessPath;
        if (exe == null || !Path.GetFileName(exe).Equals("blinkpip.exe", StringComparison.OrdinalIgnoreCase)) return; // dotnet run 등
        try { RepairProtocol(exe); } catch (Exception) { } // 고치지 못해도 켜기는 막지 않는다 — 설정의 등록 버튼이 남아 있다
        try { RepairOpenWith(exe); } catch (Exception) { }
    }

    internal static void RepairProtocol(string exe)
    {
        string? current;
        using (var key = Registry.CurrentUser.OpenSubKey(ProtocolKey))
        {
            if (key == null || !OwnedByUs(key)) return; // 등록하지 않았거나 다른 프로그램의 등록
            using var open = key.OpenSubKey(@"shell\open\command");
            current = open?.GetValue("") as string;
        }
        string wanted = ProtocolCommand(exe);
        if (string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase) || IsLiveCopy(current)) return;
        RegisterProtocolCommand(wanted);
    }

    internal static void RepairOpenWith(string exe)
    {
        bool hadLegacy;
        using (var legacy = Registry.CurrentUser.OpenSubKey(LegacyAppKey)) hadLegacy = legacy != null;
        string? current;
        using (var k = Registry.CurrentUser.OpenSubKey(AppKey + @"\shell\open\command")) current = k?.GetValue("") as string;

        bool stale = current != null && !string.Equals(ExePathOf(current), exe, StringComparison.OrdinalIgnoreCase) && !IsLiveCopy(current);
        if ((hadLegacy && current == null) || stale) Register(exe);
        if (hadLegacy)
        {
            Registry.CurrentUser.DeleteSubKeyTree(LegacyAppKey, throwOnMissingSubKey: false); // 연결 프로그램 목록에 죽은 예전 이름이 남지 않게
            SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
        }
    }

    // 등록 명령이 지금 디스크에 있는 다른 blinkpip.exe 를 가리키는가
    private static bool IsLiveCopy(string? command)
    {
        string? path = ExePathOf(command);
        return path != null && Path.GetFileName(path).Equals("blinkpip.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
    }

    // "\"C:\경로\blinkpip.exe\" \"%1\"" → C:\경로\blinkpip.exe
    private static string? ExePathOf(string? command)
    {
        string c = command?.Trim() ?? "";
        if (c.Length == 0) return null;
        if (c[0] == '"') { int end = c.IndexOf('"', 1); return end > 1 ? c[1..end] : null; }
        int space = c.IndexOf(' ');
        return space < 0 ? c : c[..space];
    }
}
