# Game-manifest roadmap — design (2026-06-12)

## 0. What this is, and what it is not

This is the multi-game roadmap: how the launcher grows from ~12 hand-wired games to *most games a user owns*, how it knows the way each engine expects mods to be structured, and how it stays current without a release for every new title. Three axes, phased: **breadth** (hundreds of game definitions), **freshness** (new games without an app release), **engine depth** (fewer games falling through to `custom`).

It is **not** a reversal of the 2026-05-31 single-game-support decision. That decision still holds for the data it governs. This spec operates on a layer 05-31 explicitly carved out as already-data-capable, and it inherits 05-31's security spine wholesale. Section 2 is the reconciliation — read it before assuming this contradicts the prior call. It doesn't, and the seam between them is load-bearing.

## 1. The framing: 05-31's three layers, split one finer

The 2026-05-31 spec named three layers of "game support," each with different physics:

1. **Game definition / profile** — engine key, mod path, save root, store IDs, file extensions, grouping rule. *Already data-capable* (`GameProfileDraft` is JSON; `SteamGameImport` builds these at runtime).
2. **Catalog entries** — known mods/frameworks/tools: fingerprint signatures, config paths, attribution. *Compiled C# static arrays.*
3. **Engine enable/disable logic** — the code that toggles a pak vs a DLL vs an esp reversibly. *Code, never data. A law, not a phase.*

That framing answered "how does a single-game *update* reach friends?" — and the answer was Approach A (version-only), because the signed Velopack delta already ships data cheaply, and a remote feed over executable-adjacent data is a trust surface you don't take to save a CI cycle.

This spec asks a *different* question: "how do we support *hundreds* of games, and stay current?" The 05-31 framing survives intact, but layer 1 needs splitting one notch finer, because 05-31 lumped five compiled arrays into one bucket and two of them don't belong with the other three:

- **Layer 1a — game-identity data.** `PopularGames.All`, `KnownEngines.ByAppId`, `NexusDomains`, plus the per-game *overrides* to engine defaults (mod path, extensions, grouping, save hints, store IDs). This is **descriptive, not executable-adjacent**: a wrong Nexus domain shows the wrong web page; a wrong Steam app ID fails to match a library folder. Nothing here gates a file write on its own.
- **Layer 1b / layer 2 — install-affecting catalog data.** `KnownDirectInjectMod.Catalog`, `KnownFramework.Catalog`, `ToolCatalog`. Fingerprint signatures that decide *what gets written where* during intake. **Executable-adjacent.** This is the data 05-31's security analysis was about.

The whole roadmap turns on this split: **layer 1a can travel on a remote channel; layer 1b/2 stays compiled until 05-31's own triggers fire.** One caveat the split must respect — `modPath` lives in layer 1a but influences a write destination, so it is treated as the one trust-sensitive field in the manifest and re-validated through the existing forbidden-paths gate (Section 6). Everything else in 1a is descriptive.

## 2. Relationship to the 2026-05-31 decision (the reconciliation)

Stated plainly, because a reader who knows 05-31 will otherwise think this forgot it:

**05-31 recommended Approach A and put a remote feed (B) at 13/30 — bottom on security and infra cost.** Three things make this spec consistent with that, not a reversal of it:

1. **Different data.** B scored 1/5 on security *specifically because* it put a feed over executable-adjacent data (fingerprints that gate writes). This spec's remote channel carries **only layer 1a** — descriptive game-identity data. Layer 1b/2 (the data 05-31 was protecting) stays exactly where 05-31 left it: compiled C#, gated by the compiler and xUnit. The security objection is *honored*, not overridden — we route around the data it was protecting.

2. **Different driver.** 05-31's trigger to revisit was explicit: "a second contributor, or measured daily catalog churn, **or a real decision to ship a feed.**" The breadth goal — supporting hundreds of games — is that scale driver arriving. 05-31 didn't weigh it because it was scoped to single-game-*update* distribution, not multi-game *breadth*. A version-only model where every new game is a code edit + release does not scale to hundreds; that's the friction 05-31 said would, when real, earn the next move.

