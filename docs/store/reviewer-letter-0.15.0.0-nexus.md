# Notes for certification — reviewer letter (Store build WITH Nexus) — v0.15.0.0

> **Status: claims verified against the built package (2026-08-03).** The bundle exists at
> `src/ModManager.App/AppPackages/ModManager.App_0.15.0.0_Store_Test/ModManager.App_0.15.0.0_x64_Store.msixbundle`.
> It was written before the build on purpose: it names every claim the build must actually satisfy, so the build gets shaped by what we can
> honestly say rather than the letter being retrofitted to whatever got built. Fill the version, re-run the
> pre-submission checks, and re-read every claim against the real package before submitting.
>
> **This is the first submission that reverses our headline claim.** Every prior letter led with
> *"NO IN-APP MOD STOREFRONT IN THIS PACKAGE."* This one leads with the opposite. A reviewer diffing against
> the approved v0.11.2.0 will see that reversal immediately, so we state it ourselves, in the first
> paragraph, rather than letting them discover it. Do not soften or bury it — being caught understating it
> is far worse than the clause itself.
>
> **The two load-bearing facts** (everything else supports these):
> 1. The app never acquires, downloads, installs, or sells mods. Every "get" opens the user's browser to the
>    author's page on nexusmods.com. There is no in-app acquisition and no commerce of any kind.
> 2. Adult content is excluded **server-side, in the query** — it never reaches the app, which is why there
>    is no age gate to evaluate and nothing to moderate client-side.

---

```text
Hello reviewer,

Thank you for your time on version 0.15.0.0.

WHAT CHANGED SINCE THE APPROVED 0.11.2.0 — PLEASE READ FIRST

Previous submissions of this app stated that it contained no in-app mod
browsing. That is no longer true, and we want to be the ones to tell you.
This version adds an in-app browser for Nexus Mods, an established
third-party mod hosting site, for the game the user is managing. We are
declaring it up front because it is a real change to how this app behaves.

626 Mod Launcher remains a load-order and file-management utility for PC
games the user already has installed on their own machine.

THE APP DOES NOT DISTRIBUTE, DOWNLOAD, OR SELL ANY CONTENT

This is the important distinction, and it is absolute:

  - The app never downloads a mod. Not in the background, not on request.
  - Every "Download on Nexus" control opens the user's default web browser
    to that mod's page on nexusmods.com. Acquisition happens there, in the
    browser, on Nexus's own site, under Nexus's terms — exactly as it would
    if the user had searched the web themselves.
  - There is no commerce anywhere in the app. Nothing is sold, no payment
    is taken or facilitated, and there is no in-app currency, catalog
    purchase, or subscription.
  - After downloading in their browser, the user drags the file onto the
    app themselves, and the app organizes the file they already have.

So the in-app surface is a search-and-read view over a public catalog, with
links out. The app is not a distribution channel and cannot install remote
content.

NO ADULT CONTENT REACHES THIS APP (11.12 / 11.13)

Mature and adult-flagged mods are excluded at the source: every catalog
query the app issues carries a server-side filter that excludes adult
content, so adult listings are never returned to the app in the first
place. This is a structural property of the request, not a client-side
filter that could be toggled, bypassed, or fail open.

Consequently the app contains no age gate, no adult-content toggle, and no
"show mature results" option — there is nothing for such a control to
reveal. Listings show a mod's title, author, description, an image, and
public counts (endorsements, downloads); all of it is content Nexus already
publishes publicly, filtered as described.

Users may still, of course, obtain any file they like from the web
themselves and drag it in — at that point the app is managing a file on the
user's own disk, and it does not inspect or judge its content, in the same
way a file manager or archive tool does not.

ACCOUNT SIGN-IN (OPTIONAL)

The user may optionally sign in to their own Nexus Mods account using the
standard OAuth authorization-code flow with PKCE, in their browser. We never
see or store a password. Signing in enables reading their own state (which
mods they have already downloaded or endorsed) and lets them endorse or
track a mod — actions the user explicitly clicks, applied to their own
account on Nexus. Sign-in is entirely optional; the app's file-management
features work fully without it.

NO DOWNLOADED OR DYNAMIC CODE (10.2.x)

Everything this package runs ships inside the package and is reviewable by
you. This build contains no plugin loader and no mechanism to download,
side-load, or execute code obtained at runtime. Our off-Store distribution
delivers this Nexus integration as a downloaded signed add-on; that loader
is COMPILED OUT here, and a seal check (scripts/check-store-seal.ps1) reads
every shipped DLL as raw bytes and fails the build if the loader symbols
appear. An anti-cheat-related toggle available in our off-Store build is
likewise compiled out and covered by the same check.

REVERSIBLE FILE MANAGEMENT (runFullTrust)

It writes into the user's own game folders to enable and disable mods, and
does so reversibly: disabling moves files to a holding folder rather than
deleting them, and replacing a file snapshots the original first. It
requests runFullTrust for this ordinary file management; it does not modify
Windows system files or other applications.

NETWORK AND PRIVACY

Outbound calls are: the Nexus Mods public API (catalog search and mod
details; plus the user's own account state and their endorse/track actions
when signed in), our read-only CurseForge metadata proxy used to identify a
mod file already on disk, and our own update and game-definition feeds. No
telemetry. No advertising. No personal data is collected by us; the Nexus
account is the user's own and its token is stored encrypted on their
machine only.
Privacy policy: https://626labs.dev/privacy.html

Game names and mod content belong to their respective publishers and
authors. 626 Mod Launcher is an independent utility, not affiliated with or
endorsed by any game publisher or mod host. Source is public at
https://github.com/estevanhernandez-stack-ed/626-mod-launcher

To exercise it: launch the app, add a game (or let Steam detection find
one), open it, and click "Browse Nexus". Search, open a mod for details, and
press "Download on Nexus" — note that this hands off to your browser rather
than downloading anything in the app. Signing in is optional and can be
skipped entirely.

If anything is unclear, please reach out and we will respond same-day.

Estevan Hernandez
626 Labs LLC
```

