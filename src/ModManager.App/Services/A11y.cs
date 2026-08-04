using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace ModManager.App.Services;

/// <summary>
/// Screen-reader plumbing (vibe-glow wave 5, F-035). LiveSetting alone is only a politeness
/// hint — nothing is announced unless a LiveRegionChanged automation event is raised after the
/// text changes. WireLiveRegion hooks a status TextBlock once so every write announces.
/// </summary>
internal static class A11y
{
    public static void WireLiveRegion(TextBlock status)
        => status.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(status.Text)) return;
            FrameworkElementAutomationPeer.FromElement(status)
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        });
}
