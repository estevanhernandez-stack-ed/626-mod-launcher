# Microsoft Store submission — 0.11.2.0

> Update submission for the v0.11.2 line. Listing fields (description, category **Utilities & tools**, screenshots, privacy URL) unchanged from prior submissions — see [`store-listing-0.10.0.md`](store-listing-0.10.0.md) / [`store-listing-0.8.1.md`](store-listing-0.8.1.md). Only the two fields below are needed.
>
> The Store SKU is the sealed core (no Nexus, no anti-cheat toggle). Everything new in this version is **UI-only** and flavor-neutral — the OAuth work in v0.11.0/v0.11.1 was all Nexus (GitHub-only) and does **not** touch the Store binaries.

**Package to upload:** `src/ModManager.App/AppPackages/ModManager.App_0.11.2.0_Store_Test/ModManager.App_0.11.2.0_x64_Store.msixbundle` (unsigned — the Store re-signs). Partner Center → 626 Mod Launcher → Packages → upload. Store seal verified on this exact build.

---

## What's new in this version  *(max 1500 chars)*

The library is quicker to move around in. Click a game anywhere on its row to open it — no hunting for a button. Once you're in a game, a dropdown in the title bar switches you straight to another game without going back to the library first. And the two refresh buttons are now one: "Refresh" re-scans your mod folder for changes in a single click.

Same reversible, atomic, no-telemetry mod management underneath — this pass is all about getting around faster.

---

## Notes for certification  *(to the Store testing team — not public)*

626 Mod Launcher is a load-order and file-management utility for PC games the user already has installed on their own machine. It does not download, distribute, or bundle any third-party mods or game content — the user supplies their own mod files, and the app organizes and toggles them in place.

There is no in-app mod browser, mod search, or mod download anywhere in the app — it is not connected to any mod storefront or marketplace and cannot fetch mods. The user obtains their mods themselves, entirely outside this app, and the app only manages files that are already on the user's disk.

It writes into the user's own game folders to enable/disable mods, and it does this reversibly (disabling moves files to a holding folder rather than deleting; replacing a file snapshots the original first). It requests the runFullTrust capability for this ordinary file management — it does not modify Windows system files or other applications.

New in this version — all presentation/UI, with **no new capabilities, no new permissions, and no new network calls** versus the previously certified 0.10.0.0: (1) the whole game row on the library home is now clickable to open a game, rather than only a button; (2) a dropdown in the title bar switches between the user's own added games without returning to the library; (3) the previous "Rescan" and "Refresh stats" controls were merged into a single "Refresh" button that re-scans the user's own game folder for mod changes.

To exercise it: launch the app — it opens on the library; add a game (or let Steam detection find one), click a game's row to open it, use the title-bar dropdown to switch games, and click Refresh. Drag a mod archive or folder onto the window and toggle it on/off. No account or sign-in is required.
