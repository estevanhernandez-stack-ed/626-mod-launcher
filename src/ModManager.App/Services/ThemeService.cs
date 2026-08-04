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
    // Forge is the flagship (vibe-glow reveal). The user's saved pick outranks this at launch
    // (AppSettingsService.ThemeId, restored in MainViewModel's ctor — F-080); Default covers
    // first-run, a cleared setting, and a deleted user theme.
    public Theme Default => _themes.FirstOrDefault(t => t.Id == "forge") ?? _themes[0];

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
        Set(res, "ThemeWarning", t["warning"]);
        Set(res, "ThemeInk", t["text"]);
        Set(res, "ThemeInkSoft", t["text_secondary"]);
        Set(res, "ThemeInkMuted", t["text_muted"]);
        Set(res, "ThemeInkDim", t["text_dim"]);
        Set(res, "ThemeInfo", t["info"]);

        // Presenter-generated text (wave 3, F-047): button/checkbox content, ComboBox display,
        // TextBox input/header/placeholder — reached via system keys, not TextBlock styles.
        Set(res, "ButtonForeground", t["text"]);
        Set(res, "ButtonForegroundPointerOver", t["text"]);
        Set(res, "ButtonForegroundPressed", t["text_secondary"]);
        Set(res, "CheckBoxForegroundUnchecked", t["text"]);
        Set(res, "CheckBoxForegroundUncheckedPointerOver", t["text"]);
        Set(res, "CheckBoxForegroundUncheckedPressed", t["text"]);
        Set(res, "CheckBoxForegroundChecked", t["text"]);
        Set(res, "CheckBoxForegroundCheckedPointerOver", t["text"]);
        Set(res, "CheckBoxForegroundCheckedPressed", t["text"]);
        Set(res, "ComboBoxForeground", t["text"]);
        Set(res, "ComboBoxForegroundPointerOver", t["text"]);
        Set(res, "ComboBoxForegroundPressed", t["text"]);
        Set(res, "ComboBoxForegroundFocused", t["text"]);
        Set(res, "ComboBoxForegroundFocusedPressed", t["text"]);
        Set(res, "ComboBoxHeaderForeground", t["text_secondary"]);
        Set(res, "ComboBoxPlaceHolderForeground", t["text_muted"]);
        Set(res, "ComboBoxPlaceHolderForegroundPointerOver", t["text_muted"]);
        Set(res, "ComboBoxPlaceHolderForegroundPressed", t["text_muted"]);
        Set(res, "ComboBoxPlaceHolderForegroundFocused", t["text_muted"]);
        Set(res, "ComboBoxPlaceHolderForegroundFocusedPressed", t["text_muted"]);
        Set(res, "CheckBoxForegroundIndeterminate", t["text"]);
        Set(res, "CheckBoxForegroundIndeterminatePointerOver", t["text"]);
        Set(res, "CheckBoxForegroundIndeterminatePressed", t["text"]);
        Set(res, "TextControlForeground", t["text"]);
        Set(res, "TextControlForegroundPointerOver", t["text"]);
        Set(res, "TextControlForegroundFocused", t["text"]);
        Set(res, "TextControlPlaceholderForeground", t["text_muted"]);
        Set(res, "TextControlPlaceholderForegroundPointerOver", t["text_muted"]);
        Set(res, "TextControlPlaceholderForegroundFocused", t["text_muted"]);
        Set(res, "TextControlHeaderForeground", t["text_secondary"]);

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
        Set(res, "AccentButtonForeground", t["bg"]);
        Set(res, "AccentButtonForegroundPointerOver", t["bg"]);
        Set(res, "AccentButtonForegroundPressed", t["bg"]);
        Set(res, "AccentButtonForegroundDisabled", t["text_secondary"]);

        // Wave-1 system-brush overrides (plain app-level instances; dialogs additionally get the
        // same instances element-scoped via DialogTheming — see App.xaml field notes).
        Set(res, "AccentFillColorDefaultBrush", t["accent"]);
        Set(res, "AccentFillColorSecondaryBrush", t["accent"]);
        Set(res, "AccentFillColorTertiaryBrush", t["accent"]);
        Set(res, "TextOnAccentFillColorPrimaryBrush", t["bg"]);
        Set(res, "ToggleSwitchFillOn", t["accent"]);
        Set(res, "ToggleSwitchFillOnPointerOver", t["accent"]);
        Set(res, "ToggleSwitchFillOnPressed", t["accent"]);
        Set(res, "ToggleSwitchStrokeOn", t["accent"]);
        Set(res, "ToggleSwitchStrokeOnPointerOver", t["accent"]);
        Set(res, "ToggleSwitchStrokeOnPressed", t["accent"]);
        Set(res, "ToggleSwitchKnobFillOn", t["bg"]);
        Set(res, "ToggleSwitchKnobFillOnPointerOver", t["bg"]);
        Set(res, "ToggleSwitchKnobFillOnPressed", t["bg"]);
        Set(res, "ToggleButtonBackgroundChecked", t["accent"]);
        Set(res, "ToggleButtonBackgroundCheckedPointerOver", t["accent"]);
        Set(res, "ToggleButtonBackgroundCheckedPressed", t["accent"]);
        Set(res, "ToggleButtonForegroundChecked", t["bg"]);
        Set(res, "ToggleButtonForegroundCheckedPointerOver", t["bg"]);
        Set(res, "ToggleButtonForegroundCheckedPressed", t["bg"]);
        Set(res, "CheckBoxCheckBackgroundFillChecked", t["accent"]);
        Set(res, "CheckBoxCheckBackgroundFillCheckedPointerOver", t["accent"]);
        Set(res, "CheckBoxCheckBackgroundFillCheckedPressed", t["accent"]);
        Set(res, "CheckBoxCheckBackgroundStrokeChecked", t["accent"]);
        Set(res, "CheckBoxCheckBackgroundStrokeCheckedPointerOver", t["accent"]);
        Set(res, "CheckBoxCheckBackgroundStrokeCheckedPressed", t["accent"]);
        Set(res, "CheckBoxCheckGlyphForegroundChecked", t["bg"]);
        Set(res, "CheckBoxCheckGlyphForegroundCheckedPointerOver", t["bg"]);
        Set(res, "CheckBoxCheckGlyphForegroundCheckedPressed", t["bg"]);
        Set(res, "TextControlBorderBrushFocused", t["accent"]);
        Set(res, "TextControlSelectionHighlightColor", t["accent"]);
        Set(res, "HyperlinkButtonForeground", t["accent"]);
        Set(res, "HyperlinkButtonForegroundPointerOver", t["info"]);
        Set(res, "HyperlinkButtonForegroundPressed", t["accent"]);
        Set(res, "HyperlinkButtonForegroundDisabled", t["text_muted"]);
        Set(res, "TextFillColorPrimaryBrush", t["text"]);
        Set(res, "TextFillColorSecondaryBrush", t["text_secondary"]);

        // ComboBox flyout (game picker + VIEW dropdown).
        Set(res, "ComboBoxDropDownBackground", t["glass"]);
        Set(res, "ComboBoxDropDownBackgroundPointerOver", t["glass"]);
        Set(res, "ComboBoxDropDownBackgroundPointerPressed", t["glass"]);
        Set(res, "ComboBoxDropDownBorderBrush", t["border"]);
        Set(res, "ComboBoxDropDownForeground", t["text"]);
        Set(res, "ComboBoxItemBackgroundSelected", t["border"]);
        Set(res, "ComboBoxItemBackgroundSelectedPointerOver", t["border"]);
        Set(res, "ComboBoxItemBackgroundSelectedPressed", t["border"]);
        Set(res, "ComboBoxItemBackgroundPointerOver", t["border"]);
        Set(res, "ComboBoxItemForegroundSelected", t["text"]);
        Set(res, "ComboBoxItemForegroundSelectedPointerOver", t["text"]);
        Set(res, "ComboBoxItemForegroundPointerOver", t["text"]);

        // MenuFlyout (THEME dropdown, ... More menu, per-row right-click menus).
        Set(res, "MenuFlyoutPresenterBackground", t["glass"]);
        Set(res, "MenuFlyoutPresenterBorderBrush", t["border"]);
        Set(res, "MenuFlyoutItemForeground", t["text"]);
        Set(res, "MenuFlyoutItemBackgroundPointerOver", t["border"]);
        Set(res, "MenuFlyoutItemBackgroundPressed", t["border"]);
        Set(res, "MenuFlyoutItemForegroundPointerOver", t["text"]);
        Set(res, "MenuFlyoutItemForegroundPressed", t["text"]);
        Set(res, "MenuFlyoutSeparatorBackground", t["border"]);

        // InfoBar error severity (F-049) — panel body, danger badge, themed text.
        Set(res, "InfoBarErrorSeverityBackgroundBrush", t["glass"]);
        Set(res, "InfoBarErrorSeverityIconBackground", t["danger"]);
        Set(res, "InfoBarErrorSeverityIconForeground", t["bg"]);
        Set(res, "InfoBarTitleForeground", t["text"]);
        Set(res, "InfoBarMessageForeground", t["text_secondary"]);

        // Row hover glass (F-023).
        Set(res, "ListViewItemBackgroundPointerOver", t["glass"]);
        Set(res, "ListViewItemBackgroundPressed", t["glass"]);

        // Button hover glass (Este's flash catch): hover = glass, pressed = border tone.
        Set(res, "ButtonBackgroundPointerOver", t["glass"]);
        Set(res, "ButtonBackgroundPressed", t["border"]);
        Set(res, "ButtonBorderBrushPointerOver", t["border"]);
        Set(res, "ButtonBorderBrushPressed", t["border"]);

        // Glow rule (F-002): restyle every attached bloom from this theme's accent_bloom token.
        // A theme with alpha 0 reads flat — that's the token doing its job, not a bug.
        Bloom.OnThemeChanged(Parse(t["accent"]), Parse(t["danger"]), t.AccentBloom.Blur, t.AccentBloom.Alpha);
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
