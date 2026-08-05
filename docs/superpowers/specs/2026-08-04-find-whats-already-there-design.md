# Spec A — "Find what's already there"

> Design doc. Brainstormed 2026-08-04 with Este. Spec A of two for the next release
> ("meet your setup where it is"); Spec B covers the tools/launch surfaces and safeRoute
> rendering and is written separately.

## The problem

The launcher only sees mods it installed, plus what the Scanner finds in the mod folder the
manifest names. Everything a user hand-installed before the launcher existed — dropped in the
game root, left in an odd subfolder, or extracted years ago from an archive that's long gone —
is invisible. It can't be listed, identified, or turned off. The one thing a user most wants
from a mod manager on day one ("what do I even have installed?") is the thing we don't answer.

Two existing pieces get us partway and are deliberately reused:

- `LooseModScan` — by-nature signature detection (proxy DLLs, `.asi`), already proven.
- `LooseIdentify` — Nexus name-search with a review-before-write dialog, but hard-scoped to
  loose-root games (`LooseRootListing.LooseRootLocation`), so it never helps a Bethesda `Data`
  folder or a UE `Paks` directory.

## Scope

**In:** the discovery sweep, the per-game Nexus name index, the three-tier match, the adoption
path (metadata only), and generalizing `LooseIdentify` beyond loose-root.

**Out (Spec B):** the tools section, catalog unification, launch-dropdown reorganization,
`safeRoute` rendering. **Out entirely:** drive-wide scanning, vanilla-diff file lists, fuzzy-match
tuning UI, background re-sweeps, cross-game index sharing.

## The pipeline

Five stages, one direction, review before any write:

1. **Sweep** — enumerate the game folder, plus any single path the user points at. Read-only.
2. **Classify** — decide what is plausibly a mod, using known shapes + signatures.
3. **Match** — resolve identity best-evidence-first against archives and the name index.
4. **Propose** — build one reviewable row per candidate, with honest confidence.
5. **Adopt** — on approval, write metadata only. No file is moved, renamed, or deleted.

### Stage 1 — Sweep boundaries

