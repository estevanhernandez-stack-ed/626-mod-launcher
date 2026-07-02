# Loose-root mods (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Fresh implementer per task + two-stage review (spec + quality/laws) + fix gate, then a whole-branch review. Steps use `- [ ]`.

**Goal:** Detect, categorize, toggle, and intake loose-file root mods (DS2/Decima: ASI plugins, ReShade + addons, proxy loaders) — organized in the app, files retained in place so they keep working.

**Architecture:** Generalize the existing `DirectInject` machinery. A new pure-Core by-nature detector (`LooseModScan`) emits the same `DirectInjectMod` shape the catalog detector emits, so the proven toggle (move-to-holding + `__626mod.json` sidecar), listing, and intake plumbing are reused wholesale. A new `ModLocation.Form = "loose-root"` + a `decima` engine preset route games to it. Category rides the existing `Kind`/ChipKind field — no sidecar schema change.

**Tech Stack:** .NET 10 / C# (Core: pure + xUnit; App: WinUI 3). Both flavors.

## Global Constraints

- **Both STORE and FULL** — no `#if FULL` anywhere in this feature. STORE must seal (`pwsh scripts/check-store-seal.ps1`).
- **Core purity** — detector/listing/toggle logic is pure Core (file ops through existing Core primitives); UI in App. `CorePurityTests` stays green.
- **Reversibility** — toggle = move-to-holding + sidecar, byte-for-byte restore, no `File.Delete` in toggle paths, no-partial on failure. Intake is validate-then-extract (`DirectInject.Plan` before any write).
- **Safety lines (from the spec, non-negotiable):** standalone loose `.ini` files are never claimed; generic `.dll`s are never claimed except the exact proxy names; unmatched files are invisible (never listed, never moved); detection is top-level only.
- **camelCase JSON on disk** — the `__626mod.json` sidecar already round-trips camelCase; any extension keeps that + a string-contains test.
- **Never bare `dotnet` at repo root.** Core: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. App: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (+ `-p:Configuration=Store`); kill `ModManager.App` first.
- **Conventional commits** + trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`. Branch `feat/loose-root-mods`.
- **Ground-truth caveat:** line numbers below come from an exploration pass — read the named regions before editing; if a shape differs (e.g. `DirectInjectMod`'s exact record fields at `src/ModManager.Core/DirectInject.cs` ~:100-130), match the real shape and keep the plan's semantics.

## Categories (ChipKind values)

`"plugin"` (ASI) · `"shaders"` (ReShade + addons) · `"loader"` (proxy DLLs) · `"ui"` (reserved) — flowing through the existing chip rendering; the loose-root rows sort category-then-name.

---

## Task 1: `LooseModScan` — the by-nature detector (Core, TDD)

**Files:**
- Create: `src/ModManager.Core/LooseMods/LooseModScan.cs`
- Test: `tests/ModManager.Tests/LooseMods/LooseModScanTests.cs`

**Interfaces:**
- Consumes: `DirectInjectMod` (the existing record in `src/ModManager.Core/DirectInject.cs` ~:100-130 — read it first; exploration reports `(Name, Kind, Evidence, Entries)` with entries as play-folder-relative names; match its exact shape + conventions).
- Produces: `static class LooseModScan { public static IReadOnlyList<DirectInjectMod> Detect(IReadOnlyList<string> topFiles, IReadOnlyList<string> topDirs, ISet<string>? alreadyOwned = null); }` — `topFiles`/`topDirs` are top-level basenames of the game root; `alreadyOwned` excludes entries the catalog detector (`DirectInject.Detect`) already claimed. All matching case-insensitive.

- [ ] **Step 1: Write the failing tests** — the fixture is the REAL DS2 root from the 2026-07-01 smoke:

```csharp
using ModManager.Core;
using ModManager.Core.LooseMods;

namespace ModManager.Tests.LooseMods;

