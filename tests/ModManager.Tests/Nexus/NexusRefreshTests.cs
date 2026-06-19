using ModManager.Core;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Nexus;

// The per-mod refresh primitive: resolve a mod id (stored NexusModId or parsed from the stored
// Url), FetchMetadata by id over the IModSource contract, then refresh the live stats + capture
// the latest version WITHOUT touching the installed Version / NexusFileId (the "what you have"
// side of the update compare).
public class NexusRefreshTests
{
    // A fake IModSource — only FetchMetadataAsync is exercised; everything else throws if touched.
    private sealed class FakeSource : IModSource
    {
        private readonly Func<SourceModRef, Task<SourceModMetadata?>> _fetch;
        public int FetchCalls { get; private set; }
        public string? LastDomain { get; private set; }
        public int LastModId { get; private set; }
        public FakeSource(Func<SourceModRef, Task<SourceModMetadata?>> fetch) => _fetch = fetch;

        public string Id => "nexus";
        public bool RequiresApiKey => true;

        public Task<SourceModMetadata?> FetchMetadataAsync(SourceModRef modRef)
        {
            FetchCalls++;
            LastDomain = modRef.GameDomain;
            LastModId = modRef.ModId;
            return _fetch(modRef);
        }

        public Task<SourceIdentifyResult?> IdentifyByHashAsync(string gameDomain, string md5) => throw new NotSupportedException();
        public Task<bool> IsUpdateAvailableAsync(SourceModRef modRef, string installedVersion) => throw new NotSupportedException();
        public Task<EndorseResult> SetEndorsedAsync(SourceModRef modRef, bool endorsed) => throw new NotSupportedException();
        public Task<IReadOnlyList<SourceEndorsement>> GetUserEndorsementsAsync() => throw new NotSupportedException();
        public Task<IReadOnlyList<SourceUpdateEntry>> GetRecentlyUpdatedAsync(string gameDomain, string period) => throw new NotSupportedException();
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

        // The fetched metadata reports new stats + a newer current version. Identity fields the
        // source happens to report (Title, NexusFileId) MUST be ignored by the selective overlay.
        var fetched = new SourceModMetadata(
            Endorsements: 99, Downloads: 5000, LatestVersion: "2.1", Available: true, Endorsed: null,
            Title: "Cool Mod (renamed upstream)", NexusFileId: 9999);
        var fake = new FakeSource(r => Task.FromResult<SourceModMetadata?>(r.ModId == 1234 ? fetched : null));

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
        // identity preserved (selective overlay never clobbers the manual/installed title)
        Assert.Equal("Cool Mod", result.Title);
        Assert.Equal("modder", result.Author);
        Assert.Equal("Nexus", result.Source);
        Assert.Equal(1234, result.NexusModId);
        Assert.Equal(1, fake.FetchCalls);
        Assert.Equal("eldenring", fake.LastDomain);
        Assert.Equal(1234, fake.LastModId);
    }

    [Fact]
    public async Task RefreshOneAsync_preserves_manual_flag_and_identity()
    {
        var existing = new ModMeta { IsManual = true, NexusModId = 1234, Version = "1.0", Title = "Pinned" };
        var fetched = new SourceModMetadata(
            Endorsements: 5, Downloads: null, LatestVersion: "2.0", Available: null, Endorsed: null,
            Title: "Upstream Title");
        var fake = new FakeSource(_ => Task.FromResult<SourceModMetadata?>(fetched));

        var result = await NexusRefresh.RefreshOneAsync(existing, "eldenring", fake);

        Assert.NotNull(result);
        Assert.True(result!.IsManual);
        Assert.Equal("Pinned", result.Title);
        Assert.Equal("1.0", result.Version);
        Assert.Equal("2.0", result.NexusLatestVersion);
    }

    [Fact]
    public async Task RefreshOneAsync_skips_when_no_resolvable_id_no_source_call()
    {
        var existing = new ModMeta { Url = "https://www.curseforge.com/skyrim/mods/x" };
        var fake = new FakeSource(_ => Task.FromResult<SourceModMetadata?>(
            new SourceModMetadata(null, null, "9", null, null)));

        var result = await NexusRefresh.RefreshOneAsync(existing, "skyrim", fake);

        Assert.Null(result);
        Assert.Equal(0, fake.FetchCalls);
    }

    [Fact]
    public async Task RefreshOneAsync_returns_null_when_fetch_misses()
    {
        var existing = new ModMeta { NexusModId = 1234, Version = "1.0" };
        var fake = new FakeSource(_ => Task.FromResult<SourceModMetadata?>(null));

        var result = await NexusRefresh.RefreshOneAsync(existing, "eldenring", fake);

        Assert.Null(result);
        Assert.Equal(1, fake.FetchCalls);
    }

    [Fact]
    public async Task RefreshOneAsync_propagates_rate_limit_exception()
    {
        var existing = new ModMeta { NexusModId = 1234, Version = "1.0" };
        var fake = new FakeSource(_ => throw new SourceRateLimitException());

        await Assert.ThrowsAsync<SourceRateLimitException>(
            () => NexusRefresh.RefreshOneAsync(existing, "eldenring", fake));
    }
}
