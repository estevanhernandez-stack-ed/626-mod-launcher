using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModManager.App.Services;

/// <summary>
/// The one dialog for writing something new into a save on a high-risk game. The copy lives here and
/// nowhere else, so the character editor and the save-mod drop cannot drift apart. It only asks: the
/// caller decides whether to (BanRiskRules.ShouldGateSaveWrite) and records the tick
/// (BanRiskAckStore, BanRiskAck.WriteSaves). Not danger-filled: this is an informed choice about the
/// user's own file, and the snapshot is taken either way. It never calls a write safe.
/// </summary>
public static class SaveWriteRiskPrompt
{
    public static async Task<(bool proceed, bool dontAskAgain)> ShowAsync(XamlRoot root, string gameName)
    {
        var body = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "This game uses anti-cheat, and its publisher's rules can treat a modified save as a reason "
                 + "to ban an account. The launcher snapshots the save first, so you can put it back. It can't "
                 + "tell you whether an edited save is safe to take online, and it won't guess.",
        };
        AutomationProperties.SetAutomationId(body, "SaveWriteRiskBody");

        var cloud = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "If your saves sync to the cloud, the edited one syncs too.",
        };

        var dontAsk = new CheckBox { Content = "Don't ask again for this game's saves", Margin = new Thickness(0, 12, 0, 0) };
        AutomationProperties.SetAutomationId(dontAsk, "SaveWriteRiskDontAsk");

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(body);
        panel.Children.Add(cloud);
        panel.Children.Add(dontAsk);

        var dialog = new ContentDialog
        {
            Title = $"Write to a save on {gameName}?",
            Content = panel,
            PrimaryButtonText = "Write the save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close, // cancel is the safe default
            XamlRoot = root,
        };
        DialogTheming.Apply(dialog);
        var proceed = await dialog.ShowAsync() == ContentDialogResult.Primary;
        return (proceed, proceed && dontAsk.IsChecked == true);
    }
}
