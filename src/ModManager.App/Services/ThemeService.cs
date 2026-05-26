using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ModManager.Core;
using Windows.UI;
using CoreThemes = ModManager.Core.Themes;

namespace ModManager.App.Services;

/// <summary>
/// Applies a Core <see cref="Theme"/> to the app's shared brushes (single instances referenced
/// via {StaticResource}, so setting Color re-themes the live UI with no reload). Also loads
/// user themes from the data dir and imports new ones (validated against the 15-color contract).
/// </summary>
public sealed class ThemeService
{
    private List<Theme> _themes;

    public ThemeService() => _themes = BuildList();

    public IReadOnlyList<Theme> Themes => _themes;
    public Theme Default => _themes.FirstOrDefault(t => t.Id == "626-labs") ?? _themes[0];

    private static string UserDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModManagerBuilder", "themes");

    private static List<Theme> BuildList()
        => CoreThemes.BuildThemeList(CoreThemes.BuiltinThemes, LoadUserThemes()).ToList();

    private static IEnumerable<(string Id, RawTheme Data)> LoadUserThemes()
    {
        var outList = new List<(string, RawTheme)>();
        if (!Directory.Exists(UserDir)) return outList;
        foreach (var f in Directory.GetFiles(UserDir, "*.json"))
        {
            try { outList.Add((Path.GetFileNameWithoutExtension(f).ToLowerInvariant(), CoreThemes.ParseRawTheme(File.ReadAllText(f)))); }
            catch { /* skip a bad theme file */ }
        }
        return outList;
    }

    public void Reload() => _themes = BuildList();

    /// <summary>Validate + persist a theme from LLM-returned JSON; reload and return the new theme.</summary>
    public Theme ImportUserTheme(string json)
    {
        var raw = CoreThemes.ParseRawTheme(json);
        var name = raw.Tokens.TryGetValue("name", out var n) && !string.IsNullOrWhiteSpace(n) ? n : "Custom";
        var id = EnginePresets.Slugify(name);
        var normalized = CoreThemes.NormalizeTheme(id, raw)
            ?? throw new InvalidOperationException("That JSON isn't a complete theme — it's missing required color fields.");
        Directory.CreateDirectory(UserDir);
        File.WriteAllText(Path.Combine(UserDir, id + ".json"), json);
        Reload();
        return _themes.FirstOrDefault(t => t.Id == id) ?? normalized;
    }

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

        // ContentDialog + AccentButton resource overrides (declared in App.xaml). Same in-place
        // mutation pattern: WinUI's default popup templates look up these specific keys, and
        // because we re-color them in lockstep with the rest of the theme, every dialog in the
        // app re-themes live without needing an explicit RequestedTheme on each one.
        Set(res, "ContentDialogBackground", t["bg"]);
        Set(res, "ContentDialogForeground", t["text"]);
        Set(res, "ContentDialogBorderBrush", t["border"]);
        Set(res, "ContentDialogTopOverlay", t["glass"]);
        Set(res, "ContentDialogSeparatorBorderBrush", t["border"]);

        Set(res, "AccentButtonBackground", t["accent"]);
        Set(res, "AccentButtonBackgroundPointerOver", t["accent"]);
        Set(res, "AccentButtonBackgroundPressed", t["accent"]);
        Set(res, "AccentButtonBackgroundDisabled", t["border"]);
        Set(res, "AccentButtonForeground", "#000000");
        Set(res, "AccentButtonForegroundPointerOver", "#000000");
        Set(res, "AccentButtonForegroundPressed", "#000000");
        Set(res, "AccentButtonForegroundDisabled", t["text_secondary"]);
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
