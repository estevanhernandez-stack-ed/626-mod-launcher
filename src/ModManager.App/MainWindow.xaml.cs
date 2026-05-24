using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace ModManager.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private bool _loaded;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<MainViewModel>();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Activated += OnFirstActivated;
    }

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        await ViewModel.LoadAsync();
    }

    // OneWay IsOn + this handler: ignore the programmatic set during reload (when the switch
    // already matches the committed state), act only on a real user flip.
    private async void OnModToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch sw || sw.DataContext is not ModRowViewModel row) return;
        if (sw.IsOn == row.Mod.Enabled) return;
        row.Enabled = sw.IsOn;
        await ViewModel.ToggleAsync(row);
    }

    private async void OnAddMods(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");
        var files = await picker.PickMultipleFilesAsync();
        if (files is { Count: > 0 })
            await ViewModel.AddModsAsync(files.Select(f => f.Path).ToList());
    }

    private async void OnAddGame(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var steamGames = App.AppHost.Services.GetRequiredService<Services.SteamService>().InstalledGames();
        var dialog = new AddGameDialog(hwnd, steamGames) { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.AddGameAsync(dialog.BuildInput());
    }

    private async void OnRemoveGame(object sender, RoutedEventArgs e)
    {
        var name = ViewModel.ActiveGame?.Name ?? "this game";
        var dialog = new ContentDialog
        {
            Title = "Remove game?",
            Content = $"Remove \"{name}\" from the launcher? Your mod files stay on disk — this only stops "
                      + "managing it here. Any disabled mods remain in the launcher's data folder.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RemoveActiveGameAsync();
    }

    // Gated uninstall: the destructive op is always behind an explicit confirm.
    private async void OnUninstall(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ModRowViewModel row) return;
        var dialog = new ContentDialog
        {
            Title = "Uninstall mod?",
            Content = $"Permanently delete \"{row.DisplayName}\"? This removes the mod's files and can't be undone.",
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.UninstallAsync(row);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null) e.DragUIOverride.Caption = "Install to active game";
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (paths.Count > 0) await ViewModel.AddModsAsync(paths);
    }
}
