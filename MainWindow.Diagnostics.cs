using System.Windows;
using System.Windows.Threading;

namespace blinkpip;

public partial class MainWindow
{
    private readonly DiagInfo _diagnostics = new();
    private int _diagnosticRouteRevision;
    private ulong _diagnosticNavigation;

    private void ShowMpv(bool appendToExisting = false)
    {
        // Appending keeps the loading entry and its handoff options; leaving WebView must still cancel its pending navigation.
        if (!appendToExisting || !_engineA) ++_diagnosticRouteRevision;
        if (!_engineA) ResumePracticeForExistingMedia();
        DrmWebView.Visibility = Visibility.Collapsed;
        MpvHost.Visibility = Visibility.Visible;
    }

    private void InitDiagnostics()
    {
        _diagnostics.Failed += OnDiagnosticFailure;
        _ = Task.Run(_diagnostics.ProbeYtdlpAsync);
        Closed += (_, _) => _diagnostics.Dispose();
    }

    private void AttachDiagnostics(MpvPlayer player)
    {
        _diagnostics.SetEngineVersion(player.Version);
        player.DiagnosticStarted += path =>
        {
            // start-file also covers playlist navigation that bypasses RouteMedia.
            _diagnostics.NativeStarted(path);
        };
        player.DiagnosticLog += _diagnostics.NativeLog;
        player.FileLoaded += _diagnostics.NativeLoaded;
        player.DiagnosticPlaybackStarted += _diagnostics.NativePlaying;
        player.DiagnosticEnded += _diagnostics.NativeEnded;
        player.LogError += message =>
        {
            // End-file has the structured toast. Other command errors still reach the status bar.
            if (!message.StartsWith("명령 실패 ", StringComparison.Ordinal) || Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(() => { if (!_closing) SetStatus(DiagInfo.Redact(message)); }));
        };
    }

    private void OnDiagnosticFailure(string message)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_closing) return;
            SetStatus(message);
            ShowToast("재생 실패", message, null, "진단 보기", () =>
            {
                OpenSettings();
                _settingsWindow?.ShowDiagnostics();
            });
        }));
    }
}
