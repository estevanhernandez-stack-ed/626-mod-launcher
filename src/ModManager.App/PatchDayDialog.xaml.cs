using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core;

namespace ModManager.App;

/// <summary>
/// The Patch Day question at Play (design 2026-10-16, section 3): the game updated, the loader predates it, and
/// the player has not said what to do yet. Shows the chip's own sentence and button labels (from
/// <see cref="GameStateStrip.PatchDayChip"/>, no second copy) and reports which choice was pressed. It only
/// asks: the view model routes the answer to the same methods the chip's buttons use, so the ban-risk gate
/// and every file change stay where they already are.
/// </summary>
public sealed partial class PatchDayDialog : ContentDialog
{
    /// <summary>The choice pressed, or null when the dialog was dismissed (Not now, or Escape).</summary>
    public PatchDayAction? Choice { get; private set; }

    public PatchDayDialog(GameStateChip chip)
    {
        InitializeComponent();
        ModManager.App.Services.DialogTheming.Apply(this); // vibe-glow wave 1: popup-scope theme brushes

        SentenceText.Text = chip.Detail;
        Label(PlayVanillaButton, chip.ActionLabel);
        Label(PlayModdedButton, chip.SecondaryActionLabel);
        Label(SnoozeButton, chip.TertiaryActionLabel);
    }

    private static void Label(Button b, string? text)
    {
        b.Content = text;
        AutomationProperties.SetName(b, text ?? "");
    }

    private void Pick(PatchDayAction action)
    {
        Choice = action;
        Hide();
    }

    private void OnPlayVanilla(object sender, RoutedEventArgs e) => Pick(PatchDayAction.PlayVanilla);
    private void OnPlayModded(object sender, RoutedEventArgs e) => Pick(PatchDayAction.PlayModded);
    private void OnSnooze(object sender, RoutedEventArgs e) => Pick(PatchDayAction.Snooze);
    private void OnNotNow(object sender, RoutedEventArgs e) => Hide();
}
