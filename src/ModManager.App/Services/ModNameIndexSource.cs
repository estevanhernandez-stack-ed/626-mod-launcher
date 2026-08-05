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

    // Same debounce window + per-game stamp convention as NexusUpdatePoll.MaybePollAsync — piggyback
    // on the established mechanism instead of inventing a second one.
    private static readonly TimeSpan SeedDebounce = TimeSpan.FromHours(24);

    private static string SeedStampPath(string gameId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModManagerBuilder",
        $"last-nameindex-seed-{Sanitize(gameId)}.txt");

    // Game ids are slugs already, but keep the stamp filename safe regardless of what's in the id
    // (mirrors NexusUpdatePoll.Sanitize).
    private static string Sanitize(string id)
    {
        var chars = id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

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

    /// <summary>Debounced seed for the active game — the ~24h gate <see cref="SeedAsync"/> itself
    /// doesn't have (Task 7 shipped the seed with no caller ever gating or calling it; this closes
    /// that gap). Mirrors <c>NexusUpdatePoll.MaybePollAsync</c>'s stamp mechanism exactly (same
    /// window, same per-game stamp file convention under <c>%LOCALAPPDATA%\ModManagerBuilder</c>) so
    /// the two auto-run-on-game-load checks share one throttling pattern. No-op (and does not touch
    /// the stamp) when there's no source, no Nexus connection, or no domain for this game — comfort,
    /// never load-bearing; every failure is swallowed the same way <see cref="SeedAsync"/> already
    /// swallows its own.</summary>
    public async Task MaybeSeedAsync(string dataDir, string gameId, string? gameDomain, bool nexusConnected, object? source)
    {
        try
        {
            if (!nexusConnected || source is null || string.IsNullOrWhiteSpace(gameDomain)) return;

            var stampPath = SeedStampPath(gameId);
            var last = NexusPollStamp.Read(stampPath);
            if (!NexusPollStamp.ShouldPoll(last, DateTime.UtcNow, SeedDebounce)) return;

            await SeedAsync(dataDir, gameDomain!, source);
            NexusPollStamp.Write(stampPath, DateTime.UtcNow);
        }
        catch { /* comfort, not load-bearing — seeding failure never breaks the session */ }
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
