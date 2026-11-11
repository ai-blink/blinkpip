using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace HybridPipPlayer;

public partial class SettingsWindow
{
    private DiagInfo? _diagnostics;
    private readonly DispatcherTimer _diagnosticRefresh = new() { Interval = TimeSpan.FromSeconds(1) };

    private void InitDiagnostics(DiagInfo? diagnostics)
    {
        _diagnostics = diagnostics;
        if (diagnostics == null) return;
        // Poll only while this window is alive; native log bursts never flood the UI dispatcher.
        _diagnosticRefresh.Tick += (_, _) => RefreshDiagnostics();
        _diagnosticRefresh.Start();
        Closed += (_, _) => { _diagnosticRefresh.Stop(); _diagnostics = null; };
        RefreshDiagnostics();
    }

    private void RefreshDiagnostics()
    {
        if (_diagnostics != null) InfoText.Text = _diagnostics.Snapshot();
    }

    public void ShowDiagnostics()
    {
        Nav.SelectedIndex = 5;
        RefreshDiagnostics();
        InfoText.BringIntoView();
    }

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_diagnostics?.Snapshot() ?? InfoText.Text);
            DiagnosticCopyStatus.Text = "진단을 복사했습니다. 주소 경로·쿼리·인증 값은 가렸습니다.";
        }
        catch (ExternalException) { DiagnosticCopyStatus.Text = "클립보드를 사용할 수 없습니다. 잠시 후 다시 시도하세요."; }
    }
}
