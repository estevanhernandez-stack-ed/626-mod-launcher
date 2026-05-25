# Agentic game profiles — design

- **Date:** 2026-05-24
- **Status:** Approved (shape confirmed with Este)
- **Why:** Per-game knowledge is the hardest scaling problem in a mod manager. The curated
  `popular-games` catalog is finite; hand-curating every moddable game doesn't scale. But an LLM
  already knows each game's mod-folder and save conventions. So apply the **proven theme-generator
  pattern** (prompt → any agent → JSON → validate → import) to **game registration**: the user feeds
  their game's *name* to the agent of their choice, the agent returns a structured profile, the app
  validates and resolves it. This is the unbounded generalization of `popular-games`, built on the
  existing `GameProfiles.Resolve` foundation and the `ThemePrompt` rails.

## The load-bearing principle: structured, not absolute

The agent never emits a machine path (`C:\Users\...`). It emits **structure** the app resolves
locally — engine key, mod path *relative* to the install, save location as an *enum root + subpath*,
launcher *relative* to the install. The app resolves structure → real paths via the split it already
uses (Steam-library detect for the install, Ludusavi/known-folders for saves). A profile is therefore
portable across machines, and no guessed absolute path is ever baked in.

## Decisions (locked with Este)

| Question | Decision |
|---|---|
| Profile scope | **Full `GameEntry`** — engine, mod path, save location, launcher requirement, launch targets, file-ext/grouping overrides, CurseForge game id. |
| Save representation | **Enum root + relative subpath** (closed vocabulary), validatable + strictly structured. Ludusavi (by Steam App id) still resolves first where it has data; this is the fallback. |
| Import flow | **Resolve → verify on disk → confirm.** Validate schema, resolve `GameRoot` + saves, check on disk (pass/warn), let the user edit/confirm, then register. |
| Confirm/edit surface | **The existing Add Game wizard, pre-filled** (approach A's full profile + Core validation, delivered through B's familiar wizard). No bespoke preview dialog. |
| Agency | **Bring-your-own-agent** — the app builds the prompt and validates the answer; it does not call an LLM itself (same as the theme generator). |

## The structured profile contract

The JSON the agent returns. All paths relative or enum; absolute paths are rejected at validation.

```jsonc
{
  "name": "Elden Ring",
  "engine": "fromsoft",              // a key from the known EnginePresets set
  "windowTitle": "ELDEN RING",       // optional
  "steamAppId": "1245620",           // optional; drives GameRoot + save resolution
  "modPath": "Game/mod",             // optional; relative to install. Defaults from the engine preset.
  "fileExtensions": [],              // optional override; else from preset
  "groupingRule": "by_folder",       // optional override; else from preset
  "saveRoot": "AppData",             // enum: DocumentsMyGames | AppData | LocalAppData | SteamUserData | GameInstall
  "saveSubPath": "EldenRing",        // relative path under the resolved save root
  "requiredLauncher": "Game/ersc_launcher.exe", // optional; relative exe that must be used when modded
  "curseforgeGameId": null           // optional
  // launchTargets deferred to the launch-enforcement follow-up — see "Out of scope (v1)"
}
```

**Save root enum → resolution:**

| Enum | Resolves to |
|---|---|
| `DocumentsMyGames` | `Environment.GetFolderPath(MyDocuments)\My Games` (honors OneDrive/redirection) |
| `AppData` | `%APPDATA%` (Roaming) |
| `LocalAppData` | `%LOCALAPPDATA%` |
| `SteamUserData` | `<SteamPath>\userdata\<user>\<steamAppId>` (user resolved by the app: single userdata dir, else Steam config) |
| `GameInstall` | `<GameRoot>` |

## Architecture (pure-core / thin-shell)

### Core (pure, unit-tested)

- **`GameProfilePrompt.Build(string gameName) : string`** — twin of `ThemePrompt.Build`. Emits the
  prompt: app context, the game name, the full field list, the allowed `engine` keys (from
  `EnginePresets`), the `saveRoot` enum values, "strict JSON only," and "relative/structured paths
  only — never an absolute machine path."
- **`GameProfileImport`** — `Load(string json) : ProfileImportResult` (parse + validate in one call;
  shipped as a single method, not separate `Parse` + `Validate`). The result carries a
  `GameProfileDraft?` (non-null only when there are no errors) plus the error list.
  `GameProfileDraft` is a record mirroring the contract. Validation:
  required fields present (`name`, `engine`, `saveRoot`, `saveSubPath`); `engine` ∈ known
  `EnginePresets` keys; `saveRoot` ∈ enum; every path (`modPath`, `saveSubPath`, `requiredLauncher`)
  is **relative + safe** (no `..` segment, not drive-rooted, not absolute);
  `steamAppId` numeric if present. Bad JSON or any error → rejected with reasons, nothing applied
  (the `Themes.NormalizeTheme` contract).

### App (thin shell, build-verified + smoke-tested)

