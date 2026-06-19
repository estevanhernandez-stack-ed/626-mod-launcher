using System.Net;
using ModManager.Plugins.Abstractions;

namespace ModManager.Plugin.Nexus.Tests;

// FIX 6: the plugin must wire the host's AppVersion into the NexusModSource so the Nexus-ToS
// Application-Version header carries the REAL launcher version, not the source's "0.0.0" fallback.
// NexusPlugin.Register reads host.AppVersion and passes it to the source ctor; this drives Register
// through a fake host whose HttpClient is the StubHandler, then asserts the registered source sends
// that version on the wire.
public class NexusPluginRegisterTests
{
    private sealed class FakeHost(HttpClient http, string appVersion) : IPluginHostServices
    {
        public IModSource? Registered { get; private set; }
        public void AddModSource(IModSource source) => Registered = source;
        public string? GetCredential(string key) => "K";
        public HttpClient HttpClient { get; } = http;
        public string AppVersion { get; } = appVersion;
    }

    [Fact]
    public async Task Register_passes_host_app_version_through_to_the_tos_header()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{ "mod_id": 5, "name": "X" }""");
        var host = new FakeHost(new HttpClient(stub), appVersion: "1.4.2");

        new NexusPlugin().Register(host);

        Assert.NotNull(host.Registered);
        Assert.Equal("nexus", host.Registered!.Id);

        // Exercise the registered source — the version it sends is the host's, not the "0.0.0" fallback.
        await host.Registered.FetchMetadataAsync(
            new SourceModRef(SourceId: "nexus", GameDomain: "skyrim", ModId: 5, Version: "1.0"));
        Assert.Equal("1.4.2", stub.Calls[0].AppVersion);
        Assert.Equal("626-mod-launcher", stub.Calls[0].AppName);
    }
}
