using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfImage = System.Windows.Controls.Image;
using WpfText = System.Windows.Controls.TextBlock;
using WpfPanel = System.Windows.Controls.StackPanel;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;

namespace HybridPipPlayer;

/// <summary>4×3 리모컨 창. 기본 5칸(이전·재생·다음·음소거·전체 화면) + 즐겨찾기 6칸 + 편집 1칸.
/// 편집을 누르면 같은 창이 편집 화면(칸 고르기 → 상위 카드 → 하위 카드)으로 바뀐다.
/// 눌린 칸·바뀐 설정은 이벤트로만 알리고, 실제 동작과 저장은 MainWindow 가 한다.</summary>
public partial class RemoteWindow : Window
{
    /// <summary>(id 또는 기본 키, 기본 칸인가, 창을 닫을까). false 면 창을 그대로 둔다(이전·재생·다음·음소거는 연속으로 누른다)</summary>
    public event Action<string, bool, bool>? Invoked;
    /// <summary>즐겨찾기나 메인 추가 아이콘이 바뀌었다 — 저장하고 재생 줄을 다시 그린다</summary>
    public event Action? Changed;

    private enum Mode { Home, Slot, Cat, Sub }

    private readonly string[] _favorites;   // MainWindow 설정의 배열을 그대로 고친다
    private readonly string[] _extras;
    private readonly Func<string, ImageSource> _icon;
    private readonly Func<string, string?> _state;                    // 기능 id → 이름 아래 상태 글자(없으면 null)
    private readonly Dictionary<string, WpfText> _subs = new();       // 지금 화면의 상태 글자 칸
    private readonly Dictionary<string, string> _subExtra = new();    // 상태 줄 뒤에 붙일 긴 단축키
    private WpfImage? _playImg, _muteImg;
    private bool _paused, _muted, _closing;

    private Mode _mode = Mode.Home;
    private bool _tabMain;                  // 편집 화면의 탭: false = 리모컨 즐겨찾기, true = 메인 툴바
    private int _slot;
    private string _cat = "nav";

    private static readonly (string Key, string Name, string Icon)[] Basics =
    {
        ("prev", "이전", "prev"), ("play", "재생/일시정지", "play"), ("next", "다음", "next"),
        ("mute", "음소거", "mute"), ("full", "전체 화면", "fullscreen"),
    };

    public RemoteWindow(string[] favorites, string[] extras, Func<string, ImageSource> icon, Func<string, string?> state, bool paused, bool muted)
    {
        InitializeComponent();
        _favorites = favorites;
        _extras = extras;
        _icon = icon;
        _state = state;
        _paused = paused;
        _muted = muted;
        Render();
    }

    /// <summary>재생/음소거 칸의 그림을 지금 상태에 맞춘다</summary>
    public void Refresh(bool paused, bool muted)
    {
        _paused = paused; _muted = muted;
        if (_playImg != null) _playImg.Source = _icon(paused ? "play" : "pause");
        if (_muteImg != null) _muteImg.Source = _icon(muted ? "mute" : "volume");
        foreach (var id in _subs.Keys) ApplyState(id);
    }

    // ───────── 화면 그리기 ─────────

    private string[] CurrentList => _tabMain ? _extras : _favorites;
    private string SlotLabel => _tabMain ? $"메인 추가 {_slot + 1}" : $"칸 {_slot + 1}";

    private void Render()
    {
        Cells.Children.Clear();
        _playImg = _muteImg = null;
        _subs.Clear();
        _subExtra.Clear();
        BackBtn.Visibility = _mode == Mode.Home ? Visibility.Collapsed : Visibility.Visible;
        TabRow.Visibility = _mode == Mode.Slot ? Visibility.Visible : Visibility.Collapsed;
        Hint.Text = "";
        switch (_mode)
        {
            case Mode.Home: TitleText.Text = "리모컨"; RenderHome(); break;
            case Mode.Slot: TitleText.Text = "리모컨 편집"; RenderSlot(); break;
            case Mode.Cat: TitleText.Text = $"{SlotLabel} · 어느 묶음에서 고를까요?"; RenderCat(); break;
            case Mode.Sub: TitleText.Text = $"{SlotLabel} › {FeatureCatalog.Categories.First(c => c.Id == _cat).Name}"; RenderSub(); break;
        }
    }

