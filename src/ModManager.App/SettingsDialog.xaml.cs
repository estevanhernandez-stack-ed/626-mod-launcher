using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ModManager.App.Services;
using ModManager.App.ViewModels;
using ModManager.Core;
using ModManager.Core.Catalog;
using ModManager.Core.Frameworks;
using ModManager.Core.Plugins;
using ModManager.Core.Tools;
using Windows.Storage.Pickers;
using Windows.UI;

namespace ModManager.App;

/// <summary>One row in the Settings → Installed frameworks list. Pre-formats the detail
/// string (author + install time + path) so the XAML template doesn't need a value converter.
/// The Get-link is exposed as a Uri object because x:Bind requires the right binding type.</summary>
public sealed record InstalledFrameworkRow(
    string FrameworkId,
    string DisplayName,
    string Detail,
    string GetUrl)
{
    public Uri? GetUriObj => string.IsNullOrEmpty(GetUrl) ? null : new Uri(GetUrl);
}

/// <summary>One row in the Settings → Direct-inject mod configs list. Subtitle shows the
/// effective path (override if set, else catalog default) so the user can see at a glance
/// whether they've customized + which file the pencil icon would open.</summary>
public sealed record DirectInjectConfigRow(
    string ModId,
    string DisplayName,
    string RelativeConfigPath,
    string EffectivePath,
    string Title,
    string Subtitle);

/// <summary>One row in the Settings → Restore points list. Detail pre-formats the game names
/// and total size so the XAML template binds a plain string, no converter needed.</summary>
public sealed record RestorePointRow(string Timestamp, string Detail, string Id);

/// <summary>
/// The settings hub. Identity (avatar / derived theme / window transparency) and Nexus Mods
/// account in one place. The Apply button commits the avatar + derived theme changes (gated by
/// the checkboxes); the transparency dropdown applies immediately; the Nexus Connect/Disconnect
/// buttons are also inline so the toolbar dot updates the moment you act here.
/// </summary>
public sealed partial class SettingsDialog : ContentDialog
{
    private readonly IntPtr _hwnd;
    private readonly AvatarService _avatars;
    private readonly ThemeService _themes;
    private readonly AppSettingsService _appSettings;
    private readonly MainViewModel _vm;
    private readonly RestorePointService _rp;
    private bool _suppressBackdropChange = true; // ignore the initial SelectionChanged from seeding

    private string? _pickedSourcePath;
    private IReadOnlyList<PaletteColor> _palette = Array.Empty<PaletteColor>();

    /// <summary>True when the user applied a change that needs to flow back to the main shell
    /// (avatar swap → icon refresh, theme add → dropdown refresh). Nexus + backdrop changes flow
    /// through their own notification paths and don't need this flag.</summary>
    public bool Changed { get; private set; }

    /// <summary>True when the user clicked "Reset launcher…". MainWindow.OnSettings checks this
    /// after ShowAsync() returns and opens SafeClearDialog if set — avoids nesting two
    /// ContentDialogs simultaneously, which is fragile in WinUI 3.</summary>
    public bool OpenSafeClearRequested { get; private set; }

    /// <summary>Set when the user clicks "Restore" on a restore-point row. MainWindow.OnSettings
    /// reads this after ShowAsync() returns and shows the confirm + performs the restore — same
    /// flag-then-hide pattern used by OpenSafeClearRequested, no nested ContentDialog.</summary>
    public string? RestoreRequestedTimestamp { get; private set; }

    /// <summary>Set when the user clicks "Delete" on a restore-point row. MainWindow.OnSettings
    /// reads this after ShowAsync() returns and shows the confirm + deletes — same pattern.</summary>
    public string? DeleteRequestedTimestamp { get; private set; }

