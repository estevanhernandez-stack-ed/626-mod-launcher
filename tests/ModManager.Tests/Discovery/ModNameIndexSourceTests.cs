using ModManager.App.Services;
using ModManager.Core.Discovery;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Discovery;

// ModNameIndexSource.cs is source-linked into this project (see the .csproj — App-only, no WinUI
// dependency for this file) rather than moved into Core: final-review test-coverage gap. Covers
// Grow's degrade-on-throw behavior, SeedAsync's partial-progress persistence, and the
// autoCheckEnabled gate MaybeSeedAsync added in an earlier fix round.
public class ModNameIndexSourceTests
{
    private sealed class FakeCatalogBrowse : IModCatalogBrowse
    {
        private readonly Func<CatalogQuery, Task<CatalogPage>> _fn;
        public int Calls { get; private set; }
        public FakeCatalogBrowse(Func<CatalogQuery, Task<CatalogPage>> fn) => _fn = fn;
        public Task<CatalogPage> BrowseCatalogAsync(CatalogQuery query) { Calls++; return _fn(query); }
    }

    private static SourceSearchHit Hit(int modId, string name, string? url = null)
        => new("windrose", modId, name, "Author", null, 10, url);

    // MaybeSeedAsync's debounce stamp lives at a FIXED, non-injectable real-filesystem path
    // (%LOCALAPPDATA%\ModManagerBuilder\last-nameindex-seed-<gameId>.txt) — there's no seam to
    // fake the clock or redirect the path. Every gameId below is a fresh GUID so a stamp left by
    // an earlier test run (same day, same machine) can never make a "should have seeded" test
    // flaky by looking already-debounced. Best-effort cleanup afterward keeps the real
    // LocalAppData folder from accumulating one throwaway file per test run forever.
    private static string UniqueGameId() => "test-" + Guid.NewGuid().ToString("N");