    private void RenderHome()
    {
        foreach (var (key, name, ic) in Basics)
        {
            var img = MakeImage(ic);
            if (key == "play") _playImg = img; else if (key == "mute") _muteImg = img;
            bool keepOpen = key is "prev" or "play" or "next" or "mute";
            Cells.Children.Add(MakeCell(name, img, CellKind.Plain, () => Fire(key, true, !keepOpen), id: key));
        }
        Refresh(_paused, _muted);
        foreach (string id in _favorites)
        {
            var f = FeatureCatalog.ById(id);
            if (f == null) { Cells.Children.Add(MakeCell("비어 있음", GlyphText(""), CellKind.Dashed, () => { }, enabled: false)); continue; }
            string fid = f.Id;
            Cells.Children.Add(MakeCell(f.Name, FeatureIcon(f), CellKind.Fav, () => Fire(fid, false, true), id: fid, shortcut: f.Shortcut));
        }
        Cells.Children.Add(MakeCell("편집", GlyphText(""), CellKind.Dashed, () => { _mode = Mode.Slot; _slot = 0; Render(); }));
    }

    private void RenderSlot()
    {
        TabFav.BorderBrush = _tabMain ? PlainBorder : AccentBorder;
        TabMain.BorderBrush = _tabMain ? AccentBorder : PlainBorder;
        var list = CurrentList;
        if (!_tabMain)
        {
            foreach (var (_, name, ic) in Basics) Cells.Children.Add(MakeCell(name, MakeImage(ic), CellKind.Plain, () => { }, enabled: false));
        }
        for (int i = 0; i < list.Length; i++)
        {
            int slot = i;
            var f = FeatureCatalog.ById(list[i]);
            Cells.Children.Add(MakeCell(f?.Name ?? "비어 있음", f != null ? FeatureIcon(f) : GlyphText(""), CellKind.Pick, () => { _slot = slot; _mode = Mode.Cat; Render(); }));
        }
        Cells.Children.Add(MakeCell("편집 끝", GlyphText(""), CellKind.Dashed, () => { _mode = Mode.Home; Render(); }));
        Hint.Text = _tabMain ? "고른 기능이 재생 줄(전체 화면 옆)에 아이콘으로 붙습니다" : "기본 5칸은 고정입니다. 깜빡이는 칸 중 바꿀 칸을 누르세요";
    }

    private void RenderCat()
    {
        foreach (var (id, name) in FeatureCatalog.Categories)
        {
            string cid = id;
            int n = FeatureCatalog.All.Count(f => f.Category == id);
            Cells.Children.Add(MakeCell($"{name} ({n})", GlyphText(CategoryGlyph(id)), CellKind.Plain, () => { _cat = cid; _mode = Mode.Sub; Render(); }));
        }
        Cells.Children.Add(MakeCell("비우기", GlyphText(""), CellKind.Dashed, () => { CurrentList[_slot] = ""; Changed?.Invoke(); _mode = Mode.Slot; Render(); }));
        Hint.Text = "상위 카드 → 하위 카드(개별 기능) 순서입니다";
    }

    private void RenderSub()
    {
        var list = CurrentList;
        foreach (var f in FeatureCatalog.All.Where(f => f.Category == _cat))
        {
            string fid = f.Id;
            bool used = list.Contains(f.Id);
            Cells.Children.Add(MakeCell(f.Name, FeatureIcon(f), used ? CellKind.Fav : CellKind.Plain, () => Assign(fid), id: fid, shortcut: f.Shortcut, star: used));
        }
        Hint.Text = $"누르면 {SlotLabel}에 들어갑니다. ★은 이미 쓰는 기능입니다(누르면 서로 자리를 바꿈)";
    }

    private void Assign(string id)
    {
        var list = CurrentList;
        int other = Array.IndexOf(list, id);
        if (other >= 0 && other != _slot) list[other] = list[_slot];
        list[_slot] = id;
        Changed?.Invoke();
        _mode = Mode.Slot;
        Render();
    }

    private static string CategoryGlyph(string id) => id switch
    {
        "nav" => "", "list" => "", "av" => "", "view" => "", _ => "",
    };

    // ───────── 칸 만들기 ─────────

    private enum CellKind { Plain, Fav, Dashed, Pick }

