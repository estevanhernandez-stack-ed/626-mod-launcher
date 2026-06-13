# Game Manifest — Phase 1 (slice 1): remote-manifest trust core

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the pure-Core trust primitives that verify, gate, and merge a remote game manifest — the security core Phase 1's network path depends on — with zero change to the current read path.

**Architecture:** Three pure Core units in `src/ModManager.Core/Manifest/`: `ManifestSignature` (ECDSA P-256 + SHA-256 detached-signature verify), `ManifestLoader.LoadVerifiedRemote` (verify → parse → schema/version gate → validate → `GameManifest?`), and `EffectiveManifest.Merge` (overlay a verified remote onto the embedded snapshot by `id`). No network, no new repo, no committed key — the App-side fetch, the pinned production key, and the facade rewire are explicitly later slices. Nothing here is wired into the facades yet, so Phase 0 behavior is untouched.

**Tech Stack:** .NET 10, C#, `System.Security.Cryptography` (`ECDsa`, pure — stays in Core, `CorePurityTests` green), System.Text.Json, xUnit. No new package references.

**Spec:** `docs/superpowers/specs/2026-06-12-game-manifest-roadmap-design.md` §6 (trust model — signing resolved to ECDSA P-256 + SHA-256), §5 (forward-compat: unknown engines skip; schema/version gating), §7 (signature tests).

---

## Why this slice, and what it is NOT

Phase 1 is large (new `626-game-manifest` repo + miner, App-side remote fetch, settings toggle, signature/merge core). This slice is the **trust core only** — the security-critical, pure, fully-testable foundation that every other Phase 1 piece sits on. Building it test-first, in isolation, before any network code touches it, is the disciplined order.

**Explicitly out of scope here (later slices / plans):**
- **No network code.** No `HttpClient`, no fetch, no cache, no 24h debounce. That's the App-layer `RemoteManifestSource` (slice 2).
- **No facade rewire.** `KnownEngines`/`NexusDomains`/`PopularGames` keep reading `EmbeddedGameManifest.Current` exactly as they do today. Wiring them to the effective (merged) manifest is slice 2, where the App registers the source and init-ordering is concrete.
- **No pinned production key, no private key, no CI signing.** `ManifestSignature.Verify` takes the public key as a parameter; tests use an in-test ephemeral keypair. The production keypair is generated and the public key pinned in slice 2 (private key → CI Actions secret). **No secret lands in source** (project rule).
- **No miner / no `626-game-manifest` repo.** Separate plan. This slice consumes raw bytes+signature, it does not produce them.

Because the facades are untouched, this slice changes **zero observable behavior** — the existing parity tests stay green by virtue of nothing in the read path moving.

## Current Phase 0 surface this builds on (already on `master`)

- `ModManager.Core.Manifest.GameManifest` — `{ int SchemaVersion, string? GeneratedUtc, string? MinBinaryVersion, IReadOnlyList<GameManifestEntry> Games }` (init-property records).
- `GameManifestEntry` — `{ Id, Name, Engine?, StoreIds Stores, NexusDomain?, CurseforgeGameId?, ModPath?, FileExtensions?, GroupingRule?, Featured?, ManifestProvenance Provenance }`.
- `ManifestJson.Options` — camelCase, case-insensitive, indented.
- `ManifestValidator.Validate(GameManifest manifest, IReadOnlySet<string> knownEngines)` → `ManifestValidationResult(GameManifest Manifest, IReadOnlyList<string> SkippedUnknownEngines, IReadOnlyList<string> RejectedEntries)`.
- `EmbeddedGameManifest.Current` — the validated embedded `GameManifest` (cached).
- Engine keys come from `EnginePresets.Presets.Keys`.

---

## File Structure

