# Notes for certification — reviewer letter — v0.28.1.0 (ACE COMBAT 8 offline launch, launches without anti-cheat stated)

> **Verified against the real package, not against master.** Identity, version and capability below were read
> out of the built bundle (the inner `AppxManifest.xml`) on 2026-11-11. The seal script reports the plugin
> loader and the EAC-disable mechanism absent and Nexus compiled in. A byte search of the sealed
> `ModManager.Core.dll` finds the AC8 offline launch (title, exe path, args, Steam ids): the seal keeps it.
>
> ```text
> File      : src/ModManager.App/AppPackages/ModManager.App_0.28.1.0_Store_Test/
>             ModManager.App_0.28.1.0_x64_Store.msixbundle
> Size      : 83.8 MB
> SHA-256   : 6E3F57E76F6B826B20CF87E8DDC52E1ED40E22418544D97CF27C7BD95E03D5F9
> Identity  : 626LabsLLC.626ModLauncher
> Publisher : CN=177BCE59-0966-4975-9962-10E36652141F
> Version   : 0.28.1.0            (application x64)
> Capability: runFullTrust  — the only one declared; no manifest extensions
> Target    : Windows.Desktop, min 10.0.17763.0
> Seal      : OK — plugin loader + EAC-disable absent; Nexus compiled in
> Tests     : 4197 passing (Core, master f1d18f9)
> Submodule : external/626-mod-plugins pinned at nexus-v0.14.0 (87a1bdc)
> Exes      : ModManager.App.exe, RestartAgent.exe (Windows App SDK). No MCP server.
> Endpoints : none added since 0.27.0.0 (host list diffed across the source, v0.27.0..f1d18f9)
> ```
>
> **The honest headline.** One new launch that starts a game without its anti-cheat, and a correction.
> Every earlier letter said the anti-cheat toggle is sealed out of the Store build, which is true. The
> 0.27.0.0 letter went further and said the app "never disables, bypasses or alters anti-cheat software".
> That overstated it: on Elden Ring the Store build has always run the Seamless Co-op and Mod Engine 2
> launchers, which start the game without its anti-cheat. This letter names all three launches in one
> place so a tester finds them here, not on their own.

---

```text
Hello reviewer,

Thank you for your time on version 0.28.1.0.

WHAT CHANGED

1. ACE COMBAT 8 can be played with mods, offline.
   This game's anti-cheat refuses to start when a mod loader sits
   beside the game, so modded play could not start at all. The app's
   "Play modded" for this game now runs the game's own executable
   directly, with the same arguments as the publisher's own internal
   launch configuration, offline and without the anti-cheat. Nothing
   in the game folder is replaced, patched or removed to do this. The
   app tells the user in plain words to stay out of online modes while
   modded, and the game is marked as carrying a ban risk, so enabling
   its first mod asks the user to acknowledge that.
   "Play vanilla" sets the mod loader aside and starts the game through
   Steam with its anti-cheat running normally, for online play.

2. A mod framework (UE4SS) installs beside any Unreal Engine game's
   executable, not only executables with one particular name.

3. Play on the app's home screen now launches the same way Play in the
   game's own view does, including starting without mods when the user
   has chosen that. When a launch needs a question answered first, the
   game's view opens and asks.

4. While the user is playing without mods, the app no longer reports
   the mod loaders it set aside as missing, and its launch menu offers
   only the unmodded ways to start the game.

5. When a launch needs Steam and Steam is closed, the app starts Steam
   and now waits until an account has signed in before starting the
   game. Before, it started the game a few seconds too early and the
   game reported that the user did not own it.

LAUNCHES THAT START A GAME WITHOUT ITS ANTI-CHEAT

We want to state these plainly, together. Three launches in this
package start a game without its anti-cheat:

- Elden Ring, through the Seamless Co-op launcher, when the user has
  installed Seamless Co-op.
- Elden Ring, through the Mod Engine 2 launcher, when the user has
  installed Mod Engine 2.
- ACE COMBAT 8, through the offline launch described above.

The first two are third-party launchers the user installs. The app
detects them and runs them; it does not bundle them. These two were
present in earlier approved versions, and an earlier letter of ours
described the app as never bypassing anti-cheat, which did not account
for them. We are correcting that here.

In every case:
- The app never modifies, disables, removes or replaces anti-cheat
  software or any of its files. The mechanism that turns anti-cheat
  off by swapping files is not in this package, verified by the seal
  described under VERIFICATION.
- "Play vanilla" never uses any of these launches. It always starts
  the game through the store client with its anti-cheat on, and the
  app launches nothing if it cannot find that path.

WHAT THIS DOES NOT ADD

- No new capabilities. runFullTrust remains the only one declared, the
  same as the last approved version. No manifest extensions.
- No new network endpoints.
- No new data collection. Still no telemetry and no account of its own.
- No runtime code loading. The Nexus integration remains compiled into
  this package.

WHY runFullTrust IS STILL REQUIRED

Unchanged from previous submissions. The app manages mod files inside
game installation folders chosen by the user, outside any sandboxed
location, launches games through the user's store clients or the
game's own executable, and reads store install records from the local
machine.

VERIFICATION

The submitted package was checked with a build-time seal script that
reads the compiled binaries and asserts both that no runtime
code-loading mechanism is present and that the Nexus integration is
compiled in rather than downloaded. Both are verified for this build.

The behaviour described above is covered by an automated test suite
(4197 tests). Each change was also driven end to end on the real games
through Windows UI Automation: ACE COMBAT 8 modded and unmodded, and
Elden Ring through Seamless Co-op and unmodded, and ACE COMBAT 8 from
a closed Steam client. In unmodded play the
anti-cheat process was confirmed running each time.

Thank you again.
```

---

## Why the letter has a section of its own for anti-cheat

A tester who launches Elden Ring through Seamless, or ACE COMBAT 8 modded, will see a game start with no
anti-cheat. If the letter says nothing, that reads as something hidden. Naming all three, saying which are
third-party, and saying what vanilla always does is the answer to the question they will have first.

## The correction, and why it is in the letter

0.27.0.0's "never disables, bypasses or alters anti-cheat software" was written about the app's own
mechanism and missed what the Seamless and Mod Engine 2 launchers do. Leaving it uncorrected while adding
an AC8 line would make the old sentence look deliberate. The new wording ("never modifies, disables,
removes or replaces anti-cheat software or its files") is the claim that is actually true of every path.

## What is deliberately not in the letter

- The agent-access (MCP) work. Not in the package.
- Internal PR numbers. Answer honestly if asked.
- The game-feed ban-risk rating for ACE COMBAT 8. It arrives through the signed definitions feed, not the
  package; the letter only says the game is marked as carrying a ban risk.
- The packaged-build smoke. The live claim rests on the debug-build runs of 2026-11-09 and 2026-11-10; the
  packaged-build pass (below) must happen before this letter is pasted.