---

## Defenses by clause

| Clause | Defense | If rejected, escalate with |
|---|---|---|
| **10.1.6 / 11.12** in-app storefront — **the one we expect to be challenged** | The app never acquires, downloads, installs, or sells content; every get is a browser handoff to the author's page on nexusmods.com. No commerce of any kind. The in-app surface is search-and-read over a public catalog, with links out. | A screen recording of the full flow ending in the browser opening. Point out no code path performs a mod download — the app's only file intake is the user dragging in a file they already have. Offer to add an explicit "opens in your browser" label on the control if that resolves it. |
| **11.12 / 11.13** UGC + moderation | Adult content is excluded **server-side in every query**, so it never reaches the app; hence no age gate and nothing to moderate client-side. Displayed fields are public catalog metadata. | Show the literal query filter and the code path proving it is unconditional. Offer to add a user-reporting affordance that deep-links to Nexus's own report flow if they want a report path. |
| **10.2.x** downloaded/dynamic code | No plugin loader, nothing executed that did not ship in the package; the off-Store loader is compiled out and binary-verified absent by the seal check. | Seal output + MSIX inspection showing no loader symbols and no plugin DLL. |
| **Prior-statement consistency** | We disclose the reversal in the first paragraph rather than letting a diff surface it. | — |
| **10.5** privacy | No telemetry, no PII collected by us; optional OAuth sign-in with the token encrypted on-machine; privacy policy linked. | Privacy policy URL; note the app is fully usable signed-out. |
| **Age rating** | Re-run the age-rating questionnaire for this version: it now displays third-party UGC. Answer honestly, noting the adult exclusion. **Do not carry the old rating over unexamined.** | — |

## Pre-submission checks — MUST all be re-verified against the real package

- [x] `Package.appxmanifest` Identity `Version` matches the submission, 4th component zero — **verified in-bundle: 0.15.0.0**
- [x] `PublisherDisplayName` = `626Labs LLC`
- [x] `pwsh scripts/check-store-seal.ps1` → **STORE seal OK** — verified on this exact build, with Nexus IN
- [x] Inspected the `.msixbundle`: **no plugin DLL inside**; loader symbols (PluginFeedSource / WirePluginFeed / LoadVerified / AssemblyLoadContext) all **0** in the shipped `ModManager.App.dll`; `NexusModSource` present (15) — i.e. Nexus is compiled in, not loaded
- [x] **Age-rating questionnaire re-run** (Online Content = Yes; Violence = Yes, since mod screenshots for M-rated titles can depict combat/blood)
- [ ] **Confirm the adult filter is actually in every shipped query path** (not just the ones we remember) before claiming it in writing
- [x] Sign-in confirmed in a packaged build (side-loaded 2026-08-03: signed in, Browse Nexus present, storefront working). **Re-confirm once on this exact bundle before uploading.**
- [ ] Privacy policy updated to mention the optional Nexus account and what it is used for
- [ ] Every claim in the letter re-read against the built package — **no claim may outrun the build**
