using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.ViewModels;

namespace ModManager.App;

/// <summary>
/// The Game Library home surface — the landing view. Renders the recent cover strip, the searchable
/// all-games list (mod-state rows with tier / ban / loader chips + Play / Manage), and the collapsed
/// discovery lane. All behavior lives in <see cref="LibraryViewModel"/>; this code-behind only routes
/// control events to the VM commands. The shell (MainWindow) owns the view swap on Open — the VM
/// raises <see cref="LibraryViewModel.GameOpened"/> and the window swaps to the game's mod view.
/// </summary>
public sealed partial class LibraryView : UserControl
{
    public LibraryViewModel ViewModel { get; }

    public LibraryView(LibraryViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    // A recent cover card opens the game's mod view (same as Manage).
    private void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GameLibraryRowViewModel row })
            ViewModel.OpenGameCommand.Execute(row);
    }

    // Manage opens the game's mod view.
    private void OnManage(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GameLibraryRowViewModel row })
            ViewModel.OpenGameCommand.Execute(row);
    }

    // Play launches the game's current on-disk state (modded if mods are on, vanilla if they aren't).
    // The vanilla/modded toggle lives in the game view, not the home.
    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GameLibraryRowViewModel row })
            ViewModel.PlayCommand.Execute(row);
    }

    // + Add a store-discovered game — the VM raises AddGameRequested; the shell runs the Add flow.
    private void OnAddDiscovered(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DiscoveredGameViewModel row })
            ViewModel.AddDiscoveredCommand.Execute(row);
    }
}