    public SettingsDialog(IntPtr hwnd, AvatarService avatars, ThemeService themes, AppSettingsService appSettings, MainViewModel vm)
    {
        InitializeComponent();
        _hwnd = hwnd;
        _avatars = avatars;
        _themes = themes;
        _appSettings = appSettings;
        _vm = vm;
        _rp = App.AppHost.Services.GetRequiredService<RestorePointService>();

        DeriveThemeCheck.Checked   += (_, _) => ThemeNameBox.Visibility = Visibility.Visible;
        DeriveThemeCheck.Unchecked += (_, _) => ThemeNameBox.Visibility = Visibility.Collapsed;

        // Seed the avatar preview if one is set.
        if (_avatars.HasAvatar)
        {
            PreviewImage.Source = new BitmapImage(new Uri(_avatars.AvatarPngPath));
            FileLabel.Text = "Current avatar";
            RemoveButton.Visibility = Visibility.Visible;
        }

        // Seed the backdrop dropdown to the currently-saved value. The flag suppresses the initial
        // SelectionChanged firing as a "user action" — we only apply on actual user changes.
        BackdropBox.SelectedIndex = _appSettings.Backdrop switch
        {
            WindowBackdropKind.Mica    => 1,
            WindowBackdropKind.Acrylic => 2,
            _                          => 0,
        };
        _suppressBackdropChange = false;

        // Seed the auto-check-for-mod-updates toggle from the saved setting (default on).
        AutoCheckModUpdatesCheck.IsChecked = _appSettings.AutoCheckModUpdates;

        // Seed the keep-plugins-updated toggle.
        KeepPluginsUpdatedCheck.IsChecked = _appSettings.KeepPluginsUpdated;
        RefreshPluginStatus();

        // Seed the Nexus section. Re-validate the stored key first (offline-safe) so the account
        // name + premium tag are current before we render the banner.
        _ = InitializeNexusSectionAsync();

        // Seed the About → Installed tools list. Pure file-read, fast — fine on the UI thread.
        RefreshInstalledTools();
        RefreshInstalledFrameworks();
        RefreshDirectInjectConfigs();
        RefreshRestorePoints();
    }

