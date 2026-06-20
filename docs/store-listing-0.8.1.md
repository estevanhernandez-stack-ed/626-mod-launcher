# Microsoft Store listing — 626 Mod Launcher (Store SKU)

> Paste-ready copy for the Partner Center submission. Written for the **sealed Store SKU** — it does
> NOT mention Nexus browsing, mod downloading, or the anti-cheat toggle (all stripped from this flavor).
> Fill the `<…>` placeholders (support contact + the hosted privacy-policy URL). Voice: builder-to-builder,
> honest about what it does, polite about your files. Char limits noted per field.

---

## Properties

- **Category:** Utilities & tools  *(not Games — keeps it out of the gaming-specific policy rules)*
- **Subcategory:** (none / leave default)
- **Copyright and trademark info:** © 626Labs LLC
- **Support contact info:** `<your support email or https://github.com/estevanhernandez-stack-ed/626-mod-launcher/issues>`
- **Privacy policy URL:** the existing **626Labs hub website privacy policy** URL. The Store SKU collects no personal data, so the company-wide policy satisfies the requirement — no app-specific page needed. (The drafted policy at the bottom is optional reference only, in case you ever want launcher-specific language on the hub page.)

---

## Short description  *(max 270 chars)*

A native mod manager for the PC games you already own. Drop in a mod and it figures out where it goes; turn mods on and off without deleting anything; snapshot your saves before you experiment. Honest about what it does, careful with your files.

---

## Description  *(max 10,000 chars)*

626 Mod Launcher is a native Windows mod manager for the games on your own machine. It is built on one rule: your files are yours. Nothing it does is one-way.

You drop a mod onto the window — a .zip, .7z, .rar, or a loose folder — and it works out what the mod actually is and where it belongs for that game's loader, instead of dumping files and hoping. Turning a mod off moves it aside to a holding area; it never deletes it. Replacing a file snapshots the original first. Every change is written safely and can be undone.

**What it does**

- Reads your installed games (Steam libraries detected automatically) and manages each one's mods in one place.
- Installs mods by reading the archive first, then placing files where the game's loader expects them — engine-aware, not one-size-fits-all.
- Turns mods on and off reversibly — disabling moves files to a holding folder, never deletes.
- Manages load order for the loaders that care about it.
- Snapshots your save files before you experiment, and restores them when you want to go back.
- Edits mod config and INI files with a built-in editor that keeps a previous version you can restore.
- Saves loadouts as profiles so you can switch between mod sets.
- Keeps restore points of your whole setup, so a bad afternoon is one click away from undone.
- Warns you before enabling mods on games with anti-cheat, so an experiment doesn't cost you an account.
- Themes the whole app to taste.

**Who it's for**

People who mod the single-player games they paid for and want a tool that treats those files with respect — no silent deletes, no half-finished installs, no mystery about what changed. If you have been burned by a manager that left a game folder in pieces, this one is built to never do that.

**What it is not**

It is not a place to download or distribute mods, and it does not bundle anyone else's work. You bring the mods you already have; it manages them on your machine. It never ships third-party mod files or game content.

626 Mod Launcher is made by 626Labs.

---

## Product features  *(short bullets, max ~200 chars each, up to 20)*

- Engine-aware install — drop an archive, it places files where the loader expects them
- Reversible by default — disabling moves files aside, never deletes
- Load-order management for the loaders that need it
- Save snapshots — back up and restore your saves before you experiment
- Built-in config and INI editor with restore-previous
- Profiles — save and switch between mod loadouts
- Restore points for your whole setup
- Anti-cheat awareness — warns before you mod a game that could risk your account
- Automatic Steam game detection
- Full theming

---

## Search terms  *(max 7 terms, ≤30 chars each — not shown to users)*

- mod manager
- mods
- load order
- game mods
- save backup
- modding
- mod organizer

---

## What's new in this version  *(max 1500 chars)*

First release of 626 Mod Launcher on the Microsoft Store. A native, reversible mod manager for your own installed PC games: engine-aware install, on/off without deleting, load order, save snapshots, config editing, profiles, and whole-setup restore points. Built so nothing it does to your files is one-way.

---

## Notes for certification  *(to the Store testing team — not public)*

626 Mod Launcher is a load-order and file-management utility for PC games the user already has installed on their own machine. It does not download, distribute, or bundle any third-party mods or game content — the user supplies their own mod files, and the app organizes and toggles them in place.

There is no in-app mod browser, mod search, or mod download anywhere in the app — it is not connected to any mod storefront or marketplace and cannot fetch mods. The user obtains their mods themselves, entirely outside this app, and the app only manages files that are already on the user's disk.

It writes into the user's own game folders to enable/disable mods, and it does this reversibly (disabling moves files to a holding folder rather than deleting; replacing a file snapshots the original first). It requests the runFullTrust capability for this ordinary file management — it does not modify Windows system files or other applications.

To exercise it: launch the app, add a game (or let Steam detection find one), drag a mod archive or folder onto the window, and toggle it on/off. No account or sign-in is required.

---

## Privacy policy  *(OPTIONAL reference — the 626Labs hub website policy is what goes in Properties. Use this only if you want launcher-specific language added to the hub page.)*

### 626 Mod Launcher — Privacy Policy

**The short version:** 626 Mod Launcher does not collect, store, or transmit your personal information. Your settings and your mod setup stay on your device.

**What stays on your device.** All of your configuration — your registered games, mod state, profiles, themes, save snapshots, and restore points — is stored locally on your own computer. It is never uploaded anywhere.

**Network use.** The app may make read-only network requests to:
- fetch updated game-definition data (which games and engines it knows how to manage), and
- identify installed mods by sending file fingerprints (content hashes) to a lookup service so it can show mod details.

These requests send no personal information — no account, no name, no email, no identifiers. The app contains no analytics, no telemetry, no advertising, and no third-party tracking.

**App updates** are delivered by the Microsoft Store under your control.

**Contact.** Questions about this policy: `<your support email or GitHub issues URL>`

_Last updated: <date you publish>._
