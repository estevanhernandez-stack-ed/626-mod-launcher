# Microsoft Store submission — 0.25.0.0 (the game's own regulation.bin stays put)

> **A fix release.** Nothing reaches further than 0.24.0.0: no new capability, no manifest extension, no
> new endpoint, no new data. The letter lists four changes, each of which moves fewer of the user's files
> or says more about what the app did.
>
> **Package to upload:**
>
> ```text
> File      : src/ModManager.App/AppPackages/ModManager.App_0.25.0.0_Store_Test/
>             ModManager.App_0.25.0.0_x64_Store.msixbundle
> Size      : 83.7 MB
> SHA-256   : 648D451011DDB4CAAE894C5BCCAD71BA1DC116F888805BC143229EDB5004A9B9
> Identity  : 626LabsLLC.626ModLauncher
> Publisher : CN=177BCE59-0966-4975-9962-10E36652141F
> Version   : 0.25.0.0            (application x64)
> Capability: runFullTrust  — the only one declared; no manifest extensions
> Target    : Windows.Desktop, min 10.0.17763.0
> Seal      : OK — plugin loader + EAC-disable absent; Nexus compiled in
> Tests     : 3866 passing (Core), 42 of 42 UI Automation smoke checks (master c63f22e)
> Submodule : external/626-mod-plugins pinned at nexus-v0.14.0 (87a1bdc)
> Source    : master 5d2203e plus the version bump in this commit
> ```
>
> Identity, version and capability above were read **out of the bundle**, not off the manifest on disk.
> The build printed one warning: `mspdbcmf.exe` not found, so no symbols package (`.appxsym`) was made.
> That affects crash-dump symbolication only, not the package.

---

## What changed for a user

- **Elden Ring's own `regulation.bin` stays put** (#397). It counts as a mod only when 626 installed it.
  Return to vanilla names an unprovable one as left in place, with a verify-before-online note.
- **Cyberpunk standalone `.xl` mods list and toggle** (#396).
- **Fallout 4 / Starfield `.ba2` archives move with their plugin** (#398); base archives never move.
- **The ban-risk warning states the risk, not a mechanism** (#393).
- **Enable all continues past a refusal** and names it (#397).

## What's new in this version

Paste [`store/whats-new-0.25.0.txt`](store/whats-new-0.25.0.txt): **1,055 of 1,500**, opening with the
`Version 0.25.0` header.

## Short description and description

- [`store/short-description-0.25.0.txt`](store/short-description-0.25.0.txt): **238 of 270**, unchanged.
- [`store/description-0.25.0.txt`](store/description-0.25.0.txt): two lines reworded, none added. The
  anti-cheat line now reads "games that carry a ban risk", and the attention row says "ban risk" instead
  of "anti-cheat risk", matching the app after #393.

## Product features

[`store/product-features-0.25.0.txt`](store/product-features-0.25.0.txt): **20 of 20**, longest **173**.
The same two rewordings.

## Certification notes

Paste the letter block of [`store/reviewer-letter-0.25.0.0.md`](store/reviewer-letter-0.25.0.0.md)
(2,775 characters).

## Properties (hand-only, from the next-submission reminder task)

Set **Support contact info** to `support@626labs.dev` on the Properties page while uploading the package.
The API's field is obsolete, so only the page sets it.

## Screenshots

The seven published screenshots carry forward unchanged.

## Store half of the release

As 0.24.0.0: copy into `store-listing-console/apps/mod-launcher/copy/`, config to `0.25.0.0`, then plan,
apply, captions, verify; the owner does Packages, Notes for certification and Properties; verify; commit
from the console. GitHub release `v0.25.0` is published only once the public catalog serves 0.25.0.0.
