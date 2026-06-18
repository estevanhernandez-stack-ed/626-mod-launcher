# 5c-consumer — Plugin feed fetch/verify/install (launcher side) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The FULL launcher fetches the signed `plugins.json` index from GitHub, verifies it against the pinned plugin key, gates it on `minBinaryVersion`, downloads + verifies + atomically installs the signed plugin into `PluginHost.PluginsDir`, records the installed version, and hot-loads it — fetching on Nexus connect and on a 24h debounce.

**Architecture:** The heavy logic is **pure Core** and fully unit-tested against a hand-signed fixture (a test keypair): index parse (`PluginIndex`), the version/schema gate (`PluginGate`), integrity (`PluginIntegrity`), the installed-version record (`InstalledPluginsStore`), and the install orchestration (`PluginFeedInstaller`, which takes an injected download delegate so it needs no `HttpClient` type and no WinUI). Only the thin glue is App-side `#if FULL`: build the download delegate from the shared `HttpClient`, debounce, gate on the setting, call the Core installer, then hot-load via a new `PluginHost.LoadOne`. The producer repo need not exist — everything is tested with locally test-signed bytes.

**Tech Stack:** .NET 10, C#, xUnit. Reuses `ManifestSignature`/`PluginSignature` (ECDSA P-256), `AtomicJson` (atomic camelCase writes), `AppSettingsService` (toggle), the `RemoteManifestSource` fetch/debounce pattern.

## Global Constraints

- **Pure-Core:** no WinUI/WinRT/`Microsoft.UI`/`Windows.*` in `src/ModManager.Core/`. `CorePurityTests` enforces it. The Core installer takes an injected `Func<...>` download delegate — never an `HttpClient`-typed parameter is required (BCL `Func` is fine).
- **App fetcher is `#if FULL`:** the `HttpClient` wiring + the `PluginHost.LoadOne` hot-load + the connect-trigger glue compile out of the STORE build, exactly like `PluginHost`.
- **camelCase JSON on disk:** every persisted shape sets `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` and ships a round-trip test asserting the camelCase key literally appears. The installed-version record uses `AtomicJson` (already camelCase).
- **Fail-silent:** offline / bad index sig / bad dll sig / sha mismatch / unknown `schemaVersion` / `minBinaryVersion` too high → nothing installed, the install method returns an empty/partial result, the App glue logs via `AppDiagnostics`, the app continues. A bad feed never breaks a working install (atomic verify-before-replace).
- **Never bare `dotnet` at the repo root.** Core: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. App build: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (kill `ModManager.App` first). STORE adds `-p:Configuration=Store`.
- **TDD:** every behavior change starts with a failing xUnit test. Conventional commits + trailer `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- **Out of scope (deferred to 5c-producer):** the `626-mod-plugins` repo contents, Abstractions-as-NuGet, moving the plugin source, the signing CI. This plan ships dark — the feed URL points at the (currently empty) repo's latest release; until the producer publishes, the fetch 404s and fail-silent leaves the app exactly as today.

---

## File structure

| File | Responsibility |
|---|---|
| `src/ModManager.Core/Plugins/PluginIndex.cs` | the `plugins.json` model (`PluginIndex`, `PluginIndexEntry`) + tolerant `TryParse` + `KnownSchemaVersion` |
| `src/ModManager.Core/Plugins/PluginGate.cs` | `SelectInstallable` — schema + `minBinaryVersion` + not-already-installed gate (pure) |
| `src/ModManager.Core/Plugins/PluginIntegrity.cs` | `Sha256Hex` / `Sha256Matches` (pure) |
| `src/ModManager.Core/Plugins/InstalledPluginsStore.cs` | read/write the installed-version record (camelCase, atomic) |
| `src/ModManager.Core/Plugins/PluginFeedInstaller.cs` | the orchestration: download-delegate → verify index → parse → gate → download dll+sig → verify sha+sig → atomic install → update record. Returns what installed. |
| `src/ModManager.App/Services/PluginHost.cs` (modify) | extract `LoadOne(dllPath, …)` from `LoadAll`; expose it for hot-load |
| `src/ModManager.App/Services/PluginFeedSource.cs` (new, `#if FULL`) | HTTP delegate + debounce stamp + `KeepPluginsUpdated` gate + connect-trigger + hot-load glue |
| `src/ModManager.App/Services/AppSettingsService.cs` (modify) | add `KeepPluginsUpdated` (default on) |
| `src/ModManager.App/Views/…` Settings (modify) | status line + "keep plugins updated" toggle |
| `tests/ModManager.Tests/Plugins/*` | Core tests for Tasks 1–5 + the setting round-trip |

---

## Task 1: Core — `PluginIndex` model + `TryParse`

**Files:**
- Create: `src/ModManager.Core/Plugins/PluginIndex.cs`
- Test: `tests/ModManager.Tests/Plugins/PluginIndexTests.cs`

**Interfaces:**
- Produces:
  - `sealed record PluginIndexEntry(string Id, string DisplayName, string Version, string MinBinaryVersion, string DownloadUrl, string SigUrl, string Sha256)`
  - `sealed record PluginIndex(int SchemaVersion, IReadOnlyList<PluginIndexEntry> Plugins)` with `const int KnownSchemaVersion = 1` and `static bool TryParse(ReadOnlySpan<byte> utf8Json, out PluginIndex? index)`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Plugins/PluginIndexTests.cs
