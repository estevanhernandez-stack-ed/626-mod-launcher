using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core;

namespace ModManager.App;

/// <summary>
/// The save-type offer at Play (spec 2026-11-07-save-type-offer-design): the save this launch reads has no
/// characters, and another mode's save does. It only asks; the view model does the snapshot, the copy and the
/// launch. Copies into vanilla warn, and an overhaul source (Reforged, Alt Saves) needs the box ticked first.
/// </summary>
public sealed partial class SaveOfferDialog : ContentDialog
{
    private readonly SaveOfferPlan _plan;
    private SaveOfferSource _chosen;

    /// <summary>The answer, or null when the dialog was closed (Escape): nothing launches.</summary>
    public SaveOfferAnswer? Answer { get; private set; }

    public SaveOfferDialog(SaveOfferPlan plan, string gameName)
    {
        InitializeComponent();
        ModManager.App.Services.DialogTheming.Apply(this);
        _plan = plan;
        _chosen = plan.Sources[0];

        var target = plan.Target.Label;
        TitleText.Text = $"No {target} characters yet";
        SentenceText.Text = plan.Sources.Count == 1
            ? $"Your characters are in your {plan.Sources[0].Type.Label} save. {gameName} in {target} mode reads a different file, so you'd start with an empty title screen."
            : $"You have characters in more than one save, but not in {target}. {gameName} in {target} mode reads its own file, so you'd start with an empty title screen. Pick the save to copy over.";

        if (plan.Sources.Count == 1)
        {
            SourceDetailText.Text = Detail(plan.Sources[0]);
            SourcesPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            SourceDetailText.Visibility = Visibility.Collapsed;
            foreach (var s in plan.Sources)
            {
                var label = $"{s.Type.Label}: {Who(s)}, last played {s.File.LastWriteUtc.ToLocalTime():MMM d}";
                var radio = new RadioButton { Content = label, GroupName = "SaveOfferSource", Tag = s, IsChecked = ReferenceEquals(s, _chosen) };
                AutomationProperties.SetAutomationId(radio, "SaveOfferSource." + s.Type.Extension.TrimStart('.'));
                AutomationProperties.SetName(radio, label);
                radio.Checked += (_, _) => { _chosen = s; Refresh(); };
                SourcesPanel.Children.Add(radio);
            }
        }
        Refresh();
    }

    private static string Who(SaveOfferSource s)
    {
        var f = s.File;
        if (f.FirstCharacter is not { Length: > 0 } name) return f.Characters == 1 ? "1 character" : $"{f.Characters} characters";
        var lead = f.FirstLevel is { } lvl ? $"{name}, level {lvl}" : name;
        return f.Characters > 1 ? $"{lead}, and {f.Characters - 1} more" : lead;
    }

    private string Detail(SaveOfferSource s)
        => $"Copy your {s.Type.Label} save over? {Who(s)} {(s.File.Characters > 1 ? "come" : "comes")} with it. Your {s.Type.Label} save stays as it is.";

    private void Refresh()
    {
        WarningText.Visibility = _chosen.Warning == SaveOfferWarning.None ? Visibility.Collapsed : Visibility.Visible;
        var ack = _chosen.Warning == SaveOfferWarning.Acknowledge;
        AcknowledgeBox.Visibility = ack ? Visibility.Visible : Visibility.Collapsed;
        var ackText = $"I understand this character may carry {_chosen.Type.Label} items into online play.";
        AcknowledgeBox.Content = ackText;
        AutomationProperties.SetName(AcknowledgeBox, ackText);
        if (!ack) AcknowledgeBox.IsChecked = false;
        CopyButton.IsEnabled = !ack || AcknowledgeBox.IsChecked == true;
    }

    private void OnAcknowledgeChanged(object sender, RoutedEventArgs e) => Refresh();

    private void Pick(SaveOfferAnswer answer)
    {
        Answer = answer;
        Hide();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_chosen.Warning == SaveOfferWarning.Acknowledge && AcknowledgeBox.IsChecked != true) return;
        Pick(new SaveOfferAnswer(SaveOfferChoice.CopyAndPlay, _chosen));
    }

    private void OnPlayWithout(object sender, RoutedEventArgs e) => Pick(new SaveOfferAnswer(SaveOfferChoice.PlayWithout));
    private void OnDontAsk(object sender, RoutedEventArgs e) => Pick(new SaveOfferAnswer(SaveOfferChoice.DontAsk));
}
