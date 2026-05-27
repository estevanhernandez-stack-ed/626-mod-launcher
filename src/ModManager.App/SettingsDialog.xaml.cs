using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ModManager.App.Services;
using ModManager.App.ViewModels;
using ModManager.Core;
using ModManager.Core.Frameworks;
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
    private bool _suppressBackdropChange = true; // ignore the initial SelectionChanged from seeding

    private string? _pickedSourcePath;
    private IReadOnlyList<PaletteColor> _palette = Array.Empty<PaletteColor>();

    /// <summary>True when the user applied a change that needs to flow back to the main shell
    /// (avatar swap → icon refresh, theme add → dropdown refresh). Nexus + backdrop changes flow
    /// through their own notification paths and don't need this flag.</summary>
    public bool Changed { get; private set; }

    public SettingsDialog(IntPtr hwnd, AvatarService avatars, ThemeService themes, AppSettingsService appSettings, MainViewModel vm)
    {
        InitializeComponent();
        _hwnd = hwnd;
        _avatars = avatars;
        _themes = themes;
        _appSettings = appSettings;
        _vm = vm;

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

        // Seed the Nexus section. Re-validate the stored key first (offline-safe) so the account
        // name + premium tag are current before we render the banner.
        _ = InitializeNexusSectionAsync();

        // Seed the About → Installed tools list. Pure file-read, fast — fine on the UI thread.
        RefreshInstalledTools();
        RefreshInstalledFrameworks();
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
        }
        finally { NexusConnectButton.IsEnabled = true; }
    }

    private void OnNexusDisconnect(object sender, RoutedEventArgs e)
    {
        _vm.DisconnectNexus();
        StatusText.Text = "Disconnected from Nexus.";
        RefreshNexusUi();
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
}
