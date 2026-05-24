# Launcher feature map — load order, save manager, theme generator

- **Date:** 2026-05-23
- **Context:** Phase 2 shell is working (games add/remove/switch, toggle, MP/SP, intake, metadata,
  uninstall, 7 themes, Steam detect). This maps the next three features Este called for, plus the
  standing backlog. Built test-first on the Core; the App wires UI over it.

## The three new features

### A. Load order management
**Why:** mods conflict; order decides who wins. Engine-specific mechanism, one shared model.

- **Core (pure, test-first):** `LoadOrder` — an ordered list of mod keys per game persisted to the
  data dir (`loadorder.json`). Reconcile with the live mod list (append new, drop missing), move
  up/down, normalize. No IO in the ordering logic itself.
- **Apply (engine-specific):**
  - **Bethesda** — rewrite `plugins.txt` / `loadorder.txt` in the configured order. **Safe**
    (text file, no mod-file touch, trivially reversible).
  - **Unreal pak** (Windrose) — order is alphabetical, so apply by prefixing files with a numeric
    key (`0010_Name.pak`). **Touches mod files → must be reversible:** record original names in the
    data dir; "reset" strips prefixes. Gated.
- **UI:** reorder list (up/down) per game + "Apply order".
- **Phasing:** ordering model (test-first) → show current order → Bethesda apply → UE prefix apply
  (reversible + gated).

### B. Save manager
**Why:** a risky loadout can corrupt a save; snapshot before you experiment. Built-in beats a side tool.

- **Core (test-first, `System.IO.Compression` like intake):** `SaveManager` — `Backup(saveDir) ->`
  timestamped zip in the data dir (`saves/`), `Restore(zip, saveDir)` (backs up current first),
  `ListSnapshots()`, `Delete(snapshot)`. Pure-ish; tested against temp dirs.
- **Save-folder discovery:** v1 = a `saveDir` configured per game (browse), with a smart default
  guess (`Documents\My Games\<game>`, Steam `userdata`). Auto-detect is a later enhancement.
- **UI:** a Saves panel — list snapshots (label + time), Backup now, Restore, Delete.
- **Phasing:** Core SaveManager (test-first) → `saveDir` on the game entry → UI.

### C. Theme generator (prompt → JSON → import)
**Why:** anyone can author a theme with an LLM. Not agentic — we hand out a prompt, you bring back JSON.

- **Core (test-first):** `ThemePrompt.Build(vibe)` → a prompt string that pins the 15-color contract
  (+ optional tokens) and asks for strict JSON. Import reuses `Themes.NormalizeTheme` to validate;
  save to the user themes dir (`themes/<id>.json`).
- **App:** ThemeService already builds from built-ins + user themes — extend it to load the user
  themes dir, and add `SaveUserTheme` + reload so a new theme appears in the picker and applies.
- **UI:** "New theme" dialog — a vibe textbox, **Copy prompt** (to clipboard), a paste box for the
  returned JSON, **Import** → validate → save → apply.
- **Phasing:** Core prompt + user-theme persistence (test-first) → import dialog. Fully headless to
  build; smallest + safest. **Do first.**

## Recommended build order for the super sesh

1. **Theme generator** — safest, full clarity, headless. Warms up the evening.
2. **Save manager** — clean v1 (manual save folder + zip snapshots).
3. **Load order** — biggest; Bethesda apply (safe) before UE prefix apply (reversible, gated).
4. Backlog cleanup if time: **profiles UI**, **MVVMTK0045** fields→partial-properties.

## Decisions (locked unless Este steers otherwise)

- **All file ops reversible/atomic** (operating law #3): UE load-order rename records originals;
  save restore backs up current first; nothing deletes without a gate.
- **Core stays UI-free + test-first**; the App holds registry/fs/clipboard integration.
- **Save-folder = manual config + smart default** for v1 (auto-detect later).
- **Theme import validates via the existing 15-color contract** (`Themes.NormalizeTheme`) — bad JSON
  is rejected with a reason, never half-applied.

## Open forks for Este

1. **Build priority** — recommend theme → saves → load order. (Load order is the one he's "worried
   about," but it's the riskiest to land blind; theme/saves bank safe wins first.)
2. **UE load-order apply renames mod files (reversibly).** OK to do that gated + undoable, or keep
   load order Bethesda-only / read-only until he can watch it run on Windrose?

## Standing backlog (carried)

- Nexus as a 2nd metadata source (the lever for mods with no CurseForge art/credits).
- Profiles UI (Core ready).
- MVVMTK0045 cleanup.
- Cover-art themes; engine auto-detect for off-catalog games (partially done via folder probe).
