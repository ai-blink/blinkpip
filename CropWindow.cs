using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace blinkpip;

public sealed class CropWindow : Window
{
    private readonly CropStore _store;
    private readonly Func<CropValues, bool> _apply;
    private readonly Slider _zoom = new() { Minimum = 1, Maximum = 4, TickFrequency = .05, IsSnapToTickEnabled = true };
    private readonly Slider _x = new() { Minimum = -100, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly Slider _y = new() { Minimum = -100, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly TextBlock _value = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly ComboBox _presets = new() { Height = 30, DisplayMemberPath = "Name", Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBox _name = new() { Height = 30, MaxLength = 50, Margin = new Thickness(0, 4, 0, 8) };
    private readonly StackPanel _tools = new();
    private bool _sync, _available = true;
    private const string UnavailableMessage = "재생 중인 엔진 A 영상에서만 영역을 바꿀 수 있습니다.";

    public CropWindow(CropStore store, CropValues current, Func<CropValues, bool> apply)
    {
        _store = store; _apply = apply;
        Title = "영역 · 화면 자르기"; Width = 480; MinWidth = 440; SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BarBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(20) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "영역 · 화면 자르기", FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "영상은 실시간으로 바뀝니다. 엔진 A 전용이며 OTT 보호 영상은 지원하지 않습니다.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16) });
        panel.Children.Add(_tools);
        var defaults = new WrapPanel();
        AddButton(defaults, "전체", "CropFull", () => SetValues(CropValues.Full, true));
        AddButton(defaults, "중앙 2×", "CropCenter", () => SetValues(new(2), true));
        AddButton(defaults, "오른쪽 위", "CropTopRight", () => SetValues(new(2, -.25, .25), true));
        _tools.Children.Add(defaults);
        AddSlider("확대", _zoom, "CropZoom");
        AddSlider("보는 위치 · 왼쪽 ↔ 오른쪽", _x, "CropHorizontal");
        AddSlider("보는 위치 · 위 ↔ 아래", _y, "CropVertical");
        AutomationProperties.SetAutomationId(_value, "CropValue");
        _tools.Children.Add(_value);
        _tools.Children.Add(new TextBlock { Text = "저장된 영역", FontWeight = FontWeights.SemiBold });
        AutomationProperties.SetAutomationId(_presets, "CropPresets");
        AutomationProperties.SetName(_presets, "저장된 영역");
        _tools.Children.Add(_presets);
        var saved = new WrapPanel();
        AddButton(saved, "불러오기", "CropLoad", LoadSelected);
        AddButton(saved, "선택한 영역 삭제", "CropDelete", DeleteSelected);
        _tools.Children.Add(saved);
        _tools.Children.Add(new TextBlock { Text = "영역 이름", Margin = new Thickness(0, 12, 0, 0) });
        AutomationProperties.SetAutomationId(_name, "CropName");
        AutomationProperties.SetName(_name, "영역 이름");
        _tools.Children.Add(_name);
        var save = new WrapPanel();
        AddButton(save, "현재 영역 저장 / 같은 이름 덮어쓰기", "CropSave", SaveCurrent);
        _tools.Children.Add(save);
        panel.Children.Add(_message);
        RefreshPresets();
        SetValues(current, false);
        _zoom.ValueChanged += (_, _) => Changed();
        _x.ValueChanged += (_, _) => Changed();
        _y.ValueChanged += (_, _) => Changed();
        if (store.LoadWarning is { } warning) _message.Text = warning;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }

    private void AddSlider(string name, Slider slider, string id)
    {
        _tools.Children.Add(new TextBlock { Text = name, Margin = new Thickness(0, 12, 0, 4) });
        slider.Height = 28;
        slider.SetResourceReference(StyleProperty, "PlayerSlider");
        AutomationProperties.SetAutomationId(slider, id);
        AutomationProperties.SetName(slider, name);
        _tools.Children.Add(slider);
    }
    private static void AddButton(System.Windows.Controls.Panel parent, string text, string id, Action action)
    {
        var button = new Button { Content = text, MinHeight = 32, Margin = new Thickness(0, 0, 8, 4), Focusable = true };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => action(); parent.Children.Add(button);
    }
    private CropValues Values
    {
        get { double limit = (1 - 1 / _zoom.Value) / 2; return new(_zoom.Value, -_x.Value / 100 * limit, -_y.Value / 100 * limit); }
    }
    private void Changed()
    {
        if (_sync) return;
        _value.Text = $"확대 {_zoom.Value:0.00}× · 가로 위치 {_x.Value:0}% · 세로 위치 {_y.Value:0}%";
        _x.IsEnabled = _y.IsEnabled = _zoom.Value > 1;
        _message.Text = _apply(Values) ? "" : "영역을 적용하지 못했습니다. 현재 영상 출력값을 다시 표시했습니다.";
    }
    private bool SetValues(CropValues input, bool apply)
    {
        var v = input.Normalize();
        _sync = true;
        _zoom.Value = v.Zoom;
        _x.Value = v.PanLimit > 0 ? -v.PanX / v.PanLimit * 100 : 0;
        _y.Value = v.PanLimit > 0 ? -v.PanY / v.PanLimit * 100 : 0;
        _sync = false;
        _value.Text = $"확대 {v.Zoom:0.00}× · 가로 위치 {_x.Value:0}% · 세로 위치 {_y.Value:0}%";
        _x.IsEnabled = _y.IsEnabled = v.Zoom > 1;
        bool success = !apply || _apply(v);
        if (apply) _message.Text = success ? "" : "영역을 적용하지 못했습니다. 현재 영상 출력값을 다시 표시했습니다.";
        return success;
    }
    public void SetAvailable(bool available)
    {
        _tools.IsEnabled = available;
        if (!available) _message.Text = UnavailableMessage;
        else if (!_available && _message.Text == UnavailableMessage) _message.Text = "";
        _available = available;
    }
    public void SynchronizeCrop(CropValues values) => SetValues(values, false);
    private void RefreshPresets(string? selected = null)
    {
        _presets.ItemsSource = _store.Presets;
        _presets.SelectedItem = _store.Presets.FirstOrDefault(p => p.Name.Equals(selected, StringComparison.OrdinalIgnoreCase));
    }
    private void LoadSelected()
    {
        if (_presets.SelectedItem is not CropPreset preset) { _message.Text = "불러올 영역을 선택하세요."; return; }
        if (!SetValues(preset.Values, true)) return;
        _name.Text = preset.Name; _message.Text = "저장된 영역을 적용했습니다.";
    }
    private void SaveCurrent()
    {
        if (!_store.Save(_name.Text, Values, out string error)) { _message.Text = error; return; }
        RefreshPresets(_name.Text.Trim()); _message.Text = "영역을 저장했습니다. 해상도와 무관한 비율로 보관합니다.";
    }
    private void DeleteSelected()
    {
        if (_presets.SelectedItem is not CropPreset preset) { _message.Text = "삭제할 영역을 선택하세요."; return; }
        if (!_store.Remove(preset.Name, out string error)) { _message.Text = error; return; }
        RefreshPresets(); _message.Text = "저장된 영역을 삭제했습니다.";
    }
}
