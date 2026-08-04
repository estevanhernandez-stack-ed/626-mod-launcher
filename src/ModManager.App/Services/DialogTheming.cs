using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModManager.App.Services;

/// <summary>
/// Scopes the app's system-brush overrides onto a ContentDialog (vibe-glow wave 1, F-003/F-006).
/// Popup roots do not consult the app-level plain resource entries for framework template lookups
/// — dialog-hosted toggles, checkboxes, text boxes, and the primary button fall back to the OS
/// accent (verified per-pixel). Element-scope resources outrank the framework theme dictionaries
/// in every root, so each dialog gets a dictionary whose entries are the SAME brush instances
/// ThemeService mutates — dialogs re-theme live with the rest of the app.
/// Call from every ContentDialog constructor (XAML dialogs) or construction site (code-built).
/// </summary>
internal static class DialogTheming
{
    private static readonly string[] SharedKeys =
    {
        "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
        "TextOnAccentFillColorPrimaryBrush",
        "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed",
        "AccentButtonBackgroundDisabled", "AccentButtonForeground", "AccentButtonForegroundPointerOver",
        "AccentButtonForegroundPressed", "AccentButtonForegroundDisabled",
        "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed",
        "ToggleSwitchStrokeOn", "ToggleSwitchStrokeOnPointerOver", "ToggleSwitchStrokeOnPressed",
        "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed",
        "ToggleButtonBackgroundChecked", "ToggleButtonBackgroundCheckedPointerOver", "ToggleButtonBackgroundCheckedPressed",
        "ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed",
        "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundFillCheckedPressed",
        "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundStrokeCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPressed",
        "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver", "CheckBoxCheckGlyphForegroundCheckedPressed",
        "TextControlBorderBrushFocused", "TextControlSelectionHighlightColor",
        "HyperlinkButtonForeground", "HyperlinkButtonForegroundPointerOver",
        "HyperlinkButtonForegroundPressed", "HyperlinkButtonForegroundDisabled",
        "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush",
    };

    public static void Apply(ContentDialog dialog)
    {
        var app = Application.Current.Resources;
        var d = new ResourceDictionary();
        foreach (var key in SharedKeys)
        {
            if (app.TryGetValue(key, out var brush)) d[key] = brush; // same instance, stays mutable
        }
        dialog.Resources.MergedDictionaries.Add(d);
    }
}