using System.Text;
using ModManager.Core.Plugins;

namespace ModManager.Tests.Plugins;

public class PluginIndexTests
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void TryParse_reads_a_camelCase_index()
    {
        var json = """
        { "schemaVersion": 1, "plugins": [
          { "id": "nexus", "displayName": "Nexus Mods", "version": "1.0.0",
            "minBinaryVersion": "0.7.0",
            "downloadUrl": "https://x/nexus.dll", "sigUrl": "https://x/nexus.dll.sig",
            "sha256": "abc123" } ] }
        """;
        Assert.True(PluginIndex.TryParse(Utf8(json), out var index));
        Assert.Equal(1, index!.SchemaVersion);
        var e = Assert.Single(index.Plugins);
        Assert.Equal("nexus", e.Id);
        Assert.Equal("Nexus Mods", e.DisplayName);
        Assert.Equal("1.0.0", e.Version);
        Assert.Equal("0.7.0", e.MinBinaryVersion);
        Assert.Equal("https://x/nexus.dll", e.DownloadUrl);
        Assert.Equal("https://x/nexus.dll.sig", e.SigUrl);
        Assert.Equal("abc123", e.Sha256);
    }

    [Fact]
    public void TryParse_returns_false_on_garbage()
    {
        Assert.False(PluginIndex.TryParse(Utf8("not json"), out var index));
        Assert.Null(index);
    }

    [Fact]
    public void TryParse_returns_false_on_empty_input()
    {
        Assert.False(PluginIndex.TryParse(Array.Empty<byte>(), out _));
    }
}
```

- [ ] **Step 2: Run it, verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter PluginIndexTests`
Expected: FAIL — `PluginIndex` does not exist.

- [ ] **Step 3: Implement**

```csharp
// src/ModManager.Core/Plugins/PluginIndex.cs
using System.Text.Json;

namespace ModManager.Core.Plugins;

/// <summary>One plugin row in the signed feed (mirrors the producer's plugins.json). All strings —
/// versions are compared via <see cref="PluginGate"/>, the sha is hex.</summary>
public sealed record PluginIndexEntry(
    string Id, string DisplayName, string Version, string MinBinaryVersion,
    string DownloadUrl, string SigUrl, string Sha256);

/// <summary>The signed plugin feed index. <see cref="TryParse"/> is tolerant — any malformed input
/// yields false + null, never throws (the App treats that as "no feed", fail-silent).</summary>
public sealed record PluginIndex(int SchemaVersion, IReadOnlyList<PluginIndexEntry> Plugins)
{
    /// <summary>The newest schema this binary understands. A feed declaring a higher version is
    /// rejected wholesale by <see cref="PluginGate"/> (forward-compat: a newer feed never breaks us).</summary>
    public const int KnownSchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static bool TryParse(ReadOnlySpan<byte> utf8Json, out PluginIndex? index)
    {
        index = null;
        if (utf8Json.IsEmpty) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<PluginIndex>(utf8Json, Json);
            if (parsed is null) return false;
            // Normalize a null plugins array to empty so callers never null-check it.
            index = parsed.Plugins is null ? parsed with { Plugins = Array.Empty<PluginIndexEntry>() } : parsed;
            return true;
        }
        catch (JsonException) { return false; }
    }
}
```

- [ ] **Step 4: Run it, verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter PluginIndexTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Plugins/PluginIndex.cs tests/ModManager.Tests/Plugins/PluginIndexTests.cs
git commit -m "feat(plugins): PluginIndex model + tolerant TryParse for the signed feed"
```

---

## Task 2: Core — `PluginGate.SelectInstallable`

**Files:**
- Create: `src/ModManager.Core/Plugins/PluginGate.cs`
- Test: `tests/ModManager.Tests/Plugins/PluginGateTests.cs`

**Interfaces:**
- Consumes: `PluginIndex`, `PluginIndexEntry`, `PluginIndex.KnownSchemaVersion`
- Produces: `static class PluginGate` with `static IReadOnlyList<PluginIndexEntry> SelectInstallable(PluginIndex index, Version binaryVersion, IReadOnlyDictionary<string, string> installedVersions)`

Rules: if `index.SchemaVersion > KnownSchemaVersion` → empty (reject the whole feed). Per entry, include iff `MinBinaryVersion` parses as a `Version` AND `≤ binaryVersion` AND the entry isn't already installed at `entry.Version`. An unparseable `MinBinaryVersion` skips the entry (safe).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Plugins/PluginGateTests.cs
using ModManager.Core.Plugins;

namespace ModManager.Tests.Plugins;

public class PluginGateTests
{
    private static PluginIndexEntry Entry(string id, string version, string minBin) =>
        new(id, id, version, minBin, $"https://x/{id}.dll", $"https://x/{id}.dll.sig", "hash");

    private static PluginIndex Index(int schema, params PluginIndexEntry[] entries) => new(schema, entries);

    private static readonly Dictionary<string, string> None = new();

    [Fact]
    public void Eligible_entry_is_selected()
    {
        var got = PluginGate.SelectInstallable(
            Index(1, Entry("nexus", "1.0.0", "0.7.0")), new Version(0, 7, 0), None);
        Assert.Equal("nexus", Assert.Single(got).Id);
    }

    [Fact]
    public void Unknown_schema_rejects_the_whole_feed()
    {
        var got = PluginGate.SelectInstallable(
            Index(99, Entry("nexus", "1.0.0", "0.0.0")), new Version(9, 9, 9), None);
        Assert.Empty(got);
    }

    [Fact]
    public void Entry_needing_a_newer_binary_is_skipped()
    {
        var got = PluginGate.SelectInstallable(
            Index(1, Entry("nexus", "2.0.0", "0.9.0")), new Version(0, 7, 0), None);
        Assert.Empty(got);
    }

    [Fact]
    public void Already_installed_at_same_version_is_skipped()
    {
        var got = PluginGate.SelectInstallable(
            Index(1, Entry("nexus", "1.0.0", "0.7.0")), new Version(0, 7, 0),
            new Dictionary<string, string> { ["nexus"] = "1.0.0" });
        Assert.Empty(got);
    }

    [Fact]
    public void Installed_at_older_version_is_selected_for_update()
    {
        var got = PluginGate.SelectInstallable(
            Index(1, Entry("nexus", "1.1.0", "0.7.0")), new Version(0, 7, 0),
            new Dictionary<string, string> { ["nexus"] = "1.0.0" });
        Assert.Equal("nexus", Assert.Single(got).Id);
    }

    [Fact]
    public void Unparseable_min_binary_version_skips_the_entry()
    {
        var got = PluginGate.SelectInstallable(
            Index(1, Entry("nexus", "1.0.0", "not-a-version")), new Version(9, 9, 9), None);
        Assert.Empty(got);
    }
}
```

