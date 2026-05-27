using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.ViewModels;
using ModManager.Core.Tools;

namespace ModManager.App.Tools;

/// <summary>
/// Slim tools row that sits above the mod list. Renders three states from the active VM:
///   - <see cref="MainViewModel.Tools"/> as buttons (installed tools, click to launch in Task 9)
///   - <see cref="MainViewModel.MissingTools"/> as HyperlinkButton chips (catalog entries that
///     apply to this game but aren't installed — clicking opens the download page)
///   - An empty-state hint when both collections are empty
/// The + button is always visible so a user can install an ad-hoc tool zip (Task 9 wires the picker).
/// </summary>
public sealed partial class ToolsPanel : UserControl
{
    public ToolsPanel()
    {
        InitializeComponent();
    }

    /// <summary>The active main-window VM. Set by the host (MainWindow) right after the VM is built.
    /// Null until then — bindings on the XAML use <c>x:Bind … Mode=OneWay</c> so empty collections
    /// + collapsed visibility is the harmless default. Notifications for the visibility properties
    /// (HasTools / HasMissingTools / ToolsEmptyHintVisibility / ToolsRowVisible) flow from
    /// MainViewModel — they fire at every <c>ReloadModsAsync</c> tick.</summary>
    public MainViewModel? ViewModel { get; set; }

    // Snapshot-then-launch lives on the VM. The handler reads the tool entry off the button's Tag
    // (Task 8 convention — the slim row uses Tag rather than CommandParameter) and delegates.
    private async void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is ToolEntry entry && ViewModel is not null)
        {
            await ViewModel.LaunchToolAsync(entry);
        }
    }

    // Catalog "Get …" chip: open the Nexus / vendor page in the system browser. Some catalog entries
    // ship with a null/empty GetUrl (pin not landed yet) — guard so the click is a no-op there.
    private async void OnGetToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton btn && btn.Tag is KnownTool known
            && !string.IsNullOrEmpty(known.GetUrl))
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(known.GetUrl));
        }
    }

    // Open a file picker for a tool archive — the drop pipeline (ToolDetector.Classify → ToolIntake)
    // carves tools out of the mod intake, so we reuse AddModsAsync just like the drag-drop path.
    private async void OnAddToolClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.PromptAddToolAsync();
        }
    }
}
