# Game Manifest — Phase 1 (slice 4): wire LoadVerifiedRemote to the pinned key + effective manifest

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the trust pipeline a one-call entry point: take fetched remote bytes + signature, verify against the **pinned** production key, validate/gate, and — on success — make the manifest effective via `EffectiveManifest.SetRemote`. This is the exact call the App's future `RemoteManifestSource` will make.

**Architecture:** Two additions to `ManifestLoader` (pure Core): (1) a convenience `LoadVerifiedRemote(bytes, sig, binaryVersion)` overload that defaults the pinned key (`ManifestSigningKey.PublicKeySpki`) and the known-engine set (`EnginePresets.Presets.Keys`), and (2) `TryApplyRemote(bytes, sig, binaryVersion, publicKey?=pinned)` that composes load + `SetRemote` and returns whether a remote was applied. No network, no App changes — the fetch that calls this is the next slice.

**Tech Stack:** .NET 10, C#, `System.Security.Cryptography`, xUnit. No new packages.

**Spec:** `docs/superpowers/specs/2026-06-12-game-manifest-roadmap-design.md` §5 (verify → gate → validate → fall back to embedded on any failure), §6 (pinned key is the trust anchor).

---

## Why this slice / what it is NOT

The trust core (`ManifestSignature`, `LoadVerifiedRemote`, `EffectiveManifest.Merge`/`SetRemote`) and the pinned key (`ManifestSigningKey`) are all merged. This slice connects them into the entry point the App will call, so the App slice is reduced to "fetch bytes → call `TryApplyRemote`." Pure Core, fully testable.

**Out of scope:** no `HttpClient`/network, no `src/ModManager.App` change, no `Program.Main` edit, no settings toggle, no miner. The App-side `RemoteManifestSource` (fetch → cache → call `TryApplyRemote` at startup) is the next slice.

**Testability note (important):** the production private key lives only in CI, so a *genuine* signature under the pinned key cannot be produced in tests. Therefore:
- The pinned-key **acceptance** path is covered indirectly: `LoadVerifiedRemote`'s happy path is already tested in `ManifestLoaderTests` with an in-test keypair; the convenience overload just supplies the pinned key as the default.
- The pinned-key **rejection** path IS directly testable (a forged signature must be refused) and is asserted here.
- `TryApplyRemote`'s full success path (load → `SetRemote`) is tested by passing an explicit in-test `publicKey` + a genuine signature from its private half. Production omits `publicKey`, so it uses the pinned key.

## Current shapes this builds on (on `master`)

- `ManifestLoader.LoadVerifiedRemote(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> pinnedPublicKey, Version currentBinaryVersion, IReadOnlySet<string> knownEngines)` → `GameManifest?`. Plus `ManifestLoader.KnownSchemaVersion`.
- `ManifestSigningKey.PublicKeySpki` → `byte[]` (pinned SPKI, P-256).
- `EffectiveManifest.SetRemote(GameManifest?)`, `EffectiveManifest.Current`, `EffectiveManifest.Generation`.
- `EnginePresets.Presets` → `IReadOnlyDictionary<string, EnginePreset>` (keys are the known engine ids). `EnginePresets` is in `ModManager.Core`; `ManifestLoader` is in `ModManager.Core.Manifest` (child namespace), so it resolves `EnginePresets` / `ManifestSigningKey` without a `using`.
- `ManifestSignature.Format` (`IeeeP1363FixedFieldConcatenation`) — the signature format both sides use.
- Test isolation: `[CollectionDefinition("ManifestState", DisableParallelization = true)]` already exists in `tests/ModManager.Tests/Manifest/ManifestStateCollection.cs`.

---

## File Structure

- Modify: `src/ModManager.Core/Manifest/ManifestLoader.cs` — add the convenience overload + `TryApplyRemote`.
- Create: `tests/ModManager.Tests/Manifest/ManifestLoaderWiringTests.cs` (in the `ManifestState` collection).
- Untouched: every existing test file (including `ManifestLoaderTests`, the parity tests).

**Test command (never bare root):** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

---

### Task 1: Convenience overload — default the pinned key + engine set