- [ ] **Step 2: Run it, verify it fails** — `--filter PluginGateTests` → FAIL (`PluginGate` missing).

- [ ] **Step 3: Implement**

```csharp
// src/ModManager.Core/Plugins/PluginGate.cs
namespace ModManager.Core.Plugins;

/// <summary>Decides which feed entries this binary should install: the schema must be known, the
/// entry's minimum binary version must be satisfied, and it must not already be installed at the
/// listed version. Pure — no I/O. An unknown schema rejects the whole feed (forward-compat).</summary>
public static class PluginGate
{
    public static IReadOnlyList<PluginIndexEntry> SelectInstallable(
        PluginIndex index, Version binaryVersion, IReadOnlyDictionary<string, string> installedVersions)
    {
        if (index.SchemaVersion > PluginIndex.KnownSchemaVersion) return Array.Empty<PluginIndexEntry>();

        var result = new List<PluginIndexEntry>();
        foreach (var e in index.Plugins)
        {
            if (!Version.TryParse(e.MinBinaryVersion, out var min)) continue; // unparseable → skip (safe)
            if (binaryVersion < min) continue;                                // needs a newer binary
            if (installedVersions.TryGetValue(e.Id, out var have) && have == e.Version) continue; // already current
            result.Add(e);
        }
        return result;
    }
}
```

- [ ] **Step 4: Run it, verify it passes** — `--filter PluginGateTests` → PASS (6).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Plugins/PluginGate.cs tests/ModManager.Tests/Plugins/PluginGateTests.cs
git commit -m "feat(plugins): PluginGate — schema + minBinaryVersion + not-already-installed gate"
```

---

## Task 3: Core — `PluginIntegrity` (sha256)

**Files:**
- Create: `src/ModManager.Core/Plugins/PluginIntegrity.cs`
- Test: `tests/ModManager.Tests/Plugins/PluginIntegrityTests.cs`

**Interfaces:**
- Produces: `static class PluginIntegrity` with `static string Sha256Hex(ReadOnlySpan<byte> bytes)` (lowercase hex) and `static bool Sha256Matches(ReadOnlySpan<byte> bytes, string expectedHex)` (case-insensitive).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Plugins/PluginIntegrityTests.cs
using System.Text;
using ModManager.Core.Plugins;

namespace ModManager.Tests.Plugins;

public class PluginIntegrityTests
{
    // Known vector: SHA-256("abc") = ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
    private static readonly byte[] Abc = Encoding.ASCII.GetBytes("abc");
    private const string AbcHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public void Sha256Hex_matches_the_known_vector_in_lowercase()
        => Assert.Equal(AbcHash, PluginIntegrity.Sha256Hex(Abc));

    [Fact]
    public void Sha256Matches_is_case_insensitive()
    {
        Assert.True(PluginIntegrity.Sha256Matches(Abc, AbcHash));
        Assert.True(PluginIntegrity.Sha256Matches(Abc, AbcHash.ToUpperInvariant()));
    }

    [Fact]
    public void Sha256Matches_rejects_a_wrong_hash()
        => Assert.False(PluginIntegrity.Sha256Matches(Abc, new string('0', 64)));
}
```

- [ ] **Step 2: Run it, verify it fails** — `--filter PluginIntegrityTests` → FAIL.

- [ ] **Step 3: Implement**

```csharp
// src/ModManager.Core/Plugins/PluginIntegrity.cs
using System.Security.Cryptography;

namespace ModManager.Core.Plugins;

/// <summary>Content integrity for downloaded plugin bytes — the sha256 the signed index pins. A
/// mismatch means the download was corrupted or swapped; the installer refuses it.</summary>
public static class PluginIntegrity
{
    public static string Sha256Hex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static bool Sha256Matches(ReadOnlySpan<byte> bytes, string expectedHex)
        => !string.IsNullOrWhiteSpace(expectedHex)
           && string.Equals(Sha256Hex(bytes), expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run it, verify it passes** — `--filter PluginIntegrityTests` → PASS (3).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Plugins/PluginIntegrity.cs tests/ModManager.Tests/Plugins/PluginIntegrityTests.cs
git commit -m "feat(plugins): PluginIntegrity sha256 verify for downloaded plugin bytes"
```

