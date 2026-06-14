# Game Manifest — go-live, slice: the signer

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sign the generated manifest so the launcher will trust it. A pure `ManifestSigner.Sign` (ECDSA P-256, the **same** `ManifestSignature.Format` the launcher verifies with) + a miner `--sign` step that reads the private key from `MANIFEST_SIGNING_KEY` and emits `games-manifest.json` + `games-manifest.json.sig`.

**Architecture:** Extends `tools/ManifestMiner/`. `ManifestSigner.Sign(manifestBytes, privateKeyPem) → signature` reuses `ModManager.Core.Manifest.ManifestSignature.Format` so sign and verify can never drift — proven by a sign→verify round-trip test. The CLI's `--sign` serializes the final manifest **once**, writes `games-manifest.json` with those exact bytes, signs those exact bytes, and writes the `.sig` (canonical: sign the literal published bytes, never re-serialize).

**Tech Stack:** .NET 10, C#, `System.Security.Cryptography`, xUnit. Tool-only.

**Spec:** roadmap §6 (detached signature, ECDSA P-256, private key in CI); runbook `docs/manifest-feed-runbook.md`.

---

## Scope

**In:** `ManifestSigner.Sign` (pure, tested) + the miner `--sign` CLI step (emit `games-manifest.json` + `.sig`). **Out:** the feed-repo CI workflow (next slice — it invokes this `--sign`), the feed URL wiring + release (go-live), any Core/App change. The private key never appears in source — `--sign` reads it from the `MANIFEST_SIGNING_KEY` env var (the CI provides it from the repo secret).

## The one correctness invariant

`ManifestSigner.Sign` MUST use `ManifestSignature.Format` (`IeeeP1363FixedFieldConcatenation`) and `HashAlgorithmName.SHA256` — identical to the launcher's `ManifestSignature.Verify`. Signing with raw `openssl` (DER / `Rfc3279DerSequence`) would fail verification. The round-trip test (`Sign` here → `ManifestSignature.Verify` in Core) is the guard.

---

## File Structure

- Create: `tools/ManifestMiner/ManifestSigner.cs`
- Modify: `tools/ManifestMiner/Program.cs` — `--sign` step.
- Create: `tests/ModManager.Tests/Miner/ManifestSignerTests.cs`

**Test command:** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
**Run (manual):** `MANIFEST_SIGNING_KEY="$(cat key.pkcs8.pem)" dotnet run --project tools/ManifestMiner -- --with-mo2 --with-overrides --sign`

---

### Task 1: ManifestSigner.Sign — round-trips with the launcher's verify

**Files:**
- Create: `tools/ManifestMiner/ManifestSigner.cs`
- Test: `tests/ModManager.Tests/Miner/ManifestSignerTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ModManager.Tests/Miner/ManifestSignerTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using ManifestMiner;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Miner;

public class ManifestSignerTests
{
    private static (string PrivatePem, byte[] Spki) NewKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportPkcs8PrivateKeyPem(), ecdsa.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void Signature_verifies_against_the_launchers_verify_path()
    {
        var (privatePem, spki) = NewKeyPair();
        var bytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"games\":[]}");

        var sig = ManifestSigner.Sign(bytes, privatePem);

        // The launcher's own verifier must accept it — proves format + hash match (no DER/P1363 drift).
        Assert.True(ManifestSignature.Verify(spki, bytes, sig));
    }

    [Fact]
    public void Tampered_bytes_fail_verification()
    {
        var (privatePem, spki) = NewKeyPair();
        var bytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1}");
        var sig = ManifestSigner.Sign(bytes, privatePem);

        var tampered = Encoding.UTF8.GetBytes("{\"schemaVersion\":2}");
        Assert.False(ManifestSignature.Verify(spki, tampered, sig));
    }

    [Fact]
    public void Signature_from_one_key_fails_against_another()
    {
        var (privatePem, _) = NewKeyPair();
        var (_, otherSpki) = NewKeyPair();
        var bytes = Encoding.UTF8.GetBytes("payload");
        var sig = ManifestSigner.Sign(bytes, privatePem);

        Assert.False(ManifestSignature.Verify(otherSpki, bytes, sig));
    }

    [Fact]
    public void Garbage_private_key_throws_cryptographic_exception()
    {
        // The CLI surfaces this as a hard failure (a bad MANIFEST_SIGNING_KEY must not silently no-op).
        Assert.ThrowsAny<CryptographicException>(() => ManifestSigner.Sign(new byte[] { 1 }, "not a pem"));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestSignerTests"`
Expected: FAIL — `ManifestSigner` does not exist.

- [ ] **Step 3: Implement the signer**

`tools/ManifestMiner/ManifestSigner.cs`:

```csharp
using System.Security.Cryptography;
using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Signs the manifest bytes with ECDSA P-256 / SHA-256, using the EXACT
/// <see cref="ManifestSignature.Format"/> the launcher verifies with — so sign and verify cannot
/// drift. The private key (PKCS#8 PEM) comes from CI (the MANIFEST_SIGNING_KEY secret); it never
/// touches source. Sign the literal published bytes — the caller must pass the same bytes it writes
/// to games-manifest.json (no re-serialize on either side).</summary>
public static class ManifestSigner
{
    public static byte[] Sign(ReadOnlySpan<byte> manifestBytes, string privateKeyPem)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);                 // throws CryptographicException on garbage
        return ecdsa.SignData(manifestBytes, HashAlgorithmName.SHA256, ManifestSignature.Format);
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestSignerTests"`
Expected: PASS (4 facts) — crucially the round-trip against the launcher's `ManifestSignature.Verify`.