**Files:**
- Modify: `src/ModManager.Core/Manifest/ManifestLoader.cs`
- Test: `tests/ModManager.Tests/Manifest/ManifestLoaderWiringTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/ModManager.Tests/Manifest/ManifestLoaderWiringTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text.Json;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

[Collection("ManifestState")]
public class ManifestLoaderWiringTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null);

    private static readonly Version Binary = new(0, 6, 0);

    private static (byte[] Spki, ECDsa Signer) NewKeyPair()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportSubjectPublicKeyInfo(), ecdsa);
    }

    private static (byte[] Bytes, byte[] Sig) SignManifest(ECDsa signer, GameManifest m)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(m, ManifestJson.Options);
        var sig = signer.SignData(bytes, HashAlgorithmName.SHA256, ManifestSignature.Format);
        return (bytes, sig);
    }

    private static GameManifest OneGame(string id = "wired-game") => new()
    {
        SchemaVersion = 1,
        MinBinaryVersion = "0.6.0",
        Games = new[]
        {
            new GameManifestEntry
            {
                Id = id, Name = id, Engine = "bethesda",
                Stores = new StoreIds { SteamAppId = "80001" },
                Provenance = new ManifestProvenance { Sources = new[] { ManifestSources.KnownEngines } },
            },
        },
    };

    [Fact]
    public void Convenience_overload_rejects_a_signature_not_from_the_pinned_key()
    {
        // Signed by a random key, not the pinned production key -> the 3-arg overload (which defaults
        // to the pinned key) must refuse it.
        var (_, attacker) = NewKeyPair();
        var (bytes, sig) = SignManifest(attacker, OneGame());

        Assert.Null(ManifestLoader.LoadVerifiedRemote(bytes, sig, Binary));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestLoaderWiringTests.Convenience_overload_rejects_a_signature_not_from_the_pinned_key"`
Expected: FAIL — the 3-arg `LoadVerifiedRemote` overload does not exist.

- [ ] **Step 3: Add the convenience overload**

In `src/ModManager.Core/Manifest/ManifestLoader.cs`, add this overload alongside the existing 5-parameter `LoadVerifiedRemote` (do not change the existing method):

```csharp
    /// <summary>
    /// Convenience overload: verify + load a remote manifest using the PINNED production key
    /// (<see cref="ManifestSigningKey.PublicKeySpki"/>) and this binary's known engine set
    /// (<see cref="EnginePresets.Presets"/>). Returns null on any failure (caller falls back to embedded).
    /// </summary>
    public static GameManifest? LoadVerifiedRemote(byte[] manifestBytes, byte[] signature, Version currentBinaryVersion)
        => LoadVerifiedRemote(
            manifestBytes,
            signature,
            ManifestSigningKey.PublicKeySpki,
            currentBinaryVersion,
            EnginePresets.Presets.Keys.ToHashSet());
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestLoaderWiringTests.Convenience_overload_rejects_a_signature_not_from_the_pinned_key"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Manifest/ManifestLoader.cs tests/ModManager.Tests/Manifest/ManifestLoaderWiringTests.cs
git commit -m "feat(manifest): LoadVerifiedRemote overload defaulting the pinned key"
```

---

### Task 2: TryApplyRemote — verify-then-apply to the effective manifest

**Files:**
- Modify: `src/ModManager.Core/Manifest/ManifestLoader.cs`
- Test: append to `tests/ModManager.Tests/Manifest/ManifestLoaderWiringTests.cs`

- [ ] **Step 1: Append the failing tests**

Add to the `ManifestLoaderWiringTests` class:

```csharp
    [Fact]
    public void TryApplyRemote_applies_a_genuinely_signed_manifest()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame("applied-game"));

        var applied = ManifestLoader.TryApplyRemote(bytes, sig, Binary, spki); // explicit test key

        Assert.True(applied);
        Assert.Contains(EffectiveManifest.Current.Games, g => g.Id == "applied-game");
    }

    [Fact]
    public void TryApplyRemote_does_not_apply_a_forged_manifest()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame("forged-game"));
        bytes[12] ^= 0xFF; // tamper after signing

        var applied = ManifestLoader.TryApplyRemote(bytes, sig, Binary, spki);

        Assert.False(applied);
        Assert.DoesNotContain(EffectiveManifest.Current.Games, g => g.Id == "forged-game");
        Assert.Same(EmbeddedGameManifest.Current, EffectiveManifest.Current); // stayed on embedded
    }

    [Fact]
    public void TryApplyRemote_with_default_pinned_key_rejects_a_non_pinned_signature()
    {
        var (_, attacker) = NewKeyPair();
        var (bytes, sig) = SignManifest(attacker, OneGame("nope"));

        var applied = ManifestLoader.TryApplyRemote(bytes, sig, Binary); // default -> pinned key

        Assert.False(applied);
        Assert.Same(EmbeddedGameManifest.Current, EffectiveManifest.Current);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestLoaderWiringTests.TryApplyRemote"`
