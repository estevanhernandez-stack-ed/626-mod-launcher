using ModManager.Core;

namespace ModManager.Tests.Nexus;

// The per-mod refresh primitive: resolve a mod id (stored NexusModId or parsed from the stored
// Url), GetMod by id, then refresh the live stats + capture the latest version WITHOUT touching
// the installed Version / NexusFileId (the "what you have" side of the update compare).
public class NexusRefreshTests
{
    // A fake INexusClient — only GetModAsync is exercised; everything else throws if touched.
    private sealed class FakeNexus : INexusClient
    {
        private readonly Func<string, int, Task<ModMeta?>> _getMod;
        public int GetModCalls { get; private set; }
        public FakeNexus(Func<string, int, Task<ModMeta?>> getMod) => _getMod = getMod;

        public Task<ModMeta?> GetModAsync(string gameDomain, int modId)
        {
            GetModCalls++;
            return _getMod(gameDomain, modId);
        }

        public Task<NexusMd5Match?> GetByMd5Async(string gameDomain, string md5) => throw new NotSupportedException();
        public Task<NexusUser?> ValidateAsync() => throw new NotSupportedException();
        public Task<IReadOnlyList<NexusUpdateEntry>> GetRecentlyUpdatedAsync(string d, string p) => throw new NotSupportedException();
        public NexusRateLimit? LastRateLimit => null;
    }

    [Fact]
    public void ResolveModId_prefers_stored_NexusModId()
    {
        var meta = new ModMeta { NexusModId = 4242, Url = "https://www.nexusmods.com/eldenring/mods/9999" };
        Assert.Equal(4242, NexusRefresh.ResolveModId(meta));
    }

    [Fact]
    public void ResolveModId_parses_nexus_url_when_no_id()
    {
        var meta = new ModMeta { Url = "https://www.nexusmods.com/eldenring/mods/1234" };
        Assert.Equal(1234, NexusRefresh.ResolveModId(meta));
    }

    [Fact]
    public void ResolveModId_returns_null_for_curseforge_url()
    {
        var meta = new ModMeta { Url = "https://www.curseforge.com/skyrim/mods/some-mod" };
        Assert.Null(NexusRefresh.ResolveModId(meta));
    }

    [Fact]
    public void ResolveModId_returns_null_when_nothing_resolvable()
    {
        Assert.Null(NexusRefresh.ResolveModId(new ModMeta()));
        Assert.Null(NexusRefresh.ResolveModId(new ModMeta { Url = "not a url" }));
    }

    [Fact]
    public async Task RefreshOneAsync_refreshes_stats_and_latest_version_preserving_installed()
    {
        var existing = new ModMeta
        {
            Title = "Cool Mod",
            Author = "modder",
            Source = "Nexus",
            IsManual = false,
            NexusModId = 1234,
            NexusFileId = 5000,          // installed file id — must NOT change
            Version = "1.0",             // installed version — must NOT change
            EndorsementCount = 1,
            Downloads = 10,
            Available = true,
        };

        // The fetched mod reports new stats + a newer current version.
        var fetched = new ModMeta
        {
            Title = "Cool Mod (renamed upstream)",
            EndorsementCount = 99,
            Downloads = 5000,
            Available = true,
            Version = "2.1",             // the current Nexus version
            NexusModId = 1234,
            NexusFileId = 9999,          // upstream's latest file — must be ignored
        };
        var fake = new FakeNexus((_, id) => Task.FromResult<ModMeta?>(id == 1234 ? fetched : null));

        var result = await NexusRefresh.RefreshOneAsync(existing, "eldenring", fake);

        Assert.NotNull(result);
        // refreshed live stats
        Assert.Equal(99, result!.EndorsementCount);
        Assert.Equal(5000, result.Downloads);
        Assert.True(result.Available);
        // latest version captured
        Assert.Equal("2.1", result.NexusLatestVersion);
        // installed side preserved
        Assert.Equal("1.0", result.Version);
        Assert.Equal(5000, result.NexusFileId);
        // identity preserved
        Assert.Equal("Cool Mod", result.Title);
        Assert.Equal("modder", result.Author);
        Assert.Equal("Nexus", result.Source);
        Assert.Equal(1234, result.NexusModId);
        Assert.Equal(1, fake.GetModCalls);
    }

    [Fact]
    public async Task RefreshOneAsync_preserves_manual_flag_and_identity()
    {
        var existing = new ModMeta { IsManual = true, NexusModId = 1234, Version = "1.0", Title = "Pinned" };
        var fetched = new ModMeta { Version = "2.0", EndorsementCount = 5 };
        var fake = new FakeNexus((_, _) => Task.FromResult<ModMeta?>(fetched));

        var result = await NexusRefresh.RefreshOneAsync(existing, "eldenring", fake);

        Assert.NotNull(result);
        Assert.True(result!.IsManual);
        Assert.Equal("Pinned", result.Title);
        Assert.Equal("1.0", result.Version);
        Assert.Equal("2.0", result.NexusLatestVersion);
    }

    [Fact]
    public async Task RefreshOneAsync_skips_when_no_resolvable_id_no_client_call()
    {
        var existing = new ModMeta { Url = "https://www.curseforge.com/skyrim/mods/x" };
        var fake = new FakeNexus((_, _) => Task.FromResult<ModMeta?>(new ModMeta { Version = "9" }));

        var result = await NexusRefresh.RefreshOneAsync(existing, "skyrim", fake);

        Assert.Null(result);
        Assert.Equal(0, fake.GetModCalls);
    }

    [Fact]
    public async Task RefreshOneAsync_returns_null_when_GetMod_misses()
    {
        var existing = new ModMeta { NexusModId = 1234, Version = "1.0" };
        var fake = new FakeNexus((_, _) => Task.FromResult<ModMeta?>(null));

        var result = await NexusRefresh.RefreshOneAsync(existing, "eldenring", fake);

        Assert.Null(result);
        Assert.Equal(1, fake.GetModCalls);
    }

    [Fact]
    public async Task RefreshOneAsync_propagates_rate_limit_exception()
    {
        var existing = new ModMeta { NexusModId = 1234, Version = "1.0" };
        var fake = new FakeNexus((_, _) =>
            throw new NexusRateLimitException(new NexusRateLimit(0, 100, 0, 50)));

        await Assert.ThrowsAsync<NexusRateLimitException>(
            () => NexusRefresh.RefreshOneAsync(existing, "eldenring", fake));
    }
}
