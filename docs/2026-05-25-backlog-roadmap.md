# Backlog roadmap — post-merge wave (2026-05-25)

- **Date:** 2026-05-25
- **Status:** Sequencing approved with Este. Each item below gets its own spec → plan → build
  cycle; this doc is the ordered index. Phase A entries are detailed enough to build from; Phase
  B–D entries are shaped with the open design questions flagged for their own deep brainstorm.
- **Context:** Drawn from a live Windrose modding session that exposed real bugs + feature needs.
  Already shipped + merged this wave: mod-update (collision-aware intake, PR #1) and agentic game
  profiles (PR #2). Everything below is the *next* wave.
- **Sizing:** `S` one sitting · `M` a focused build · `L` its own multi-day feature.

## Phase A — Correctness (fix what's shipped before building on it)

### A1. Load-order mirror sync (+ SP/MP naming parity) · `S` · ready

**Problem:** `Scanner.ApplyLoadOrder` (Scanner.cs ~463-470) and `ResetLoadOrder` (~479-485) apply
the UE-pak load-order prefix (`0010__Name.pak`) to files in the **primary location only
(`loc.Abs`)** — never `loc.Mirrors`. Result: primary gets `0010__Foo_P.pak` while the mirror keeps
bare `Foo_P.pak`. Same mod, different filenames across SP and the MP/server-build mirror. Then
`disable`/`delete` (which match by filename, and *do* handle mirrors at Scanner.cs:272/316-356)
look for `0010__Foo` in the mirror, miss the bare `Foo`, and strand it. This is the exact Windrose
desync — stale mods left in `R5\Builds\WindowsServer\...` that broke co-op hosting.

**Fix:** in both `ApplyLoadOrder` and `ResetLoadOrder`, apply the identical `File.Move` to every
`loc.Mirrors` path, not just `loc.Abs`. Keeps primary + mirrors in filename lockstep, so SP/MP
parity is automatic and disable/delete work. (SP/MP naming parity folds in here — same root cause.)

**Test (test-first, Core):** apply order to a mod that has a mirror → both primary and mirror files
get the prefix; reset → both stripped; then disable → removed from both. Build off master.

### A2. Folder-drop flatten · `S` · ready

**Problem:** `DirectInject.Plan`/`Install` (and the Scanner equivalent) flatten a *zip's* single
wrapping folder via `WrapperPrefix`, but a dropped **folder** is prefixed with the folder's own
name (`baseName`) — so dragging an extracted mod folder nests everything under
`SomeMod v1.2\…` and nothing matches the install layout (the "drop did nothing" Este hit).

**Fix:** treat a dropped folder like a zip — strip a single redundant wrapper folder instead of
prefixing with its name, so its *contents* install relative to the target. Apply to both the
direct-inject and standard intake folder branches; reuse the `WrapperPrefix` logic.

**Test (test-first, Core):** drop a folder whose contents map to the mod layout → entries land at
the right relative paths (not nested under the folder name); a multi-top-folder drop is not
wrongly flattened.

## Phase B — Daily-use wins (small/medium, high-leverage)

### B3. Readme viewer · `S/M`

**Need:** mods ship `README`/`.txt`/`.md` (and CurseForge carries descriptions) with install rules,
MP-compat notes, and settings (e.g. the Seamless password, the save-mod Local-not-Cloud rule).
Surface them in-app. **Shape:** detect a readme in the mod's files (or reuse the CF description the
app already fetches); a "Readme" affordance on the mod row opens it (textContent only — mod strings
are attacker-controlled). Feeds B4, C5, and save-mods. **Open Qs (for its brainstorm):** in-app
viewer vs open-externally; markdown render vs plain text; per-mod-file vs per-mod when a mod has many.

### B4. Launch-enforcement (required launcher) · `M`

**Need:** a mod that requires its own launcher (Seamless Co-op → `ersc_launcher.exe`) must not be
silently launchable the wrong way; when present, that launcher becomes the Play target and the
vanilla launch is gated. **Shape:** the `requiredLauncher` field already ships on `GameEntry` (from
the agentic profile). Wire enforcement: when an enabled mod declares a required launcher, surface it
as the default Play target + warn/gate the vanilla path. **Open Qs:** hard-gate vs warn; per-mod
required launcher vs per-game; how it interacts with the existing `LaunchTargets` + the Seamless
"needs launcher" hint that already exists.

## Phase C — Mod intelligence

### C5. MP-safety flags · `M`

**Need:** know which mods are MP-safe vs SP-only so co-op doesn't break (the Windrose lesson).
**Shape:** infer risk from the mod's class (data/gameplay = MP-risky; cosmetic/client = MP-safe),
let the user tag a mod SP-only, and warn before a risky mod enters the MP set. Builds on the
existing **MP/SP loadout** (the lever) + B3 (readmes state compat). **Open Qs:** inference rules +
confidence; manual tag persistence (classification.json); whether to block or just warn; pulling
MP-compat from metadata (CF/Nexus) when available.

## Phase D — The big levers (large; they compose)

### D6. Nexus integration · `L`

**Need:** a 2nd metadata + file-ID source alongside CurseForge — the differentiator. Many mods
(Windrose's, repacked, save/world mods) are Nexus-only and the CF MurmurHash fingerprint misses
them. **Shape:** `NexusClient` in Core (injected `HttpClient`, mirrors `CurseForgeClient`):
search, mod details, file lookup. **md5-at-intake** (Nexus IDs files by md5, not MurmurHash) to
identify dropped mods. **Per-user Nexus key or a thin proxy — never baked into the exe** (law #2,
as CF). Merge Nexus into the curated-wins metadata; author/endorsement/**donation** fields feed
honor-the-builders. Nexus keys games by a **domain name** (`windrose`) not a numeric id → add
`nexusGameDomain` to `GameEntry` / the agentic profile. **Open Qs (own brainstorm):** key-handling
(per-user vs proxy) + rate limits; merge/precedence between CF and Nexus; how much of the Nexus API
to wrap for v1.

### D7. Save / world mods · `L`

**Need:** a new mod **class** — downloadable save/world mods (Windrose "Pirate Depot") that install
into the **save tree**, not Content/Paks. Full requirements in
[docs/2026-05-25-save-world-mods-note.md](2026-05-25-save-world-mods-note.md). **Shape:** install to
`<saveDir>\<profile>\RocksDB\<version>\Worlds\<GUID>\`; **never** touch `RocksDB_v2`/`_Backups`;
warn about the post-install Local-not-Cloud prompt; snapshot saves first (tie into the save manager,
which already snapshots this tree); offer one-click **reset-to-pristine** (re-extract original). The
install path + forbidden subfolders belong in the (agent-fillable) game profile, not hardcoded.
Benefits from D6 (Nexus IDs Nexus-hosted save-mods) and B3 (readmes carry the install rules).
**Open Qs (own brainstorm):** classify a save-mod vs a pak at drop; multi-world naming (GUID →
friendly name map); per-game generality of the save-tree structure.

## Build flow

Per item: brainstorm (for B–D, where design Qs are open) → spec → writing-plans → subagent-driven
build → mod-safety/final review → finishing-a-development-branch. Phase A items are root-caused and
can go straight to a short spec → plan → build. Independent branches off `master`; keep PRs
independent (master was just synced, so PRs show only their own commits).
