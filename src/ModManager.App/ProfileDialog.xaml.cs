using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ModManager.App.Services;
using ModManager.App.ViewModels;
using ModManager.Core;
using Windows.Storage.Pickers;
using Windows.UI;

namespace ModManager.App;

/// <summary>
/// The user-identity hub. Three concerns in one place: avatar (the launcher's icon), a theme
/// derived from that image, and the Nexus Mods connection. The Apply button commits the avatar
/// + derived theme changes (gated by the checkboxes); the Nexus Connect/Disconnect buttons are
/// inline (they take effect immediately so the toolbar dot updates the moment you act here).
/// </summary>
public sealed partial class ProfileDialog : ContentDialog
{
    private readonly IntPtr _hwnd;
    private readonly AvatarService _avatars;
    private readonly ThemeService _themes;
    private readonly MainViewModel _vm;

    private string? _pickedSourcePath;
    private IReadOnlyList<PaletteColor> _palette = Array.Empty<PaletteColor>();

    /// <summary>True when the user applied a change that needs to flow back to the main shell
    /// (avatar swap → icon refresh, theme add → dropdown refresh). Nexus changes flow through
    /// the VM's existing notifications and don't need this flag.</summary>
    public bool Changed { get; private set; }

    public ProfileDialog(IntPtr hwnd, AvatarService avatars, ThemeService themes, MainViewModel vm)
    {
        InitializeComponent();
        _hwnd = hwnd;
        _avatars = avatars;
        _themes = themes;
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

        // Seed the Nexus section. Re-validate the stored key first (offline-safe) so the account
        // name + premium tag are current before we render the banner.
        _ = InitializeNexusSectionAsync();
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
