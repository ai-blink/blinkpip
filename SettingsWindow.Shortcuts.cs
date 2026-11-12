using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace blinkpip;

public partial class SettingsWindow
{
    private System.Windows.Input.KeyEventHandler? _shortcutCaptureHandler;
    private TextCompositionEventHandler? _shortcutTextCaptureHandler;

    public void ConfigureShortcuts(ShortcutStore store)
    {
        if (_shortcutCaptureHandler != null) PreviewKeyDown -= _shortcutCaptureHandler;
        if (_shortcutTextCaptureHandler != null) PreviewTextInput -= _shortcutTextCaptureHandler;
        ShortcutList.Children.Clear();
        var hint = new TextBlock
        {
            Text = "키 이름을 입력하고 저장합니다. 예: Ctrl+K · 별칭은 F11 | F · 빈 칸은 해제. 영상 창 전용 기능은 WPF 버튼에 포커스가 있으면 동작하지 않습니다.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 8),
        };
        ShortcutList.Children.Add(hint);
        var search = new TextBox { Height = 28, Margin = new Thickness(0, 0, 0, 8), ToolTip = "기능 이름이나 키 검색" };
        AutomationProperties.SetAutomationId(search, "ShortcutSearch");
        AutomationProperties.SetName(search, "단축키 검색");
        ShortcutList.Children.Add(search);
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(feedback, "ShortcutFeedback");
        ShortcutList.Children.Add(feedback);
        TextBox? captureTarget = null;
        string captureName = "";
        bool capturing = false;
        var capture = new Button
        {
            Content = "선택한 기능에 키 입력", IsEnabled = false, Focusable = true,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "아래 편집 칸을 선택한 뒤 누르세요. 새 키 입력 후 해당 행의 저장 버튼을 눌러 적용합니다.",
        };
        AutomationProperties.SetAutomationId(capture, "ShortcutCapture");
        ShortcutList.Children.Add(capture);
        capture.Click += (_, _) =>
        {
            if (!capture.Focus()) { capturing = false; feedback.Text = "키 입력 버튼에 포커스를 둘 수 없습니다."; return; }
            capturing = true;
            feedback.Text = captureName + ": 새 키를 누르세요. Esc는 취소합니다. 입력 후 저장 버튼으로 적용합니다.";
        };
        capture.LostKeyboardFocus += (_, _) =>
        {
            if (capturing) { capturing = false; feedback.Text = "키 입력을 취소했습니다."; }
        };
        void PreviewChord(ShortcutChord chord)
        {
            capturing = false;
            if (captureTarget != null) captureTarget.Text = chord.ToString();
            feedback.Text = captureName + ": " + chord + " 입력됨. 저장 버튼으로 적용하거나 직접 별칭을 더 입력하세요.";
        }
        _shortcutCaptureHandler = (_, e) =>
        {
            if (!capturing) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { capturing = false; feedback.Text = "키 입력을 취소했습니다."; return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed) return;
            if (ShortcutCatalog.TryCapture(key, Keyboard.Modifiers, out var chord)) PreviewChord(chord);
            else feedback.Text = "지원하지 않는 키입니다. 다른 키를 누르거나 Esc로 취소하세요.";
        };
        _shortcutTextCaptureHandler = (_, e) =>
        {
            if (!capturing) return;
            e.Handled = true;
            if (e.Text.Length == 1 && ShortcutCatalog.TryCapture(ShortcutCatalog.KeyForCharacter(e.Text[0]), Keyboard.Modifiers, out var chord))
                PreviewChord(chord);
        };
        PreviewKeyDown += _shortcutCaptureHandler;
        PreviewTextInput += _shortcutTextCaptureHandler;
        var rows = new StackPanel();
        ShortcutList.Children.Add(rows);
        var allRows = new List<(Grid Row, ShortcutFeature Feature, TextBox Input)>();
        foreach (var feature in ShortcutCatalog.All)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock
            {
                Text = feature.Name + (feature.NativeOnly ? " (영상 창)" : ""), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 6, 0),
            };
            var input = new TextBox { Text = string.Join(" | ", store.Keys(feature.Id)), Height = 28, FontSize = 12 };
            AutomationProperties.SetAutomationId(input, "ShortcutKey_" + feature.Id);
            AutomationProperties.SetName(input, feature.Name + " 단축키");
            input.GotKeyboardFocus += (_, _) =>
            {
                capturing = false;
                captureTarget = input;
                captureName = feature.Name;
                capture.IsEnabled = true;
                capture.Content = feature.Name + " · 키 입력";
            };
            var save = new Button { Content = "저장", Focusable = true, Margin = new Thickness(5, 0, 0, 0), Height = 28, MinWidth = 42, Padding = new Thickness(4, 0, 4, 0) };
            var reset = new Button { Content = "복원", Focusable = true, Margin = new Thickness(5, 0, 0, 0), Height = 28, MinWidth = 42, Padding = new Thickness(4, 0, 4, 0) };
            AutomationProperties.SetAutomationId(save, "ShortcutSave_" + feature.Id);
            AutomationProperties.SetAutomationId(reset, "ShortcutReset_" + feature.Id);
            void Refresh() { input.Text = string.Join(" | ", store.Keys(feature.Id)); reset.IsEnabled = store.IsChanged(feature.Id); }
            save.Click += (_, _) =>
            {
                string[] keys = string.IsNullOrWhiteSpace(input.Text) ? Array.Empty<string>() : input.Text.Split('|', StringSplitOptions.TrimEntries);
                if (store.TrySet(feature.Id, keys, out string error))
                { feedback.Text = feature.Name + ": 저장했습니다."; Refresh(); }
                else feedback.Text = error;
            };
            reset.Click += (_, _) =>
            {
                if (store.TryReset(feature.Id, out string error)) { feedback.Text = feature.Name + ": 기본값을 복원했습니다."; Refresh(); }
                else feedback.Text = error;
            };
            Refresh();
            Grid.SetColumn(input, 1); Grid.SetColumn(save, 2); Grid.SetColumn(reset, 3);
            row.Children.Add(label); row.Children.Add(input); row.Children.Add(save); row.Children.Add(reset);
            rows.Children.Add(row);
            allRows.Add((row, feature, input));
        }
        search.TextChanged += (_, _) =>
        {
            string query = search.Text.Trim();
            foreach (var (row, feature, input) in allRows)
                row.Visibility = (feature.Name + " " + input.Text).Contains(query, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        };
        var resetAll = new Button { Content = "단축키 모두 기본값으로", Focusable = true, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 8) };
        AutomationProperties.SetAutomationId(resetAll, "ShortcutResetAll");
        resetAll.Click += (_, _) =>
        {
            if (store.TryReset(null, out string error)) ConfigureShortcuts(store); else feedback.Text = error;
        };
        ShortcutList.Children.Add(resetAll);
        ShortcutList.Children.Add(new TextBlock
        {
            Text = "전역 단축키 (변경 불가)\n" + string.Join(" · ", ShortcutCatalog.Globals.Select(g => g.Key + " " + g.Name)),
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = (System.Windows.Media.Brush)FindResource("SubTextBrush"),
        });
        if (store.LoadWarning is { } warning) feedback.Text = warning;
    }
}
