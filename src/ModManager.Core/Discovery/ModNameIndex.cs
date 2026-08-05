namespace ModManager.Core.Discovery;

/// <summary>One remembered mod: enough to name a file and credit its author, nothing more.
/// Facts only — never mod content (never-bundle law).</summary>
public sealed record ModNameIndexEntry(int ModId, string Name, string? Author, int? Endorsements);

/// <summary>
/// A per-game cache of mod names, used to identify extracted mods the launcher finds on disk.
/// Nexus md5 lookup matches the PUBLISHED ARCHIVE hash, so an extracted mod's loose files can
/// never be md5-identified — this index is what makes those identifiable at all.
///
/// A cache, never a database: bounded, lossy, and safe to delete. Pure — the App fetches and
/// persists; matching happens here with no I/O.
/// </summary>
public sealed record ModNameIndex(IReadOnlyList<ModNameIndexEntry> Entries)
{
    public const int DefaultCap = 5000;

    public static ModNameIndex Empty { get; } = new(Array.Empty<ModNameIndexEntry>());

    /// <summary>Fold new entries in: dedupe by mod id (incoming wins — it is fresher), then cap,
    /// dropping the lowest-endorsement entries first so the mods people actually have survive.</summary>
    public static ModNameIndex Merge(
        ModNameIndex existing, IEnumerable<ModNameIndexEntry> incoming, int cap = DefaultCap)
    {
        var byId = existing.Entries.ToDictionary(e => e.ModId);
        foreach (var entry in incoming) byId[entry.ModId] = entry;

        var kept = byId.Values
            .OrderByDescending(e => e.Endorsements ?? 0)
            .Take(cap)
            .ToList();

        return new ModNameIndex(kept);
    }

    /// <summary>Best known mod for a file name, or null when nothing clears the threshold.
    /// Uses the SAME cleaning + scoring as loose-root identify so both surfaces agree.</summary>
    public ModNameIndexEntry? Match(string fileName)
    {
        if (Entries.Count == 0) return null;
        var query = NameMatch.CleanModName(fileName);
        if (string.IsNullOrWhiteSpace(query)) return null;
        return NameMatch.PickBestMatch(query, Entries, e => e.Name);
    }
}
