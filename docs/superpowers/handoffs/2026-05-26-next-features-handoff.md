# Next-feature handoff prompts

Each section below has a **copy-pasteable prompt** for starting a fresh-context Claude session to execute one of the four planned features. The Claude session will use `superpowers:subagent-driven-development` to walk the plan task-by-task — fresh subagent per task, two-stage review between tasks, continuous execution.

**Pre-flight (do once before starting any of these):**

1. Pull master so you're on the latest landed state: `git -C "C:\Users\estev\Projects\626-mod-launcher" checkout master && git pull --ff-only`
2. Make sure no `ModManager.App.exe` instance is running (the build needs the Core DLL).
3. Read the spec + plan files yourself if you want context — they're at the paths each prompt references.

**Recommended order** (easiest → biggest):

1. BND4 walk (small, hardens what just shipped)
2. Mod-dependency detection (wide library impact)
3. Windrose save editor (completes editor for daily-driver)
4. ER inventory editing (biggest, save-editor phase 2)

---

## 1. BND4 file-table walk hardening

**What it does:** Replaces the hardcoded `SaveHeadersSectionStart = 0x019003B0` in `EldenRingSave.cs` with a runtime walk of the BND4 file table. Looks up the SAVE_HEADER entry by name (`USER_DATA011`) and uses its on-disk `data_offset`. Future ER patches that shift the file layout fail loud (clear `InvalidDataException`) instead of silently corrupting the save.

**Effort:** small — ~half day. 7 tasks, pure-core, no UI, no new NuGets.

**Concern surfaced during planning:** Task 2 bumps `BndHeaderSize` from `0x300` to `0x320` to accommodate the name table; this cascades to `FirstSlotMd5Offset` / `FirstSlotDataOffset` constants in `EldenRingSave.cs`. The plan calls this out and locks the bump direction.

### Prompt (copy this to a fresh Claude session)

```
Execute the implementation plan at docs/superpowers/plans/2026-05-26-saves-editor-fromsoft-bnd4-walk.md in C:\Users\estev\Projects\626-mod-launcher.

Set up:
1. Pull master and create a fresh branch off it: feat/saves-editor-bnd4-walk
2. Read the spec at docs/superpowers/specs/2026-05-26-saves-editor-fromsoft-bnd4-walk-design.md for context
3. Read memory note [[saves-editor-fromsoft]] for the brainstorm history

Use superpowers:subagent-driven-development to execute the 7 tasks. Fresh subagent per task, two-stage review between tasks, continuous execution.

Test command: dotnet test tests/ModManager.Tests/ModManager.Tests.csproj (NEVER bare dotnet test — hangs building WinUI).
Build (App): dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64.
Kill ModManager.App.exe before App builds if Core DLL is locked: Get-Process -Name ModManager.App,testhost -ErrorAction SilentlyContinue | Stop-Process -Force

Known cascading change to watch for: Task 2 bumps BndHeaderSize from 0x300 to 0x320, which forces a coordinated update to FirstSlotMd5Offset and FirstSlotDataOffset in EldenRingSave.cs. The plan covers it; just don't let Task 3+ proceed until the constants are updated.

When done: open a PR with a smoke checklist (the existing tests prove the read path works against the synthesized fixture; smoke point is "real ER save still parses correctly").
```

**What to expect:** The chain dispatches a fresh subagent per task with the plan's full task text inline. After each task the subagent reports DONE / DONE_WITH_CONCERNS / NEEDS_CONTEXT / BLOCKED. Reviews are spec-compliance + code-quality after each task. After all 7 land, a final cross-branch review then `gh pr create`.

---

## 2. Mod-dependency detection

**What it does:** Surfaces "your mod won't load because UE4SS isn't installed" as a per-row chip and a status hint at drop time. New `FrameworkDeps.cs` per-engine catalog (6 entries: UE4SS, BepInEx, SMAPI, EML/dinput8, ME2, Forge/Fabric). Checks framework presence via known on-disk paths. Adds a "Get UE4SS" hyperlink so the user knows where to install.

