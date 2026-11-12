using System.Windows;
namespace blinkpip;
public partial class SettingsWindow
{
    private void RefreshHandoffRegistration()
    {
        bool registered = FileAssociation.IsProtocolRegistered();
        HandoffRegister.Content = registered ? "확장 연결 해제" : "확장 연결 등록";
        HandoffRegistrationStatus.Text = registered ? "이 앱에 확장 연결이 등록되어 있습니다." : "확장에서 앱을 열려면 한 번 등록하세요. 앱을 옮긴 뒤에는 다시 등록해야 합니다.";
    }
    private void HandoffRegister_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (FileAssociation.IsProtocolRegistered()) FileAssociation.UnregisterProtocol(); else FileAssociation.RegisterProtocol();
            RefreshHandoffRegistration();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException or InvalidOperationException)
        { HandoffRegistrationStatus.Text = "확장 연결을 변경하지 못했습니다: " + ex.Message; }
    }
}
