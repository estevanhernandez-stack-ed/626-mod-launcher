using Microsoft.UI.Xaml.Controls;
using ModManager.Core.Frameworks;

namespace ModManager.App.Frameworks;

public sealed partial class FrameworkInstallDialog : ContentDialog
{
    public FrameworkInstallDialog(
        KnownFramework framework,
        IReadOnlyList<string> filesToInstall,
        IReadOnlyList<string> filesThatWillBeReplaced,
        string installLocation)
    {
        InitializeComponent();
        HeadlineText.Text = $"{framework.DisplayName} — install at game root?";
        AuthorText.Text = $"by {framework.Author}  ·  {framework.GetUrl}";
        FilesList.ItemsSource = filesToInstall;
        LocationText.Text = $"Install location: {installLocation}";
        if (filesThatWillBeReplaced.Count > 0)
        {
            OverwriteWarning.Text =
                $"⚠ {filesThatWillBeReplaced.Count} existing file(s) will be replaced and " +
                $"backed up to _626mods/<game>/frameworks/{framework.FrameworkId}/backup/. " +
                $"Replaced: {string.Join(", ", filesThatWillBeReplaced)}";
            OverwriteWarning.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
    }
}
