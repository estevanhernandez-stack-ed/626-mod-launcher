using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ModManager.Core;
using Windows.UI;

namespace ModManager.App.Services;

/// <summary>
/// Applies a Core <see cref="Theme"/> to the app's shared brushes. Because the brushes are
/// single instances referenced via {StaticResource}, setting their Color re-themes the live
/// UI with no reload. The 7 built-in themes come straight from Core; default is "626 Labs".
/// </summary>
public sealed class ThemeService
{
    public IReadOnlyList<Theme> Themes { get; } =
        Core.Themes.BuildThemeList(Core.Themes.BuiltinThemes, Array.Empty<(string, RawTheme)>());

    public Theme Default => Themes.FirstOrDefault(t => t.Id == "626-labs") ?? Themes[0];

    public void Apply(Theme t)
    {
        var res = Application.Current.Resources;
        Set(res, "ThemeBg", t["bg"]);
        Set(res, "ThemeTitleBg", t["title_bg"]);
        Set(res, "ThemeBarBg", t["bar_bg"]);
        Set(res, "ThemeFooterBg", t["footer_bg"]);
        Set(res, "ThemePanel", t["glass"]);
        Set(res, "ThemeBorder", t["border"]);
        Set(res, "ThemeAccent", t["accent"]);
        Set(res, "ThemeDanger", t["danger"]);
        Set(res, "ThemeInk", t["text"]);
        Set(res, "ThemeInkSoft", t["text_secondary"]);
    }

    private static void Set(ResourceDictionary res, string key, string hex)
    {
        if (res.TryGetValue(key, out var v) && v is SolidColorBrush brush) brush.Color = Parse(hex);
    }

    private static Color Parse(string hex)
    {
        hex = hex.TrimStart('#');
        var r = Convert.ToByte(hex.Substring(0, 2), 16);
        var g = Convert.ToByte(hex.Substring(2, 2), 16);
        var b = Convert.ToByte(hex.Substring(4, 2), 16);
        return Color.FromArgb(255, r, g, b);
    }
}
