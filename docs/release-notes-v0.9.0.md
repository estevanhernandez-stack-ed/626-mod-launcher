# Release notes — v0.9.0

This file holds two paste-ready artifacts for the v0.9.0 cut:
1. **GitHub Release body** (fuller notes)
2. **Microsoft Store "What's new in this version"** blurb (short)

---

## 1. GitHub Release body

> Paste everything between the rules below into the GitHub Release body.

---

## v0.9.0 — the ban-risk warning now points at the safe way in

The anti-cheat warning stops being a dead end. It names the game's anti-cheat-safe loaders and hands you a one-click way to launch them — and when 626 can't read a game's engine, you can ask for it in one click instead of guessing.

### What's new

- **The ban-risk warning now shows you the safe path.** Hit the anti-cheat acknowledgment on a game like Elden Ring and it now lists that game's anti-cheat-safe loaders — Mod Engine 2, Seamless Co-op — with **Launch** if the loader's installed or **Get it here** if it isn't. The warn-and-acknowledge gate is unchanged; nothing enables itself. The warning just stopped being a shrug.
- **Detected mod loaders show up as one-click buttons in the bar.** If a loader's installed in the game folder, 626 surfaces a **Launch via Mod Engine 2** / **Launch via Seamless Co-op** button. 626 stays the hub and manages your files and saves; the loader does the loading. Saves snapshot first where a launch touches them.
- **Anti-cheat-safe guidance works on the Microsoft Store build too** — and there it's the *primary* safe path, since the Store SKU ships without the EAC offline toggle. Same warning, same safe loaders, surfaced the same way.
- **"Request this game" for undetected engines.** Add a game whose engine 626 can't read and you get a **Request this game** link that opens a prefilled GitHub issue against the game-definition feed repo — name and Steam id already filled in. The link retires itself the moment you pick an engine by hand. No form to hunt down, no typing the same fields twice.

Loaders surfaced this release — never bundled, detection + a Get-it-here link only:
- **Mod Engine 2** — thanks to soulsmods.
- **Seamless Co-op** — thanks to LukeYui.

### Under the hood

- **Ban-safe loaders + loaders-in-the-bar** (#160). New `KnownLoader` catalog (Core) carries Mod Engine 2 + Seamless Co-op, each flagged `BanSafe`, with launcher-exe detection hints and a `GetUrl` — the four existing catalogs are untouched. `LoaderScan` (Core, TDD'd) detects a loader's launcher exe in the play folder and resolves a game's ban-safe loaders. The bar reuses the existing `ToolLauncher` process-start path; the ban-risk dialog pulls the game's `BanSafe` entries into the warning. The warn-and-acknowledge gate is byte-for-byte intact — this is purely additive guidance, no silent anti-cheat disabling, no bundled binaries (NOTICE updated). Both flavors, no `#if FULL`; on the Store SKU the loader guidance is the primary safe path because the offline toggle is stripped there. Spec: `docs/superpowers/specs/2026-06-26-ban-safe-loaders-design.md`. Plan: `docs/superpowers/plans/2026-06-26-ban-safe-loaders.md`.
- **Request a game** (#161). Pure Core URL builder `GameRequestUrl.Build` constructs the prefilled `issues/new?template=game-request.yml` URL — title, name, Steam id, notes, all escaped via `Uri.EscapeDataString`. The feed repo's engine dropdown is a required field with fixed options, so the builder maps our engine key to the exact option string (default `Not sure`), verified char-for-char against the live template down to the em-dash codepoint — drift fails a test. The App surfaces a `HyperlinkButton` in `AddGameDialog`, shown only when the engine isn't resolved, opening the URL via `Windows.System.Launcher.LaunchUriAsync` (mirrors `FrameworkUnrecognizedNudgeDialog`). Both flavors, no new constructor param. Plan: `docs/superpowers/plans/2026-06-26-request-a-game.md`.
- **Manifest feed:** Core suite at 1328/0 on combined master; FULL + STORE build clean; STORE seal holds; CorePurity green (#162 bumps the MSIX package to 0.9.0.0).

### Already live (no update needed)

- **The game-definition feed grew to ~95 games with a featured / quick-pick pass.** This is feed-delivered — it's already reached existing installs at runtime, no app update required. New game coverage keeps landing the same way: a data PR to the feed repo, not a launcher release. (And now "Request this game" is how you start that PR.)

---

## 2. Microsoft Store "What's new in this version"

> Paste plain text into the Store "What's new" field.

The anti-cheat warning now points you at the safe way in. On games like Elden Ring it lists the anti-cheat-safe loaders — Mod Engine 2, Seamless Co-op — with Launch if you have them or a link to get them. The warning and your acknowledgment are unchanged; nothing enables itself.

Detected loaders also show up as one-click Launch buttons in the bar — 626 manages your files and saves, the loader does the loading.

And when 626 can't read a game's engine, a new Request this game link opens a prefilled report so we can add it.
