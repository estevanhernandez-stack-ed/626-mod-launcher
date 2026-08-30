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

    // The Library home surface + its VM. The app lands here; navigating into a game collapses it,
    // Home shows it again (reloading so recency / mod counts refresh). Built once, reused.
    private readonly LibraryViewModel _libraryVm;
    private readonly LibraryView _libraryView;

    private bool _loaded;
    // Session-level opt-out for the "managed by another tool" toggle warning (set from the dialog).
    private bool _suppressOwnedToggleWarning;

    public MainWindow()
    {
        InitializeComponent();
        ModManager.App.Services.A11y.WireLiveRegion(AppStatusText); // vibe-glow wave 5: announce status writes
        ViewModel = App.AppHost.Services.GetRequiredService<MainViewModel>();
        // Hand the same VM instance to the tools row. The control reads installed tools + catalog
        // gaps off MainViewModel directly — no separate data context for this slim strip.
        ToolsRow.ViewModel = ViewModel;

        // Glow rule (vibe-glow F-002): the two always-on sanctioned surfaces in the shell — the
        // primary action blooms accent, the ban-risk banner blooms danger. Color + blur + alpha
        // come from the applied theme's accent_bloom; ThemeService.Apply re-styles them live.
        Services.Bloom.Attach(LaunchBloomHost, LaunchSplitButton, Services.BloomToken.Accent);
        // The ban-risk text shadow went with the inline warning it decorated. The glow existed to
        // help a Border Padding="8,2" beside the theme picker get noticed; the same fact now leads
        // the game-state strip as a danger-coloured sentence under a danger-outlined chip. A glow on
        // top of that is decoration for a problem that no longer exists.

        // Active nav glow (F-077): a segment is lit exactly while its fill IS the shared accent
        // brush instance — the same signal the VM uses to mark it active. Reference equality is
        // deliberate: SegmentBrushFor returns the app's ThemeAccent instance or TransparentBrush.
        var accentBrush = Application.Current.Resources["ThemeAccent"];
        void WireSegment(Border host, Button segment) =>
            Services.Bloom.AttachStateGlow(host, segment, Services.BloomToken.Accent,
                () => ReferenceEquals(segment.Background, accentBrush), Button.BackgroundProperty);
        // Ctrl+, for Settings - the platform convention. Wired here rather than in XAML because the
        // comma key is VK_OEM_COMMA (188), which the VirtualKey enum has no named member for and the
        // XAML compiler will not take as a number.
        var settingsKey = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
            Key = (Windows.System.VirtualKey)188,
        };
        settingsKey.Invoked += OnSettingsAccelerator;
        RootGrid.KeyboardAccelerators.Add(settingsKey);

        WireSegment(ShowAllGlowHost, LoadoutAllSegment);
        WireSegment(ShowMpGlowHost, LoadoutMpSegment);
        WireSegment(ShowSpGlowHost, LoadoutSpSegment);

        // Library home: build the VM + view, mount into the overlay host, wire its navigation events.
        // Open (card/Manage) collapses the overlay onto the game's mod view; Add routes the discovered
        // game through the existing + Game flow, then reloads the home.
        _libraryVm = App.AppHost.Services.GetRequiredService<LibraryViewModel>();
        _libraryVm.GameOpened += OnLibraryGameOpened;
        _libraryVm.AddGameRequested += OnLibraryAddGameRequested;
        _libraryVm.UpdatesRequested += ShowUpdates;
        _libraryView = new LibraryView(_libraryVm);
        LibraryHost.Children.Add(_libraryView);
