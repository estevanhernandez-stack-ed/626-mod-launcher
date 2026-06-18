// tests/ModManager.Tests/Plugins/PluginFeedInstallerTests.cs
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ModManager.Core.Manifest;
using ModManager.Core.Plugins;

namespace ModManager.Tests.Plugins;

public class PluginFeedInstallerTests
{
    // A throwaway signing key for the fixtures — proves the verify PATH without the real private key.
    private sealed class Signer : IDisposable
    {
        private readonly ECDsa _ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public byte[] PublicKey => _ec.ExportSubjectPublicKeyInfo();
        public byte[] Sign(byte[] data) => _ec.SignData(data, HashAlgorithmName.SHA256, ManifestSignature.Format);
        public void Dispose() => _ec.Dispose();
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "mm-feed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    // Build an in-memory "server": url → bytes. The download delegate just looks the url up.
    private static PluginFeedInstaller.PluginDownload Serve(Dictionary<string, byte[]> map)
        => (url, _) => Task.FromResult(map.TryGetValue(url, out var b) ? b : null);

    private static byte[] IndexJson(string sha) => Encoding.UTF8.GetBytes($$"""
        { "schemaVersion": 1, "plugins": [
          { "id": "nexus", "displayName": "Nexus Mods", "version": "1.0.0", "minBinaryVersion": "0.7.0",
            "downloadUrl": "https://x/nexus.dll", "sigUrl": "https://x/nexus.dll.sig", "sha256": "{{sha}}" } ] }
        """);

    [Fact]
    public async Task Happy_path_installs_the_signed_plugin_and_records_the_version()
    {
        using var signer = new Signer();
        var dir = TempDir();
        try
        {
            var dll = Encoding.ASCII.GetBytes("FAKE-DLL-BYTES");
            var index = IndexJson(PluginIntegrity.Sha256Hex(dll));
            var map = new Dictionary<string, byte[]>
            {
                ["https://x/plugins.json"] = index,
                ["https://x/plugins.json.sig"] = signer.Sign(index),
                ["https://x/nexus.dll"] = dll,
                ["https://x/nexus.dll.sig"] = signer.Sign(dll),
            };
            var record = Path.Combine(dir, "installed.json");
            var req = new PluginFeedRequest("https://x/plugins.json", signer.PublicKey,
                new Version(0, 7, 0), dir, record);

            var installed = await PluginFeedInstaller.RunAsync(req, Serve(map));

            var one = Assert.Single(installed);
            Assert.Equal("nexus", one.Id);
            Assert.Equal("1.0.0", one.Version);
            Assert.True(File.Exists(Path.Combine(dir, "nexus.dll")));
            Assert.True(File.Exists(Path.Combine(dir, "nexus.dll.sig")));
            Assert.Equal("1.0.0", InstalledPluginsStore.Read(record)["nexus"]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task A_bad_index_signature_installs_nothing()
    {
        using var signer = new Signer();
        using var attacker = new Signer();   // signs with the WRONG key
        var dir = TempDir();
        try
        {
            var dll = Encoding.ASCII.GetBytes("FAKE-DLL-BYTES");
            var index = IndexJson(PluginIntegrity.Sha256Hex(dll));
            var map = new Dictionary<string, byte[]>
            {
                ["https://x/plugins.json"] = index,
                ["https://x/plugins.json.sig"] = attacker.Sign(index),   // wrong key
                ["https://x/nexus.dll"] = dll,
                ["https://x/nexus.dll.sig"] = signer.Sign(dll),
            };
            var record = Path.Combine(dir, "installed.json");
            var req = new PluginFeedRequest("https://x/plugins.json", signer.PublicKey,
                new Version(0, 7, 0), dir, record);

            var installed = await PluginFeedInstaller.RunAsync(req, Serve(map));

            Assert.Empty(installed);
            Assert.False(File.Exists(Path.Combine(dir, "nexus.dll")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task A_sha_mismatch_refuses_the_dll()
    {
        using var signer = new Signer();
        var dir = TempDir();
        try
        {
            var dll = Encoding.ASCII.GetBytes("FAKE-DLL-BYTES");
            var index = IndexJson(new string('0', 64));   // index pins a wrong sha
            var map = new Dictionary<string, byte[]>
            {
                ["https://x/plugins.json"] = index,
                ["https://x/plugins.json.sig"] = signer.Sign(index),
                ["https://x/nexus.dll"] = dll,
                ["https://x/nexus.dll.sig"] = signer.Sign(dll),
            };
            var record = Path.Combine(dir, "installed.json");
            var req = new PluginFeedRequest("https://x/plugins.json", signer.PublicKey,
                new Version(0, 7, 0), dir, record);

            var installed = await PluginFeedInstaller.RunAsync(req, Serve(map));

            Assert.Empty(installed);
            Assert.False(File.Exists(Path.Combine(dir, "nexus.dll")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task A_too_high_min_binary_version_is_skipped()
    {
        using var signer = new Signer();
        var dir = TempDir();
        try
        {
            var dll = Encoding.ASCII.GetBytes("FAKE-DLL-BYTES");
            var index = IndexJson(PluginIntegrity.Sha256Hex(dll)); // minBinaryVersion 0.7.0 in the fixture
            var map = new Dictionary<string, byte[]>
            {
                ["https://x/plugins.json"] = index,
                ["https://x/plugins.json.sig"] = signer.Sign(index),
                ["https://x/nexus.dll"] = dll,
                ["https://x/nexus.dll.sig"] = signer.Sign(dll),
            };
            var record = Path.Combine(dir, "installed.json");
            var req = new PluginFeedRequest("https://x/plugins.json", signer.PublicKey,
                new Version(0, 6, 0), dir, record);   // binary older than minBinaryVersion 0.7.0

            var installed = await PluginFeedInstaller.RunAsync(req, Serve(map));
            Assert.Empty(installed);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Offline_index_returns_empty_without_throwing()
    {
        using var signer = new Signer();
        var dir = TempDir();
        try
        {
            var record = Path.Combine(dir, "installed.json");
            var req = new PluginFeedRequest("https://x/plugins.json", signer.PublicKey,
                new Version(0, 7, 0), dir, record);

            // The download delegate returns null for everything (offline).
            var installed = await PluginFeedInstaller.RunAsync(req, (_, _) => Task.FromResult<byte[]?>(null));
            Assert.Empty(installed);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
