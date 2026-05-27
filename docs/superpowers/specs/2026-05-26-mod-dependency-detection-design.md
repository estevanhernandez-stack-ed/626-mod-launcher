# Mod dependency detection — Design exploration

**Date:** 2026-05-26
**Status:** Draft — for Este's review before planning
**Branch:** N/A (research only, no code)

## Why

When a user drops a mod into the launcher today, the mod may have an unmet **framework dependency** the launcher doesn't know about — and the mod silently does nothing in-game. Example: a Windrose mod that needs UE4SS under `R5/Binaries/Win64/ue4ss/`. If UE4SS isn't there, the drop "succeeds" (the `.pak` lands under `R5/Content/Paks/~mods/`), but the mod never loads. The user has no idea why.

Este's framing (2026-05-26):
> "What would happen when a user drops a mod into any of the games that needs another dependency that needs to be installed. For instance, with Windrose, I have a mod that needs UE4SS and we have that installed already, but what would happen if a user didn't? I would prefer that we auto-installed it, but that would mean that we already had it on hand. So, a search link would be useful, I guess, or just make sure the user knows what to look for, but it needs to be called out. This way they know why something doesn't work."

Three options on the table:

1. **Auto-install** the dep if we have it bundled (we don't bundle frameworks today)
2. **Search link** to where the user can get it
3. **Clear callout** so the user understands WHY their mod won't work

The product law: **honor the builders** — and that extends to the user's understanding. A silent-fail mod is a betrayal of the moment the user dropped it in.

## What the launcher knows today vs needs to know

The launcher already classifies dropped mods (`Intake.ClassifyDrop`, `DirectInject.MatchSignaturesInZip`, `Ue4ssLuaDetect.Detect`, `SaveModDetect.Detect`, `BepInExPlugins.Scan`) and routes them to the right install path. It does NOT verify that the LOADER / FRAMEWORK each mod needs is actually present on disk.

| Engine | What the launcher detects today | Framework dep it does NOT check |
|---|---|---|
| **UE-pak** (Windrose, Hogwarts, Palworld) | `.pak/.ucas/.utoc` → routes to `Content/Paks/~mods` | Blueprint-pak mods need UE4SS + `BPModLoaderMod` (under `Binaries/Win64/ue4ss/`) for "LogicMods"; plain content paks usually don't |
| **UE4SS Lua** | Detects Lua-mod archives by `Scripts/*.lua` or `enabled.txt + dlls/*.dll` (`Ue4ssLuaDetect.cs`) | REQUIRES UE4SS itself — `dwmapi.dll` + `ue4ss/` framework under `Binaries/Win64/`. Currently surfaced as a Vortex-managed-folder message, not a presence check |
| **BepInEx plugins** | `.dll` under `BepInEx/plugins/` (`BepInExPlugins.cs`) | REQUIRES BepInEx — `winhttp.dll` + `BepInEx/core/` framework. Plugin DLLs may also declare BepInEx-plugin dependencies (`[BepInDependency]`) on other plugins, but those aren't readable without an assembly parser |
| **SMAPI mods (Stardew)** | Folder under `Mods/` with `manifest.json` | REQUIRES SMAPI (`StardewModdingAPI.exe` next to `Stardew Valley.exe`). Mods also declare deps in `manifest.json` `Dependencies[]` by SMAPI `UniqueID` — these are the easiest "real" mod-to-mod deps in any engine we support |
| **FromSoft direct-inject** | `DirectInject.Catalog` signatures (ReShade, Seamless Co-op, ERSS2, ultrawide, DLL loader) | Some loose-DLL mods need a generic DLL proxy loader (`dinput8.dll`, `version.dll`, `winhttp.dll`) already present from a previous mod. Implicit; rare to check |
| **FromSoft Mod Engine 2** | TOML at `<root>/mod/config_*.toml` (`ModEngine2Config.cs`) | ME2 itself: `modengine2_launcher.exe` (the LaunchScan probe already finds it). No declared mod-to-mod deps |
| **Bethesda (Creation Engine)** | `.esp/.esl/.esm/.bsa` under `Data/` | A formal "masters" list lives inside every plugin header (lists which other plugins it depends on). Reading it = parsing the plugin binary. NOT a framework dep — it's plugin-to-plugin |
| **Save/world mods** | `Worlds/<GUID>` or top-level GUID folder (`SaveModDetect.cs`) | No framework dep — they install to the save tree, which always exists once the game has run once |
| **Minecraft (Forge/Fabric)** | `.jar` in `mods/` | REQUIRES the chosen loader (Forge OR Fabric installer's output in `<game>/.minecraft/`). Mods declare deps in their jar's `META-INF/mods.toml` (Forge) or `fabric.mod.json` (Fabric) — readable manifests |

The recurring shape: **most engines have ONE framework dep** that's an existence check at a known path. A smaller set (SMAPI, Forge/Fabric, BepInEx) ALSO have **per-mod manifest-declared deps** that compose on top.

## Approach options

### Option A — Per-engine framework presence check

For each engine, encode 1-2 known framework file paths. At drop time (or at game-scan time), check existence. If a dropped mod's engine has a missing framework, surface a "needs X" warning + a download link.

**Where this lives**: a new pure core `FrameworkDeps.cs` with a static catalog keyed by engine. The check is a function `Probe(GameContext) → MissingFrameworks[]`. Run once at game load + on every drop.

**Catalog shape (rough)**:

```csharp
new FrameworkDep(
    Engine: "ue-pak",
    Name: "UE4SS",
    AppliesWhen: drop => drop.NeedsUe4ss, // Lua mod, OR LogicMods pak, OR loose dwmapi.dll references
    DetectFile: gameRoot => Path.Combine(gameRoot, projSub, "Binaries", "Win64", "ue4ss", "UE4SS.dll"),
    GetUrl: "https://github.com/UE4SS-RE/RE-UE4SS/releases",
    NexusUrl: "https://www.nexusmods.com/<game>/mods/<id>" // when we know it
)
```

**Tradeoffs**:
- + Easy to implement, narrow + correct coverage, no parsing
- + Composes with the existing engine map (`KnownEngines`, `EnginePresets`)
- + Surfaces value on the FIRST drop — no need to wait for manifest parsing
- − Doesn't catch mod-to-mod deps (Stardew mod A needs Stardew mod B)
- − False-positive risk: warning "needs UE4SS" on a plain content pak that doesn't actually need it. Mitigated by narrowing `AppliesWhen` to the cases we KNOW need the framework (Lua-mod verdict, `LogicMods/` pak path, `dwmapi.dll` in archive)

### Option B — Manifest-declared deps (SMAPI / BepInEx / Forge / Fabric style)

For engines where each mod ships a manifest declaring deps by ID, parse the manifest at drop time, resolve declared deps against the installed mod list, surface missing ones.

**Where this lives**: per-engine manifest readers in pure cores (`SmapiManifest.cs`, `ForgeManifest.cs`, `FabricManifest.cs`, optionally `BepInExAttributes.cs` if we want assembly parsing). Each returns `IReadOnlyList<DeclaredDep>`. A resolver compares against the installed list (`Scanner.ScanMods` + the manifest readers themselves for the installed mods).

**Tradeoffs**:
- + Rich, accurate coverage where manifests exist
- + Catches the "you installed mod A but mod B needs mod C" chain
- + Aligns with how the modding community itself thinks about deps
- − Useless for engines without manifests (FromSoft direct-inject, plain UE-pak content, Bethesda plugin masters without a binary parser)
- − More surface area, more parsers, more maintenance
- − Most cases are framework deps, not mod-to-mod (the user almost always has the framework problem, not a mod-chain problem)

### Option C — Hybrid (recommended)

A handles framework deps for every engine. B layers on top for engines whose manifests are cheap to read (SMAPI first — it's a clean JSON; Forge/Fabric next; BepInEx attributes deferred). The user sees a unified "Missing dependency" surface regardless of which layer caught it.

**Build order**: A first as a pure core + UI surface. B added engine-by-engine, starting with SMAPI (real users + real manifests + zero parsing cost). Both layers feed the same `MissingDependency` record + the same UI chip.

**Tradeoffs**:
- + Covers 90% of the silent-fail cases with Option A alone
- + Leaves room for Option B without re-architecting the surface
- + Composes with how the launcher already thinks (pure-core detection per engine)
- − Two layers to maintain. Mitigated: B is opt-in per engine

### Recommendation

**Option C, built in two iterations: A first, then SMAPI manifest deps as the second iteration.** Reasoning:

1. **Framework deps are the actual problem the user is hitting** today — Este's example is a UE4SS framework miss, not a mod-to-mod miss.
2. **A is cheap** — one new pure core (`FrameworkDeps.cs`), a presence check per engine, ~6 entries in the initial catalog.
3. **B without A would leave UE-pak / FromSoft / Bethesda users with no detection at all** — the engines where most of the launcher's users live.
4. **SMAPI as iteration 2** because Stardew users are real, the manifest is trivial JSON, and it proves the layering pattern for Forge/Fabric/BepInEx later.

## UX shape

The warning needs to be **calm, specific, and impossible to miss**, in that order. Layered:

### 1. Drop status line (primary surface — the moment of confusion)

Right after a drop, append to the existing `StatusText` line in `MainViewModel.AddModsAsync`. Same place "Installed 1 save-mod world" and the UE4SS Lua callout already land.

Example: *"Added 1, skipped 0. Heads up: this mod needs UE4SS — get it at github.com/UE4SS-RE/RE-UE4SS/releases."*

This is the **most important surface**. It catches the user in the exact second they're wondering "did it work?".

### 2. Row-level chip (persistent surface — the next time they look)

A new chip on the mod row, alongside the existing `MANAGED`, `BUILTIN`, `MP-SAFE` chips: **`NEEDS UE4SS`** (or whatever the missing framework is), colored with `ThemeDanger`. Click → opens the download link via `HyperlinkButton.NavigateUri` (`SafeUrl` already guards http(s)).

Implementation: add `MissingDependency` to the `Mod` record + a `MissingDepBadge` / `MissingDepLink` / `MissingDepVisibility` set on `ModRowViewModel` — mirrors the existing `Managed` / `Builtin` / `MpBadge` pattern verbatim.

### 3. Settings → Frameworks panel (overview surface — the third visit)

A new section in the Settings dialog that lists, per active game, every detected framework + its status (installed at `<path>` / missing) + a download link for the missing ones. Not in v1 — deferred to a follow-up iteration if users ask.

### What NOT to do

- **No modal dialog.** Modal-on-drop interrupts the "just install it" promise. The status line + chip is the right pressure level.
- **No toast notification.** Toasts disappear; chips persist. The user might miss a toast while alt-tabbed to the game.
- **No "install for me" button without bundled binaries.** Auto-install requires either bundling UE4SS (~500KB, GPL — fine) or a key-less downloader. Deferred — for v1, the link IS the action.

### The single most important surface

**The drop status line.** It catches the user at the moment of confusion. The row chip is the safety net for the second visit. Build both; do not skip the status line.

## File structure

```
src/ModManager.Core/
  FrameworkDeps.cs          # new — pure catalog + Probe(GameContext) → MissingDependency[]
                            # one record per framework: Name, AppliesTo(engine, drop verdict),
                            # DetectFile(gameRoot, projectSubfolder), DownloadUrl, NexusUrl?
  Mod.cs                    # add: string? MissingDependency, string? MissingDependencyUrl

src/ModManager.App/
  Services/
    FrameworkDepsService.cs # new — App-layer wrapper: Probe(GameEntry) using
                            # ModManager.App.Services.EngineScan's resolved project subfolders
  ViewModels/
    ModRowViewModel.cs      # add: MissingDepBadge, MissingDepUrl, MissingDepVisibility
  MainWindow.xaml           # add the chip alongside ManagedBadge in the row template
  ViewModels/MainViewModel.cs
                            # extend AddModsAsync's status-line builder: after intake,
                            # call FrameworkDepsService.Probe + append missing-dep line
                            # when the drop's verdict needed a framework that isn't there

tests/ModManager.Core.Tests/
  FrameworkDepsTests.cs     # new — unit tests over the catalog with fake game-root layouts
```

**Test-first**, per the keystone law. Each catalog entry gets a positive + negative test (framework present → no warning; absent → warning + correct URL).

## Risk

**Low** — purely additive callouts. No file ops, no schema changes, no Electron/.NET shared-state changes. Worst case:

- **False positive**: warn "needs UE4SS" on a content pak that didn't actually need it. Mitigated by narrowing `AppliesWhen` to verdicts we're sure about (Lua-mod detection, `LogicMods/` pak path).
- **Stale catalog**: a framework moves its release page. URL goes stale. Mitigated by the catalog being a couple lines per framework, easy to update + ship in a point release. Not worse than any other curated catalog in the launcher (`DirectInject.Catalog`, `Ue4ssBuiltins.Catalog`, `KnownEngines.Map` all have the same shape and the same maintenance posture).
- **User has the framework but at a non-standard path**: the existence check misses it, we warn "needs UE4SS" when they have it. Mitigated by probing the project-scoped candidates first (mirrors `ModLocations.Candidates`), then a root fallback. If users still hit this, add a "I have it, suppress this warning" override later.

## Out of scope for first iteration

- **Auto-install** the framework — requires bundled binaries or a downloader. Defer.
- **Cross-engine dependency resolution** — e.g. "this mod needs both UE4SS AND a specific BPModLoader version". Probe just answers "framework present yes/no" in v1; versions later.
- **Version pinning** — "needs UE4SS >= 3.0.1". Defer until users hit it.
- **Bethesda plugin masters** — parsing `.esp` headers for plugin-to-plugin deps. Real but expensive parser; defer.
- **BepInEx `[BepInDependency]` assembly attributes** — same shape as Bethesda masters: real but expensive. Defer.

## Why this is the right next step

The launcher's job is to **leave nothing silent**. Today a Windrose drop is silent when UE4SS is missing. Tomorrow, with this design shipped, the same drop says: *"Added 1. This mod needs UE4SS — get it here."* That's the difference between a mod manager and a launcher that respects the builder. Honor the builders extends to honoring the user's understanding of why their mod isn't loading.
