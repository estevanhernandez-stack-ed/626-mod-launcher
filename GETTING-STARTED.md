# Getting started — 626 Mod Launcher

A mod manager for your own installed games. It enables, disables, and organizes mods
reversibly — disabling *moves* a mod to a holding folder, it never deletes anything.

> **Easiest install: the [Microsoft Store](https://apps.microsoft.com/detail/9N53V6RRJK95)** — signed, no SmartScreen warning, auto-updates. That's the sealed-core build (full mod management, minus the Nexus integration + anti-cheat toggle). This guide covers the **portable** build; the full GitHub `Setup.exe` and the portable zip add the Nexus integration.

This is a portable build: no installer, nothing to set up. Unzip and run.

## Run it

1. **Unzip** `626-Mod-Launcher-portable-win-x64.zip` anywhere — your Desktop, a folder, a USB stick.
   Keep the files together; the `.exe` needs the folder around it.
2. **Run** `ModManager.App.exe`.
3. **First launch, Windows will warn you** — "Windows protected your PC." That's SmartScreen
   flagging an unsigned app, not a virus. Click **More info → Run anyway**. (Code-signing is
   on the roadmap; until then every unsigned app gets this.)

That's it. No .NET, no runtime, no Visual C++ — everything it needs ships in the folder.

## First time in

1. Click **+ Game**. Either pick a popular game from the quick-pick (it pre-fills the engine,
   mod folder, and Steam App ID) or enter them yourself, then point it at the game's install
   folder.
2. Your mods show up as a list — real names, descriptions, and author credit where we can find it.
3. **Toggle a mod off** and its files move to a holding folder. **Toggle it back on** and they
   return. Nothing is ever deleted.
4. Use **All / MP / SP** to swap loadouts, **Profiles** to save named sets, **Saves** to
   snapshot your save files before a risky run, and **Launch** to start the game.

## Where your stuff lives

- Registered games + paths: `%APPDATA%\ModManagerBuilder\`
- Per-game mod state (disabled mods, profiles, snapshots): in the game's library folder.
- Disabling never touches the internet and never deletes — it moves files you can always get back.

## If something looks off

| What you see | What's going on |
|---|---|
| "Windows protected your PC" | Unsigned build — **More info → Run anyway**. Expected. |
| App won't start at all | Make sure you unzipped the *whole* folder and run the `.exe` from inside it (don't run it from inside the zip). |
| No mods listed | The game or mod folder isn't set right — re-check the path in **+ Game**, or re-scan. |
| A mod "disappeared" after disabling | It moved, it didn't delete. Toggle it back on to restore it. |
| Steam launch does nothing | Re-enter the Steam App ID, or point **Browse** at the game folder directly. |

Found a bug or a game that doesn't fit? Tell Este — that's how this gets better.
