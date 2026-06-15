namespace ModManager.Core;

/// <summary>
/// A mod as the launcher sees it: filename identity (name/base/variant), MP-SP class +
/// enabled state, and the enrichment fields filled in from per-game metadata.
/// </summary>
public sealed class Mod
{
    // identity
    public string Name { get; set; } = "";
    public string Base { get; set; } = "";
    public string? Variant { get; set; }

    // state
    public string? Class { get; set; }
    public bool Enabled { get; set; }
    public bool OnServer { get; set; }

    // scan placement
    public string Location { get; set; } = "";
    public List<string> Files { get; set; } = new();
    public bool IsFolder { get; set; }
    public bool HasVortexFolder { get; set; }
    // Set when this mod lives in a location another tool manages (e.g. "vortex"): show read-only.
    public string? Managed { get; set; }
    // True when this mod's location is owned by another tool (Coexist posture): the row is read-only.
    public bool ReadOnly { get; set; }
    // Set to a loader id ("ue4ss") when this mod's enable state is driven through a loader manifest
    // (Conductor posture) rather than by moving files. Null = file-move model.
    public string? Loader { get; set; }
    // True for a UE4SS framework folder that ships with the loader (described from the bundled catalog).
    public bool Builtin { get; set; }
    // True for the DLL mod loader row (dinput8.dll) — the App renders it distinguished (LOADER chip)
    // and routes its toggle through the reversible cascade. Transient: Mod is never serialized
    // (only ModMeta + the DisabledMeta sidecar reach disk); add [JsonIgnore] if a write path is ever added.
    public bool IsLoader { get; set; }

    // enrichment (from Metadata.MergeMetadata)
    public string DisplayName { get; set; } = "";
    public string BaseTitle { get; set; } = "";
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? AuthorUrl { get; set; }
    public string? ModUrl { get; set; }
    public string? Source { get; set; }
    public string? Donate { get; set; }
    public string? Image { get; set; }
    public long? Downloads { get; set; }
    public int? EndorsementCount { get; set; }
    public bool? Available { get; set; }     // false = removed from Nexus (drives the row hint)
    public string? Category { get; set; }
    public bool HasMeta { get; set; }

    // version + update-available (from a Nexus by-mod-id poll, via Metadata.MergeMetadata)
    public string? Version { get; set; }              // the installed version (what you have)
    public string? NexusLatestVersion { get; set; }   // current version on Nexus (what's available)

    /// <summary>True when Nexus reports a different current version than the installed one. Computed,
    /// never trusted from disk: false when no latest was fetched or the versions match.</summary>
    public bool UpdateAvailable => NexusLatestVersion is { } v && v != Version;
}

/// <summary>A per-game metadata.json entry: the real title/credit/links for a mod base.</summary>
public sealed class ModMeta
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? AuthorUrl { get; set; }
    public string? Url { get; set; }
    public string? Source { get; set; }
    public string? Donate { get; set; }
    public string? Image { get; set; }
    public long? Downloads { get; set; }
    public int? CurseforgeId { get; set; }
    public string? Category { get; set; }
    /// <summary>True when this entry was set by the user via the manual-match dialog. Auto-identify
    /// (Nexus md5, CF fingerprint, name search) never clobbers a manual entry — the row is locked
    /// to whatever the user pasted, even when a later rescan would match the same key.</summary>
    public bool IsManual { get; set; }

    /// <summary>When this mod first landed (set by the App at intake). Drives the off-boarding sheet's
    /// "installed on" line. Nullable: mods that predate this field have no recorded date.</summary>
    public DateTime? InstalledUtc { get; set; }

    /// <summary>How the source Url was derived: "manual" | "fingerprint" | "md5" | "nameSearch" | null.
    /// Lets the off-boarding sheet hedge a low-confidence name-search match ("likely source:")
    /// versus a high-confidence one ("source:").</summary>
    public string? SourceConfidence { get; set; }

    // Nexus enrichment (read live from the API response; all optional/additive).
    public int? EndorsementCount { get; set; }
    public string? Version { get; set; }
    public bool? Available { get; set; }              // false = Nexus reports the mod removed/unavailable
    public bool? ContainsAdultContent { get; set; }
    public int? NexusModId { get; set; }              // stable handle for endorse / update-check
    public int? NexusFileId { get; set; }             // the installed file's id (update-check key)

    /// <summary>Last-fetched current version on Nexus (from a by-mod-id poll). The "what's available"
    /// side of the update compare; the installed-side stays in <see cref="Version"/>. Additive/nullable —
    /// metadata that predates the poll has none.</summary>
    public string? NexusLatestVersion { get; set; }
}
