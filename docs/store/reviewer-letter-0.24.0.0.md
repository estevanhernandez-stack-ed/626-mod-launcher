# Notes for certification — reviewer letter — v0.24.0.0 (the game's own files stay put)

> **Verified against the real package, not against master.** Identity, version and capability below
> were read out of the built bundle (`AppxBundleManifest.xml` and the inner `AppxManifest.xml`), not
> off the manifest on disk. The seal script reports the plugin loader and the EAC-disable mechanism
> absent and Nexus compiled in.
>
> ```text
> File      : src/ModManager.App/AppPackages/ModManager.App_0.24.0.0_Store_Test/
>             ModManager.App_0.24.0.0_x64_Store.msixbundle
> Size      : 83.7 MB
> SHA-256   : FDD0EF2F017145D5A250A5303E07AE79D0EBBA7B5FEE1F849CED81C96EEA2B5D
> Identity  : 626LabsLLC.626ModLauncher
> Publisher : CN=177BCE59-0966-4975-9962-10E36652141F
> Version   : 0.24.0.0            (application x64)
> Capability: runFullTrust  — the only one declared; no manifest extensions
> Target    : Windows.Desktop, min 10.0.17763.0
> Seal      : OK — plugin loader + EAC-disable absent; Nexus compiled in
> Tests     : 3797 passing (Core), 42 of 42 UI Automation smoke checks
> Submodule : external/626-mod-plugins pinned at nexus-v0.14.0 (87a1bdc)
> Exes      : ModManager.App.exe, RestartAgent.exe (Windows App SDK). No MCP server.
> ```
>
> **The honest headline.** Nothing here reaches further than 0.23.0.0 did. No new capability, no new
> endpoint, no new data. Most of the release makes the app touch less: it stops turning off the game's
> own files and stops renaming files it should not. Two changes a tester can see without looking for
> them come first: the app can now stay in the notification area after its window closes (off by
> default), and uninstall deletes files after a confirm that names them.

---

```text
Hello reviewer,

Thank you for your time on version 0.24.0.0.

TWO THINGS A TESTER MAY SEE

1. Close to tray (Settings, off by default). With the setting on,
   closing the window hides it and leaves an icon in the notification
   area with two items: Open and Quit. Quit exits the app. With the
   setting off, which is the default, closing the window exits as
   before. The app does not start with Windows and registers no
   startup task.

2. Uninstalling a mod deletes its files. Before it does, a confirm
   names every file and folder it will remove, including copies the app
   is holding for that mod, and Cancel is the default button. Turning a
   mod off still only moves files aside; only uninstall deletes.

WHAT THE APP NOW TOUCHES LESS

- The game's own files are never turned off. On Bethesda games
  (Skyrim, Fallout, Starfield, Oblivion) and on some Unreal Engine
  games, turning off every mod could previously move one of the game's
  own files into the holding folder. The app now recognises the game's
  files and leaves them in place on every path: a single switch, turn
  off all, profiles and the reset to an unmodded game.

- Load order never renames a Bethesda game's plugins. Renaming them
  broke the mods that depend on them. The app now refuses and explains
  that this load order lives in a file it does not edit yet. Where it
  renamed files for load order on other games, Undo restores the
  original names. Undo only touches names in the exact numbered form
  the app writes, never overwrites an existing file, and only renames.

- "Return to vanilla" now turns off and holds the mods the app can
  prove are mods, and Restore puts them back. Anything it cannot tell
  apart from the game's own files is left where it is and named in the
  summary.

- A mod that keeps files in several folders (Cyberpunk 2077 does this)
  now turns off and on in all of them together, by moving files, as
  every toggle does.

SAVES

- Restoring a save snapshot into a different folder than the one it
  was taken from is refused before anything changes.
- Saves for the two EA app games added in 0.23.0.0 are listed and
  backed up, and the app never writes to them, because the EA app syncs
  those folders to its cloud.

WHAT THIS DOES NOT ADD

- No new capabilities. runFullTrust remains the only one declared, the
  same as the last approved version.
- No new network endpoints. Identifying a mod the user installed by
  hand can now fall back to a name search on Nexus Mods when the user
  is signed in; it is the same Nexus search the app already used, capped
  at 50 lookups when adding a game and 200 per run of "Identify my
  mods". The app's existing signed game-definition list can now carry
  links to loader download pages, as data; links must be https, the
  app only opens them in the browser, and the list's address is
  unchanged.
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
(3797 tests). The app was also driven end to end through Windows
UI Automation in 42 scripted checks, including turning off every mod on
a sample Bethesda game folder with the game's own files left in place
and the folder byte-identical afterwards.

Thank you again.
```

---

## Why this letter leads with the tray and uninstall

They are the two behaviours a tester can meet without looking for them: a window that closes but leaves
an icon, and a confirm that deletes. Both are explained before they are found. Everything after that is
the app doing less to a user's files than the last approved version, which is why the letter can be
short about it.

## What is deliberately not in the letter

- The agent-access (MCP) server work in this release. That server is not part of the Store package.
  Shipping it in the Store package is a separate, later decision (dashboard task "Ship the agent tool
  (MCP) in the Store package"), and it will need its own letter section.
- The Windrose save-mod fixes and the outdated-loader warning. Both are on the listing's What's new;
  neither reaches outside the app's existing lanes.
- Internal PR numbers, specs and plans. Same rule as every letter: answer honestly if asked.
