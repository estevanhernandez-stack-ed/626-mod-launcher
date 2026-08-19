# Microsoft Store submission — 0.19.0.0 (one place for what's true)

> **This is a UI-shape submission.** The launcher does the same things to a user's files as 0.18.1;
> what changed is where it says them and how it asks. **No new capabilities, no new network
> endpoints, no new data collection.** `runFullTrust` remains the only declared capability.
>
> **One thing a reviewer WILL notice:** the toolbar and Settings both look different. Section
> headings were renamed, Settings was regrouped, and eight scattered warnings became one row of chips
> above the mod list. Say so up front rather than letting them find it.
>
> **Package to upload — built and verified 2026-08-19 from master `5101293`:**
>
> ```text
> File      : src/ModManager.App/AppPackages/ModManager.App_0.19.0.0_Store_Test/
>             ModManager.App_0.19.0.0_x64_Store.msixbundle
> Size      : 83.5 MB
> SHA-256   : 9D21719D7118C6D0ABAD2CEAF8A5ADFB8771D71A21D8DB72DA264EA8310FEF01
> Identity  : 626LabsLLC.626ModLauncher
> Publisher : CN=177BCE59-0966-4975-9962-10E36652141F
> Version   : 0.19.0.0            (application x64)
> Capability: runFullTrust  — the only one declared
> Target    : Windows.Desktop, min 10.0.17763.0
> Seal      : OK — loader + EAC-disable absent; Nexus compiled in
> Tests     : 2235 passing (Release)
> ```
>
> Identity, version and capability above were read **out of the bundle** — `AppxBundleManifest.xml`
> and the inner package's `AppxManifest.xml` — not off the manifest on disk. A build passing
> `-p:Version=0.18.1.0` once produced a `0.17.0.0` package, which is why that distinction is written
> down rather than assumed.

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

## What's new in this version

Paste [`store/whats-new-0.19.0.txt`](store/whats-new-0.19.0.txt) into Partner Center's **What's new in
this version** field, verbatim.

It is a separate file on purpose. That field is plain text with a 1500-character limit and no markdown
rendering, so neither of the two documents that already describe this release fits it: the section
below is prose written for a reviewer, and `docs/release-notes-v0.19.0.md` is shaped for a GitHub
release body — headings, tables, links. Pasting either one produces a wall of literal `##` and `|`
characters on the storefront.

Written for a shopper, not a builder: what changed for them, no wave numbers, no internal names.

## Certification notes

Paste [`store/reviewer-letter-0.19.0.0.md`](store/reviewer-letter-0.19.0.0.md) into the
certification-notes box.

The letter leads with the visual change, because a reviewer comparing to the last approved build will
see a different toolbar and a different Settings page, and an unexplained difference invites the
question of what else was not mentioned.

## Screenshots

**Retaken 2026-08-19 against this build: `docs/store-assets/screenshots-0.19/`.** All seven, all
1920x1080, captured with `scripts/capture-store-screenshots.ps1 -Auto`, and every one looked at before
being committed.

Shot for shot, what they now show that `screenshots-0.18` did not:

| Shot | Now shows |
|---|---|
| `01-library-home` | unchanged — six games with real cover art, ban-risk and update badges |
| `02-game-mods-view` | `SHOW` / `MANAGE` / `GROUP BY`, both renamed doors, and the `UPDATED` chip with its sentence |
| `03-browse-nexus` | 20 cards, every thumbnail loaded, "20 of 363" |
| `04-updates-view` | the A10/A27 row fixes — no "unknown", no backwards arrows |
| `05-add-game` | over the library, not over the updates list |
| `06-settings` | the four-group shape — `Appearance` and `Accounts` headings |
| `07-saves-snapshots` | **Elden Ring, not Windrose** — real save files, three characters, Clone and Edit |

Shot 7 changed game deliberately. On Windrose that dialog is three lines of *"No save files / No
editable characters / No save mods installed"*, which is the opposite of the reversibility promise the
shot exists to make. Elden Ring is a FromSoft title, so the save format is itemised and the dialog
shows what the feature actually does.

### Two things to look at before uploading — your call, not blockers

- **`07` shows a real save path** containing your Windows username and the leading digits of a Steam
  ID, plus two character names. All truthful, none secret, and you publish under your own name anyway.
  Worth a conscious yes rather than a default.
- **`07` leads with `BAN RISK` and the anti-cheat sentence.** I would keep it: the Store SKU has the
  EAC-disable mechanism sealed out entirely, so a shot of the app *warning* about anti-cheat is the
  most accurate advert for that build there is. But it is a judgement call about first impressions.
**Fixed rather than shipped:** `04` used to show two rows where the installed version read newer than
the listed one. That was a real defect, not a capture problem — the pending rule was string
inequality and never asked which version was newer. Both rows are gone and the shot was retaken; the
badge went from 4 to 2. See `an update is not just a difference` in the git log.

### Two traps the capture script now handles

Both cost a set, and both are written into the script so they cannot cost another.

- **A 1px window border let the desktop through.** The visible frame sizes to exactly 1920x1080, but
  the client area inside it is 1918x1079 and the border is partly transparent. Every shot in the first
  automated set carried the same 117 stray bright pixels along the bottom — identical across six
  different screens, because it was never app content. The window is sized +2 and captured inset by 1.
- **Tooltips get photographed.** Parking the mouse over a control long enough to capture is exactly
  what a tooltip waits for; two shots caught one. The pointer is moved off-window and given time to
  fade before every capture.

## Age rating

Unchanged. No user-generated content is displayed in-app beyond mod names and descriptions the user
already has on disk or fetches from a source they connected themselves.

## Before you upload

1. **Upload `docs/store-assets/screenshots-0.19/`** — the full set of seven, retaken against this build.
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
