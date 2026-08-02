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

The full formal reviewer letter — paste-ready, with a defenses-by-clause cheat sheet and a
pre-submission sanity check — is in **[`store/reviewer-letter-0.11.2.0.md`](store/reviewer-letter-0.11.2.0.md)**.
Paste the fenced block from that file into Partner Center → **Notes for certification**.

(Single source of truth: the reviewer letter, following the RoRoRo `docs/store/reviewer-letter-*.md`
format. It leads with the compiled-out-Nexus/marketplace posture — the 10.1.6/11.12/11.13 defense —
and states the UI-only delta versus the certified 0.10.0.0.)
