# Shipping single-game support updates — design (2026-05-31)

## 1. The question, and the framing that answers it

When support for a single game changes — a new known mod, a fingerprint fix, a brand-new game profile — how should that reach friends? Two named options: ship it as a new app **version** through the existing Velopack pipeline, or ship it as a separate installable **package / definitions feed** that updates independently of the binary. Plus any hybrid.

You can't answer that until you stop saying "game support" as if it's one thing. It's three layers, and they have different physics:

1. **Game definition / profile** — how to detect and add a game: engine key, mod path, save root/subpath, Steam app id, required launcher, file extensions, grouping rule. **Already data-capable.** `GameProfileDraft` is a JSON record (`Core/GameProfileImport.cs`), the Add-with-AI flow pastes agent-produced JSON, and Steam auto-add (`SteamGameImport`, shipped v0.4.0) builds these from installed games.

2. **Catalog entries** — known mods/frameworks/tools for a game: fingerprint signature files, config paths, attribution, self-provides-proxy flags. **Currently compiled C# static arrays** — `KnownDirectInjectMod.Catalog`, `KnownFramework.Catalog`, `ToolCatalog`, plus `PopularGames.All` and `KnownEngines.ByAppId`.

3. **Engine enable/disable logic** — the actual code that knows how to toggle a pak vs a DLL vs an esp vs a Lua mod, reversibly. **Tested logic in Core** (Scanner, DirectInject, framework installers). It is **not data** and must ship as code.

The whole decision turns on one fact this framing exposes: **layer 3 must ship as a version no matter what.** The fork only lives in layers 1–2. So the real question is narrow — "do the data layers want their own distribution channel?" — and the answer depends on whether the cost of that channel buys anything the existing pipeline doesn't already deliver.

It doesn't. Not at this stage. Here's the work.

## 2. The three approaches, compressed

**A — Version-only (formalized).** Catalog stays compiled C#. Single-game changes ride the existing tag → DRAFT → Publish → 24h-poll pipeline as a PATCH bump. The only new things you build are a documented catalog-only patch lane and a per-game `CHANGELOG-games.md`. *Decisive pro:* zero net-new trust surface — executable-adjacent data (fingerprints, install paths) stays inside the one signed artifact, gated by the compiler and xUnit. *Decisive con:* every per-game tweak is a full release ritual (edit, test, tag, CI, Publish), and a friend on an old version has stale support, full stop.

**B — Data pack / definitions feed.** Layers 1–2 become signed JSON the app pulls from a remote feed and merges over the compiled baseline; layer 3 still ships as a version. *Decisive pro:* data-half updates reach users in minutes with no Velopack run, no Publish click. *Decisive con:* it creates a brand-new, independently-poisonable supply-chain surface over exactly the executable-adjacent data you most want gated — to solve a problem the signed ~1.3 MB delta already solves.

**C — Hybrid: data-shaped, version-bundled, feed-ready.** Convert the compiled catalogs to camelCase JSON loaded at startup through a validated, tested loader behind `ICatalogSource` — but bundle that JSON inside the release and keep shipping it as a version, so a `RemoteCatalogSource` is later a one-class addition. *Decisive pro:* draws the data/logic seam while the catalogs are small, and a future `RemoteCatalogSource` is additive, not a rewrite. *Decisive con:* it trades a free, perfect, always-on compiler check for a hand-rolled runtime validator you have to keep as good — real plumbing cost, no user-visible payoff, for optionality toward a feed it explicitly refuses to ship.

## 3. Scoring + the adversary's verdict, honestly

| Approach | Ship speed | Security | Drift risk | Infra cost | Law fit | Friend UX | Total |
|---|---|---|---|---|---|---|---|
| **A — Version-only** | 4 | 5 | 5 | 5 | 5 | 4 | **28** |
| **B — Definitions feed** | 3 | 1 | 2 | 1 | 3 | 3 | **13** |
| **C — Hybrid** | 4 | 4 | 4 | 3 | 5 | 5 | **25** |

