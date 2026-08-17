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

    // Automation identity. Keyed on FrameworkId rather than DisplayName for the same reason the mod
    // rows are keyed on the on-disk name: the display string is catalog metadata and can be retitled.
    public string RowAutomationId => $"Framework.{FrameworkId}";
    public string GetAutomationName => "Get " + DisplayName;
    public string UninstallFrameworkAutomationName => "Uninstall " + DisplayName;
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
    string Subtitle)
{
    public string RowAutomationId => $"DirectInjectConfig.{ModId}";
    public string OverrideAutomationName => "Override the config path for " + DisplayName;
}

/// <summary>One row in the Settings → Restore points list. Detail pre-formats the game names
/// and total size so the XAML template binds a plain string, no converter needed.</summary>
public sealed record RestorePointRow(string Timestamp, string Detail, string Id)
{
    public string RowAutomationId => $"RestorePoint.{Id}";
    public string RestoreAutomationName => "Restore the point from " + Timestamp;
    public string DeleteAutomationName => "Delete the restore point from " + Timestamp;
}

/// <summary>One Settings tool row: the entry plus the game data dir that owns it, so
/// Configure can write back to the right registry (vibe-glow F-032).</summary>
public sealed record SettingsToolRow(ToolEntry Entry, string DataDir, string? GameName = null)
{
    public string DisplayName => Entry.DisplayName;
    /// <summary>Which game owns this copy — the same tool can be installed per-game, and
    /// Configure→Uninstall must never ambiguate between copies. Display name when the
    /// registry knows the game; folder key as the fallback (F-068).</summary>
    public string GameLabel => $"For {(string.IsNullOrWhiteSpace(GameName) ? System.IO.Path.GetFileName(DataDir) : GameName)}";

