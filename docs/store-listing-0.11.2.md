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

626 Mod Launcher is a load-order and file-management utility for PC games the user already has installed. It does not download, distribute, or bundle any third-party mods or game content — the user supplies their own mod files, and the app organizes and toggles them in place, reversibly (disabling moves files to a holding folder rather than deleting; replacing snapshots the original first).

**This version is UI-only relative to the previously certified build (0.10.0.0):** the changes are (1) the whole game row on the home screen is clickable to open a game (previously only a button was); (2) a dropdown in the title bar switches between the user's own added games; (3) the "Rescan" and "Refresh stats" buttons were merged into one "Refresh" button. There are **no new capabilities, no new permissions, and no new network calls** in this Store build. It requests the same `runFullTrust` capability for ordinary file management and makes no outbound calls except the existing CurseForge metadata proxy (read-only, no personal data). It does not modify Windows system files or other applications. No account or sign-in is required.

To exercise it: launch the app (opens on the library), click a game row to open it, use the title-bar dropdown to switch games, click Refresh. Drag a mod archive or folder onto the window and toggle it on/off.