**Effort:** ~1 day. 7 tasks. Wide impact — affects every game in the library.

**Concerns surfaced during planning:**
- Spec mentioned a `FrameworkDepsService` wrapper; plan drops it (the pure `CheckPresent(GameContext)` is callable directly from the VM, no App-layer shim needed).
- Row chip "relevance" simplified to "every row in active game" when a framework is missing. The spec wanted per-row narrowing (only Lua rows need UE4SS, only BepInEx plugins need BepInEx, etc.) but that requires data we don't persist. v1 is slightly noisier but easier to calibrate later.
- Spec had test path `tests/ModManager.Core.Tests/`; plan uses real path `tests/ModManager.Tests/`.

### Prompt (copy this to a fresh Claude session)

```
Execute the implementation plan at docs/superpowers/plans/2026-05-26-mod-dependency-detection.md in C:\Users\estev\Projects\626-mod-launcher.

Set up:
1. Pull master and create a fresh branch off it: feat/mod-dependency-detection
2. Read the spec at docs/superpowers/specs/2026-05-26-mod-dependency-detection-design.md for context (the spec was specced earlier and already lives in master)
3. Read memory notes [[fromsoft-two-mod-worlds]] and [[windrose-four-mod-locations]] for engine context

Use superpowers:subagent-driven-development to execute the 7 tasks. Fresh subagent per task, two-stage review between tasks, continuous execution.

Test command: dotnet test tests/ModManager.Tests/ModManager.Tests.csproj (NEVER bare dotnet test).
Build (App): dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64.
Kill ModManager.App.exe before App builds: Get-Process -Name ModManager.App,testhost -ErrorAction SilentlyContinue | Stop-Process -Force

Plan diverges from spec in three places (intentional, called out in the plan's self-review):
- No FrameworkDepsService wrapper (use the pure CheckPresent directly from the VM).
- Row chip attaches to every row in the active game when a framework is missing — not narrowed per-row.
- FromSoft engine gets TWO catalog entries (ME2 + DLL proxy) because they're orthogonal worlds per memory [[fromsoft-two-mod-worlds]].

Load-bearing task: Task 3 (FrameworkDeps.CheckPresent — the seam everything hangs off; UE-pak's project-subfolder probe is the only non-trivial piece and gets 8 unit tests).

When done: open a PR with a smoke checklist — drop a mod that needs UE4SS into Windrose, confirm the row gets a "NEEDS UE4SS" chip with a working hyperlink; then install UE4SS, confirm the chip disappears.
```

**What to expect:** ~7 task subagent invocations + reviews + a final cross-task review. The trickiest part is Task 3's UE-pak detection (Windrose's UE4SS lives under `R5/Binaries/Win64/ue4ss/`, not the game root — the project subfolder convention).

---

## 3. Windrose save editor

**What it does:** Apply the FromSoft save-editor pattern to Windrose (UE5 game using RocksDB for saves). New `ModManager.Core.SaveEditor.Windrose.*` adapter mirrors the FromSoft shape. `SaveEditorService` becomes engine-aware (routes to the right adapter based on game.Engine + steamAppId). Ships the WSE Project's 1,268-item catalog now so phase 2 (inventory editing for Windrose) is UI-only later.

**Effort:** ~3-5 days. 9 tasks. Bigger because RocksDB is a new dependency and the key schema needs research.

**Concerns surfaced during planning:**
- Research doc path the planner referenced (`2026-05-26-save-editor-references.md`) doesn't exist in the .NET repo — it's in the old Electron repo. Task 0 derives the format from prompt-included findings (Chris971991 + WSE Project + RocksDB key schema) instead.
- `RocksDB.NET` NuGet (Apache-2.0) bundles a ~10MB native binary. Task 1 has an explicit publish-and-verify step before any downstream code lands.

### Prompt (copy this to a fresh Claude session)