- [ ] **Step 5: Commit**

```bash
git add tools/ManifestMiner/ManifestSigner.cs tests/ModManager.Tests/Miner/ManifestSignerTests.cs
git commit -m "feat(miner): ManifestSigner — ECDSA P-256, round-trips the launcher verify"
```

---

### Task 2: CLI --sign — emit games-manifest.json + .sig

**Files:**
- Modify: `tools/ManifestMiner/Program.cs`

- [ ] **Step 1: Add the --sign step**

In `Program.cs`, after the final manifest is built (Ludusavi [+ MO2] [+ overrides]) and validated, when `--sign` is passed: serialize the validated manifest to bytes **once**, write `out/games-manifest.json` with those bytes, read `MANIFEST_SIGNING_KEY` from the environment, sign those **exact** bytes with `ManifestSigner.Sign`, and write `out/games-manifest.json.sig`. If `--sign` is passed but the env var is missing/empty, fail hard (non-zero exit + clear message) — never emit an unsigned-but-named artifact.

```csharp
if (args.Contains("--sign"))
{
    var finalManifest = /* the most-processed validated manifest in scope
                            (validatedEnriched/curated if those ran, else the Ludusavi one) */;
    var bytes = JsonSerializer.SerializeToUtf8Bytes(finalManifest, ManifestJson.Options);
    var manifestOut = Path.Combine(outDir, "games-manifest.json");
    File.WriteAllBytes(manifestOut, bytes);   // the published artifact

    var keyPem = Environment.GetEnvironmentVariable("MANIFEST_SIGNING_KEY");
    if (string.IsNullOrWhiteSpace(keyPem))
    {
        Console.Error.WriteLine("--sign requires MANIFEST_SIGNING_KEY (PKCS#8 PEM) in the environment.");
        Environment.Exit(1);
        return;
    }

    var sig = ManifestSigner.Sign(bytes, keyPem);          // signs the EXACT published bytes
    File.WriteAllBytes(manifestOut + ".sig", sig);
    Console.WriteLine($"Signed {finalManifest.Games.Count} games -> games-manifest.json (+ .sig, {sig.Length} bytes)");
}
```

(Thread the in-scope final-manifest variable from the existing blocks; keep the unsigned `manifest-draft.json` output intact when `--sign` is absent.)

- [ ] **Step 2: Offline smoke-run (with a throwaway key)**

Generate a throwaway keypair, sign a fixture, verify the `.sig` is produced (full verify is covered by Task 1's round-trip; this proves the CLI wiring + env read):

Run:
```bash
pwsh -Command "$e=[System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256); [System.IO.File]::WriteAllText(\"$env:TEMP\sign.pem\", $e.ExportPkcs8PrivateKeyPem())"
printf 'Skyrim:\n  steam:\n    id: 72850\n' > "$TEMP/ludu-sign.yaml"
MANIFEST_SIGNING_KEY="$(cat "$TEMP/sign.pem")" dotnet run --project tools/ManifestMiner -- --file "$TEMP/ludu-sign.yaml" --with-overrides --sign
ls -1 tools/ManifestMiner/out/games-manifest.json tools/ManifestMiner/out/games-manifest.json.sig
```
Expected: prints "Signed N games -> games-manifest.json (+ .sig, 64 bytes)" (P-256 P1363 sigs are 64 bytes); both files exist. Also confirm the missing-key path: re-run without the env var → exits non-zero with the clear message, no `.sig` written.

- [ ] **Step 3: Commit**

```bash
git add tools/ManifestMiner/Program.cs
git commit -m "feat(miner): --sign step — emit games-manifest.json + detached .sig from MANIFEST_SIGNING_KEY"
```

---

### Task 3: Full suite + scope clean

**Files:** none (verification only).

- [ ] **Step 1: Full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — existing + `ManifestSignerTests`. `CorePurityTests` green.

- [ ] **Step 2: Scope**

Run: `git diff --name-only master..HEAD -- src/`
Expected: EMPTY (this slice is `tools/` + `tests/` only). No Core/App change. Miner output (`out/`) gitignored.

- [ ] **Step 3: Final commit (if needed)**

```bash
git add -A && git commit -m "chore(miner): signer slice — full suite green"
```

(Skip if clean.)

---

## Self-Review

**Spec coverage:** §6 detached ECDSA P-256 signature, private key from CI env, sign the canonical bytes → Tasks 1–2. The feed CI that invokes `--sign` + the URL wiring are later slices. ✓

**Placeholder scan:** none. ✓

**Type consistency:** `ManifestSigner.Sign(ReadOnlySpan<byte>, string) → byte[]` consistent across impl, test, CLI. Reuses real `ManifestSignature.Format` + `ManifestJson.Options`. ✓

**Correctness:** the round-trip test signs here and verifies via the launcher's `ManifestSignature.Verify` — the single guard against format/hash drift. The CLI signs the exact bytes written to `games-manifest.json` (no re-serialize), and fails hard if `MANIFEST_SIGNING_KEY` is absent (no silently-unsigned artifact). ✓

**Judgment flagged:** `Sign` throws `CryptographicException` on a bad key (asserted) rather than returning empty — the CLI turns that + a missing env var into a non-zero exit, so CI fails loudly instead of publishing an unsigned/garbage `.sig`.