#if FULL
        // Off-Store: let the live VM light up the Nexus surfaces the instant the feed hot-loads the
        // plugin on a first-ever connect (no rescan needed). FULL-only — the Store SKU has no feed.
        if (App.AppHost.Services.GetService<Services.PluginFeedSource>() is { } feed)
        {
            ViewModel.WirePluginFeed(feed);
            // First-install consent: the feed asks before the first-ever plugin download. Shown from the
            // shell (not nested under SettingsDialog — the connect action hands back via ConnectNexusRequested).
            feed.ConfirmFirstInstallAsync = ShowFirstInstallConsentAsync;
        }
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
        // The safe-loader list lets the dialog surface "Launch / Get" buttons — installed loaders can
        // be started in one click; uninstalled loaders open the Get-it-here URL.
        ViewModel.ConfirmBanRiskEnable = ConfirmBanRiskEnableAsync;
        // Loose-root loader-disable warning is a view concern too. The VM owns the policy trigger
        // (disabling a loader-kind loose-root row); the window owns the dialog. Warn-and-proceed,
        // never a hard block — Cancel leaves the mod exactly as it was.
        ViewModel.ConfirmLooseLoaderDisable = ConfirmLooseLoaderDisableAsync;
        // Discovery review-before-adopt is a view concern too (dialog + XamlRoot). The VM sweeps,
        // classifies, and matches; Cancel (or an unwired delegate) means nothing gets adopted.
        ViewModel.ReviewDiscoveries = async proposals =>
        {
            var dialog = new DiscoveryReviewDialog(proposals) { XamlRoot = Content.XamlRoot };
            // Three outcomes now, not two: adopt what is installed, install what is only downloaded,
            // or do neither. Cancel still writes nothing at all.
            return await dialog.ShowAsync() switch
            {
                ContentDialogResult.Primary => new DiscoveryReviewOutcome(dialog.Approved, Array.Empty<ModManager.Core.Discovery.AdoptionProposal>()),
                ContentDialogResult.Secondary => new DiscoveryReviewOutcome(Array.Empty<ModManager.Core.Discovery.AdoptionProposal>(), dialog.ToInstall),
                _ => DiscoveryReviewOutcome.Nothing,
            };
        };
        // The unified identify run's single review — same view-owns-the-dialog split as above, but
        // it returns BOTH approved sections. Cancel (or an unwired delegate) writes nothing at all.
        ViewModel.ReviewIdentifyRun = async (adoptions, identifications) =>
        {
            var dialog = new IdentifyReviewDialog(adoptions, identifications) { XamlRoot = Content.XamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return (Array.Empty<ModManager.Core.Discovery.AdoptionProposal>(),
                        Array.Empty<(string, ModManager.Plugins.Abstractions.SourceSearchHit)>());
            return (dialog.ApprovedAdoptions(), dialog.ApprovedIdentifications());
        };
        // A chip's action opens a dialog, and dialogs live here. The view-model says WHICH FACT the
        // user pressed on; this decides what that opens. That split is what lets the ranking stay a
        // pure Core decision with no view types anywhere near it.
        ViewModel.StateChipActionRequested += OnStateChipAction;
        ViewModel.PropertyChanged += (_, args) =>
        {
            // The storefront is scoped to one game. Switching games from the title-bar switcher while it
            // is open would leave it labelled for the game you just left, so close it instead.
            if (args.PropertyName == nameof(MainViewModel.ActiveGame))
                HideCatalog();
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

        // Land on the Library home. LoadAsync above already resolved the active game + mods behind the
        // overlay, so tapping into a game is instant. Load() reads the registry + builds the rows.
        ShowLibrary();

        // One-time reconnect notice: an upgrade from a pre-OAuth build discarded the stored API key on
        // load (keys are non-compliant now). Nudge the user to reconnect via secure sign-in. Only fires
        // the launch where the key was discarded — once reconnected, the legacy file is gone for good.
        if (ViewModel.NexusLegacyKeyDiscarded)
        {
            var legacy = new ContentDialog
            {
                Title = "Reconnect your Nexus account",
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "Nexus now uses secure sign-in. Your old API key was removed — open Settings and "
                           + "click Connect Nexus account to reconnect.",
                },
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot,
            };
            ModManager.App.Services.DialogTheming.Apply(legacy); // vibe-glow wave 1: popup-scope theme brushes
            await legacy.ShowAsync();
        }

#if FULL
        // Startup fetch for already-connected users: if Nexus credentials are persisted from a
        // previous session, the user never triggers a ConnectAsync (so MaybeFetchOnConnectAsync
        // never fires). Kick off a debounced UPDATE check here — but only when a plugin is already
        // installed. If none is installed yet (NeedsFirstInstallConsent), do NOT auto-fetch on
        // startup: the first-ever install only happens through the consented connect path, never
        // silently at launch. Fire-and-forget; LoadAsync already completed so the app is fully
        // usable. The PluginLoaded event (wired via WirePluginFeed) carries the UI refresh.
        if (App.AppHost.Services.GetService<Services.PluginFeedSource>() is { } feedOnStart
            && App.AppHost.Services.GetRequiredService<Services.NexusService>().IsConnected
            && !feedOnStart.NeedsFirstInstallConsent())
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
            ModManager.App.Services.DialogTheming.Apply(d); // vibe-glow wave 1: popup-scope theme brushes
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
            ModManager.App.Services.DialogTheming.Apply(d); // vibe-glow wave 1: popup-scope theme brushes
            if (await d.ShowAsync() == ContentDialogResult.Primary)
                rp.DiscardPartial(ic.Timestamp);
        }
    }

    // ---------- Library home navigation ----------

    // Show the Library overlay (the landing view). Reloads its rows so recency + mod counts are fresh
    // every time the user returns — cheap read-only registry build, idempotent by design.
    private void ShowLibrary()
    {
        // The storefront is scoped to one game — going home leaves it behind.
        HideCatalog();
        // Home is the Updates view's parent surface; showing home means we are behind it, not under it.
        HideUpdates();
        _libraryVm.Load();
        LibraryHost.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        // On the home there's no current game — hide the game-context title-bar controls.
        GameTitleControls.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        LaunchSplitButton.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    // Collapse the Library overlay onto the game's mod view. The active game is already set in the
    // registry by the VM's OpenGame command before GameOpened fires; LoadAsync re-syncs the title-bar
    // switcher's selection to it and repaints the mod list for that game.
    private async void HideLibraryForGame()
    {
        HideUpdates();
        LibraryHost.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        // In a game's mod view — surface the game-context title-bar controls (name, More, Play).
        GameTitleControls.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        LaunchSplitButton.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        await ViewModel.LoadAsync();
    }

    // Home button in the title bar — return to the Library landing view.
    private void OnGoHome(object sender, RoutedEventArgs e) => ShowLibrary();

    // Keyboard access (F-025). Each handler acts only when its surface is live and marks the
    // accelerator handled ONLY then — otherwise the key keeps its normal meaning.
    private void OnFilterAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ViewModel.HasGame || GameTitleControls.Visibility != Visibility.Visible) return;
        if (CatalogHost.Visibility == Visibility.Visible) return; // storefront overlay owns the keys (S1)
        ModFilterBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void OnRefreshAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ViewModel.HasGame || GameTitleControls.Visibility != Visibility.Visible) return;
        if (CatalogHost.Visibility == Visibility.Visible) return; // no invisible rescans under the overlay (S1)
        if (ViewModel.RefreshCommand.CanExecute(null)) { ViewModel.RefreshCommand.Execute(null); args.Handled = true; }
    }

    // Wave 10. Same guard as the two above: act only when the game view is live and the storefront
    // overlay is not on top, and mark the key handled ONLY then, so it keeps its normal meaning
    // everywhere else.
    private bool GameKeysLive =>
        ViewModel.HasGame && GameTitleControls.Visibility == Visibility.Visible
        && CatalogHost.Visibility != Visibility.Visible;

    // Ctrl+, - the platform convention for Settings, and the one accelerator here that is NOT gated
    // on a live game view: Settings is reachable from the library home too.
    private void OnSettingsAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (CatalogHost.Visibility == Visibility.Visible) return;
        OnSettings(this, new RoutedEventArgs());
        args.Handled = true;
    }

    private void OnAddModsAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!GameKeysLive) return;
        OnAddMods(this, new RoutedEventArgs());
        args.Handled = true;
    }

    private void OnProfilesAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!GameKeysLive) return;
        OnProfiles(this, new RoutedEventArgs());
        args.Handled = true;
    }

    // Ctrl+1/2/3 are only safe to bind because wave 6 made these segments a FILTER. Until then they
    // enabled and disabled every mod in the game, and a number key that did that silently would have
    // been the worst control in the app.
    private void SetShowMode(string mode, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!GameKeysLive) return;
        if (ViewModel.SetModeCommand.CanExecute(mode)) { ViewModel.SetModeCommand.Execute(mode); args.Handled = true; }
    }

    private void OnShowAllAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args) => SetShowMode("all", args);
    private void OnShowMpAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args) => SetShowMode("mp", args);
    private void OnShowSpAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args) => SetShowMode("sp", args);

    private void OnEscapeAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        // Esc goes home from a game view — but never while any popup/dialog is open (Esc is
        // theirs), and never from the home itself.
        if (GameTitleControls.Visibility != Visibility.Visible) return;
        if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(Content.XamlRoot).Count > 0) return;
        // Esc means "clear the filter" while one is live — home is the SECOND Esc (S2).
        if (!string.IsNullOrEmpty(ViewModel.ModFilterText))
        {
            ViewModel.ModFilterText = "";
            args.Handled = true;
            return;
        }
        ShowLibrary();
        args.Handled = true;
    }

    // Space toggles the focused mod row (F-025) — routed through the row's own ToggleSwitch so
    // the real enable path (ban-risk gate, busy latch, revert-on-fail) runs, same as a click.
    private void OnModListKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Space) return;
        if (e.OriginalSource is not ListViewItem item || item.Content is not ViewModels.ModRowViewModel) return;
        if (FindToggle(item) is { IsEnabled: true, Visibility: Visibility.Visible } toggle)
        {
            toggle.IsOn = !toggle.IsOn;
            e.Handled = true;
        }

        static ToggleSwitch? FindToggle(DependencyObject root)
        {
            var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
                if (child is ToggleSwitch t) return t;
                if (FindToggle(child) is { } hit) return hit;
            }
            return null;
        }
    }

    // VM raised GameOpened after SetActiveGame — swap to that game's mod view.
    private void OnLibraryGameOpened(string gameId) => HideLibraryForGame();

    // Show the cross-game Updates overlay. Built fresh per open from the snapshot the Library VM's last
    // Load() already produced — no second pass over the metadata files, and no network call anywhere in
    // the surface. Back empties the host so a stale snapshot can never linger behind a hidden view.
    private void ShowUpdates()
    {
        var view = new UpdatesView(_libraryVm.UpdateSummaries);
        view.BackRequested += (_, _) => HideUpdates();
        // Open game routes back through the Library VM's normal open path (set active, raise GameOpened),
        // which the shell already handles — the Updates view never navigates or touches the registry.
        view.OpenGameRequested += (_, gameId) =>
        {
            HideUpdates();
            _libraryVm.OpenGameById(gameId);
        };
        UpdatesHost.Children.Clear();
        UpdatesHost.Children.Add(view);
        UpdatesHost.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
    }

    private void HideUpdates()
    {
        UpdatesHost.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        UpdatesHost.Children.Clear();
    }

    // VM raised AddGameRequested for a store-discovered game — add it, then reload the home so the
    // newly-added game leaves the discovery lane and appears in the all-games list.
    private async void OnLibraryAddGameRequested(ModManager.Core.InstalledGame game)
    {
        await AddDiscoveredGameAsync(game);
        ShowLibrary();
    }

    // Add a store-discovered game. When the engine is auto-detectable, register it in one step through
    // the same GameInput path the Steam quick-add uses (no guessing — Plan.Addable gates it). When it
    // isn't, hand off to the full + Game dialog so the user sets the engine — reusing the existing flow,
    // no new mechanism. Reversible: registration is additive; the launch mechanism is untouched.
    private async Task AddDiscoveredGameAsync(ModManager.Core.InstalledGame game)
    {
        var plan = ModManager.Core.SteamGameImport.Plan(
            new ModManager.Core.SteamImportCandidate(game.AppId, game.Name, game.InstallDir),
            Services.EngineScan.Detect(game.InstallDir));

        if (plan.Addable && plan.Input is not null)
        {
            await ViewModel.AddGameAsync(plan.Input);
            return;
        }

        // Undetectable engine — the full dialog lets the user pick it. Same flow the + Game button uses,
        // awaited: the caller repaints the home when this returns, and it must not do that with the
        // dialog still open.
        await AddGameViaDialogAsync();
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
            var family = row.Mod.Name;
            await ViewModel.ToggleFamilyAsync(row, sw.IsOn);
            KeepRowInView(family); // same reload, same lost scroll position
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
        var toggled = row.Mod.Name;
        await ViewModel.ToggleAsync(row);
        KeepRowInView(toggled);
    }

    /// <summary>
    /// Put the row the user just toggled back under their eyes.
    ///
    /// <para>A toggle ends in a full reload, and the reload assigns a NEW ObservableCollection to
    /// <c>Mods</c> — so the ListView gets a new ItemsSource and drops its scroll position to the
    /// top. Invisible on a small library; on a 194-row one it throws the user back to the start of
    /// the list on every single flip, which makes toggling several mods in a row miserable.</para>
    ///
    /// <para>Matched by mod name rather than by reference: the reload builds fresh
    /// <c>ModRowViewModel</c> instances, so the object the caller held is not in the new collection.
    /// A row that the active filter drops after toggling (say "enabled only") simply is not there —
    /// no match, no scroll, which is the correct outcome rather than a special case.</para>
    ///
    /// <para>Queued rather than called inline: the new ItemsSource has not been laid out yet at the
    /// moment the await returns, and ScrollIntoView against an unrealised list is a no-op.</para>
    /// </summary>
    private void KeepRowInView(string modName)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var again = ViewModel.Mods.FirstOrDefault(r => r.Mod.Name == modName);
            if (again is not null) ModListView.ScrollIntoView(again);
        });
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
        ModManager.App.Services.DialogTheming.Apply(dialog); // vibe-glow wave 1: popup-scope theme brushes
        var ok = await dialog.ShowAsync() == ContentDialogResult.Primary;
        if (ok && dontAsk.IsChecked == true) _suppressOwnedToggleWarning = true;
        return ok;
    }

    /// <summary>Warn before disabling a loose-root loader (the proxy DLL — dinput8 et al. — every
    /// ASI plugin loads through). Proceed-or-cancel only, NEVER a hard block: "Disable anyway"
    /// returns true and the reversible disable runs; Cancel returns false and nothing changes on
    /// disk. Mirrors the ConfirmBanRiskEnable wiring — the VM owns the policy, this owns the dialog.</summary>
    private async Task<bool> ConfirmLooseLoaderDisableAsync(string modName, string consequence)
    {
        var dialog = new ContentDialog
        {
            Title = "This mod is a loader",
            Content = consequence,
            PrimaryButtonText = "Disable anyway",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        ModManager.App.Services.DialogTheming.Apply(dialog); // vibe-glow wave 1: popup-scope theme brushes
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Confirm enabling mods on an anti-cheat/ban-risk game. Returns (proceed, dontWarnAgain).
    /// Cancel is the safe default; "Enable anyway" proceeds. Distinct copy from the co-op-desync
    /// warning — this is about getting your account banned, not a multiplayer mismatch.
    /// When ban-safe loaders are available for this game, the dialog surfaces them: installed loaders
    /// get a "Launch {name}" button (Process.Start); uninstalled ones get a "Get {name}" link that
    /// opens the download URL. This is guidance only — the ack gate is unchanged.</summary>
    private async Task<(bool proceed, bool dontWarnAgain)> ConfirmBanRiskEnableAsync(
        string gameName,
        IReadOnlyList<ViewModels.BanSafeLoaderOption> safeLoaders)
    {
        var dontWarn = new CheckBox { Content = "Don't warn me again for this game", Margin = new Thickness(0, 12, 0, 0) };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "This game uses anti-cheat. Enabling mods for online play can get your account banned. Disabling is always reversible.",
        });

        // Safe-loader guidance: "The safe way to mod this game:" + one button per safe loader.
        // Installed loaders → Launch button (Process.Start); not installed → Get button (opens URL).
        // Renders only when the catalog has ban-safe loaders for this game.
        if (safeLoaders.Count > 0)
        {
            body.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "The safe way to mod this game:",
                Margin = new Thickness(0, 8, 0, 0),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            var loaderPanel = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
            foreach (var opt in safeLoaders)
            {
                var btn = new Button
                {
                    Content = opt.LauncherPath is not null ? $"Launch {opt.DisplayName}" : $"Get {opt.DisplayName}",
                    Tag = opt,
                };
                btn.Click += (_, _) =>
                {
                    try
                    {
                        // Installed loader -> launch its exe; otherwise open the Get-it-here URL, gated
                        // through SafeUrl.IsHttpUrl like every other URL-open site in the app.
                        if (opt.LauncherPath is not null)
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = opt.LauncherPath,
                                UseShellExecute = true,
                                WorkingDirectory = System.IO.Path.GetDirectoryName(opt.LauncherPath) ?? "",
                            });
                        }
                        else if (ModManager.Core.SafeUrl.IsHttpUrl(opt.GetUrl))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = opt.GetUrl,
                                UseShellExecute = true,
                            });
                        }
                    }
                    catch { /* OS refusal — ignore silently; user sees the button did nothing */ }
                };
                loaderPanel.Children.Add(btn);
            }
            body.Children.Add(loaderPanel);
        }

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
        ModManager.App.Services.DialogTheming.Apply(dialog); // vibe-glow wave 1: popup-scope theme brushes
        var proceed = await dialog.ShowAsync() == ContentDialogResult.Primary;
        return (proceed, proceed && dontWarn.IsChecked == true);
    }

