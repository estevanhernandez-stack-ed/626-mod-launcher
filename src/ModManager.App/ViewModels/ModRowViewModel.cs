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
    [NotifyPropertyChangedFor(nameof(ManageVisibility))]
    [NotifyPropertyChangedFor(nameof(MonogramVisibility))]
    [NotifyPropertyChangedFor(nameof(ThumbnailVisibility))]
    private bool inLoadOrder;

    // Toggling and uninstall are granted separately. Direct-inject mods can be toggled (reversible
    // move) but not uninstalled here — we never delete loose files in the game's exe folder.
    private readonly bool _canToggle;
    private readonly bool _canUninstall;
    public bool CanToggle => _canToggle;
    public Visibility ManageVisibility => !InLoadOrder && _canUninstall ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrderValue))]
    private int orderPosition;

    public double OrderValue => OrderPosition;
    public Visibility OrderVisibility => InLoadOrder ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NormalVisibility => InLoadOrder ? Visibility.Collapsed : Visibility.Visible;

    public ModRowViewModel(Mod mod, bool canToggle = true, bool canUninstall = true)
    {
        Mod = mod;
        enabled = mod.Enabled;
        _canToggle = canToggle;
        _canUninstall = canUninstall;
    }

    public string DisplayName => string.IsNullOrEmpty(Mod.DisplayName) ? Mod.Name : Mod.DisplayName;
    public string? Description => Mod.Description;
    public bool HasDescription => !string.IsNullOrEmpty(Mod.Description);
    public Visibility DescriptionVisibility => HasDescription ? Visibility.Visible : Visibility.Collapsed;

    // Captured-at-intake readme file for this mod (set by the parent, which holds the GameContext).
    // The "Readme" affordance shows when a captured readme OR a CurseForge description exists.
    public string? ReadmeFilePath { get; init; }
    public Visibility ReadmeVisibility => (ReadmeFilePath is not null || HasDescription) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The markdown to show: captured readme file -> CurseForge description -> empty state.</summary>
    public string GetReadmeMarkdown()
    {
        if (ReadmeFilePath is not null)
        {
            try { var t = System.IO.File.ReadAllText(ReadmeFilePath); if (!string.IsNullOrWhiteSpace(t)) return t; }
            catch { /* fall through to the description / empty state */ }
        }
        return string.IsNullOrWhiteSpace(Mod.Description) ? "No readme available." : Mod.Description!;
    }

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

    // MP-safety: effective = the user's override (if any) over the class-inferred risk. The badge
    // doubles as the set-override affordance, so it's always shown (faint "MP?" when we make no claim).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MpBadge))]
    [NotifyPropertyChangedFor(nameof(MpBadgeBrush))]
    [NotifyPropertyChangedFor(nameof(MpBadgeOpacity))]
    private MpRisk? mpOverride;

    public MpRisk EffectiveMp => MpCompat.Effective(MpCompat.Infer(Mod.Class), MpOverride);
    public string MpBadge => EffectiveMp switch
    {
        MpRisk.Safe => "MP-SAFE",
        MpRisk.Risky => "MP-RISKY",
        MpRisk.SpOnly => "SP-ONLY",
        _ => "MP?",
    };
    public Brush? MpBadgeBrush => EffectiveMp switch
    {
        MpRisk.Safe => Res("ThemeAccent"),
        MpRisk.Risky or MpRisk.SpOnly => Res("ThemeDanger"),
        _ => Res("ThemeInkSoft"),
    };
    public double MpBadgeOpacity => EffectiveMp == MpRisk.Unknown ? 0.5 : 1.0;
    private static Brush? Res(string key) => Application.Current.Resources.TryGetValue(key, out var v) ? v as Brush : null;

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
