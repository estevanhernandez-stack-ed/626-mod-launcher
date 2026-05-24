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
        _themes = themes;
    }

    private void OnCopyPrompt(object sender, RoutedEventArgs e)
    {
        var pkg = new DataPackage();
        pkg.SetText(ThemePrompt.Build(VibeBox.Text));
        Clipboard.SetContent(pkg);
        Show("Prompt copied — paste it into any AI chat, then bring the JSON back here.", "ThemeAccent");
    }

    private void OnImport(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try
        {
            Imported = _themes.ImportUserTheme(JsonBox.Text);
        }
        catch (Exception ex)
        {
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
