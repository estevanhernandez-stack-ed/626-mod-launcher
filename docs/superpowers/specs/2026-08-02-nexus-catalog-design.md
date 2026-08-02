# In-app Nexus catalog (browse / discover) — design

**Date:** 2026-08-02
**Status:** Spec (approved in-conversation). GitHub/FULL-only feature. Spans both repos — a new optional plugin capability (Abstractions 0.12.0 + nexus-v0.12.0) so the catalog can exclude adult content server-side without touching the existing loose-identify search. Composes with the existing intake flow.

## The problem

Discovery today is a round-trip out of the app: the **"Find mods"** dropdown opens Nexus/CurseForge search **in the browser** (scoped to the active game); the user finds a mod, comes back, drags the file in. We now have live authenticated Nexus access (OAuth) and already read Nexus's catalog via `IModTextSearch.SearchAsync` (loose-identify). Bring the *finding* in-app — search Nexus mods for the active game, see results without leaving the launcher — while the *download* stays a browser handoff.

## Locked decisions

- **Browse/discover only.** The **download stays a browser/`nxm://` handoff** — Nexus API downloads are Premium-gated + AUP-constrained, and our operating law keeps downloads in the browser (honors-the-builders: land the user on the author's page to endorse/donate). "Get" opens the mod's Nexus page; it never fetches a file in-app.
- **No adult content in the catalog.** The in-app catalog **excludes adult/mature mods entirely** — no listings, and **no note or notification** about them (users go straight to Nexus in their browser for that). Rationale: surfacing adult content in-app would pull us into age-verification; excluding it keeps the app clear of any age-gating. **Intake is unaffected and content-agnostic** — if the user downloads an adult mod from Nexus themselves and drops it in, the app installs/manages it like any file. The line is: we don't *surface/browse* adult content in-app; we don't police what the user brings.
- **Adult exclusion is server-side, scoped to the catalog only.** A new plugin capability filters adult mods out of the catalog query. The existing `IModTextSearch.SearchAsync` used by **loose-identify is untouched** — it keeps matching everything the user already owns (including adult) so identify still names their own files.
- **Text-forward / lean rows.** Reuse `SourceSearchHit` fields (name, author, summary, endorsements, url). Thumbnails/categories/sort are deferred (would need richer DTO fields).
- **GitHub/FULL-only.** Gated on the plugin capability; absent on the sealed Store build. **No `#if FULL`; seal untouched.**
- **AUP discipline.** Search fires on submit (not per-keystroke), scoped to one game domain, self-timeout, no bulk crawl.

## Contract (Abstractions 0.12.0) — new optional interface

Add a **separate optional interface** (the `IModTextSearch` / `IAuthorizedSend` precedent — never modify existing interfaces/DTOs; old plugins keep loading):

```csharp
/// <summary>
/// Optional catalog-browse capability: search a game's mods for in-app discovery, with adult/mature
/// content EXCLUDED server-side (so the launcher never surfaces it and needs no age-gating). Distinct
/// from IModTextSearch.SearchAsync, which stays unfiltered for identifying the user's own files.
/// </summary>
public interface IModCatalog
{
    Task<IReadOnlyList<SourceSearchHit>> SearchCatalogAsync(string gameDomain, string query);
}
```

Reuses `SourceSearchHit` (no DTO change — adult exclusion is in the query, not a field). `IModTextSearch` and all DTOs untouched → loose-identify and the shipped 0.11.0 plugin are unaffected (ABI-safe).

## Plugin (nexus-v0.12.0)

`NexusModSource : IModSource, IModTextSearch, IAuthorizedSend?` also implements `IModCatalog`. `SearchCatalogAsync` runs the same GraphQL v2 mods search as `SearchAsync` **plus an adult-content-exclusion filter**. **CONFIRM at build:** the exact Nexus GraphQL field/filter for excluding adult mods (e.g. an `adultContent`/`containsAdultContent` filter on the mods search) — verify against the live schema before shipping, do not assume. `SearchAsync` is left exactly as-is (loose-identify unchanged).

## UI (launcher)

- The existing **"Find mods"** DropDownButton gains a first item **"Browse Nexus (in app)"**; the current browser items ("Find mods on Nexus Mods", "Find mods on CurseForge") stay as the full-site fallback.
- The item opens a **browse dialog** (`ContentDialog`, mirroring `LooseIdentifyDialog`), titled for the active game:
  - **Search box** (+ submit / Enter) at top.
  - Scrollable **results list**; each row: **name** (bold), **author**, **♥ endorsements**, one-line **summary**, a **Get** button (opens the mod's Nexus `Url` in the browser) + a secondary **View on Nexus** link.
  - States: initial ("Search Nexus for {game} mods"), loading ("searching…"), empty ("No results for '{query}'"), error ("Couldn't reach Nexus — try again").

## Data flow

1. Open Browse Nexus → dialog for `ActiveGame`'s Nexus domain (`NexusDomains.Effective(_ctx.Game)`).
2. Query + submit → `((IModCatalog)NexusSource).SearchCatalogAsync(domain, query)` → adult-excluded `SourceSearchHit`s.
3. Render. **Get** → open `hit.Url` in the default browser (existing `LauncherService`/`Process.Start`).
4. User downloads on Nexus → drags the file onto the window → **existing intake** installs it (unchanged).

Runs host-side through the plugin like loose-identify (plugin builds the request; host attaches the OAuth bearer via `IAuthorizedSend`; GraphQL search also works unauthenticated). Per-call self-timeout (~10s) so a hung request can't wedge the dialog; never throws → empty/error state on failure.

## Gating (flavor guard)

`CatalogAvailable => NexusActionsAvailable && NexusSource is IModCatalog && ActiveGameHasNexusDomain`. The capability check IS the flavor gate: on STORE / zero-plugins the registry is empty → false → item absent. Also false until nexus-v0.12.0 is delivered (older plugins don't implement `IModCatalog`) — so the catalog lights up when the new plugin arrives via the feed, no launcher release needed at that point. **No `#if FULL`; seal unaffected.**

## Architecture / units

- **Abstractions**: `IModCatalog` (new file or appended to Contract.cs), ABI-safe.
- **Plugin**: `NexusModSource` implements `IModCatalog.SearchCatalogAsync` (adult-excluding GraphQL); `NexusPlugin` unchanged (already registers the source). csproj → Abstractions 0.12.0. release.yml minBinaryVersion as appropriate.
- **VM (`MainViewModel`)**: `Task<IReadOnlyList<SourceSearchHit>> SearchCatalogAsync(string query)` (resolves the active game's domain, calls the source's `IModCatalog`, self-timeout, never throws) + `CatalogAvailable`/`CatalogVisibility`.
- **View (`NexusCatalogDialog.xaml(.cs)`)**: search box + results list + Get; bind `SourceSearchHit` (or a thin row VM).
- **Wiring (`MainWindow`)**: the menu item + click → open the dialog.

No Core changes, no new persisted shape.

## Release coupling (human-gated)

Launcher release (publishes **Abstractions 0.12.0**) → **plugin nexus-v0.12.0** (implements `IModCatalog`, resolves 0.12.0 from NuGet) → the feed delivers the new plugin → the catalog lights up for FULL installs. Same order/pattern as the OAuth / loose-identify plugin cuts. Dev: local-pack Abstractions 0.12.0 to build the plugin before it's on NuGet.

## Error handling

- Search failure/timeout/disconnect mid-session → dialog error state; never throws.
- Empty query → no-op.
- (Adult exclusion is server-side; the launcher never sees adult hits, so there's nothing to filter or message client-side.)

## Testing

- **Abstractions**: a contract test that `IModCatalog` has the expected shape and `IModTextSearch`/DTOs are unchanged (ABI-safe), mirroring `AuthorizedSendContractTests`.
- **Plugin**: TDD `SearchCatalogAsync` — asserts the GraphQL query carries the adult-exclusion filter (character-match against the verified query), routes through the shared transport, and (via a stub host) returns hits; `SearchAsync` (identify) is byte-for-byte unchanged.
- **Launcher/App**: build (FULL + STORE) + STORE seal green; the dialog + search are App UI → a `docs/smoke-tests/pending.md` entry: Browse Nexus for a game with a domain → search → results (no adult listings) → Get opens the mod page; item absent on Store / no-domain / disconnected / pre-0.12.0-plugin.

## Non-goals (deferred)

- Thumbnails, categories, sort/filter (richer DTO — later).
- A default "trending/latest" grid (query-driven contract).
- An `nxm://` one-click install handler.
- Any in-app adult content, or any age-gating UI (excluded by construction).
- Compiling the catalog into the Store build (the separate Store-Nexus swing, held until the UX Store version publishes).
- CurseForge in-app catalog (Nexus first; CurseForge stays the browser fallback).

## Success criteria

- On the GitHub build with Nexus connected, the nexus-v0.12.0 plugin loaded, and a game that has a Nexus domain: "Browse Nexus (in app)" searches Nexus and shows **adult-free** results in-app; Get lands the user on the mod's Nexus page; the downloaded file drops into the existing intake unchanged.
- Loose-identify still matches everything the user owns (unchanged — `SearchAsync` untouched).
- On the Store build (or no plugin / no domain / disconnected / older plugin), the item is absent and the browser fallbacks remain; seal stays green.
- The shipped 0.11.0 plugin still loads on the 0.12.0 host (ABI intact — `IModCatalog` is additive).
