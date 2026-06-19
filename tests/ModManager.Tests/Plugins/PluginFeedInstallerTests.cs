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

    // A one-entry index with an arbitrary id + version (used for the update / escape cases).
    private static byte[] IndexJson(string id, string version, string sha) => Encoding.UTF8.GetBytes($$"""
        { "schemaVersion": 1, "plugins": [
          { "id": "{{id}}", "displayName": "Nexus Mods", "version": "{{version}}", "minBinaryVersion": "0.7.0",
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

    [Fact]
    public async Task A_staging_failure_on_update_does_not_clobber_the_working_install()
    {
        // C2: a pre-existing valid dll+sig pair must survive a failed UPDATE. We force the dll STAGING
        // write to throw by pre-creating a DIRECTORY at the dll temp path (File.WriteAllBytes to an
        // existing-directory path throws on every platform), then assert the live pair is byte-identical.
        using var signer = new Signer();
        var dir = TempDir();
        try
        {
            var dllPath = Path.Combine(dir, "nexus.dll");
            var sigPath = dllPath + ".sig";
            var oldDll = Encoding.ASCII.GetBytes("OLD-WORKING-DLL");
            var oldSig = Encoding.ASCII.GetBytes("OLD-WORKING-SIG");
            File.WriteAllBytes(dllPath, oldDll);
            File.WriteAllBytes(sigPath, oldSig);

            // Block the dll staging write: a directory where the temp file wants to be.
            var dllTmp = dllPath + ".tmp-" + Environment.ProcessId;
            Directory.CreateDirectory(dllTmp);

            // The feed offers a NEW version (1.1.0) over a recorded 1.0.0 — this is an update.
            var newDll = Encoding.ASCII.GetBytes("NEW-DLL-BYTES");
            var index = IndexJson("nexus", "1.1.0", PluginIntegrity.Sha256Hex(newDll));
            var map = new Dictionary<string, byte[]>
            {
                ["https://x/plugins.json"] = index,
                ["https://x/plugins.json.sig"] = signer.Sign(index),
                ["https://x/nexus.dll"] = newDll,
                ["https://x/nexus.dll.sig"] = signer.Sign(newDll),
            };
            var record = Path.Combine(dir, "installed.json");
            InstalledPluginsStore.Write(record, new Dictionary<string, string> { ["nexus"] = "1.0.0" });
            var req = new PluginFeedRequest("https://x/plugins.json", signer.PublicKey,
                new Version(0, 7, 0), dir, record);

            var installed = await PluginFeedInstaller.RunAsync(req, Serve(map));

            // Update was refused; the working pair is untouched byte-for-byte.
            Assert.Empty(installed);
            Assert.Equal(oldDll, File.ReadAllBytes(dllPath));
            Assert.Equal(oldSig, File.ReadAllBytes(sigPath));
            // The record still reflects the old version (nothing was installed).
            Assert.Equal("1.0.0", InstalledPluginsStore.Read(record)["nexus"]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task A_clean_update_writes_both_files_as_the_new_bytes()
    {
        // C2 complement: a successful UPDATE lands a MATCHED pair — never new-dll + old-sig.
        using var signer = new Signer();
        var dir = TempDir();
        try
        {
            var dllPath = Path.Combine(dir, "nexus.dll");
            var sigPath = dllPath + ".sig";
            File.WriteAllBytes(dllPath, Encoding.ASCII.GetBytes("OLD-WORKING-DLL"));
            File.WriteAllBytes(sigPath, Encoding.ASCII.GetBytes("OLD-WORKING-SIG"));

            var newDll = Encoding.ASCII.GetBytes("NEW-DLL-BYTES");
            var index = IndexJson("nexus", "1.1.0", PluginIntegrity.Sha256Hex(newDll));
            var newSig = signer.Sign(newDll);
            var map = new Dictionary<string, byte[]>
            {
                ["https://x/plugins.json"] = index,
                ["https://x/plugins.json.sig"] = signer.Sign(index),
                ["https://x/nexus.dll"] = newDll,
                ["https://x/nexus.dll.sig"] = newSig,
            };
            var record = Path.Combine(dir, "installed.json");
            InstalledPluginsStore.Write(record, new Dictionary<string, string> { ["nexus"] = "1.0.0" });
            var req = new PluginFeedRequest("https://x/plugins.json", signer.PublicKey,
                new Version(0, 7, 0), dir, record);

            var installed = await PluginFeedInstaller.RunAsync(req, Serve(map));

            Assert.Single(installed);
            Assert.Equal(newDll, File.ReadAllBytes(dllPath));
            Assert.Equal(newSig, File.ReadAllBytes(sigPath));   // matched sig, not the stale one
            Assert.Equal("1.1.0", InstalledPluginsStore.Read(record)["nexus"]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task An_entry_id_that_escapes_the_plugins_dir_is_skipped_and_writes_nothing()
    {
        // C3: id "../escape" is correctly signed, but must never compose a path outside PluginsDir.
        using var signer = new Signer();
        var dir = TempDir();
        try
        {
            var dll = Encoding.ASCII.GetBytes("FAKE-DLL-BYTES");
            var index = IndexJson("../escape", "1.0.0", PluginIntegrity.Sha256Hex(dll));
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
            // Nothing landed anywhere — not in PluginsDir, not in its parent.
            var parent = Directory.GetParent(dir)!.FullName;
            Assert.False(File.Exists(Path.Combine(parent, "escape.dll")));
            Assert.False(File.Exists(Path.Combine(parent, "escape.dll.sig")));
            Assert.Empty(Directory.GetFiles(dir, "*.dll"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