    /// <summary>
    /// Populate the About → Installed tools list from every per-game <c>tools.json</c> under the
    /// launcher's <c>_626mods</c> root. Falls back to "active game only" when the root can't be
    /// resolved (no game loaded yet). Malformed registries are skipped silently — this surface
    /// is informational, not load-bearing.
    /// </summary>
    private void RefreshInstalledTools()
    {
        var all = new List<ToolEntry>();

        // The active game's DataDir is <_626mods>/<gameId>. Its parent is the _626mods root that
        // holds every game's per-game data dir. Enumerate them all to list tools across games.
        var activeDataDir = _vm.GameDataDirPublic();
        var modsRoot = string.IsNullOrEmpty(activeDataDir) ? null : System.IO.Path.GetDirectoryName(activeDataDir);

        if (!string.IsNullOrEmpty(modsRoot) && Directory.Exists(modsRoot))
        {
            foreach (var gameDir in Directory.EnumerateDirectories(modsRoot))
            {
                try { all.AddRange(ToolRegistry.Load(gameDir).Tools); }
                catch { /* skip malformed per-game registries */ }
            }
        }
        else if (!string.IsNullOrEmpty(activeDataDir) && Directory.Exists(activeDataDir))
        {
            // Fallback: read only the active game's registry.
            try { all.AddRange(ToolRegistry.Load(activeDataDir).Tools); }
            catch { /* skip */ }
        }

        InstalledToolsList.ItemsSource = all;
        InstalledToolsEmpty.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Populate the About → Installed frameworks list. Mirrors RefreshInstalledTools' shape:
    /// enumerate every per-game data dir under _626mods, read each one's framework manifests via
    /// FrameworkRegistry.List, render rows with display name + author + install time + path.
    /// </summary>
    private void RefreshInstalledFrameworks()
    {
        var all = new List<InstalledFrameworkRow>();

        var activeDataDir = _vm.GameDataDirPublic();
        var modsRoot = string.IsNullOrEmpty(activeDataDir) ? null : System.IO.Path.GetDirectoryName(activeDataDir);

        var sources = new List<string>();
        if (!string.IsNullOrEmpty(modsRoot) && Directory.Exists(modsRoot))
            sources.AddRange(Directory.EnumerateDirectories(modsRoot));
        else if (!string.IsNullOrEmpty(activeDataDir) && Directory.Exists(activeDataDir))
            sources.Add(activeDataDir);

        foreach (var gameDataDir in sources)
        {
            try
            {
                foreach (var m in FrameworkRegistry.List(gameDataDir))
                {
                    all.Add(new InstalledFrameworkRow(
                        FrameworkId: m.FrameworkId,
                        DisplayName: m.DisplayName,
                        Detail: $"by {m.Author}  ·  installed {m.InstalledUtc.ToLocalTime():g}  ·  {m.InstallPath}",
                        GetUrl: GetUrlForFramework(m.FrameworkId)));
                }
            }
            catch { /* skip malformed per-game registries */ }
        }

        InstalledFrameworksList.ItemsSource = all;
        InstalledFrameworksEmpty.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string GetUrlForFramework(string frameworkId)
        => KnownFramework.Catalog.FirstOrDefault(f => f.FrameworkId == frameworkId)?.GetUrl ?? "";

    /// <summary>Uninstall handler. Walks every per-game data dir and uninstalls the framework
    /// from any game where it's currently installed. Idempotent — a missing manifest in one
    /// game is no problem if another game still has it.</summary>
    private void OnUninstallFramework(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string frameworkId) return;

        var activeDataDir = _vm.GameDataDirPublic();
        var modsRoot = string.IsNullOrEmpty(activeDataDir) ? null : System.IO.Path.GetDirectoryName(activeDataDir);

        var sources = new List<string>();
        if (!string.IsNullOrEmpty(modsRoot) && Directory.Exists(modsRoot))
            sources.AddRange(Directory.EnumerateDirectories(modsRoot));
        else if (!string.IsNullOrEmpty(activeDataDir) && Directory.Exists(activeDataDir))
            sources.Add(activeDataDir);

        foreach (var gameDataDir in sources)
        {
            try
            {
                var match = FrameworkRegistry.List(gameDataDir)
                    .FirstOrDefault(m => m.FrameworkId == frameworkId);
                if (match is null) continue;
                FrameworkRegistry.Uninstall(gameDataDir, frameworkId, match.InstallPath);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Couldn't uninstall {frameworkId}: {ex.Message}";
                return;
            }
        }

        RefreshInstalledFrameworks();
        Changed = true;  // Tell the main shell to reload mod rows (framework chip should reappear).
        StatusText.Text = $"Uninstalled {frameworkId}.";
    }

    /// <summary>
    /// Populate the Settings → Direct-inject mod configs list. One row per (catalog mod ×
    /// ConfigPath). Subtitle shows the EFFECTIVE path (override if set, catalog default
    /// otherwise) plus an "(override)" tag when the user has set a custom location.
    /// </summary>
    private void RefreshDirectInjectConfigs()
    {
        var rows = new List<DirectInjectConfigRow>();
        var dataDir = _vm.GameDataDirPublic();
        var gameRoot = _vm.GameRootText;
        if (string.IsNullOrEmpty(gameRoot) || string.IsNullOrEmpty(dataDir))
        {
            DirectInjectConfigsList.ItemsSource = rows;
            DirectInjectConfigsEmpty.Visibility = Visibility.Visible;
            return;
        }

        var overrides = DirectInjectConfigOverrides.Load(dataDir);
        var installRoot = ResolveInstallRoot(gameRoot);

        foreach (var mod in KnownDirectInjectMod.Catalog)
        {
            if (mod.ConfigPaths.Count == 0) continue; // mods with no editable config are silent
            overrides.OverridesByModId.TryGetValue(mod.ModId, out var modOverrides);
            foreach (var rel in mod.ConfigPaths)
            {
                bool isOverridden = modOverrides is not null && modOverrides.TryGetValue(rel, out var customAbs);
                string effective = isOverridden
                    ? modOverrides![rel]
                    : System.IO.Path.GetFullPath(System.IO.Path.Combine(installRoot, rel.Replace('/', System.IO.Path.DirectorySeparatorChar)));
                rows.Add(new DirectInjectConfigRow(
                    ModId: mod.ModId,
                    DisplayName: mod.DisplayName,
                    RelativeConfigPath: rel,
                    EffectivePath: effective,
                    Title: $"{mod.DisplayName} — {rel}",
                    Subtitle: isOverridden ? $"{effective}  ·  (override)" : effective));
            }
        }

        DirectInjectConfigsList.ItemsSource = rows;
        DirectInjectConfigsEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string ResolveInstallRoot(string gameRoot)
    {
        if (string.IsNullOrEmpty(gameRoot)) return gameRoot;
        var game = System.IO.Path.Combine(gameRoot, "Game");
        return Directory.Exists(game) ? game : gameRoot;
    }

    /// <summary>
    /// Override picker. File picker → save the chosen absolute path to the per-game override
    /// store. Changed=true triggers a row reload on dialog close so the pencil icon updates.
    /// </summary>
    private async void OnDirectInjectOverrideClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DirectInjectConfigRow row) return;

        var dataDir = _vm.GameDataDirPublic();
        if (string.IsNullOrEmpty(dataDir))
        {
            StatusText.Text = "No active game to override against.";
            return;
        }

        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add(".ini");
        picker.FileTypeFilter.Add(".toml");
        picker.FileTypeFilter.Add(".cfg");
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var current = DirectInjectConfigOverrides.Load(dataDir);
        var newMap = current.OverridesByModId.ToDictionary(
            kv => kv.Key,
            kv => new Dictionary<string, string>(kv.Value));
        if (!newMap.TryGetValue(row.ModId, out var modOverrides))
        {
            modOverrides = new Dictionary<string, string>();
            newMap[row.ModId] = modOverrides;
        }
        modOverrides[row.RelativeConfigPath] = file.Path;

        DirectInjectConfigOverrides.Save(dataDir, new DirectInjectConfigOverrides(newMap));

        Changed = true; // re-render mod rows on close so the pencil icon picks up the new path
        RefreshDirectInjectConfigs();
        StatusText.Text = $"Override saved for {row.DisplayName} → {file.Path}.";
    }

