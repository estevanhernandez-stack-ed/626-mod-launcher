# Notes for certification — reviewer letter (v0.11.2.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> **Read this before editing.** This is the first Store submission since the GitHub build gained a Nexus Mods sign-in + integration (v0.11.x on the GitHub channel). A reviewer who is aware of that — or who diffs against the approved v0.10.0.0 — should not be left to wonder whether an in-app mod storefront snuck into the Store package. It did not, and the letter **leads with that**: the entire Nexus integration (and the plugin host that would load it, and the anti-cheat toggle) is **compiled out of this package and binary-verified absent** by our seal check. Everything actually new in 0.11.2.0 is user-interface only. Do not shorten the compiled-out paragraph to save space; cut elsewhere if the field is tight.
>
> No new package capabilities, no new network endpoints, no new stored data versus the approved v0.10.0.0. The 10.1.6 / 11.12 / 11.13 storefront posture is unchanged (the mod-source integration is compiled out).

---

```text
Hello reviewer,

Thank you for your time on v0.11.2.0. 626 Mod Launcher is a load-order
and file-management utility for PC games the user already has installed
on their own machine. It does not download, distribute, or bundle any
third-party mods or game content — the user supplies their own mod
files, and the app organizes and toggles them in place.

NO IN-APP MOD STOREFRONT IN THIS PACKAGE (10.1.6 / 11.12 / 11.13)

There is no in-app mod browser, mod search, mod download, or mod
marketplace anywhere in this Store package. It is not connected to any
mod storefront and cannot fetch mods. The user obtains their mods
themselves, entirely outside this app, and the app only manages files
already on their disk.

Our separate, off-Store GitHub build offers an OPTIONAL Nexus Mods
account integration, delivered there as a downloaded, signed add-on.
That entire integration — and the plugin host that would load any such
add-on, and an anti-cheat toggle — is COMPILED OUT of this Store
package. We do not merely disable it: a seal check
(scripts/check-store-seal.ps1) reads every shipped DLL as raw bytes and
scans both its metadata and its string literals for those surfaces, and
fails the build if any of them leak into the Store binaries. So this
package physically cannot browse, search, fetch, download, or sign in to
any mod source. Its build output is verifiable and its source is public.

REVERSIBLE FILE MANAGEMENT (runFullTrust)

It writes into the user's own game folders to enable/disable mods, and
does so reversibly (disabling moves files to a holding folder rather
than deleting; replacing a file snapshots the original first). It
requests the runFullTrust capability for this ordinary file management —
it does not modify Windows system files or other applications.

NEW IN THIS VERSION — USER INTERFACE ONLY

All three changes are presentation-only, with no new capabilities, no
new permissions, and no new network calls versus the approved
v0.10.0.0:

  1. The whole game row on the library home is now clickable to open a
     game, rather than only a button.
  2. A dropdown in the title bar switches between the user's own added
     games without returning to the library.
  3. The previous "Rescan" and "Refresh stats" controls were merged into
     one "Refresh" button that re-scans the user's own game folder for
     mod changes. (The Nexus-stats half of that refresh exists only on
     the GitHub build; it is compiled out here, so in this package
     Refresh only re-scans local files.)

NETWORK, PRIVACY, IDENTITY

The only outbound call this Store package makes is a read-only metadata
lookup to our CurseForge metadata proxy, to identify a mod file the user
has already placed on disk (name/author) — it fetches no mods and sends
no personal data. No telemetry. No account or sign-in is required.
Privacy policy: https://626labs.dev/privacy.html

Game names and mod content belong to their respective publishers and
authors; 626 Mod Launcher is an independent utility, not affiliated with
or endorsed by any game publisher or mod host. Source is public at
https://github.com/estevanhernandez-stack-ed/626-mod-launcher (the seal
check described above lives in scripts/check-store-seal.ps1 and is
greppable).

To exercise it: launch the app — it opens on the library; add a game (or
let Steam detection find one), click a game's row to open it, use the
title-bar dropdown to switch games, and click Refresh. Drag a mod
archive or folder onto the window and toggle it on/off.

If anything is unclear, please reach out and we will respond same-day.

Estevan Hernandez
626 Labs LLC
```

---

## Defenses by clause (cheat sheet for v0.11.2.0)

| Clause | Defense in this letter | If rejected, what to add |
|---|---|---|
| **10.1.6 / 11.12 / 11.13** in-app storefront / UGC browsing — *the one to watch* | The lead paragraph states there is no mod browser/search/download/marketplace in this package; the Nexus mod-source integration is compiled out and binary-verified absent | MSIX walkthrough: unzip the `.msixbundle` → the app `.dll` contains no plugin-loader/Nexus symbols; there is no marketplace UI on a packaged install. Show the seal script output and point to `scripts/check-store-seal.ps1`. |
| **10.2.x** dynamic / downloaded code | The plugin host that loads downloaded add-ons (GitHub build only) is compiled out; the seal check proves the loader symbols are absent from the shipped DLLs | Same seal + MSIX inspection; note the host is `#if FULL` and the Store build leaves `FULL` undefined. |
| **Prior-statement consistency** (reviewer diffs vs approved v0.10.0.0) | We state up front that everything new is UI-only — no new capabilities, permissions, or network calls | Enumerate the three UI changes; confirm the manifest capability set and network endpoints are identical to v0.10.0.0. |
| **10.5** privacy | No PII collected; the sole outbound call is a read-only metadata proxy; privacy policy linked | Privacy policy URL; note the app requires no account and stores nothing about the user off-machine. |
| **10.1.1** accurate representation | Product name/description describe a file manager for the user's own games; no other product's name is used as the title | — |

## Pre-submission sanity check (v0.11.2.0)

- [ ] `Package.appxmanifest` Identity `Version` = `0.11.2.0` (4th component zero) — **confirmed**
- [ ] Store MSIX built with a matching `-p:Version=0.11.2` (the bundle version derives from the manifest identity)
- [ ] `PublisherDisplayName` = `626Labs LLC` (no space inside `626Labs`)
- [ ] `TargetDeviceFamily MinVersion` `10.0.17763.0` unchanged
- [ ] `pwsh scripts/check-store-seal.ps1` → **STORE seal OK** (plugin loader + anti-cheat absent) — the evidence behind the compiled-out paragraph. **Actually run it; do not assume.** (Confirmed for this build.)
- [ ] Inspect the `.msixbundle`: no plugin DLL inside, no Nexus/marketplace UI on a packaged install
- [ ] This letter's block pasted into **Notes for certification**
- [ ] Public **What's new in this version** filled from [`../store-listing-0.11.2.md`](../store-listing-0.11.2.md) (no Nexus/marketplace mention)

## Source

Follows the RoRoRo reviewer-letter format (`ROROROblox/docs/store/reviewer-letter-*.md`). Predecessor cert notes for this app: [`../store-listing-0.10.0.md`](../store-listing-0.10.0.md) · [`../store-listing-0.8.1.md`](../store-listing-0.8.1.md).
