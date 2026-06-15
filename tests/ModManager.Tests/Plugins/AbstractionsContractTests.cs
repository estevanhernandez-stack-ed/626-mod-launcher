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
        public Task<SourceModRef?> IdentifyByHashAsync(string gameDomain, string md5)
            => Task.FromResult<SourceModRef?>(new SourceModRef("fake", gameDomain, 42, "1.0"));
        public Task<SourceModMetadata?> FetchMetadataAsync(SourceModRef r)
            => Task.FromResult<SourceModMetadata?>(new SourceModMetadata(10, 1000, "1.1", Available: true, Endorsed: false));
        public Task<bool> IsUpdateAvailableAsync(SourceModRef r, string installedVersion)
            => Task.FromResult(r.Version != installedVersion);
        public Task<EndorseResult> SetEndorsedAsync(SourceModRef r, bool endorsed)
            => Task.FromResult(new EndorseResult(Ok: true, Refused: false, Message: null, NowEndorsed: endorsed));
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
        Assert.Equal(42, refr!.ModId);
        var meta = await s.FetchMetadataAsync(refr);
        Assert.Equal(10, meta!.Endorsements);
        Assert.True(await s.IsUpdateAvailableAsync(refr with { Version = "1.0" }, "0.9"));
        var endorse = await s.SetEndorsedAsync(refr, true);
        Assert.True(endorse.Ok && endorse.NowEndorsed == true);
    }

    private sealed class TestHost(List<IModSource> sink) : IPluginHostServices
    {
        public void AddModSource(IModSource source) => sink.Add(source);
        public string? GetCredential(string key) => null;
        public System.Net.Http.HttpClient HttpClient { get; } = new();
    }
}
