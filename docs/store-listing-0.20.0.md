# Microsoft Store submission — 0.20.0.0 (move a modded setup to another PC)

> **This is a capability submission, not a UI-shape one.** 0.19.0 changed where the launcher said
> things; this one adds something it could not do before: back up every game's mods, saves and
> settings into one file, read that file, and put chosen parts of it back.
>
> **No new declared capabilities, no new network endpoints, no new data collection.** `runFullTrust`
> remains the only capability, verified out of the bundle below. The archive is a file the user picks,
> written and read locally. Nothing is uploaded, and no new host is contacted.
>
> **What a reviewer WILL notice, said up front:** the app now *writes into save folders and mod
> folders* on an explicit restore. That is new behaviour under an existing capability, and it is the
> thing worth looking at hardest. Every guard around it is described under
> *[The half that writes](#the-half-that-writes)*.
>
> **Package to upload — built and verified 2026-08-30 from `chore/0.20.0-store-cut`:**
>
> ```text
> File      : src/ModManager.App/AppPackages/ModManager.App_0.20.0.0_Store_Test/
>             ModManager.App_0.20.0.0_x64_Store.msixbundle
> Size      : 83.6 MB
> SHA-256   : 0EA9E7DAD1A5476631D75908BD2620989284CCE3CC5F64824B2837A4AA095F8E
> Identity  : 626LabsLLC.626ModLauncher
> Publisher : CN=177BCE59-0966-4975-9962-10E36652141F
> Version   : 0.20.0.0            (application x64)
> Capability: runFullTrust  — the only one declared
> Target    : Windows.Desktop, min 10.0.17763.0
> Seal      : OK — loader + EAC-disable absent; Nexus compiled in
> Tests     : 2450 passing (Release)
> ```
>
> Identity, version and capability above were read **out of the bundle** — `AppxBundleManifest.xml`
> and the inner package's `AppxManifest.xml` — not off the manifest on disk. A build passing
> `-p:Version=0.18.1.0` once produced a `0.17.0.0` package, which is why that distinction keeps being
> written down rather than assumed.

---

## What changed for a user

**One file holds your whole setup.** Settings → *Back up everything* writes every registered game's
mods, saves and launcher settings into a single `.626profile`. On a real twelve-game library that is
1,664 files and 4.9 GB. Snapshot history is left out by default — it is backups of backups, and on
that same machine it was 446 MB of a 482 MB launcher-data total.

**You can read a backup before it touches anything.** *Look inside a backup…* says what the file
holds and how much of it this machine can already use — which games are set up here, which are waiting
on the game, and which mods are named in the backup but not installed. There is no restore button on
that screen until you tick something.

**You choose what goes back, per game and per part.** Saves, mods and settings are separate ticks, and
a part is only offered when the backup actually holds it.

**A game you have not reinstalled yet can be held.** The common case on a fresh Windows install is
that the backup holds twelve games and the machine has none of them. Those can be kept until their
game exists, and the game's own screen then offers to put it back. Nothing about *where* the game
lived is recorded — a game can come back on a different drive, in a different Steam library — so the
destination is worked out at the moment the game is registered, not when the backup was made.

**A shared world does not have to carry the person who played it.** Where the line between the place
and the player is known, a world can be shared without the character.

**Saves reach the public game list.** The [supported-games page](https://github.com/estevanhernandez-stack-ed/626-game-manifest/blob/main/SUPPORTED-GAMES.md)
now says what is known about each game's saves.

## The half that writes

The restore path is the highest-consequence code in this release and the part a reviewer should
scrutinise. What guards it:

- **Nothing is deleted.** Saves are snapshotted before they are replaced. Mods are added over what is
  there rather than clearing it, because a mod folder holds the game's own content intermixed with
  mods and emptying it would take the game with it.
- **It refuses while the game is running,** and fails *closed* — if it cannot tell whether the game is
  running, that counts as running. A folder changed under a live game is silently undone on exit,
  which reaches the user as "it didn't work" with nothing in any log to see.
- **Two presses, never one.** The first press arms and names the count it is about to act on; the
  second acts. Changing any tick disarms it, so a confirm cannot act on a different set than the one
  it named.
- **An archive is untrusted input.** Every entry is checked to resolve inside its destination by
  relative-path containment, not by a prefix match — a prefix accepts a *sibling* directory, so a root
  of `…/saves/pal` would otherwise take `…/saves/palworld-evil/x.sav`.
- **Paths are resolved at restore time,** never replayed from the archive.

### It has been run against real game folders

Not only against test fixtures. On the maintainer's own machine, three restores into real folders,
each preceded by an independent sha256 manifest and an out-of-band copy of every file involved:

| Target | Written | Result |
|---|---|---|
| `_626mods/witchfire` (settings) | 7 files | byte-identical |
| Witchfire `SaveGames` | 7 files | byte-identical, snapshot written first |
| Windrose mods × 3 locations | 81 files | byte-identical in all three |

The method: back up, restore that same backup, and require the tree to come back byte-identical, with
one file deliberately corrupted first so a restore that silently no-ops cannot pass. Written up in
`docs/smoke-tests/pending.md`.

## Privacy — what a backup contains, and what it deliberately does not

The archive holds the user's own save and mod files. It never leaves the machine unless the user moves
it. Two things are worth stating plainly because the file is the most portable artifact this app
produces:

- **Sign-in tokens are excluded and recorded as excluded.** Any file scanned as carrying a credential
  is left out, and the report names how many and why. On a machine with Cyberpunk 2077 that is
  `user.gls`, a CDPR token.
- **A Steam account id is carried, and disclosed.** `steam_autocloud.vdf` is part of a working save
  folder, so it travels — and the app says so on the screen rather than leaving someone to find out.
  It is disclosed precisely so a user knows not to post the file publicly.

No telemetry, no analytics, no account required for any of this.

## Build procedure

1. **Upload the existing screenshots** — `docs/store-assets/screenshots-0.19/`. The surfaces they show
   (toolbar, Settings groups, chip strip, mod list) are unchanged in 0.20.0; the new work is inside
   Settings behind two buttons. Retake if a reviewer asks to see the backup screen.
2. Confirm the submodule pointer — `external/626-mod-plugins` pinned at **`nexus-v0.14.0`**
   (`87a1bdc`). The Store SKU compiles Nexus in from that pin; the GitHub SKU downloads a signed
   plugin from the feed. They do not advance together. See `docs/release-msstore.md`.
3. Wipe `src/ModManager.App/AppPackages/` so a side-load test package cannot overwrite the submission
   bundle (this bit us on 0.15.0.0).
4. **Bump `Version` in `src/ModManager.App/Package.appxmanifest` to `0.20.0.0`.** `-p:Version` does
   NOT set the MSIX package version — it is hardcoded there.
5. Build: `dotnet build src/ModManager.App/ModManager.App.csproj -c Store -p:Platform=x64`
6. `pwsh scripts/check-store-seal.ps1` — must report **`Nexus compiled in`**, not just "seal OK". A
   check that only asserts absence would pass a build with nothing in it.
7. **Verify the identity by reading it out of the bundle**, never off the manifest on disk.

## Known gaps, stated rather than hidden

- **Two mods with the same filename in two different mod locations of one game** are collapsed by the
  scanner into a single entry before a backup ever sees them, so only one is archived. A scanner
  limitation rather than an archive one — the launcher cannot tell them apart for enabling either.
  Reviewed and accepted; it needs scanner work, not a format change.
- **Installing a framework over one already present in a different layout produces two copies** and
  reports success. Carried over from 0.19.0, written up in
  `docs/2026-08-19-framework-install-over-existing.md`. Not a regression — the check has never
  existed — and it does not affect a clean install.
- Three smoke cases remain human-only; see `docs/smoke-tests/smoke.json`.

---

## What's new (store copy)

> **Move your whole modded setup to another PC.**
>
> Back up every game's mods, saves and settings into one file, then put it back on a new machine —
> or a freshly reinstalled one. Read what a backup holds before anything is touched, and choose what
> goes back, per game and per part.
>
> Haven't reinstalled a game yet? Its mods wait until you do, and land wherever the game turns up —
> a different drive is fine.
>
> Nothing is deleted putting a backup back. Saves are snapshotted before they are replaced, mods are
> added over what is there, and a restore refuses while the game is running. Sign-in tokens are left
> out of backups, and the app tells you what a backup carries about you.
