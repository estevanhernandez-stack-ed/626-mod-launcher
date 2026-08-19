# 626 Mod Launcher v0.19.0

*Paste-ready for the GitHub Release body. 40 commits since v0.18.1.*

---

The launcher knew a lot about your games and said most of it badly. This release is mostly about
where the app puts things and what it calls them — plus one control that was doing something quite
different from what it looked like.

## One row for what's true about a game

Anti-cheat risk, a missing launch option, a missing framework, setup drift, a Steam update, a broken
co-op install, mods that might desync, and folders another tool owns — eight things, rendered in two
different visual registers, with three different ways to make one go away. The one that can cost you
your game account was the smallest thing on the screen, wedged between a hint and the theme picker.

They're one row of chips above the mod list now, **ordered by what the situation actually costs you**.
Account first, then "nothing will load", then "some things won't load", then "this might be stale",
then "this affects other players", then "another tool owns this folder". The most serious one reads as
a full sentence without you tapping anything.

A ninth state had no surface at all: the launcher had learned to tell you *"UE4SS — loader present,
runtime missing"* instead of just *"Missing: UE4SS"*, and then displayed it nowhere. It shows up now.

## MP / SP were not a filter

Three buttons marked **All / MP / SP**, under a heading that said LOADOUT, shaped exactly like a view
filter in every other app you own. They enabled every mod matching the mode and disabled everything
else — a bulk file operation wearing a view control's clothes. And on some games they did nothing at
all, with identical styling.

**They filter now**, on every engine. Applying a set of mods is a separate, named action that tells
you how many will turn on and how many will turn off before it does anything.

## Enable all / Disable all stop destroying what you had

Every file they moved was already reversible. The *knowledge* wasn't: press `Disable all` on a
200-mod setup and the fact that 140 of them were on is gone — `Enable all` isn't the undo, because it
turns on all 200 including the sixty you'd deliberately switched off.

They save a profile first now, and the status line tells you its name. An undo nobody mentions isn't
an undo.

## Things that were there and didn't say so

- **The in-app Nexus browser stopped vanishing.** It disappeared whenever it couldn't be used — which
  made the app look like it had never had one. It stays put and names the single step that would make
  it work. And the two ways to find mods now say *where they land*: **Find mods (in-app)** and
  **Find mods in browser**.
- **An empty mod list says something.** A registered game with no mods used to render a blank
  rectangle at exactly the moment you most need to be told to drop a zip on the window. Four different
  reasons a list can be empty, four different sentences, each naming what to press.
- **The `NEEDS UE4SS` chip offers to install it.** It used to be a link to a releases page full of
  files you can't choose between. The launcher could always install it from an archive you already
  have; now it says so.

## Settings

Four groups and a footer — **Appearance, Accounts, Restore points, Reset** — instead of one scroll
with nine headings and About sitting fourth. The inventories left: framework removal moved onto the
framework's own chip (with a confirmation it never used to ask for), and per-mod config paths moved
onto the mod row they configure.

Restore points get their own heading rather than living under Reset. They're the undo *for* it, and
filing them under a danger heading teaches you to fear the control that saves you.

## Words and keys

One word per thing. "Profile" meant three different things — a saved set of mods, a game definition,
and an engine's save types — two of them visible one click apart. It means the saved set of mods now,
everywhere. `LOADOUT` became `SHOW`, the second `LIBRARY` became `MANAGE`, `VIEW` became `GROUP BY`.

Seven keyboard shortcuts, each named in its own tooltip:

| | |
|---|---|
| `Ctrl` `,` | Settings |
| `Ctrl` `O` | Add mods |
| `Ctrl` `P` | Profiles |
| `Ctrl` `1` `2` `3` | Show all / MP / SP |
| `Ctrl` `F` · `Ctrl` `R` · `Esc` | (as before) |

## Also in this release

- **Launch verification** — the launcher watches a launch and tells you whether your loaders actually
  ran, with an honest *"couldn't tell"* verdict instead of a guess.
- **Loader identity** — it names the loader that was doing the loading, on any engine.
- **Intake keeps the folder tree an archive declares** instead of flattening it.
- **Mod provenance** — the launcher records what it placed, so a row can say which files are its own.
- **Registration repair** for games whose mod folder was recorded wrong.
- **A smoke harness** that drives the app through UI Automation, plus a machine-readable catalogue of
  what is verified, what is pending, and what no harness may ever claim.
- **Agent access** — the MCP server gained its first write tool, gated and audited.

## Fixes

- Adding the same game twice no longer creates a duplicate registration.
- Play-vanilla steps aside proxy loaders on every engine, not just FromSoft.
- A library can be switched off when nothing that needs it is on.
- The cached game-definition feed is applied at startup, and seeds a mod folder the feed names.

## For Store users

The Microsoft Store build now compiles the Nexus integration in **by default**. It was opt-in at build
time, and the automated workflow never opted in — so a package built by CI would have shipped without
it. The build seal checks for its presence now, not only for the absence of things that must not ship.

---

**Full changelog:** https://github.com/estevanhernandez-stack-ed/626-mod-launcher/compare/v0.18.1...v0.19.0