    private static readonly Brush PlainBorder = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xAA, 0x00, 0x00, 0x00)); // 시안 3: 어두운 1px 윤곽
    private static readonly Brush AccentBorder = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xCC, 0x4C, 0xC3, 0x8A));

    private UIElement FeatureIcon(Feature f) => f.Icon != null ? MakeImage(f.Icon) : GlyphText(f.Glyph);

    private WpfImage MakeImage(string name) => new() { Source = _icon(name), Width = 26, Height = 26 };

    private WpfText GlyphText(string glyph) => new()
    {
        Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 22,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        Foreground = (Brush)FindResource("TextBrush"),
    };

    // id 를 주면 이름 아래에 그 기능의 지금 상태(예: 반복 "끔")를, shortcut 은 오른쪽 위에, star 는 왼쪽 위 ★ 를 보인다
    private WpfButton MakeCell(string name, UIElement icon, CellKind style, Action click, bool enabled = true,
                               string? id = null, string? shortcut = null, bool star = false)
    {
        var center = System.Windows.HorizontalAlignment.Center;
        var label = new WpfText
        {
            Text = name, FontSize = 13, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = center, TextAlignment = TextAlignment.Center,
        };
        var sub = new WpfText
        {
            FontSize = 11, Foreground = (Brush)FindResource("SubTextBrush"), HorizontalAlignment = center,
            Margin = new Thickness(0, 1, 0, 0), Visibility = Visibility.Collapsed,
        };
        var panel = new WpfPanel { Orientation = System.Windows.Controls.Orientation.Vertical, HorizontalAlignment = center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(icon);
        panel.Children.Add(label);
        panel.Children.Add(sub);
        // 긴 단축키는 아이콘과 겹치므로 오른쪽 위 대신 이름 아래 줄에 붙인다
        bool longKey = !string.IsNullOrEmpty(shortcut) && shortcut.Length > 7;
        if (id != null) { _subs[id] = sub; if (longKey) _subExtra[id] = shortcut!; ApplyState(id); }
        var box = new System.Windows.Controls.Grid { Width = 106, Height = 80 };
        box.Children.Add(panel);
        if (star)
            box.Children.Add(new WpfText
            {
                Text = "★", FontSize = 11, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xC3, 0x8A)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(7, 4, 0, 0),
            });
        if (!string.IsNullOrEmpty(shortcut) && !longKey)
            box.Children.Add(new WpfText
            {
                Text = shortcut, FontSize = 10, Foreground = (Brush)FindResource("SubTextBrush"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 7, 0),
            });
        var b = new WpfButton
        {
            Content = box, Style = (System.Windows.Style)FindResource("CellButton"), Width = 112, Height = 84, Margin = new Thickness(4),
            BorderBrush = style is CellKind.Fav or CellKind.Pick ? AccentBorder : PlainBorder,
            BorderThickness = new Thickness(style == CellKind.Pick ? 2 : 1),
            IsEnabled = enabled, ToolTip = name,
        };
        if (style == CellKind.Pick) b.Loaded += (_, _) => Pulse(b);
        System.Windows.Automation.AutomationProperties.SetName(b, name); // 화면 읽기·자동 시험이 칸을 이름으로 찾게
        b.Click += (_, _) => click();
        return b;
    }

    private void ApplyState(string id)
    {
        if (!_subs.TryGetValue(id, out var sub)) return;
        string text = string.Join(" · ", new[] { _state(id), _subExtra.GetValueOrDefault(id) }.Where(x => !string.IsNullOrEmpty(x)));
        sub.Text = text;
        sub.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // 고를 칸은 테두리가 천천히 깜빡인다
    private static void Pulse(WpfButton b)
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(800))
        { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        b.BeginAnimation(OpacityProperty, anim);
    }

    // ───────── 이벤트 ─────────

    private void Fire(string id, bool basic, bool close)
    {
        Invoked?.Invoke(id, basic, close);
        if (close) CloseOnce();
    }

    private void CloseOnce()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => StepBack();

    private void StepBack()
    {
        switch (_mode)
        {
            case Mode.Sub: _mode = Mode.Cat; break;
            case Mode.Cat: _mode = Mode.Slot; break;
            case Mode.Slot: _mode = Mode.Home; break;
            default: CloseOnce(); return;
        }
        Render();
    }

    private void TabFav_Click(object sender, RoutedEventArgs e) { _tabMain = false; Render(); }
    private void TabMain_Click(object sender, RoutedEventArgs e) { _tabMain = true; Render(); }
    private void Close_Click(object sender, RoutedEventArgs e) => CloseOnce();

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape) { StepBack(); e.Handled = true; }
    }

    // 창 바깥을 누르면 닫는다
    private void Window_Deactivated(object? sender, EventArgs e) => CloseOnce();
}