---

## Task 4: Core — `InstalledPluginsStore` (camelCase record)

**Files:**
- Create: `src/ModManager.Core/Plugins/InstalledPluginsStore.cs`
- Test: `tests/ModManager.Tests/Plugins/InstalledPluginsStoreTests.cs`

**Interfaces:**
- Produces: `static class InstalledPluginsStore` with `static IReadOnlyDictionary<string,string> Read(string path)` (id→version; missing/corrupt → empty) and `static void Write(string path, IReadOnlyDictionary<string,string> versions)` (atomic, camelCase — the on-disk shape is `{ "versions": { "nexus": "1.0.0" } }`).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Plugins/InstalledPluginsStoreTests.cs
using System.IO;
using ModManager.Core.Plugins;

namespace ModManager.Tests.Plugins;

public class InstalledPluginsStoreTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "mm-installed-plugins-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Round_trips_versions_and_writes_camelCase()
    {
        var path = TempFile();
        try
        {
            InstalledPluginsStore.Write(path, new Dictionary<string, string> { ["nexus"] = "1.0.0" });

            var json = File.ReadAllText(path);
            Assert.Contains("\"versions\"", json);      // camelCase key on disk
            Assert.DoesNotContain("\"Versions\"", json);

            var read = InstalledPluginsStore.Read(path);
            Assert.Equal("1.0.0", read["nexus"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_of_a_missing_file_is_empty()
        => Assert.Empty(InstalledPluginsStore.Read(TempFile()));

    [Fact]
    public void Read_of_a_corrupt_file_is_empty()
    {
        var path = TempFile();
        try { File.WriteAllText(path, "{ not json"); Assert.Empty(InstalledPluginsStore.Read(path)); }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run it, verify it fails** — `--filter InstalledPluginsStoreTests` → FAIL.

- [ ] **Step 3: Implement**

```csharp
// src/ModManager.Core/Plugins/InstalledPluginsStore.cs
using System.IO;
using System.Text.Json;

namespace ModManager.Core.Plugins;

/// <summary>The on-disk record of which plugin is installed at which version (lives next to the
/// plugin dlls). Drives the "already current" gate and the "is anything installed" decision. camelCase
/// + atomic (via <see cref="AtomicJson"/>); a missing or corrupt file reads as "nothing installed".</summary>
public static class InstalledPluginsStore
{
    private sealed class File_ { public Dictionary<string, string> Versions { get; set; } = new(); }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Dictionary<string, string>();
            var file = JsonSerializer.Deserialize<File_>(File.ReadAllText(path), Json);
            return file?.Versions ?? new Dictionary<string, string>();
        }
        catch { return new Dictionary<string, string>(); }
    }

    public static void Write(string path, IReadOnlyDictionary<string, string> versions)
        => AtomicJson.WriteJsonAtomic(path, new File_ { Versions = new Dictionary<string, string>(versions) });
}
```

- [ ] **Step 4: Run it, verify it passes** — `--filter InstalledPluginsStoreTests` → PASS (3).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Plugins/InstalledPluginsStore.cs tests/ModManager.Tests/Plugins/InstalledPluginsStoreTests.cs
git commit -m "feat(plugins): InstalledPluginsStore — camelCase installed-version record"
```

---

## Task 5: Core — `PluginFeedInstaller` (the orchestration)

**Files:**
- Create: `src/ModManager.Core/Plugins/PluginFeedInstaller.cs`
- Test: `tests/ModManager.Tests/Plugins/PluginFeedInstallerTests.cs`

**Interfaces:**
- Consumes: `PluginIndex.TryParse`, `PluginGate.SelectInstallable`, `PluginIntegrity.Sha256Matches`, `PluginSignature.VerifyWithKey`, `InstalledPluginsStore`.
- Produces:
  - `sealed record InstalledPlugin(string Id, string Version, string DllPath)`
  - `delegate Task<byte[]?> PluginDownload(string url, CancellationToken ct)` — returns the bytes, or null on any fetch failure (the App wraps `HttpClient` + swallows).
  - `sealed record PluginFeedRequest(string IndexUrl, byte[] VerifyKey, Version BinaryVersion, string PluginsDir, string InstalledRecordPath)`
  - `static class PluginFeedInstaller` with `static Task<IReadOnlyList<InstalledPlugin>> RunAsync(PluginFeedRequest req, PluginDownload download, CancellationToken ct = default)`

Flow (every failure → skip that step, continue or return what's done — never throw): download `IndexUrl` + `IndexUrl + ".sig"` → `PluginSignature.VerifyWithKey(req.VerifyKey, index, sig)` (false → return empty) → `PluginIndex.TryParse` (false → empty) → `PluginGate.SelectInstallable(index, BinaryVersion, InstalledPluginsStore.Read(InstalledRecordPath))` → for each entry: download dll + `dll.sig` → `Sha256Matches(dll, entry.Sha256)` AND `VerifyWithKey(req.VerifyKey, dll, dllSig)` (either false → skip) → atomic-write `dll` + `dll.sig` into `PluginsDir` as `<id>.dll` / `<id>.dll.sig` → record `id→version`. Return the installed list.

- [ ] **Step 1: Write the failing test** (hand-signed fixture with a test keypair)

```csharp
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
```

- [ ] **Step 2: Run it, verify it fails** — `--filter PluginFeedInstallerTests` → FAIL (`PluginFeedInstaller` missing).

- [ ] **Step 3: Implement**

```csharp
// src/ModManager.Core/Plugins/PluginFeedInstaller.cs
using System.IO;

namespace ModManager.Core.Plugins;

/// <summary>A plugin the installer placed on disk (ready for the host to load).</summary>
public sealed record InstalledPlugin(string Id, string Version, string DllPath);

/// <summary>Inputs for one feed run. <c>VerifyKey</c> is the pinned plugin SPKI in production (the App
/// passes <c>PluginSigningKey.PublicKeySpki</c>); tests inject a throwaway public key.</summary>
public sealed record PluginFeedRequest(
    string IndexUrl, byte[] VerifyKey, Version BinaryVersion, string PluginsDir, string InstalledRecordPath);

/// <summary>
/// The headless fetch→verify→gate→download→verify→install pipeline. Takes an injected
/// <see cref="PluginDownload"/> so Core never references <c>HttpClient</c> and the whole thing is
/// unit-testable with in-memory bytes. Never throws — any failure (offline, bad index sig, bad schema,
/// too-high min version, sha mismatch, bad dll sig) skips that unit and the method returns whatever
/// installed cleanly. The pinned dll signature is re-verified by the host at load (defense in depth),
/// but we verify here too so a bad download is never written to disk.
/// </summary>
public static class PluginFeedInstaller
{
    /// <summary>Fetch the bytes at <paramref name="url"/>, or null on any failure (the App wraps an
    /// <c>HttpClient</c> and swallows network errors into null).</summary>
    public delegate Task<byte[]?> PluginDownload(string url, CancellationToken ct);

    public static async Task<IReadOnlyList<InstalledPlugin>> RunAsync(
        PluginFeedRequest req, PluginDownload download, CancellationToken ct = default)
    {
        var installed = new List<InstalledPlugin>();

        var indexBytes = await download(req.IndexUrl, ct).ConfigureAwait(false);
        var indexSig = await download(req.IndexUrl + ".sig", ct).ConfigureAwait(false);
        if (indexBytes is null || indexSig is null) return installed;
        if (!PluginSignature.VerifyWithKey(req.VerifyKey, indexBytes, indexSig)) return installed;
        if (!PluginIndex.TryParse(indexBytes, out var index)) return installed;

        var have = InstalledPluginsStore.Read(req.InstalledRecordPath);
        var toInstall = PluginGate.SelectInstallable(index!, req.BinaryVersion, have);
        if (toInstall.Count == 0) return installed;

        // Mutable copy of the record so multiple installs in one run all persist.
        var record = new Dictionary<string, string>(have);

        foreach (var e in toInstall)
        {
            var dll = await download(e.DownloadUrl, ct).ConfigureAwait(false);
            var dllSig = await download(e.SigUrl, ct).ConfigureAwait(false);
            if (dll is null || dllSig is null) continue;
            if (!PluginIntegrity.Sha256Matches(dll, e.Sha256)) continue;
            if (!PluginSignature.VerifyWithKey(req.VerifyKey, dll, dllSig)) continue;

            string dllPath = Path.Combine(req.PluginsDir, e.Id + ".dll");
            string sigPath = dllPath + ".sig";
            try
            {
                Directory.CreateDirectory(req.PluginsDir);
                AtomicWriteBytes(dllPath, dll);     // verify-before-replace: only verified bytes land
                AtomicWriteBytes(sigPath, dllSig);
            }
            catch { continue; } // a write failure leaves any prior install intact

            record[e.Id] = e.Version;
            installed.Add(new InstalledPlugin(e.Id, e.Version, dllPath));
        }

        if (installed.Count > 0)
        {
            try { InstalledPluginsStore.Write(req.InstalledRecordPath, record); } catch { /* best-effort */ }
        }
        return installed;
    }

    // Atomic byte write: temp sibling + rename (mirrors AtomicJson for non-JSON payloads).
    private static void AtomicWriteBytes(string path, byte[] bytes)
    {
        var tmp = path + ".tmp-" + Environment.ProcessId;
        try { File.WriteAllBytes(tmp, bytes); File.Move(tmp, path, overwrite: true); }
        catch { try { File.Delete(tmp); } catch { } throw; }
    }
}
```

- [ ] **Step 4: Run it, verify it passes** — `--filter PluginFeedInstallerTests` → PASS (5).

- [ ] **Step 5: Run CorePurity + the whole Plugins folder, then commit**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~Plugins|CorePurity"`
Expected: PASS (Core stays pure — the installer references only BCL + Core).

```bash
git add src/ModManager.Core/Plugins/PluginFeedInstaller.cs tests/ModManager.Tests/Plugins/PluginFeedInstallerTests.cs
git commit -m "feat(plugins): PluginFeedInstaller — headless fetch/verify/gate/install pipeline"
```

---

## Task 6: App — `PluginHost.LoadOne` hot-load seam

**Files:**
- Modify: `src/ModManager.App/Services/PluginHost.cs`

**Interfaces:**
- Produces: `public static bool LoadOne(string dllPath, ModSourceRegistry registry, Func<string, string?> getCredential, HttpClient httpClient)` — verifies the sibling `.sig` against the pinned key, loads via the existing `LoadVerified`, returns true iff a plugin was loaded. `LoadAll` is refactored to call `LoadOne` per dll (no behavior change).

- [ ] **Step 1: Refactor `LoadAll` to delegate to a new `LoadOne`** (inside the existing `#if FULL`):

```csharp
    public static void LoadAll(ModSourceRegistry registry, Func<string, string?> getCredential, HttpClient httpClient)
    {
        if (!Directory.Exists(PluginsDir)) return;
        foreach (var dll in Directory.EnumerateFiles(PluginsDir, "*.dll"))
            LoadOne(dll, registry, getCredential, httpClient);
    }

    /// <summary>Verify + load a single plugin dll (the just-downloaded hot-load path and the per-file
    /// step of <see cref="LoadAll"/>). Returns true iff a plugin assembly was loaded and registered.
    /// Fail-closed + never throws: a missing/bad signature or a load error logs and returns false.</summary>
    public static bool LoadOne(string dllPath, ModSourceRegistry registry, Func<string, string?> getCredential, HttpClient httpClient)
    {
        try
        {
            var sig = dllPath + ".sig";
            if (!File.Exists(sig)) return false;
            var assemblyBytes = File.ReadAllBytes(dllPath);
            var signatureBytes = File.ReadAllBytes(sig);
            if (!PluginSignature.Verify(assemblyBytes, signatureBytes)) return false;
            LoadVerified(assemblyBytes, registry, getCredential, httpClient);
            return true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("plugin-host", ex);
            return false;
        }
    }
```

- [ ] **Step 2: Build the App (FULL), verify it compiles**

Run (kill the app first): `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: 0 errors. (`LoadOne` is exercised end-to-end by the live smoke + indirectly by every existing `LoadAll` path; the ALC load isn't unit-tested — the verify it depends on is covered by `PluginSignatureTests`.)

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.App/Services/PluginHost.cs
git commit -m "refactor(plugins): extract PluginHost.LoadOne for hot-load (no behavior change)"
```

---

## Task 7: App — `PluginFeedSource` (`#if FULL`): HTTP delegate + debounce + trigger + hot-load

**Files:**
- Create: `src/ModManager.App/Services/PluginFeedSource.cs` (`#if FULL`)
- Test: `tests/ModManager.Tests/Plugins/PluginFeedDebounceTests.cs` (the debounce predicate is a pure static, testable in the Core test project — it lives in Core; see Step 1)

**Interfaces:**
- Consumes: `PluginFeedInstaller.RunAsync`, `PluginFeedRequest`, `PluginSigningKey.PublicKeySpki`, `PluginHost.PluginsDir`/`LoadOne`, `ModSourceRegistry`, `AppSettingsService.KeepPluginsUpdated` (Task 8), `InstalledPluginsStore.Read`.
- Produces: `PluginFeedSource` (App) with `Task MaybeFetchOnConnectAsync()`; and a pure Core helper `PluginPollStamp.ShouldPoll(DateTime? last, DateTime now, TimeSpan window)` reused for the 24h debounce.

The debounce decision is pure → put it in Core so it's unit-tested; the App calls it. Note: `NexusPollStamp.ShouldPoll` already exists with this exact shape — reuse it rather than adding a duplicate.

- [ ] **Step 1: Confirm/áreuse the pure debounce predicate (Core)** — `NexusPollStamp.ShouldPoll(DateTime? last, DateTime now, TimeSpan window)` already exists and is tested. If a `Plugins`-namespaced wrapper is wanted, skip it — reuse `NexusPollStamp.ShouldPoll` directly. (No new Core code; no failing test needed here — the predicate is already covered.)

- [ ] **Step 2: Implement `PluginFeedSource` (App, `#if FULL`)**

```csharp
// src/ModManager.App/Services/PluginFeedSource.cs
#if FULL
using System.IO;
using System.Net.Http;
using System.Reflection;
using ModManager.Core;
using ModManager.Core.Plugins;

namespace ModManager.App.Services;

/// <summary>
/// Drives the off-Store plugin feed on the FULL flavor. On Nexus connect (or a 24h-debounced re-check)
/// it fetches the signed plugins.json from the 626-mod-plugins repo, verifies + gates + installs via the
/// headless <see cref="PluginFeedInstaller"/>, then hot-loads anything new through <see cref="PluginHost.LoadOne"/>
/// so Nexus works without a restart. Gated on the "keep plugins updated" setting (for re-checks); the
/// first install (no plugin yet) bypasses the debounce. Fail-silent: every failure is swallowed + logged;
/// a bad feed never disturbs an installed, working plugin. Absent entirely from the STORE build (#if FULL).
/// </summary>
public sealed class PluginFeedSource
{
    // Stable "latest release" asset URL on the plugins repo (see the 5c spec).
    private const string FeedUrl =
        "https://github.com/estevanhernandez-stack-ed/626-mod-plugins/releases/latest/download/plugins.json";

    private static readonly TimeSpan DebounceWindow = TimeSpan.FromHours(24);

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModManagerBuilder");
    private static readonly string StampPath = Path.Combine(DataDir, "last-plugin-check.txt");
    private static string RecordPath => Path.Combine(PluginHost.PluginsDir, "installed-plugins.json");

    private static Version AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private readonly HttpClient _http;
    private readonly ModSourceRegistry _registry;
    private readonly Func<string, string?> _getCredential;
    private readonly AppSettingsService _settings;

    public PluginFeedSource(HttpClient http, ModSourceRegistry registry,
        Func<string, string?> getCredential, AppSettingsService settings)
    {
        _http = http; _registry = registry; _getCredential = getCredential; _settings = settings;
    }

    /// <summary>Called after a successful Nexus connect. Installs the plugin immediately if none is
    /// installed yet (bypassing the debounce — the user wants Nexus now); otherwise it's a debounced
    /// update check gated on the "keep plugins updated" setting. Never throws.</summary>
    public async Task MaybeFetchOnConnectAsync()
    {
        try
        {
            bool anyInstalled = InstalledPluginsStore.Read(RecordPath).Count > 0;

            if (anyInstalled)
            {
                if (!_settings.KeepPluginsUpdated) return;                // re-checks are opt-out-able
                var last = ReadStamp();
                if (!NexusPollStamp.ShouldPoll(last, DateTime.UtcNow, DebounceWindow)) return;
            }
            // else: first install — fetch now regardless of stamp/toggle (they connected to use Nexus).

            var req = new PluginFeedRequest(FeedUrl, PluginSigningKey.PublicKeySpki.ToArray(),
                AppVersion, PluginHost.PluginsDir, RecordPath);

            var installed = await PluginFeedInstaller.RunAsync(req, Download).ConfigureAwait(false);

            foreach (var p in installed)
                PluginHost.LoadOne(p.DllPath, _registry, _getCredential, _http);  // hot-load — Nexus live now
        }
        catch (Exception ex) { AppDiagnostics.Log("plugin-feed", ex); }
        finally { WriteStamp(); }
    }

    private async Task<byte[]?> Download(string url, CancellationToken ct)
    {
        try { return await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false); }
        catch { return null; }  // offline / 404 (feed not published yet) → null, the installer treats as skip
    }

    private static DateTime? ReadStamp()
    {
        try
        {
            if (!File.Exists(StampPath)) return null;
            return DateTime.TryParse(File.ReadAllText(StampPath).Trim(), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : null;
        }
        catch { return null; }
    }

    private static void WriteStamp()
    {
        try { Directory.CreateDirectory(DataDir); File.WriteAllText(StampPath, DateTime.UtcNow.ToString("O")); }
        catch { /* best-effort */ }
    }
}
#endif
```

- [ ] **Step 3: Wire the trigger into the Nexus connect path (`#if FULL`)**

In the App's DI host, register `PluginFeedSource` as a singleton (alongside `NexusService`/`ModSourceRegistry`). At the call site that handles a successful Nexus connect (the toolbar "Connect Nexus" command in the shell view-model — search for `ConnectAsync(`), after a non-null result, fire-and-forget the feed (guarded by `#if FULL`):

```csharp
#if FULL
        _ = _pluginFeedSource.MaybeFetchOnConnectAsync(); // off-Store: pull/refresh the Nexus plugin
#endif
```

(STORE has no `PluginFeedSource` registration and the call compiles out, so connect there just stores the key with no plugin surface — unchanged.)

- [ ] **Step 4: Build both flavors**

Run (kill the app first):
`dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
`dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Configuration=Store`
Expected: both 0 errors. (The orchestration is covered by Task 5's tests over the same `RunAsync`; this glue is build-verified + live-smoked.)

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/Services/PluginFeedSource.cs src/ModManager.App/<host + connect-call-site files>
git commit -m "feat(plugins): PluginFeedSource — fetch/install the Nexus plugin on connect, hot-load (FULL)"
```

---

## Task 8: App — `KeepPluginsUpdated` setting + Settings surface

**Files:**
- Modify: `src/ModManager.App/Services/AppSettingsService.cs`
- Modify: the Settings view + its view-model (the same surface that hosts the "auto-update definitions" toggle)
- Test: `tests/ModManager.Tests/...` — add to the existing App-settings test if present, else `tests/ModManager.App.NexusValidate.Tests/` (App-reachable, no WinUI). If no App-settings test project exists, assert the JSON shape directly (below).

**Interfaces:**
- Produces: `AppSettingsService.KeepPluginsUpdated` (bool, default **true**) + `SetKeepPluginsUpdated(bool)`, persisted as the camelCase key `keepPluginsUpdated`.

- [ ] **Step 1: Write the failing test** (round-trip + camelCase key)

```csharp
// In an App-reachable test project (e.g. tests/ModManager.App.NexusValidate.Tests/AppSettingsKeepPluginsTests.cs)
using System.IO;
using ModManager.App.Services;

namespace ModManager.App.NexusValidate.Tests;

public class AppSettingsKeepPluginsTests
{
    [Fact]
    public void KeepPluginsUpdated_defaults_true_and_persists_camelCase()
    {
        var svc = new AppSettingsService();          // fresh machine default
        Assert.True(svc.KeepPluginsUpdated);

        svc.SetKeepPluginsUpdated(false);
        var json = File.ReadAllText(svc.Path);
        Assert.Contains("\"keepPluginsUpdated\":false", json);   // camelCase on disk

        // A new instance reads the persisted value back.
        Assert.False(new AppSettingsService().KeepPluginsUpdated);

        new AppSettingsService().SetKeepPluginsUpdated(true);     // restore default for other tests
    }
}
```

- [ ] **Step 2: Run it, verify it fails** — `dotnet test tests/ModManager.App.NexusValidate.Tests/ModManager.App.NexusValidate.Tests.csproj --filter AppSettingsKeepPluginsTests` → FAIL.

- [ ] **Step 3: Implement in `AppSettingsService`** — mirror `AutoCheckModUpdates` exactly:

```csharp
    private bool _keepPluginsUpdated;

    /// <summary>Whether the launcher auto-updates installed off-Store plugins on a 24h debounce
    /// (default on). The first install on Nexus connect happens regardless; this gates re-checks.</summary>
    public bool KeepPluginsUpdated => _keepPluginsUpdated;

    public void SetKeepPluginsUpdated(bool enabled)
    {
        if (_keepPluginsUpdated == enabled) return;
        _keepPluginsUpdated = enabled;
        Save();
    }
```

In the ctor, after `_autoCheckModUpdates = LoadAutoCheckModUpdates();` add `_keepPluginsUpdated = LoadKeepPluginsUpdated();`, add a `LoadKeepPluginsUpdated()` copy of `LoadAutoCheckModUpdates()` reading `"keepPluginsUpdated"`, and extend `Save()`'s JSON string with `+ $",\"keepPluginsUpdated\":{(_keepPluginsUpdated ? "true" : "false")}"` (before the closing `}}`).

- [ ] **Step 4: Run it, verify it passes** — `--filter AppSettingsKeepPluginsTests` → PASS.

- [ ] **Step 5: Add the Settings UI** — in the Settings view, beside the "auto-update definitions" toggle, add a "Keep plugins updated" toggle bound to `KeepPluginsUpdated`/`SetKeepPluginsUpdated`, and a read-only status line sourced from `InstalledPluginsStore.Read(PluginHost.PluginsDir + "/installed-plugins.json")` (FULL only — wrap the status row in `#if FULL`, or show "Plugins are a desktop-only feature" on STORE). Builder-voice copy: `Keep plugins updated` / `Nexus plugin: v1.0.0 installed` / `Nexus plugin: not installed`.

- [ ] **Step 6: Build FULL + STORE**

Run (kill the app first): both `-p:Platform=x64` and `+ -p:Configuration=Store` → 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/ModManager.App/Services/AppSettingsService.cs src/ModManager.App/Views/<settings files> tests/ModManager.App.NexusValidate.Tests/AppSettingsKeepPluginsTests.cs
git commit -m "feat(settings): keep-plugins-updated toggle + installed-plugin status (FULL)"
```

---

## Task 9: Verify — full suite + both flavors + STORE absence

**Files:** none (verification only).

- [ ] **Step 1: Full Core suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: all green, including `CorePurity` (the new Core `Plugins` code references only BCL + Core) and the new Plugin* tests.

- [ ] **Step 2: App-reachable test project**

Run: `dotnet test tests/ModManager.App.NexusValidate.Tests/ModManager.App.NexusValidate.Tests.csproj`
Expected: green (incl. the new setting test).

- [ ] **Step 3: Both App flavors**

Run (kill the app first):
`dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
`dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Configuration=Store`
Expected: both 0 errors.

- [ ] **Step 4: Confirm `PluginFeedSource` is absent from STORE**

Run: `grep -rn "PluginFeedSource" src/ModManager.App/Services/PluginFeedSource.cs` (confirm the file is wholly inside `#if FULL`/`#endif`), and confirm the connect-call-site reference is inside `#if FULL`. The STORE build proves it (it compiled with the reference compiled out).

- [ ] **Step 5: Update the smoke doc + commit**

Add a `## Plugin slice 5c-consumer` section to `docs/smoke-tests/pending.md`: the consumer is built + unit-tested against a hand-signed fixture, but the **live** loop (connect Nexus → fetch the real signed plugin → hot-load) can't smoke until 5c-producer publishes the feed. Note the dark-until-producer behavior: today the feed URL 404s and fail-silent leaves the app exactly as now.

```bash
git add docs/smoke-tests/pending.md
git commit -m "docs(smoke): 5c-consumer — built + fixture-tested; live loop pending 5c-producer"
```

---

## Self-review notes (resolved)

- **Spec coverage:** trust chain (Tasks 5,6 — index sig + dll sig verify), index + gate (Tasks 1,2), sha (Task 3), atomic install + installed record (Tasks 4,5), hot-load (Task 6), connect-trigger + debounce + fail-silent (Task 7), Settings + toggle (Task 8), STORE absence (Task 9). The `schemaVersion`/`minBinaryVersion` gate is Task 2; first-install-bypasses-debounce is Task 7 Step 2.
- **Producer items intentionally absent:** Abstractions NuGet, source move, signing CI, the repo — all 5c-producer.
- **Type consistency:** `PluginFeedRequest`, `InstalledPlugin`, `PluginDownload`, `PluginGate.SelectInstallable`, `PluginIntegrity.Sha256Matches`, `InstalledPluginsStore.Read/Write`, `PluginHost.LoadOne`, `AppSettingsService.KeepPluginsUpdated/SetKeepPluginsUpdated` are used identically wherever referenced.
- **camelCase:** Task 4 asserts the `versions` key; Task 8 asserts the `keepPluginsUpdated` key. Both literal-string assertions.
- **Open detail for the implementer:** the exact connect-call-site file (Task 7 Step 3) and Settings view files (Task 8 Step 5) are located by searching `ConnectAsync(` and the existing "auto-update definitions" toggle — named in-task rather than guessed at a line number.