The judge picked A. The reasoning that matters: the problem a feed exists to solve — cheap, fast data shipping — is **already solved** by the signed Velopack delta. v0.4.0 shipped tonight with a ~1.3 MB delta and a 24h auto-update poll. So B's machinery buys you a shaved CI cycle and pays for it with a new trust surface over executable-adjacent data. That asymmetry is the honest-builder argument against B, and it's why B sits at 13/30 — bottom on security and infra cost, a textbook no-premature-infrastructure violation.

**The adversary attacked C, and the blows landed.** Represented straight, because they're right:

- **C relocates an already-drawn seam.** The data/logic split exists *today* as a file boundary — edit `Catalog/*.cs` = data, edit `Scanner.cs`/`DirectInject` = logic. C doesn't create a missing seam; it moves an existing one from "which file" to "which serialization format" and charges a loader + validator + DTO tax to do it. The migration-now justification rests on a 50-games future that may never arrive at friends-scale with one maintainer.

- **C's validator defends against a bug class C itself introduces.** Today every catalog invariant — required fields, non-empty fingerprints, relative paths, no bundled-binary URLs — is a compile error or an existing test. C ships JSON inside the *same signed release*, so the validator's whole threat model is "the maintainer typo'd JSON he wrote himself" — a risk that exists only *because* C chose JSON over an array that physically cannot miss a required field.

- **C softens the project's core discipline.** The engineering identity is `TreatWarningsAsErrors` + nullable-on + compile-time guarantees. C's own con admits it trades a free, always-on compiler check for a hand-rolled validator that must be *maintained* to stay as good. For one maintainer, that validator can rot or carry a gap the compiler never would have allowed.

- **C's best card is available without the migration.** Machine-enforcing honor-the-builders (reject bundled-binary URLs on every load) is an `Assert.All` over the compiled static array — an afternoon, with the compiler still backing it. C's strongest pro is reachable in A-land for the price of one test file.

The adversary's conclusion: C does **not** survive as the move-to-make-now. I agree. C is a genuinely good *second* move — it just isn't earned yet, and pretending it is would be building insurance you may never cash.

## 4. Recommendation for this stage

**Ship Approach A. Formalize it, bank the adversary's one cheap win, and write down the trigger that earns C.** No remote feed (B) until scale outgrows one maintainer baking the catalog — which it hasn't.

The reasoning, plainly:

- **The user outcome is already delivered.** Per-game updates reach friends as a ~1.3 MB delta through one auto-update channel they already trust. A doesn't approximate that — it *is* that, with the catalog gated by the compiler and xUnit on the way out.
- **A bends none of the four operating laws and reinforces law 4.** Layer-3 logic must version anyway; keeping layers 1–2 versioned alongside means a friend's enable/disable logic and their catalog data can never disagree. That version-skew class is exactly what B invites and what A makes structurally impossible.
- **C is right tool, wrong stage.** The seam is already drawn at the file boundary; moving it to a serialization boundary is plumbing with no user payoff and a softened safety net. Hold it for when the recompile-to-ship-data shape becomes real friction, or a non-maintainer wants to contribute.
- **B stays off the table** until there's a second maintainer or a community-PR pipeline. A remote feed over executable-adjacent data is a trust surface you don't take to save a CI cycle.

This is the honest-builder call: build almost nothing, bend nothing, and don't manufacture a problem the delta already solved.

## 5. Phased plan

### Phase 0 — now, before the next single-game update (Windrose)

A is mostly process, not architecture. Three small, cheap moves:

1. **`docs/CHANGELOG-games.md`** — per-game changelog grouped by game name, not by release. One line per catalog/profile change: `v0.5.1 — Windrose: added detection for X loader; fixed Y pak grouping.` Lets a friend scan "what changed for my game" without reading the whole app changelog. Surface it in the GitHub release body.

2. **`CatalogInvariantsTests` over the compiled catalogs** — this is the adversary's one good idea, banked cheaply. `Assert.All` across `KnownDirectInjectMod.Catalog`, `KnownFramework.Catalog`, `ToolCatalog`, `PopularGames.All`, `KnownEngines.ByAppId`: no bundled-binary URLs, non-empty fingerprints, relative-paths-only config paths (no `..`, no absolute), unique ids, valid engine keys. Machine-enforced honor-the-builders **with the compiler still behind it** — C's strongest pro for an afternoon, zero migration.

