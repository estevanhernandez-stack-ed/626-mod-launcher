# MCP `list_mods` — unified Core mod-listing resolver — Design

**Status:** Approved design, ready for implementation plan. Fixes [#86](https://github.com/estevanhernandez-stack-ed/626-mod-launcher/issues/86).

**Goal:** Make the agent-access MCP report the same mods the launcher's UI shows — across every engine — by extracting a single read-only Core resolver that both the App and the MCP enter through. Kill the second source of truth that let `list_mods` silently return `[]` for direct-inject (FromSoft) games.

**Branch:** to be cut as `fix/mcp-list-mods-direct-inject` off `master` (this design doc lands first; see *Out of scope* for sequencing).

---

## The problem

`list_mods elden-ring` returns `{ "mods": [] }` while the game visibly has Seamless Co-op + Elden Mod Loader (and its DLL mods) installed and tracked by the launcher. Silent-empty is the worst failure mode: an agent concludes "no mods" and is confidently wrong.

Found while verifying the Phase-1 read tools on `feat/agent-access-mcp-read` against the live registry (12 games; elden-ring carries the real Seamless + EML install). `list_games`, `get_active_game`, `get_mod_context`, and the `unknown_game` error path all passed; only `list_mods` under-reported.

### Root cause

`ModTools.ListMods` calls **only** `Scanner.BuildModListAsync`, which walks the registry's `modLocations` (folders / pak files / BepInEx plugins) + the disabled holding folder. The App, however, lists mods through **three** engine-specific worlds — [`MainViewModel.ReloadModsAsync`](../../../src/ModManager.App/ViewModels/MainViewModel.cs):

| World | Predicate | App source | In Core today? |
| --- | --- | --- | --- |
| ME2 / config-backed | `ModEngineService.IsConfigBacked` | `ModEngineService.ListMods` | parser yes (`ModEngine2Config`), glue no |
| Direct-inject (FromSoft) | `!ConfigBacked && DirectInjectService.Applies` (`engine == "fromsoft"`) | `DirectInjectService.List` | detection yes (`DirectInject`), glue no |
| Everything else | fallthrough | `Scanner.ListWithClassAsync` | yes |

The MCP reproduces only world 3 — and even that imperfectly: it calls bare `BuildModList`, not `ListWithClass`, so it skips the classification step that fills `Class`. For elden-ring (`engine == "fromsoft"`, no ME2 config) the App takes world 2; the MCP has no world-2 path, so it returns the empty world-3 result.

The direct-inject and ME2 listing glue lives App-side (`src/ModManager.App/Services/`), which the Core-only MCP cannot reach. `list_mods` is a second, lossier source of truth. The original [agent-access sketch](2026-05-26-agent-access-design-sketch.md) promised `list_mods` would return "enabled/class/loader/variant/metadata"; the implementation delivered only the Scanner subset.

---

## Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Scope | **Close the whole seam** (all 3 worlds), not just direct-inject | The bug is a symptom of a duplicated dispatch; fixing only direct-inject leaves the identical ME2 gap latent for the next person. |
| Output depth | **Add metadata enrichment** — `displayTitle`, `author`, `sourceUrl` | An agent can name the mod properly and link to its page, not just echo a detection key. |
| Where the resolver lives | **A — dedicated `ModListing` Core type** | One bounded unit, testable in isolation; keeps `Scanner` (~61KB) from growing; mirrors the `RegistryStore` "one reader" extraction (commit `55a1881`). Rejected: folding onto `Scanner` (god-object pressure); dispatching inside `ModTools` (recreates the bug). |
| Side effects | **Resolver is strictly read-only**; the two writes stay explicit App-side | The MCP is read-only by law. `ListWithClass` hides two writes (`SaveClassification`, and `MigrateDataDir` in its caller) — those must not enter a read tool. |

---

## Architecture

A new Core front door both consumers call; the listing logic itself is already in Core in every world — what moves is thin glue + the by-engine dispatch.

```text
                 ┌─────────────────────────────────────────────┐
   App ─────────▶│  ModListing.Resolve(GameEntry)  [NEW, Core]  │◀──── MCP
 (MainViewModel) │  1. ctx = Scanner.GameContext(game)          │  (ModTools.ListMods)
                 │  2. dispatch by engine ↓                     │
                 │  3. Metadata.MergeMetadata(list, meta)        │
                 └───────────────┬─────────────────────────────┘
                                 │ dispatch (ME2 → direct-inject → scanner)
            ┌────────────────────┼─────────────────────────┐
            ▼                    ▼                          ▼
  ModEngine2Listing.List  DirectInjectListing.List   Scanner.BuildModList
  [NEW Core glue]         [NEW Core glue]            + Scanner.ClassifyInMemory
  config-backed?          engine == fromsoft         [refactored, read-only]
```

**New Core files (3):**

- `src/ModManager.Core/ModListing.cs` — `public static IReadOnlyList<Mod> Resolve(GameEntry game)`. The by-engine dispatcher + metadata merge. Synchronous (all listing is sync file I/O); the MCP keeps its `async` signature via `Task.FromResult`, the way `Scanner.BuildModListAsync` already wraps `BuildModList`.
- `src/ModManager.Core/DirectInjectListing.cs` — relocated from `DirectInjectService`: `Applies`, `List`, and shared folder helpers `PlayFolder`, `Holding`, `Enabled` (`public`), `Row`, `Names` (`private`). Pure `System.IO` + Core (`DirectInject`, `Scanner.DataDirForGame`).
- `src/ModManager.Core/ModEngine2Listing.cs` — relocated from `ModEngineService`: `IsConfigBacked`, `List`, `ReadConfig` (`public`). Sits on the already-in-Core `ModEngine2Config.ParseMods`.

**What stays App-side (deliberately):** every write/UI op — `DirectInjectService.SetEnabled/Install/Plan/Execute/SeamlessNeedsLauncher/SeamlessFullyInstalled/AnyActiveProxyDll`, `ModEngineService.SetEnabled/SetAll/Reorder/Remove`. They keep living at the App boundary but call the relocated Core helpers instead of holding private copies, so there's no duplicated folder/config-resolution logic.

The boundary test: after this, "what is the mod list for game X?" has exactly one answer-producer (`ModListing.Resolve`), and any one world's detection can change without touching the other two consumers.

---

## Data flow

**`ModListing.Resolve(GameEntry game)` — the whole body:**

```text
1. ctx  = Scanner.GameContext(game)
2. raw  = dispatch by engine:
     if   ModEngine2Listing.IsConfigBacked(game) → ModEngine2Listing.List(game)
     elif DirectInjectListing.Applies(game)      → DirectInjectListing.List(game)
     else                                        → { l = Scanner.BuildModList(ctx);
                                                       Scanner.ClassifyInMemory(ctx, l); l }
3. return Metadata.MergeMetadata(raw, Scanner.LoadMetadata(ctx))   // merge once, uniformly
```

No `SaveClassification`, no `MigrateDataDir`. Pure read.

**Dispatch order is load-bearing.** ME2 first, then direct-inject, then scanner — mirroring `MainViewModel`'s `if (ConfigBacked) … else if (directInject) … else`. The App's `DirectInjectBacked` predicate is `!ConfigBacked && Applies`; the `!ConfigBacked` half is enforced *by the if/else-if ordering* here, so `DirectInjectListing.Applies` only carries the `engine == "fromsoft"` test.

**Why enrichment hits (correctness linchpin).** `MergeMetadata` keys on `mod.Base` then `mod.Name`. Both listing helpers set `Base = Name`, and the detection names (`"Seamless Co-op"`, `DirectInject.LoaderName == "DLL mod loader"`) are the exact keys in elden-ring's `metadata.json` (verified in the audit). So Seamless resolves to `DisplayName = "Seamless Co-op (Elden Ring)"`, etc. Unrecognized mods fall back to `Prettify(name)` — `DisplayName` is never empty.

**Inherited behavior to lock in a test:** for elden-ring, `list_mods` returns **Seamless Co-op + the individual EML-loaded DLL mods** (AdjustTheFov, RemoveVignette, …), *not* a single "Elden Mod Loader" row. That's `DirectInjectListing.Enabled`'s rule — when the loader's `mods\` folder has contents, the bare loader row is dropped and represented by its contents (exactly what the App shows). EML-as-a-framework is tracked separately (the `frameworks/` folder / missing-framework banner), not as a mod row.

---

## MCP output contract (`list_mods`)

Per-mod object — current 5 fields + 3 enrichment fields:

```jsonc
{
  "name":         "Seamless Co-op",              // m.Name — detection key, stable
  "displayTitle": "Seamless Co-op (Elden Ring)", // m.DisplayName — always present post-merge
  "enabled":      true,                          // m.Enabled
  "class":        "CO-OP",                       // m.Class — chip (CO-OP / GRAPHICS / DLL / "both" for ME2…)
  "location":     "direct-inject",               // m.Location — direct-inject / mod engine 2 / mods / …
  "loader":       null,                          // m.Loader — null for direct-inject & ME2; "ue4ss"/"bepinex" elsewhere
  "author":       "Yui",                         // m.Author
  "sourceUrl":    "https://www.nexusmods.com/eldenring/mods/510"  // m.ModUrl
}
```

- `displayTitle ← Mod.DisplayName`, `author ← Mod.Author`, `sourceUrl ← Mod.ModUrl` (the mod's own page; `Mod.Source` — provenance — is intentionally not surfaced).
- **Null fields are omitted**, matching observed tool behavior (windrose's null `engine` did not appear in `list_games`). Absent = unknown.
- camelCase keys (consistent with every other tool output; the on-disk camelCase *rule* does not govern MCP wire output, but we match the convention).
- `description` / `image` / `category` / `downloads` are populated on the `Mod` post-merge and trivially addable later — out of scope now (YAGNI).

---

## App refactor + the read-only wrinkle

**The wrinkle:** `ListWithClass` (the scanner branch) carries two writes — `SaveClassification` (best-effort, line ~610) and `MigrateDataDirAsync` (in its caller `ReloadFromScannerAsync`). It also already does the metadata merge internally. If `Resolve` called `ListWithClass`, `list_mods` would mutate the user's disk — violating the read-only contract.

**Resolution — resolver strictly read-only; the two writes stay explicit App-side:**

- Factor a read-only primitive `Scanner.ClassifyInMemory(ctx, mods)` — seeds classification + sets `Class`/`Base`/`Variant` in memory, **no `SaveClassification`**. `ListWithClass` keeps its current behavior by becoming `ClassifyInMemory` + `SaveClassification`. Keeping `ListWithClass` intact is **required**, not just convenient: it has callers beyond `ReloadFromScannerAsync` that want the write — two internal `Scanner` callers (`Scanner.cs:569`, `Scanner.cs:1081`) and a test (`ScannerCoreTests.cs:44`). The refactor must leave `ListWithClass`'s contract byte-identical; only `Resolve` (the new read path) skips the persist.
- `Resolve` uses `BuildModList` + `ClassifyInMemory` for the scanner branch — no writes.

**`MainViewModel.ReloadModsAsync` — the two writes become explicit boundary steps, gated to scanner-world exactly as today:**

```csharp
if (!ConfigBacked && !DirectInjectBacked)
    await Scanner.MigrateDataDirAsync(_ctx);              // write — scanner world only, as before
var list = ModListing.Resolve(_ctx.Game);                 // shared read path (replaces lines 286-297)
if (!ConfigBacked && !DirectInjectBacked)
    Scanner.PersistClassification(_ctx, list);            // write — persists the seed, as before
```

`Scanner.PersistClassification(ctx, mods)` is a new tiny Core helper that runs the **exact** persist step `ListWithClass` does today — `SaveClassification(ctx, Classification.Seed(LoadClassification(ctx), mods.Select(m => (m.Name, m.OnServer))))`. It re-seeds rather than reconstructing a dictionary from `m.Class`, so the on-disk result is byte-identical to the current behavior regardless of `Classification.Seed`'s keep/prune semantics — no guesswork, no silent change. (The extra seed pass is cheap and deterministic.) Everything downstream of `list` (rows, chips, framework banner) is untouched. **Net displayed behavior: identical, and `classification.json` writes are identical.** The writes the App always did, it still does — lifted to where they are visibly writes.

**The two App services slim down** (listing guts → Core; write ops stay, repointed at the Core helpers): `DirectInjectService.Applies` becomes a one-line delegation; `List`/`Enabled`/`Row`/`Names`/`PlayFolder`/`Holding` move out. `ModEngineService.IsConfigBacked` delegates; `ListMods`/`ReadConfig` move out.

---

## Error handling & edge cases

- **Unknown game id** → `Find(gameId)` null → existing `unknown_game` error shape. Unchanged.
- **Missing/nonexistent game folder** → `PlayFolder` null → `Enabled(null)` empty → only holding-folder disabled mods (possibly empty). No throw (`Names` wraps enumeration in try/catch).
- **ME2 config absent/unreadable** → `IsConfigBacked` false (it `File.Exists`-checks) → falls through. Backed-but-unreadable → empty list, graceful.
- **metadata.json missing/corrupt** → `LoadMetadata` returns empty map; `MergeMetadata` null-coalesces. Mods still list; enrichment fields omitted.
- **Registry missing/corrupt** → `RegistryStore.Load` returns empty registry (never throws) → `unknown_game`.
- **Read-only under failure** → resolver has no write path; no partial-write risk. App writes stay best-effort (`try/catch`).
- **App open while MCP runs** → both read the same files; MCP never writes, so no new race. App writes remain atomic (`AtomicJson`).
- **Un-migrated install (documented divergence)** → the MCP won't trigger the migration the App would, so it reflects current disk without fixing it. Correct for a read tool; a known, acceptable edge.

---

## Test plan

Project law: every Core behavior change starts with a failing xUnit test; `CorePurityTests` stays green.

**Core — `tests/ModManager.Tests/ModListingTests.cs` (new):**

1. **Direct-inject happy path (the #86 regression)** — fixture: temp `Game\dinput8.dll` + `Game\mods\AdjustTheFov.dll` + `Game\SeamlessCoop\` + `ersc_launcher.exe` + holding folder + `metadata.json` keyed `"Seamless Co-op"`. Assert Resolve returns Seamless Co-op + the loader-loaded mod, **no bare "DLL mod loader" row**, correct enabled flags, `DisplayName`/`Author`/`ModUrl` enriched.
2. **Loader-contents rule** — `mods\` populated → bare loader row dropped; `mods\` empty → loader row present.
3. **ME2 config-backed** — fixture TOML, 2 mods (1 on / 1 off) → Location `"mod engine 2"`, Class `"both"`, priority order preserved.
4. **Scanner-world (bepinex) no-regression** — plugins folder → returns plugins, `Class` populated, merged.
5. **Read-only guarantee (law enforcement)** — snapshot temp dir before/after Resolve on a scanner-world game; assert `classification.json` not created/modified and data dir not migrated.
6. **Empty/missing folder** → empty list, no throw. **metadata.json absent** → lists, enrichment omitted.
7. **`ClassifyInMemory` extraction** — sets `Class`/`Base`/`Variant`, writes nothing; `ListWithClass` still writes (preserve existing contract).

**MCP — `tests/ModManager.Tests/Mcp/ReadToolsTests.cs` (extend):**

8. **`ListMods_fromsoft_returns_direct_inject_mods`** — seed registry + fromsoft fixture; assert JSON contains the Seamless name, `displayTitle`, `author`, `sourceUrl`, enabled flags. Mirrors the live failure end-to-end.
9. **`ListMods_marshals_enrichment_fields`** — correct camelCase keys; null fields omitted. Keep the existing `unknown_game` test.

**Shared fixture:** a `SeedFromSoftGame(...)` helper (extends the existing `TestSupport.TempDir` pattern), reused by Core + MCP tests.

**App:** no new unit tests (thin shell). Add a `docs/smoke-tests/pending.md` entry — "open elden-ring in the App, confirm the mod list is identical after the refactor."

**TDD order (red → green):** (1) MCP fromsoft test → RED (reproduces `[]`) → (2) Core direct-inject test → RED → (3) build `DirectInjectListing` + `ModEngine2Listing` + `ModListing.Resolve` + `ClassifyInMemory` → (4) point MCP at `Resolve` → MCP green → (5) refactor App + full suite + CorePurity + manual smoke → (6) read-only + edge tests green.

---

## Acceptance criteria

- [ ] `list_mods elden-ring` returns Seamless Co-op + the EML-loaded DLL mods (no bare-loader row when contents exist) with correct `enabled`/`class`/`location` and `displayTitle`/`author`/`sourceUrl`, matching what the App shows.
- [ ] A bepinex game still returns its plugin list — no regression.
- [ ] An ME2 config-backed game returns its config mods (priority order preserved).
- [ ] `list_mods` performs **zero** disk writes (read-only guarantee test passes).
- [ ] The App's displayed mod list is unchanged for all three worlds (manual smoke + existing suite green).
- [ ] `CorePurityTests` green — no WinUI/WinRT pulled into Core during the extraction.
- [ ] App and MCP demonstrably share one listing path (`DirectInjectService.List` / `ModEngineService.ListMods` are gone, not duplicated).

---

## Costs & benefits

**Benefits:** kills #86 for real; one source of truth (App + MCP cannot drift); closes the latent ME2 gap *and* the scanner-world `Class` gap, not just direct-inject; makes the read-only law enforceable (resolver provably side-effect-free); enrichment gives agents usable identity + links; Phase-2 write-tools build on a truthful read path; direct-inject/ME2 listing becomes Core-testable headless for the first time.

**Costs:** touches the main read path (regression risk to the App's displayed list — mitigated by zero-displayed-change discipline + smoke); the migrate/save-classification gating must stay exact or it *is* a behavior change; `ListWithClass` refactor must preserve all existing callers; real fixture-building effort (the bulk of the work); a documented MCP/App divergence on un-migrated installs; slightly more Core public surface; a bigger PR spanning Core + App + MCP + tests.

Bottom line: production-code change is modest and mostly relocation; the spend is care on the App read path and fixture-building. Worth paying before Phase 2 leans on this.

---

## Out of scope / future

- Write tools (`set_mod_enabled`, `intake`, …) — Phase 2.
- Surfacing `description`/`image`/`category`/`downloads` in `list_mods` — trivial later if an agent needs it.
- Moving the App services' *write* ops into Core — unrelated refactor; not done here.
- Sequencing: this design assumes the `feat/agent-access-mcp-read` read-tools land first; the fix branch (`fix/mcp-list-mods-direct-inject`) builds on the merged read surface.