public class LooseModScanTests
{
    // The actual Death Stranding 2 game root observed live (2026-07-01).
    private static readonly string[] Ds2Files =
    {
        "Zipliner_v1.1.asi", "DollmanMute.asi", "DollmanMute.ini",
        "DeathStranding2Fix.asi", "DeathStranding2Fix.ini",
        "ReShade.ini", "ReShadePreset.ini", "ReShade.log",
        "ShaderToggler.addon64", "ShaderToggler.ini", "DeathStranding2UI.addon64",
        "OptiScaler.ini", "Chiral Clarity.ini", "NaturalDS2.ini", "SDR+.ini",
        "DS2.exe", "DeathStranding2Core.dll", "HashDB.bin",
        "PsPcSdkRuntimeInstaller.msi", "DS2nexusfullgame.CT", "CLAUDE.md",
        "dinput8.dll",
    };
    private static readonly string[] Ds2Dirs = { "LocalCacheWinGame", "reshade-shaders" };

    private static IReadOnlyList<DirectInjectMod> Scan(ISet<string>? owned = null)
        => LooseModScan.Detect(Ds2Files, Ds2Dirs, owned);

    [Fact]
    public void Asi_plugins_detect_one_mod_each_with_same_stem_config_grouped()
    {
        var mods = Scan();
        var dollman = Assert.Single(mods, m => m.Name.Contains("DollmanMute", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("plugin", dollman.Kind);
        Assert.Contains(dollman.Entries, e => e.Equals("DollmanMute.asi", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dollman.Entries, e => e.Equals("DollmanMute.ini", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(mods, m => m.Entries.Contains("Zipliner_v1.1.asi"));          // no config — still a mod
        Assert.Contains(mods, m => m.Entries.Contains("DeathStranding2Fix.asi"));
    }

    [Fact]
    public void Each_addon_is_its_own_shaders_mod_with_its_config()
    {
        var mods = Scan();
        var st = Assert.Single(mods, m => m.Entries.Contains("ShaderToggler.addon64"));
        Assert.Equal("shaders", st.Kind);
        Assert.Contains("ShaderToggler.ini", st.Entries);
        var ui = Assert.Single(mods, m => m.Entries.Contains("DeathStranding2UI.addon64"));
        Assert.Equal("shaders", ui.Kind);
    }

    [Fact]
    public void Exact_proxy_names_detect_as_loader()
    {
        var proxy = Assert.Single(Scan(), m => m.Kind == "loader");
        Assert.Contains("dinput8.dll", proxy.Entries);
    }

    [Fact]
    public void Safety_lines_hold_untouchables_are_never_claimed()
    {
        var claimed = Scan().SelectMany(m => m.Entries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // standalone INIs (incl. ReShade presets the user collected) — left alone
        Assert.DoesNotContain("OptiScaler.ini", claimed);
        Assert.DoesNotContain("Chiral Clarity.ini", claimed);
        Assert.DoesNotContain("NaturalDS2.ini", claimed);
        Assert.DoesNotContain("SDR+.ini", claimed);
        // game files + ambiguous DLL + stray files — invisible
        Assert.DoesNotContain("DS2.exe", claimed);
        Assert.DoesNotContain("DeathStranding2Core.dll", claimed);
        Assert.DoesNotContain("HashDB.bin", claimed);
        Assert.DoesNotContain("DS2nexusfullgame.CT", claimed);
        Assert.DoesNotContain("CLAUDE.md", claimed);
        Assert.DoesNotContain("LocalCacheWinGame", claimed);
        // ReShade's own set belongs to the CATALOG detector, not this one (see next test)
    }

    [Fact]
    public void Already_owned_entries_are_excluded()
    {
        // Simulate the catalog detector having claimed ReShade's set — nature scan must not re-claim.
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "ReShade.ini", "ReShadePreset.ini", "reshade-shaders", "ReShade.log" };
        var claimed = Scan(owned).SelectMany(m => m.Entries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReShade.ini", claimed);
        Assert.DoesNotContain("reshade-shaders", claimed);
    }

    [Fact]
    public void Same_stem_directory_groups_into_the_mod()
    {
        var mods = LooseModScan.Detect(new[] { "CoolMod.asi" }, new[] { "CoolMod" }, null);
        var m = Assert.Single(mods);
        Assert.Contains("CoolMod.asi", m.Entries);
        Assert.Contains("CoolMod", m.Entries);
    }

    [Fact]
    public void Empty_and_unmatched_inputs_detect_nothing()
    {
        Assert.Empty(LooseModScan.Detect(Array.Empty<string>(), Array.Empty<string>(), null));
        Assert.Empty(LooseModScan.Detect(new[] { "readme.txt", "game.exe", "data.core" }, new[] { "data" }, null));
    }
}
```

- [ ] **Step 2: Run, verify fail** — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter LooseModScan` → FAIL (type not defined).
- [ ] **Step 3: Implement** `LooseModScan.cs` — reference shape (align `DirectInjectMod` construction with the real record):

```csharp
namespace ModManager.Core.LooseMods;

/// <summary>By-nature detection of loose-file root mods: files that are RELIABLY mods regardless of
/// game (a game never ships .asi/.addon64; the proxy names are the ASI-loader convention). Emits the
/// same <see cref="DirectInjectMod"/> shape the catalog detector emits so listing/toggle/intake reuse
/// the proven DirectInject plumbing. Safety lines: standalone INIs and generic DLLs are NEVER claimed;
/// anything unmatched is invisible. Top-level only. Pure — the caller supplies the listing.
/// Phase-2 seam: this is one signal; a vanilla-diff signal composes alongside it later.</summary>
public static class LooseModScan
{
    private static readonly string[] ProxyNames =
        { "dinput8.dll", "version.dll", "winmm.dll", "d3d11.dll", "dxgi.dll", "winhttp.dll" };

    public static IReadOnlyList<DirectInjectMod> Detect(
        IReadOnlyList<string> topFiles, IReadOnlyList<string> topDirs, ISet<string>? alreadyOwned = null)
    {
        var owned = alreadyOwned ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Free(string name) => !owned.Contains(name);
        var mods = new List<DirectInjectMod>();

        // ASI plugins → "plugin"; ReShade addons → "shaders". Same-stem .ini/.txt/.log + same-stem dir group in.
        foreach (var (ext, kind) in new[] { (".asi", "plugin"), (".addon64", "shaders"), (".addon32", "shaders") })
        {
            foreach (var f in topFiles)
            {
                if (!f.EndsWith(ext, StringComparison.OrdinalIgnoreCase) || !Free(f)) continue;
                var stem = Path.GetFileNameWithoutExtension(f);
                var entries = new List<string> { f };
                foreach (var cfgExt in new[] { ".ini", ".txt", ".log" })
                {
                    var cfg = topFiles.FirstOrDefault(x =>
                        x.Equals(stem + cfgExt, StringComparison.OrdinalIgnoreCase) && Free(x));
                    if (cfg is not null) entries.Add(cfg);
                }
                var dir = topDirs.FirstOrDefault(d => d.Equals(stem, StringComparison.OrdinalIgnoreCase) && Free(d));
                if (dir is not null) entries.Add(dir);
                mods.Add(new DirectInjectMod(stem, kind, $"loose {ext} in game root", entries));
            }
        }

        // Exact-name proxy loaders → "loader" (flagged by Kind; disable warns App-side).
        foreach (var p in ProxyNames)
        {
            var hit = topFiles.FirstOrDefault(f => f.Equals(p, StringComparison.OrdinalIgnoreCase) && Free(f));
            if (hit is not null)
                mods.Add(new DirectInjectMod(Path.GetFileNameWithoutExtension(hit) + " (ASI loader)",
                    "loader", "proxy loader DLL in game root", new List<string> { hit }));
        }

        return mods;
    }
}
```

- [ ] **Step 4: Run, verify pass** — same filter → PASS (all cases). Full Core suite → green.
- [ ] **Step 5: Commit** — `feat(loose-mods): by-nature loose-root detector (ASI/addons/proxies, Core)`.

## Task 2: `Form="loose-root"` + `decima` preset + listing + toggle (Core)

**Files:**
- Modify: `src/ModManager.Core/EnginePresets.cs` (~:12-32 — add `decima`), `src/ModManager.Core/Scanner.cs` (`GameContext` ~:41-86 where per-engine `Form` resolves; add the loose-root routing), `src/ModManager.Core/ModListing.cs` (~:12-22 dispatch)
- Create: `src/ModManager.Core/LooseMods/LooseRootListing.cs`
- Test: `tests/ModManager.Tests/LooseMods/LooseRootListingTests.cs` (+ an EnginePresets assertion)

**Interfaces:**
- Consumes: `LooseModScan.Detect` (Task 1); `DirectInject.Detect` (catalog — so ReShade et al. detect via existing signatures, engine-agnostic); `DirectInject.Disable/Enable(playFolder, holdingRoot, ...)` (~:188-234); `DirectInjectListing.List` (~:17-50) as the structural template.
- Produces:
  - `EnginePresets.Presets["decima"]` — no file extensions; modPath `"."`; the resulting `ModLocation` carries `Form = "loose-root"`.
  - `static class LooseRootListing { List(game) }` mirroring `DirectInjectListing.List`'s return shape: detected-enabled mods (catalog hits first via `DirectInject.Detect`, then `LooseModScan.Detect` with the catalog's entries as `alreadyOwned`) + disabled mods read from `<dataDir>/loose-disabled/*/__626mod.json` sidecars.
  - Toggle for loose-root mods = the existing `DirectInject.Disable/Enable` with holding root `<dataDir>/loose-disabled` (byte-for-byte restore, no-clobber — already proven; do NOT fork the implementation).
  - `ModListing.Resolve` routes games whose mod location has `Form == "loose-root"` to `LooseRootListing`.

- [ ] **Step 1:** Read the four named regions. Write failing tests: `decima` preset exists with modPath `"."` + empty extensions; `LooseRootListing.List` on a temp dir seeded with the DS2 fixture files returns catalog + nature mods without double-claiming; disable→enable round-trip through `loose-disabled` restores byte-identical files (reuse the existing DirectInject round-trip test pattern with a temp play folder); a corrupt/missing sidecar lists the mod as disabled-unrestorable, never guesses.
- [ ] **Step 2:** Verify fail, implement (routing + listing; toggle is parameterization of existing calls), verify pass. Full Core suite green.
- [ ] **Step 3: Commit** — `feat(loose-mods): decima preset + loose-root form, listing + reversible toggle (Core)`.

## Task 3: Intake for loose-root games (Core)

**Files:**
- Modify: the intake classification/planning path (`src/ModManager.Core/Intake.cs` `ClassifyDrop` ~:15-24 and/or `Scanner.PlanIntake`/`ExecuteIntake` ~:1026-1139 — read first) so loose-root games route drops through `DirectInject.Plan`/`Install` (~:238-310).
- Test: `tests/ModManager.Tests/LooseMods/LooseRootIntakeTests.cs`

**Interfaces:**
- Consumes: `LooseModScan` signatures (what counts as a recognized loose mod in a drop), `DirectInject.Plan/Install` (root-direct, path-safe, no-clobber, validate-then-extract).
- Produces: for a game in loose-root form — a dropped `.asi`/`.addon64`/recognized set (loose file or archive) plans into the game root and appears in the listing; an unrecognized drop is refused into the root (routes to the existing unrecognized flow, never silently dumped).

- [ ] **Step 1:** Failing tests: loose `.asi` drop plans to game root + lists afterward; archive of `Mod.asi`+`Mod.ini` plans both; `readme.txt` drop is refused for the root; `..`-path archive entry refused before any write (reuse the existing unsafe-path test pattern).
- [ ] **Step 2:** Verify fail → implement routing → verify pass → full Core suite green.
- [ ] **Step 3: Commit** — `feat(loose-mods): loose-root intake via validate-then-extract root placement (Core)`.

## Task 4: App surface — categorized rows, proxy-disable warning, vanilla step-aside (App)

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (row builder — loose-root rows flow the direct-inject lane; they're `DirectInjectMod`-shaped already), the toggle path (proxy warning), `BuildVanillaOps` (loose-root participation), and the mod-list chip labels for `plugin`/`shaders`/`loader`.
- Modify: `docs/smoke-tests/pending.md` (smoke entry).

**Interfaces:**
- Consumes: `LooseRootListing`/`ModListing.Resolve` (Task 2), the existing direct-inject row rendering + ChipKind chips, the `ConfirmBanRiskEnable`-style dialog delegate pattern (`MainWindow.xaml.cs` ~:44).
- Produces: loose-root mods render in the mod list with category chips, sorted category-then-name; disabling a `Kind == "loader"` mod first shows a warn-and-proceed dialog ("This is the loader other mods inject through — disabling it disables every ASI plugin." / Disable anyway / Cancel — never a hard block); vanilla launch steps loose-root mods aside + restores them (verify `BuildVanillaOps` includes them; add if absent).

- [ ] **Step 1:** Read the row-builder + toggle + vanilla-ops regions; wire the three behaviors. No `#if FULL`.
- [ ] **Step 2: Gate** — kill app; FULL + STORE builds 0 errors; `check-store-seal.ps1` OK; full Core suite green.
- [ ] **Step 3: Smoke entry** — on the real DS2 install (after re-adding DS2 or editing its entry to loose-root): mods appear categorized (ReShade under Shaders via catalog; Zipliner/DollmanMute/DS2Fix under Plugins with configs grouped; addons under Shaders; dinput8 under Loaders); `OptiScaler.ini`/preset INIs/`DS2.exe`/`DeathStranding2Core.dll` NOT listed; toggle off removes from root into `loose-disabled` + game stops loading it; toggle on restores byte-identical; proxy disable shows the dependency warning; drop a new `.asi` → installs to root + lists; vanilla launch steps them aside and restores.
- [ ] **Step 4: Commit** — `feat(loose-mods): categorized loose-root rows + loader-disable warning + vanilla step-aside (App)`.

## Task 5: Companion feed PR + registered-entry note (data — sequenced LAST)

- [ ] **After the launcher PR merges** (the feed CI validates engines against the launcher's `EnginePresets` keys — `decima` must exist on launcher master first): in `c:/Users/estev/Projects/626-game-manifest`, update `overrides/death-stranding-2-on-the-beach.json` → `"engine": "decima"`, `"modPath": "."`, remove the `.pak/.core` fileExtensions. PR + merge; CI re-signs the feed.
- [ ] Note in the smoke entry: games registered BEFORE this change keep their old local config — re-add DS2 (or edit its `games.json` entry) to pick up loose-root mode; the feed fixes fresh adds.

## Self-review

- **Spec coverage:** by-nature detection incl. all safety lines (T1, tested against the real DS2 fixture) · categories via ChipKind (T1 kinds + T4 chips/sort) · `Form="loose-root"` + decima + manifest opt-in (T2, T5) · reversible in-place toggle via existing DirectInject holding machinery (T2) · intake validate-then-extract (T3) · proxy warn-and-proceed (T4) · vanilla-launch participation (T4) · ownership/coexist untouched (existing posture logic applies; no changes needed) · Phase-2 diff seam (T1's signal design). DS2 re-curation split to the feed (T5), sequenced after merge.
- **Placeholder scan:** T1 carries complete test + reference code; T2–T4 are integration tasks with exact anchors, consumed signatures, and concrete test lists (the style that ships here) — no TBDs.
- **Type consistency:** everything flows the `DirectInjectMod` shape end-to-end (detector → listing → toggle → rows); category = `Kind`; the one flagged uncertainty (the record's exact fields) is called out with a read-first instruction in both the constraints and T1.
- **Law check:** no deletes, validate-then-extract, camelCase sidecar untouched-or-tested, both flavors, Core pure.