    private static string StampPathFor(string gameId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModManagerBuilder", $"last-nameindex-seed-{gameId}.txt");

    // --- Grow ---------------------------------------------------------------------------------

    [Fact]
    public void Grow_discards_the_whole_batch_when_the_hit_sequence_throws_mid_enumeration()
    {
        var dataDir = TestSupport.TempDir("nameidx-grow-throw-");
        var src = new ModNameIndexSource();
        src.Grow(dataDir, new[] { Hit(1, "Faster Ships") }); // seed one good entry first

        static IEnumerable<SourceSearchHit> Throwing()
        {
            yield return Hit(2, "Good Hit Before The Throw");
            throw new InvalidOperationException("network hiccup mid-stream");
        }

        // Grow's try/catch wraps the WHOLE Merge+Save — a throw partway through the incoming
        // sequence discards the entire call, not "keep what enumerated before the exception".
        var result = src.Grow(dataDir, Throwing());

        var only = Assert.Single(result.Entries);
        Assert.Equal(1, only.ModId);
        var reloaded = src.Load(dataDir);
        var onlyOnDisk = Assert.Single(reloaded.Entries);
        Assert.Equal(1, onlyOnDisk.ModId); // nothing partial ever hit disk either
    }

    [Fact]
    public void Grow_tolerates_a_degenerate_hit_without_throwing()
    {
        var dataDir = TestSupport.TempDir("nameidx-grow-malformed-");
        var src = new ModNameIndexSource();

        var result = src.Grow(dataDir, new[] { new SourceSearchHit("windrose", 99, null!, null, null, null, null) });

        var only = Assert.Single(result.Entries);
        Assert.Equal(99, only.ModId);
        Assert.Null(only.Name); // tolerated, not thrown — Match() on a null name just misses later
    }

    [Fact]
    public void Grow_merges_new_hits_over_the_existing_persisted_index()
    {
        var dataDir = TestSupport.TempDir("nameidx-grow-merge-");
        var src = new ModNameIndexSource();
        src.Grow(dataDir, new[] { Hit(1, "Faster Ships") });

        var result = src.Grow(dataDir, new[] { Hit(2, "More Stacks") });

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(2, src.Load(dataDir).Entries.Count);
    }

    // --- SeedAsync ------------------------------------------------------------------------------

    [Fact]
    public async Task SeedAsync_persists_the_pages_fetched_before_a_mid_loop_failure()
    {
        var dataDir = TestSupport.TempDir("nameidx-seed-partial-");
        var src = new ModNameIndexSource();
        var browse = new FakeCatalogBrowse(q =>
        {
            if (q.Offset == 0)
                return Task.FromResult(new CatalogPage(new[] { Hit(1, "Page One Mod") }, 1000, Array.Empty<CatalogCategory>()));
            throw new HttpRequestException("rate limited");
        });

        var index = await src.SeedAsync(dataDir, "windrose", browse);

        Assert.True(browse.Calls >= 2); // proves the second page was attempted and failed
        var only = Assert.Single(index.Entries);
        Assert.Equal(1, only.ModId);
        var reloaded = src.Load(dataDir);
        Assert.Single(reloaded.Entries); // persisted despite the mid-loop failure
    }

    [Fact]
    public async Task SeedAsync_stops_and_persists_when_a_page_comes_back_empty()
    {
        var dataDir = TestSupport.TempDir("nameidx-seed-empty-page-");
        var src = new ModNameIndexSource();
        var browse = new FakeCatalogBrowse(q => Task.FromResult(q.Offset == 0
            ? new CatalogPage(new[] { Hit(1, "Only Mod") }, 1, Array.Empty<CatalogCategory>())
            : CatalogPage.Empty));

        var index = await src.SeedAsync(dataDir, "windrose", browse);

        Assert.Single(index.Entries);
    }

    [Fact]
    public async Task SeedAsync_seeds_nothing_for_a_source_without_catalog_browse()
    {
        var dataDir = TestSupport.TempDir("nameidx-seed-noop-");
        var src = new ModNameIndexSource();

        var index = await src.SeedAsync(dataDir, "windrose", new object());

        Assert.Empty(index.Entries);
    }

    // --- MaybeSeedAsync gate ----------------------------------------------------------------

    [Fact]
    public async Task MaybeSeedAsync_does_nothing_when_autoCheck_is_disabled()
    {
        var dataDir = TestSupport.TempDir("nameidx-gate-off-");
        var gameId = UniqueGameId();
        var src = new ModNameIndexSource();
        var browse = new FakeCatalogBrowse(q => Task.FromResult(CatalogPage.Empty));

        await src.MaybeSeedAsync(dataDir, gameId, "windrose", nexusConnected: true, autoCheckEnabled: false, source: browse);

        Assert.Equal(0, browse.Calls);
        Assert.Empty(src.Load(dataDir).Entries);
        Assert.False(File.Exists(StampPathFor(gameId))); // gated out before the stamp is ever touched
    }

    [Fact]
    public async Task MaybeSeedAsync_does_nothing_when_not_connected_even_if_autoCheck_is_on()
    {
        var dataDir = TestSupport.TempDir("nameidx-gate-disconnected-");
        var gameId = UniqueGameId();
        var src = new ModNameIndexSource();
        var browse = new FakeCatalogBrowse(q => Task.FromResult(CatalogPage.Empty));

        await src.MaybeSeedAsync(dataDir, gameId, "windrose", nexusConnected: false, autoCheckEnabled: true, source: browse);

        Assert.Equal(0, browse.Calls);
    }

    [Fact]
    public async Task MaybeSeedAsync_seeds_when_connected_domained_and_autoCheck_is_on()
    {
        var dataDir = TestSupport.TempDir("nameidx-gate-on-");
        var gameId = UniqueGameId();
        var src = new ModNameIndexSource();
        var browse = new FakeCatalogBrowse(q =>
            Task.FromResult(new CatalogPage(new[] { Hit(1, "Seeded Mod") }, 1, Array.Empty<CatalogCategory>())));
        try
        {
            await src.MaybeSeedAsync(dataDir, gameId, "windrose", nexusConnected: true, autoCheckEnabled: true, source: browse);

            Assert.True(browse.Calls >= 1);
            Assert.Single(src.Load(dataDir).Entries);
        }
        finally { TryDeleteStamp(gameId); }
    }

    [Fact]
    public async Task MaybeSeedAsync_does_not_reseed_within_the_debounce_window()
    {
        var dataDir = TestSupport.TempDir("nameidx-gate-debounce-");
        var gameId = UniqueGameId();
        var src = new ModNameIndexSource();
        var browse = new FakeCatalogBrowse(q =>
            Task.FromResult(new CatalogPage(new[] { Hit(1, "Seeded Mod") }, 1, Array.Empty<CatalogCategory>())));
        try
        {
            await src.MaybeSeedAsync(dataDir, gameId, "windrose", nexusConnected: true, autoCheckEnabled: true, source: browse);
            var callsAfterFirst = browse.Calls;
            await src.MaybeSeedAsync(dataDir, gameId, "windrose", nexusConnected: true, autoCheckEnabled: true, source: browse);

            Assert.Equal(callsAfterFirst, browse.Calls); // the stamp gates the second call out entirely
        }
        finally { TryDeleteStamp(gameId); }
    }

    private static void TryDeleteStamp(string gameId)
    {
        try { File.Delete(StampPathFor(gameId)); } catch { /* best-effort test cleanup only */ }
    }
}
