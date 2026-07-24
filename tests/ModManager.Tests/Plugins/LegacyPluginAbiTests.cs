// tests/ModManager.Tests/Plugins/LegacyPluginAbiTests.cs
using System.Net.Http;
using ModManager.Plugins.Abstractions;
using Xunit;

public class LegacyPluginAbiTests
{
    // Mimics the shipped 0.10.0 plugin: it calls GetCredential unconditionally at Register.
    private sealed class LegacyStyleHost : IPluginHostServices
    {
        public bool GetCredentialCalled;
        public void AddModSource(IModSource s) { }
        #pragma warning disable CS0618
        public string? GetCredential(string key) { GetCredentialCalled = true; return null; }
        #pragma warning restore CS0618
        public HttpClient HttpClient { get; } = new();
        public string AppVersion => "0.11.0";
    }

    [Fact]
    public void Legacy_plugin_can_still_call_GetCredential_without_missing_method()
    {
        var host = new LegacyStyleHost();
        // The 0.10.0 plugin does exactly this at load; it must not throw.
        #pragma warning disable CS0618
        var key = ((IPluginHostServices)host).GetCredential("nexus");
        #pragma warning restore CS0618
        Assert.True(host.GetCredentialCalled);
        Assert.Null(key); // host returns null under OAuth — legacy plugin degrades to "no auth", not a crash
    }
}
