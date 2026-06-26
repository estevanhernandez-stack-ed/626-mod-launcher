# Microsoft Store submission — v0.9.0

> Update submission for the v0.9.0 cut (ban-safe loaders + loaders-in-the-bar, Mod Engine 2 engine-wide, request-a-game). The listing fields (description, category **Utilities & tools / File managers**, screenshots, privacy URL) are **unchanged from 0.8.1** — see [`store-listing-0.8.1.md`](store-listing-0.8.1.md). For this update you only need the two fields below.

**Package to upload:** `src/ModManager.App/AppPackages/ModManager.App_0.9.0.0_Store_Test/ModManager.App_0.9.0.0_x64_Store.msixbundle` (unsigned — the Store re-signs). Partner Center → 626 Mod Launcher → Packages → upload.

---

## What's new in this version  *(max 1500 chars)*

The anti-cheat warning now points you at the safe way in. On games like Elden Ring it lists the anti-cheat-safe loaders — Mod Engine 2, Seamless Co-op — with Launch if you have them or a link to get them. The warning and your acknowledgment are unchanged; nothing enables itself.

Detected loaders also show up as one-click Launch buttons in the bar — 626 manages your files and saves, the loader does the loading.

And when 626 can't read a game's engine, a new Request this game link opens a prefilled report so we can add it.

---

## Notes for certification  *(to the Store testing team — not public)*

626 Mod Launcher is a load-order and file-management utility for PC games the user already has installed on their own machine. It does not download, distribute, or bundle any third-party mods or game content — the user supplies their own mod files, and the app organizes and toggles them in place.

There is no in-app mod browser, mod search, or mod download anywhere in the app — it is not connected to any mod storefront or marketplace and cannot fetch mods. The user obtains their mods themselves, entirely outside this app, and the app only manages files that are already on the user's disk.

It writes into the user's own game folders to enable/disable mods, and it does this reversibly (disabling moves files to a holding folder rather than deleting; replacing a file snapshots the original first). It requests the runFullTrust capability for this ordinary file management — it does not modify Windows system files or other applications.

New in this version: (1) when a third-party mod loader is already installed in the user's game folder, the app shows a button that launches that already-installed program — it does not download or install the loader. (2) Where a loader or game isn't present, the app shows a link that opens the user's external web browser (to the loader's official page, or to a pre-filled GitHub issue form to request a game). All obtaining of loaders or mods happens in the external browser, outside the app — nothing is fetched or installed inside the app.

To exercise it: launch the app, add a game (or let Steam detection find one), drag a mod archive or folder onto the window, and toggle it on/off. No account or sign-in is required.
