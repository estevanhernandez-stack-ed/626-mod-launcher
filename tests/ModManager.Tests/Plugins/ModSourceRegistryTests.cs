using ModManager.Core.Plugins;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Plugins;

public class ModSourceRegistryTests
{
    private sealed class Src(string id) : IModSource
    {
        public string Id => id;
        public bool RequiresApiKey => false;
        public Task<SourceIdentifyResult?> IdentifyByHashAsync(string g, string m) => Task.FromResult<SourceIdentifyResult?>(null);
        public Task<SourceModMetadata?> FetchMetadataAsync(SourceModRef r) => Task.FromResult<SourceModMetadata?>(null);
        public Task<bool> IsUpdateAvailableAsync(SourceModRef r, string v) => Task.FromResult(false);
        public Task<EndorseResult> SetEndorsedAsync(SourceModRef r, bool e) => Task.FromResult(new EndorseResult(true, false, null, e));
        public Task<IReadOnlyList<SourceEndorsement>> GetUserEndorsementsAsync()
            => Task.FromResult<IReadOnlyList<SourceEndorsement>>(System.Array.Empty<SourceEndorsement>());
        public Task<IReadOnlyList<SourceUpdateEntry>> GetRecentlyUpdatedAsync(string gameDomain, string period)
            => Task.FromResult<IReadOnlyList<SourceUpdateEntry>>(System.Array.Empty<SourceUpdateEntry>());
    }

    [Fact]
    public void Empty_registry_has_no_sources()  // the zero-plugins invariant (the Store SKU)
    {
        var reg = new ModSourceRegistry();
        Assert.Empty(reg.Sources);
        Assert.Null(reg.ById("nexus"));
    }

    [Fact]
    public void Registered_sources_resolve_by_id()
    {
        var reg = new ModSourceRegistry();
        reg.Add(new Src("nexus"));
        Assert.Single(reg.Sources);
        Assert.Equal("nexus", reg.ById("nexus")!.Id);
    }
}
