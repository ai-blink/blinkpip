using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HybridPipPlayer;

/// <summary>
/// 탐색기의 "연결 프로그램"(다른 앱 선택) 목록에 이 앱을 등록·해제한다. 현재 사용자(HKCU)만 건드리고 기본 앱은 바꾸지 않는다
/// (Windows 는 기본 앱을 프로그램이 직접 바꾸지 못하게 막는다 — 사용자가 설정에서 고른다).
/// </summary>
public static partial class FileAssociation
{
    private const string AppKey = @"Software\Classes\Applications\HybridPipPlayer.exe";

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    private const int SHCNE_ASSOCCHANGED = 0x08000000;

    public static bool IsRegistered()
    {
        using var k = Registry.CurrentUser.OpenSubKey(AppKey + @"\shell\open\command");
        string? cmd = k?.GetValue("") as string;
        return cmd != null && Environment.ProcessPath != null && cmd.Contains(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
    }

    public static void Register()
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("실행 파일 경로를 알 수 없습니다");
        using (var app = Registry.CurrentUser.CreateSubKey(AppKey))
        {
            app.SetValue("FriendlyAppName", "Hybrid PIP Player");
            using var icon = app.CreateSubKey("DefaultIcon");
            icon.SetValue("", $"\"{exe}\",0");
            using var cmd = app.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{exe}\" \"%1\"");
            using var types = app.CreateSubKey("SupportedTypes");
            foreach (string ext in MediaFiles.VideoExt.Concat(MediaFiles.AudioExt)) types.SetValue(ext, "");
        }
        SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(AppKey, throwOnMissingSubKey: false);
        SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
    }
}
