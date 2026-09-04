# Microsoft Store submission — 0.22.0.0 (the folders a removed game left behind)

> **This submission covers two releases.** 0.21.0 shipped to GitHub only, so the Store goes from
> 0.20.0 straight to 0.22.0 and carries both. Everything below treats them as one submission and says
> which release each thing came from.
>
> **What a reviewer WILL notice, said up front:** 0.20.0 taught the app to *write* into a user's save
> and mod folders. This one teaches it to **permanently delete a folder of the user's files on
> request**. It is one small section in Settings and the screenshots barely change, which is exactly
> why it leads the letter. Every guard around it is described under
> *[The half that deletes](#the-half-that-deletes)*.
>
> **No new declared capabilities, no new network endpoints, no new data collection.** `runFullTrust`
> remains the only capability, verified out of the bundle below. The leftover-folders feature is
> entirely local. The one other new outward behaviour is opening a mod's page in the user's default
> browser — a URL handed to the shell, not a request the app makes.
>
> **Package to upload:**
>
> ```text
> File      : src/ModManager.App/AppPackages/ModManager.App_0.22.0.0_Store_Test/
>             ModManager.App_0.22.0.0_x64_Store.msixbundle
> Size      : 83.6 MB
> SHA-256   : 3D959C32029800F26852BE5567113DB937A1552CC49B2F831FA01EB54DC15885
> Identity  : 626LabsLLC.626ModLauncher
> Publisher : CN=177BCE59-0966-4975-9962-10E36652141F
> Version   : 0.22.0.0            (application x64)
> Capability: runFullTrust  — the only one declared
> Target    : Windows.Desktop, min 10.0.17763.0
> Seal      : OK — plugin loader + EAC-disable absent; Nexus compiled in
> Tests     : 2508 passing
> Submodule : external/626-mod-plugins pinned at nexus-v0.14.0 (87a1bdc)
> ```
>
> Identity, version and capability above were read **out of the bundle** — `AppxBundleManifest.xml`
> and the inner package's `AppxManifest.xml` — not off the manifest on disk. A build passing
> `-p:Version=0.18.1.0` once produced a `0.17.0.0` package, which is why that distinction keeps being
> written down rather than assumed.

---

## What changed for a user

**Folders left behind (0.22.0).** Turning a mod off moves its files to a holding folder rather than
deleting them. Remove the game from the launcher and that folder stayed on disk, holding the user's
files, referenced by nothing and shown nowhere — which sits badly beside a promise that disabling
moves files rather than deleting them. Settings now lists them, with three actions on each: **show
the files**, **save a copy**, and **remove**. Seven exist on the maintainer's machine; two of the
seven were created by launcher bugs since fixed. It is not a disk-space feature, it is an honesty
one.

**Remove deletes permanently, and says so.** It asks first, names the folder and a freshly counted
file total, says the deletion cannot be undone and includes any profiles and settings in that folder,
offers saving a copy right beside it, and defaults to keeping. It never acts on more than the one
folder picked.

**The updates list takes you to the mod (0.22.0).** A row with a newer version now offers **Get
update**, which opens that mod's page. A row the launcher could not match to a specific mod shows no
button at all rather than a greyed one — a greyed button invites a hover looking for an explanation
that does not exist.

**The Add Game picker offers every curated game (0.21.0).** It listed only 18 of 156 before; a game
curated since was invisible in the one surface built for finding curated games. It now offers every
game the launcher carries a workable definition for — 116 on the maintainer's machine — with the ones
it can see installed ranked to the top. **Ranking, not filtering:** a game the launcher cannot detect
sits lower in the list, never absent, because a detection miss should cost a scroll rather than the
game.

**Picking a game now keeps its curation (0.21.0).** A registered game finds its engine, mod path,
save layout and ban risk by matching its id, and that id used to be the display name slugified. Two
of five sampled entries lost everything — no engine, no mod path, no warning. A pick now records
*which* game was picked and carries that id through registration.

**A game no longer has to be on Steam (0.21.0).** A curated entry can key on its slug instead of a
Steam app id, so a game sold on EA, Epic, GOG or the Microsoft Store needs no app release.

## The half that deletes

The remove path is the highest-consequence code in this release and the part a reviewer should
scrutinise. Six guards, all implemented, each one for a specific way this could hurt someone:

- **It only ever sees folders this app created.** The scan walks the app's own `_626mods` holding
  roots and only the roots a currently-registered game points at. It never scans drives. A root whose
  parent is not literally named `_626mods` is rejected even when a hand-edited `games.json` points at
  it — without that gate, a `DataDir` aimed at an ordinary folder would turn every sibling inside it
  into an offer to permanently delete it.
- **A registered game's live folder is never listed** — guarded twice, by game id *and* by the actual
  leaf folder name on disk, then re-checked against a fresh scan at the moment of the confirm. A
  folder that stopped being left over while the section was open is not deleted; the app says so and
  removes nothing.
- **Never bulk.** No remove-all, nothing preselected, nothing grouped. One folder, one decision.
- **The confirm names it and counts it fresh.** Folder name plus a file total counted at that moment
  rather than reused from the listing, the word permanent, and "Keep it" holding focus so Enter and
  Escape both keep. **If it cannot count the folder it refuses to open the confirm at all** — it
  cannot honestly name what it would delete.
- **Save a copy is offered alongside**, takes the whole folder, refuses to write into an
  existing folder of that name rather than merging over it, and refuses a destination inside the
  folder being copied.
- **A copy and a remove can never run at once.** The busy guard is `static`, so it outlives the
  Settings dialog — closing Settings and reopening it mid-copy does not buy a second concurrent
  action on the same tree.

### What the real-machine run proved, and what it did not

`scripts/smoke-leftovers.ps1` is read-only and registers nothing. It proved all seven orphans listed
and none of the fifteen registered games listed — 6 of 6 cases. The updates surface showed 3 rows
with 3 correctly-named buttons.

**The write half is deliberately unexercised on real folders.** Remove, Save a copy and Show files
were never clicked against the maintainer's own leftover folders, because those are his files and
what happens to each is his decision. The guards are individually covered by the automated suite;
`docs/smoke-tests/pending.md` carries the manual steps, including three hover states that are
headless-untestable.

## What's new in this version

Paste [`store/whats-new-0.22.0.txt`](store/whats-new-0.22.0.txt) into Partner Center's **What's new in
this version** field, verbatim. **1,158 of 1,500 characters**, opening with the `Version 0.22.0`
header the field expects.

It is a separate file for the same reason as every release before it: that field is plain text with
no markdown rendering, so neither this document nor a GitHub release body fits it — pasting either
produces a wall of literal `#` and `|` on the storefront. Written for a shopper: what changed for
them, no internal names.

**It carries both releases without saying so.** A shopper does not care which of our tags a change
came from; the leftover folders and the updates button lead because they are what a user meets first,
and the picker and non-Steam work follow.

## Short description and description

Paste [`store/short-description-0.22.0.txt`](store/short-description-0.22.0.txt) (**238 of 270**) and
[`store/description-0.22.0.txt`](store/description-0.22.0.txt) (**4,363 of 10,000**).

**The short description is 0.20.0's, carried forward unchanged.** It is still accurate, and the 32
characters left over are not enough to say anything true about leftover folders without cutting
something that is doing more work. Changing it to change it would cost the "back your whole setup up
and move it to another PC" clause that is one release old.

**The description gains a sentence, not a paragraph.** 0.20.0 added a fourth prose paragraph because
moving a setup between machines was a genuinely new thing the app did. This is not that: listing the
folders a removed game left behind is the *completion of the promise the first paragraph already
makes* — your files are yours, turning a mod off moves it aside rather than deleting it. So it lands
as one sentence at the end of that paragraph rather than as a fourth rule the app is built on. Three
list entries changed:

- **New:** the leftover folders, worded so "remove" and "asks first and names what it deletes" arrive
  in the same breath.
- **New:** the picker offering every game it can set up, with the non-Steam fact folded in.
- **Extended:** the updates line now ends "and opens that mod's page when you want it."

The Nexus paragraph also gains four words for the same reason — the updates promise it makes was
previously true and unfinished.

**Not changed: the search terms.** Still the seven from 0.8.1, for the reason recorded in the 0.19.0
note — adding "nexus" would help discovery and is somebody else's trademark, so it stays a deliberate
decision rather than a quiet edit.

## Product features

Paste [`store/product-features-0.22.0.txt`](store/product-features-0.22.0.txt) — one per line, in
order. **20 of the 20 Partner Center allows**, longest **147 of 200** characters.

0.20.0 filled the list exactly, so this release needed **displacement, not extension**:

- **Extended at no cost:** the updates line now covers getting to the mod page too, and line 1 picks
  up "a game does not have to be on Steam." Both facts land inside lines that already existed.
- **Added:** one line for the leftover folders.
- **Displaced:** *"Themes the whole app to taste."* It was the weakest of the twenty — the only line
  about how the app looks rather than what it does with your files, and the one a shopper is least
  likely to choose this manager for. It survives in the description's list, so nothing is lost, only
  demoted.

## Certification notes

Paste [`store/reviewer-letter-0.22.0.0.md`](store/reviewer-letter-0.22.0.0.md) into the
certification-notes box.

**The letter leads with the delete.** 0.20.0's led with "this can now write into your save folders,
and here is how that is constrained." This release's equivalent is blunter: an app whose stated
promise is that it never deletes your files now has a button that permanently deletes a folder of
them. Six numbered constraints follow, then the one other new outward behaviour (opening a mod page
in the default browser), then what the release does *not* add, then a short section naming what
0.21.0 changed so it does not arrive unannounced.

Identity, version, capability, seal and submodule pin in the letter's header were read out of the
built bundle, not off the manifest on disk.

## Age rating

Unchanged. No user-generated content is displayed in-app beyond mod names and descriptions the user
already has on disk or fetches from a source they connected themselves. The leftover-folders section
displays folder names and file counts from the user's own disk. The Get update button opens an
external site in the user's browser rather than rendering anything in-app.

## Screenshots

**The 0.20 set is the starting point** — `docs/store-assets/screenshots-0.20/`, nine shots, all
1920x1080.

| Shot | State for this submission |
|---|---|
| `01-library-home` | unchanged, reuse |
| `02-game-mods-view` | unchanged, reuse |
| `03-browse-nexus` | unchanged, reuse — carried from the 0.19 set, still the only signed-in capture |
| `04-updates-view` | **recapture** — rows now carry a Get update button, so the 0.20 shot no longer matches the build |
| `05-add-game` | **recapture** — the picker offers 116 games, not 18; the 0.20 shot shows the old list |
| `06-settings` | **recapture or replace** — Settings has a new section |
| `07-saves-snapshots` | unchanged, reuse |
| `08-back-up-everything` | unchanged, reuse |
| `09-inside-a-backup` | unchanged, reuse |

**A shot of the Folders left behind section is needed and has not been captured.** No 0.22 screenshot
directory exists yet. It is the headline feature of the release and there is currently no image of
it — that is a gap in this submission, not something already handled. Capturing it means the section
listing real leftover folders, and it is worth deciding beforehand whether that listing names games
the maintainer would rather not put on a public storefront page (the same conscious-yes call `09` in
the 0.20 set needed).

**The Flyout trap still applies.** The confirm on Remove is a `Flyout`, and the capture script's
reposition-before-capture step dismisses one — that is exactly how the 0.20 set photographed the
screen behind shot 9. Any shot of the remove confirm must mark itself `Fragile`.

## Build procedure

1. **Capture the missing screenshots** into `docs/store-assets/screenshots-0.22/` — at minimum the
   Folders left behind section, plus recaptures of `04`, `05` and `06`. Look at every one before
   committing it.
2. Confirm the submodule pointer — `external/626-mod-plugins` pinned at **`nexus-v0.14.0`**
   (`87a1bdc`). Note that as of 0.21.0 **Nexus is compiled into every build**, not only the Store
   one; the plugin loader remains off-Store only and has nothing of the launcher's own left to load.
   See `docs/release-msstore.md`.
3. Wipe `src/ModManager.App/AppPackages/` so a side-load test package cannot overwrite the submission
   bundle (this bit us on 0.15.0.0).
4. **Bump `Version` in `src/ModManager.App/Package.appxmanifest` to `0.22.0.0`.** `-p:Version` does
   NOT set the MSIX package version — it is hardcoded there.
5. Build: `dotnet build src/ModManager.App/ModManager.App.csproj -c Store -p:Platform=x64`
6. `pwsh scripts/check-store-seal.ps1` — must report **`Nexus compiled in`**, not just "seal OK". A
   check that only asserts absence would pass a build with nothing in it.
7. **Verify the identity by reading it out of the bundle**, never off the manifest on disk.

## Known gaps, stated rather than hidden

- **No screenshot of the release's headline feature exists yet.** See above. This is the one blocking
  item in this document.
- **The write half of the leftovers section has not been clicked on real folders.** Remove, Save a
  copy and Show files are covered by the automated suite and by a read-only smoke run against the
  real registry; the destructive path was not exercised against the maintainer's own files. Manual
  steps are in `docs/smoke-tests/pending.md`.
- **A library whose games have all been removed stays invisible.** Leftover detection only looks
  under holding roots that currently-registered games point at, so if every game is gone there is no
  root to walk. That is the correct trade — the alternative is a tool that offers to delete a
  directory it merely recognised — but it means the feature cannot help the one user who needs it
  most.
- **Games registered before 0.21.0 keep their old, name-derived id.** Nothing migrates them; a
  migration would have to guess which curated entry an old row meant, which is the guess 0.21.0
  exists to stop making. Re-adding the game is the fix.
- **A game whose files sit one folder below its Steam install is still not detected** — How to Fish
  is the repro — so it falls to manual setup even though it is curated. Unreal tolerates that shape;
  Unity does not yet.
- **Two mods with the same filename in two different mod locations of one game** are collapsed by the
  scanner into a single entry. Carried over from 0.20.0; needs scanner work, not a format change.
- **Installing a framework over one already present in a different layout produces two copies** and
  reports success. Carried over from 0.19.0, written up in
  `docs/2026-08-19-framework-install-over-existing.md`. Not a regression, and it does not affect a
  clean install.
- Smoke cases that remain human-only are listed in `docs/smoke-tests/smoke.json`.

---
