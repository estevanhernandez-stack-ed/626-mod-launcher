using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ModManager.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace ModManager.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private bool _loaded;
    // Session-level opt-out for the "managed by another tool" toggle warning (set from the dialog).
    private bool _suppressOwnedToggleWarning;

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

        // Owned loader-driven mods (UE4SS manifest flip / BepInEx .dll rename): the managing tool
        // (Vortex/MO2) may overwrite the change on its next deploy. Warn before applying; cancel
        // reverts the switch.
        if (row.Mod.ReadOnly && row.Mod.Loader is "ue4ss" or "bepinex" && !_suppressOwnedToggleWarning)
        {
            if (!await ConfirmOwnedToggleAsync(row, turningOn: sw.IsOn))
            {
                sw.IsOn = row.Mod.Enabled; // revert visual; nothing applied (re-entry is a no-op via the guard above)
                return;
            }
        }

        row.Enabled = sw.IsOn;
        await ViewModel.ToggleAsync(row);
    }

    // One level of a multi-variant family — toggle that specific variant independently.
    private async void OnVariantClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.Controls.Primitives.ToggleButton tb
            || tb.DataContext is not VariantOptionVM opt) return;
        await ViewModel.ToggleVariantAsync(opt, tb.IsChecked == true);
    }

    /// <summary>Confirm flipping a mod whose folder another tool owns. Returns false on cancel.
    /// A "don't warn again" check sets a session-level opt-out.</summary>
    private async Task<bool> ConfirmOwnedToggleAsync(ModRowViewModel row, bool turningOn)
    {
        var owner = string.IsNullOrEmpty(row.Mod.Managed) ? "ANOTHER TOOL" : row.Mod.Managed!.ToUpperInvariant();
        // Describe the actual mechanism for each loader so the warning matches reality.
        var (mechanism, restoreNote) = row.Mod.Loader switch
        {
            "bepinex" => ("renames the plugin's .dll", "BepInEx plugins (.dll files) are typically tracked, so the rename is the most likely thing to be undone."),
            _         => ("changes the UE4SS manifest", "Mods enabled via an enabled.txt file are the most likely to be restored."),
        };
        var dontAsk = new CheckBox { Content = "Don't warn me again this session", Margin = new Thickness(0, 12, 0, 0) };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"\"{row.DisplayName}\" is managed by {owner}. Turning it {(turningOn ? "on" : "off")} here " +
                   $"{mechanism}, but {owner} may overwrite that on its next deploy. " + restoreNote,
        });
        body.Children.Add(dontAsk);
        var dialog = new ContentDialog
        {
            Title = $"Managed by {owner}",
            Content = body,
            PrimaryButtonText = turningOn ? "Turn on anyway" : "Turn off anyway",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        var ok = await dialog.ShowAsync() == ContentDialogResult.Primary;
        if (ok && dontAsk.IsChecked == true) _suppressOwnedToggleWarning = true;
        return ok;
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
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        // Batch mode wins when there's at least one approved row - register them in order and skip
        // the single-form path. Otherwise the existing single-game flow applies.
        if (dialog.BatchApproved.Count > 0)
        {
            foreach (var (input, resolvedSaveDir) in dialog.BatchApproved)
                await ViewModel.AddGameAsync(input, resolvedSaveDir);
            return;
        }

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

    private async void OnShowChipGlossary(object sender, RoutedEventArgs e)
    {
        var content = new StackPanel { Spacing = 8 };
        void Add(string chip, string explain)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var pill = new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Background = (Application.Current.Resources["ThemePanel"] as Brush) ?? new SolidColorBrush(Microsoft.UI.Colors.DimGray),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = chip,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    MinWidth = 56,
                    TextAlignment = TextAlignment.Center,
                },
            };
            row.Children.Add(pill);
            row.Children.Add(new TextBlock
            {
                Text = explain,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            });
            content.Children.Add(row);
        }
        Add("BOTH",     "Active in both your SP and MP loadouts.");
        Add("SP",       "Active only in your single-player loadout.");
        Add("MP",       "Active only in your multiplayer loadout.");
        Add("MP-SAFE",  "Author or verified-safe list says this works in MP.");
        Add("MP-RISKY", "Flagged risky in MP (anti-cheat / desync). Use with care.");
        Add("MP?",      "No MP stance claimed. Right-click the badge to set one.");
        Add("3x / 10x", "Active level of a variant family. Click another in the family to switch.");
        Add("VARIANT",  "One of several variants of the same mod — pick whichever fits.");
        Add("📄 readme",   "Open the mod's bundled readme.");
        Add("⚙ config",    "Open the config cockpit (UE4SS keybinds + settings).");
        Add("🗑 uninstall", "Permanently remove the mod from disk.");

        var dialog = new ContentDialog
        {
            Title = "What do these mean?",
            CloseButtonText = "Got it",
            DefaultButton = ContentDialogButton.Close,
            Content = new ScrollViewer { Content = content, MaxHeight = 420 },
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
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
        // Re-validate a stored key first so the account name + Premium/Free tag are current (and to
        // populate premium for a connection saved before it was tracked). Offline-safe.
        if (ViewModel.NexusConnected) await ViewModel.RefreshNexusAsync();

        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85,
            Text = ViewModel.NexusConnected
                ? $"Connected as {ViewModel.NexusAccountLine}. Paste a new key to switch, or disconnect."
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

    // Backfill metadata for installed mods by md5-matching the user's downloaded Nexus archives.
    private async void OnNexusBackfill(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        // Recurse — a downloads folder usually nests archives in per-mod subfolders.
        var archives = System.IO.Directory.GetFiles(folder.Path, "*.*", System.IO.SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
            .ToList();
        await ViewModel.BackfillNexusAsync(archives);
    }

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

    // Config cockpit: per-mod panel for editing config files and viewing keybinds/commands.
    // Config VALUE edits are intentionally allowed even on tool-owned folders (user-data).
    // Owned folders show a warning; the edit is not blocked. Mod CONTENT invariant is untouched.
    private async void OnShowCockpit(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ModRowViewModel row) return;
        if (!row.HasCockpit) return;
        await ShowCockpitForRowAsync(row);
    }

    private async Task ShowCockpitForRowAsync(ModRowViewModel row)
    {
        var (configs, keybinds, commands) = ViewModel.BuildCockpit(row.ModFolderAbs);
        var conflicts = ModManager.Core.Hotkeys.Conflicts(keybinds);

        var panelBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ThemePanel"];
        var accentBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ThemeAccent"];
        var dangerBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ThemeDanger"];
        var inkSoftBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ThemeInkSoft"];

        var root = new StackPanel { Spacing = 12, MinWidth = 480 };

        // Owned-folder warning (shown when Mod.ReadOnly)
        if (!string.IsNullOrEmpty(row.OwnedConfigWarning))
        {
            var warn = new Border { Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(4), Background = panelBrush };
            var warnText = new TextBlock
            {
                Text = row.OwnedConfigWarning,  // textContent — no raw mod data
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.9,
                Foreground = dangerBrush,
            };
            warn.Child = warnText;
            root.Children.Add(warn);
        }

        // Config files
        if (configs.Count == 0 && keybinds.Count == 0 && commands.Count == 0)
        {
            root.Children.Add(new TextBlock { Text = "No config files or Lua registrations found in this mod folder.", Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        }

        foreach (var cfg in configs)
        {
            var section = new StackPanel { Spacing = 8 };

            var header = new TextBlock
            {
                Text = cfg.FileName,  // filename from our own file scan, safe
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13,
            };
            section.Children.Add(header);

            if (cfg.Entries.Count == 0)
            {
                section.Children.Add(new TextBlock { Text = "No parseable entries.", Opacity = 0.5 });
            }

            foreach (var entry in cfg.Entries)
            {
                var row2 = new Grid { ColumnSpacing = 8 };
                row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var keyLabel = new TextBlock
                {
                    Text = entry.Key,   // key from parsed config — textContent only
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                if (!string.IsNullOrEmpty(entry.Description))
                    ToolTipService.SetToolTip(keyLabel, entry.Description);
                Grid.SetColumn(keyLabel, 0);

                var valueBox = new TextBox
                {
                    Text = entry.Value,  // value from parsed config — text binding only
                    IsSpellCheckEnabled = false,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 12,
                };
                Grid.SetColumn(valueBox, 1);

                // Capture loop vars for async closure
                var capturedCfgPath = cfg.Path;
                var capturedSection = entry.Section;
                var capturedKey = entry.Key;
                var capturedBox = valueBox;

                var saveBtn = new Button { Content = "Save", VerticalAlignment = VerticalAlignment.Center };
                saveBtn.Click += async (_, _) =>
                {
                    try { await ViewModel.SaveConfigValueAsync(capturedCfgPath, capturedSection, capturedKey, capturedBox.Text); }
                    catch (Exception ex) { ViewModel.StatusText = "Config save failed: " + ex.Message; }
                };
                Grid.SetColumn(saveBtn, 2);

                row2.Children.Add(keyLabel);
                row2.Children.Add(valueBox);
                row2.Children.Add(saveBtn);
                section.Children.Add(row2);
                // Each option stays a single line; its description lives on the key's hover tooltip
                // (set above) rather than a second line.
            }

            root.Children.Add(new Border { Padding = new Thickness(10), CornerRadius = new CornerRadius(6), Background = panelBrush, Child = section });
        }

        // cockpitDialog declared before building keybind rows so the Set click handlers can reference it
        ContentDialog? cockpitDialog = null;

        // Keybinds — editable for Lua-hardcoded binds (SourceFile != null), read-only for dynamic ones
        if (keybinds.Count > 0)
        {
            var kbSection = new StackPanel { Spacing = 6 };
            kbSection.Children.Add(new TextBlock { Text = "Keybinds", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13 });

            foreach (var bind in keybinds)
            {
                var modText = bind.Modifiers.Count > 0 ? string.Join("+", bind.Modifiers) + "+" : "";
                var sig = ModManager.Core.Hotkeys.Signature(bind);
                var hasConflict = conflicts.Contains(sig);

                if (bind.SourceFile is null)
                {
                    // Dynamic/unparsed bind — render read-only as before
                    var chip = new Border { Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(3), Background = panelBrush };
                    chip.Child = new TextBlock
                    {
                        Text = modText + bind.Key,   // key/modifier names from Lua regex scan — textContent
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 12,
                    };
                    kbSection.Children.Add(chip);
                }
                else
                {
                    // Lua-hardcoded bind with a known source file — editable
                    var bindRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

                    // Modifier prefix (read-only — modifier editing is deferred)
                    if (bind.Modifiers.Count > 0)
                    {
                        bindRow.Children.Add(new TextBlock
                        {
                            Text = modText,   // modifier names from Lua regex — textContent
                            VerticalAlignment = VerticalAlignment.Center,
                            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                            FontSize = 12,
                            Opacity = 0.7,
                        });
                    }

                    // Editable key TextBox
                    var capturedBind = bind;
                    var keyBox = new TextBox
                    {
                        Text = bind.Key,    // key name from Lua regex scan — text property only
                        Width = 80,
                        IsSpellCheckEnabled = false,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    bindRow.Children.Add(keyBox);

                    // Conflict marker (shown when this signature clashes with another bind)
                    if (hasConflict)
                    {
                        var conflictMark = new TextBlock
                        {
                            Text = "!",   // literal — not mod-supplied
                            Foreground = dangerBrush,
                            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                            VerticalAlignment = VerticalAlignment.Center,
                            FontSize = 14,
                        };
                        ToolTipService.SetToolTip(conflictMark, "Conflict: another bind uses the same key combo.");
                        bindRow.Children.Add(conflictMark);
                    }

                    var capturedKeyBox = keyBox;
                    var setBtn = new Button { Content = "Set", Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center };
                    setBtn.Click += async (_, _) =>
                    {
                        await ViewModel.RemapKeyBindAsync(capturedBind, capturedKeyBox.Text);
                        // Dismiss current cockpit and rebuild to reflect the new key
                        cockpitDialog?.Hide();
                        await ShowCockpitForRowAsync(row);
                    };
                    bindRow.Children.Add(setBtn);

                    kbSection.Children.Add(bindRow);
                }
            }

            root.Children.Add(new Border { Padding = new Thickness(10), CornerRadius = new CornerRadius(6), Background = panelBrush, Child = kbSection });
        }

        // Console commands (read-only)
        if (commands.Count > 0)
        {
            var cmdSection = new StackPanel { Spacing = 6 };
            cmdSection.Children.Add(new TextBlock { Text = "Console commands", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13 });
            foreach (var cmd in commands)
            {
                var row3 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var chip = new Border { Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(3), Background = panelBrush };
                chip.Child = new TextBlock
                {
                    Text = cmd.Name,  // command name from Lua regex scan — textContent
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 12,
                };
                var copyBtn = new Button { Content = "Copy", Padding = new Thickness(6, 2, 6, 2) };
                var capturedCmdName = cmd.Name;
                copyBtn.Click += (_, _) => { var dp = new Windows.ApplicationModel.DataTransfer.DataPackage(); dp.SetText(capturedCmdName); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp); };
                row3.Children.Add(chip);
                row3.Children.Add(copyBtn);
                cmdSection.Children.Add(row3);
            }
            root.Children.Add(new Border { Padding = new Thickness(10), CornerRadius = new CornerRadius(6), Background = panelBrush, Child = cmdSection });
        }

        var dialog = new ContentDialog
        {
            Title = $"{row.DisplayName} — Config",
            Content = new ScrollViewer { Content = root, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
        };
        cockpitDialog = dialog;
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
