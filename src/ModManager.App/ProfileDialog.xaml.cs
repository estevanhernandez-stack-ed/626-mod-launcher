using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ModManager.App.Services;
using ModManager.Core;
using Windows.Storage.Pickers;
using Windows.UI;

namespace ModManager.App;

public sealed partial class ProfileDialog : ContentDialog
{
    private readonly IntPtr _hwnd;
    private readonly AvatarService _avatars;
    private readonly ThemeService _themes;

    private string? _pickedSourcePath;     // the original picked file (decoded for preview + palette)
    private IReadOnlyList<PaletteColor> _palette = Array.Empty<PaletteColor>();

    /// <summary>True when the user applied the avatar or its derived theme — the caller refreshes
    /// the title-bar icon binding and theme list.</summary>
    public bool Changed { get; private set; }

    public ProfileDialog(IntPtr hwnd, AvatarService avatars, ThemeService themes)
    {
        InitializeComponent();
        _hwnd = hwnd;
        _avatars = avatars;
        _themes = themes;

        DeriveThemeCheck.Checked   += (_, _) => ThemeNameBox.Visibility = Visibility.Visible;
        DeriveThemeCheck.Unchecked += (_, _) => ThemeNameBox.Visibility = Visibility.Collapsed;

        // Seed the preview from the current avatar if one is set.
        if (_avatars.HasAvatar)
        {
            PreviewImage.Source = new BitmapImage(new Uri(_avatars.AvatarPngPath));
            FileLabel.Text = "Current avatar";
            RemoveButton.Visibility = Visibility.Visible;
        }
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
        if (string.IsNullOrEmpty(_pickedSourcePath))
        {
            // No new pick — close without doing anything.
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            if (UseAsIconCheck.IsChecked == true)
                await _avatars.ImportAsync(_pickedSourcePath);

            if (DeriveThemeCheck.IsChecked == true && _palette.Count > 0)
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
