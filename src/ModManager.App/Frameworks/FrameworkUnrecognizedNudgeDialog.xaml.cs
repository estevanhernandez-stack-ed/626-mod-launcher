using Microsoft.UI.Xaml.Controls;

namespace ModManager.App.Frameworks;

public sealed partial class FrameworkUnrecognizedNudgeDialog : ContentDialog
{
    public const string FeedbackUrl =
        "https://github.com/estevanhernandez-stack-ed/626-mod-launcher/issues/new?labels=framework-request&title=Framework+request";

    public FrameworkUnrecognizedNudgeDialog(string archiveFileName)
    {
        InitializeComponent();
        FilenameText.Text = archiveFileName;
        // The SecondaryButtonClick fires for the "Open feedback link" button. We launch the URL
        // on click but the dialog still closes with the Secondary result — caller routes through
        // to mod intake either way (Primary or Secondary), since the user already saw the warning.
        this.SecondaryButtonClick += (_, _) =>
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(FeedbackUrl));
    }
}
