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

**Only `04-updates-view` needs retaking.** Este's call, 2026-08-19, and the current set is
`docs/store-assets/screenshots-0.18/` — not the 0.17 Forge set the 0.18.1 listing had to argue about.
That set was already remediated; the theme discrepancy is gone.

I originally wrote here that all seven needed retaking because the layout changed. That was written
without opening them, and it was wrong for most of the set. Checked shot by shot:

| Shot | State against this build |
|---|---|
| `01-library-home` | unchanged by this release |
| `02-game-mods-view` | **six visible differences** — see below |
| `03-browse-nexus` | unchanged |
| `04-updates-view` | **retake** — the row fixes (A10 "unknown", A27 backwards arrows) landed after it |
| `05-add-game` | *"Apply profile"* now reads *"Apply definition"* |
| `06-settings` | *"Identity"* → *"Appearance"*, *"Nexus Mods"* → *"Accounts"* |
| `07-saves-snapshots` | unchanged |

### What `02-game-mods-view` shows that the build no longer has

Recorded so nobody later assumes it went unexamined. Este's call is that these do not block; the
evidence is here either way.

- Toolbar headings `LOADOUT`, `LIBRARY` and `VIEW` — now `SHOW`, `MANAGE` and `GROUP BY`.
- `Browse Nexus` and `Find mods` — now `Find mods (in-app)` and `Find mods in browser`.
- The Steam-updated notice as a **full-width bar with its own button**, which is exactly the pattern
  the game-state strip replaced. It is a chip with an expanded sentence now.

Under requirement 10.1 the question is whether a shopper is misled about what the app does. Every
control in that shot still exists and still does the same thing; four of them are differently worded
and one moved into the strip. That is a weaker discrepancy than 0.18.1's colour argument had to carry,
and it is not the kind of thing a reviewer rejects over — but if you are retaking `04` anyway, `02` is
the one worth the extra two minutes.

## Age rating

Unchanged. No user-generated content is displayed in-app beyond mod names and descriptions the user
already has on disk or fetches from a source they connected themselves.

## Before you upload

1. **Retake `04-updates-view`** (see above), and `02-game-mods-view` if you want it exact. The
   rest of `screenshots-0.18/` stands.
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
