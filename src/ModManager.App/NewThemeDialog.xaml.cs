using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ModManager.App.Services;
using ModManager.Core;
using Windows.ApplicationModel.DataTransfer;

namespace ModManager.App;

public sealed partial class NewThemeDialog : ContentDialog
{
    private readonly ThemeService _themes;

    /// <summary>The imported theme, set when the user successfully imports (Primary closed the dialog).</summary>
    public Theme? Imported { get; private set; }

    public NewThemeDialog(ThemeService themes)
    {
        InitializeComponent();
        ModManager.App.Services.DialogTheming.Apply(this); // vibe-glow wave 1: popup-scope theme brushes
        ModManager.App.Services.A11y.WireLiveRegion(StatusText); // vibe-glow wave 5: announce status writes
        _themes = themes;
    }

    private void OnCopyPrompt(object sender, RoutedEventArgs e)
    {
        var pkg = new DataPackage();
        pkg.SetText(ThemePrompt.Build(VibeBox.Text));
        Clipboard.SetContent(pkg);
        Show("Prompt copied — paste it into any AI chat, then bring the JSON back here.", "ThemeAccent");
    }

    private bool _warned; // second Import click after a readability warning proceeds to close
                          // (re-armed by OnJsonChanged — a corrected paste must re-import)

    private void OnJsonChanged(object sender, TextChangedEventArgs e) => _warned = false;

    private void OnImport(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_warned) return; // proceed with the close — the theme is already imported
        try
        {
            Imported = _themes.ImportUserTheme(JsonBox.Text);
            // Advisory only (vibe-glow F-046): the import always succeeds — user theming stays
            // unrestricted — but low-contrast pairs get named so nobody lives in an unreadable
            // theme by accident. Keep the dialog open long enough to read the warning.
            var warnings = ModManager.Core.Themes.ContrastReport(Imported);
            if (warnings.Count > 0)
            {
                Show($"Theme imported. Readability heads-up: {warnings[0]}"
                     + (warnings.Count > 1 ? $" (+{warnings.Count - 1} more pair{(warnings.Count > 2 ? "s" : "")})" : ""),
                     "ThemeWarning");
                args.Cancel = true; // stays open to show the note; Close (or Import again) proceeds
                _warned = true;
            }
        }
        catch (Exception ex)
        {
            Imported = null; // never let a stale earlier import survive a failed retry
            Show(ex.Message, "ThemeDanger");
            args.Cancel = true; // keep the dialog open so they can fix the JSON
        }
    }

    private void Show(string message, string brushKey)
    {
        StatusText.Text = message;
        if (Application.Current.Resources.TryGetValue(brushKey, out var v) && v is Brush b) StatusText.Foreground = b;
        StatusText.Visibility = Visibility.Visible;
    }
}
