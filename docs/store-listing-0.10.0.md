# Microsoft Store submission — v0.10.0

> Update submission for the v0.10.0 cut (Library home + loose-root mods + cover art). Listing fields (description, category **Utilities & tools / File managers**, screenshots, privacy URL) unchanged from 0.8.1/0.9.x — see [`store-listing-0.8.1.md`](store-listing-0.8.1.md). Only the two fields below are needed.
>
> **Consider new screenshots** — the Library home is the app's new face; the current screenshots show the old game-first view. Not cert-blocking, but the listing sells better with the home visible.

**Package to upload:** `src/ModManager.App/AppPackages/ModManager.App_0.10.0.0_Store_Test/ModManager.App_0.10.0.0_x64_Store.msixbundle` (unsigned — the Store re-signs). Partner Center → 626 Mod Launcher → Packages → upload. Store seal verified on this exact build.

---

## What's new in this version  *(max 1500 chars)*

626 now opens on your game library — real cover art, every game's mod state at a glance (how many mods, how many are on, engine, ban-risk, detected loaders), a Jump back in row ordered by when you actually played, and one-click Play and Manage. Games you have installed but haven't added yet show up at the bottom, one click to add.

Games modded by loose files in the game root — Death Stranding 2 and friends: ASI plugins, ReShade addons, proxy loaders — are now detected, categorized, and reversibly toggleable, with files staying exactly where the game needs them. Game files and standalone configs are never touched.

Cover art is sharper too: portrait art instead of blurry icons, fetched from Steam's public CDN when it isn't cached locally — brand-new games get real covers on day one.

---

## Notes for certification  *(to the Store testing team — not public)*

626 Mod Launcher is a load-order and file-management utility for PC games the user already has installed on their own machine. It does not download, distribute, or bundle any third-party mods or game content — the user supplies their own mod files, and the app organizes and toggles them in place.

There is no in-app mod browser, mod search, or mod download anywhere in the app — it is not connected to any mod storefront or marketplace and cannot fetch mods. The user obtains their mods themselves, entirely outside this app, and the app only manages files that are already on the user's disk.

It writes into the user's own game folders to enable/disable mods, and it does this reversibly (disabling moves files to a holding folder rather than deleting; replacing a file snapshots the original first). It requests the runFullTrust capability for this ordinary file management — it does not modify Windows system files or other applications.

New in this version: (1) the app now opens on a library view of the user's own registered games — a presentation change; it reads the same local data the app already managed. (2) For games that are modded by loose files placed in the game's own folder, the app can now recognize those files as mods and toggle them the same reversible way (moved to a holding folder when disabled, restored when enabled) — it only ever touches files it can positively identify as user-installed mod files; game files and configuration files are never listed or moved. (3) The app may fetch game cover images from Steam's public image CDN (read-only, no authentication, no personal data sent) and cache them locally so the library shows box art.

To exercise it: launch the app — it opens on the library; add a game (or let Steam detection find one), click Manage, drag a mod archive or folder onto the window, and toggle it on/off. No account or sign-in is required.
