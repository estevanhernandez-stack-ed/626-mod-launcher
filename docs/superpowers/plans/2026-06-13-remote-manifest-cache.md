# Game Manifest — App fetch glue, slice A: RemoteManifestCache (Core)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The testable heart of the remote-feed consume path: read a cached signed manifest from disk and apply it (verify → validate → `SetRemote`) at startup, and write a fetched manifest+signature to that cache atomically. Pure Core (file IO + crypto, no network, no WinUI) — fully unit-tested.

**Architecture:** `RemoteManifestCache` (Core, `src/ModManager.Core/Manifest/`): `ApplyCached(cacheDir, version, publicKey?)` reads `game-manifest.json` + `.sig` from the cache dir and routes them through the already-tested `ManifestLoader.TryApplyRemote`; `WriteCache(cacheDir, bytes, sig)` writes both atomically (temp + rename, mirroring `AtomicJson`). The App-layer shell (HttpClient fetch, `Program.Main`/`OnLaunched` wiring, 24h debounce, settings toggle) is **slice B** — it calls these two methods.

**Tech Stack:** .NET 10, C#, `System.IO`, `System.Security.Cryptography` (via `TryApplyRemote`), xUnit. No new packages.

**Spec:** roadmap §5/§6 (verify → validate → fall back to embedded on any failure); runbook `docs/manifest-feed-runbook.md` (the App consumes the signed output).

---

## Scope

**In:** `RemoteManifestCache.ApplyCached` + `WriteCache`, in Core, fully tested. **Out (slice B):** HttpClient fetch, the feed URL, `Program.Main`/`OnLaunched` wiring, the 24h debounce, the "auto-update definitions" settings toggle + SettingsDialog. No `src/ModManager.App` change in this slice; no network.

**Why split:** the App shell is WinUI/network — not coverable by the headless test suite. Concentrating the cache/apply/verify logic in Core keeps the load-bearing part fully tested; slice B's shell is then a thin, build-verified caller.

**Built-before-caller:** `RemoteManifestCache` has no production caller until slice B wires it — same disciplined order as the trust core and `TryApplyRemote`. Proven inert (nothing calls it; behavior unchanged).

## Current shapes this builds on (on `master`)

- `ManifestLoader.TryApplyRemote(byte[] manifestBytes, byte[] signature, Version currentBinaryVersion, byte[]? publicKey = null) → bool` — verify (pinned key by default) → validate → `EffectiveManifest.SetRemote` on success; false + embedded on any failure; never throws.
- `ManifestSigningKey.PublicKeySpki` — the pinned production key (the default `TryApplyRemote` uses).
- `AtomicJson` (Core) — the temp-file + rename atomic-write pattern to mirror.
- Test isolation: `[CollectionDefinition("ManifestState", DisableParallelization = true)]` exists (`tests/ModManager.Tests/Manifest/ManifestStateCollection.cs`); tests that touch `SetRemote` join it and reset `SetRemote(null)`.

---

## File Structure

- Create: `src/ModManager.Core/Manifest/RemoteManifestCache.cs`
- Create: `tests/ModManager.Tests/Manifest/RemoteManifestCacheTests.cs`

**Test command (never bare root):** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

---

### Task 1: ApplyCached — read the cache and apply