    private void OnBackdropChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressBackdropChange) return;
        if (BackdropBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
        var kind = tag switch
        {
            "mica"    => WindowBackdropKind.Mica,
            "acrylic" => WindowBackdropKind.Acrylic,
            _         => WindowBackdropKind.Solid,
        };
        _appSettings.SetBackdrop(kind);
        // The MainWindow listens to AppSettingsService.BackdropChanged and re-applies the backdrop.
    }

    private async Task InitializeNexusSectionAsync()
    {
        if (_vm.NexusConnected) await _vm.RefreshNexusAsync();
        RefreshNexusUi();
    }

    /// <summary>Render the Nexus section based on the current connection state. Called on dialog
    /// open and after every Connect/Disconnect action.</summary>
    private void RefreshNexusUi()
    {
        if (_vm.NexusConnected)
        {
            NexusConnectedBanner.Visibility = Visibility.Visible;
            NexusConnectedText.Text = $"Connected as {_vm.NexusAccountLine}";
            NexusExplainer.Text = "Your saved API key is being used for metadata + mod ID lookups. " +
                                  "Disconnect to remove the saved key, or paste a different key to switch accounts.";
            NexusKeyBox.PlaceholderText = "Paste a new key only if switching accounts";
            NexusConnectButton.Content = "Switch account";
            NexusDisconnectButton.Visibility = Visibility.Visible;
        }
        else
        {
            NexusConnectedBanner.Visibility = Visibility.Collapsed;
            NexusExplainer.Text = "Get your personal API key from nexusmods.com → account settings → API access, " +
                                  "then paste it here. The key stays on your machine — it's never sent anywhere except Nexus's own API.";
            NexusKeyBox.PlaceholderText = "Nexus personal API key";
            NexusConnectButton.Content = "Connect";
            NexusDisconnectButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnNexusConnect(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NexusKeyBox.Password))
        {
            StatusText.Text = "Paste your Nexus API key first.";
            return;
        }
        NexusConnectButton.IsEnabled = false;
        try
        {
            var ok = await _vm.ConnectNexusAsync(NexusKeyBox.Password);
            StatusText.Text = ok ? $"Connected as {_vm.NexusAccountLine}." : "Nexus key rejected — check it on your account's API access page.";
            if (ok) NexusKeyBox.Password = "";
            RefreshNexusUi();
#if FULL
            if (ok) _ = App.AppHost.Services.GetRequiredService<PluginFeedSource>().MaybeFetchOnConnectAsync(); // off-Store: pull/refresh the Nexus plugin
#endif
        }
        finally { NexusConnectButton.IsEnabled = true; }
    }

    private void OnNexusDisconnect(object sender, RoutedEventArgs e)
    {
        _vm.DisconnectNexus();
        StatusText.Text = "Disconnected from Nexus.";
        RefreshNexusUi();
    }

    /// <summary>Persist the auto-check-for-mod-updates preference immediately on toggle (no Apply
    /// needed — it mirrors the backdrop dropdown's apply-on-change behavior).</summary>
    private void OnAutoCheckModUpdatesToggled(object sender, RoutedEventArgs e)
        => _appSettings.SetAutoCheckModUpdates(AutoCheckModUpdatesCheck.IsChecked == true);

    /// <summary>Persist the keep-plugins-updated preference immediately on toggle.</summary>
    private void OnKeepPluginsUpdatedToggled(object sender, RoutedEventArgs e)
        => _appSettings.SetKeepPluginsUpdated(KeepPluginsUpdatedCheck.IsChecked == true);

    /// <summary>Manual "Install / refresh Nexus plugin" button. FULL only — awaits
    /// <see cref="PluginFeedSource.FetchAsync"/> with <c>force: true</c> and maps the outcome to a
    /// human-readable status line. The button is disabled while the fetch is in flight so rapid
    /// double-clicks don't race the installer. STORE: <see cref="PluginFeedSource"/> is not
    /// registered, so we guard with <see cref="Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService{T}"/> and
    /// show the desktop-only note instead.</summary>
    private async void OnRefreshPlugin(object sender, RoutedEventArgs e)
    {
#if FULL
        var feed = App.AppHost.Services.GetService<PluginFeedSource>();
        if (feed is null)
        {
            PluginStatusText.Text = "Plugins are a desktop-only feature.";
            return;
        }
        RefreshPluginButton.IsEnabled = false;
        PluginStatusText.Text = "Checking the plugin feed…";
        try
        {
            var result = await feed.FetchAsync(force: true);
            PluginStatusText.Text = result.Outcome switch
            {
                PluginFetchOutcome.Installed    => $"Nexus plugin v{result.Version} installed.",
                PluginFetchOutcome.UpToDate     => $"Nexus plugin is up to date (v{result.Version}).",
                PluginFetchOutcome.RequiresUpdate => $"This plugin needs launcher v{result.Version} — update the launcher.",
                PluginFetchOutcome.NotApplicable => "Connect Nexus first.",
                PluginFetchOutcome.Failed        => $"Couldn’t fetch the plugin: {result.Message}",
                _                               => result.Message ?? "Done.",
            };
        }
        finally { RefreshPluginButton.IsEnabled = true; }
#else
        PluginStatusText.Text = "Plugins are a desktop-only feature.";
        await System.Threading.Tasks.Task.CompletedTask;
#endif
    }

    /// <summary>Populate the plugin status line. FULL shows the installed version (or "not
    /// installed"); STORE shows a static note because plugin delivery is desktop-only.</summary>
    private void RefreshPluginStatus()
    {
#if FULL
        var recordPath = System.IO.Path.Combine(PluginHost.PluginsDir, "installed-plugins.json");
        var installed = InstalledPluginsStore.Read(recordPath);
        if (installed.TryGetValue("nexus", out var version))
            PluginStatusText.Text = $"Nexus plugin: v{version} installed";
        else
            PluginStatusText.Text = "Nexus plugin: not installed";
#else
        PluginStatusText.Text = "Plugins are a desktop-only feature.";
#endif
    }

    private async void OnPickImage(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".gif");
        picker.FileTypeFilter.Add(".webp");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        _pickedSourcePath = file.Path;
        FileLabel.Text = System.IO.Path.GetFileName(file.Path);

        try
        {
            // Decode + sample at 64×64 for palette extraction (fast, plenty of signal).
            var (_, rgba64) = await AvatarService.ResizeToSquareAsync(file.Path, 64);
            _palette = PaletteExtractor.Extract(rgba64, 64, 64, k: 5);
            PreviewImage.Source = new BitmapImage(new Uri(file.Path));
            RenderPaletteStrip();
            StatusText.Text = "";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Couldn't read that image: " + ex.Message;
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        _avatars.Delete();
        Changed = true;
        Hide();
    }

    private async void OnApply(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var hasPick    = !string.IsNullOrEmpty(_pickedSourcePath);
        var wantsIcon  = UseAsIconCheck.IsChecked == true;
        var wantsTheme = DeriveThemeCheck.IsChecked == true && _palette.Count > 0;

        // Nothing semantically requested: no new pick, no existing avatar to clear, no theme to save.
        if (!hasPick && !wantsTheme && (!_avatars.HasAvatar || wantsIcon)) return;

        var deferral = args.GetDeferral();
        try
        {
            // Icon flow. "Use this image as the launcher's icon" is the END STATE the user wants:
            //   - Checked + new image picked → set the avatar to the picked image.
            //   - Unchecked + avatar exists  → revert to the bundled icon (delete the saved avatar).
            //   - Unchecked + no avatar      → no-op.
            //   - Checked + no new pick      → keep whatever's already set (no-op).
            if (wantsIcon && hasPick)
                await _avatars.ImportAsync(_pickedSourcePath!);
            else if (!wantsIcon && _avatars.HasAvatar)
                _avatars.Delete();

            if (wantsTheme)
            {
                var name = string.IsNullOrWhiteSpace(ThemeNameBox.Text) ? "From avatar" : ThemeNameBox.Text.Trim();
                var raw = PaletteToTheme.Derive(_palette, name);
                var json = SerializeRawTheme(raw);
                _themes.ImportUserTheme(json);
            }

            Changed = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void RenderPaletteStrip()
    {
        PaletteStrip.Children.Clear();
        foreach (var p in _palette)
        {
            PaletteStrip.Children.Add(new Rectangle
            {
                Width = 48, Height = 32,
                RadiusX = 4, RadiusY = 4,
                Fill = new SolidColorBrush(Color.FromArgb(255, p.R, p.G, p.B)),
            });
        }
        PaletteEmpty.Visibility = _palette.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string SerializeRawTheme(RawTheme raw)
    {
        var pairs = raw.Tokens.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\"");
        var bloom = raw.AccentBloom is null
            ? ""
            : $",\"accent_bloom\":{{\"blur\":{raw.AccentBloom.Blur},\"alpha\":{raw.AccentBloom.Alpha}}}";
        return "{" + string.Join(",", pairs) + bloom + "}";
    }

    /// <summary>Populate the Settings → Restore points list from the service.</summary>
    private void RefreshRestorePoints()
    {
        var pts = _rp.ListRestorePoints();
        RestorePointsList.ItemsSource = pts
            .Select(p => new RestorePointRow(
                Timestamp: p.Timestamp,
                Detail: $"{string.Join(", ", p.GameNames)} · {FormatSize(p.TotalBytes)}",
                Id: p.Timestamp))
            .ToList();
        NoRestorePointsText.Visibility = pts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatSize(long bytes)
    {
        const long GiB = 1L << 30;
        const long MiB = 1L << 20;
        return bytes >= GiB
            ? $"{bytes / (double)GiB:F1} GB"
            : $"{bytes / (double)MiB:F0} MB";
    }

    /// <summary>Restore button. WinUI 3 forbids opening a second ContentDialog while one is already
    /// showing — the confirm would throw InvalidOperationException. Pattern mirrors OnResetLauncher:
    /// set the hand-off timestamp, Hide() this dialog, and let MainWindow.OnSettings show the confirm
    /// after ShowAsync() returns (SettingsDialog is fully closed by then, no nesting).</summary>
    private void OnRestorePoint(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string ts) return;
        RestoreRequestedTimestamp = ts;
        Hide();
    }

    /// <summary>Delete button. Same nested-ContentDialog constraint as OnRestorePoint — route the
    /// action out to MainWindow via the flag-then-hide hand-off.</summary>
    private void OnDeleteRestorePoint(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string ts) return;
        DeleteRequestedTimestamp = ts;
        Hide();
    }

    /// <summary>Reset launcher button. Sets the hand-off flag and closes this dialog — MainWindow
    /// opens SafeClearDialog after ShowAsync() returns so both ContentDialogs never overlap.</summary>
    private void OnResetLauncher(object sender, RoutedEventArgs e)
    {
        OpenSafeClearRequested = true;
        Hide();
    }
}
