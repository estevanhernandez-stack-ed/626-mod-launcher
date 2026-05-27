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
    [NotifyPropertyChangedFor(nameof(ToggleVisibility))]
    [NotifyPropertyChangedFor(nameof(VariantOptionsVisibility))]
    private bool inLoadOrder;

    // Toggling and uninstall are granted separately. Direct-inject mods can be toggled (reversible
    // move) but not uninstalled here — we never delete loose files in the game's exe folder.
    private readonly bool _canToggle;
    private readonly bool _canUninstall;
    public bool CanToggle => _canToggle;
    // Family rows get a family-scope uninstall (deletes every variant, gated by a confirm naming the
    // count). The single-mod uninstall handler branches on HasVariantOptions in MainWindow.xaml.cs.
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

    // A multi-variant family row shows the BASE title (e.g. "Faster Ships"); the per-level distinction
    // lives in the inline option chips, not the title.
    public string DisplayName => HasVariantOptions && !string.IsNullOrEmpty(Mod.BaseTitle)
        ? Mod.BaseTitle
        : (string.IsNullOrEmpty(Mod.DisplayName) ? Mod.Name : Mod.DisplayName);

    // When metadata renames the mod, surface the underlying file key so two same-titled mods (e.g.
    // FasterShips vs aaUltraFastShips, both "Faster Ships") are distinguishable at a glance. Hidden on
    // a variant-family row (the option chips carry the distinction).
    public string FileTag => HasVariantOptions || string.Equals(DisplayName, Mod.Name, StringComparison.OrdinalIgnoreCase) ? "" : Mod.Name;
    public Visibility FileTagVisibility => string.IsNullOrEmpty(FileTag) ? Visibility.Collapsed : Visibility.Visible;

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

    // The page-link label reflects the ACTUAL host, not a hardcoded "CurseForge".
    public string ModUrlLabel
    {
        get
        {
            var host = ModUri?.Host ?? "";
            if (host.Contains("nexusmods", StringComparison.OrdinalIgnoreCase)) return "Nexus";
            if (host.Contains("curseforge", StringComparison.OrdinalIgnoreCase)) return "CurseForge";
            return "Mod page";
        }
    }

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
    [NotifyPropertyChangedFor(nameof(MpBadgeTooltip))]
    private MpRisk? mpOverride;

    // Readme/description claim outranks the class hint ("unless the readme says otherwise"); a
    // user override (set after testing) outranks both. Silence stays honest at "MP?".
    public MpRisk EffectiveMp => MpCompat.Effective(MpCompat.InferAll(Mod.Class, Mod.Description), MpOverride);
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

    // Part of a multi-variant family (same mod page / _Nx base) — members sit adjacent + show a chip.
    public bool InVariantGroup { get; init; }
    public Visibility VariantGroupVisibility => InVariantGroup ? Visibility.Visible : Visibility.Collapsed;

    // A multi-variant family is collapsed onto THIS single row; each level is an inline toggle chip.
    public IReadOnlyList<VariantOptionVM> VariantOptions { get; init; } = System.Array.Empty<VariantOptionVM>();
    public bool HasVariantOptions => VariantOptions.Count > 0;
    public Visibility VariantOptionsVisibility => !InLoadOrder && HasVariantOptions ? Visibility.Visible : Visibility.Collapsed;
    // The family row keeps its on/off switch — flipping OFF disables every variant (the parent VM
    // remembers the active one); flipping ON restores the last-active variant. The variant CHIPS
    // pick WHICH one is active when the family is on; the SWITCH picks whether the family is on at all.
    public Visibility ToggleVisibility => !InLoadOrder ? Visibility.Visible : Visibility.Collapsed;
    // What the ToggleSwitch.IsOn binds to: for family rows, ANY variant enabled means the family is on;
    // for single-mod rows, it's just the underlying mod's Enabled. Evaluated at row construction;
    // the row VM is recreated whenever the list rebuilds, so this stays fresh without a notify.
    public bool ToggleIsOn => HasVariantOptions ? VariantOptions.Any(v => v.Enabled) : Enabled;

    // Section divider rendered ABOVE this row — set by the parent on the first row of each list
    // section (e.g. "UE4SS SCRIPTS"), null otherwise. Lets the flat list show sections without grouping.
    public string? SectionHeader { get; set; }
    public Visibility SectionHeaderVisibility => string.IsNullOrEmpty(SectionHeader) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>True for the topmost row that carries a SectionHeader. Drives the one-time chip-legend
    /// affordance — only the first header shows the "?" button to avoid noise across group-by views.</summary>
    public bool IsFirstSectionHeader { get; set; }
    public Visibility FirstSectionHelpVisibility =>
        IsFirstSectionHeader && !string.IsNullOrEmpty(SectionHeader) ? Visibility.Visible : Visibility.Collapsed;

    // Ships with UE4SS (framework mod) — shown with a quiet built-in badge, described from the catalog.
    public bool IsBuiltin => Mod.Builtin;
    public Visibility BuiltinVisibility => Mod.Builtin ? Visibility.Visible : Visibility.Collapsed;

    // Another tool (e.g. Vortex) manages this mod's folder — we surface it read-only with a badge
    // so the picture is complete, but the toggle is disabled and there's no uninstall (honor the law).
    public bool IsManaged => !string.IsNullOrEmpty(Mod.Managed);
    public Visibility ManagedVisibility => IsManaged ? Visibility.Visible : Visibility.Collapsed;
    public string ManagedBadge => string.IsNullOrEmpty(Mod.Managed) ? "" : Mod.Managed.ToUpperInvariant();

    // Capsule chips (uppercase, tracked in XAML).
    public string LocationChip => Mod.Location;
    public bool HasVariant => !string.IsNullOrEmpty(Mod.Variant);
    public Visibility VariantVisibility => HasVariant ? Visibility.Visible : Visibility.Collapsed;
    public string VariantChip => Mod.Variant ?? "";
    public string ClassChip => (Mod.Class ?? "both").ToUpperInvariant();

    /// <summary>Human-friendly explainer for the class chip (BOTH/SP/MP). Used as a hover tooltip;
    /// the chip text itself stays terse. Switch is on the chip's literal value, which the VM derives
    /// from the mod's loadout membership. No notify is wired because Mod.Class is immutable
    /// post-construction (the whole row VM is replaced on rescan) - the default x:Bind OneTime
    /// mode picks up the right value at first render and never goes stale.</summary>
    public string ClassChipTooltip => ClassChip switch
    {
        "BOTH" => "This mod is active in both your SP and MP loadouts.",
        "SP"   => "This mod is active only in your single-player loadout.",
        "MP"   => "This mod is active only in your multiplayer loadout.",
        _      => $"Mod class: {ClassChip}",
    };

    /// <summary>Human-friendly explainer for the MP-safety badge. Switches on the badge text the VM
    /// renders (MP-SAFE / MP-RISKY / SP-ONLY / MP?). Replaces the previous static "click to set" line
    /// with state-aware help.</summary>
    public string MpBadgeTooltip => MpBadge switch
    {
        "MP-SAFE"  => "Author or verified-safe list says this works in MP. Click to override.",
        "MP-RISKY" => "Flagged as risky in MP (anti-cheat / desync). Click to override.",
        "SP-ONLY"  => "Marked SP-only — not in your MP loadout. Click to override.",
        "MP?"      => "No MP stance claimed. Click to override (MP-safe, MP-risky, or SP-only).",
        _          => "Multiplayer safety — click to set.",
    };

    /// <summary>Display name of the framework this mod's engine is missing ("UE4SS"); empty when
    /// nothing's missing. Set by the parent VM from <see cref="MainViewModel.MissingFrameworks"/>.</summary>
    public string MissingFrameworkName { get; init; } = "";

    /// <summary>Get-link URL for the missing framework. Opened via HyperlinkButton.NavigateUri
    /// (SafeUrl guards to https only).</summary>
    public string? MissingFrameworkUrl { get; init; }

    /// <summary>One-sentence why-it-matters for the tooltip.</summary>
    public string MissingFrameworkNote { get; init; } = "";

    public bool HasMissingFramework => !string.IsNullOrEmpty(MissingFrameworkName);
    public Visibility MissingFrameworkVisibility => HasMissingFramework ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Chip label: "NEEDS UE4SS". Uppercased to match the existing chip convention.</summary>
    public string MissingFrameworkChip => HasMissingFramework
        ? "NEEDS " + MissingFrameworkName.ToUpperInvariant()
        : "";

    public Uri? MissingFrameworkUri => SafeUrl.IsHttpUrl(MissingFrameworkUrl) ? new Uri(MissingFrameworkUrl!) : null;

    // Cockpit (config + Lua surfacing). Built on demand by the parent VM, which holds the GameContext.
    public string ModFolderAbs { get; init; } = "";   // set by parent: the mod's folder (for folder mods)
    public bool HasCockpit => Mod.IsFolder && !string.IsNullOrEmpty(ModFolderAbs);

    /// <summary>Absolute paths to .ini files inside this mod's folder. Capped at 20 hits at row build
    /// time. Drives the pencil-icon affordance — surfaces .ini editing without forcing the user to
    /// open the cockpit (which is folder-only and config-cockpit-style). Empty when the mod has no
    /// INIs or isn't a folder mod.</summary>
    public IReadOnlyList<string> IniFiles { get; init; } = Array.Empty<string>();

    /// <summary>Stable id for this mod row, used as the namespace key for INI history backups
    /// (<c>&lt;DataDir&gt;/.ini-history/&lt;ModId&gt;/...</c>). Derived from the family display name
    /// at row build time so two same-titled mods from different sources don't share a snapshot bucket.</summary>
    public string ModId { get; init; } = "";

    public bool HasIniFiles => IniFiles.Count > 0;
    public Visibility IniIconVisibility => HasIniFiles ? Visibility.Visible : Visibility.Collapsed;

    public string OwnedConfigWarning =>
        Mod.ReadOnly && !string.IsNullOrEmpty(Mod.Managed)
            ? $"Managed by {Mod.Managed!.ToUpperInvariant()} — config edits may be overwritten on its next deploy."
            : "";
    public Visibility OwnedConfigWarningVisibility =>
        string.IsNullOrEmpty(OwnedConfigWarning) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CockpitVisibility => HasCockpit ? Visibility.Visible : Visibility.Collapsed;

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
