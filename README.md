# 626 Mod Launcher

> A native Windows mod manager that's honest about what it does, polite about your files, and decent to the modders.

[![release](https://img.shields.io/github/v/release/estevanhernandez-stack-ed/626-mod-launcher?include_prereleases&label=latest)](https://github.com/estevanhernandez-stack-ed/626-mod-launcher/releases/latest)
[![license](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

A reversible, atomic mod toggler for PC games — Bethesda, Unreal pak, FromSoft, BepInEx, UE4SS Lua, Mod Engine 2, Stardew SMAPI, and friends. Built because Vortex made me angry. Free, no ads, no telemetry, no account required to use it.

## Download

Two ways to get it:

**[Get it from the Microsoft Store](https://apps.microsoft.com/detail/9N53V6RRJK95)** — the easy path. Signed by Microsoft, so no SmartScreen warning; installs and auto-updates through the Store. This is the **sealed-core build**: full mod management — intake, reversible toggles, load order, saves, config editor, profiles, themes — but *without* the optional Nexus integration and the anti-cheat toggle, which live on the GitHub build below.

[![Get it from Microsoft Store](https://get.microsoft.com/images/en-us%20dark.svg)](https://apps.microsoft.com/detail/9N53V6RRJK95)

**[Setup.exe on GitHub Releases](https://github.com/estevanhernandez-stack-ed/626-mod-launcher/releases/latest)** — the full build, everything including the Nexus integration. Windows will warn you the first time (SmartScreen — no code-signing cert on this channel); click **More info → Run anyway**. Installs to your user profile, lands in the Start Menu, and auto-updates from GitHub. Uninstall via the normal Add/Remove Programs.

Either way, the launcher writes nothing outside `%LOCALAPPDATA%\626ModLauncher\` and `%APPDATA%\ModManagerBuilder\`. No registry surgery, no `Program Files` install.

## What's new

- **On the Microsoft Store.** The launcher now ships as a signed Microsoft Store app — install with no SmartScreen warning, auto-updated through the Store. It's the sealed-core build (full mod management; the Nexus integration and anti-cheat toggle stay on the GitHub build).
- **Nexus integration is an off-Store plugin.** On the GitHub build, Nexus support (mod identification, endorsements, update checks) loads as a signed plugin fetched from the [626-mod-plugins](https://github.com/estevanhernandez-stack-ed/626-mod-plugins) repo — verified against a key pinned in the binary, and absent entirely from the sealed Store build. New mod sources can ship without an app release.
- **Signed game-definition feed.** Game definitions stay current from the signed [626-game-manifest](https://github.com/estevanhernandez-stack-ed/626-game-manifest) feed — a new game on an engine the launcher already knows arrives without an app update.

Full notes: **[latest release](https://github.com/estevanhernandez-stack-ed/626-mod-launcher/releases/latest)**.

## What it does

The short version: you point it at a game, it lists your mods, you flip switches.

The slightly longer version:

- **Reversible toggles.** Disabling a mod *moves* its files to a holding folder. Nothing is ever deleted by a toggle. Flip it back and the mod returns where it was.
- **Atomic state.** Every state change writes through `fs-atomic` — temp file, rename, no partial-write corruption. Lose power mid-toggle and your library survives.
- **Drag-and-drop intake.** Drop a `.zip` / `.7z` / `.rar` / loose mod folder on the window. It identifies the mod (CurseForge fingerprint → Nexus md5 → name-search), fills in the real name, author, description, downloads count, and source link. Honor the builders.
- **Engine-aware.** Knows about Bethesda plugins, Unreal pak folders + UE4SS Lua mods, FromSoft Mod Engine 2 configs, BepInEx plugins, Stardew SMAPI mods, and more. Each engine gets its own enable/disable mechanism — the one the mod loader actually expects, not a one-size hack.
- **Framework intake.** Loaders like Elden Mod Loader and Mod Engine 2 install via the same drop-zip flow. The launcher validates the archive first, places it where the loader actually expects (under `Game/` for FromSoft, not the root), and records the install so uninstall is one click from Settings → Installed frameworks.
- **Config editor for direct-inject mods.** Mods that ship as DLL + INI (Seamless Co-op, etc.) get a pencil icon on the row. Edit the INI in-app; snapshot-first, atomic write, "Restore previous" rolls it back. Settings → Direct-inject mod configs handles non-standard install paths via file picker.
- **MP / SP loadouts.** Toggle between multiplayer-safe and single-player-only sets for the same game. Named profiles let you save and switch between configurations ("my Elden Ring NG+ build", "my chill survival setup").
- **Save manager.** Snapshot saves, 3-way clone across save types (Vanilla / Seamless Co-op / Reforged for Elden Ring, etc.), per-type restore, auto-backup-on-launch, prune the autos. The named backups never get pruned.
- **Save mods.** Some mods install into the save tree (Windrose worlds, etc.), not the game folder. Drop a world zip and it installs to the right place — safely guarded against writing to game-managed save folders.
- **Themes.** Seven built-ins (Obsidian, Aurora, Ember, Mint, Matrix, Blueprint, the on-brand 626 Labs). Pick an image — your Discord avatar, a screenshot, anything — and the launcher derives a theme from its dominant colors. Local k-means quantization; no AI in the loop. The same image can become your app icon.
- **Lua keybind editing.** UE4SS Lua mods that bind keys via `RegisterKeyBind` — the launcher reads them, surfaces conflicts, lets you remap. Snapshots first, atomic write.
- **Coordination with — and migration off — Vortex / MO2.** If another tool owns a folder, the launcher shows those mods read-only with a clear "managed by Vortex" badge, and never touches files another tool claims. Moving off that tool? Take the folder over in one click — the launcher archives the old marker out of the way (reversibly) and manages the folder normally, so a migration doesn't leave you stuck.
- **Honest vanilla vs modded launch.** The launch button knows the difference. **Play vanilla** steps every active loader aside for a genuinely clean run; **Play modded** restores your exact set. The label tells you which mode you're in and how it launches — no more "Play vanilla" that quietly runs with your mods loaded.
- **Game definitions stay current.** The launcher ships with a built-in set of game definitions, and keeps them fresh from a signed remote feed (the [626-game-manifest](https://github.com/estevanhernandez-stack-ed/626-game-manifest) repo). At startup it fetches the feed, verifies its signature against a key pinned in the binary, and merges it over the built-in set — gated by an **auto-update definitions** setting, debounced to roughly once a day. Any problem (offline, bad signature, schema it doesn't understand) falls back to the built-in set, so a remote feed can never break a working install. A growing curated set — not thousands of games — but a new game on an engine the launcher already knows arrives without waiting for an app update.

## What it doesn't do

- **Pirate, crack, or modify game code.** It moves mod files around. Your game is your game.
- **Phone home.** No analytics, no telemetry, no "thanks for using" pings. The CurseForge metadata proxy is the only outbound network call by default; the Nexus integration is opt-in and uses YOUR personal API key (never bundled, never shared).
- **Auto-install mods for you.** You drop the file, the launcher places it where the game expects. Discovery — finding new mods on Nexus or CurseForge — is one click, but the actual download stays in your browser.
- **Touch your game's executable.** Mods modify mods. The launcher doesn't patch, inject into, or rewrite game binaries.

## Run it from source

```pwsh
git clone https://github.com/estevanhernandez-stack-ed/626-mod-launcher
cd 626-mod-launcher

# Run the Core test suite (1040+ tests, full architectural contract)
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj

# Build + run the app
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
# (then open the produced bin/x64/Debug/.../ModManager.App.exe)
```

Requires the **.NET 10 SDK** and Windows 10/11. Tests run headless; the WinUI 3 app is x64 / arm64 Windows-only.

> ⚠️ A bare `dotnet test` or `dotnet build` at the root hangs building the WinUI app. Always target the explicit project as shown above.

## How it's built

**Two projects, one philosophy.**

- `ModManager.Core` — pure data + logic + xUnit tests. No WinRT, no UI, no Electron. Every behavior change starts with a failing test here. Runs headless on any platform.
- `ModManager.App` — the WinUI 3 shell. Views, ViewModels, DI host, Windows-specific services (Steam library detection, Mica/Acrylic backdrop, AppWindow.SetIcon). A thin layer over the Core.

That split is the operating discipline that lets the launcher ship features fast without breaking — every file-touching invariant lives in pure functions with tests in front of them, and the UI is allowed to be the messy parts. The full architectural cores (Scanner, Intake, SaveModInstaller, PaletteExtractor, ThemePrompt, etc.) are all under `src/ModManager.Core/`.

## Operating laws

These outrank convenience. Every change in the repo rests on them:

1. **Honor the builders.** Surface attribution, source, donation links, downloads counts. The mod row links straight to the author's page.
2. **Never embed an API key.** CurseForge access goes through a proxy that holds the key server-side. Nexus uses the user's personal key, supplied at runtime, kept on-machine.
3. **File ops stay reversible and atomic.** Temp-write + rename, no clobber on intake, disable rolls back on failure. Snapshot-first on save-tree writes.
4. **Pure-core, thin-shell.** Core stays UI-free and test-first (guarded by `CorePurityTests`). The app shell is allowed to be the messy edges.
5. **Never auto-force mods onto a ban-risk game.** Detecting a game's engine and mod path is fine. Enabling a mod on an anti-cheat/ban-risk title (GameGuard, online EAC, BattlEye) warns you and waits for an explicit acknowledgment — the launcher never enables behind your back, and never refuses your call (disable is always one click away).

## Versions + roadmap

Tag-triggered GitHub Releases ship the Setup.exe + auto-update payload. See [docs/RELEASE.md](docs/RELEASE.md) for the maintainer-side flow.

**Where it's headed:**

- **Agent access — write tools.** The read surface shipped in v0.4.0 (list games + mods over a local MCP server). Next: guarded writes — local-only, per-session token, audit log of every write, consent on first connect. Sketch at [docs/superpowers/specs/2026-05-26-agent-access-design-sketch.md](docs/superpowers/specs/2026-05-26-agent-access-design-sketch.md).
- **Microsoft Store.** MSIX channel parallel to GitHub Releases. Signed by Microsoft, bypasses SmartScreen, auto-updates through Store. Mod manager apps are a 10.2.2 gray area — the GitHub channel is the load-bearing one regardless.
- **Nexus SSO.** OAuth-style sign-in next to the existing personal-API-key flow. Pending Nexus application approval, which wants a public binary to evaluate against — hence the GitHub Release path coming first.
- **Save-mod browser.** Today you drop a world zip and it installs; next is browsing + installing from Nexus's save-mod listings directly.
- **Unified catalog phases 2 + 3.** Phase 1 shipped in v0.3.0 — kind-tagged catalog schema (`directInjectMod` today). Next: third-party tools (Phase 2) and frameworks (Phase 3) fold into the same shape so detection, attribution, and intake stay consistent across types.

## Why it exists

I was using Vortex (Nexus's official installer) for Windrose mods and it kept either silently failing or doing the wrong thing — re-downloading files I already had, missing mods that came from CurseForge, not understanding UE4SS Lua mods, asking me to "deploy" three times after every change. Mod Organizer is great if you live in the Skyrim ecosystem but it didn't fit non-Bethesda games.

So I built this. The thesis: a mod manager should be honest about what it touches, reversible by default, and decent to the modders whose work makes any of this fun. No mystery overlays, no proprietary databases, no "deploy" buttons. And as of v0.5.0, if you're coming from Vortex, the launcher will take your managed folders over for you — migrating off shouldn't mean starting from scratch.

Free, open, licensed per [LICENSE](LICENSE). If you want to contribute, [CONTRIBUTING.md](CONTRIBUTING.md) is a few paragraphs and the test suite is your contract.

## Honor the builders

If you used a mod from one of the modding communities (Nexus, CurseForge, modding Discords) and it made your game more fun, **say thanks where it counts**. The mod row's source link goes straight to the author's page. Tip them, leave a comment, endorse on Nexus. The launcher exists to make their work easier to live with — they're the reason any of us are here.

---

**626 Labs** · [@estevanhernandez-stack-ed](https://github.com/estevanhernandez-stack-ed) · Fort Worth, TX
