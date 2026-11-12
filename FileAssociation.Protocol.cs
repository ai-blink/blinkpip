using System.IO;
using Microsoft.Win32;
namespace HybridPipPlayer;
public static partial class FileAssociation
{
    private const string ProtocolKey = @"Software\Classes\hybridpip";
    private static string ProtocolCommand()
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("실행 파일 경로를 알 수 없습니다.");
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("blinkpip.exe로 실행한 뒤 등록하세요.");
        return $"\"{exe}\" \"%1\"";
    }
    public static bool IsProtocolRegistered()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(ProtocolKey + @"\shell\open\command"); return string.Equals(key?.GetValue("") as string, ProtocolCommand(), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
    public static void RegisterProtocol()
    {
        string command = ProtocolCommand();
        using (var existing = Registry.CurrentUser.OpenSubKey(ProtocolKey))
            if (existing != null && existing.GetValue("HybridPipPlayer.Owner") as string != "HybridPipPlayer")
                throw new InvalidOperationException("이 주소가 다른 프로그램에 등록되어 있습니다. 기존 등록을 확인하세요.");
        using (var key = Registry.CurrentUser.CreateSubKey(ProtocolKey))
        {
            key.SetValue("", "URL:blinkpip"); key.SetValue("URL Protocol", ""); key.SetValue("HybridPipPlayer.Owner", "HybridPipPlayer");
            using var open = key.CreateSubKey(@"shell\open\command"); open.SetValue("", command);
        }
        SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
    }
    public static void UnregisterProtocol()
    {
        if (!IsProtocolRegistered()) throw new InvalidOperationException("현재 앱의 등록이 아니므로 해제하지 않았습니다.");
        Registry.CurrentUser.DeleteSubKeyTree(ProtocolKey, false);
        SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
    }
}
