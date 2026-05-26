# Direct-inject + ME2 metadata-merge gap — Design Spec

**Date:** 2026-05-26
**Status:** Approved (Este, in-chat — explicit "Full TDD subagent loop")
**Branch:** `fix/direct-inject-and-me2-metadata-merge`
**Predecessor:** [`2026-05-26-identify-direct-inject-and-manual-match-design.md`](2026-05-26-identify-direct-inject-and-manual-match-design.md) — the metadata IDENTIFY path landed in PR #40. This is the row-RENDER half.

## Why

Smoke test of PR #40 against real Elden Ring + Seamless Co-op revealed metadata still doesn't surface on rows — no icon, no Nexus title, no author credit. Investigation showed: **the identify chain worked**. `metadata.json` at `_626mods/elden-ring/metadata.json` contains the right entries:

```json
{
  "Seamless Co-op": {
    "title": "Seamless Co-op (Elden Ring)",
    "author": "Yui",
    "url": "https://www.nexusmods.com/eldenring/mods/510",
    "image": "https://staticdelivery.nexusmods.com/mods/4333/images/510/...png"
  },
  "Ultrawide / Widescreen Fix": { ... },
  "DLL mod loader": { ... },
  "ERSS2 Frame Gen": { ... }
}
```

But the row builder never merges that map onto the displayed rows.

## Root cause

`MainViewModel.ReloadModsAsync` (src/ModManager.App/ViewModels/MainViewModel.cs:238-242) branches three ways:

```csharp
IReadOnlyList<Mod> list;
if (ConfigBacked) list = _me2.ListMods(_ctx.Game);
else if (directInject) list = _direct.List(_ctx.Game);
else list = await ReloadFromScannerAsync();
```

- **Scanner branch** ✅ merges. `Scanner.ListWithClassAsync` → `Metadata.MergeMetadata(mods, LoadMetadata(c))` at Scanner.cs:582.
- **Direct-inject branch** ❌ does NOT merge. `DirectInjectService.List` returns bare rows with `Mod.Image=null, Description="Detected: ..."`.
- **ME2 branch** ❌ does NOT merge. `ModEngineService.ListMods` returns bare rows from the TOML parse.

The structural class: the merge lives inside ONE of the three list builders (the scanner), invisible to the other two. The non-scanner branches never call it.

This isn't an artifact of PR #40 — it's a pre-existing gap PR #40 made visible by being the first feature to populate `metadata.json` for a non-scanner-backed engine. Before today, fromsoft games had no metadata source at all, so the missing merge was silent.

## Fix

Promote `Metadata.MergeMetadata` from a per-list-builder internal step to a uniform post-branch step in `ReloadModsAsync`. Three-line change:

```csharp
IReadOnlyList<Mod> list;
if (ConfigBacked) list = _me2.ListMods(_ctx.Game);
else if (directInject) list = _direct.List(_ctx.Game);
else list = await ReloadFromScannerAsync();

// Always merge metadata.json into the row list. The scanner branch did this internally too;
// re-merging is idempotent (same map → same fields), so the double-merge for scanner-backed
// games is safe and the direct-inject + ME2 branches now pick up Nexus / CF identifies the
// same way Windrose does.
list = Metadata.MergeMetadata(list, Scanner.LoadMetadata(_ctx));
```

Why post-branch instead of inside each service: the alternative (push the merge into `_direct.List` + `_me2.ListMods`) requires both services to learn about `Scanner.LoadMetadata` + `GameContext`. The VM already holds `_ctx` — that's the right seam.

## TDD

The bug exists in the VM seam; WinUI VMs aren't unit-testable here. The regression pin lives at the Core level: a test that takes a row list shaped like `_direct.List` output (catalog name as both `Name` and `Base`, `Image=null`, `Description="Detected: ..."`), merges a metadata map keyed by the catalog name, and asserts the merged row picks up `Title`, `Image`, `Author`, `ModUrl`, `Description` (the "Detected" filler gets replaced with the Nexus description).

This pins the merge contract for catalog-keyed rows. It doesn't prove the VM CALLS the merge — only manual smoke can do that — but it ensures that when the VM does call it, the merge works for this row shape.

## Scope

- Direct-inject (Elden Ring + future FromSoft direct-inject games).
- Mod Engine 2 (FromSoft games configured with `modEngineConfig`).
- Both fixed by the same one-line change at the VM seam.

Out of scope:
- Refactoring services to take a `GameContext` for internal merging. The VM-seam fix is cheaper and equivalent.
- Adding metadata identify paths for additional engines (covered by the audit doc / future per-engine work).

## Risk

Very low. The scanner branch's double-merge is idempotent (verified by reading `Metadata.MergeMetadata` — every assignment is the same for the same input). The new merge for direct-inject + ME2 unlocks the metadata.json entries that already existed.

The one watch-out: rows from `_me2.ListMods` set `m.Base = m.Name`. Catalog-keyed direct-inject rows from `_direct.List` do the same. Both work with `Metadata.MergeMetadata`'s lookup order (`metaMap[m.Base]` first → `metaMap[m.Name]` fallback). Either match writes the same fields.
