# Notes for certification — reviewer letter — v0.17.0.0 (the redesign)

> **Status: written before the submission build, on purpose** — it names every claim the build must
> satisfy. Before submitting: build the bundle, run `scripts/check-store-seal.ps1`, verify the
> identity out of the bundle (`626LabsLLC.626ModLauncher` / `0.17.0.0`), and re-read every claim
> below against the real package.
>
> **The shape of this letter:** 0.15.0.0's letter led with a capability reversal (in-app Nexus
> browsing) because that was the honest headline. This submission's honest headline is smaller and
> should be stated just as plainly: **the app looks different, and nothing about what it does
> changed.** A reviewer comparing this package to the approved listing's screenshots will see a
> different-looking product — we replaced the screenshots in the same submission so listing and app
> match again.
>
> **The one behavioral addition** worth a reviewer's attention: the chosen UI theme now persists
> across launches. Implementation is a string in the app's own local settings file
> (`%APPDATA%\ModManagerBuilder\app-settings.json`). It is not synced, not transmitted, and not
> tied to any account.

---

```text
Hello reviewer,

Thank you for your time on version 0.17.0.0.

WHAT CHANGED SINCE THE APPROVED 0.15.0.0 — PLEASE READ FIRST

This is a visual redesign release. The app's default theme, typography,
spacing, and dialog styling all changed (the app now defaults to a
gunmetal-and-amber look). We replaced the listing screenshots in this
submission so the store page matches the product you are reviewing.

Functionally, nothing was added or removed relative to the approved
0.15.0.0:

  - No new capabilities are declared. runFullTrust remains the only
    declared capability, unchanged.
  - No new network endpoints. The app talks to the same services as the
    approved version (Nexus Mods API with the user's own signed-in
    account, for the in-app browsing approved in 0.15.0.0), and every mod
    download still happens in the user's web browser on nexusmods.com —
    the app itself never downloads, installs, distributes, or sells any
    mod or game content, and there is no commerce of any kind in the app.
  - No new data collection. One new locally-stored preference: the user's
    chosen UI theme is now remembered between launches, as a plain string
    in the app's local settings file on the user's machine. It is not
    transmitted anywhere.
  - Adult-flagged content remains excluded server-side in the Nexus query,
    exactly as in the approved version.

WHAT THIS RELEASE ACTUALLY CONTAINS

  - A new default visual theme and seven other built-in themes (all
    previously present; the default changed).
  - An accessibility pass: proper UIA names on every interactive control,
    announced status updates, verified text contrast in every built-in
    theme, and the app honors the Windows "animation effects" setting —
    when the user turns animations off, the app's animations stop.
  - Keyboard access: Ctrl+F (filter), Ctrl+R (refresh), Esc (back),
    Space (toggle the focused mod).
  - Quality-of-life polish: a better mod-list filter, a reorganized
    "Add a game" dialog that leads with the user's detected Steam library,
    and clearer error messages.

UNCHANGED, AND STILL TRUE

  - The app manages mod files the user already has on their own machine.
    Disabling a mod moves files aside; nothing is deleted by a toggle.
  - The app never modifies game binaries and never bypasses or disables
    any anti-cheat. On games known to carry account risk for online
    modding, the app warns and requires explicit acknowledgment before
    enabling mods.
  - Sign-in to Nexus Mods is optional. Every file-management feature works
    without it. Authentication is OAuth in a loopback flow; the user's
    token stays on the machine.
  - Privacy policy (unchanged):
    https://github.com/estevanhernandez-stack-ed/626-mod-launcher/blob/master/PRIVACY.md

Thank you again for your review.
626Labs LLC
```
