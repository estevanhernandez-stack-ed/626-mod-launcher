# Notes for certification — reviewer letter — v0.20.0.0 (move a modded setup to another PC)

> **Verified against the real package, not against master.** Identity, version and capability below
> were read out of the built bundle (`AppxBundleManifest.xml` and the inner `AppxManifest.xml`):
> `626LabsLLC.626ModLauncher`, `0.20.0.0`, x64, `runFullTrust` as the only declared capability. The
> seal script reports the loader and EAC-disable mechanism absent and Nexus compiled in.
> SHA-256 `0EA9E7DAD1A5476631D75908BD2620989284CCE3CC5F64824B2837A4AA095F8E`.
>
> **The honest headline, and it is the opposite of 0.19.0's.** That release looked different and
> behaved the same toward a user's files. This one looks almost identical and *does something new to
> files*: it can write a backup of a user's saves and mods, and put one back. A reviewer comparing
> screenshots will see very little; the change is behind two buttons in Settings. Saying that first is
> the point of this letter.

---

```text
Hello reviewer,

Thank you for your time on version 0.20.0.0.

WHAT A REVIEWER SHOULD LOOK AT FIRST

This version adds the ability to back up a user's game mods, save files
and app settings into a single file, and to restore chosen parts of that
file later. The user picks where the backup is written and which file to
read back.

Unlike the previous submission, the visible layout is almost unchanged.
The new work sits behind two buttons in Settings. What is new is the
BEHAVIOUR: for the first time, this app can write files into a game's
save folder and mod folders from a file the user supplies. That is the
part worth examining, so it is stated up front rather than left to be
found.

WHAT THIS DOES NOT ADD

- No new capabilities. runFullTrust remains the only one declared, the
  same as the last approved version.
- No new network endpoints. A backup is an ordinary file written to and
  read from a location the user chooses. Nothing about this feature
  contacts any service, and nothing is uploaded.
- No new data collection. The app still has no telemetry and no account
  of its own.
- No runtime code loading. The Nexus integration remains compiled into
  this package rather than downloaded, verified by the seal described
  under VERIFICATION.

HOW THE RESTORE IS CONSTRAINED

The restore is the only part of this release that writes to a user's
files from an external source, so it is guarded in five ways:

1. It is explicit. The user opens a backup, is shown what it holds, and
   ticks which games and which parts to restore. The screen that reads a
   backup has no restore control on it until something is ticked.

2. It confirms. The first press arms the action and names how many games
   it is about to change; a second press performs it. Changing any
   selection disarms it, so a confirmation cannot act on a different set
   than the one it named.

3. It does not delete. Save files are copied to a timestamped snapshot
   before they are replaced. Mod files are written over what is present
   rather than the folder being cleared, because a game's mod folder also
   holds the game's own content.

4. It refuses while the game is running, and fails closed - if the app
   cannot determine whether the game is running, it treats that as
   running and does nothing.

5. It treats the backup as untrusted input. Every entry in the file is
   checked to resolve inside its intended destination before anything is
   written, so a malformed or hostile file cannot cause a write outside
   the folders the user selected.

WHAT A BACKUP CONTAINS

A backup holds the user's own save files, the mod files they installed,
and this app's per-game settings. Two points worth stating plainly:

- Files identified as sign-in credentials are EXCLUDED from a backup, and
  the app reports how many were excluded and why.
- One file that forms part of a working save folder can contain a Steam
  account identifier. It is carried, because the save folder does not
  work without it - and the app says so on screen, so a user knows the
  file identifies them and should not be posted publicly.

WHY runFullTrust IS STILL REQUIRED

Unchanged from previous submissions. The app manages mod files inside
game installation folders chosen by the user, which are outside any
sandboxed location, and it launches games through Steam. The backup
feature reads and writes those same user-chosen locations.

VERIFICATION

The submitted package was checked with a build-time seal script that
reads the compiled binaries and asserts both that no runtime code-loading
mechanism is present and that the Nexus integration is compiled in rather
than downloaded. Both are verified for this build.

The restore behaviour described above is covered by an automated test
suite (2450 tests) and was additionally exercised against real game
folders before submission, with the affected files verified byte for byte
against an independent checksum manifest taken beforehand.

Thank you again.
```

---

## Why this letter leads with the file-writing behaviour

0.19.0's letter led with a visual change, because a reviewer comparing to the last approved build
would see a different toolbar and an unexplained difference invites the question of what else went
unmentioned. This release inverts that: **almost nothing looks different, and the behaviour toward a
user's files genuinely changed.** A reviewer skimming the screenshots could reasonably conclude this
is a maintenance release. Leading with "this can now write into your save folders, here is how it is
constrained" is both more accurate and less likely to raise a question later.

## What is deliberately not in the letter

- Internal wave numbering, PR references and format-version details. A reviewer does not need our
  sequencing.
- The same-filename-in-two-mod-folders limitation. It is a scanner limitation that predates this
  feature, affects only which of two identically-named files is archived, and cannot cause a wrong
  write — but answer it honestly if asked about backup completeness.
- The framework-install-over-existing gap, carried over from 0.19.0 and unrelated to this submission.
  Same rule: not volunteered, answered honestly if raised.