- **`GameProfileResolver`** (service) — turns a validated draft + the machine into a resolved,
  verified result:
  - `GameRoot`: from `steamAppId` via the existing Steam-library detect; else the user browses.
  - mod folder: `GameRoot` + (`modPath` or the engine preset's `ModPath`).
  - save dir: **Ludusavi by `steamAppId` first**; else expand `saveRoot` enum root + `saveSubPath`.
  - launcher: `GameRoot` + `requiredLauncher`.
  - verify each on disk → `pass` / `warn` (warn never blocks: mods may not be installed yet).
- **Extend `GameInput`** (and `BuildGameEntry`) with `SaveRoot`, `SaveSubPath`, `RequiredLauncher`
  so the wizard can assemble the full `GameEntry`. **Extend `GameEntry`** with `RequiredLauncher`
  (stored for the future launch-enforcement feature; carried now).
- **Extend `AddGameDialog`** — add an "Add with AI" entry path and the new fields (save root combo +
  subpath, required launcher). The AI path: game-name box + **Copy prompt** (clipboard) → paste box
  for the JSON → **Resolve** (parse → validate → `GameProfileResolver`) → the wizard fields fill from
  the resolved profile with pass/warn badges → user edits/confirms → existing register path. Manual
  add now also exposes the save/launcher fields (bonus).

## Data flow

```
game name
  → GameProfilePrompt.Build → (user runs it in their agent) → JSON
  → GameProfileImport.Load (parse + validate)  [reject w/ reasons on error]
  → GameProfileResolver: resolve GameRoot (Steam detect/browse), mod folder, save dir
       (Ludusavi-first), launcher; verify on disk → pass/warn
  → pre-fill the Add Game wizard (editable, badges)
  → user confirms → BuildGameEntry (incl. save/launcher fields) → LauncherService.AddGame → registry
```

## Error handling

- Bad/incomplete JSON, unknown `engine`, bad `saveRoot`, absolute/`..` path → inline field-level
  reasons in the dialog; nothing registered.
- `GameRoot` not found (Steam detect fails, no browse) → can't verify; user browses or cancels.
- Mod folder / launcher / save dir missing on disk → shown as **warn**; the user can still register
  (they may install mods or the launcher later). The game just won't show mods until the path is real.

## Testing (test-first, pure Core)

`tests/ModManager.Tests/GameProfileImportTests.cs` + `GameProfilePromptTests.cs`:

1. `GameProfilePrompt.Build("X")` contains the game name, every field name, the known engine keys,
   and the `saveRoot` enum values.
2. A valid full profile parses to a draft with the expected field values.
3. Unknown `engine` → validation error naming the allowed keys.
4. `saveRoot` outside the enum → error.
5. A `modPath` / `saveSubPath` / `requiredLauncher` that is absolute or contains `..` → error.
6. Missing a required field (`name` / `engine` / `saveRoot` / `saveSubPath`) → error.
7. Non-numeric `steamAppId` → error; absent `steamAppId` → allowed.
8. Optional fields absent (modPath, curseforgeGameId) → draft valid, defaults applied.

App `GameProfileResolver` + the wizard pre-fill are build-verified + a live smoke test (register a
real game from an agent profile end-to-end), as with the mod-update feature — there are no WinUI UI
unit tests.

## Out of scope (v1)

- **Launch-time enforcement** of `requiredLauncher` (a mod's launcher gating the vanilla launch) —
  its own follow-up feature; the schema + `GameEntry` field land here so it's ready.
- **Launch targets / multi-launch authoring** (`launchTargets`) — lands with the launch-enforcement
  follow-up. It overlaps that feature and isn't consumed by the register path, so it's deferred (not
  emitted in the prompt) rather than carried half-wired.
- **Sharing / distributing** community-authored profiles (a profile registry) — strategic, later.
- **Auto-calling an LLM** — stays bring-your-own-agent, like the theme generator.

## File structure

- Create: `src/ModManager.Core/GameProfilePrompt.cs` — the prompt builder.
- Create: `src/ModManager.Core/GameProfileImport.cs` — `GameProfileDraft` + `Load` (parse + validate in one call, returning `ProfileImportResult`).
- Modify: `src/ModManager.Core/GameEntry.cs` — `GameInput` gains `SaveRoot`/`SaveSubPath`/`RequiredLauncher`; `GameEntry` gains `RequiredLauncher`.
- Modify: `src/ModManager.Core/EnginePresets.cs` (or wherever `BuildGameEntry` lives) — map the new `GameInput` fields onto the `GameEntry`.
- Create: `src/ModManager.App/Services/GameProfileResolver.cs` — resolve + on-disk verify.
- Modify: `src/ModManager.App/AddGameDialog.xaml` + `.xaml.cs` — "Add with AI" path, the new fields, pre-fill + pass/warn badges.
- Tests: `tests/ModManager.Tests/GameProfilePromptTests.cs`, `GameProfileImportTests.cs`.