#if FULL
    /// <summary>First-plugin-download consent (first-ever connect only). The combined "connect + install the
    /// signed Nexus add-on" agreement — nothing is installed until the user agrees. Wired onto the feed's
    /// <see cref="Services.PluginFeedSource.ConfirmFirstInstallAsync"/> delegate; returns true to proceed.
    /// Shown from the shell so it's never nested under SettingsDialog (which hands the connect action back
    /// here via <c>ConnectNexusRequested</c>). FULL only — the Store SKU has no plugin feed.</summary>
    private async Task<bool> ShowFirstInstallConsentAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Connect Nexus and install the Nexus add-on?",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "To use Nexus features, 626 needs to (1) sign you in to Nexus in your browser, and "
                       + "(2) download a small signed add-on (the Nexus plugin) from the 626 plugin feed. "
                       + "Nothing is installed until you agree.",
            },
            PrimaryButtonText = "Connect and install",
            CloseButtonText = "Not now",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        ModManager.App.Services.DialogTheming.Apply(dialog); // vibe-glow wave 1: popup-scope theme brushes
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
#endif


    // Enabled-toggle glow (F-077): each realized row's switch gets a bloom behind it, lit while
    // IsOn. Containers are ListView-recycled — AttachStateGlow no-ops on an already-wired host,
    // and the IsOn callback reads the CURRENT row's state after rebind, so recycling stays honest.
    private void OnRowToggleLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || toggle.Parent is not Grid wrap
            || wrap.Children.Count == 0 || wrap.Children[0] is not Border host) return;
        Services.Bloom.AttachStateGlow(host, toggle, Services.BloomToken.Accent,
            () => toggle.IsOn && toggle.Visibility == Visibility.Visible,
            ToggleSwitch.IsOnProperty, UIElement.VisibilityProperty);
    }

    /// <summary>
    /// The NEEDS / MAY NEED chip (wave 8, item 5).
    ///
    /// <para>It used to be a <c>HyperlinkButton</c> straight to a GitHub releases page, so the app's
    /// answer to "you need UE4SS" was a list of files a first-time modder cannot choose between. That
    /// is where the round table's new modder closed the app — seconds after a successful toggle.</para>
    ///
    /// <para>The launcher CAN install it, and always could: a dropped archive goes through
    /// <c>AddModsAsync</c>, gets classified, shows <c>FrameworkInstallDialog</c> with exactly what
    /// lands where, and is written by <c>FrameworkInstaller.Install</c> validate-then-extract. Nothing
    /// here is a new install path — this is the existing one, finally reachable from the place that
    /// says it is needed.</para>
    /// </summary>
    private async void OnMissingFramework(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ViewModels.ModRowViewModel row) return;
        await ShowFrameworkOfferAsync(row.MissingFrameworkOffer, row.MissingFrameworkUrl);
    }

    /// <summary>Both doors, one dialog. Reached from the row chip and from the game-state strip's
    /// FRAMEWORK chip, so the two cannot drift into offering different things for the same fact.</summary>
    private async Task ShowFrameworkOfferAsync(ModManager.Core.FrameworkOffer offer, string? getUrl)
    {
        var dialog = new ContentDialog
        {
            Title = offer.Title,
            Content = new TextBlock { Text = offer.Consequence, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = offer.InstallLabel,
            CloseButtonText = "Not now",
            XamlRoot = Content.XamlRoot,
        };
        if (offer.GetLabel is not null) dialog.SecondaryButtonText = offer.GetLabel;
        ModManager.App.Services.DialogTheming.Apply(dialog);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // The same picker + the same intake as + Add mods. A framework archive is recognised on
            // the way in and routed to FrameworkInstallDialog, which shows the file list and the
            // destination before anything is written.
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add("*");
            var files = await picker.PickMultipleFilesAsync();
            if (files is { Count: > 0 })
                await ViewModel.AddModsAsync(files.Select(f => f.Path).ToList());
        }
        else if (result == ContentDialogResult.Secondary && ModManager.Core.SafeUrl.IsHttpUrl(getUrl))
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(getUrl!) { UseShellExecute = true }); }
            catch (Exception ex) { ViewModel.StatusText = ModManager.Core.ErrorRemedy.Describe(ex); }
        }
    }

    /// <summary>
    /// What the in-app storefront button does when it cannot browse (wave 8, item 3).
    ///
    /// <para>It used to render nothing at all in this situation, which is why the app presented as
    /// though the in-app storefront had never been built. Both remedies are one Settings page away, so
    /// the button says which one and opens it.</para>
    /// </summary>
    private async Task ShowBrowseRemedyAsync()
    {
        var dialog = new ContentDialog
        {
            Title = ModManager.Core.ModBrowseRules.InAppLabel,
            Content = new TextBlock { Text = ViewModel.BrowseButtonDetail, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Open settings",
            CloseButtonText = "Not now",
            XamlRoot = Content.XamlRoot,
        };
        ModManager.App.Services.DialogTheming.Apply(dialog);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            OnSettings(this, new RoutedEventArgs());
    }

    /// <summary>
    /// Point the launcher at this mod's real config file (wave 9).
    ///
    /// <para>This was a Settings section listing EVERY catalog direct-inject mod whether or not the
    /// user had it - a catalog browser filed under settings. It is per-mod configuration, so it lives
    /// on the mod. One declared path goes straight to the picker; several ask which one first, the
    /// same shape OnEditIniClick already uses.</para>
    /// </summary>
    private async void OnOverrideConfigPath(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ModRowViewModel row) return;
        if (!row.HasConfigOverride) return;

        string rel;
        if (row.DirectInjectConfigRelPaths.Count == 1)
        {
            rel = row.DirectInjectConfigRelPaths[0];
        }
        else
        {
            var list = new ListView
            {
                ItemsSource = row.DirectInjectConfigRelPaths,
                SelectionMode = ListViewSelectionMode.Single,
            };
            var pick = new ContentDialog
            {
                Title = $"Which config file for {row.DisplayName}?",
                Content = list,
                PrimaryButtonText = "Choose file…",
                CloseButtonText = "Cancel",
                XamlRoot = Content.XamlRoot,
            };
            ModManager.App.Services.DialogTheming.Apply(pick);
            if (await pick.ShowAsync() != ContentDialogResult.Primary) return;
            if (list.SelectedItem is not string chosen) return;
            rel = chosen;
        }

        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        foreach (var ext in new[] { ".ini", ".toml", ".cfg", "*" }) picker.FileTypeFilter.Add(ext);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        ViewModel.StatusText = ViewModel.SetDirectInjectConfigOverride(row.DirectInjectModId, rel, file.Path);
        await ViewModel.RefreshAsync();
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

    private async void OnAddGame(object sender, RoutedEventArgs e) => await AddGameViaDialogAsync();

    /// <summary>
    /// The + Game dialog flow, awaitable.
    ///
    /// <para>Kept separate from the <c>async void</c> click handler so callers can sequence work
    /// AFTER the dialog closes. <see cref="AddDiscoveredGameAsync"/> used to invoke the handler
    /// directly, which returned at the first await — so the caller's "now repaint the home" step ran
    /// while the dialog was still open, against a library that had not been added to yet.</para>
    /// </summary>
    private async Task AddGameViaDialogAsync()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var steamGames = App.AppHost.Services.GetRequiredService<Services.SteamService>().InstalledGames();
        var dialog = new AddGameDialog(hwnd, steamGames) { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        // Batch mode wins when there's at least one approved row - register them in order and skip
        // the single-form path. Otherwise the existing single-game flow applies.
        if (dialog.BatchApproved.Count > 0)
        {
            // sweep: false — a batch of N games would otherwise mean N sequential recursive discovery
            // sweeps and up to N modal review dialogs stacked under one busy state with no cancel.
            // Point at the manual re-run instead of running it N times unattended.
            foreach (var (input, resolvedSaveDir) in dialog.BatchApproved)
                await ViewModel.AddGameAsync(input, resolvedSaveDir, sweep: false);
            ViewModel.StatusText = $"Added {dialog.BatchApproved.Count} games. "
                + "Use More -> Find existing mods on each to sweep for hand-installed mods.";
        }
        else
        {
            await ViewModel.AddGameAsync(dialog.BuildInput(), dialog.ResolvedSaveDir);
        }

        // Single refresh site for BOTH success exits — the batch loop above and the single-game add.
        RefreshLibraryIfVisible();
    }

    /// <summary>
    /// Repaint the Library home when it is the surface the user is looking at.
    ///
    /// <para>The home grid is <see cref="LibraryViewModel"/>'s; adding a game runs
    /// <c>MainViewModel.LoadAsync</c>, which refreshes the game switcher and the mod list and never
    /// touches the library rows. Without this, adding from the home looks like nothing happened —
    /// and the "Added X." status line is no help, because the library host covers the shell's status
    /// bar on that surface.</para>
    ///
    /// <para><c>Load()</c> only, deliberately — not <c>ShowLibrary()</c>. The user never left home,
    /// so there is nothing to navigate to: <c>ShowLibrary</c> would additionally close the storefront
    /// and the Updates overlay and re-hide the game-context title-bar controls, none of which this
    /// moment asks for. <c>Load()</c> is idempotent and read-only.</para>
    /// </summary>
    private void RefreshLibraryIfVisible()
    {
        if (LibraryHost.Visibility == Visibility.Visible)
            _libraryVm.Load();
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
            ModManager.App.Services.DialogTheming.Apply(dialog); // vibe-glow wave 1: popup-scope theme brushes
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
            ModManager.App.Services.DialogTheming.Apply(dialog); // vibe-glow wave 1: popup-scope theme brushes
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
        // The mods this save was made with, taken from the UNFILTERED rows - a bundle must carry
        // what the save actually used, not what the search box happens to be showing.
        var mods = ViewModel.AllModRowsPublic
            .Select(r => new ModManager.Core.Transport.BundleMod(
                r.Mod.Name, r.Mod.Version, r.NexusModId, r.Mod.Enabled))
            .ToList();

        var dialog = new SavesDialog(ctx, svc, hwnd, mods) { XamlRoot = Content.XamlRoot };
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
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(6, 2, 6, 2),
                Background = (Brush)Application.Current.Resources["ThemePanel"],
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = chip,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = (double)Application.Current.Resources["MetaFontSize"],
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
        Add("BOTH",     "Safe in both single-player and multiplayer.");
        Add("SP",       "Active only in your single-player loadout.");
        Add("MP",       "Active only in your multiplayer loadout.");
        Add("MP-SAFE",  "Author or verified-safe list says this works in MP.");
        Add("MP-RISKY", "Flagged risky in MP (anti-cheat / desync). Use with care.");
        Add("MP?",      "No MP stance claimed. Right-click the badge to set one.");
        Add("[N]x",     "Active level of a variant family (the number is the level — e.g. 5x, 10x, 20x). Click another in the family to switch.");
        Add("VARIANT",  "One of several variants of the same mod — pick whichever fits.");
        Add("PLUGIN",   "Loose ASI plugin in the game root — loads through the ASI loader.");
        Add("SHADERS",  "Shader/addon package (ReShade addons and presets).");
        Add("LOADER",   "The DLL other mods load through — turning it off turns off every mod that injects through it.");
        Add("readme",   "Open the mod's bundled readme.");
        Add("config",    "Open the config cockpit (UE4SS keybinds + settings).");
        Add("uninstall", "Permanently remove the mod from disk.");

        var dialog = new ContentDialog
        {
            Title = "What do these mean?",
            CloseButtonText = "Got it",
            DefaultButton = ContentDialogButton.Close,
            Content = new ScrollViewer { Content = content, MaxHeight = 420 },
            XamlRoot = Content.XamlRoot,
        };
        ModManager.App.Services.DialogTheming.Apply(dialog); // vibe-glow wave 1: popup-scope theme brushes
        await dialog.ShowAsync();
    }

    private async void OnNewTheme(object sender, RoutedEventArgs e)
    {
        var themes = App.AppHost.Services.GetRequiredService<Services.ThemeService>();
        var dialog = new NewThemeDialog(themes) { XamlRoot = Content.XamlRoot };
        // Imported alone drives the apply: a readability-warned import cancels the Primary close
        // to show its note (args.Cancel), so the result is None when the user then closes — the
        // theme is real and installed either way (vibe-glow F-046 review fix).
        await dialog.ShowAsync();
        if (dialog.Imported is not null) ViewModel.OnThemeImported(dialog.Imported);
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
        // A restore rewrote files under the active game; the list on screen is now stale.
        if (dialog.RestoreHappened) await ViewModel.RefreshAsync();

        // ── Post-close hand-offs ─────────────────────────────────────────────────────────────────
        // WinUI 3 forbids nesting a second ContentDialog while the first is still open. For any
        // action that needs its own dialog (Reset, Restore, Delete), SettingsDialog sets a flag
        // and calls Hide() — ShowAsync() returns here with SettingsDialog fully closed, so we can
        // open the follow-up without conflict. At most one flag fires per Settings session.

        // Nexus OAuth connect: runs here (Settings closed) so the browser round-trip and the first-install
        // consent dialog never nest under the Settings ContentDialog.
        if (dialog.ConnectNexusRequested)
            await ViewModel.ConnectNexusAsync();

        var rp = App.AppHost.Services.GetRequiredService<Services.RestorePointService>();

        // The tool-configure hand-off used to live here, fed by the Settings tools inventory. Wave 9
        // moved that inventory out - ToolsPanel already had a Configure flyout on every tool chip - and
        // this branch survived as a condition that could never be true: read on every Settings close,
        // set by nothing. Dead code that still compiles is the quietest kind.
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
            ModManager.App.Services.DialogTheming.Apply(confirm); // vibe-glow wave 1: popup-scope theme brushes
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
                    ModManager.App.Services.DialogTheming.Apply(err); // vibe-glow wave 1: popup-scope theme brushes
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
            ModManager.App.Services.DialogTheming.Apply(confirm); // vibe-glow wave 1: popup-scope theme brushes
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

    // Browse Nexus in-app. The menu item stays bound to ViewModel.CatalogVisibility (true for BOTH the
    // rich and the simple path) rather than CatalogBrowseVisibility, and the SURFACE is chosen here: a
    // 0.12.x plugin only implements IModCatalog, so binding the item to the richer gate would make the
    // menu entry vanish for those users instead of degrading. Rich plugin -> the full-size storefront;
    // anything older -> the original simple-list dialog, unchanged.
    private async void OnBrowseNexusInApp(object sender, RoutedEventArgs e)
    {
        // Wave 8: this button stays on screen when in-app browsing is unavailable, because both
        // reasons it can be - signed out, plugin missing - are one Settings page away and the app used
        // to say nothing about either. A visible control that does nothing when pressed would be a
        // worse lie than the vanishing one, so the press opens the remedy.
        if (!ViewModel.BrowseCanAct)
        {
            await ShowBrowseRemedyAsync();
            return;
        }

        if (ViewModel.CatalogBrowseAvailable)
        {
            ShowCatalog();
            return;
        }

        var dlg = new NexusCatalogDialog(ViewModel, ViewModel.ActiveGame?.Name ?? "this game")
        {
            XamlRoot = Content.XamlRoot,
        };
        await dlg.ShowAsync();
    }

    // Show the storefront overlay. Built fresh per open so filters, paging and the loaded page never
    // carry over from a previous game or session; Back empties the host again, releasing the thumbnails.
    private void ShowCatalog()
    {
        var view = new NexusCatalogView(ViewModel, ViewModel.ActiveGame?.Name ?? "this game");
        view.BackRequested += (_, _) => HideCatalog();
        CatalogHost.Children.Clear();
        CatalogHost.Children.Add(view);
        CatalogHost.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
    }

    private void HideCatalog()
    {
        CatalogHost.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        CatalogHost.Children.Clear();
    }

    private async void OnRedetect(object sender, RoutedEventArgs e) => await ViewModel.RedetectActiveAsync();

    // Backfill metadata for installed mods by md5-matching the user's downloaded Nexus archives.
    private async void OnNexusBackfill(object sender, RoutedEventArgs e)
    {
        // Explain before costing the user a picker round-trip. Mirrors BackfillNexusAsync's own
        // precondition chain so the pre-check and the operation can never disagree.
        if (!ViewModel.CanBackfillFromDownloads()) return;

        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        // Enumeration lives in the VM so this entry and the unified run's downloads pass share one
        // definition of "an archive in a downloads folder".
        await ViewModel.BackfillNexusAsync(MainViewModel.EnumerateDownloadArchives(folder.Path));
    }

    // One prompt before anything runs — the downloads folder is the only pass that needs input,
    // and asking mid-run would interrupt a sweep the user is watching.
    private async void OnIdentifyMyMods(object sender, RoutedEventArgs e)
    {
        // Refuse BEFORE the prompt. The view model guards the slot too, but only once this handler
        // hands control back to it — which is after the user has chosen a folder. Asking someone a
        // question and then telling them it was never going to run is worse than not asking.
        if (ViewModel.RefuseIfLongOpRunning()) return;

        var ask = new ContentDialog
        {
            Title = "Also check a downloads folder?",
            Content = "If you have a folder of downloaded mod archives, we can match them exactly by file hash. "
                      + "Otherwise we'll match by name, which is a good guess but still a guess.",
            PrimaryButtonText = "Choose folder",
            CloseButtonText = "Skip",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        ModManager.App.Services.DialogTheming.Apply(ask);

        string? folder = null;
        if (await ask.ShowAsync() == ContentDialogResult.Primary)
        {
            var picker = new FolderPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add("*");
            folder = (await picker.PickSingleFolderAsync())?.Path;
        }

        await ViewModel.IdentifyMyModsAsync(folder);
    }

    // The Stop button beside the busy ring. Cancellation is the VM's to own — the window only
    // forwards the click.
    private void OnCancelLongOperation(object sender, RoutedEventArgs e) => ViewModel.CancelLongOperation();

    private async void OnEnrichMetadata(object sender, RoutedEventArgs e) => await ViewModel.EnrichMetadataAsync();

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
        ModManager.App.Services.DialogTheming.Apply(dialog); // vibe-glow wave 1: popup-scope theme brushes
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
        ModManager.App.Services.DialogTheming.Apply(dialog); // vibe-glow wave 1: popup-scope theme brushes

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
                card.Children.Add(new TextBlock { Text = opt.Title, FontSize = (double)Application.Current.Resources["RowTitleFontSize"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                card.Children.Add(new TextBlock { Text = opt.Detail, TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["ThemeInkSoft"] });

                switch (opt.Kind)
                {
#if FULL
                    // FULL only — the EAC-disable toggle is stripped from the Store SKU (LaunchOptions.For
                    // also filters the option out for Store, so this case never fires there).
                    case ModManager.Core.LaunchOptionKind.AntiCheatToggle:
                        AddAntiCheatToggle(card, opt, Build);
                        break;
#endif

                    case ModManager.Core.LaunchOptionKind.Internal:
                        var run = new Button { Content = "Play this", Margin = new Thickness(0, 2, 0, 0) };
                        run.Click += async (_, _) => { dialog.Hide(); await ViewModel.RunInternalOption(opt); };
                        card.Children.Add(run);
                        break;

                    default: // External
                        card.Children.Add(new TextBlock
                        {
                            Text = "Add this in Steam → right-click the game → Properties → General → Launch Options:",
                            TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["ThemeInkSoft"],
                        });
                        card.Children.Add(new TextBox { Text = opt.SteamOptions ?? "", IsReadOnly = true, IsSpellCheckEnabled = false });
                        var copy = new Button { Content = "Copy" };
                        copy.Click += (_, _) => { var dp = new DataPackage(); dp.SetText(opt.SteamOptions ?? ""); Clipboard.SetContent(dp); };
                        card.Children.Add(copy);
                        break;
                }

                panel.Children.Add(new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(0), Background = panelBrush, Child = card });
            }
        }

        Build();
        await dialog.ShowAsync();
    }

#if FULL
    // Anti-cheat toggle card: shows current state and a button to flip it (reversible swap), then
    // rebuilds the dialog in place so the new state shows. Off = press Play for modded + offline.
    // FULL only — stripped from the sealed Store SKU (the option is filtered out + AntiCheat is absent).
    private void AddAntiCheatToggle(StackPanel card, ModManager.Core.LaunchOption opt, Action rebuild)
    {
        var state = ViewModel.AntiCheatStateOf(opt);
        if (state == ModManager.Core.AntiCheatState.Unsupported)
        {
            card.Children.Add(new TextBlock { Text = "Couldn't find the game files to toggle anti-cheat.", Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["ThemeInkSoft"], TextWrapping = TextWrapping.Wrap });
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
            Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["ThemeInkSoft"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
        });
        var toggle = new Button
        {
            Content = on
                ? "Switch to offline mode (anti-cheat off)"
                : "Switch to online mode (anti-cheat on)",
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ThemeBg"],
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ThemeAccent"],
        };
        toggle.Click += (_, _) => { ViewModel.SetAntiCheat(opt, turnOn: !on); rebuild(); };
        card.Children.Add(toggle);
    }
#endif

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
        ModManager.App.Services.DialogTheming.Apply(dialog); // vibe-glow wave 1: popup-scope theme brushes
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RemoveActiveGameAsync();
    }

    // Session dismissal used to be two bool flags here plus a PropertyChanged listener writing
    // Visibility straight onto named panels — necessary because the x:Bind was OneWay to the VM and
    // any recompute would overwrite a raw write. The strip made that unnecessary: a dismissal is a
    // set of chip ids in the view-model, so it survives a rebuild by construction and is cleared when
    // the active game changes.

    // "Take them over" / "Take over again" — take over every Vortex-owned + re-deployed location
    // for the active game, then rescan (the VM flips the banners off when nothing's owned anymore).
    private async void OnTakeOverGame(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.TakeOverGameAsync();
    }

    // One chip id, one thing it opens. Every target here already existed — this wave moved where
    // the user presses, not what happens when they do.
    private void OnStateChipAction(string chipId)
    {
        var e = new RoutedEventArgs();
        switch (chipId)
        {
            case "launch-options": OnLaunchOptions(this, e); break;
            case "coop-launcher": OnCoopHint(this, e); break;
            case "setup-drift": OnCheckSetup(this, e); break;
            case "vortex-managed":
            case "vortex-redeployed": OnTakeOverGame(this, e); break;
            // An ACTION, not a dismissal: it re-records the build baseline the warning compares to.
            case "steam-updated": ViewModel.DismissBuildWarningCommand.Execute(null); break;
            case "framework-missing": _ = OfferMissingFrameworkAsync(); break;
        }
    }

    // The FRAMEWORK chip in the game-state strip. Wave 7 wired this straight to a download page and
    // said in its own PR that item 5 would fix it; this is that. It opens the SAME offer the row chip
    // opens, so two surfaces cannot drift into saying different things about one missing framework.
    private async Task OfferMissingFrameworkAsync()
    {
        var dep = ViewModel.MissingFrameworks.FirstOrDefault();
        if (dep is null) return;
        await ShowFrameworkOfferAsync(
            ModManager.Core.FrameworkOfferRules.For(dep.Name, dep.GetUrl, soft: false),
            dep.GetUrl);
    }

    private async void OnCheckSetup(object sender, RoutedEventArgs e)
    {
        var game = ViewModel.ActiveContextPublic?.Game;
        if (game is null) return;

        // Refuse here, before the dialog, rather than after the user has filled one in — the save
        // re-checks and is the authority. Chiefly this catches re-opening during a data-dir move,
        // which has no Stop and leaves the window looking idle.
        if (ViewModel.RefuseIfBusy()) return;

        var repair = App.AppHost.Services.GetRequiredService<Services.RegistrationRepairService>();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dialog = new GameSetupDialog(hwnd, game, repair) { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();

        if (dialog.Proposed is not { } proposed) return;

        // The move-or-pin decision, and the save, happen HERE rather than inside the dialog: WinUI 3
        // permits one ContentDialog per XamlRoot, so a confirm cannot open while the setup dialog is up.
        // FALSE, not true. This is the answer to "did the user ask for a move", and nobody asked when
        // no plan surfaced here. The save re-previews and can find a plan this dialog did not (the
        // folder came into existence in between, say) — defaulting to true would move gigabytes of the
        // user's only copy of their disabled mods without ever putting the question on screen. Pinning
        // moves nothing, so a wrong default in this direction costs a settings key, not files.
        var move = false;
        if (dialog.MoveDataDirRequested is { } plan)
        {
            var confirm = new ContentDialog
            {
                Title = "Move this game's launcher data?",
                Content = $"You changed the game folder. This game's launcher data — disabled mods, "
                          + $"profiles, saves, installed tools — is {plan.FileCount} files at {plan.From}.\n\n"
                          + "Move it next to the new folder, or leave it where it is. Leaving it works "
                          + "fine; nothing is lost either way — it records this folder in the game's "
                          + "setup, so the launcher keeps using it from here on.\n\n"
                          + "Cancel abandons the whole edit, including the other fields you changed.",
                PrimaryButtonText = "Move it",
                SecondaryButtonText = "Leave it",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = Content.XamlRoot,
            };
            Services.DialogTheming.Apply(confirm);
            var answer = await confirm.ShowAsync();
            if (answer == ContentDialogResult.None) return;      // Cancel: change nothing
            move = answer == ContentDialogResult.Primary;
        }

        await ViewModel.SaveRegistrationAsync(repair, game, proposed, move);
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
        ModManager.App.Services.DialogTheming.Apply(dialog); // vibe-glow wave 1: popup-scope theme brushes
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
        ModManager.App.Services.DialogTheming.Apply(dialog); // vibe-glow wave 1: popup-scope theme brushes
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
            ModManager.App.Services.DialogTheming.Apply(picker); // vibe-glow wave 1: popup-scope theme brushes
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
            var warn = new Border { Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(0), Background = panelBrush };
            var warnText = new TextBlock
            {
                Text = row.OwnedConfigWarning,  // textContent — no raw mod data
                TextWrapping = TextWrapping.Wrap,
                Foreground = dangerBrush,
            };
            warn.Child = warnText;
            root.Children.Add(warn);
        }

        // Config files
        if (configs.Count == 0 && keybinds.Count == 0 && commands.Count == 0)
        {
            root.Children.Add(new TextBlock { Text = "No config files or Lua registrations found in this mod folder.", Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["ThemeInkDim"], TextWrapping = TextWrapping.Wrap });
        }

        foreach (var cfg in configs)
        {
            var section = new StackPanel { Spacing = 8 };

            var header = new TextBlock
            {
                Text = cfg.FileName,  // filename from our own file scan, safe
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = (double)Application.Current.Resources["BodyFontSize"],
            };
            section.Children.Add(header);

            if (cfg.Entries.Count == 0)
            {
                section.Children.Add(new TextBlock { Text = "No parseable entries.", Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["ThemeInkDim"] });
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
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                    FontSize = (double)Application.Current.Resources["BodyFontSize"],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                if (!string.IsNullOrEmpty(entry.Description))
                    ToolTipService.SetToolTip(keyLabel, entry.Description);
                Grid.SetColumn(keyLabel, 0);

                var valueBox = new TextBox
                {
                    Text = entry.Value,  // value from parsed config — text binding only
                    IsSpellCheckEnabled = false,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                    FontSize = (double)Application.Current.Resources["BodyFontSize"],
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

            root.Children.Add(new Border { Padding = new Thickness(10), CornerRadius = new CornerRadius(0), Background = panelBrush, Child = section });
        }

        // cockpitDialog declared before building keybind rows so the Set click handlers can reference it
        ContentDialog? cockpitDialog = null;

        // Keybinds — editable for Lua-hardcoded binds (SourceFile != null), read-only for dynamic ones
        if (keybinds.Count > 0)
        {
            var kbSection = new StackPanel { Spacing = 6 };
            kbSection.Children.Add(new TextBlock { Text = "Keybinds", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = (double)Application.Current.Resources["BodyFontSize"] });

            foreach (var bind in keybinds)
            {
                var modText = bind.Modifiers.Count > 0 ? string.Join("+", bind.Modifiers) + "+" : "";
                var sig = ModManager.Core.Hotkeys.Signature(bind);
                var hasConflict = conflicts.Contains(sig);

                if (bind.SourceFile is null)
                {
                    // Dynamic/unparsed bind — render read-only as before
                    var chip = new Border { Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(0), Background = panelBrush };
                    chip.Child = new TextBlock
                    {
                        Text = modText + bind.Key,   // key/modifier names from Lua regex scan — textContent
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                        FontSize = (double)Application.Current.Resources["BodyFontSize"],
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
                            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                            FontSize = (double)Application.Current.Resources["BodyFontSize"],
                            Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["ThemeInkSoft"],
                        });
                    }

                    // Editable key TextBox
                    var capturedBind = bind;
                    var keyBox = new TextBox
                    {
                        Text = bind.Key,    // key name from Lua regex scan — text property only
                        Width = 80,
                        IsSpellCheckEnabled = false,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                        FontSize = (double)Application.Current.Resources["BodyFontSize"],
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
                            FontSize = (double)Application.Current.Resources["RowTitleFontSize"],
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

            root.Children.Add(new Border { Padding = new Thickness(10), CornerRadius = new CornerRadius(0), Background = panelBrush, Child = kbSection });
        }

        // Console commands (read-only)
        if (commands.Count > 0)
        {
            var cmdSection = new StackPanel { Spacing = 6 };
            cmdSection.Children.Add(new TextBlock { Text = "Console commands", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = (double)Application.Current.Resources["BodyFontSize"] });
            foreach (var cmd in commands)
            {
                var row3 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var chip = new Border { Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(0), Background = panelBrush };
                chip.Child = new TextBlock
                {
                    Text = cmd.Name,  // command name from Lua regex scan — textContent
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                    FontSize = (double)Application.Current.Resources["BodyFontSize"],
                };
                var copyBtn = new Button { Content = "Copy", Padding = new Thickness(6, 2, 6, 2) };
                var capturedCmdName = cmd.Name;
                copyBtn.Click += (_, _) => { var dp = new Windows.ApplicationModel.DataTransfer.DataPackage(); dp.SetText(capturedCmdName); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp); };
                row3.Children.Add(chip);
                row3.Children.Add(copyBtn);
                cmdSection.Children.Add(row3);
            }
            root.Children.Add(new Border { Padding = new Thickness(10), CornerRadius = new CornerRadius(0), Background = panelBrush, Child = cmdSection });
        }

        var dialog = new ContentDialog
        {
            Title = $"{row.DisplayName} — Config",
            Content = new ScrollViewer { Content = root, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
        };
        ModManager.App.Services.DialogTheming.Apply(dialog); // vibe-glow wave 1: popup-scope theme brushes
        cockpitDialog = dialog;
        await dialog.ShowAsync();
    }

    // Drops are game-scoped writes. On the Library home no game is on screen and the status
    // receipt is painted over — a drop there would install into the LAST-opened game with an
    // invisible result (vibe-glow F-033). Refuse with an instruction; in a game view, name the
    // game in the caption so the target is explicit.
    private bool DropTargetIsHome => LibraryHost.Visibility == Microsoft.UI.Xaml.Visibility.Visible
        || CatalogHost.Visibility == Microsoft.UI.Xaml.Visibility.Visible; // storefront also paints over the receipt

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        if (DropTargetIsHome)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            if (e.DragUIOverride is not null) e.DragUIOverride.Caption = CatalogHost.Visibility == Microsoft.UI.Xaml.Visibility.Visible
                    ? "Close the store to install mods" : "Open a game first to install mods";
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Copy;
        var game = ViewModel.ActiveGame?.Name;
        if (e.DragUIOverride is not null)
            e.DragUIOverride.Caption = string.IsNullOrEmpty(game) ? "Install to active game" : $"Install to {game}";
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DropTargetIsHome) return; // belt to OnDragOver's braces — never a silent home install
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (paths.Count > 0) await ViewModel.AddModsAsync(paths);
    }
}
