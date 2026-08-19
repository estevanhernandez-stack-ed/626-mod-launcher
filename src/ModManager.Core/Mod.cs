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

    /// <summary>The current user's endorse state on Nexus (from ModMeta, via Metadata.MergeMetadata).
    /// null = unknown, true = endorsed, false = abstained/undecided. Drives the heart affordance.</summary>
    public bool? Endorsed { get; set; }

    // version + update-available (from a Nexus by-mod-id poll, via Metadata.MergeMetadata)
    public string? Version { get; set; }              // the installed version (what you have)
    public string? NexusLatestVersion { get; set; }   // current version on Nexus (what's available)

    /// <summary>Nexus's own per-user "you have an update" flag (from ModMeta, via
    /// Metadata.MergeMetadata). Null = never told. When present it OUTRANKS the version compare
    /// below, because Nexus knows which file the user downloaded and we often do not.</summary>
    public bool? NexusUpdateAvailable { get; set; }

    /// <summary>True when Nexus reports a different current version than the installed one. Computed,
    /// never trusted from disk: false when no latest was fetched or the versions match.
    ///
    /// <para>A blank/whitespace latest counts as NOT fetched, not as a difference. A blank string is not a
    /// version, so claiming an update for it would put an UPDATE chip on a mod with nothing to update to —
    /// and it would disagree with <c>ModUpdateSummary</c>, which drives the library badge and the updates
    /// view off the same persisted field. The chip and the badge must always agree.</para>
    ///
    /// <para>An unknown INSTALLED version is likewise not an update. A name-search identify deliberately
    /// never writes <see cref="Version"/> — matching by name establishes WHICH mod this is, never which
    /// FILE is on disk — and the by-mod-id enrichment pass then writes <see cref="NexusLatestVersion"/>.
    /// Without this clause the comparison runs between a real upstream version and nothing, differs
    /// always, and lights the chip on every identified row at once. Live smoke on a 98-mod library hit
    /// exactly that, and the reason it survived is that "everything needs updating" is PLAUSIBLE to
    /// someone returning to an old install — a false positive shaped like the truth never gets reported.
    /// Not knowing what is installed is a reason to stay quiet, not a reason to claim a difference.</para></summary>
    /// <para>And a difference is not enough on its own: it has to point FORWARDS. Nexus listing a
    /// version we can prove is older than what is on disk is not an update, and A10's careful wording
    /// for it ("1.0.1 installed · Nexus lists 1.0.0") made the row honest without making it right.
    /// See <see cref="PendingUpdate.LatestIsProvablyOlder"/> — an unorderable pair still lists.</para>
    public bool UpdateAvailable =>
        NexusUpdateAvailable
        ?? (!string.IsNullOrWhiteSpace(NexusLatestVersion)
            && !string.IsNullOrWhiteSpace(Version)
            && NexusLatestVersion != Version
            && !PendingUpdate.LatestIsProvablyOlder(Version, NexusLatestVersion));
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

    /// <summary>The current user's endorse state on Nexus: null = unknown, true = endorsed,
    /// false = abstained/undecided. Persisted user intent (like <see cref="IsManual"/>) — set from an
    /// endorse/abstain action's response or the bulk user-endorsements list, and must survive a rescan.
    /// Additive/nullable: metadata that predates this field has none.</summary>
    public bool? Endorsed { get; set; }

    /// <summary>Nexus's OWN per-user "you have an update" flag, as reported on a search/browse hit.
    /// Null = we were never told.
    ///
    /// <para>This is authoritative in a way the version compare cannot be. Nexus knows which FILE the
    /// user downloaded; we frequently do not — a name-search identify establishes which MOD a row is
    /// and never which file is installed, so <see cref="Version"/> stays null and no local comparison
    /// is possible. Persisting the flag is what lets a row say "update available" for exactly the mods
    /// the browse cards already say it for, instead of guessing from version strings we may not
    /// have.</para>
    ///
    /// <para>It only ever arrives on <c>SourceSearchHit</c> — the by-mod-id metadata fetch used for
    /// enrichment does not carry it — so it is written when a search result passes through, and left
    /// alone otherwise.</para></summary>
    public bool? NexusUpdateAvailable { get; set; }
}