3. **05-31's spine is inherited, not discarded.** Detached signature over canonical bytes, public key pinned in the binary, `minBinaryVersion`/schema-version gating, feed paths re-validated through the *same* forbidden-paths gate as user archives, hard-reject any entry carrying a binary, reversibility stays code-enforced in layer 3. Section 6 lifts these verbatim. The difference is we apply them to layer 1a now, because breadth makes them earned now.

The honest line: 05-31 said "don't build a feed to save a CI cycle over data the compiler should guard." This spec says "build a narrow feed for *descriptive* data to make breadth possible, keep the guarded data guarded." Both true. If a reader concludes these conflict, Section 6's trust model is where to push back.

## 3. The GameManifest schema (the spine)

One on-disk shape, camelCase like everything the launcher writes (per the camelCase-JSON-on-disk rule — round-trip test ships with it). Top level:

```jsonc
{
  "schemaVersion": 1,
  "generatedUtc": "2026-06-12T00:00:00Z",
  "minBinaryVersion": "0.6.0",   // a binary older than this ignores the remote copy
  "games": [ /* GameManifestEntry[] */ ]
}
```

Per `GameManifestEntry`:

| Field | Purpose | Notes |
|---|---|---|
| `id` | stable slug | unique; primary key |
| `name` | display name | |
| `engine` | engine key | **must be one the binary knows** — see Section 5 forward-compat rule |
| `stores` | `{ steamAppId?, gogId?, epicAppName?, xboxStoreId? }` | store-agnostic from day one; Steam is the only one *probed* in this roadmap |
| `nexusDomain` | Nexus game key (name, not id) | descriptive |
| `curseforgeGameId` | CurseForge numeric id | descriptive |
| `modPath` | override to engine-default mod folder | **trust-sensitive** — relative only, gate-validated (Section 6) |
| `fileExtensions` | override to engine-default extensions | optional; defaults flow from `EnginePresets` |
| `groupingRule` | override to engine-default grouping | optional |
| `saveDirHint`, `saveTypes`, `launchExe`, `windowTitle` | save/launch hints | optional, descriptive |
| `featured` | quick-pick rank (int) or absent | replaces the `PopularGames` concept |
| `provenance` | `{ sources: ["vortex"\|"mo2"\|"ludusavi"\|"curated"], status: "auto"\|"curated" }` | makes the curation diff reviewable; drives NOTICE attribution |

**Design principle: the manifest says what's *different*, not what's *default*.** Engine defaults still live in `EnginePresets` (code). A manifest entry for a vanilla Bethesda game might carry only `id`, `name`, `engine: "bethesda"`, and `stores.steamAppId`. Overrides are the exception, not the row.

This absorbs `PopularGames`, `KnownEngines`, and `NexusDomains` into one source of truth. It does **not** absorb `KnownDirectInjectMod`/`KnownFramework`/`ToolCatalog` — those stay compiled (Section 2).

## 4. App-side architecture (Core module + thin facades)

New `src/ModManager.Core/Manifest/`:

- `GameManifestEntry`, `GameManifest` — records, camelCase round-trip tested.
- `ManifestValidator` — pure. Rejects unknown-engine-as-fatal (skips the row instead — Section 5), absolute or `..`-bearing `modPath`, malformed entries. Returns a validated manifest plus a skip report.
- `EffectiveManifest` — merges two sources into the answer the rest of Core consumes:
  1. `EmbeddedManifestSource` — snapshot baked into the binary at build (always present, always offline-safe).
  2. Cached remote — **App layer does the HTTP** (mirrors `UpdateChecker` in `src/ModManager.App/Services/UpdateChecker.cs`: 24h debounce stamp under `%LOCALAPPDATA%\ModManagerBuilder`). **Core does parse → validate → signature verify** against a public key pinned in the binary. Network at the App boundary, pure verification in Core — `CorePurityTests` stays green.

**Strict fallback chain:** bad signature, corrupt JSON, `schemaVersion` newer than the binary understands, `minBinaryVersion` gate fails, or offline → silently use embedded. The remote path can only ever make the game list *longer/fresher*, never break a working install. No scary dialog for a network blip.

