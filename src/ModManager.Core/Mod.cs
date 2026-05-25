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
    public bool HasMeta { get; set; }
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
}