- Create: `src/ModManager.Core/Manifest/ManifestSignature.cs` — ECDSA P-256 verify (pure).
- Create: `src/ModManager.Core/Manifest/ManifestLoader.cs` — `LoadVerifiedRemote` pipeline + `KnownSchemaVersion`.
- Create: `src/ModManager.Core/Manifest/EffectiveManifest.cs` — `Merge(embedded, remote?)`.
- Create: `tests/ModManager.Tests/Manifest/ManifestSignatureTests.cs`
- Create: `tests/ModManager.Tests/Manifest/ManifestLoaderTests.cs`
- Create: `tests/ModManager.Tests/Manifest/EffectiveManifestTests.cs`

**Test command (never bare root — the WinUI App project hangs the build):**
`dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
One class: append ` --filter "FullyQualifiedName~ClassName"`.

---

### Task 1: ManifestSignature — ECDSA P-256 detached-signature verify

**Files:**
- Create: `src/ModManager.Core/Manifest/ManifestSignature.cs`
- Test: `tests/ModManager.Tests/Manifest/ManifestSignatureTests.cs`

- [ ] **Step 1: Write the failing tests**

The verifier takes the public key as a parameter (the production key is pinned in a later slice). Tests use an in-test ephemeral P-256 keypair — no committed key, no secret.

Create `tests/ModManager.Tests/Manifest/ManifestSignatureTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

