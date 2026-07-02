# Nexus name-search identify for loose-root mods — design

**Date:** 2026-07-01
**Status:** Spec (brainstormed in-conversation; grounded against the identify machinery + the plugin contract; Nexus GraphQL v2 availability web-verified). Awaiting review → writing-plans. **Two-repo feature:** a launcher PR (contract + Core + dialog) and a companion Nexus-plugin PR/release in `626-mod-plugins`, sequenced launcher-first.

## The problem

Loose-root rows (PR #169 — DS2/Decima: `Zipliner_v1.1`, `DollmanMute`, `ShaderToggler`, `renodx-deathstranding2`, …) carry no metadata: no real title, author, mod page, endorsements, or update checks. **Nexus md5 identification cannot work here** — Nexus indexes the *published archive's* hash, and these files are long since extracted (the codebase documents the same limit for other extracted mods, `MainViewModel.cs:1477`). The existing name-search tier is **CurseForge-only** (`Scanner.RefreshMetadataByNameAsync`, `Scanner.cs:1201`), and DS2's mods live on Nexus. The stems, however, are highly searchable on the game's Nexus domain.

## Approved decisions

1. **Review-first, batch approve.** Matches are *proposed*, never silently attached: one dialog, one proposal per row, checkbox each, Apply writes only what the user approved. (Name-search is guessy; the curation-batch trust model applies.)
2. **Two-repo scope accepted.** The Nexus plugin implementation lives in `626-mod-plugins`; "done" = launcher merge + plugin release through the feed. The launcher side ships dormant until the updated plugin arrives.
3. **Separate optional interface — no plugin ABI break.** The new capability is NOT added to `IModSource` (extending a shipped interface breaks already-installed plugin binaries at type-load). Installed plugins keep working; the feature lights up per-plugin via a type check.

## Component A — the contract (launcher, `ModManager.Plugins.Abstractions`)

```csharp
/// <summary>Optional text-search capability. A source that can search its catalog by name for a game
/// domain implements this ALONGSIDE IModSource. The App feature-detects via a type check —
/// older plugins without it keep loading and working unchanged.</summary>
public interface IModTextSearch
{
    Task<IReadOnlyList<SourceSearchHit>> SearchAsync(string gameDomain, string query);
}

public sealed record SourceSearchHit(
    string GameDomain, int ModId, string Name, string? Author,
    string? Summary, int? EndorsementCount, string? Url);
```

App-side resolution: `NexusSource is IModTextSearch search` — null-plugin (Store) and old-plugin cases degrade to the action being hidden/disabled.

## Component B — the plugin implementation (`626-mod-plugins` repo)

The Nexus plugin implements `IModTextSearch` against **Nexus GraphQL v2** (`api.nexusmods.com/v2/graphql` — the official, documented search surface; v1 REST has no text search; verified via graphql.nexusmods.com). `mods` query filtered by name + game domain, mapped to `SourceSearchHit`. Transport unchanged: `IPluginHostServices.HttpClient` + `GetCredential("nexus")` + AppVersion header; key never persisted.

**Verify-at-implementation caveat (the GOG-epoch pattern):** confirm the exact GraphQL query shape, the domain filter argument, and whether the name filter works with the user's API key (or unauthenticated) against the live endpoint before shipping; adjust the mapper to ground truth. A failing/unavailable search returns an empty list — never throws to the App.

**Release coupling:** the plugin release sets `minBinaryVersion` to the launcher version that carries `IModTextSearch`; the feed delivers it. Until then the launcher feature stays dormant by design.

## Component C — the Core proposal pass (pure, TDD)

`LooseIdentify` (Core, new):

- **Candidate selection** from the game's loose-root rows: `Location == "loose-root"`, **excluding** loaders (`Class/Kind == "loader"` — searching "dxgi"/"version" is noise by construction), rows already identified (existing `NexusModId` or non-null confidence), and **always excluding `IsManual`** (the lock is absolute).
- **Query build:** the row's `Base` stem through the existing `NameMatch.CleanModName` (already strips version tokens: `Zipliner_v1.1` → `Zipliner`).
- **Match pick:** plugin `SearchAsync` hits through the existing `NameMatch.PickBestMatch` (Jaccard ≥ 0.5, the established threshold). Below threshold → a no-match proposal (shown greyed, unchecked, uncheckable).
- **Output:** `IReadOnlyList<LooseIdentifyProposal>` — `(ModKey, CleanQuery, SourceSearchHit? Match)`. Pure data; no writes. The plugin call is injected (`Func<string,string,Task<IReadOnlyList<SourceSearchHit>>>` or the interface) so Core stays plugin-agnostic and testable with fakes.
- **Apply:** `LooseIdentify.ToMeta(hit)` maps an approved hit to a `ModMeta` (Title, Author, Url = the mod page, NexusModId, EndorsementCount, `SourceConfidence = "nameSearch"`); the App writes approved entries via the existing `Scanner.WriteManyMeta` keyed by `ModKey` (= `Base` — the key `MergeMetadata` already resolves for loose rows). The existing `MergeMeta` never-clobber discipline applies on any later re-identify.

## Component D — the review dialog + trigger (App)

- **Action:** "Identify loose mods on Nexus…" in the game **More** menu beside the existing Nexus items, visible under the existing `NexusActionsVisibility` gating **and** only when `NexusSource is IModTextSearch` and the active game has loose-root rows + a `NexusGameDomain`. No new `#if FULL` — the plugin gate is the flavor gate, as for every Nexus surface.
- **Dialog:** one row per proposal — stem → proposed match (title · author · summary · endorsement count), checkbox (checked by default for matched rows), greyed "no confident match" rows, a "game domain missing" early-out message when `NexusGameDomain` is null. **Apply** writes checked rows (`WriteManyMeta`) then reloads the mod list; **Cancel** writes nothing.
- **Payoff cascade (no new code):** once `NexusModId` is on the row — endorsement hearts, update-check chips, stats refresh (`NexusRefresh` resolves by `NexusModId`), and the off-boarding sheet's "likely source:" hedge for `nameSearch` confidence all work via existing machinery. Wrong match → the existing "Match to a mod…" manual flow corrects and locks (`IsManual`).

## Ride-alongs

- **Restore DS2's `nexusGameDomain`** (`deathstranding2`) — lost in the re-add; one-line `games.json` edit with the app closed. Root cause note: the manual Add dialog never sets a Nexus domain; the queued **DS2 feed re-curation** (post-#169) carries `nexusDomain: deathstranding2` so fresh adds get it from the manifest.
- **Branch:** stacks on `feat/loose-root-mods` (operates on loose rows); lands after #169 merges (rebase onto master if #169 is already in).

## Error handling

- Plugin search failure / timeout / empty → proposals show "no confident match"; the dialog never blocks on a row; the action never throws to the shell.
- No `NexusGameDomain` → honest early-out message, no search fired.
- Plugin without `IModTextSearch` (old plugin) → action hidden (or disabled with "plugin update pending" tooltip — implementer's pick, match existing menu conventions).
- Apply is all-or-nothing per checked set via `WriteManyMeta` (atomic write of metadata.json — existing path).

## Testing

- **Core (TDD):** candidate selection (loader/manual/identified exclusion — fixture = the real DS2 loose rows incl. `dxgi`/`version` loaders), stem cleaning (`Zipliner_v1.1` → `Zipliner`), threshold behavior (match vs no-match proposals), `ToMeta` mapping (incl. `sourceConfidence: "nameSearch"` and camelCase round-trip via the existing ModMeta shape), injected-search fakes (empty, throwing → skipped as no-match).
- **App:** build both flavors + seal (the Abstractions change must not disturb the Store build) + a smoke entry: on the live DS2 list, run the action, expect proposals for the plugins/shaders (not the loaders), approve a subset, rows gain title/author/hearts; a wrong match corrected via manual match stays locked afterward.
- **Plugin (in `626-mod-plugins`):** its own repo's test conventions + the live-endpoint verification step.

## Non-goals

- No auto-attach path (review-first is the only write path; a future "auto for exact matches" needs its own decision).
- No CurseForge text-search changes; no changes to md5/fingerprint tiers.
- No search for loaders, manual rows, or already-identified rows.
- No OAuth flow — the user's existing API key transport only; if the GraphQL name filter proves key-incompatible, the plugin returns empty (feature degrades honestly) and we revisit.

## Success criteria

- On DS2, the action proposes correct matches for the searchable stems (Zipliner, DollmanMute, DS2Fix, ShaderToggler, RenoDX) and proposes nothing for `dxgi`/`version` loaders.
- Only approved matches attach; they carry `nameSearch` confidence + `NexusModId`; hearts/update-chips/stats light up via existing machinery.
- A manual match is never overwritten by a later identify run.
- Old plugin / Store flavor: the action is absent; nothing breaks; STORE seal holds.
- Both flavors build; CorePurity green; no new persisted shapes (ModMeta reused).

## Repo-law checklist

- **No ABI break:** new capability = separate optional interface; shipped plugins load unchanged.
- **No secrets:** the user's key stays in the existing credential flow; nothing persisted by the plugin.
- **camelCase on disk:** metadata.json entries reuse `ModMeta` (existing shape + tests).
- **Core purity:** `LooseIdentify` is pure with injected search; UI in App.
- **Honest degradation:** every failure path ends in "no match proposed" or a clear message — never a silent wrong attach (the review gate makes silent-wrong structurally impossible).
