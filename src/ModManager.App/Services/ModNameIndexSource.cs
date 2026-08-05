using System.IO;
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Discovery;
using ModManager.Plugins.Abstractions;

namespace ModManager.App.Services;

/// <summary>
/// Fetches, grows, and persists the per-game Nexus name index. Seed once (bounded), then grow
/// for free from every catalog page browsed, search run, and update poll. camelCase on disk via
/// AtomicJson — the launcher's on-disk JSON law.
///
/// Every failure is non-fatal: a missing, corrupt, or unreachable index resolves to
/// <see cref="ModNameIndex.Empty"/>, and discovery degrades to found-but-unidentified.
/// </summary>
public sealed class ModNameIndexSource
{
    private const int SeedTarget = 500;
    private const int PageSize = 50;

    // Serializes the disk-touching critical section (Load/Merge/Save) — Grow runs from every
    // catalog page browsed and every search, so overlapping calls are the expected case, not the
    // exception. A simple lock is enough for a cache with brief contention; never held across an
    // await (SeedAsync's network calls run unlocked — only its final Save is inside the gate).
    private readonly object _gate = new();

    // Read-side only — AtomicJson.WriteJsonAtomic owns the write-side policy (its Options field is
    // private, so this can't reference it directly). Both are camelCase and therefore compatible;
    // this copy must stay in sync with AtomicJson's policy by hand. A third compatible copy lives in
    // tests/ModManager.Tests/Discovery/ModNameIndexJsonTests.cs. Accepted duplication, not a bug —
    // see task-7 brief. Case-insensitive here only because STJ read-side tolerance already made the
    // camelCase law unenforceable on read; the write side (AtomicJson) is what actually holds the line.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static string PathFor(string dataDir) => Path.Combine(dataDir, "nexus-name-index.json");

    public ModNameIndex Load(string dataDir)
    {
        try
        {
            var file = PathFor(dataDir);
            if (!File.Exists(file)) return ModNameIndex.Empty;
            return JsonSerializer.Deserialize<ModNameIndex>(File.ReadAllText(file), JsonOpts)
                   ?? ModNameIndex.Empty;
        }
        catch { return ModNameIndex.Empty; }
    }

    public void Save(string dataDir, ModNameIndex index)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            AtomicJson.WriteJsonAtomic(PathFor(dataDir), index);
        }
        catch { /* best-effort cache; in-memory state still serves this session */ }
    }

    /// <summary>One bounded seed: the top mods by endorsements — the ones people actually have.
    /// A source without catalog browse (sealed build, old plugin) simply seeds nothing.</summary>
    public async Task<ModNameIndex> SeedAsync(string dataDir, string gameDomain, object source)
    {
        var index = Load(dataDir);
        if (source is not IModCatalogBrowse browse) return index;

        try
        {
            for (var offset = 0; offset < SeedTarget; offset += PageSize)
            {
                var page = await browse.BrowseCatalogAsync(
                    new CatalogQuery(gameDomain, Sort: CatalogSort.MostEndorsed, Offset: offset, Count: PageSize));
                if (page.Hits.Count == 0) break;
                index = ModNameIndex.Merge(index, page.Hits.Select(ToEntry));
            }
        }
        catch { /* offline / rate-limited — keep whatever we already had */ }

        // Persist regardless of where the loop stopped — a page-5-of-10 failure must not throw
        // away the entries already merged from pages 1-4 and waste the network calls that fetched
        // them. Only the disk cycle is locked; the network calls above already finished.
        lock (_gate) { Save(dataDir, index); }

        return index;
    }

    /// <summary>Fold hits the app saw during normal use into the index. Free — no extra calls.</summary>
    public ModNameIndex Grow(string dataDir, IEnumerable<SourceSearchHit> hits)
    {
        lock (_gate)
        {
            var index = Load(dataDir);
            try { index = ModNameIndex.Merge(index, hits.Select(ToEntry)); Save(dataDir, index); }
            catch { /* malformed hits — keep whatever we already had */ }
            return index;
        }
    }

    private static ModNameIndexEntry ToEntry(SourceSearchHit hit)
        => new(hit.ModId, hit.Name, hit.Author, hit.EndorsementCount);
}
