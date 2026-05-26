using Microsoft.UI.Xaml.Controls;

namespace ModManager.App;

public sealed partial class ManualMatchDialog : ContentDialog
{
    /// <summary>The URL the user pasted, or empty if they cancelled.</summary>
    public string Url => UrlBox.Text ?? "";

    public ManualMatchDialog(string displayName)
    {
        InitializeComponent();
        IntroText.Text = $"Pick a Nexus or CurseForge mod page for \"{displayName}\".";
    }
}
