# Microsoft Store submission — 0.27.0.0 (Patch Day, honest vanilla, save-copy offer)

> **Covers two GitHub releases.** The Store is on 0.25.0.0. This submission carries 0.26.0 (Patch Day,
> vanilla mode fixed) and 0.27.0 (the save-copy offer, Bring my mods back without launching). No new
> capability, no manifest extension, no new endpoint, no new data.
>
> **Package to upload:**
>
> ```text
> File      : src/ModManager.App/AppPackages/ModManager.App_0.27.0.0_Store_Test/
>             ModManager.App_0.27.0.0_x64_Store.msixbundle
> Size      : 83.8 MB
> SHA-256   : 25C111B80F586367CB19C54336660D0BA26EDD5F7C8F827C6622072B087A891A
> Identity  : 626LabsLLC.626ModLauncher
> Publisher : CN=177BCE59-0966-4975-9962-10E36652141F
> Version   : 0.27.0.0            (application x64)
> Capability: runFullTrust  — the only one declared; no manifest extensions
> Target    : Windows.Desktop, min 10.0.17763.0
> Seal      : OK — plugin loader + EAC-disable absent; Nexus compiled in
> Tests     : 4164 passing (Core)
> Submodule : external/626-mod-plugins pinned at nexus-v0.14.0 (87a1bdc)
> Source    : master 8e54d4e plus the version bump in this commit
> ```
>
> Identity, version and capability above were read **out of the bundle**, not off the manifest on disk.
> The build printed one warning: `mspdbcmf.exe` not found, so no symbols package (`.appxsym`) was made.
> That affects crash-dump symbolication only, not the package.

## Packaged-build smoke: passed 2026-11-08

Run on Neb against the submission bundle's own files, registered under a throwaway identity
(`626LabsLLC.626ModLauncherSmoke`) with `Add-AppxPackage -Register` in Developer Mode, beside the Store
0.25.0.0 install. The only signing key with a private half had expired (2026-10-25), and loose
registration runs byte-identical binaries with real package identity, so no cert was needed. The
`ModManager.App.dll` hash matched the bundle's.

On Elden Ring:

- **Play vanilla** (`LaunchMenuPlayVanilla`): launched through Steam with EasyAntiCheat. ERSS2, ReShade,
  `SeamlessCoop\`, `dinput8.dll`, `d3d12.dll`, `dxgi.dll` and every `mods\` DLL set aside.
  `ersc_launcher.exe` stays, by design: a launch target, inert without `SeamlessCoop\`.
- **Bring my mods back (don't launch)**: "Your mods are back. Nothing launched." No process started.
  The game folder diff against the pre-launch snapshot was **empty** (450 files).
- **Play modded**: launched through Seamless and loaded every mod (the Nov 4 patch errors came from
  the mods' authors). After exit, only game-written logs and settings differed.

The test package was removed after its private folder was confirmed to hold none of the user's files.
Found in passing, not blocking: Play modded from modded mode writes no status line, so a previous
"Nothing launched." stays up while the game runs (filed for the next Patch Day slice).

## What changed for a user

- **Patch Day** (0.26.0): warning before Play when a game updates past a version-locked loader, three
  choices, a one-game restore point taken first.
- **Vanilla is vanilla** (0.26.0, #407): every mod steps aside, the game starts through Steam with
  anti-cheat on. Seamless Co-op is no longer launched in vanilla.
- **Save-copy offer** (#408): copy a character into an empty save from another mode, with warnings
  and an undo snapshot. Offer again lives in the Saves dialog (#409).
- **Bring my mods back (don't launch)** (#409).
- **Restore points** name their kind, keep three undo snapshots per game, and report an interrupted
  restore.

## What's new in this version

Paste [`store/whats-new-0.27.0.txt`](store/whats-new-0.27.0.txt): **1,140 of 1,500**, opening with the
`Version 0.27.0` header.

## Short description and description

- [`store/short-description-0.27.0.txt`](store/short-description-0.27.0.txt): unchanged from 0.25.0.
- [`store/description-0.27.0.txt`](store/description-0.27.0.txt): two bullets added under *What it
  does*, after the on-and-off bullet: playing without mods, and the Patch Day warning.

## Product features

[`store/product-features-0.27.0.txt`](store/product-features-0.27.0.txt): **20 of 20**, longest
**173**. A playing-without-mods line added at 5; the two restore-from-backup lines (19 and 20) merged
into one to make room.

## Certification notes

Paste the letter block of [`store/reviewer-letter-0.27.0.0.md`](store/reviewer-letter-0.27.0.0.md)
(3,368 characters).

## Screenshots

The seven published screenshots carry forward unchanged. A Patch Day chip screenshot would help the
listing but needs a game in the patched state to capture; not blocking.

## Store half of the release

As 0.25.0.0: copy into `store-listing-console/apps/mod-launcher/copy/`, config to `0.27.0.0`, then plan,
apply, captions, verify; the owner does Packages, Notes for certification and Properties; verify; commit
from the console. GitHub release `v0.27.0` is published only once the public catalog serves 0.27.0.0.