```
Execute the implementation plan at docs/superpowers/plans/2026-05-26-saves-editor-windrose.md in C:\Users\estev\Projects\626-mod-launcher.

Set up:
1. Pull master and create a fresh branch off it: feat/saves-editor-windrose
2. Read the spec at docs/superpowers/specs/2026-05-26-saves-editor-windrose-design.md for context
3. Read memory notes [[saves-editor-fromsoft]] (the architectural pattern this mirrors), [[ue-project-subfolder]] (Windrose's R5 convention), [[windrose-four-mod-locations]] (Windrose context), and [[deps-policy-correction]] (NuGet bundling rule)

Use superpowers:subagent-driven-development to execute the 9 tasks. Fresh subagent per task, two-stage review between tasks, continuous execution.

Test command: dotnet test tests/ModManager.Tests/ModManager.Tests.csproj (NEVER bare dotnet test).
Build (App): dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64.
Kill ModManager.App.exe before App builds: Get-Process -Name ModManager.App,testhost -ErrorAction SilentlyContinue | Stop-Process -Force

Known constraints:
- Task 0 (RocksDB key schema research) is load-bearing. WebFetch BenGrn-style references for Windrose: Chris971991/WindroseCharacterEditor (MIT C++) and WSE Project (Python + wxPython + rocksdb.dll bundled). The Windrose save folder is R5/Saved/SaveProfiles/<steamid>/.
- Task 1 (RocksDB.NET NuGet add) MUST verify the native binary bundles into the self-contained portable before proceeding. Apache-2.0 license. Native librocksdb.dll lands at runtimes/win-x64/native/ when published.
- The original Windrose save-editor research was committed to the OLD Electron repo at C:\Users\estev\Projects\mod-manager-builder\docs\superpowers\research\2026-05-26-save-editor-references.md — if accessible, read it for context. Otherwise Task 0 derives from the format references named in the plan.
- Engine routing: SaveEditorService.ReadCharacters needs to dispatch by game.Engine + steamAppId. Windrose-specifically (steamAppId="3041230"), not all ue-pak games (Hogwarts, Palworld etc. are also ue-pak but have totally different save formats).

When done: open a PR with a smoke checklist — open Windrose's Saves dialog, confirm the Characters section shows a character from the RocksDB save with name/level/money, edit a stat, confirm the in-game character reflects the change.
```

**What to expect:** Bigger run (~9 tasks + Task 0 research). The RocksDB.NET dependency is the highest-risk add — if bundling fails on a clean machine, Task 1 must report BLOCKED. The pre-built portable should be smoke-tested on a Windrose-installed PC.

---

## 4. ER inventory editing (save-editor phase 2)

**What it does:** Phase 2 of the FromSoft save editor. Adds inventory edit: add item, remove item, set quantity / +N level / infusion. New tabbed `CharacterEditDialog` (identity / attributes / inventory tabs, all commit through one Save edit). Embedded JSON item catalog (~225 KB) covers ~1,500 ER items with quest-locked flags for ~90 softlock-risk items.

**Effort:** 17-24 hours. 12 tasks. Biggest of the four — proper format research, item catalog acquisition + attribution, UI build, quest-item warning surfacing.

**Concerns surfaced during planning:**
- The plan landed on the WRONG branch initially (fix/edit-dialog-crash-handling) — now cherry-picked to the docs branch. Implementer should branch off master fresh.
- Item catalog source: ClayAmore (Apache-2.0) primary + alfizari (MIT) for ER 1.13+ diff. License-bundle check happens in Task 1.
- Quest-locked item list estimated at ~90 ± 20 — needs final lock in Task 0 cross-referencing wiki questline trackers.

### Prompt (copy this to a fresh Claude session)

