# Saves editor (Windrose MVP) — Design Spec

**Date:** 2026-05-26
**Status:** Draft (Este, autonomous mode)
**Branch:** `feat/saves-editor-windrose-mvp` (to be created off `master`)
**Memory anchors:** [[saves-editor-fromsoft]] (the architectural precedent), [[windrose-four-mod-locations]] (game context), [[ue-project-subfolder]] (saves under `R5/`)

## Why

The FromSoft save editor MVP just shipped (PRs #44 → #46). Pattern proved: pure-core format layer + `SaveEditorService` snapshot-first wrapper + Saves dialog "Characters" section. The next target is **Windrose** — Este's daily-driver UE5 game.

Windrose adds a second engine family to the editor, which is the whole point of the FromSoft work being engine-scoped from day one. Two engines = the abstraction is real, not theoretical. It also unlocks the differentiation thesis on the launcher's daily-driver game itself — save-editing on Windrose is value Este personally consumes the day it ships.

Two existing community editors confirm the format is solvable:
- **Chris971991's "Windrose Character Editor 0.2.17"** (MIT) — edits character (name, level, XP, stats, talents), inventory (32 slots + action bar + equipment quality), ships (rename, hull/cannon/sail/flag, cargo, fleet), recipes, biome discoveries.
- **WSE Project Save Editor GUI 1.3** — Python + wxPython + PyInstaller, **bundles `rocksdb.dll`**, reads/writes the save directly via RocksDB. Ships a **1.17 MB standalone HTML Item ID Database** — 1,268 items, 20 categories, 5 rarities. The catalog-as-shippable-data pattern is real and lifted-able.

We honor both. Attribution rides into the dialog + NOTICE per the keystone.

## Goal

Ship a working **Windrose save editor MVP** that mirrors the FromSoft MVP's scope: read characters, edit stats + currency + name, snapshot-first. The MVP must:

1. **Read** Windrose characters from the RocksDB store at `R5/Saved/SaveProfiles/<steamid>/`.
2. **Edit** a focused field set: name + level + XP (and/or money/gold — whichever maps cleanly; locked in Task 0) + the visible attribute stats.
3. **Write** the modified character back into RocksDB with valid key-payload structure so the game loads it.
4. **Snapshot before every edit, atomically** — same `SaveEditorService` rule, extended to be engine-aware (the snapshot is the existing whole-save-folder zip; it already covers RocksDB files).

### Explicitly out of scope (this MVP)

- **Inventory editing** — the item picker + 1,268-item catalog wiring is real work; deferred to phase 2 even though the catalog file ships in this iteration so it's already where it needs to be.
- **Ships / fleet editing** — Windrose-specific; phase 2.
- **Recipe / biome discovery editing** — phase 2.
- **Other UE games with custom save formats** — Demonologist, Witchfire, R6 Siege, Ready or Not. They each have their own format. **Not in scope.** Each gets its own adapter when its turn comes.

This MVP is **Windrose ONLY** for the format. The architectural seam (engine routing) is what makes the next UE game cheap; doing the next UE game is not part of this work.

## Approach

### Engine adapter pattern (the cross-cutting design move)

Mirror the FromSoft namespace shape exactly. The Core grows a sibling tree:

```
src/ModManager.Core/SaveEditor/
├── FromSoft/             ← exists (shipped in PR #44)
│   ├── EldenRingSave.cs
│   ├── CharacterSlot.cs
│   ├── CharacterEdit.cs
│   ├── SlotData.cs
│   └── SlotChecksum.cs
└── Windrose/             ← NEW
    ├── WindroseSave.cs              (public API — Read/Write)
    ├── WindroseCharacterSlot.cs     (read model)
    ├── WindroseCharacterEdit.cs     (write model)
    ├── WindroseKeys.cs              (RocksDB key schema — load-bearing)
    ├── WindroseCharacterPayload.cs  (per-character byte layout)
    └── ItemCatalog/
        └── windrose-items.json      (committed; out-of-scope read for MVP, attribution shipped)
```

The two adapter trees converge on **`SaveEditorService`** — which becomes engine-aware. The service is the dispatch point; the Saves dialog never branches on engine.

### `SaveEditorService` — engine routing

Today the service has two methods bound to FromSoft types directly:

```csharp
public IReadOnlyList<CharacterSlot> ReadCharacters(string savePath)
public SaveSnapshot EditCharacter(..., int slotIndex, CharacterSlot beforeEdit, CharacterEdit edit)
```

The refactor introduces a neutral character DTO at the service boundary so callers (the Saves dialog) work in one type space:

```csharp
public sealed record CharacterRow(
    int SlotIndex,
    string Name,
    string Class,                 // "Vagabond" / "—" / "Captain" / etc.
    int Level,
    long Currency,                // runes (FromSoft) / gold (Windrose)
    string CurrencyLabel,         // "runes" / "gold"
    IReadOnlyList<(string Label, int Value)> Attributes,
    string Identity);             // SteamId (FromSoft) / character GUID (Windrose)

public sealed record CharacterEditInput(
    string Name,
    long Currency,
    IReadOnlyList<(string Label, int Value)> Attributes);
```

The service dispatches on **engine** (resolved from `GameEntry.Engine` + `SteamAppId`):

```csharp
public IReadOnlyList<CharacterRow> ReadCharacters(string savePath, string? engine, string? steamAppId)
    => SaveEditorRouting.Route(engine, steamAppId) switch
    {
        SaveEditorAdapter.FromSoft => FromSoftAdapter.Read(savePath),
        SaveEditorAdapter.Windrose => WindroseAdapter.Read(savePath),
        _                           => Array.Empty<CharacterRow>(),
    };
```

Adapter classes (`FromSoftAdapter`, `WindroseAdapter`) live in `ModManager.App.Services.SaveEditorAdapters` and translate between the engine-specific Core records and the neutral DTOs. The Core stays engine-pure; the adapter glue is App-layer (it owns the neutral shape, which is a UI concern).

**Routing rules** (in order):

1. `engine == "fromsoft"` → FromSoft (Elden Ring path; today's behavior, no change).
2. `engine == "ue-pak" && steamAppId == "2399830"` → Windrose. Steam App ID is the gate; UE-pak alone is not enough (other UE-pak games have totally different save formats).
3. Anything else → `None` (the Characters section stays empty; the rest of the Saves dialog still works).

Future games join the table by adding a Steam App ID + adapter pair. The seam is built to grow.

### Save folder detection (already covered)

PR #45 wired Ludusavi + Steam-user-id resolution for Windrose. `ctx.SaveDir` already lands at `R5/Saved/SaveProfiles/<steamid>/` for the registered Windrose entry. No new work here — the Windrose adapter receives a path; it doesn't probe for one.

### RocksDB read/write (`RocksDB.NET`)

Windrose's save folder is a RocksDB store: `CURRENT`, `MANIFEST-*`, `*.sst`, `LOG`, `OPTIONS-*`. To read or write a character we open the store, look up keys, mutate, and commit back. Reverse-engineering RocksDB's on-disk format ourselves is a non-starter; we use the official `RocksDB.NET` NuGet (Apache-2.0).

**Dependency:** `RocksDB.NET` version pinned in Task 1.
- **License:** Apache 2.0 (per-user permissive — fits the launcher's bar). The native `rocksdb` library it wraps is BSD-3-Clause (Facebook).
- **Bundling:** ships native binaries (`librocksdb.dll`) per platform under `runtimes/win-x64/native/`. They land in the self-contained portable's `runtimes/` folder automatically — **zero user-installed prerequisites**, per the keystone's deps rule. (Verified pattern: `RocksDB.NET` is what WSE Project uses via PyInstaller bundling; the C# port is the same library wrapped.)
- **Binary size:** ~10 MB native `.dll` per platform. The current portable is ~120 MB; this is a measurable but acceptable bump. Worth it for the feature.
- **Honors the deps law from [[deps-policy-correction]]:** the rule is zero user-installed prerequisites, not zero deps. A bundled native that ships in the self-contained build is fine.

**Read posture:** open the store **read-only** for the read path. RocksDB's `OpenForReadOnly` doesn't grab the LOCK file, so the editor can list characters while the game is closed without leaving a lock that breaks the next game launch.

**Write posture:** open read-write, mutate the keys, close. **The game must be closed.** A lock-conflict error is the natural enforcement — surface it as: *"Close Windrose before editing. The save is locked by the running game."*

### Key schema (Task 0 deliverable — load-bearing unknown)

What RocksDB keys store character data is **the** load-bearing piece of research. Chris971991's editor source + WSE Project's HTML database give us the way in. The plan's Task 0 produces `docs/superpowers/research/2026-05-26-windrose-save-format.md` pinning:

- **Profile/character key format** — e.g. `Profile/<guid>` or `Character/<index>` etc. (recovered from Chris971991's source)
- **Per-character payload structure** — name offset, level offset, XP/gold offset, stats offsets. Likely a binary blob; possibly serialized via a known framework (FlatBuffers? Protobuf? Custom?). Task 0 confirms.
- **Ancillary keys** — index/manifest keys that point at the character list (so we know what characters exist without scanning every key).
- **Sentinel keys to leave alone** — version stamps, save-system metadata that must not be touched.

Confidence the schema is recoverable: **high**. Two open-source editors already do this — Chris971991's MIT C++ source and WSE Project's Python source. We are not the first; we are the third. The risk is *transcription* (getting the offsets right in C#), which the snapshot-first safety law absorbs.

### Foundational safety rule (locked, unchanged)

Every edit operation runs the snapshot first via `SaveManager.Backup(saveDir, snapshotsDir, label, auto: false)`. The label format:

```
before-edit: <character name> — <yyyy-MM-dd HH:mm:ss>
```

Atomic — if the snapshot fails (disk full, permission denied, game holding a file lock), the edit aborts before any RocksDB write. The snapshot is the whole-folder zip; it captures every RocksDB file. Restore re-extracts the entire folder — the store comes back bit-identical.

This is the same `SaveEditorService.EditCharacter` wrapper extended to dispatch by engine. The safety contract does not change; what changes is the engine the wrapper calls into.

### Editable fields (MVP)

Locked once Task 0 confirms the schema. Working assumption from the references:

| Field | Type | Edit? | Validation |
|---|---|---|---|
| Character name | UTF-? string (TBD by Task 0) | **write** | non-empty; length cap per format |
| Level | int | **write** | 1 – 100 (Windrose max; verify in Task 0) |
| XP | int | **write** | 0 – level-cap (verify in Task 0) |
| Gold | int | **write** | 0 – format max (verify) |
| Attributes | int[] | **write** | per-field bounds from the format |
| Class/build | string | read-only | shown for context |
| Ships, recipes, biomes | — | **out of MVP** | phase 2 |
| Inventory | — | **out of MVP** | phase 2 (catalog ships, picker doesn't) |

Validation level: **Light** — per-field floor/ceiling. We don't reconcile level↔XP↔attribute-sum; the game does that on its end (and Chris971991's editor doesn't either, per the reference description).

### Item catalog (ships in MVP, used in phase 2)

WSE Project's standalone HTML is 1.17 MB containing 1,268 items × 20 categories × 5 rarities. We extract it to JSON, commit it at `src/ModManager.Core/SaveEditor/Windrose/ItemCatalog/windrose-items.json`, and credit the source. The catalog is dead code in this MVP — no UI consumes it — but it's shipped so phase 2 (inventory editing) is a UI-only PR, not a UI-plus-data PR.

**Attribution lives in the JSON itself** (header field) AND in the in-dialog credit AND in NOTICE.

### Honor the builders

Three surfaces:

1. **In the Saves dialog** — extend the existing `EditorCredit` line. For Windrose: *"Save format support by Chris971991 (MIT) and WSE Project. Item catalog by WSE Project."*
2. **Settings → About** — credit block updated to list the Windrose sources alongside the existing FromSoft credits.
3. **Repo NOTICE file** — extend with: Chris971991 (MIT) source URL, WSE Project source URL, license texts, RocksDB.NET (Apache-2.0), facebook/rocksdb (BSD-3-Clause).

The keystone's law applies directly: *"Never monetize an author's work without permission. This is the product's spine, not a feature."* These authors did the reverse-engineering; we honor that.

### UI surface (Saves dialog already has the seam)

The Saves dialog's "Characters" section already exists from the FromSoft work. It reads via `SaveEditorService.ReadCharacters(savePath)` today (FromSoft-only call). After this work:

- `SavesDialog.RefreshCharacters` calls `svc.ReadCharacters(savePath, _engine, _steamAppId)`.
- The neutral `CharacterRow` shows `Lv 42 · 18,500 gold · Captain` for a Windrose row, `Lv 120 · 198,500 runes · Vagabond` for an Elden Ring row. Same dialog, same template — the *labels* change, the *shape* doesn't.
- The Edit button opens the per-character editor. We **reuse** `CharacterEditDialog`'s structure; if the attribute count or labels differ enough, the dialog grows a constructor overload that accepts the engine-specific shape. Decision in Task 6.

The Characters section's `EditorCredit` line updates per-engine (FromSoft authors when listing FromSoft saves; Chris971991 + WSE Project when listing Windrose).

### Why not just port WSE Project's Python or Chris971991's C++?

- **WSE Project (Python, GPL-3.0 in places, wxPython + PyInstaller):** the executable bundle is ~80 MB; we'd add a Python runtime to a .NET app. Wrong shape. We use it as a **reference** for the format + lift the item catalog (HTML data table); not a runtime dependency.
- **Chris971991 (MIT, ImGui + DirectX, C++):** correct license, wrong UI runtime. We lift the **format knowledge** (key schema, offsets) into C#; not the source.

Native C# + `RocksDB.NET` + the reverse-engineered schema = the right shape for the launcher's stack.

## File structure

| File | Role |
|---|---|
| `src/ModManager.Core/SaveEditor/Windrose/WindroseSave.cs` | New: RocksDB open/read/write, key walk, payload parse/serialize |
| `src/ModManager.Core/SaveEditor/Windrose/WindroseCharacterSlot.cs` | New: read model (one character) |
| `src/ModManager.Core/SaveEditor/Windrose/WindroseCharacterEdit.cs` | New: write model (name + level + XP + gold + stats) |
| `src/ModManager.Core/SaveEditor/Windrose/WindroseKeys.cs` | New: RocksDB key schema constants + key builder helpers |
| `src/ModManager.Core/SaveEditor/Windrose/WindroseCharacterPayload.cs` | New: per-character byte-layout read/write helpers |
| `src/ModManager.Core/SaveEditor/Windrose/ItemCatalog/windrose-items.json` | New: item DB extracted from WSE Project HTML (used in phase 2) |
| `src/ModManager.Core/ModManager.Core.csproj` | Modify: add `RocksDB.NET` PackageReference |
| `src/ModManager.App/Services/SaveEditorAdapters/SaveEditorRouting.cs` | New: engine → adapter enum |
| `src/ModManager.App/Services/SaveEditorAdapters/FromSoftAdapter.cs` | New: translate FromSoft Core records to neutral `CharacterRow` / route `CharacterEditInput` back |
| `src/ModManager.App/Services/SaveEditorAdapters/WindroseAdapter.cs` | New: same for Windrose |
| `src/ModManager.App/Services/SaveEditorService.cs` | Modify: engine-aware `ReadCharacters` + `EditCharacter` |
| `src/ModManager.App/SavesDialog.xaml.cs` | Modify: pass engine/appId to the service; per-engine `EditorCredit` text |
| `src/ModManager.App/CharacterEditDialog.xaml(.cs)` | Modify: support either engine's attribute list (likely via the neutral `CharacterEditInput`) |
| `tests/ModManager.Tests/SaveEditor/Windrose/WindroseSaveTests.cs` | New: round-trip tests against an in-memory RocksDB fixture |
| `tests/ModManager.Tests/SaveEditor/Windrose/WindroseFixture.cs` | New: build a minimal Windrose-shaped RocksDB store in a temp dir |
| `tests/ModManager.Tests/SaveEditor/SaveEditorRoutingTests.cs` | New: engine routing dispatch coverage |
| `docs/superpowers/research/2026-05-26-windrose-save-format.md` | New: Task 0 deliverable (key schema + offsets + sources) |
| `NOTICE` | Modify: add Chris971991, WSE Project, RocksDB.NET, facebook/rocksdb |

## Tech stack

.NET 10, ModManager.Core (pure), ModManager.App (WinUI 3), xUnit. Dependency adds:

- **`RocksDB.NET`** (Apache-2.0, NuGet). Bundles native `rocksdb` (BSD-3-Clause). Adds ~10 MB to the win-x64 portable.

No other new NuGets. The item catalog is a static JSON file in the Core assembly (no runtime cost in this MVP since nothing reads it yet).

## Risk

**Moderate — comparable to FromSoft.** A wrong-payload write is localized to the edited character key (RocksDB doesn't propagate corruption to other keys), but a parse-corruption-then-rewrite can produce a character the game refuses to load.

**Mitigations baked in:**

1. **Round-trip tests** against a synthesized RocksDB fixture: build a store with one known character → read → no-op edit → write → re-read → fields match.
2. **Post-write verification:** after writing, re-open read-only and verify the targeted character's fields decode to the new values. Any mismatch throws *before* the user thinks the edit succeeded.
3. **Conservative scope** — name + level + XP + gold + attributes. No inventory arrays, no quest flags, no world state. The bug surface is small.
4. **Snapshot first, always.** The whole `R5/Saved/SaveProfiles/<steamid>/` folder gets zipped before any RocksDB write. One-click restore brings everything back.
5. **Game-running detection.** Lock-conflict on write surfaces as a friendly "close Windrose first" message — no silent corruption from a half-write.

## Approval gates

- [ ] Layer 0 — Format research locked in writing (key schema + offsets + sources cited)
- [ ] Layer 1 — `RocksDB.NET` bundles cleanly into the self-contained portable
- [ ] Layer 2 — Pure-core read/write with round-trip tests against a fixture
- [ ] Layer 3 — `SaveEditorService` engine routing with neutral DTOs
- [ ] Layer 4 — Saves dialog + CharacterEditDialog working against both engines
- [ ] Layer 5 — Honor-the-builders surfacing (in-dialog credit + NOTICE + catalog header)

Future (logged separately, NOT in this PR):

- Inventory edit (uses the catalog we ship in this MVP)
- Ship / fleet / cargo edit (Windrose-specific)
- Recipe + biome discovery edit
- Other UE-custom-save games (Demonologist, Witchfire, R6, Ready or Not — each gets its own adapter)
- Cross-character transfer within Windrose (move a character between saves)
