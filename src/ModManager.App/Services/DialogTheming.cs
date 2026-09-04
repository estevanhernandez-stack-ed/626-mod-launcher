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
        "ButtonBackgroundPointerOver", "ButtonBackgroundPressed",
        "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed",
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

    /// <summary>
    /// Scope the shared keys onto any element that roots its own popup — a Flyout's content, most of
    /// all.
    ///
    /// <para>A Flyout does not live under the dialog that opened it, so merging into
    /// <c>SettingsDialog.Resources</c> never reaches it: the buttons inside a confirm flyout fall
    /// back to framework chrome while everything around them is themed. Resource lookup walks UP the
    /// tree, so applying this to the flyout's content root covers everything inside it.</para>
    ///
    /// <para>Brushes only — no dialog shell. A ContentDialog binds to the overload below; pass one
    /// through a <c>FrameworkElement</c>-typed variable and it would quietly land here instead and
    /// lose its title rail.</para>
    /// </summary>
    public static void Apply(FrameworkElement element)
        => element.Resources.MergedDictionaries.Add(SharedDictionary());

    /// <summary>The shared keys as a dictionary of the SAME brush instances ThemeService mutates —
    /// never copies, which would freeze at the moment of injection.</summary>
    private static ResourceDictionary SharedDictionary()
    {
        var app = Application.Current.Resources;
        var d = new ResourceDictionary();
        foreach (var key in SharedKeys)
        {
            if (app.TryGetValue(key, out var brush)) d[key] = brush; // same instance, stays mutable
        }
        return d;
    }

    public static void Apply(ContentDialog dialog)
    {
        var app = Application.Current.Resources;
        dialog.Resources.MergedDictionaries.Add(SharedDictionary());

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

    /// <summary>
    /// Keep a filled-danger button danger THROUGH hover and press (F-037 / F-072, and the reason
    /// <c>.claude/rules/vsm-danger-buttons.md</c> exists).
    ///
    /// <para>A style that only sets Background wins at rest and loses the moment the pointer
    /// arrives: the stock Button template's PointerOver/Pressed states re-resolve
    /// <c>ButtonBackground*</c> through ThemeResource. Element-scoped entries on the button itself
    /// outrank the framework dictionaries, and these are the SAME live brush instances
    /// <see cref="ThemeService"/> mutates — never new brushes, which would freeze the colour at the
    /// moment of injection and stop re-theming with the rest of the app.</para>
    /// </summary>
    public static void KeepDangerFilled(Button button)
    {
        var res = Application.Current.Resources;
        button.Resources["ButtonBackgroundPointerOver"] = res["ThemeDanger"];
        button.Resources["ButtonBackgroundPressed"] = res["ThemeDanger"];
        button.Resources["ButtonForegroundPointerOver"] = res["ThemeBg"];
        button.Resources["ButtonForegroundPressed"] = res["ThemeBg"];
    }

    /// <summary>
    /// The outlined variant of the same trap: a danger button that carries its danger in its
    /// <c>Foreground</c> and <c>BorderBrush</c> loses both to the PointerOver/Pressed states, which
    /// re-resolve <c>ButtonForeground*</c> and <c>ButtonBorderBrush*</c> through ThemeResource onto
    /// the ContentPresenter. So it reads danger at rest and turns into an ordinary button the moment
    /// you point at it — backwards, exactly as with the filled variant.
    ///
    /// <para>Same discipline: the live <c>ThemeDanger</c> instance, never a new brush.</para>
    /// </summary>
    public static void KeepDangerOutlined(Button button)
    {
        var res = Application.Current.Resources;
        button.Resources["ButtonForegroundPointerOver"] = res["ThemeDanger"];
        button.Resources["ButtonForegroundPressed"] = res["ThemeDanger"];
        button.Resources["ButtonBorderBrushPointerOver"] = res["ThemeDanger"];
        button.Resources["ButtonBorderBrushPressed"] = res["ThemeDanger"];
    }

    /// <summary>
    /// The same treatment for a ContentDialog's primary button, which is a template part that does
    /// not exist until the popup tree is built.
    ///
    /// <para>Hooked on the Title content's <c>Loaded</c> — never <c>Opened</c>, which races the popup
    /// wiring in both directions, the same trap <see cref="Apply"/> documents. Call this AFTER
    /// <see cref="Apply"/>: a code-built dialog's string Title only becomes a FrameworkElement once
    /// Apply has wrapped it.</para>
    /// </summary>
    public static void ApplyDangerPrimary(ContentDialog dialog)
    {
        if (dialog.Title is not FrameworkElement titleContent) return;

        titleContent.Loaded += (s, _) =>
        {
            // Search down from THIS dialog first (tight — can't hit another popup's PrimaryButton);
            // the walk-to-root pass is the fallback for template shapes where the part tree doesn't
            // hang off the dialog element.
            var primary = FindDescendant(dialog, "PrimaryButton") as Button;
            if (primary is null)
            {
                DependencyObject? node = (DependencyObject)s, root = null;
                while (node is not null) { root = node; node = VisualTreeHelper.GetParent(node); }
                primary = root is null ? null : FindDescendant(root, "PrimaryButton") as Button;
            }
            if (primary is null) return;
            KeepDangerFilled(primary);
        };
    }

    private static FrameworkElement? FindDescendant(DependencyObject root, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name) return fe;
            if (FindDescendant(child, name) is { } hit) return hit;
        }
        return null;
    }
}