3. **The catalog-only PATCH-lane runbook in `docs/RELEASE.md`** — when a tag changes *only* layers 1–2 (catalog arrays / `GameProfile` data / detection signatures) and touches *no* layer-3 enable/disable logic, it's a PATCH bump (third digit, or the fourth Velopack digit `v0.5.0.1`) the reviewer can fast-track. Write it as a 5-minute checklist so there's no "do I dare cut a release for one entry?" hesitation. That hesitation is the real friction — kill it with a documented ritual, not new code.

### Shipping Windrose under Phase 0

- New game profile: wire the engine key in Scanner/EngineDetect (layer 3, if needed) and add the `GameProfileDraft`. Pure-detection profile = mostly data.
- New known mod/framework/tool: append one record to the relevant compiled array; cover it with a test in `tests/ModManager.Tests/Catalog/` (the `catalog-entry-reviewer` agent already gates the shape).
- Add the `CHANGELOG-games.md` line under a `Windrose` heading.
- `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (never bare root — the WinUI project hangs it).
- `git tag v0.5.1 && git push origin v0.5.1` → CI builds the DRAFT → click Publish. Friends get the ~1.3 MB delta within their 24h poll.

### Phase 1 — convert to C (data-shaped, version-bundled) only when a trigger fires

Build the `ICatalogSource` loader + `CatalogValidator` + camelCase DTOs, bundle the JSON as an embedded resource, refactor the five call sites behind the existing surface. Estimate a focused day or two given the `GameProfileDraft`/`AtomicJson` foundation already exists. **Do not build this on spec.** Triggers, any one of:

- **(a)** A non-maintainer is submitting catalog entries and "compiled C# + tag a release" is a real barrier for them.
- **(b)** Catalog edits become frequent enough — measured, not vibes; daily-ish — that the recompile-to-ship-data shape is genuine, repeated friction.
- **(c)** You actually decide to build the remote feed (Phase 2), in which case the loader is the prerequisite.

`ICatalogSource` is no harder to introduce then than now — that's the whole reason it's safe to defer.

### Phase 2 — the remote feed (B), only if scale genuinely outgrows one maintainer

A `RemoteCatalogSource` behind the Phase 1 interface, with everything B's security analysis demands and none of it optional: detached signature over canonical bytes with the public key pinned in the binary; feed-sourced paths re-validated through the *same* forbidden-paths gate as user archives (the feed must never widen it); `minBinaryVersion` gating; reversibility stays code-enforced in layer 3, never data-driven; hard-reject any feed entry that tries to carry a binary. This is a real supply-chain surface — take it only when there's a second person who needs to ship support without building a binary. Not before.

## 6. Non-goals — do not build yet

- **No remote definitions feed / `RemoteCatalogSource`.** No new network fetch of catalog data, no public definitions endpoint, no second update channel. (Phase 2, trigger-gated.)
- **No JSON-ification of the catalogs.** Catalogs stay compiled C# static arrays. No `CatalogLoader`, no `CatalogValidator`, no DTOs, no embedded-resource wiring. (Phase 1, trigger-gated.)
- **No signing key / key-management story for definitions.** One maintainer, no HSM, no rotation plan — don't create a key you'd then have to protect for a feed you aren't shipping.
- **No schema-versioning / `minBinaryVersion` machinery.** Data and logic version together by construction; there is no skew to gate against until there's a feed.
- **No moving layer-3 (engine logic) toward data — ever.** Reversibility and enable/disable logic stay compiled, tested C#. This is a law, not a phase.
- **No "your support is N versions stale" in-app nudge.** Real gap (portable/dev builds never auto-update; the version number is the only signal), but it's a separate UX feature, not part of this distribution decision. Log it as a smoke/UX item if it bites.

The trigger to revisit this whole call is concrete: a second contributor, or measured daily catalog churn, or a real decision to ship a feed. Until one of those is true, A is the only model that requires bending nothing and building almost nothing — and it already does the job.
