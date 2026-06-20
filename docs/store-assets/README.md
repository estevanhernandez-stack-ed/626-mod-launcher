# 626 Mod Launcher — brand & store assets

One-stop kit for featuring 626 Mod Launcher (e.g. on the 626labs.dev hub). Everything a listing
needs: links, identity, images, copy, brand tokens. All facts current as of the v0.8.1 Store launch.

## Links

- **Microsoft Store:** https://apps.microsoft.com/detail/9N53V6RRJK95
- **GitHub repo / releases:** https://github.com/estevanhernandez-stack-ed/626-mod-launcher
- **GitHub latest release (full build):** https://github.com/estevanhernandez-stack-ed/626-mod-launcher/releases/latest

## Product identity

| Field | Value |
|---|---|
| Name | 626 Mod Launcher |
| Publisher | 626Labs LLC |
| Store ID | `9N53V6RRJK95` |
| Package Family Name | `626LabsLLC.626ModLauncher_wz1chhb2h2v4a` |
| Price | Free |
| Category | Utilities & tools / File managers |

## Two channels (describe accurately)

- **Microsoft Store** — signed (no SmartScreen), Store-managed updates. The **sealed-core** build: full mod management, but **no Nexus integration and no anti-cheat toggle**.
- **GitHub / Velopack** — the **full** build: everything including the Nexus integration (a signed off-Store plugin). Unsigned installer (SmartScreen warning), auto-updates from GitHub.

## Images (in this folder)

| File | Use | Dimensions |
|---|---|---|
| `store-hero-3840x2160.png` | Hero / banner (16:9) | 3840×2160 |
| `store-poster-9x16-720x1080.png` | Vertical poster (9:16) | 720×1080 |
| `store-boxart-1x1-1080x1080.png` | Square logo / box art | 1080×1080 |
| `store-apptile-300x300.png` | App tile icon | 300×300 |
| `store-display-150x150.png` | Small tile | 150×150 |
| `store-display-71x71.png` | Smallest tile | 71×71 |
| `app-icon-512.png` | App icon (favicon / inline mark) | 512×512 |

All are brand-faithful renders of the app's toggle mark (cyan on / magenta on / navy off) on the 626 navy field. Source templates were CSS compositions rendered via Playwright; regenerate at new sizes if needed.

## Copy

- **Tagline:** Mod the games you own — reversibly. Nothing it does to your files is one-way.
- **Eyebrow:** Windows · Mod Manager
- **Short description:** A native mod manager for the PC games you already own. Drop in a mod and it figures out where it goes; turn mods on and off without deleting anything; snapshot your saves before you experiment. Honest about what it does, careful with your files.
- **Feature bullets:**
  - Engine-aware install — drop an archive, it places files where the loader expects them
  - Reversible by default — disabling moves files aside, never deletes
  - Load-order management, save snapshots, config/INI editor with restore
  - Profiles, restore points, full theming
  - Anti-cheat awareness — warns before you mod a game that could risk your account
- **Full listing copy** (description, search terms, what's-new, cert notes): [`../store-listing-0.8.1.md`](../store-listing-0.8.1.md)

## Brand tokens (626 design system)

- **Navy base:** `#091023` → `#16243d` (gradient)
- **Cyan:** `#17d4fa` · **Magenta:** `#f22f89` (signature duo — always paired)
- **Type:** Space Grotesk (display), Inter (UI), JetBrains Mono (mono/eyebrow labels, uppercase +0.12em tracking)
- **626Labs tagline:** *Imagine Something Else.*