**Files:**
- Create: `src/ModManager.Core/Manifest/RemoteManifestCache.cs` (with `ApplyCached`)
- Test: `tests/ModManager.Tests/Manifest/RemoteManifestCacheTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ModManager.Tests/Manifest/RemoteManifestCacheTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text.Json;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

[Collection("ManifestState")]
public class RemoteManifestCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rmc-" + Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        EffectiveManifest.SetRemote(null);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private static readonly Version Binary = new(0, 6, 0);

    private static (byte[] Spki, ECDsa Signer) NewKeyPair()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportSubjectPublicKeyInfo(), ecdsa);
    }

    private static (byte[] Bytes, byte[] Sig) SignManifest(ECDsa signer)
    {
        var m = new GameManifest
        {
            SchemaVersion = 1,
            MinBinaryVersion = "0.6.0",
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "cached-game", Name = "Cached Game", Engine = "bethesda",
                    Stores = new StoreIds { SteamAppId = "70010" },
                    Provenance = new ManifestProvenance { Sources = new[] { ManifestSources.KnownEngines } },
                },
            },
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(m, ManifestJson.Options);
        return (bytes, signer.SignData(bytes, HashAlgorithmName.SHA256, ManifestSignature.Format));
    }

    [Fact]
    public void Applies_a_validly_signed_cached_manifest()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer);
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "game-manifest.json"), bytes);
        File.WriteAllBytes(Path.Combine(_dir, "game-manifest.json.sig"), sig);

        var applied = RemoteManifestCache.ApplyCached(_dir, Binary, spki);

        Assert.True(applied);
        Assert.Contains(EffectiveManifest.Current.Games, g => g.Id == "cached-game");
    }

    [Fact]
    public void Returns_false_when_cache_is_missing()
    {
        var applied = RemoteManifestCache.ApplyCached(_dir, Binary); // dir doesn't exist
        Assert.False(applied);
        Assert.Same(EmbeddedGameManifest.Current, EffectiveManifest.Current);
    }

    [Fact]
    public void Returns_false_and_stays_on_embedded_for_a_tampered_cache()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer);
        bytes[15] ^= 0xFF; // tamper after signing
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "game-manifest.json"), bytes);
        File.WriteAllBytes(Path.Combine(_dir, "game-manifest.json.sig"), sig);

        var applied = RemoteManifestCache.ApplyCached(_dir, Binary, spki);

        Assert.False(applied);
        Assert.Same(EmbeddedGameManifest.Current, EffectiveManifest.Current);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~RemoteManifestCacheTests"`
Expected: FAIL — `RemoteManifestCache` does not exist.

- [ ] **Step 3: Implement ApplyCached**

`src/ModManager.Core/Manifest/RemoteManifestCache.cs`:

```csharp
namespace ModManager.Core.Manifest;

/// <summary>
/// On-disk cache for the remote game manifest. The App fetches the manifest + detached signature
/// into this cache (background, debounced) and applies the cached copy at startup via
/// <see cref="ApplyCached"/> — so a slow/offline network never blocks launch, and a bad/tampered
/// cache silently falls back to the embedded manifest. Pure Core (file IO + crypto, no network).
/// </summary>
public static class RemoteManifestCache
{
    public const string ManifestFileName = "game-manifest.json";
    public const string SignatureFileName = "game-manifest.json.sig";

    /// <summary>
    /// Read the cached manifest + signature from <paramref name="cacheDir"/> and, if they verify,
    /// make the manifest effective. Returns true iff a remote was applied. Missing/unreadable/invalid
    /// cache → false and the effective manifest stays on the embedded snapshot. Never throws.
    /// <paramref name="publicKey"/> defaults to the pinned production key; tests pass an explicit key.
    /// </summary>
    public static bool ApplyCached(string cacheDir, Version binaryVersion, byte[]? publicKey = null)
    {
        try
        {
            var manifestPath = Path.Combine(cacheDir, ManifestFileName);
            var sigPath = Path.Combine(cacheDir, SignatureFileName);
            if (!File.Exists(manifestPath) || !File.Exists(sigPath))
                return false;

            var bytes = File.ReadAllBytes(manifestPath);
            var sig = File.ReadAllBytes(sigPath);
            return ManifestLoader.TryApplyRemote(bytes, sig, binaryVersion, publicKey);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~RemoteManifestCacheTests"`
