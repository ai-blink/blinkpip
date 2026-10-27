using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace HybridPipPlayer;

/// <summary>색 테마 후보 A/B/C 를 런타임에 바꾼다. Themes/Dark.xaml 의 색 키를 Application.Resources 에서 덮어쓰면
/// DynamicResource 로 참조하는 모든 브러시·그라데이션이 따라 바뀐다.</summary>
public static class ThemeManager
{
    // 기본색 6개. 그라데이션 단계색(Top/Bottom)·BarDeep 은 아래에서 계산한다(WPF 에 색 혼합이 없음).
    public sealed record Palette(string Label, string Bar, string Field, string Btn, string Sub, string Accent, string Track);

    public static readonly IReadOnlyDictionary<string, Palette> Palettes = new Dictionary<string, Palette>
    {
        ["A"] = new("A. 차분한 회색 + 파랑", "#1E1E1E", "#2D2D2D", "#3A3A3A", "#AAAAAA", "#6AA9FF", "#444444"),
        ["B"] = new("B. 깊은 검정 + 초록", "#121212", "#1D1D1D", "#262626", "#8D8D8D", "#4CC38A", "#303030"),
        ["C"] = new("C. 푸른 회색 + 주황", "#1B1F27", "#262C37", "#323A48", "#9AA5B8", "#FF9F43", "#3B4454"),
    };

    public static void Apply(string name)
    {
        if (!Palettes.TryGetValue(name, out var p)) return;
        // 브러시(Themes/Dark.xaml)는 자기가 들어 있는 병합 사전 안에서 색 키를 찾으므로, 앱 최상위가 아니라 그 사전의 값을 바꿔야 한다
        // (바꾸면 DynamicResource 로 참조하는 모든 브러시·그라데이션이 따라 바뀐다)
        var app = System.Windows.Application.Current.Resources;
        var r = app.MergedDictionaries.FirstOrDefault(d => d.Contains("BarColor")) ?? app;
        Color bar = Parse(p.Bar), btn = Parse(p.Btn), accent = Parse(p.Accent);
        r["BarColor"] = bar;
        r["BarDeepColor"] = Scale(bar, 0.78);
        r["FieldColor"] = Parse(p.Field);
        r["SubTextColor"] = Parse(p.Sub);
        r["TrackColor"] = Parse(p.Track);
        r["BtnTopColor"] = Mix(btn, 0.20);
        r["BtnColor"] = btn;
        r["BtnBottomColor"] = Scale(btn, 0.74);
        r["AccentTopColor"] = Mix(accent, 0.20);
        r["AccentColor"] = accent;
        r["AccentBottomColor"] = Scale(accent, 0.74);

        // 색 키만 바꿔서는 브러시가 따라오지 않는 경우가 있어(실측), 브러시·그라데이션 자체를 새 값으로 교체한다.
        // 이를 DynamicResource 로 쓰는 모든 곳이 새 브러시를 다시 찾는다.
        r["BarBrush"] = new SolidColorBrush(bar);
        r["BarDeepBrush"] = new SolidColorBrush(Scale(bar, 0.78));
        r["FieldBrush"] = new SolidColorBrush(Parse(p.Field));
        r["SubTextBrush"] = new SolidColorBrush(Parse(p.Sub));
        r["TrackBrush"] = new SolidColorBrush(Parse(p.Track));
        r["AccentBrush"] = new SolidColorBrush(accent);
        r["BtnGradient"] = Gradient(Mix(btn, 0.20), btn, Scale(btn, 0.74));
        r["AccentGradient"] = Gradient(Mix(accent, 0.20), accent, Scale(accent, 0.74));
    }

    private static LinearGradientBrush Gradient(Color top, Color mid, Color bottom) => new()
    {
        StartPoint = new System.Windows.Point(0, 0),
        EndPoint = new System.Windows.Point(0, 1),
        GradientStops = { new GradientStop(top, 0), new GradientStop(mid, 0.52), new GradientStop(bottom, 1) },
    };

    private static Color Parse(string hex) => (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
    private static Color Mix(Color c, double white) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * white), (byte)(c.G + (255 - c.G) * white), (byte)(c.B + (255 - c.B) * white));
    private static Color Scale(Color c, double k) => Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
}
