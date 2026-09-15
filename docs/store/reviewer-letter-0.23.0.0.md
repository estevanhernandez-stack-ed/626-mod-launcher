# Notes for certification — reviewer letter — v0.23.0.0 (games from the EA app)

> **Verified against the real package, not against master.** Identity, version and capability below
> were read out of the built bundle (`AppxBundleManifest.xml` and the inner `AppxManifest.xml`), not
> off the manifest on disk. The seal script reports the plugin loader and the EAC-disable mechanism
> absent and Nexus compiled in.
>
> ```text
> File      : src/ModManager.App/AppPackages/ModManager.App_0.23.0.0_Store_Test/
>             ModManager.App_0.23.0.0_x64_Store.msixbundle
> Size      : 83.6 MB
> SHA-256   : 50A14E6493B63A38999E1A78773719DDB144B6B5045EFB395CA0BAD0A7B05A94
> Identity  : 626LabsLLC.626ModLauncher
> Publisher : CN=177BCE59-0966-4975-9962-10E36652141F
> Version   : 0.23.0.0            (application x64)
> Capability: runFullTrust  — the only one declared
> Target    : Windows.Desktop, min 10.0.17763.0
> Seal      : OK — plugin loader + EAC-disable absent; Nexus compiled in
> Tests     : 2614 passing
> Submodule : external/626-mod-plugins pinned at nexus-v0.14.0 (87a1bdc)
> ```
>
> **The honest headline.** The app now reads a second store's install records and hands a launch to
> a second store's client. Both are read-and-hand-off, nothing more, and the letter says exactly what
> is read and what is launched. The other changes make the app ask more often, not less.

---

```text
Hello reviewer,

Thank you for your time on version 0.23.0.0.

WHAT IS NEW THAT A REVIEWER SHOULD KNOW ABOUT

1. The app now recognises games installed through the EA app.

   To find them it reads, and only reads:
   - the registry keys under HKEY_LOCAL_MACHINE\SOFTWARE\EA Sports,
     \EA Games and \Electronic Arts, in the 64-bit and 32-bit views,
     for each title's "Install Dir" and "DisplayName" values;
   - one file per install: <Install Dir>\__Installer\installerdata.xml,
     the EA app's own record of the install. It is parsed with DTD
     processing prohibited and no external resolution.

   It writes nothing to the registry, starts nothing, and does not
   contact the EA app or any EA service to do this. A game is only
   offered to the user when its EA content id matches a game our own
   signed game-definition list already describes, so the app never
   guesses at an unknown EA title.

2. Play for those games opens the EA app's own launch link:
   origin2://game/launch/?offerIds=<content id>
   That URL is handed to Windows, which passes it to the EA app. The
   EA app does the launching, including its own updates, cloud saves
   and anti-cheat. This app never starts the game's executable itself.
   The content id in the link is the one matched in point 1, not free
   text from the file.

3. The app does not manage mods for these games, and says so. They run
   EA's kernel-level anti-cheat, and the tools that mod them work by
   replacing EA's anti-cheat launcher, which this app will not do. The
   mod list, drag-and-drop, the mod switches and the discovery tools
   all refuse with one plain sentence. Inside the game's install folder
   the app writes nothing and opens no file except the one named in
   point 1; it lists file and folder names there only to tell whether
   the game is running and whether a mod loader file is present, the
   same checks it makes for every game.
   The app keeps its own data for these games under the user's local
   application data folder rather than beside the game, because the
   game lives under Program Files.

THE APP NOW ASKS BEFORE IT CHANGES A SAVE ON AN ANTI-CHEAT GAME

Previously the app warned before enabling mods on a game with a known
anti-cheat risk, but its save editor could change a save on such a game
without a word. It now asks first, every time, before editing a
character, installing a save mod, or bringing a save in from a backup,
until the user ticks "don't ask again" for that game. Restoring a
snapshot, removing a save mod and resetting one never ask: getting back
to what you had is never made harder. The warning also now applies to
games that did not come from Steam, which previously went unwarned.

TURNING A MOD ON OR OFF NEVER DELETES A HELD COPY

Turning a mod off moves its files to a holding folder. If the user then
installed a fresh copy of the same mod, turning either copy on or off
could remove the older one in a cleanup step. That path now refuses
before moving anything, keeps both copies where they are, and names the
files in the way. This is a fix in the direction of never deleting the
user's files.

WHAT THIS DOES NOT ADD

- No new capabilities. runFullTrust remains the only one declared, the
  same as the last approved version.
- No new network endpoints. Finding EA app games is local; launching
  one hands a URL to Windows. (The app's existing signed game-definition
  list gained the two EA ids as data; its address is unchanged.)
- No new data collection. Still no telemetry and no account of its own.
- No runtime code loading. The Nexus integration remains compiled into
  this package, verified by the seal described under VERIFICATION.

WHY runFullTrust IS STILL REQUIRED

Unchanged from previous submissions. The app manages mod files inside
game installation folders chosen by the user, outside any sandboxed
location, launches games through the user's store clients, and now
reads the EA app's install records from the local machine registry and
the install folders it names.

VERIFICATION

The submitted package was checked with a build-time seal script that
reads the compiled binaries and asserts both that no runtime
code-loading mechanism is present and that the Nexus integration is
compiled in rather than downloaded. Both are verified for this build.

The behaviour described above is covered by an automated test suite
(2614 tests). Detection, adding and Play for the two EA games were also
exercised on the maintainer's machine: both were found and added, Play
started each game through the EA app, and nothing was written under the
EA install folders.

Thank you again.
```

---

## Why this letter leads with the EA app

It is the one change that reaches outside this app's own lanes: a second store's registry keys, a file
inside another publisher's install folder, and a launch handed to another company's client. A reviewer
who sees `HKEY_LOCAL_MACHINE` access or an `origin2://` launch in testing should already know why it is
there and how narrow it is.

The two other sections both move toward caution: a new question before a save changes, and a refusal
where a cleanup used to delete. They follow because they are the kind of change a reviewer is glad to
hear about and never needs to go looking for.

## What is deliberately not in the letter

- EA's user agreement and the anti-cheat's enforcement posture toward offline modding. The listing and
  the app tell the user the app does not mod these games; the letter does not speculate about EA's
  policy on the user's behalf.
- The agent-access (MCP) server fix in this release. That server is not part of the Store package.
- Internal PR numbers, the spec, and the plan. Same rule as every letter: answer honestly if asked.
