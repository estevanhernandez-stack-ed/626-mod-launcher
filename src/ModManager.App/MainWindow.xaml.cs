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

    // Populate the Launch dropdown from the active game's targets each time it opens, so it
    // always reflects the current game (modded / Seamless Co-op / vanilla).
    private void OnLaunchMenuOpening(object sender, object e)
    {
        if (sender is not MenuFlyout menu) return;
        menu.Items.Clear();
        foreach (var target in ViewModel.LaunchTargets)
        {
            var item = new MenuFlyoutItem { Text = target.Label, Tag = target };
            item.Click += OnLaunchTargetClick;
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuFlyoutItem { Text = "No launch options for this game", IsEnabled = false });
    }

    private void OnLaunchTargetClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: ModManager.Core.LaunchTarget target })
            ViewModel.LaunchTargetExplicit(target);
    }

    private async void OnSaves(object sender, RoutedEventArgs e)
    {
        var svc = App.AppHost.Services.GetRequiredService<Services.LauncherService>();
        var ctx = svc.ActiveContext();
        if (ctx is null) return;

        // Find the save folder (Ludusavi by Steam id, then heuristics) if it's unset or stale.
        if (string.IsNullOrEmpty(ctx.SaveDir) || !System.IO.Directory.Exists(ctx.SaveDir))
        {
            var ludu = App.AppHost.Services.GetRequiredService<Services.LudusaviService>();
            var dir = await Services.SaveLocator.DetectAsync(ludu, ctx.Game.GameName, ctx.Game.Engine, ctx.Game.GameRoot, ctx.Game.SteamAppId);
            if (dir is not null) { svc.SetSaveDir(ctx.Game.Id, dir); ctx = svc.ActiveContext()!; }
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dialog = new SavesDialog(ctx, svc, hwnd) { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();
    }

    // ---------- inline load-order mode ----------

    private async void OnUnlockLoadOrder(object sender, RoutedEventArgs e) => await ViewModel.EnterLoadOrderAsync();
    private async void OnApplyOrder(object sender, RoutedEventArgs e) => await ViewModel.ApplyLoadOrderAsync();
    private async void OnCancelOrder(object sender, RoutedEventArgs e) => await ViewModel.CancelLoadOrderAsync();

    private void OnReorderCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => ViewModel.Renumber();

    private void OnJump(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (sender.DataContext is not ModRowViewModel row || double.IsNaN(args.NewValue)) return;
        ViewModel.MoveTo(row, (int)Math.Round(args.NewValue));
    }

    private async void OnProfiles(object sender, RoutedEventArgs e)
    {
        var ctx = App.AppHost.Services.GetRequiredService<Services.LauncherService>().ActiveContext();
        if (ctx is null) return;
        var dialog = new ProfilesDialog(ctx) { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();
        if (dialog.Changed) await ViewModel.RefreshAsync(); // a profile was applied
    }

    private async void OnNewTheme(object sender, RoutedEventArgs e)
    {
        var themes = App.AppHost.Services.GetRequiredService<Services.ThemeService>();
        var dialog = new NewThemeDialog(themes) { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Imported is not null)
            ViewModel.OnThemeImported(dialog.Imported);
    }

    private void OnFindMods(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string key) return;
        var name = ViewModel.ActiveGame?.Name;
        if (string.IsNullOrEmpty(name)) return;
        var url = Services.ModSites.SearchUrl(key, name);
        if (url is not null && ModManager.Core.SafeUrl.IsHttpUrl(url))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async void OnRedetect(object sender, RoutedEventArgs e) => await ViewModel.RedetectActiveAsync();

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
