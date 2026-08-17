# Notes for certification — reviewer letter — v0.18.1.0 (adding a game actually works)

> **Written after the submission build, and checked against it.** Identity, version, capability and
> seal were all read out of the real package (`626LabsLLC.626ModLauncher` / `0.18.1.0` /
> `runFullTrust` only / seal OK) before this letter was finalised. Every claim below matches that
> package.
>
> **The honest headline:** four bug fixes to a single flow — adding a game — and one visual
> reversal. Nothing this app does with a user's files changed.
>
> **The one thing a reviewer might notice and wonder about** is the colour scheme, and which way it
> reads depends on what was last approved. If 0.17.0.0 was approved, this build *reverts* the default
> theme that submission introduced. If 0.17.0.0 was never submitted, this build simply matches the
> approved listing. **Pick the matching paragraph below before pasting.**

---

```text
Hello reviewer,

Thank you for your time on version 0.18.1.0.

WHAT CHANGED SINCE THE LAST APPROVED VERSION

This is a bug-fix release. Four fixes, all in one flow: adding a game to
the library.

1. The home screen did not redraw after a game was added. The game WAS
   added correctly and written to the user's local config, but the screen
   still showed the old contents, so it looked as though nothing had
   happened.

2. Because of (1), users retried. Each retry created a duplicate entry.
   Adding a game the user already has now switches to that game instead of
   registering a second copy of it.

3. On a machine with no games added yet, the list of games found on the PC
   was hidden. It only appeared after the user had already added a game by
   hand, which is the opposite of when it is useful. It now appears on a
   fresh install.

4. The app's default colour scheme returns to the original dark blue.

NO CAPABILITY, NETWORK OR DATA CHANGES

No new capabilities are declared; runFullTrust remains the only one, as in
previously approved versions. No new network endpoints. No new data is
collected, stored or transmitted. The fixes above are entirely local UI and
local-config behaviour.

ABOUT THE COLOUR SCHEME  [KEEP ONE PARAGRAPH — DELETE THE OTHER]

[If 0.17.0.0 was approved:]
Version 0.17.0.0 changed the app's default theme to a gunmetal-and-amber
look. This version reverts that: the app opens on the original dark blue
theme again. The gunmetal theme is unchanged and remains available in
Settings. This affects only the default a user gets before choosing; a user
who has already chosen a theme keeps their choice. The listing screenshots
have been updated to match this build.

[If 0.17.0.0 was never submitted:]
No visual change since the last approved version. An interim build changed
the default theme; that change was reverted before submission, so this
package matches the approved listing's screenshots.

WHY runFullTrust IS REQUIRED  (unchanged from prior submissions)

The app manages mod files inside PC game installation folders that the user
selects. It reads, moves and restores files in those folders on the user's
instruction. That is not possible from a sandboxed container, so
runFullTrust is required for the app's core purpose.

WHAT THE APP DOES NOT DO

It does not bypass, disable or interfere with any anti-cheat system. Where a
game is known to carry a ban risk for modding, the app warns the user and
requires an explicit acknowledgement before enabling mods, and it never
enables them silently.

Thank you again for your time.

- 626 Labs LLC
```

---

## Claim-by-claim, against the built package

| Claim in the letter | How it was verified |
|---|---|
| Identity `626LabsLLC.626ModLauncher`, version `0.18.1.0` | read out of the `.msixbundle` → inner `.msix` → `AppxManifest.xml`, not off the manifest on disk |
| `runFullTrust` is the only capability | same extraction — one `<rescap:Capability>` element |
| No plugin loader in the Store build | `scripts/check-store-seal.ps1` — "STORE seal OK" |
| No new network endpoints | no source change in this release touches an HTTP client or endpoint; the four fixes are UI and registry-local |
| No new data collected | the only write is the existing local games registry and settings file, both already covered by the approved privacy policy |

## What is deliberately NOT claimed

- **Nothing is claimed about automated test coverage of these fixes.** All four are WinUI
  code-behind or view-model changes and are not reachable from the headless test suite. They were
  verified by hand on a clean machine. That limit is recorded in `docs/smoke-tests/pending.md`
  rather than papered over.
- **No claim that the app was re-reviewed against every certification requirement** — this letter
  addresses the delta since the last approved version, which is what changed and what did not.
