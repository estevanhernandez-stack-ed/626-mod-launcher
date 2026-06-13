# Game-manifest feed — go-live runbook

Operational runbook for taking the remote game-definition feed from "machinery ready" to "games update from a live feed." Companion to the roadmap spec (`docs/superpowers/specs/2026-06-12-game-manifest-roadmap-design.md`). Living doc — update as steps land.

## Where we are

The in-app machinery is complete and merged: the manifest behind facades (Phase 0), the trust core that verifies/loads/merges a remote manifest, the facade wiring + `SetRemote` hook, the pinned ECDSA P-256 public key, and `ManifestLoader.TryApplyRemote` — the one-call entry point (verify against the pinned key → validate/gate → make effective, fall back to embedded on any failure). The app can fully ingest a signed manifest; nothing produces or serves one yet.

## The sequence (dependency order)

Each step is tagged **[you]** (maintainer action) or **[me]** (agent builds it).

| # | Step | Who | Depends on |
|---|---|---|---|
| 1 | App-side `RemoteManifestSource` (fetch → cache → `TryApplyRemote` at startup) + "auto-update definitions" toggle | **[me]** | nothing — buildable now, ships dark (no URL = no-op) |
| 2 | Feed miner (C# in-repo tool: Ludusavi/Vortex/MO2 → draft `games-manifest.json` + diff) | **[me]** | nothing — produces an unsigned draft |
| 3 | Review the draft manifest | **[you]** | step 2 |
| 4 | Pre-publish legal sign-off | **[you]** | can decide anytime |
| 5 | CI signing (Actions secret + signing step) | **[you] + [me]** | the keypair (done); the feed repo (step 6) |
| 6 | Publish the feed + wire the real URL | **[you] + [me]** | steps 3–5 |
| 7 | Flip the toggle default + ship a release | **[me]** | step 6 |

**Buildable right now with zero gating:** steps 1 and 2.

## Your actions, in detail

### Action — Pre-publish legal sign-off (decide anytime)

**Not legal advice.** The research (2026-06-13, cited in spec §8) established as **fact**: the data we publish — Steam/GOG/Epic app IDs, game names, engine keys, mod-folder paths, Nexus slugs — are **uncopyrightable facts** under *Feist v. Rural*, 499 U.S. 340; the US has no sweat-of-the-brow or sui-generis database right. Mining these facts into our own schema engages **none** of the source licenses (Ludusavi MIT, Vortex GPL-3, MO2 MIT, PCGamingWiki CC BY-NC-SA 3.0).

Two conditions keep that true, and both are already our design:
1. **Facts only** — never copy prose, fix-instructions, or a source's selection/arrangement wholesale.
2. **Cross-verify** each datum against a second primary source (Steam store/SteamDB for IDs, the game's own install tree for paths) so the manifest's arrangement is demonstrably our own curation.

**The judgment that's yours:** are you comfortable publishing on that basis for a **free, not-sold** app? Caveats: if the app is ever sold, re-open the NonCommercial analysis; PCGamingWiki blocks automated fetching (HTTP 403) — that's a Terms-of-Service/access matter *separate* from copyright, so we hand-curate, never bulk-scrape. Optional belt-and-suspenders: a one-paragraph counsel read before the feed goes live.

**Decision:** sign off (proceed facts-only) · counsel read first · defer.

### Action — Feed repo decision (decide anytime)

Where the signed feed lives:
- **Separate public `626-game-manifest` repo** (recommended) — clean separation: public schema + hand-curated `overrides/` + the generated manifest + signing CI. The spec's design; keeps the data side independent of the launcher release cycle.
- **Release asset on this repo** — zero new repo, simplest, but mixes feed data into launcher releases and couples the two cycles.

**Decision:** separate repo · release asset.

### Action — CI signing secret (doable now, or when the signing step lands)

When ready, add the private key as a GitHub Actions secret:
1. GitHub → this repo → **Settings → Secrets and variables → Actions → New repository secret**.
2. Name it exactly **`MANIFEST_SIGNING_KEY`**.
3. Paste the full PKCS#8 PEM (the `-----BEGIN PRIVATE KEY-----` … `-----END PRIVATE KEY-----` block) from your password manager.
4. Save.

It pastes straight from the password manager into GitHub (encrypted at rest) and never touches disk again. **Timing:** harmless to set now — it sits unused until the signing CI step exists — or do it together when that step lands. The signer (a C# step using `ECDsa` + `DSASignatureFormat.IeeeP1363FixedFieldConcatenation`, matching the verify side) reads it from the secret; it never appears in source.

### Action — Review the draft manifest (when the miner runs)

Once the miner emits the draft `games-manifest.json` + diff report, skim for:
- engine classifications correct (a wrong engine = wrong mod handling);
- `modPath` sane and **never** absolute or `..`-bearing (the validator rejects those, but eyes help);
- store IDs / Nexus slugs plausible;
- anything that belongs in the curated `overrides/` (where your corrections beat the mined value).

This is the curation gate — your review is the quality bar before anything gets signed.

## Key rotation (reference)

The public key is pinned in `ModManager.Core` (`ManifestSigningKey.PublicKeySpki`). Rotation = mint a new ECDSA P-256 keypair, update the `MANIFEST_SIGNING_KEY` secret, re-pin the new public key, and ship a release. Because the key is pinned in the binary, rotation is a release — there is no separate revocation channel (matches the spec's "no key-rotation machinery" non-goal).

## After go-live

A new game on an engine the binary already knows is a **data PR** to the feed — no app release. A new *engine* is still code (a release), per the layer-3 law. Freshness without a release was the whole point.
