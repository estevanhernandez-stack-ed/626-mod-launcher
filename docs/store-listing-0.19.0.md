# Microsoft Store submission — 0.19.0.0 (one place for what's true)

> **This is a UI-shape submission.** The launcher does the same things to a user's files as 0.18.1;
> what changed is where it says them and how it asks. **No new capabilities, no new network
> endpoints, no new data collection.** `runFullTrust` remains the only declared capability.
>
> **One thing a reviewer WILL notice:** the toolbar and Settings both look different. Section
> headings were renamed, Settings was regrouped, and eight scattered warnings became one row of chips
> above the mod list. Say so up front rather than letting them find it.
>
> **Package to upload:** build and verify from master `c644025` or later.
>
> ```text
> Identity : 626LabsLLC.626ModLauncher
> Publisher: CN=177BCE59-0966-4975-9962-10E36652141F
> Version  : 0.19.0.0
> Capability: runFullTrust (only)
> Seal     : verify before upload — expect "loader + EAC-disable absent; Nexus compiled in"
> ```

---

## The build command changed. Read this before you build.

`-p:StoreNexus=true` is **no longer needed** — `Configuration=Store` turns it on by itself.

The old flag was opt-in, and `release-msstore.yml` never passed it. The documented manual procedure
did (step 5 of the 0.18.1 checklist), so hand-built submissions carried Nexus; anything CI produced
would not have. That gap is closed in two places:

- The csproj defaults `StoreNexus=true` for `Configuration=Store`. `-p:StoreNexus=false` still builds
  the sealed Nexus-free variant if it is ever wanted.
- **`check-store-seal.ps1` now fails when Nexus is missing.** It only ever asserted what must be
  ABSENT, and a check that only asserts absence passes a build with nothing in it. Both halves now.

So the seal is a real gate on this submission, not a formality. If it says
`Nexus compiled in`, the package genuinely contains the Nexus source.

## What changed for a user

Ordered by what a reviewer or a shopper would notice first.

**One row of chips replaced eight scattered warnings.** Anti-cheat risk, missing launch options,
missing frameworks, setup drift, a Steam update, co-op problems and Vortex ownership used to render
in two different visual registers with three different dismiss behaviours — and the one that can cost
someone their game account was the smallest thing on the screen. They are now one row above the mod
list, ordered by consequence, with the most serious one reading as a full sentence without being
tapped.

**The MP / SP buttons no longer move files.** They looked like a view filter and were a bulk file
operation: pressing MP enabled every multiplayer mod and disabled everything else. They filter now.
Applying a set is a separate, named action that says what it will change first.

**Bulk operations save your setup first.** `Enable all` / `Disable all` move files reversibly but
destroyed the *knowledge* of which mods were on. They now save a profile first and say where it went.

**Settings is four groups and a footer** — Appearance, Accounts, Restore points, Reset — instead of
one scroll with nine headings and "About" in the middle. Inventories moved to where the things
actually live.

**The in-app Nexus browser stops disappearing.** It used to vanish whenever it could not be used,
which made the app look like it had never had one. It now stays visible and names the one step that
would make it work.

**Keyboard:** Ctrl+, Settings · Ctrl+O add mods · Ctrl+P profiles · Ctrl+1/2/3 the show filter.

## Certification notes

Paste [`store/reviewer-letter-0.19.0.0.md`](store/reviewer-letter-0.19.0.0.md) into the
certification-notes box.

The letter leads with the visual change, because a reviewer comparing to the last approved build will
see a different toolbar and a different Settings page, and an unexplained difference invites the
question of what else was not mentioned.

## Screenshots

**They are now wrong in a way that matters, and this is the release to fix it.**

The 0.17 set in `docs/store-assets/screenshots-0.17/` shows the Forge theme (a colour difference —
argued in the 0.18.1 listing as acceptable under requirement 10.1, since the app really can look like
that). That argument does **not** stretch to this release: the toolbar section headings, the Settings
layout and the warning strip are all different now. Those screenshots show a **layout this build does
not have**, not merely a colour it can wear.

**Recommendation: retake before submitting.** Shoot on the main box — the 0.17 set shows six games
with real cover art and the clean test PC has almost nothing in it. All seven or none; a half-swapped
set reads as a rendering bug. Screenshot debt is tracked as D2 in `docs/2026-08-05-backlog.md`.

## Age rating

Unchanged. No user-generated content is displayed in-app beyond mod names and descriptions the user
already has on disk or fetches from a source they connected themselves.

## Before you upload

1. **Retake the screenshots** (see above). This is the blocking item, not the package.
2. Confirm the submodule pointer — `external/626-mod-plugins` is pinned at **`nexus-v0.14.0`**. The
   Store SKU compiles Nexus in from that pin; the GitHub SKU downloads a signed plugin from the feed.
   They do not advance together. See `docs/release-msstore.md`.
3. Wipe `src/ModManager.App/AppPackages/` so a side-load test package cannot overwrite the submission
   bundle (this bit us on 0.15.0.0).
4. **Bump `Version` in `src/ModManager.App/Package.appxmanifest` to `0.19.0.0`.** `-p:Version` does
   NOT set the MSIX package version — it is hardcoded there. A build passing `-p:Version=0.18.1.0`
   once produced a `0.17.0.0` package.
5. Build: `dotnet build src/ModManager.App/ModManager.App.csproj -c Store -p:Platform=x64`
   — **no `-p:StoreNexus` needed any more.**
6. `pwsh scripts/check-store-seal.ps1` — must report **`Nexus compiled in`**, not just "seal OK".
7. **Verify the identity by reading it out of the bundle**, never off the manifest on disk.

## Known gaps, stated rather than hidden

- **Installing a framework over one already present in a different layout produces two copies** and
  reports success. Found by driving the flow on a real install; written up in
  `docs/2026-08-19-framework-install-over-existing.md`. Not a regression — the check has never
  existed — and it needs a product decision. It does not affect a clean install.
- Three smoke cases remain human-only; see `docs/smoke-tests/smoke.json`.