**Existing call sites do not move.** `KnownEngines.ByAppId()`, `NexusDomains.ByAppId()`, `PopularGames.All` become thin facades over `EffectiveManifest`. `Scanner`, intake, the Add-game wizard, `EnginePresets` — none of them learn this happened. Blast radius stays at the data layer. This is what makes Phase 0 a safe refactor: the facade parity tests (Section 7) prove the facades return byte-identical answers to today's hardcoded arrays.

## 5. Forward-compat: unknown engines skip, never crash

The hard constraint that makes a remote manifest safe across versions: **a remote manifest can only add games for engines the binary already knows.** A new engine is layer 3 — code — and ships as a release. So an old binary *will* fetch a manifest listing games whose `engine` it doesn't recognize.

Rule: `ManifestValidator` **skips** any entry with an unknown engine key and records it in the skip report (surfaced as a quiet "N games need a newer app version" line, not an error). It never aborts the load. An entry the binary can't handle is simply invisible until the user updates. This is the schema-version discipline 05-31 named, applied at the row level.

This also sequences Phase 2 (engine depth): the miner's coverage report (Section 8) ranks which unknown-engine skips are costing the most games. That data — not a guess — picks which 2–3 engines get built per release.

## 6. Security & trust model (inherited from 05-31, applied to layer 1a)

The manifest is descriptive, with one trust-sensitive field (`modPath`). The model, lifted from 05-31's Phase 2 requirements and made non-optional:

- **Detached signature over canonical bytes — resolved to ECDSA P-256 + SHA-256** (2026-06-13 research, high confidence). Ed25519 is *not* first-class in `System.Security.Cryptography` on net10.0 — it exists only inside Composite ML-DSA identifiers, all marked unsupported on every platform; standalone Ed25519 (`dotnet/runtime#63174`) is milestoned for .NET 11. So the spec's named RSA/ECDSA fallback is the build target, concretely **ECDSA over NIST P-256 (secp256r1) with SHA-256** — pure, dependency-free, Core-resident. CI signs the *exact on-disk canonical bytes* (`ECDsa.ImportPkcs8PrivateKey` → `SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.<pinned>)`; private key in an Actions secret). The app verifies with a public key pinned as `SubjectPublicKeyInfo` bytes in the binary (`ECDsa.Create()` → `ImportSubjectPublicKeyInfo` yields a verify-only instance → `VerifyData(...)`). Pin one `DSASignatureFormat` and use it on both sides. **Canonicalization rule:** sign/verify the literal persisted bytes — never re-serialize on the verify side, or whitespace/key-order drift breaks verification. (Avoid the PQC types — ML-DSA/SLH-DSA need Windows 11 Insiders or OpenSSL 3.5+ at runtime.)
- **`modPath` re-validated through the *same* forbidden-paths gate as user archives.** The manifest must never widen the gate. Relative-only, no `..`, no absolute, no escape of the resolved install root. The validate-then-extract rule's gate is the single source of truth; the manifest is just another untrusted input to it.
- **`minBinaryVersion` + `schemaVersion` gating.** A future schema bump can't brick an old binary; a new-format manifest is ignored, falling back to embedded.
- **Hard-reject any entry that tries to carry a binary or a URL to one.** Layer 1a is identity data only. (The `CatalogInvariantsTests` discipline from 05-31, extended to the manifest.)
- **Reversibility stays code-enforced in layer 3.** The manifest never describes how to enable/disable a mod. It can't; those fields don't exist in the schema.

The threat that remains, named honestly: a compromised manifest repo could ship a `modPath` pointing somewhere unexpected. The forbidden-paths gate is the backstop — it already refuses traversal and game-owned paths for *user* archives, and the manifest gets no exemption. Defense in depth: signature + gate, neither trusted alone.

## 7. Testing