Expected: PASS (3 facts).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Manifest/RemoteManifestCache.cs tests/ModManager.Tests/Manifest/RemoteManifestCacheTests.cs
git commit -m "feat(manifest): RemoteManifestCache.ApplyCached — verify + apply cached feed"
```

---

### Task 2: WriteCache — atomic write of fetched manifest + signature

**Files:**
- Modify: `src/ModManager.Core/Manifest/RemoteManifestCache.cs`
- Test: append to `tests/ModManager.Tests/Manifest/RemoteManifestCacheTests.cs`

- [ ] **Step 1: Append the failing tests**

Add to `RemoteManifestCacheTests`:

```csharp
    [Fact]
    public void WriteCache_then_ApplyCached_round_trips()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer);

        RemoteManifestCache.WriteCache(_dir, bytes, sig);   // simulates a completed fetch
        var applied = RemoteManifestCache.ApplyCached(_dir, Binary, spki);

        Assert.True(applied);
        Assert.True(File.Exists(Path.Combine(_dir, "game-manifest.json")));
        Assert.True(File.Exists(Path.Combine(_dir, "game-manifest.json.sig")));
        Assert.Contains(EffectiveManifest.Current.Games, g => g.Id == "cached-game");
    }

    [Fact]
    public void WriteCache_creates_the_directory_and_overwrites()
    {
        RemoteManifestCache.WriteCache(_dir, new byte[] { 1, 2, 3 }, new byte[] { 9 });
        RemoteManifestCache.WriteCache(_dir, new byte[] { 4, 5, 6 }, new byte[] { 8 }); // overwrite

        Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(Path.Combine(_dir, "game-manifest.json")));
        Assert.Equal(new byte[] { 8 }, File.ReadAllBytes(Path.Combine(_dir, "game-manifest.json.sig")));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~RemoteManifestCacheTests"`
Expected: FAIL — `WriteCache` does not exist.

- [ ] **Step 3: Implement WriteCache**

Add to `RemoteManifestCache`:

```csharp
    /// <summary>
    /// Write a freshly fetched manifest + detached signature to the cache atomically (temp file +
    /// rename, mirroring <see cref="AtomicJson"/>), so a crash mid-write can never leave a torn cache.
    /// </summary>
    public static void WriteCache(string cacheDir, byte[] manifestBytes, byte[] signature)
    {
        Directory.CreateDirectory(cacheDir);
        WriteAtomic(Path.Combine(cacheDir, ManifestFileName), manifestBytes);
        WriteAtomic(Path.Combine(cacheDir, SignatureFileName), signature);
    }

    private static void WriteAtomic(string file, byte[] bytes)
    {
        var tmp = file + ".tmp-" + Environment.ProcessId;
        try
        {
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, file, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* nothing to clean up */ }
            throw;
        }
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~RemoteManifestCacheTests"`
Expected: PASS (5 facts total).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Manifest/RemoteManifestCache.cs tests/ModManager.Tests/Manifest/RemoteManifestCacheTests.cs
git commit -m "feat(manifest): RemoteManifestCache.WriteCache — atomic cache write"
```

---

### Task 3: Full suite + scope clean

**Files:** none (verification only).

- [ ] **Step 1: Full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — existing + the 5 new `RemoteManifestCacheTests`. `CorePurityTests` green (only `System.IO` + existing Core crypto used).

- [ ] **Step 2: Scope**

Run: `git diff --name-only master..HEAD -- src/`
Expected: only `src/ModManager.Core/Manifest/RemoteManifestCache.cs`. No `src/ModManager.App`, no `HttpClient`/`System.Net`.

- [ ] **Step 3: Final commit (if needed)**

```bash
git add -A && git commit -m "chore(manifest): RemoteManifestCache — full suite green"
```

(Skip if clean.)

---

## Self-Review

**Spec coverage:** §5/§6 verify→validate→fall-back, on-disk cache the App applies at startup → Tasks 1–2. Network fetch + debounce + wiring + toggle are slice B (out of scope). ✓

**Placeholder scan:** none. ✓

**Type consistency:** `RemoteManifestCache.ApplyCached(string, Version, byte[]?) → bool` + `WriteCache(string, byte[], byte[])` consistent across impl + tests; both reference real members (`ManifestLoader.TryApplyRemote`, `ManifestSigningKey.PublicKeySpki` via the default, `EffectiveManifest`). The `publicKey?` default mirrors `TryApplyRemote` so the apply path is testable with an in-test key (the pinned-key acceptance path stays covered indirectly, as before). ✓

**Judgment flagged:** file IO lives in Core here, consistent with `AtomicJson`/`RegistryStore` (file IO is allowed in Core; only WinUI/WinRT/network is the App boundary). The cache dir path is a parameter (the App passes `%LOCALAPPDATA%\ModManagerBuilder`), so Core hardcodes no machine paths. `ApplyCached` swallows `IOException`/`UnauthorizedAccessException` to guarantee launch is never blocked by a bad cache — degrade to embedded.
