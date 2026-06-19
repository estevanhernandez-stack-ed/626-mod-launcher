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
        // Hand the same VM instance to the tools row. The control reads installed tools + catalog
        // gaps off MainViewModel directly — no separate data context for this slim strip.
        ToolsRow.ViewModel = ViewModel;
#if FULL
        // Off-Store: let the live VM light up the Nexus surfaces the instant the feed hot-loads the
        // plugin on a first-ever connect (no rescan needed). FULL-only — the Store SKU has no feed.
        if (App.AppHost.Services.GetService<Services.PluginFeedSource>() is { } feed)
            ViewModel.WirePluginFeed(feed);
#endif
        // The collision prompt is a view concern (dialog + XamlRoot) — the VM builds the plan and
        // sequences intake, the window owns showing the dialog. null result = user cancelled.
        ViewModel.ConfirmReplacements = async plan =>
        {
            var dialog = new UpdateModsDialog(plan) { XamlRoot = Content.XamlRoot };
            return await dialog.ShowAsync() == ContentDialogResult.Primary ? dialog.ChosenReplacements() : null;
        };
        // Ban-risk acknowledgment is a view concern (dialog + XamlRoot). The VM owns the policy
        // decision (BanRiskRules.ShouldGateEnable) and only invokes this on a high-risk, un-acked game.
        ViewModel.ConfirmBanRiskEnable = ConfirmBanRiskEnableAsync;
        // Keep a session dismiss of the Vortex banner sticky across reloads: when the VM recomputes
        // the banner visibility, re-collapse the area if the user already dismissed it this session.
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (_suppressVortexBanner
                && (args.PropertyName == nameof(MainViewModel.OwnedBannerVisibility)
                    || args.PropertyName == nameof(MainViewModel.ReDeployedBannerVisibility)))
                VortexBannerArea.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        };
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (System.IO.File.Exists(iconPath)) AppWindow.SetIcon(iconPath);

        // Window backdrop (Solid / Mica / Acrylic). Applied on launch and re-applied whenever the
        // user picks a different value in Settings. Mica/Acrylic need a SystemBackdrop instance;
        // Solid clears it back to null so the Grid's Background ThemeBg fully fills the window.
        var appSettings = App.AppHost.Services.GetRequiredService<Services.AppSettingsService>();
        ApplyBackdrop(appSettings.Backdrop);
        appSettings.BackdropChanged += (_, _) => ApplyBackdrop(appSettings.Backdrop);

        Activated += OnFirstActivated;
    }

    private void ApplyBackdrop(Services.WindowBackdropKind kind)
    {
        SystemBackdrop = kind switch
        {
            Services.WindowBackdropKind.Mica    => new Microsoft.UI.Xaml.Media.MicaBackdrop(),
            Services.WindowBackdropKind.Acrylic => new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(),
            _                                   => null, // Solid — the Grid's ThemeBg fills the window
        };
        // A backdrop only shows where the root visual is transparent. Solid keeps ThemeBg painting
        // the central area; Mica/Acrylic clear it so the system backdrop tint reads through. The
        // title bar / command bar / footer keep their own opaque backgrounds either way - only the
        // central list area becomes translucent, matching how Win11 apps with Mica typically look.
        RootGrid.Background = kind == Services.WindowBackdropKind.Solid
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ThemeBg"]
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;

        var rp = App.AppHost.Services.GetRequiredService<Services.RestorePointService>();
        var interrupted = rp.DetectInterruptedClear();
        if (interrupted is not null)
            await HandleInterruptedClearAsync(rp, interrupted);

        await ViewModel.LoadAsync();

#if FULL
        // Startup fetch for already-connected users: if Nexus credentials are persisted from a
        // previous session, the user never triggers a ConnectAsync (so MaybeFetchOnConnectAsync
        // never fires). Kick off a debounced update check here — force:false lets FetchAsync
        // decide whether to first-install or honour the 24h debounce. Fire-and-forget; LoadAsync
        // already completed so the app is fully usable. The PluginLoaded event (wired in the
        // constructor via WirePluginFeed) carries the UI refresh when a new plugin is installed.
        if (App.AppHost.Services.GetService<Services.PluginFeedSource>() is { } feedOnStart
            && App.AppHost.Services.GetRequiredService<Services.NexusService>().IsConnected)
            _ = feedOnStart.FetchAsync(force: false);
#endif

        // After load: wire registry-changed so Safe Clear / Restore cause the mod list to repaint.
        var launcherService = App.AppHost.Services.GetRequiredService<Services.LauncherService>();
        launcherService.RegistryChanged += () =>
            DispatcherQueue.TryEnqueue(async () => await ViewModel.RefreshAsync());
    }

    private async Task HandleInterruptedClearAsync(Services.RestorePointService rp, ModManager.Core.RestorePoints.InterruptedClear ic)
    {
        if (ic.Sealed)
        {
            var d = new ContentDialog
            {
                Title = "A reset didn't finish",
                Content = "A previous reset was interrupted, but your setup was safely archived. Restore your saved setup now?",
                PrimaryButtonText = "Restore",
                CloseButtonText = "Not now",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
            };
            if (await d.ShowAsync() == ContentDialogResult.Primary)
                await rp.RestoreAsync(ic.Timestamp);
        }
        else
        {
            var d = new ContentDialog
            {
                Title = "A reset didn't finish",
                Content = "A previous reset was interrupted before it could be saved. Your setup is intact. Discard the incomplete archive?",
                PrimaryButtonText = "Discard",
                CloseButtonText = "Keep",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot,
            };
            if (await d.ShowAsync() == ContentDialogResult.Primary)
                rp.DiscardPartial(ic.Timestamp);
        }
    }

    // OneWay IsOn + this handler: ignore the programmatic set during reload (when the switch
    // already matches the committed state), act only on a real user flip.
    private async void OnModToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch sw || sw.DataContext is not ModRowViewModel row) return;

        // NOTE: the loader row (IsLoader) is DECOUPLED — it toggles only its own dinput8.dll via the
        // normal per-mod path below, NOT a cascade. Live testing confirmed the hosted mods\ mods sit
        // inert-but-harmless when the loader is off (they don't load, but cause no crash), so dragging
        // them to holding alongside the loader was solving a non-problem. The loader stays a visible,
        // independently-toggleable row; the hosted mods keep their own rows, untouched by this toggle.

        // Variant-family row: the switch toggles the FAMILY on/off. ON restores the last-active
        // variant (remembered by MainViewModel across rescans); OFF disables every variant after
        // recording which was on. Single-select variant CHIPS still pick which variant is active.
        if (row.HasVariantOptions)
        {
            var familyOn = row.VariantOptions.Any(v => v.Enabled);
            if (sw.IsOn == familyOn) return; // re-entry / programmatic set - no-op
            await ViewModel.ToggleFamilyAsync(row, sw.IsOn);
            return;
        }

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

    /// <summary>Confirm enabling mods on an anti-cheat/ban-risk game. Returns (proceed, dontWarnAgain).
    /// Cancel is the safe default; "Enable anyway" proceeds. Distinct copy from the co-op-desync
    /// warning — this is about getting your account banned, not a multiplayer mismatch.</summary>
    private async Task<(bool proceed, bool dontWarnAgain)> ConfirmBanRiskEnableAsync(string gameName)
    {
        var dontWarn = new CheckBox { Content = "Don't warn me again for this game", Margin = new Thickness(0, 12, 0, 0) };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "This game uses anti-cheat. Enabling mods for online play can get your account banned. Disabling is always reversible.",
        });
        body.Children.Add(dontWarn);
        var dialog = new ContentDialog
        {
            Title = $"Enable mods on {gameName}?",
            Content = body,
            PrimaryButtonText = "Enable anyway",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close, // cancel is the safe default
            XamlRoot = Content.XamlRoot,
        };
        var proceed = await dialog.ShowAsync() == ContentDialogResult.Primary;
        return (proceed, proceed && dontWarn.IsChecked == true);
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

        // Vanilla/modded is a second axis on top of the per-target list. Offer the OPPOSITE of the
        // current mode at the top: in modded mode you can switch to a clean vanilla run; in vanilla
        // mode you can restore your exact mod set. Switching steps loaders aside / restores them, then
        // launches. The per-target items below still launch the current state on the chosen target.
        if (ViewModel.CurrentLaunchMode == ModManager.Core.LaunchMode.Modded)
        {
            var vanilla = new MenuFlyoutItem { Text = "Play vanilla (no mods)" };
            vanilla.Click += OnPlayVanilla;
            menu.Items.Add(vanilla);
        }
        else
        {
            var modded = new MenuFlyoutItem { Text = "Play modded (restore mods)" };
            modded.Click += OnPlayModded;
            menu.Items.Add(modded);
        }
        if (ViewModel.LaunchTargets.Count > 0)
            menu.Items.Add(new MenuFlyoutSeparator());

        foreach (var target in ViewModel.LaunchTargets)
        {
            // The per-target list is the MECHANISM picker (Steam / Seamless / ME2) — vanilla vs modded
            // is the top item — so label by how-to-launch, never the target's legacy mode-named label.
            var item = new MenuFlyoutItem { Text = ViewModel.LaunchTargetMenuLabel(target), Tag = target };
            item.Click += OnLaunchTargetClick;
            menu.Items.Add(item);
        }
        if (ViewModel.LaunchTargets.Count == 0 && menu.Items.Count == 1)
            menu.Items.Add(new MenuFlyoutItem { Text = "No launch options for this game", IsEnabled = false });
    }

    private async void OnPlayVanilla(object sender, RoutedEventArgs e)
    {
        await ViewModel.StepAsideAndLaunchAsync();
    }

    private async void OnPlayModded(object sender, RoutedEventArgs e)
    {
        await ViewModel.RestoreAndLaunchAsync();
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
                    if (launcher is not null) await ViewModel.LaunchTargetExplicit(launcher);
                    else ViewModel.NotifyLauncherMissing();
                    break;
                case ContentDialogResult.Secondary:
                    await ViewModel.LaunchTargetExplicit(target);
                    break;
                // None (Cancel): do nothing.
            }
            return;
        }

        // A vanilla/steam launch with enabled direct-inject DLLs (dinput8 / Seamless / ReShade) crashes
        // at startup — those DLLs load into any process started from the game folder. Warn first, keep
        // the escape hatch. (RequiredLauncher games are handled by NeedsVanillaConfirm above.)
        if (ViewModel.NeedsDirectInjectStepAside(target))
        {
            var dialog = new ContentDialog
            {
                Title = "This will crash — DLL mods are loaded",
                Content = "Your enabled DLL mods (dinput8 / Seamless Co-op / ReShade) load into any program started from the game folder, including a plain Steam launch — and they crash a vanilla start. Disable them to run vanilla.",
                PrimaryButtonText = "Launch anyway",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await ViewModel.LaunchTargetExplicit(target);
            return;
        }

        await ViewModel.LaunchTargetExplicit(target);
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

    // Heart click: endorse ⇄ abstain this row's Nexus mod. The VM owns the write, the refusal mapping,
    // the rate-limit handling, and the in-place heart flip — this handler just routes the row through.
    private async void OnEndorse(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ModRowViewModel row) return;
        await ViewModel.ToggleEndorseAsync(row);
    }

    // Right-click → "Match to a mod…": opens the URL paste dialog, then hands the URL to the VM.
    private async void OnManualMatch(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ModRowViewModel row) return;
        var dialog = new ManualMatchDialog(row.DisplayName) { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await ViewModel.ManualMatchAsync(row, dialog.Url);
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
            var steam = App.AppHost.Services.GetRequiredService<Services.SteamService>();
            var dir = await Services.SaveLocator.DetectAsync(ludu, ctx.Game.GameName, ctx.Game.Engine, ctx.Game.GameRoot, ctx.Game.SteamAppId, steam.CurrentUserId64());
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
        var dialog = new ProfilesDialog(ctx, ViewModel) { XamlRoot = Content.XamlRoot };
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
                Background = (Brush)Application.Current.Resources["ThemePanel"],
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
        Add("[N]x",     "Active level of a variant family (the number is the level — e.g. 5x, 10x, 20x). Click another in the family to switch.");
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

    // Build the THEME dropdown menu fresh each time it opens. Lists every installed theme with a
    // checkmark on the active one, then a "+ New theme…" item at the bottom that opens the AI
    // generator. Theme-related actions live in one place this way.
    private void OnThemeMenuOpening(object sender, object e)
    {
        if (sender is not MenuFlyout menu) return;
        menu.Items.Clear();
        foreach (var theme in ViewModel.ThemeOptions)
        {
            var item = new MenuFlyoutItem { Text = theme.Name, Tag = theme };
            if (theme.Id == ViewModel.SelectedTheme?.Id)
                item.Icon = new FontIcon { Glyph = "" }; // checkmark
            item.Click += OnPickTheme;
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var newItem = new MenuFlyoutItem
        {
            Text = "+ New theme…",
            Icon = new FontIcon { Glyph = "" }, // paint brush (matches the old menu item)
        };
        newItem.Click += OnNewTheme;
        menu.Items.Add(newItem);
    }

    private void OnPickTheme(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is ModManager.Core.Theme t)
            ViewModel.SelectedTheme = t;
    }

    // The standalone Nexus connect/disconnect dialog moved into SettingsDialog as a section so all
    // user-identity stuff (avatar, theme, Nexus account, window transparency) lives in one place.
    // The toolbar Nexus status pill now calls OnSettings directly — the dot still signals state.

    // Open the active game's root folder in Explorer. Quiet glyph in the bottom status bar — Este
    // asked for "doesn't need to look like a button, could just say go to game folder." Errors are
    // swallowed: a missing path / shell failure isn't worth a toast.
    private void OnOpenGameFolder(object sender, RoutedEventArgs e)
    {
        var path = ViewModel.GameRootText;
        if (string.IsNullOrEmpty(path)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* path gone / shell unavailable — silent */ }
    }

    private async void OnSettings(object sender, RoutedEventArgs e)
    {
        var avatars     = App.AppHost.Services.GetRequiredService<Services.AvatarService>();
        var themes      = App.AppHost.Services.GetRequiredService<Services.ThemeService>();
        var appSettings = App.AppHost.Services.GetRequiredService<Services.AppSettingsService>();
        var hwnd        = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dialog      = new SettingsDialog(hwnd, avatars, themes, appSettings, ViewModel) { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();
        if (dialog.Changed)
        {
            // Refresh themes list (may have a new derived theme) + the title-bar icon binding.
            ViewModel.RefreshThemes();
            ViewModel.NotifyAppIconChanged();
            // Re-apply the window/taskbar icon: prefer the user's, fall back to the bundled.
            var iconPath = System.IO.File.Exists(avatars.AvatarIcoPath)
                ? avatars.AvatarIcoPath
                : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (System.IO.File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        }

        // ── Post-close hand-offs ─────────────────────────────────────────────────────────────────
        // WinUI 3 forbids nesting a second ContentDialog while the first is still open. For any
        // action that needs its own dialog (Reset, Restore, Delete), SettingsDialog sets a flag
        // and calls Hide() — ShowAsync() returns here with SettingsDialog fully closed, so we can
        // open the follow-up without conflict. At most one flag fires per Settings session.

        var rp = App.AppHost.Services.GetRequiredService<Services.RestorePointService>();

        if (dialog.OpenSafeClearRequested)
        {
            var sc = new SafeClearDialog(hwnd, rp, rp.NexusConnected) { XamlRoot = Content.XamlRoot };
            await sc.ShowAsync();
            // sc.Cleared is true on success. The UI refreshes via LauncherService.RegistryChanged → RefreshAsync.
        }
        else if (dialog.RestoreRequestedTimestamp is { } rts)
        {
            var confirm = new ContentDialog
            {
                Title = "Restore this setup?",
                Content = "Your current launcher state will be replaced with the archived setup.",
                PrimaryButtonText = "Restore",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
            };
            if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            {
                var r = await rp.RestoreAsync(rts);
                if (!r.Ok)
                {
                    string msg;
                    if (r.Conflicts.Count > 0)
                    {
                        var ids = string.Join(", ", r.Conflicts.Select(c => c.Id));
                        msg = $"Some game folders have moved since this restore point was created ({ids}). " +
                              "Update those game registrations and try again.";
                    }
                    else
                    {
                        msg = r.RefusedReason ?? "Restore failed.";
                    }
                    var err = new ContentDialog
                    {
                        Title = "Restore failed",
                        Content = msg,
                        CloseButtonText = "OK",
                        XamlRoot = Content.XamlRoot,
                    };
                    await err.ShowAsync();
                }
                // On success the UI refreshes via LauncherService.RegistryChanged → RefreshAsync.
            }
        }
        else if (dialog.DeleteRequestedTimestamp is { } dts)
        {
            var confirm = new ContentDialog
            {
                Title = "Delete this restore point?",
                Content = "The archived setup will be permanently removed.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot,
            };
            if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            {
                rp.DeleteRestorePoint(dts);
                // List refreshes next time Settings opens — RefreshRestorePoints() runs in the SettingsDialog constructor.
            }
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

    // Refresh Nexus stats for the installed library by polling by mod id — no archive needed.
    private async void OnNexusRefresh(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshNexusStatsAsync();
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
                        run.Click += async (_, _) => { dialog.Hide(); await ViewModel.RunInternalOption(opt); };
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
        // Frame as ONLINE vs OFFLINE mode, not "anti-cheat on/off + a hidden trade." The toggle is
        // the consequence; the user picks the mode they want. Old copy implied "off = play modded"
        // and quietly buried the offline-mode side effect.
        card.Children.Add(new TextBlock
        {
            Text = on
                ? "Currently in ONLINE mode (anti-cheat on) — official multiplayer works, file-based mods are blocked."
                : "Currently in OFFLINE mode (anti-cheat off) — Play loads mods. No official online until you switch back.",
            Opacity = 0.9, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
        });
        var toggle = new Button
        {
            Content = on
                ? "Switch to offline mode (anti-cheat off)"
                : "Switch to online mode (anti-cheat on)",
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

    // Session-level dismiss for the Vortex banner area (set from the Dismiss button).
    private bool _suppressVortexBanner;

    // "Take them over" / "Take over again" — take over every Vortex-owned + re-deployed location
    // for the active game, then rescan (the VM flips the banners off when nothing's owned anymore).
    private async void OnTakeOverGame(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.TakeOverGameAsync();
    }

    // Dismiss collapses the whole banner area for the session — a re-scan may re-show it (acceptable).
    private void OnDismissVortexBanner(object sender, RoutedEventArgs e)
    {
        VortexBannerArea.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        _suppressVortexBanner = true;
    }

    // If the row's folder is Vortex/MO2-owned (not yet taken over), offer to take it over first.
    // Returns true if the folder is now ours (taken over, or already ours / re-deployed), false if the
    // user declined — caller should abort the operation.
    private async Task<bool> EnsureNotVortexOwnedAsync(ModRowViewModel row)
    {
        var ctx = ViewModel.ActiveContextPublic;
        if (ctx is null) return true;
        var modFolder = row.ModFolderAbs;
        if (string.IsNullOrEmpty(modFolder)) return true;
        // The location that OWNS the mod is the mod folder's PARENT (mods live one level under the location).
        var locationAbs = System.IO.Path.GetDirectoryName(modFolder);
        if (string.IsNullOrEmpty(locationAbs)) return true;
        var res = ModManager.Core.ToolOwnership.Resolve(System.IO.Path.GetFullPath(locationAbs), ctx.TakenOver);
        if (res.State != ModManager.Core.OwnershipState.Owned) return true; // NotOwned or ReDeployed -> ours, proceed

        var dlg = new Vortex.VortexTakeoverDialog(row.DisplayName) { XamlRoot = Content.XamlRoot };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return false;
        await ViewModel.TakeOverFolderAsync(locationAbs);
        return true;
    }

    // Gated uninstall: the destructive op is always behind an explicit confirm. Family rows
    // uninstall every variant in the family - the confirm names the count so the blast radius
    // is in front of the user before they click Uninstall.
    private async void OnUninstall(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ModRowViewModel row) return;
        if (!await EnsureNotVortexOwnedAsync(row)) return;
        var (title, content) = row.HasVariantOptions
            ? ("Uninstall family?",
               $"Permanently delete \"{row.DisplayName}\" and all {row.VariantOptions.Count} variants? " +
               "This removes every variant's files and can't be undone.")
            : ("Uninstall mod?",
               $"Permanently delete \"{row.DisplayName}\"? This removes the mod's files and can't be undone.");
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (row.HasVariantOptions) await ViewModel.UninstallFamilyAsync(row);
        else                       await ViewModel.UninstallAsync(row);
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

    // Pencil icon next to the row. Tag carries the row VM (Task 8 convention), so the handler
    // works whether the row is a folder mod or a managed-folder mod. Single INI → straight to
    // the editor; multiple → quick picker first. Restore previous lives inside the editor itself.
    private async void OnEditIniClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not ModRowViewModel row) return;
        if (row.IniFiles.Count == 0) return; // shouldn't fire — IniIconVisibility gates it

        string? iniPath;
        if (row.IniFiles.Count == 1)
        {
            iniPath = row.IniFiles[0];
        }
        else
        {
            // Picker dialog for multiple INIs. Strings are paths from our own enumerate — safe to
            // render via the default ListView item template (textual).
            var list = new ListView { ItemsSource = row.IniFiles, SelectionMode = ListViewSelectionMode.Single };
            var picker = new ContentDialog
            {
                Title = $"Edit which INI in {row.DisplayName}?",
                Content = list,
                CloseButtonText = "Cancel",
                PrimaryButtonText = "Open",
                IsPrimaryButtonEnabled = false,
                XamlRoot = Content.XamlRoot,
            };
            list.SelectionChanged += (_, _) => picker.IsPrimaryButtonEnabled = list.SelectedItem is not null;
            var pickResult = await picker.ShowAsync();
            iniPath = pickResult == ContentDialogResult.Primary ? list.SelectedItem as string : null;
        }
        if (iniPath is null) return;

        var dataDir = ViewModel.GameDataDirPublic();
        if (string.IsNullOrEmpty(dataDir))
        {
            ViewModel.StatusText = "No game data dir available — can't snapshot INI history.";
            return;
        }

        var dialog = new IniEdit.IniEditorDialog(iniPath, dataDir, row.ModId) { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();
        if (dialog.StatusMessage is not null) ViewModel.StatusText = dialog.StatusMessage;
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
