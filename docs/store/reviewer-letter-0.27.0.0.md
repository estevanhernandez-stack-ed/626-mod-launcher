# Notes for certification — reviewer letter — v0.27.0.0 (Patch Day, honest vanilla, save-copy offer)

> **Verified against the real package, not against master.** Identity, version and capability below
> were read out of the built bundle (the inner `AppxManifest.xml`), not off the manifest on disk. The
> seal script reports the plugin loader and the EAC-disable mechanism absent and Nexus compiled in.
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
> Exes      : ModManager.App.exe, RestartAgent.exe (Windows App SDK). No MCP server.
> Endpoints : none added since 0.25.0.0 (host list diffed across the source)
> ```
>
> **The honest headline.** The first version since 0.25.0.0 to add behaviour, not just fixes. All three
> features work inside the lanes already approved: they move the user's own mod and save files, read
> local store records, and launch games through the user's store clients.

---

```text
Hello reviewer,

Thank you for your time on version 0.27.0.0.

WHAT CHANGED

1. Patch Day: a warning when a game updates past the user's mod loader.
   Many mods depend on a loader built for one version of the game. When
   the game updates and the loader has not, the mods often fail to load.
   The app now notices this before the user presses Play, by comparing
   the build number in the store client's local install record with the
   one it saw last, and offers three choices: play without mods for now,
   play with mods anyway, or wait. It records a restore point of that
   one game the moment it notices, and changes no files until the user
   chooses.

2. Playing without mods is now fully unmodded.
   The app could already set a game's mods aside for a session. Before
   this version, some kinds of mod stayed in place, and on one game it
   could start a mod's own launcher instead of the game. Now every mod
   is moved to the app's holding folder, the game always starts through
   the user's store client with its anti-cheat running normally, and the
   app launches nothing if it cannot find the game's own launch path.
   A new "Bring my mods back (don't launch)" puts every mod back, byte
   for byte, without starting the game.

3. An offer to copy a save between a game's modes.
   Some games keep one save file for unmodded play and another for a
   modded mode. When the user is about to play a mode whose save has no
   characters, the app offers to copy one over from another mode. Copies
   into unmodded play carry a warning, and copies from a heavily modded
   save also require ticking an acknowledgment. The save being replaced
   is snapshotted first, so the copy can be undone. "Don't ask again"
   can be reversed in the app's Saves dialog.

4. Restore points name what made them, and the app tells the user if a
   restore was interrupted, naming the snapshot that can undo it.
   Nothing is restored automatically.

WHAT THIS DOES NOT ADD

- No new capabilities. runFullTrust remains the only one declared, the
  same as the last approved version. No manifest extensions.
- No new network endpoints.
- No new data collection. Still no telemetry and no account of its own.
- No runtime code loading. The Nexus integration remains compiled into
  this package, verified by the seal described under VERIFICATION.
- No change to any game's own files or to its anti-cheat. The app moves
  only mod files and save files, and never disables, bypasses or alters
  anti-cheat software.

WHY runFullTrust IS STILL REQUIRED

Unchanged from previous submissions. The app manages mod files inside
game installation folders chosen by the user, outside any sandboxed
location, launches games through the user's store clients, and reads
store install records from the local machine.

VERIFICATION

The submitted package was checked with a build-time seal script that
reads the compiled binaries and asserts both that no runtime
code-loading mechanism is present and that the Nexus integration is
compiled in rather than downloaded. Both are verified for this build.

The behaviour described above is covered by an automated test suite
(4164 tests). Each feature was also driven end to end on a real game
through Windows UI Automation, and the game folder was compared file by
file before and after: every file returned unchanged.

Thank you again.
```

---

## Why the letter leads with what the user sees

Patch Day and vanilla mode both touch anti-cheat territory, so the letter says plainly that the app
never alters anti-cheat and that unmodded play now starts the game with it running. That is the
question a tester will have first.

## What is deliberately not in the letter

- The agent-access (MCP) work. Not in the package; the Store cut is a separate, later submission
  (spec `docs/superpowers/specs/2026-10-16-mcp-in-store-design.md`).
- Internal PR numbers. Same rule as every letter: answer honestly if asked.
- The packaged-build smoke as a method. The letter's "driven end to end on a real game" covers both the
  2026-11-07 debug-build runs and the 2026-11-08 run of this package's own binaries (empty game-folder
  diff after Bring my mods back); details in `docs/store-listing-0.27.0.md`.