public class ManifestSignatureTests
{
    // Make an ephemeral P-256 keypair; return (spkiPublicKey, signer).
    private static (byte[] Spki, ECDsa Signer) NewKeyPair()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportSubjectPublicKeyInfo(), ecdsa);
    }

    private static byte[] Sign(ECDsa signer, byte[] data)
        => signer.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    [Fact]
    public void Valid_signature_over_the_bytes_verifies()
    {
        var (spki, signer) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("{\"schemaVersion\":1}");
        var sig = Sign(signer, data);

        Assert.True(ManifestSignature.Verify(spki, data, sig));
    }

    [Fact]
    public void Tampered_payload_fails()
    {
        var (spki, signer) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("{\"schemaVersion\":1}");
        var sig = Sign(signer, data);

        var tampered = Encoding.UTF8.GetBytes("{\"schemaVersion\":2}");
        Assert.False(ManifestSignature.Verify(spki, tampered, sig));
    }

    [Fact]
    public void Tampered_signature_fails()
    {
        var (spki, signer) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("payload");
        var sig = Sign(signer, data);
        sig[0] ^= 0xFF; // flip a bit

        Assert.False(ManifestSignature.Verify(spki, data, sig));
    }

    [Fact]
    public void Signature_from_a_different_key_fails()
    {
        var (_, signerA) = NewKeyPair();
        var (spkiB, _) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("payload");
        var sigFromA = Sign(signerA, data);

        Assert.False(ManifestSignature.Verify(spkiB, data, sigFromA)); // wrong public key
    }

    [Theory]
    [InlineData(new byte[0])]              // empty signature
    [InlineData(new byte[] { 1, 2, 3 })]   // garbage signature
    public void Malformed_signature_returns_false_not_throws(byte[] badSig)
    {
        var (spki, _) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("payload");
        Assert.False(ManifestSignature.Verify(spki, data, badSig));
    }

    [Fact]
    public void Garbage_public_key_returns_false_not_throws()
    {
        var data = Encoding.UTF8.GetBytes("payload");
        Assert.False(ManifestSignature.Verify(new byte[] { 9, 9, 9 }, data, new byte[64]));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestSignatureTests"`
Expected: FAIL — `ManifestSignature` does not exist.

- [ ] **Step 3: Implement the verifier**

Create `src/ModManager.Core/Manifest/ManifestSignature.cs`:

```csharp
using System.Security.Cryptography;

namespace ModManager.Core.Manifest;

/// <summary>
/// Detached-signature verification for a remote manifest. ECDSA over NIST P-256 with SHA-256 —
/// the dependency-free, pure-Core choice (Ed25519 is not first-class in System.Security.Cryptography
/// until .NET 11; see spec §6). The signer (CI) holds the private key; the app pins only the public
/// key (SubjectPublicKeyInfo) and verifies. Signs/verifies the EXACT canonical bytes — the caller
/// must pass the literal on-disk payload, never a re-serialized copy, or whitespace/key-order drift
/// breaks verification.
/// </summary>
public static class ManifestSignature
{
    // Both sides MUST agree on this format. P1363 gives fixed 64-byte signatures for P-256.
    public const DSASignatureFormat Format = DSASignatureFormat.IeeeP1363FixedFieldConcatenation;

    /// <summary>
    /// True iff <paramref name="signature"/> is a valid P-256/SHA-256 signature over
    /// <paramref name="data"/> by the private key matching <paramref name="subjectPublicKeyInfo"/>.
    /// Never throws on malformed key/signature input — returns false.
    /// </summary>
    public static bool Verify(
        ReadOnlySpan<byte> subjectPublicKeyInfo,
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> signature)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, Format);
        }
        catch (CryptographicException)
        {
            // malformed SPKI, wrong-length signature, etc. — a verification failure, not a crash
            return false;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestSignatureTests"`
Expected: PASS (all facts + theories).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Manifest/ManifestSignature.cs tests/ModManager.Tests/Manifest/ManifestSignatureTests.cs
git commit -m "feat(manifest): ECDSA P-256 detached-signature verify (pure Core)"
```

---

### Task 2: ManifestLoader.LoadVerifiedRemote — verify → parse → gate → validate

**Files:**
- Create: `src/ModManager.Core/Manifest/ManifestLoader.cs`
- Test: `tests/ModManager.Tests/Manifest/ManifestLoaderTests.cs`

This is the full trust pipeline for a remote payload. Any failure returns `null` (the caller falls back to embedded); it never throws to the user. Order: verify signature → parse JSON → reject `schemaVersion` newer than this binary understands → reject `minBinaryVersion` newer than this binary → `ManifestValidator.Validate` (skips unknown-engine rows, rejects unsafe `modPath`).

- [ ] **Step 1: Write the failing tests**

Create `tests/ModManager.Tests/Manifest/ManifestLoaderTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

public class ManifestLoaderTests
{
    private static readonly Version Binary = new(0, 6, 0);
    private static readonly IReadOnlySet<string> Engines = EnginePresets.Presets.Keys.ToHashSet();

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

    private static GameManifest OneGame(int schema = 1, string? minBinary = "0.6.0", string engine = "bethesda")
        => new()
        {
            SchemaVersion = schema,
            MinBinaryVersion = minBinary,
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "test-game",
                    Name = "Test Game",
                    Engine = engine,
                    Stores = new StoreIds { SteamAppId = "12345" },
                    Provenance = new ManifestProvenance { Sources = new[] { "nexus-domains" } },
                },
            },
        };

    [Fact]
    public void Valid_signed_manifest_loads()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame());

        var result = ManifestLoader.LoadVerifiedRemote(bytes, sig, spki, Binary, Engines);

        Assert.NotNull(result);
        Assert.Contains(result!.Games, g => g.Id == "test-game");
    }

    [Fact]
    public void Bad_signature_returns_null()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame());
        bytes[10] ^= 0xFF; // tamper payload after signing

        Assert.Null(ManifestLoader.LoadVerifiedRemote(bytes, sig, spki, Binary, Engines));
    }

    [Fact]
    public void Unparseable_json_returns_null()
    {
        var (spki, signer) = NewKeyPair();
        var bytes = Encoding.UTF8.GetBytes("this is not json");
        var sig = signer.SignData(bytes, HashAlgorithmName.SHA256, ManifestSignature.Format);

        Assert.Null(ManifestLoader.LoadVerifiedRemote(bytes, sig, spki, Binary, Engines));
    }

    [Fact]
    public void Newer_schema_version_returns_null()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame(schema: ManifestLoader.KnownSchemaVersion + 1));

        Assert.Null(ManifestLoader.LoadVerifiedRemote(bytes, sig, spki, Binary, Engines));
    }

    [Fact]
    public void Higher_min_binary_version_returns_null()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame(minBinary: "99.0.0"));

        Assert.Null(ManifestLoader.LoadVerifiedRemote(bytes, sig, spki, Binary, Engines));
    }

    [Fact]
    public void Missing_min_binary_version_is_allowed()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame(minBinary: null));

        Assert.NotNull(ManifestLoader.LoadVerifiedRemote(bytes, sig, spki, Binary, Engines));
    }

    [Fact]
    public void Unknown_engine_rows_are_skipped_but_manifest_still_loads()
    {
        var (spki, signer) = NewKeyPair();
        var (bytes, sig) = SignManifest(signer, OneGame(engine: "engine-from-the-future"));

        var result = ManifestLoader.LoadVerifiedRemote(bytes, sig, spki, Binary, Engines);

        Assert.NotNull(result);                                   // load succeeds
        Assert.DoesNotContain(result!.Games, g => g.Id == "test-game"); // unknown-engine row dropped
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestLoaderTests"`
Expected: FAIL — `ManifestLoader` does not exist.

- [ ] **Step 3: Implement the loader**

Create `src/ModManager.Core/Manifest/ManifestLoader.cs`:

```csharp
using System.Text.Json;

namespace ModManager.Core.Manifest;

/// <summary>
/// Loads and trusts a remote manifest payload. Pure: takes raw bytes + signature + the pinned public
/// key + this binary's version, returns a validated <see cref="GameManifest"/> or null. The App layer
/// fetches the bytes (network) and supplies them here; verification, gating, and validation are pure
/// and live in Core. ANY failure returns null so the caller silently falls back to the embedded
/// snapshot — a bad/old/tampered remote can never break a working install (spec §5, §6).
/// </summary>
public static class ManifestLoader
{
    /// <summary>The newest schema version this binary understands. A remote manifest declaring a
    /// higher version is ignored (forward-compat: an old binary never consumes a newer schema).</summary>
    public const int KnownSchemaVersion = 1;

    public static GameManifest? LoadVerifiedRemote(
        ReadOnlySpan<byte> manifestBytes,
        ReadOnlySpan<byte> signature,
        ReadOnlySpan<byte> pinnedPublicKey,
        Version currentBinaryVersion,
        IReadOnlySet<string> knownEngines)
    {
        // 1. Signature over the exact bytes.
        if (!ManifestSignature.Verify(pinnedPublicKey, manifestBytes, signature))
            return null;

        // 2. Parse.
        GameManifest? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<GameManifest>(manifestBytes, ManifestJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
        if (parsed is null)
            return null;

        // 3. Schema-version gate (don't consume a newer schema than we understand).
        if (parsed.SchemaVersion > KnownSchemaVersion)
            return null;

        // 4. minBinaryVersion gate (manifest demands a newer app than this one).
        if (!string.IsNullOrWhiteSpace(parsed.MinBinaryVersion))
        {
            if (!Version.TryParse(parsed.MinBinaryVersion, out var min))
                return null; // malformed version string — refuse rather than guess
            if (min > currentBinaryVersion)
                return null;
        }

        // 5. Validate (skips unknown-engine rows, rejects unsafe modPath).
        return ManifestValidator.Validate(parsed, knownEngines).Manifest;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestLoaderTests"`
Expected: PASS (all 7 facts).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Manifest/ManifestLoader.cs tests/ModManager.Tests/Manifest/ManifestLoaderTests.cs
git commit -m "feat(manifest): LoadVerifiedRemote pipeline — verify, gate, validate"
```

---

### Task 3: EffectiveManifest.Merge — overlay a verified remote onto the embedded snapshot

**Files:**
- Create: `src/ModManager.Core/Manifest/EffectiveManifest.cs`
- Test: `tests/ModManager.Tests/Manifest/EffectiveManifestTests.cs`

Pure merge by `id`: a null remote yields the embedded manifest unchanged; otherwise remote entries override embedded entries with the same `id` and remote-only entries are appended. Merge assumes its remote input already came through `LoadVerifiedRemote` (validated) — it does not re-validate.

- [ ] **Step 1: Write the failing tests**

Create `tests/ModManager.Tests/Manifest/EffectiveManifestTests.cs`:

```csharp
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

public class EffectiveManifestTests
{
    private static GameManifestEntry Entry(string id, string engine)
        => new()
        {
            Id = id,
            Name = id,
            Engine = engine,
            Stores = new StoreIds { SteamAppId = id },
            Provenance = new ManifestProvenance { Sources = new[] { "known-engines" } },
        };

    private static GameManifest Wrap(params GameManifestEntry[] games) => new() { Games = games };

    [Fact]
    public void Null_remote_returns_the_embedded_manifest_unchanged()
    {
        var embedded = Wrap(Entry("a", "bethesda"), Entry("b", "ue-pak"));
        var effective = EffectiveManifest.Merge(embedded, null);

        Assert.Equal(2, effective.Games.Count);
        Assert.Same(embedded, effective); // identity: no copy when there is no remote
    }

    [Fact]
    public void Remote_only_game_is_added()
    {
        var embedded = Wrap(Entry("a", "bethesda"));
        var remote = Wrap(Entry("z", "smapi"));

        var effective = EffectiveManifest.Merge(embedded, remote);

        Assert.Contains(effective.Games, g => g.Id == "a");
        Assert.Contains(effective.Games, g => g.Id == "z");
        Assert.Equal(2, effective.Games.Count);
    }

    [Fact]
    public void Remote_entry_overrides_the_embedded_entry_with_the_same_id()
    {
        var embedded = Wrap(Entry("a", "bethesda"));
        var remote = Wrap(Entry("a", "ue-pak")); // same id, different engine

        var effective = EffectiveManifest.Merge(embedded, remote);

        var a = effective.Games.Single(g => g.Id == "a");
        Assert.Equal("ue-pak", a.Engine);     // remote wins
        Assert.Single(effective.Games);        // no duplicate
    }

    [Fact]
    public void Embedded_entries_not_in_remote_survive()
    {
        var embedded = Wrap(Entry("a", "bethesda"), Entry("b", "ue-pak"));
        var remote = Wrap(Entry("a", "smapi"));

        var effective = EffectiveManifest.Merge(embedded, remote);

        Assert.Equal("smapi", effective.Games.Single(g => g.Id == "a").Engine);
        Assert.Equal("ue-pak", effective.Games.Single(g => g.Id == "b").Engine); // untouched
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~EffectiveManifestTests"`
Expected: FAIL — `EffectiveManifest` does not exist.

- [ ] **Step 3: Implement the merge**

Create `src/ModManager.Core/Manifest/EffectiveManifest.cs`:

```csharp
namespace ModManager.Core.Manifest;

/// <summary>
/// Produces the effective game manifest by overlaying a verified remote manifest onto the embedded
/// snapshot. Pure. The remote is assumed already verified + validated (via
/// <see cref="ManifestLoader.LoadVerifiedRemote"/>); a null remote yields the embedded manifest
/// untouched, which is the steady state until the App-layer remote source is wired in (slice 2).
/// Merge is by <see cref="GameManifestEntry.Id"/>: remote entries override same-id embedded entries,
/// remote-only entries are appended, embedded-only entries survive.
/// </summary>
public static class EffectiveManifest
{
    public static GameManifest Merge(GameManifest embedded, GameManifest? remote)
    {
        if (remote is null)
            return embedded;

        var byId = new Dictionary<string, GameManifestEntry>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var g in embedded.Games)
        {
            if (byId.TryAdd(g.Id, g)) order.Add(g.Id);
            else byId[g.Id] = g;
        }
        foreach (var g in remote.Games) // remote wins on id collision; new ids appended in remote order
        {
            if (byId.TryAdd(g.Id, g)) order.Add(g.Id);
            else byId[g.Id] = g;
        }

        return embedded with { Games = order.Select(id => byId[id]).ToList() };
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~EffectiveManifestTests"`
Expected: PASS (all 4 facts).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Manifest/EffectiveManifest.cs tests/ModManager.Tests/Manifest/EffectiveManifestTests.cs
git commit -m "feat(manifest): EffectiveManifest.Merge — overlay verified remote by id"
```

---

### Task 4: Full Core suite + purity green, confirm read path untouched

**Files:** none (verification only).

- [ ] **Step 1: Run the complete Core test suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — all tests, including the new `ManifestSignatureTests`/`ManifestLoaderTests`/`EffectiveManifestTests`, the unchanged Phase 0 tests (`GameManifestJsonTests`, `ManifestValidatorTests`, `EmbeddedGameManifestTests`, `FacadeMembershipTests`, `ManifestInvariantsTests`), the legacy parity tests (`KnownEnginesTests`, `PopularGamesTests`, `NexusGameDomainTests`), and `CorePurityTests`. The new code uses only `System.Security.Cryptography` + System.Text.Json — both pure — so purity stays green.

- [ ] **Step 2: Confirm zero read-path change**

The facades (`KnownEngines`/`NexusDomains`/`PopularGames`) were not touched in this slice — `git diff master..HEAD --stat -- src/ModManager.Core/KnownEngines.cs src/ModManager.Core/NexusDomains.cs src/ModManager.Core/PopularGames.cs` must be empty. The three new units are not referenced by any production read path yet (only by tests), so behavior is unchanged by construction.

Run: `git diff --stat master..HEAD -- src/ModManager.Core/KnownEngines.cs src/ModManager.Core/NexusDomains.cs src/ModManager.Core/PopularGames.cs`
Expected: empty output.

- [ ] **Step 3: Final commit (if any uncommitted fixups)**

```bash
git add -A
git commit -m "chore(manifest): Phase 1 trust core — full Core suite green"
```

(Skip if the working tree is clean.)

---

## Self-Review

**Spec coverage (against §6, §5, §7):**
- §6 signing resolved to ECDSA P-256 + SHA-256, verify pure in Core, public key as a parameter (pinned key deferred to slice 2), sign/verify exact bytes → Task 1. ✓
- §6 `minBinaryVersion` + `schemaVersion` gating; §5 unknown-engine skip via validator → Task 2. ✓
- §7 signature tests (valid accepted, tampered refused, missing/garbage refused, all return false not throw; remote failures → null → caller falls back to embedded) → Tasks 1, 2. ✓
- Effective (merged) manifest from embedded + remote → Task 3. ✓
- Out of scope confirmed: no network, no facade rewire, no pinned key/secret, no miner — none of these appear in any task. ✓

**Placeholder scan:** No TBD/TODO. Every code step is complete. ✓

**Type consistency:** `ManifestSignature.Verify(ReadOnlySpan<byte>, ReadOnlySpan<byte>, ReadOnlySpan<byte>)` and `ManifestSignature.Format` used identically in Tasks 1–2. `ManifestLoader.LoadVerifiedRemote(bytes, sig, key, Version, IReadOnlySet<string>)` and `ManifestLoader.KnownSchemaVersion` consistent across Task 2 test + impl. `EffectiveManifest.Merge(GameManifest, GameManifest?)` consistent in Task 3. All reference real Phase 0 types (`GameManifest`, `GameManifestEntry`, `StoreIds`, `ManifestProvenance`, `ManifestJson.Options`, `ManifestValidator.Validate`, `EnginePresets.Presets.Keys`). ✓

**One judgment flagged:** `DSASignatureFormat.IeeeP1363FixedFieldConcatenation` (fixed 64-byte sigs) is chosen over DER. Either is fine; both sides must agree. If CI ends up signing with a non-.NET tool (e.g. OpenSSL) in a later slice, switch the `ManifestSignature.Format` constant to `Rfc3279DerSequence` and the tests follow — it's a one-line pivot in one place.
