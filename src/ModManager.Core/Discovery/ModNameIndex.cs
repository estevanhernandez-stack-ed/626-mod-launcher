namespace ModManager.Core.Discovery;

/// <summary>One remembered mod: enough to name a file, credit its author, and link its mod page —
/// nothing more. Facts only — never mod content (never-bundle law). <paramref name="Url"/> is
/// appended (not inserted) with a default so every existing positional construction keeps
/// compiling; an old cached index file deserializes it as null harmlessly.</summary>
public sealed record ModNameIndexEntry(int ModId, string Name, string? Author, int? Endorsements, string? Url = null);

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
    /// Uses the SAME cleaning + scoring as loose-root identify so both surfaces agree.
    ///
    /// A single-token query is an exception: <see cref="NameMatch.PickBestMatch{T}"/>'s 0.5
    /// Jaccard threshold lets a one-token query match ANY two-token candidate that shares that one
    /// token (1/2 = 0.5, clears the bar) — fine under <c>LooseIdentify</c>, which only ever sees
    /// loose-root non-loader rows, but discovery feeds this arbitrary filenames including vanilla
    /// game files (<c>Data/Skyrim.esm</c> -&gt; query "Skyrim" would score exactly 0.5 against an
    /// index entry "Skyrim Together" and come back pre-checked). Below two tokens, require EXACT
    /// token-sequence equality instead of the shared fuzzy threshold — <see cref="NameMatch.PickBestMatch{T}"/>
    /// itself is untouched (still used everywhere else, including two-token-plus queries here).</summary>
    public ModNameIndexEntry? Match(string fileName)
    {
        if (Entries.Count == 0) return null;
        var query = NameMatch.CleanModName(fileName);
        if (string.IsNullOrWhiteSpace(query)) return null;

        var queryTokens = NameMatch.Tokenize(query);
        if (queryTokens.Count < 2)
            return Entries.FirstOrDefault(e => queryTokens.SequenceEqual(NameMatch.Tokenize(e.Name)));

        return NameMatch.PickBestMatch(query, Entries, e => e.Name);
    }
}
