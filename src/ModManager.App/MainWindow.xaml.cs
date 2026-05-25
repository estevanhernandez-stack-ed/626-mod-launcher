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
        // The collision prompt is a view concern (dialog + XamlRoot) — the VM builds the plan and
        // sequences intake, the window owns showing the dialog. null result = user cancelled.
        ViewModel.ConfirmReplacements = async plan =>
        {
            var dialog = new UpdateModsDialog(plan) { XamlRoot = Content.XamlRoot };
            return await dialog.ShowAsync() == ContentDialogResult.Primary ? dialog.ChosenReplacements() : null;
        };
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (System.IO.File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
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
            await ViewModel.AddGameAsync(dialog.BuildInput(), dialog.ResolvedSaveDir);
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

    private async void OnLaunchTargetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: ModManager.Core.LaunchTarget target }) return;

        // Enforcement: a vanilla/steam launch while a required launcher is in force confirms first —
        // steer to the launcher, but keep vanilla reachable behind one explicit choice.
        if (ViewModel.NeedsVanillaConfirm(target))
        {
            var launcher = ViewModel.RequiredLauncherTarget();
            var dialog = new ContentDialog
            {
                Title = "Mods won't load this way",
                Content = "Your enabled mods/co-op won't load through a vanilla launch.",
                PrimaryButtonText = launcher is not null ? $"Use {launcher.Label}" : "Use launcher",
                SecondaryButtonText = "Launch vanilla anyway",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
            };
            switch (await dialog.ShowAsync())
            {
                case ContentDialogResult.Primary:
                    if (launcher is not null) ViewModel.LaunchTargetExplicit(launcher);
                    else ViewModel.NotifyLauncherMissing();
                    break;
                case ContentDialogResult.Secondary:
                    ViewModel.LaunchTargetExplicit(target);
                    break;
                // None (Cancel): do nothing.
            }
            return;
        }

        ViewModel.LaunchTargetExplicit(target);
    }

    // Set or clear a mod's MP-compat override from the badge flyout. Tag carries the choice.
    private void OnSetMpCompat(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string tag } item || item.DataContext is not ModRowViewModel row) return;
        ModManager.Core.MpRisk? value = tag switch
        {
            "Safe" => ModManager.Core.MpRisk.Safe,
            "Risky" => ModManager.Core.MpRisk.Risky,
            "SpOnly" => ModManager.Core.MpRisk.SpOnly,
            _ => null, // Auto / clear
        };
        ViewModel.SetMpOverride(row, value);
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

    // Connect Nexus Mods with a personal API key (the user's own — never baked). Validates before
    // storing; shows the connected account + a Disconnect option.
    private async void OnNexus(object sender, RoutedEventArgs e)
    {
        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85,
            Text = ViewModel.NexusConnected
                ? $"Connected as {ViewModel.NexusUser}. Paste a new key to switch, or disconnect."
                : "Get your personal API key from nexusmods.com -> account settings -> API access, then paste it here.",
        };
        var keyBox = new PasswordBox { PlaceholderText = "Nexus personal API key", Width = 380 };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(status);
        panel.Children.Add(keyBox);

        var dialog = new ContentDialog
        {
            Title = "Connect Nexus Mods",
            Content = panel,
            PrimaryButtonText = "Connect",
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
        };
        if (ViewModel.NexusConnected) dialog.SecondaryButtonText = "Disconnect";

        switch (await dialog.ShowAsync())
        {
            case ContentDialogResult.Primary:
                if (!string.IsNullOrWhiteSpace(keyBox.Password)) await ViewModel.ConnectNexusAsync(keyBox.Password);
                break;
            case ContentDialogResult.Secondary:
                ViewModel.DisconnectNexus();
                break;
        }
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

    // Flag: Seamless Co-op's files are present but its launcher is missing — co-op needs it.
    private async void OnCoopHint(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Seamless Co-op — launcher missing",
            Content = new TextBlock
            {
                Text = "Seamless Co-op's mod files are installed, but its launcher "
                       + "(launch_elden_ring_seamlesscoop.exe / ersc_launcher.exe) isn't here — and co-op only "
                       + "starts through that launcher, not the bare DLL.\n\nDownload the full Seamless Co-op mod, "
                       + "drop it on this window (or into the game folder), then Re-scan. A \"Play (Seamless Co-op)\" "
                       + "option will appear, and everyone in your group sets the same password in ersc_settings.ini.",
                TextWrapping = TextWrapping.Wrap,
            },
            CloseButtonText = "Got it",
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    // The Launch Options manager: internal options the app runs ("Play this"); external options the
    // user pastes into Steam (the exact string + Copy + plain-English steps).
    private async void OnLaunchOptions(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 12 };
        var dialog = new ContentDialog
        {
            Title = "Launch options",
            Content = new ScrollViewer { Content = panel, MaxHeight = 460 },
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
        };

        var panelBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ThemePanel"];

        void Build()
        {
            panel.Children.Clear();
            var options = ViewModel.ActiveLaunchOptions;
            if (options.Count == 0)
                panel.Children.Add(new TextBlock { Text = "No researched launch options for this game yet.", TextWrapping = TextWrapping.Wrap });

            foreach (var opt in options)
            {
                var card = new StackPanel { Spacing = 6 };
                card.Children.Add(new TextBlock { Text = opt.Title, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                card.Children.Add(new TextBlock { Text = opt.Detail, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });

                switch (opt.Kind)
                {
                    case ModManager.Core.LaunchOptionKind.AntiCheatToggle:
                        AddAntiCheatToggle(card, opt, Build);
                        break;

                    case ModManager.Core.LaunchOptionKind.Internal:
                        var run = new Button { Content = "▶ Play this", Margin = new Thickness(0, 2, 0, 0) };
                        run.Click += (_, _) => { dialog.Hide(); ViewModel.RunInternalOption(opt); };
                        card.Children.Add(run);
                        break;

                    default: // External
                        card.Children.Add(new TextBlock
                        {
                            Text = "Add this in Steam → right-click the game → Properties → General → Launch Options:",
                            TextWrapping = TextWrapping.Wrap, Opacity = 0.8,
                        });
                        card.Children.Add(new TextBox { Text = opt.SteamOptions ?? "", IsReadOnly = true, IsSpellCheckEnabled = false });
                        var copy = new Button { Content = "Copy" };
                        copy.Click += (_, _) => { var dp = new DataPackage(); dp.SetText(opt.SteamOptions ?? ""); Clipboard.SetContent(dp); };
                        card.Children.Add(copy);
                        break;
                }

                panel.Children.Add(new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(6), Background = panelBrush, Child = card });
            }
        }

        Build();
        await dialog.ShowAsync();
    }

    // Anti-cheat toggle card: shows current state and a button to flip it (reversible swap), then
    // rebuilds the dialog in place so the new state shows. Off = press Play for modded + offline.
    private void AddAntiCheatToggle(StackPanel card, ModManager.Core.LaunchOption opt, Action rebuild)
    {
        var state = ViewModel.AntiCheatStateOf(opt);
        if (state == ModManager.Core.AntiCheatState.Unsupported)
        {
            card.Children.Add(new TextBlock { Text = "Couldn't find the game files to toggle anti-cheat.", Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
            return;
        }

        var on = state == ModManager.Core.AntiCheatState.On;
        card.Children.Add(new TextBlock
        {
            Text = on ? "Anti-cheat is currently ON (online play, mods off)." : "Anti-cheat is currently OFF — press Play to start with mods, offline.",
            Opacity = 0.9, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
        });
        var toggle = new Button
        {
            Content = on ? "Turn anti-cheat OFF (play modded)" : "Turn anti-cheat ON (play online)",
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ThemeAccent"],
        };
        toggle.Click += (_, _) => { ViewModel.SetAntiCheat(opt, turnOn: !on); rebuild(); };
        card.Children.Add(toggle);
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

    // Readme viewer: captured-at-intake readme -> CurseForge description -> empty state. Rendered
    // to native controls only (no HTML/script), links gated through SafeUrl by the renderer.
    private async void OnShowReadme(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ModRowViewModel row) return;
        var dialog = new ContentDialog
        {
            Title = row.DisplayName,
            Content = ReadmeRenderer.Build(row.GetReadmeMarkdown()),
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
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
