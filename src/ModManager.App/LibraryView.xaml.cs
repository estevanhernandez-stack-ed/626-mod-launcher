using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.ViewModels;

namespace ModManager.App;

/// <summary>
/// The Game Library home surface — the landing view. Renders the recent cover strip and the searchable
/// list of every game 626 can see (B6): managed rows with tier / ban / loader chips + Play, and
/// unmanaged rows with Play through their store + Start managing. All behavior lives in
/// <see cref="LibraryViewModel"/>; this code-behind only routes control events to the VM commands. The shell (MainWindow) owns the view swap on Open — the VM
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

    // The cross-game Updates directory. The VM raises UpdatesRequested; the shell swaps the view in.
    private void OnOpenUpdates(object sender, RoutedEventArgs e) => ViewModel.RequestUpdatesView();

    // Start managing an installed game — the VM raises AddGameRequested; the shell runs the Add flow.
    private void OnStartManaging(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: UnmanagedGameRowViewModel row })
            ViewModel.StartManagingCommand.Execute(row);
    }

    // Play a game 626 doesn't manage, through its own store's launcher.
    private void OnPlayUnmanaged(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: UnmanagedGameRowViewModel row })
            ViewModel.PlayUnmanagedCommand.Execute(row);
    }
}

/// <summary>Picks the library row template by row kind (B6): one list, two kinds of row.</summary>
public sealed partial class LibraryRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Managed { get; set; }
    public DataTemplate? Unmanaged { get; set; }

    // Both templates are set in LibraryView.xaml's resources; a missing one is a markup bug to fail on.
    protected override DataTemplate SelectTemplateCore(object item)
        => (item is UnmanagedGameRowViewModel ? Unmanaged : Managed)!;

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}
