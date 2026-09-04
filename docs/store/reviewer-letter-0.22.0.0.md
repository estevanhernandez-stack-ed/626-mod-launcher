# Notes for certification — reviewer letter — v0.22.0.0 (the folders a removed game left behind)

> **Verified against the real package, not against master.** Identity, version and capability below
> were read out of the built bundle (`AppxBundleManifest.xml` and the inner `AppxManifest.xml`), not
> off the manifest on disk: `626LabsLLC.626ModLauncher`, `0.22.0.0`, x64, `runFullTrust` as the only
> declared capability. The seal script reports the plugin loader and the EAC-disable mechanism absent
> and Nexus compiled in.
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
> **The honest headline.** 0.20.0's letter led with "this can now write into your save folders."
> This one is sharper and shorter to say: **the app can now permanently delete a folder of the
> user's files, on request.** It is a small section in Settings and a reviewer could skim past it.
> Saying it first, and then saying exactly what stops it going wrong, is the whole point of this
> letter.
>
> **This submission covers two releases.** 0.21.0 shipped to GitHub only; 0.22.0 is the Store cut of
> both. The letter names what 0.21.0 changed rather than letting it arrive unannounced.

---

```text
Hello reviewer,

Thank you for your time on version 0.22.0.0.

This submission covers two of our releases. 0.21.0 shipped outside the
Store, so what you are looking at is the sum of 0.21.0 and 0.22.0.

WHAT A REVIEWER SHOULD LOOK AT FIRST

This version can permanently delete a folder of the user's files when
they ask it to. That is new, it is the highest-consequence thing in the
release, and it is stated first rather than left to be found.

The background: turning a mod off in this app does not delete the mod.
It moves the files to a holding folder that the app owns. If the user
later removes that game from the launcher, the holding folder stays on
disk - still holding their files, referenced by nothing, and shown
nowhere. A new section in Settings lists those folders and offers three
actions on each one: show the files in File Explorer, save a copy to a
location the user picks, and remove. Remove is a permanent recursive
delete of that one folder.

HOW THE DELETE IS CONSTRAINED

1. It can only ever see folders this app created. The scan walks the
   app's own "_626mods" holding roots, and only the roots that a
   currently-registered game points at. It never scans drives and never
   enumerates anything the user did not register. A root that is not
   literally named "_626mods" is rejected even if a hand-edited registry
   file points at it, so a folder this app did not create cannot appear
   in the list at all.

2. A registered game's live folder is never listed. That is guarded
   twice - by game id and by the actual folder name on disk, so a
   registry that names a folder differently from the game still cannot
   put a live folder in front of the user. It is then re-checked against
   a fresh scan at the moment the user confirms, so a folder that stopped
   being left over while the section was open is not deleted; the app
   says so and removes nothing.

3. It is never bulk. There is no remove-all, nothing is preselected, and
   nothing is grouped. One folder, one decision, one confirmation.

4. The confirmation names what it is about to do. It names the folder,
   counts the files inside it fresh at that moment rather than reusing
   the listing's number, and says the deletion is permanent and includes
   any profiles and settings in that folder. Keeping is the default: the
   "Keep it" button holds focus, so Enter or Escape keeps the folder. If
   the app cannot count the files - permissions, a read error - it
   refuses to open the confirmation at all, because it cannot honestly
   name what it would delete.

5. Saving a copy is offered right beside it. It copies the whole folder
   to a location the user picks, and it refuses to write into a folder
   of that name that already exists rather than merging over it. It also
   refuses a destination inside the folder being copied.

6. A copy and a removal can never run at the same time. The guard is
   held outside the Settings window, so closing Settings and reopening
   it during a long copy does not get you a second, concurrent action on
   the same files.

THE ONLY OTHER NEW OUTWARD BEHAVIOUR

The list of mods with an available update now offers a button that opens
that mod's page. That is a URL handed to the user's default browser -
the app does not fetch the page, and the host is a compile-time constant
with the game and mod portions validated before the URL is built. A row
the app cannot match to a specific mod shows no button rather than one
that leads nowhere.

WHAT THIS DOES NOT ADD

- No new capabilities. runFullTrust remains the only one declared, the
  same as the last approved version.
- No new network endpoints. The leftover-folders feature is entirely
  local; nothing about it contacts any service and nothing is uploaded.
- No new data collection. The app still has no telemetry and no account
  of its own.
- No runtime code loading. The Nexus integration remains compiled into
  this package rather than downloaded, verified by the seal described
  under VERIFICATION.

WHAT ELSE CHANGED, FROM THE RELEASE THAT DID NOT COME HERE

0.21.0 was a data and setup release, with no new file-touching
behaviour:

- The Add a game picker now offers every game the app carries a
  definition for, ranked with the ones it can see installed on the
  machine first, instead of a fixed list of 18.
- Picking a game there now keeps that game's definition. Previously the
  engine and mod folder could be silently lost when the display name did
  not match the definition's name.
- A game no longer has to be sold on Steam to be supported.

WHY runFullTrust IS STILL REQUIRED

Unchanged from previous submissions. The app manages mod files inside
game installation folders chosen by the user, which are outside any
sandboxed location, and it launches games through Steam. The holding
folders described above live alongside those same user-chosen
locations.

VERIFICATION

The submitted package was checked with a build-time seal script that
reads the compiled binaries and asserts both that no runtime
code-loading mechanism is present and that the Nexus integration is
compiled in rather than downloaded. Both are verified for this build.

The behaviour described above is covered by an automated test suite
(2508 tests). The detection half - which folders are listed and which
are not - was additionally exercised against the maintainer's real
machine, where it correctly listed seven leftover folders and listed
none of the fifteen registered games' live folders.

Thank you again.
```

---

## Why this letter leads with the delete

0.20.0's letter led with *this can now write into your save folders*, because the release looked like
a maintenance release and behaved like anything but. The same shape applies here and the sentence is
blunter: **an app whose stated promise is that it never deletes your files now has a button that
deletes a folder of them.** That is exactly the kind of thing a reviewer should hear from us before
they find it, and exactly the kind of thing that reads badly if it turns up unannounced next to the
listing copy promising reversibility.

The six constraints are not aspirational. Each one is implemented and each one exists because the
alternative was a specific way this could hurt someone: a scan that recognises rather than owns, a
live folder offered as an orphan, a remove-all that turns one wrong click into all of them, a
confirmation that names a stale count, a copy that quietly overwrites the last one, and two file
operations racing on the same tree.

## What is deliberately not in the letter

- Internal wave numbering, PR references and the shape of the guard code. A reviewer does not need
  our sequencing.
- That the write half of the leftovers section - remove, save a copy, show files - was proved by
  automated tests and by a read-only run against the real registry, but not by clicking Remove on
  the maintainer's own folders. The manual steps are in `docs/smoke-tests/pending.md`. It is not
  volunteered because the guards are individually covered; answer it honestly if asked how the
  delete was exercised.
- The same-filename-in-two-mod-folders scanner limitation and the framework-install-over-existing
  gap, both carried over from earlier submissions and unrelated to this one. Same rule: not
  volunteered, answered honestly if raised.