- **CamelCase round-trip** for `GameManifest`/`GameManifestEntry` — the string-contains assertion that protects the convention (per the rule), not just a deserialize check.
- **Facade parity golden tests** — the Phase 0 embedded manifest must reproduce *exactly* today's `KnownEngines.ByAppId`, `NexusDomains.ByAppId`, and `PopularGames.All` answers. This is the proof that Phase 0 changed nothing. Generated by snapshotting current outputs before the refactor, asserting after.
- **Validator tests** — unknown engine skipped (not fatal) and reported; absolute `modPath` refused; `..` traversal refused; binary-carrying entry refused; `schemaVersion`/`minBinaryVersion` too-new → fall back to embedded.
- **Signature tests** — valid signature accepted; tampered bytes refused; missing signature refused; all fall back to embedded, none throw to the user.
- **Miner snapshot tests** — fixture copies of each upstream source format (Ludusavi YAML, Vortex extension metadata, MO2 basic-games) → expected normalized entries. Pins the parser against upstream format drift.
- **`CatalogInvariantsTests`** extended to the embedded manifest: unique ids, valid engine keys, relative-only paths, no binary URLs.

## 8. The miner & the feed (separate repo: `626-game-manifest`)

A new public repo: the schema, hand-curated overrides, the generated manifest, and signing CI. The launcher repo only ever consumes the signed output (and bakes a snapshot as the embedded fallback).

Pipeline:

1. **Fetch sources** — Ludusavi manifest (YAML; already trusted for save detection), Vortex extension metadata, MO2 basic-games definitions.
2. **Normalize** — each source → candidate `GameManifestEntry`s, tagged with `provenance.sources`.
3. **Merge** — `overrides/` (hand-curated corrections) win every conflict. Curation = reviewing the merge diff.
4. **Validate** — engine known to the *current* schema, fields sane, paths relative. (The app re-validates anyway; this is the early gate.)
5. **Emit** — the manifest + a human-readable diff report (what changed since last run, which sources claimed each new entry).
6. **Sign** — CI signs the canonical bytes; private key in an Actions secret.

**Licensing — facts only, never code (resolved 2026-06-13, high confidence; not legal advice).** Source licenses confirmed from the actual repos: Ludusavi manifest is **MIT** (© 2020 Matthew T. Kennerly), Vortex is **GPL-3.0**, MO2 `modorganizer-basic_games` is **MIT** (© 2020 Holt59), and PCGamingWiki content (which Ludusavi derives from) is **CC BY-NC-SA 3.0 Unported** (not 4.0). The load-bearing point that clears all of them: under *Feist v. Rural Telephone*, 499 U.S. 340 (1991), the data we mine — Steam/GOG/Epic app IDs, game names, engine keys, mod-folder paths, Nexus slugs — are **uncopyrightable facts**; the US has no sweat-of-the-brow or sui-generis database right. Copying individual facts into our own separately-authored schema engages none of these licenses (GPL copyleft governs expression, not facts; CC NC/SA never attaches to facts). **Two guardrails make this hold, both already in scope:** (1) facts only — never copy code, prose, fix-instructions, or a source's selection/arrangement wholesale; (2) cross-verify each datum against a second primary source (Steam store/SteamDB for IDs, the game's own install tree for paths) so the manifest's arrangement is demonstrably our own curation, neutralizing any thin-compilation-copyright claim. Courtesy NOTICE entries for all four (Ludusavi, Vortex, MO2, PCGamingWiki) per honor-the-builders — not legally required for facts, but the decent move. See §11 #6 for the one maintainer judgment call (pre-publish sign-off).

## 9. Phases

