# Notes for certification — reviewer letter — v0.25.0.0 (the game's own regulation.bin stays put)

> **Verified against the real package, not against master.** Identity, version and capability below
> were read out of the built bundle (`AppxBundleManifest.xml` and the inner `AppxManifest.xml`), not
> off the manifest on disk. The seal script reports the plugin loader and the EAC-disable mechanism
> absent and Nexus compiled in.
>
> ```text
> File      : src/ModManager.App/AppPackages/ModManager.App_0.25.0.0_Store_Test/
>             ModManager.App_0.25.0.0_x64_Store.msixbundle
> Size      : 83.7 MB
> SHA-256   : 648D451011DDB4CAAE894C5BCCAD71BA1DC116F888805BC143229EDB5004A9B9
> Identity  : 626LabsLLC.626ModLauncher
> Publisher : CN=177BCE59-0966-4975-9962-10E36652141F
> Version   : 0.25.0.0            (application x64)
> Capability: runFullTrust  — the only one declared; no manifest extensions
> Target    : Windows.Desktop, min 10.0.17763.0
> Seal      : OK — plugin loader + EAC-disable absent; Nexus compiled in
> Tests     : 3866 passing (Core), 42 of 42 UI Automation smoke checks
> Submodule : external/626-mod-plugins pinned at nexus-v0.14.0 (87a1bdc)
> Exes      : ModManager.App.exe, RestartAgent.exe (Windows App SDK). No MCP server.
> ```
>
> **The honest headline.** A fix release. Every change makes the app move fewer of the user's files or
> say more about what it did. Nothing reaches further than 0.24.0.0.

---

```text
Hello reviewer,

Thank you for your time on version 0.25.0.0.

WHAT CHANGED

1. A game's own settings file is no longer treated as a mod.
   On Elden Ring, the game ships a file named regulation.bin, and mods
   replace it. The app used to treat any file by that name as a mod, so
   turning off every mod, or resetting the game to unmodded, could move
   the game's own copy aside and stop the game starting. The app now
   counts it as a mod only when the app itself installed it; otherwise
   it is the game's file and stays in place on every path.

   Where the app cannot tell the game's copy from a modded one it did
   not install, its "return to an unmodded game" summary now says it
   left that file in place and suggests verifying the game's files
   before playing online. It never guesses either way.

2. Mods that keep extra files beside their main file now move as one.
   Cyberpunk 2077 mods that consist of a single .xl file are listed and
   can be turned off and on. Fallout 4 and Starfield mods take their
   .ba2 archives with them when turned off. As before, turning a mod off
   only moves its files to a holding folder; nothing is deleted. The
   game's own archives are never moved.

3. The ban-risk warning states the consequence (an account can be
   banned) without claiming every flagged game uses anti-cheat, since
   some publishers ban modded online play by policy alone.

4. "Enable all" continues past a mod it cannot turn on and names it,
   instead of stopping at the first refusal.

WHAT THIS DOES NOT ADD

- No new capabilities. runFullTrust remains the only one declared, the
  same as the last approved version. No manifest extensions.
- No new network endpoints.
- No new data collection. Still no telemetry and no account of its own.
- No runtime code loading. The Nexus integration remains compiled into
  this package, verified by the seal described under VERIFICATION.

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
(3866 tests) and was driven end to end through Windows UI Automation in
42 scripted checks. Every change to how files move was reviewed
separately against the app's rule that each change can be undone.

Thank you again.
```

---

## Why the letter is short

Nothing here reaches outside the app's existing lanes. Each change moves fewer of the user's files or
says more about what it did, so the letter states them in the order a tester might notice them and
stops.

## What is deliberately not in the letter

- The agent-access (MCP) work. Not in the package; the Store cut is a separate, later submission
  (spec `docs/superpowers/specs/2026-10-16-mcp-in-store-design.md`).
- Internal PR numbers. Same rule as every letter: answer honestly if asked.