Expected: FAIL — `TryApplyRemote` does not exist.

- [ ] **Step 3: Add TryApplyRemote**

In `src/ModManager.Core/Manifest/ManifestLoader.cs`, add:

```csharp
    /// <summary>
    /// Verify a fetched remote manifest and, if it passes, make it effective via
    /// <see cref="EffectiveManifest.SetRemote"/>. Returns true iff a remote was applied; on any
    /// verification/validation failure returns false and leaves the effective manifest on the
    /// embedded snapshot. <paramref name="publicKey"/> defaults to the pinned production key; tests
    /// pass an explicit key. This is the entry point the App's remote fetch calls at startup.
    /// </summary>
    public static bool TryApplyRemote(
        byte[] manifestBytes,
        byte[] signature,
        Version currentBinaryVersion,
        byte[]? publicKey = null)
    {
        var manifest = LoadVerifiedRemote(
            manifestBytes,
            signature,
            publicKey ?? ManifestSigningKey.PublicKeySpki,
            currentBinaryVersion,
            EnginePresets.Presets.Keys.ToHashSet());

        if (manifest is null)
            return false;

        EffectiveManifest.SetRemote(manifest);
        return true;
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestLoaderWiringTests"`
Expected: PASS (all 4 facts in the class).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Manifest/ManifestLoader.cs tests/ModManager.Tests/Manifest/ManifestLoaderWiringTests.cs
git commit -m "feat(manifest): TryApplyRemote — verify then SetRemote (App entry point)"
```

---

### Task 3: Full suite + purity green, scope + read-path clean

**Files:** none (verification only).

- [ ] **Step 1: Run the complete Core suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — all tests including the new `ManifestLoaderWiringTests`, the unchanged `ManifestLoaderTests`/`ManifestSignatureTests`/`EffectiveManifest*`/parity tests, and `CorePurityTests` (only Core/BCL touched).

- [ ] **Step 2: Confirm scope + read path**

Run: `git diff --name-only master..HEAD -- src/`
Expected: only `src/ModManager.Core/Manifest/ManifestLoader.cs`. No `src/ModManager.App`, no `HttpClient`. The parity test files and the facades are untouched.

- [ ] **Step 3: Final commit (if any uncommitted fixups)**

```bash
git add -A && git commit -m "chore(manifest): wire LoadVerifiedRemote — full Core suite green"
```

(Skip if clean.)

---

## Self-Review

**Spec coverage:** §5 verify→gate→validate→fall-back (TryApplyRemote returns false + leaves embedded on failure) → Task 2. §6 pinned key as trust anchor (the overloads default `ManifestSigningKey.PublicKeySpki`) → Tasks 1–2. ✓

**Placeholder scan:** none. ✓

**Type consistency:** the new 3-arg `LoadVerifiedRemote(byte[], byte[], Version)` and `TryApplyRemote(byte[], byte[], Version, byte[]?)` are distinct in arity/signature from the existing 5-arg method (no ambiguous overload). They reference real members: `ManifestSigningKey.PublicKeySpki`, `EnginePresets.Presets.Keys`, `EffectiveManifest.SetRemote`, `ManifestSignature.Format`. ✓

**Testability honesty:** the production-key acceptance path is not unit-testable (private key is CI-only); covered indirectly by `ManifestLoaderTests` (in-test key happy path) + the pinned-key rejection test here. `TryApplyRemote`'s apply path is tested with an explicit in-test key. This is called out in the plan body, not hidden. ✓

**One judgment flagged:** `TryApplyRemote` lives in Core and calls the Core-static `EffectiveManifest.SetRemote` — the verify+apply composition is pure and belongs with the trust pipeline. The App slice only fetches bytes and calls this with its assembly `Version`; it owns *when* (startup) and *whether* (the settings toggle) to call it.