- **Phase 0 — this repo, no network.** Schema + `Manifest/` Core module + `EmbeddedManifestSource` *generated from today's hardcoded data* + facades over `KnownEngines`/`NexusDomains`/`PopularGames` + all of Section 7's tests except signature/miner. Pure refactor; facade parity golden tests prove zero behavior change. **This is the only phase that touches the shipping app's behavior surface, and it touches it to change nothing.**
- **Phase 1 — the feed.** The `626-game-manifest` repo + miner + remote fetch (App layer) + signature verify (Core) + `minBinaryVersion`/schema gating + a settings toggle ("Update game definitions automatically," default on). New game on a known engine = data PR, zero app release.
- **Phase 2 — engine depth, data-driven.** The miner's coverage report ranks engines by games-blocked. Add 2–3 engines per release (engines stay code — layer 3 law). Likely early targets, pending the coverage data: REDmod/Cyberpunk-native, IL2CPP Unity variants, RenPy, Paradox/Clausewitz.
- **Phase 3 — agentic tail + promotion.** Wire the 2026-05-24 agentic game-profiles flow for manifest misses (unknown game → user's AI generates a profile → app validates + imports). Add a promotion path: a validated user profile becomes a pre-filled PR into the manifest repo. The long tail feeds the head.

## 10. Non-goals — do not build in this roadmap

- **No remote channel for layer 1b/2 (install-affecting catalogs).** `KnownDirectInjectMod`, `KnownFramework`, `ToolCatalog` stay compiled C# — exactly as 05-31 ruled. This roadmap does not touch them.
- **No moving layer 3 (engine enable/disable logic) toward data — ever.** The manifest schema has no field that could express it.
- **No GOG/Epic/Game Pass *probing* in this roadmap.** The schema is store-agnostic so they slot in later without migration; the discovery code is Steam-only for now.
- **No key rotation / HSM story.** One signing key in an Actions secret. Rotation is a Phase 1+ operational concern, designed-for (key pinned in binary means rotation = a release), not built now.
- **No in-app "your game list is N days stale" nudge.** Same real-but-separate UX gap 05-31 flagged for portable/dev builds. Log as a smoke/UX item if it bites; not part of this distribution decision.

## 11. Open questions for planning

1. ~~**Signing algorithm + .NET 10 API.**~~ **RESOLVED (2026-06-13):** ECDSA P-256 + SHA-256, verify pure in Core, public key pinned as `SubjectPublicKeyInfo` bytes, private key in a CI Actions secret. Ed25519 isn't first-class until .NET 11. See Section 6.
2. ~~**PCGamingWiki/Ludusavi license terms.**~~ **RESOLVED (2026-06-13):** facts (app IDs, names, engine keys, paths) are uncopyrightable (*Feist*); mining them into our own schema engages none of the source licenses. Facts-only + multi-source-verify guardrails hold it. See Section 8. (Residual judgment call moved to #6.)
3. **Manifest size & fetch cadence.** Hundreds of entries — confirm the embedded snapshot + remote-delta stays within the cheap-delta envelope, and that 24h is the right poll (it is for app updates; game-def freshness may want different).
4. **`featured` curation.** Who/what sets quick-pick rank — hand-curated in `overrides/`, or a popularity signal from a source? (Lean hand-curated to start.)
5. **Graceful-skip vs fail-loud for malformed popular entries (Phase 1 hardening).** Phase 0's `PopularGames.Build` projects popular-games entries with null-forgiving `!` on engine/modPath/steamAppId, relying on `ManifestInvariantsTests` to fail loudly at CI time if an entry is missing one. That's correct for embedded data (the test gates every change). But once the miner feeds entries — and especially once a *remote* manifest can deliver them post-release — a malformed entry would `NullReferenceException` at static init instead of being skipped. Decide in Phase 1: keep the test-time invariant as the sole guard, or add a runtime `.Where(...)` graceful-skip in the projection for miner/remote-sourced data. (Surfaced by the Phase 0 completeness critic, 2026-06-12.)
6. **Pre-publish licensing sign-off (maintainer call, gates *publishing* the feed — not building it).** The facts-only posture is engineering-strong, but two judgments are the maintainer's, not the agent's: (a) commercial-risk sign-off that we never lift CC-covered *expression* (vs bare facts) — eased by the app being free/not-sold, but a free app from a commercial-leaning project can still read as commercial under CC's NC test, so the real protection is the facts-only discipline, not the price; (b) PCGamingWiki blocks automated fetching (HTTP 403) — that's a Terms-of-Service/access question separate from copyright, so prefer light, hand-curated, primary-source-verified pulls over bulk scraping. This gate blocks *publishing the manifest repo*, not building the in-app trust core. Optional: route to the dashboard Architect for a strategic read and/or counsel before the feed goes live. Also verify the PCGamingWiki 3.0 license in-browser (the footer couldn't be fetched; 3.0 confirmed via Wikipedia + Wikidata).
