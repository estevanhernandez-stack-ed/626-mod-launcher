# Road to dist — what's left before the first hand-off

- **Date:** 2026-05-24
- **Target:** friends-first portable hand-off (a friend unzips and runs, nothing pre-installed).
  The Microsoft Store landing is a separate, later track (see bottom).
- **Reality check:** the app is far more complete than the README claims (which still says
  "Phase 2 not started, 157 tests"). Phase 2 is deep, the suite is **271 green**, and the
  portable build now launches zero-prereq.

## Done this session

- Self-contained `win-x64` Release publish that **launches standalone** (was crashing —
  fixed the missing `resources.pri`, the WinUI XAML resource index `dotnet publish` drops).
- **Zero-prereq:** the VS2022 VC143.CRT runtime DLLs are bundled app-local, so a clean
  machine without VC++ Redist still runs it.
- Both fixes are durable MSBuild targets in `ModManager.App.csproj` (not manual copies).
- Output: `dist/626-Mod-Launcher-portable-win-x64.zip` — **86.6 MB**.

## v1 scope — built AND wired in the UI

Verified against the command surface in `MainWindow.xaml` / `MainViewModel`, not just Core:

- Games + registry, header game switcher, **+ Game** wizard
- Reversible enable/disable toggle; **All On / All Off**
- **MP / SP** loadouts
- **Profiles** (saved loadouts)
- Intake (**+ Add**) + smart-intake (fingerprint at drop)
- **Fetch metadata** (name search over the CurseForge proxy)
- **Themes** incl. the on-brand **626 Labs** theme; **+ Theme** AI generator
- **Steam launch**
- Honor-the-builders mod rows — name, description, `by <author> · downloads`, source links
- Custom title bar (`ExtendsContentIntoTitleBar`)

**Beyond original v1 (bonus, already built):** load order + apply (drag/position), pro save
manager (3-way clone, per-type restore, prune, auto-backup-on-launch), anti-cheat / co-op
launcher hints, launch options, direct-inject (FromSoft loose files), ModEngine2 config,
Ludusavi save-folder discovery, engine auto-detect, find-mods links (Nexus + CurseForge).

## What's left before dist — ranked

### Blockers (clear before any friend gets it)

1. **Functional acceptance (yours).** Run the hardened zip end-to-end on a real game:
   add a game, toggle mods, MP/SP, apply a load order, take + restore a save snapshot,
   launch. My smoke test only proves the window opens — it can't click through the app.
   **This is the gate.**
2. **Clean-machine validation (yours).** Prove the zero-prereq claim on a machine/VM with
   no VS and no VC++ Redist. Bundling VC++ can't be validated on this dev box (it has the
   runtime installed). This is the proof for the call you made.
3. **App icon.** No icon assets exist (`Assets/` / `.ico` absent) — friends would see a
   generic exe icon, which reads as broken/untrustworthy next to the SmartScreen warning an
   unsigned build already triggers. The Electron repo has the 626 mark + `make-icon.js` to
   reuse. Cheap, high trust impact.
4. **Friend install note.** A short GETTING-STARTED: download, unzip, run, and the
   "Windows protected your PC → More info → Run anyway" line (unsigned for now).

### Should-do (first-impression quality)

5. **App version** — set a real one (e.g., `0.1.0`); it currently ships as default `1.0.0.0`.
6. **README refresh** — it's stale and undersells the app badly.
7. **popular-games quick-pick** — orphaned (never ported from the Electron `feat/popular-games`
   branch). Manual + Game works, so not a hard blocker, but it's v1 scope and a real
   onboarding win for friends who don't want to find the install path themselves.

### Post-dist (roadmap — explicitly deferred)

- **Nexus as a 2nd metadata source** — today the find-mods menu only *links out* to Nexus;
  no Nexus metadata fetch yet. This is the lever for mods CurseForge can't cover (repacked
  mods, Windrose's Nexus-only art/credits).
- Cover-art themes; mod images.
- MVVMTK0045 cleanup (cosmetic AOT warnings; harmless).
- **MSIX + Microsoft Store** — the platform track. Needs a packaging project +
  `Package.appxmanifest` (capabilities: broad filesystem access, framed as "your own files")
  and a **Partner Center account** (the one gate code can't clear). Plan staged in
  `docs/release-msstore.md`.

## Recommended critical path

The hand-off is closer than it looks. The minimal cut:

1. **I do now:** app icon (626 mark) + version `0.1.0` + GETTING-STARTED + README refresh —
   then re-publish + re-zip. (Optionally fold in popular-games if we want the onboarding win.)
2. **You do:** run the zip, verify it works on a real game, then drop it on a clean
   machine/VM to confirm zero-prereq.
3. **Then:** first friend hand-off. The Store track starts after the portable build proves
   out with real friends.
