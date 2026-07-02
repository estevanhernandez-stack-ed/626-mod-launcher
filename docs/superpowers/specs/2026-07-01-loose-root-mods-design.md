# Loose-root mods (organize in place) — design

**Date:** 2026-07-01
**Status:** Spec (brainstormed in-conversation; grounded against Scanner/DirectInject/ownership machinery + the real DS2 game root from the 2026-07-01 smoke). Awaiting review → writing-plans. Launcher feature (both flavors) + a companion feed data PR.
**Backlog:** task-1782943892661-suno6ak14 (high).

## The problem

Games like Death Stranding 2 (Decima engine) are modded by dropping **loose files directly into the game root** — ASI plugins, ReShade + `.addon64` addons, config INIs, proxy loaders. There is no `mods/` folder and no `.pak`. The launcher's model (a configured mod-location subfolder + known extensions + grouping) finds nothing: DS2 shows "No mods" while a dozen mods sit in the root and load with the game.

**The goal, as Este framed it:** organize pre-existing loose mods **and retain their function**. Files that must live in the game root to work stay there. "Organized" means *in the app* — detected, categorized, toggleable — not physically rearranged. Enabled = the file sits exactly where the game expects it.

## Approved decisions

1. **Detection by nature, diff later.** Recognize files that are *reliably* mods regardless of game (ASI, ReShade, addons, proxies + their associated configs). No pristine-baseline requirement — which is exactly right for already-modded installs, where a snapshot would bless existing mods as vanilla. The detector is a pluggable signal list so a vanilla-diff source can slot in later (Phase 2) without rework.
2. **Manage pre-existing AND intake new.** Phase 1 both organizes what's already in the root and accepts dropped loose files/archives, installing them into the root and managing them.
3. **No relocation while enabled.** Toggle-off moves a mod's owned files to a holding folder (reversible, metadata-tracked); toggle-on restores them byte-for-byte. The organization is presentation, never file layout.

## The model — generalize DirectInject

`DirectInject` (src/ModManager.Core/DirectInject.cs) already IS a loose-root-mod manager: it detects known mods sitting loose in the play folder by signature (`Detect`, :134-153), toggles them in place via move-to-holding (`Disable`/`Enable`, :188-234 → `<dataDir>/direct-disabled/<slug>/` + `__626mod.json` for exact restore), and installs loose payloads root-directly with path-safety + no-clobber (`Install`/`Plan`, :238-310). Its limits: a **hardcoded FromSoft-only catalog** (ReShade, Seamless Co-op, …) and no by-nature rules.

This feature **widens DirectInject into a general loose-root capability**: by-nature signatures + categories + a `Form="loose-root"` mod-location, reusing the proven detect / toggle / intake / metadata plumbing wholesale. No new file-op mechanism is introduced.

## Component A — the by-nature detector (Core, the crux)

A new pure-Core unit, `LooseModScan` (working name), takes the game root's top-level file + dir listing and returns detected loose mods, each with a name, category, and its **owned file set**. Signals, in order:

| Signal | Matches | Category | Grouping |
|---|---|---|---|
| **Known catalog** | existing `KnownDirectInjectMod` signatures (ReShade core: `ReShade.ini` + `ReShadePreset*.ini` + `reshade-shaders/` + `ReShade.log*`) | per catalog `ChipKind` | per catalog entry |
| **ASI plugin** | `*.asi` | Plugins | + same-stem `*.ini`/`*.txt`/`*.log` and a same-stem config dir group in as that mod's files (`DollmanMute.asi` + `DollmanMute.ini` = one mod) |
| **ReShade addon** | `*.addon64` / `*.addon32` | Shaders | each addon = its own mod + same-stem config (`ShaderToggler.addon64` + `ShaderToggler.ini`) |
| **Proxy loader** | exact names: `dinput8.dll`, `version.dll`, `winmm.dll`, `d3d11.dll`, `dxgi.dll`, `winhttp.dll` | Loaders | one mod per proxy; flagged `IsLoader` |

