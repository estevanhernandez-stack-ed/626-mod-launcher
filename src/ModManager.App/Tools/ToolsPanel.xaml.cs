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

    // Task 9 wires the actual launch. Until then the click flips a status-bar message so the
    // affordance is visibly hot — easy to verify the binding is correct before we attach behavior.
    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ToolEntry entry && ViewModel is not null)
            ViewModel.StatusText = $"(Task 9 will launch {entry.DisplayName})";
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

    // Task 9 wires the file picker → AddModsAsync. Stub-status for now so the button is visibly wired.
    private void OnAddToolClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.StatusText = "(Task 9 will open file picker for tool install)";
    }
}
