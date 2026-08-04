using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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
        "ComboBoxForeground", "ComboBoxForegroundPointerOver", "ComboBoxForegroundPressed",
        "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed", "ComboBoxHeaderForeground",
        "ComboBoxPlaceHolderForeground", "ComboBoxPlaceHolderForegroundPointerOver",
        "ComboBoxPlaceHolderForegroundPressed", "ComboBoxPlaceHolderForegroundFocused",
        "ComboBoxPlaceHolderForegroundFocusedPressed",
        "CheckBoxForegroundIndeterminate", "CheckBoxForegroundIndeterminatePointerOver",
        "CheckBoxForegroundIndeterminatePressed",
        "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused",
        "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver",
        "TextControlPlaceholderForegroundFocused", "TextControlHeaderForeground",
        "HyperlinkButtonForeground", "HyperlinkButtonForegroundPointerOver",
        "HyperlinkButtonForegroundPressed", "HyperlinkButtonForegroundDisabled",
        "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush",
        // InfoBar error severity (F-049): severity brushes are framework-dictionary aliases —
        // the same popup gap as the control-state keys; scope the app's instances in.
        "InfoBarErrorSeverityBackgroundBrush", "InfoBarErrorSeverityIconBackground",
        "InfoBarErrorSeverityIconForeground", "InfoBarTitleForeground", "InfoBarMessageForeground",
        // Radius resources (wave 7, F-001): CheckBox's box radius binds the control's
        // CornerRadius whose Setter resolves ControlCornerRadius — app-level 0 doesn't reach
        // popup roots, so dialogs get the same instances. Not brushes; copied like any resource.
        "ControlCornerRadius", "OverlayCornerRadius",
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

        // Dialog shell for code-built dialogs (F-079): a plain string Title becomes rail + title,
        // so the ~22 code-built confirms share the XAML fleet's head. The UIA name a string Title
        // would have derived is preserved explicitly; the rail stays out of the accessibility
        // tree. (No eyebrow — code-built dialogs are transient confirms; the stencil stamp is
        // authored per-dialog in the XAML fleet.)
        if (dialog.Title is string s && !string.IsNullOrWhiteSpace(s))
        {
            var rail = new Border
            {
                Height = 3,
                Background = app.TryGetValue("ThemeAccent", out var accent) ? accent as Microsoft.UI.Xaml.Media.Brush : null,
                Margin = new Thickness(-24, 0, -24, 4),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(
                rail, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            var stack = new StackPanel { Spacing = 6 };
            stack.Children.Add(rail);
            stack.Children.Add(new TextBlock { Text = s, FontSize = (double)app["ViewTitleFontSize"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dialog, s);
            dialog.Title = stack;
        }

        // Dialog shell (vibe-glow F-008): the stock template pins its Title ContentControl to
        // HorizontalAlignment="Left", so title content sizes to itself and the 3px accent rail
        // the XAML dialogs put in their Title can't span the header. Stretch it once the template
        // exists (Opened) — a no-op for plain string titles. Re-entrant Apply calls just re-set
        // the same values.
        // The template parents the Title content only once the dialog's popup tree builds, and
        // Opened races that wiring in both directions — the content's own Loaded is the reliable
        // signal (it fires each ShowAsync, once the parent chain exists). Walk UP from our
        // StackPanel to the template's Title ContentControl; no popup hunting.
        if (dialog.Title is FrameworkElement titleContent)
        {
            titleContent.Loaded += (s, _) =>
            {
                DependencyObject? node = (DependencyObject)s;
                while (node is not null)
                {
                    if (node is ContentControl { Name: "Title" } title)
                    {
                        title.HorizontalAlignment = HorizontalAlignment.Stretch;
                        title.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                        return;
                    }
                    node = VisualTreeHelper.GetParent(node);
                }
            };
        }
    }
}