    /// <summary>The same tool can be installed per-game, so the name carries the owning game too —
    /// otherwise two Configure buttons announce identically and a harness cannot tell the copies
    /// apart, which is the exact ambiguity F-068 fixed for humans.</summary>
    public string ConfigureAutomationName => $"Configure {DisplayName} ({GameLabel})";
}

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

    /// <summary>Set when the user clicks "Connect Nexus account". The OAuth flow opens a browser and (on a
    /// first-ever connect) shows the consent dialog — neither can be nested under this ContentDialog, so we
    /// hand off: MainWindow.OnSettings runs <c>ViewModel.ConnectNexusAsync()</c> after this dialog closes.</summary>
    public bool ConnectNexusRequested { get; private set; }

    /// <summary>Set when the user clicks Configure… on a Settings tool row. MainWindow.OnSettings
    /// opens ToolConfigureDialog after this dialog closes — same no-nesting hand-off pattern.</summary>
    public SettingsToolRow? ToolConfigureRequested { get; private set; }

    public SettingsDialog(IntPtr hwnd, AvatarService avatars, ThemeService themes, AppSettingsService appSettings, MainViewModel vm)
    {
        InitializeComponent();
        ModManager.App.Services.DialogTheming.Apply(this); // vibe-glow wave 1: popup-scope theme brushes
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
        var all = new List<SettingsToolRow>();

        // The active game's DataDir is <_626mods>/<gameId>. Its parent is the _626mods root that
        // holds every game's per-game data dir. Enumerate them all to list tools across games.
        var activeDataDir = _vm.GameDataDirPublic();
        var modsRoot = string.IsNullOrEmpty(activeDataDir) ? null : System.IO.Path.GetDirectoryName(activeDataDir);

        // Folder key -> display name via the games dropdown the VM already holds (F-068):
        // "For ELDEN RING", not "For eldenring". Unregistered folders keep the key.
        string? NameFor(string dir)
            => _vm.Games.FirstOrDefault(g =>
                string.Equals(g.Id, System.IO.Path.GetFileName(dir), StringComparison.OrdinalIgnoreCase))?.Name;

        if (!string.IsNullOrEmpty(modsRoot) && Directory.Exists(modsRoot))
        {
            foreach (var gameDir in Directory.EnumerateDirectories(modsRoot))
            {
                try { all.AddRange(ToolRegistry.Load(gameDir).Tools.Select(t => new SettingsToolRow(t, gameDir, NameFor(gameDir)))); }
                catch { /* skip malformed per-game registries */ }
            }
        }
        else if (!string.IsNullOrEmpty(activeDataDir) && Directory.Exists(activeDataDir))
        {
            // Fallback: read only the active game's registry.
            try { all.AddRange(ToolRegistry.Load(activeDataDir).Tools.Select(t => new SettingsToolRow(t, activeDataDir, NameFor(activeDataDir)))); }
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

    /// <summary>Render the Nexus section based on the current connection + configuration state. Called on
    /// dialog open and after every Connect/Disconnect action. Secure OAuth sign-in — no key to paste. In
    /// the dark window (client_id not delivered yet) the Connect button is disabled with a "finalizing" note.</summary>
    private void RefreshNexusUi()
    {
        if (_vm.NexusConnected)
        {
            NexusConnectedBanner.Visibility = Visibility.Visible;
            NexusConnectedText.Text = $"Connected as {_vm.NexusAccountLine}";
            NexusExplainer.Text = "Your Nexus account is signed in with secure OAuth — used for endorsements, " +
                                  "mod ID lookups, and update checks. Disconnect to remove the saved sign-in, " +
                                  "or connect again to switch accounts.";
            NexusConnectButton.Content = "Switch account";
            NexusDisconnectButton.Visibility = Visibility.Visible;
        }
        else
        {
            NexusConnectedBanner.Visibility = Visibility.Collapsed;
            NexusExplainer.Text = "Sign in to Nexus with secure OAuth — it opens in your browser, no API key to " +
                                  "paste. Your sign-in stays on this machine and is only sent to Nexus's own API.";
            NexusConnectButton.Content = "Connect Nexus account";
            NexusDisconnectButton.Visibility = Visibility.Collapsed;
        }

        // Dark window: the OAuth client_id hasn't been delivered yet — connecting isn't possible, so disable
        // the button and explain instead of letting the user hit a dead end.
        var configured = _vm.NexusSignInConfigured;
        NexusConnectButton.IsEnabled = configured;
        NexusConnectSublabel.Text = "Secure sign-in is being finalized with Nexus.";
        NexusConnectSublabel.Visibility = configured ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>"Connect Nexus account" — hands off to the shell. The OAuth flow opens a browser and (on a
    /// first-ever connect) shows the consent dialog; neither can nest under this ContentDialog, so we flag +
    /// close and MainWindow.OnSettings runs <c>ViewModel.ConnectNexusAsync()</c> once this dialog is closed.</summary>
    private void OnNexusConnect(object sender, RoutedEventArgs e)
    {
        ConnectNexusRequested = true;
        Hide();
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
            // Cause framing, not bare exception text (F-062) — the user needs to know WHAT failed.
            StatusText.Text = "Couldn't apply these changes — " + ex.Message;
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
            // Color-only information gets a VISIBLE text alternative (F-045): Rectangle has no
            // automation peer, so a UIA name on it announces nothing — the hex caption is the
            // honest fix (readable, keyboard-independent, and picked up by OCR-based AT too).
            var hex = $"#{p.R:X2}{p.G:X2}{p.B:X2}";
            var swatch = new Rectangle
            {
                Width = 48, Height = 32,
                RadiusX = 0, RadiusY = 0,
                Fill = new SolidColorBrush(Color.FromArgb(255, p.R, p.G, p.B)),
            };
            ToolTipService.SetToolTip(swatch, hex);
            var caption = new TextBlock
            {
                Text = hex,
                FontSize = 10,
                FontFamily = (FontFamily)Application.Current.Resources["MonoFontFamily"],
                Foreground = (SolidColorBrush)Application.Current.Resources["ThemeInkDim"],
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            PaletteStrip.Children.Add(new StackPanel { Spacing = 2, Children = { swatch, caption } });
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

    private void OnToolConfigure(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SettingsToolRow row) return;
        // WinUI 3 allows one ContentDialog per XamlRoot — same flag + Hide() hand-off as
        // Reset/Restore/Delete: MainWindow.OnSettings opens the configure dialog after this
        // one has fully closed, then reloads so the tool rail repaints (F-032).
        ToolConfigureRequested = row;
        Hide();
    }
}
