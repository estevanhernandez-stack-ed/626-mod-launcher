using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModManager.App.Services;

/// <summary>
/// Scopes the app's system-brush overrides onto a ContentDialog (vibe-glow wave 1, F-003/F-006).
/// The popup gap is per-KEY, not per-root: direct-lookup keys (ContentDialogBackground,
/// MenuFlyoutPresenterBackground) reach popups from plain app-level entries, but the
/// control-STATE keys below (ToggleSwitchFillOn, CheckBox*Checked, AccentFillColor*, ...) do
/// not — dialog-hosted toggles, checkboxes, text boxes, and the primary button fell back to the
/// OS accent (verified per-pixel; likely framework-dictionary aliases resolved at load).
/// Element-scope resources outrank the framework theme dictionaries, so each dialog gets a
/// dictionary whose entries are the SAME brush instances ThemeService mutates — dialogs
/// re-theme live with the rest of the app.
/// Call from every ContentDialog constructor (XAML dialogs) or construction site (code-built).
/// ThemeBrushContractTests asserts SharedKeys stays a subset of App.xaml's brush keys.
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
        "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed",
        "CheckBoxForegroundUnchecked", "CheckBoxForegroundUncheckedPointerOver", "CheckBoxForegroundUncheckedPressed",
        "CheckBoxForegroundChecked", "CheckBoxForegroundCheckedPointerOver", "CheckBoxForegroundCheckedPressed",
        "ComboBoxForeground", "ComboBoxPlaceholderForeground",
        "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused",
        "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver",
        "TextControlPlaceholderForegroundFocused", "TextControlHeaderForeground",
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
