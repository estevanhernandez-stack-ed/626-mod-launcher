using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ModManager.Core;

namespace ModManager.App.ViewModels;

/// <summary>
/// One mod row. Wraps a Core <see cref="Mod"/> for binding: prominent name, quiet description,
/// a first-class-but-calm author credit line (honor the builders), and capsule chips. The
/// toggle is owned by the row but the actual enable/disable is delegated to the parent VM.
/// </summary>
public sealed partial class ModRowViewModel : ObservableObject
{
    public Mod Mod { get; }

    // Set programmatically during reload without triggering a disk write (parent guards on this).
    [ObservableProperty] private bool enabled;
    [ObservableProperty] private bool isBusy;

    // Load-order mode: show the position number (editable), hide the normal row controls/art.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrderVisibility))]
    [NotifyPropertyChangedFor(nameof(NormalVisibility))]
    [NotifyPropertyChangedFor(nameof(MonogramVisibility))]
    [NotifyPropertyChangedFor(nameof(ThumbnailVisibility))]
    private bool inLoadOrder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrderValue))]
    private int orderPosition;

    public double OrderValue => OrderPosition;
    public Visibility OrderVisibility => InLoadOrder ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NormalVisibility => InLoadOrder ? Visibility.Collapsed : Visibility.Visible;

    public ModRowViewModel(Mod mod)
    {
        Mod = mod;
        enabled = mod.Enabled;
    }

    public string DisplayName => string.IsNullOrEmpty(Mod.DisplayName) ? Mod.Name : Mod.DisplayName;
    public string? Description => Mod.Description;
    public bool HasDescription => !string.IsNullOrEmpty(Mod.Description);
    public Visibility DescriptionVisibility => HasDescription ? Visibility.Visible : Visibility.Collapsed;

    // Author credit line — visible, clickable attribution (honor the builders). Each link opens
    // in the browser via HyperlinkButton.NavigateUri; SafeUrl guards to http(s) only.
    public string AuthorText => string.IsNullOrEmpty(Mod.Author) ? "" : $"by {Mod.Author}";
    public Visibility AuthorVisibility => string.IsNullOrEmpty(Mod.Author) ? Visibility.Collapsed : Visibility.Visible;

    public Uri? ModUri => SafeUrl.IsHttpUrl(Mod.ModUrl) ? new Uri(Mod.ModUrl!) : null;
    public Visibility ModUrlVisibility => ModUri is null ? Visibility.Collapsed : Visibility.Visible;

    public Uri? SourceUri => SafeUrl.IsHttpUrl(Mod.Source) ? new Uri(Mod.Source!) : null;
    public Visibility SourceVisibility => SourceUri is null ? Visibility.Collapsed : Visibility.Visible;

    public Uri? DonateUri => SafeUrl.IsHttpUrl(Mod.Donate) ? new Uri(Mod.Donate!) : null;
    public Visibility DonateVisibility => DonateUri is null ? Visibility.Collapsed : Visibility.Visible;

    public string DownloadsText => Mod.Downloads is > 0 ? $"{Mod.Downloads:N0} downloads" : "";
    public Visibility DownloadsVisibility => Mod.Downloads is > 0 ? Visibility.Visible : Visibility.Collapsed;

    private bool HasAnyCredit => !string.IsNullOrEmpty(Mod.Author) || ModUri is not null
        || SourceUri is not null || DonateUri is not null || Mod.Downloads is > 0;
    public Visibility CreditVisibility => Mod.HasMeta && HasAnyCredit ? Visibility.Visible : Visibility.Collapsed;

    // Capsule chips (uppercase, tracked in XAML).
    public string LocationChip => Mod.Location;
    public bool HasVariant => !string.IsNullOrEmpty(Mod.Variant);
    public Visibility VariantVisibility => HasVariant ? Visibility.Visible : Visibility.Collapsed;
    public string VariantChip => Mod.Variant ?? "";
    public string ClassChip => (Mod.Class ?? "both").ToUpperInvariant();

    // Row graphic: the builder's CurseForge art when we have it (honor them), else a monogram.
    private ImageSource? _thumb;
    private bool _thumbResolved;
    public ImageSource? Thumbnail
    {
        get
        {
            if (!_thumbResolved)
            {
                _thumbResolved = true;
                if (SafeUrl.IsHttpUrl(Mod.Image)) _thumb = new BitmapImage(new Uri(Mod.Image!));
            }
            return _thumb;
        }
    }

    public Visibility ThumbnailVisibility => !InLoadOrder && Thumbnail is not null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MonogramVisibility => !InLoadOrder && Thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
    public string Initial => DisplayName.Length > 0 ? DisplayName[..1].ToUpperInvariant() : "?";
}
