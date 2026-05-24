using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
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

    public ModRowViewModel(Mod mod)
    {
        Mod = mod;
        enabled = mod.Enabled;
    }

    public string DisplayName => string.IsNullOrEmpty(Mod.DisplayName) ? Mod.Name : Mod.DisplayName;
    public string? Description => Mod.Description;
    public bool HasDescription => !string.IsNullOrEmpty(Mod.Description);
    public Visibility DescriptionVisibility => HasDescription ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Author credit line — visible attribution. "by Author · CurseForge · Source · N downloads".</summary>
    public string Credit
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Mod.Author)) parts.Add($"by {Mod.Author}");
            if (!string.IsNullOrEmpty(Mod.ModUrl)) parts.Add("CurseForge");
            if (!string.IsNullOrEmpty(Mod.Source)) parts.Add("Source");
            if (Mod.Downloads is > 0) parts.Add($"{Mod.Downloads:N0} downloads");
            return string.Join("  ·  ", parts);
        }
    }

    public bool HasCredit => Mod.HasMeta && Credit.Length > 0;
    public Visibility CreditVisibility => HasCredit ? Visibility.Visible : Visibility.Collapsed;

    // Capsule chips (uppercase, tracked in XAML).
    public string LocationChip => Mod.Location;
    public bool HasVariant => !string.IsNullOrEmpty(Mod.Variant);
    public Visibility VariantVisibility => HasVariant ? Visibility.Visible : Visibility.Collapsed;
    public string VariantChip => Mod.Variant ?? "";
    public string ClassChip => (Mod.Class ?? "both").ToUpperInvariant();

    public string? ModUrl => Mod.ModUrl;
    public string? SourceUrl => Mod.Source;
    public string? DonateUrl => Mod.Donate;
}