**Safety rules (the hard lines):**
- A **standalone loose `.ini`** (no matching mod stem, not in the ReShade set) is **left alone** — it could be the game's own config. ReShade *preset* INIs the user collected (e.g. `Chiral Clarity.ini`, `NaturalDS2.ini` in the real DS2 root) fall here deliberately: inert without ReShade, not worth a false-positive risk.
- **Generic `.dll`s are never claimed** except the exact proxy names. (The real DS2 root has `DeathStranding2Core.dll` — ambiguous, untouched in Phase 1.)
- Anything not matched by a signal is **invisible to the feature** — never listed, never moved. Game files (`DS2.exe`, `*.core`, `HashDB.bin`, `LocalCacheWinGame/`) match nothing by construction.
- Detection is **top-level only** (the root's files + first-level dirs grouped to a mod) — no recursive tree-walk claiming subfolders.

**Proxy-loader handling (decided):** proxies are detected, categorized as **Loaders**, and toggleable — but disabling one surfaces a dependency warning: *"This is the loader other mods inject through — disabling it disables every ASI plugin."* Warn-and-proceed (the ban-risk-gate pattern), never hard-block, never silent.

**Phase-2 seam:** `LooseModScan` composes an ordered list of `ILooseModSignal` implementations. A vanilla-diff signal (Steam depot file list, or a clean-install snapshot where obtainable) is one more implementation later.

## Component B — categories (the organized view)

Every detected mod carries a category via the existing `ChipKind` vocabulary: **Shaders · Plugins · Loaders · UI · Other**. The mod view groups the loose-root section by category with per-mod toggles — DS2's flat pile of root files becomes tidy labeled sections. (App-side presentation; the category comes from Core detection.)

## Component C — opt-in via `Form="loose-root"`

A new `ModLocation.Form` value `"loose-root"` (path = `"."`, the game root). `Scanner.BuildModList` (Scanner.cs:193-311) routes that form to `LooseModScan` instead of extension-scanning. Games opt in through the **manifest** (modPath `"."` + the loose-root form via the engine preset), so coverage grows as feed data:

- **New engine preset** `decima` — no file extensions, one loose-root location. (A future `looseRoot: true` flag on other engines can add a *secondary* loose-root location alongside their primary mod folder; out of scope Phase 1.)
- **DS2 re-curation** ships as a **companion feed PR** (separate from the launcher PR, per the established data-vs-code split): `death-stranding-2-on-the-beach` → `engine: decima`, `modPath: "."`, drop the wrong `.pak/.core` extensions.

## Component D — toggle + intake (reuse)

- **Toggle:** generalize `DirectInject.Disable/Enable` — holding folder `<dataDir>/loose-disabled/<slug>/` + the `__626mod.json` sidecar (name, category, owned entries). Enable restores exactly; no-clobber on collisions (both sides proven code).
- **Intake:** a dropped loose file/archive whose contents match the by-nature signatures classifies as a loose-root mod and installs via `DirectInject.Install` (root-direct, path-traversal-safe, no-clobber, validate-then-extract order per the repo rule). It then appears categorized + toggleable. Drops that *don't* match any signature follow the existing unrecognized flow — never silently dumped into the root.
- **Vanilla launch:** loose-root mods participate in `VanillaLaunch` step-aside/restore like other managed mods (they're owned file sets; the stash mechanism already handles that shape).

## Ownership + coexistence

Ownership markers are respected exactly as today (`ToolOwnership.Resolve`, `Coordination.PostureFor`): a Vortex/MO2-managed root reads as **Coexist** → the loose-root section is read-only until the user runs the existing takeover flow. No per-file ownership is invented.

## Error handling

- Detection never throws on unreadable files/dirs (skip + continue, matching `SafeReadFiles` semantics).
- A disable that fails mid-move restores what moved (the existing holding-folder no-partial pattern); the mod stays enabled rather than half-off.
- A holding folder whose `__626mod.json` is missing/corrupt lists the mod as disabled-but-unrestorable with a clear message, never guesses at entries.
- Intake refuses unsafe paths (`..`, absolute) before any write — validate-then-extract.

## Testing (Core, TDD)

The detector is pure and gets the real fixture: a synthetic listing of the **actual DS2 root from the smoke** —
`Zipliner_v1.1.asi`, `DollmanMute.asi`+`.ini`, `DeathStranding2Fix.asi`+`.ini`, `ReShade.ini`+`ReShadePreset.ini`+`ReShade.log`, `ShaderToggler.addon64`+`.ini`, `DeathStranding2UI.addon64`, `OptiScaler.ini`, `Chiral Clarity.ini`, `NaturalDS2.ini`, `DS2.exe`, `DeathStranding2Core.dll`, `HashDB.bin`, `LocalCacheWinGame/`, `PsPcSdkRuntimeInstaller.msi`, `DS2nexusfullgame.CT`, `CLAUDE.md` —
asserting: the ASI mods group with their configs; each addon is its own Shaders mod; ReShade core groups its set; **standalone INIs, game files, ambiguous DLLs, and stray files are all untouched**; categories correct. Toggle/intake tests reuse the existing DirectInject test patterns (round-trip enable/disable byte-identical, no-clobber, unsafe-path refusal, camelCase sidecar round-trip). App view = build + seal + a smoke entry on the real DS2 install.

## Non-goals (Phase 1)

- No vanilla-diff / pristine-baseline detection (Phase 2; the seam is the signal list).
- No recursive claiming of arbitrary subfolders; top-level nature-matches only.
- No generic-DLL claiming beyond exact proxy names.
- No physical reorganization of enabled mods — ever (that's the point).
- No per-file ownership model; folder-level coexistence stands.

## Success criteria

- DS2's real root shows its mods **categorized** (Shaders / Plugins / Loaders) with correct grouping; game files and standalone INIs are never listed or touched.
- Toggling a loose mod off/on is byte-for-byte reversible; the game loads it when enabled (files in place), doesn't when disabled.
- Disabling a proxy loader warns about dependent ASI mods (warn-and-proceed).
- Dropping a loose mod (file or archive) installs it into the root safely and it appears categorized + toggleable; unrecognized drops are refused into the root.
- Vortex/MO2-owned roots stay read-only until takeover.
- Both flavors; CorePurity green; camelCase sidecars round-trip; reversibility laws intact.

## Repo-law checklist

- **Reversibility** — move-to-holding + sidecar metadata only; no `File.Delete` in toggle paths; no-partial on failure.
- **Validate-then-extract** — intake plans + validates before any write (the FrameworkInstaller pattern, already embodied in `DirectInject.Plan`).
- **camelCase JSON on disk** — the `__626mod.json` sidecar (existing shape, extended with category) round-trips with string-contains tests.
- **Core purity** — detector + grouping are pure Core; file ops stay in the existing Core primitives; view is App.
- **Both flavors** — no `#if FULL`.
- **Manifest is descriptive** — the feed says *this game is loose-root* (engine/modPath); how loose mods are detected/toggled stays compiled code.