```
Execute the implementation plan at docs/superpowers/plans/2026-05-26-saves-editor-fromsoft-inventory.md in C:\Users\estev\Projects\626-mod-launcher.

Set up:
1. Pull master and create a fresh branch off it: feat/saves-editor-fromsoft-inventory
2. Read the spec at docs/superpowers/specs/2026-05-26-saves-editor-fromsoft-inventory-design.md for context
3. Read memory note [[saves-editor-fromsoft]] (the phase 1 brainstorm + future ambitions including cross-game character transfer)
4. Read the existing save-editor code: src/ModManager.Core/SaveEditor/FromSoft/SlotData.cs, EldenRingSave.cs (especially DiscoverMagicOffset — inventory edits MUST go through the same anchor)

Use superpowers:subagent-driven-development to execute the 12 tasks. Fresh subagent per task, two-stage review between tasks, continuous execution. Total estimated effort: 17-24 hours.

Test command: dotnet test tests/ModManager.Tests/ModManager.Tests.csproj (NEVER bare dotnet test).
Build (App): dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64.
Kill ModManager.App.exe before App builds: Get-Process -Name ModManager.App,testhost -ErrorAction SilentlyContinue | Stop-Process -Force

Load-bearing task: Task 0 (format research + offset lock). Inventory layout in the slot body is the riskiest unknown — wrong offsets = bricked saves. The snapshot-first safety law + post-write verification catch this, but the goal is to never trigger them.

Locked design decisions from the spec:
- Item catalog is an EMBEDDED JSON resource in ModManager.Core.dll (~225 KB), not a separate file
- CharacterEditDialog becomes TABBED (Pivot — identity / attributes / inventory), all commit through one Save edit
- Conservative Remove: zeros held-items entry, leaves GA-item entry intact (matches alfizari behavior; garbage accumulates but is safe against the multiple-references case)
- Catalog source: ClayAmore/ER-Save-Editor (Apache-2.0) primary + alfizari/Elden-Ring-Save-Editor (MIT) for ER 1.13+ diff
- Quest-locked item list ~90 items — Task 0 locks the exact set cross-referencing ER wiki questline trackers (Fia, Ranni, Goldmask, Yura, D, Volcano Manor, Hyetta, etc.)

Hard constraints carried through:
- Snapshot-first safety law applies to inventory edits same as stat edits
- Post-write verification mask MUST extend to cover the newly-touched inventory byte ranges (this is in the plan but flag if missed)
- Honor-the-builders: attribute ClayAmore + alfizari in NOTICE, Settings → About, and in-dialog

When done: open a PR with a smoke checklist — open ER's Saves dialog, click Edit on a real character, switch to the Inventory tab, search for an item (e.g. "Erdtree's Favor"), add it, confirm the snapshot lands AND the item appears in-game after launching ER.
```

**What to expect:** The longest run of the four (~12 tasks). The biggest risk concentrates in Task 0 (format research) and Task 6 (UI build of the picker). If a task hits BLOCKED on inventory format ambiguity, escalate to brainstorm before forcing it.

---

## Cross-cutting reminders

- **Always create a fresh branch off master.** Per CLAUDE.md: never start implementation on master.
- **Snapshot-first safety law** (FromSoft work) and **honor-the-builders** (community catalogs / format work) apply to all four.
- **Status DONE_WITH_CONCERNS is fine** — the implementer self-review surfaces what was non-obvious. Don't dismiss; read and decide.
- **If a subagent reports BLOCKED**, assess: context problem (provide more) / needs stronger model / task too large (break it down) / plan itself wrong (escalate to me / Este). Never force same-model retry without changes.
- **`gh pr create` may race** with quick merges — if it errors with "no commits between master and the branch," just check that the PR exists already (it often does — Este moves fast).

---

## After all four are done

Two longer-horizon items remain logged in memory but not specced:

- **Cross-FromSoft-game character transfer** ([[saves-editor-fromsoft]]) — the "killer feature" that ports a character SHAPE (level, attributes, identity) between ER ↔ DS3 ↔ Sekiro ↔ AC6 with host-game-wins on conflicts. Big ambition; needs its own brainstorm before specing.
- **DS3 / Sekiro / AC6 save adapter** — same FromSoft family, but AES-128-CBC encryption per slot (research note has the AC6 key). Adds an encryption layer on top of the existing reader/writer.

Both wait for after the four planned features land.
