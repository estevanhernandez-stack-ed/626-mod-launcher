using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Plugins;

public class AbstractionsContractTests
{
    // A minimal in-test plugin: proves the contract can actually be implemented.
    private sealed class FakePlugin : IModManagerPlugin
    {
        public string Id => "fake";
        public string DisplayName => "Fake";
        public void Register(IPluginHostServices host) => host.AddModSource(new FakeSource());
    }

    private sealed class FakeSource : IModSource
    {
        public string Id => "fake";
        public bool RequiresApiKey => true;
        public Task<SourceIdentifyResult?> IdentifyByHashAsync(string gameDomain, string md5)
            => Task.FromResult<SourceIdentifyResult?>(new SourceIdentifyResult(
                new SourceModRef("fake", gameDomain, 42, "1.0"),
                new SourceModMetadata(10, 1000, "1.1", true, false, Title: "Fake Mod")));
        public Task<SourceModMetadata?> FetchMetadataAsync(SourceModRef r)
            => Task.FromResult<SourceModMetadata?>(new SourceModMetadata(10, 1000, "1.1", Available: true, Endorsed: false));
        public Task<bool> IsUpdateAvailableAsync(SourceModRef r, string installedVersion)
            => Task.FromResult(r.Version != installedVersion);
        public Task<EndorseResult> SetEndorsedAsync(SourceModRef r, bool endorsed)
            => Task.FromResult(new EndorseResult(Ok: true, Refused: false, Message: null, NowEndorsed: endorsed));
        public Task<IReadOnlyList<SourceEndorsement>> GetUserEndorsementsAsync()
            => Task.FromResult<IReadOnlyList<SourceEndorsement>>(
                new[] { new SourceEndorsement(42, "skyrimspecialedition", "Endorsed") });
        public Task<IReadOnlyList<SourceUpdateEntry>> GetRecentlyUpdatedAsync(string gameDomain, string period)
            => Task.FromResult<IReadOnlyList<SourceUpdateEntry>>(
                new[] { new SourceUpdateEntry(42, 1700000000) });
    }

    [Fact]
    public void Fake_plugin_registers_a_mod_source()
    {
        var registered = new List<IModSource>();
        var host = new TestHost(registered);
        new FakePlugin().Register(host);
        Assert.Single(registered);
        Assert.Equal("fake", registered[0].Id);
    }

    [Fact]
    public async Task Source_dtos_carry_the_nexus_shape()
    {
        var s = new FakeSource();
        var refr = await s.IdentifyByHashAsync("skyrimspecialedition", "abc");
        Assert.Equal(42, refr!.Ref.ModId);
        Assert.Equal("Fake Mod", refr.Metadata.Title);
        var meta = await s.FetchMetadataAsync(refr.Ref);
        Assert.Equal(10, meta!.Endorsements);
        Assert.True(await s.IsUpdateAvailableAsync(refr.Ref with { Version = "1.0" }, "0.9"));
        var endorse = await s.SetEndorsedAsync(refr.Ref, true);
        Assert.True(endorse.Ok && endorse.NowEndorsed == true);
    }

    private sealed class TestHost(List<IModSource> sink) : IPluginHostServices
    {
        public void AddModSource(IModSource source) => sink.Add(source);
        public string? GetCredential(string key) => null;
        public System.Net.Http.HttpClient HttpClient { get; } = new();
    }
}