The game folder (recursive, depth-capped) and **one** user-pointed path per run (a Downloads
folder is the expected case — that's where leftover archives live). Nothing else. No drive
enumeration, no sibling-game guessing, no registry crawling. The sweep opens files only to hash
archives in stage 3; classification is name/extension/structure only.

Skips: the launcher's own data dir, the disabled-mods holding folder, and any folder recorded in
`taken-over.json` (another manager owns it — that's the existing `VortexTakeover` contract).

### Stage 2 — Classify (pure)

Candidates come from known shapes and signatures only, per Este's call — conservative by design:

- **Signature files** — the `LooseModScan` rules (proxy DLL names, `.asi`), reused as-is.
- **Engine-shaped locations** — the manifest's `modPath` for this game plus the engine's
  conventional mod homes; a file with an engine-typical extension (`.pak`, `.esp`, `.arc`,
  `.dzip`) sitting in one of them is a candidate.
- **Archives** — `.zip/.7z/.rar` anywhere in the swept paths are candidates regardless of
  location, because they are the highest-value evidence (stage 3, tier 1).

Anything unmatched is **invisible**, not "maybe". A game file must never be proposed as a mod;
false silence is the acceptable failure, false accusation is not.

### Stage 3 — Match, best evidence first

| Tier | Evidence | Confidence | Note |
|---|---|---|---|
| 1 | **Archive md5** → Nexus `IdentifyByHashAsync` | exact | Authoritative: Nexus hashes the *published archive*. Only reachable when an archive still exists — verified constraint, see below. |
| 2 | **Name → per-game index** | name-match | The load-bearing tier for extracted mods. Scoped to this game's domain, so it's a lookup, not cross-game guesswork. |
| 3 | **Neither** | none | Still proposed as "found, unidentified" — visible and toggleable beats invisible. |

**The md5 constraint (verified, not assumed):** Nexus md5 lookup matches the published archive's
hash. An extracted mod's loose files were never hashed by Nexus, so md5 cannot identify them —
this is why the launcher md5-identifies at drop time today (`MainViewModel.cs:1610`). Discovery
therefore leans on tier 2 for the common case and treats tier 1 as a bonus when a leftover
archive turns up.

### The per-game Nexus name index

**Purpose:** turn "is this a mod, and which one?" from a network round-trip per file into a local
lookup — and make the answer available offline once warm.

- **Shape (Core, pure):** entries of `{ modId, name, author, endorsements, updatedUtc }` keyed by
  the game's Nexus domain. Populated from `SourceSearchHit`, which already carries every field.
- **Seed:** on first connect for a game, one bounded fetch of the top **500** by endorsements via
  `IModCatalogBrowse.BrowseCatalogAsync` — the mods people actually have.
- **Grow:** every catalog page browsed, every search hit, and every update poll adds entries for
  free. No extra API calls beyond the seed.
- **Bound:** hard cap **5,000** entries per game; on overflow, drop lowest-endorsement first. The
  index is a cache, never a database.
- **Persistence (App):** `<dataDir>/nexus-name-index.json`, camelCase, written through
  `AtomicJson` — the repo's on-disk JSON law, with a round-trip test.
- **Refresh:** the seed re-runs on the existing ~24h Nexus debounce; growth is continuous.

**Matching (Core, pure):** normalize both sides (case-fold, strip version suffixes, separators,
common noise tokens), then score exact → prefix → token-overlap, returning ranked candidates
above a threshold. Reuses and extends `LooseIdentify`'s existing query cleaning rather than
inventing a second normalizer. Fully unit-testable over synthetic name sets — no network.

### Stage 4–5 — Propose, review, adopt

One row per candidate: what was found, where, what we think it is, and how sure we are. Matched
rows are checked by default; unidentified rows are unchecked and greyed but still adoptable.
Apply is the only write path; Cancel writes nothing. This mirrors `LooseIdentifyDialog` exactly
and should share its shape.

**Adoption writes metadata only** (Este's call): the mod is recorded and becomes listed,
identified, and toggleable — files stay exactly where they are. `ModMeta.SourceConfidence`
records the tier honestly (`md5` vs a name-match value), so a weak identification can never
masquerade as a strong one and the existing manual-match path can correct it. The **first file
move is the user's first toggle**, through the existing reversible move-to-holding path. Being
found never disturbs a working setup.

## Generalizing LooseIdentify

Remove the loose-root location gate so name-search identify offers itself for **any**
unidentified row. The existing candidate rules stay exactly as they are — never re-identify a
manually pinned row, never overwrite a stronger source confidence with a weaker one, never
propose loader rows. This is a scope widening, not a behavior change, and the existing tests
should be extended with non-loose-root rows rather than rewritten.

## Component boundaries

**Core (pure, no I/O):**

- `Discovery/DiscoverySweep` — classification over a supplied file listing (caller enumerates;
  same contract as `LooseModScan`).
- `Discovery/ModNameIndex` — the index data shape + the matcher.
- `Discovery/AdoptionProposal` — the reviewable row model + the adopt→`ModMeta` projection.

**App (I/O + UI):**

- `Services/DiscoveryScanService` — enumeration, archive hashing, path picking.
- `Services/ModNameIndexSource` — seed/grow via the plugin, persist via `AtomicJson`.
- `DiscoveryReviewDialog` — the review surface, modeled on `LooseIdentifyDialog`.

**Trigger:** automatic on first add of a game, manual re-run thereafter (Este's call).

## Degradation

No Nexus source (sealed Store build, no plugin, signed out, offline): the sweep still runs and
yields **tier 3** — found-but-unidentified mods that are visible and toggleable. The feature
degrades to "we found your mods" instead of vanishing. Index absent or corrupt → treated as
empty, never fatal.

## Testing

- **Core:** classifier over synthetic listings (game files must never be claimed; signatures and
  engine-shaped hits must be); matcher over synthetic name sets including near-miss and
  version-suffix cases; index bounding + trim order; adopt→`ModMeta` projection with confidence.
- **Round-trip:** the index file's camelCase shape, per the repo's JSON law.
- **Smoke:** a real sweep on Windrose (the richest test bed — 30 mods, four tools, a framework,
  years of hand-installed history), including the offline/no-Nexus path and a leftover-archive
  tier-1 hit.

## Repo laws

Reversibility holds trivially — adoption writes no files. Pure-core holds: all classification and
matching is I/O-free Core with the App supplying data, mirroring `LooseIdentify`. Nothing is
bundled: the index stores facts about mods (name, id, author), never mod content. The manifest
stays descriptive and is only *read* here.

## Risks

- **Plausible-but-wrong name matches.** Mitigated by per-game scoping (a much smaller haystack
  than cross-game), review-before-write, honest confidence recording, and the existing manual
  correction path. No file is touched on a wrong guess.
- **Sweep cost on large game folders.** Mitigated by depth cap, extension pre-filter, and hashing
  only archives. Should be measured on Windrose during smoke.
- **Seed API cost.** One bounded fetch per game per debounce window; growth is free.
