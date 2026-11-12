# Microsoft Store submission — 0.28.1.0 (ACE COMBAT 8 offline launch, honest vanilla mode)

> **One GitHub release, and a disclosure.** The Store is on 0.27.0.0. This submission carries 0.28.1:
> the ACE COMBAT 8 offline launch, UE4SS on any Unreal exe, home Play following the game's mode, and
> vanilla mode no longer reporting set-aside loaders as missing, and launches that wait for Steam to
> sign in. No new capability, no manifest
> extension, no new endpoint, no new data. **New for the reviewer:** a launch that starts a game without
> its anti-cheat (AC8), stated alongside the two Elden Ring launches that always did (Seamless Co-op,
> Mod Engine 2), and a correction to the 0.27.0.0 letter's "never bypasses" sentence.
>
> **Package to upload:** built 2026-11-11 from `docs/store-0.28.0` (master `f1d18f9` plus the version
> bump). Identity, version and capability read out of the bundle; seal OK.
>
> ```text
> File      : src/ModManager.App/AppPackages/ModManager.App_0.28.1.0_Store_Test/
>             ModManager.App_0.28.1.0_x64_Store.msixbundle
> Size      : 83.8 MB
> SHA-256   : 6E3F57E76F6B826B20CF87E8DDC52E1ED40E22418544D97CF27C7BD95E03D5F9
> Version   : 0.28.1.0            (application x64)
> Seal      : OK — plugin loader + EAC-disable absent; Nexus compiled in
> Tests     : 4197 passing (Core)
> Submodule : external/626-mod-plugins pinned at nexus-v0.14.0 (87a1bdc)
> ```

> **Why 0.28.1, not 0.28.0.** `v0.28.0` was tagged before #417. Its GitHub draft is superseded and is
> never published; Store and GitHub both ship 0.28.1, built from the same commit.

## Before upload: the packaged-build smoke

The AC8 and vanilla-mode work was verified live on debug builds (FULL). The Store build is the sealed
core, so check on the packaged bundle, installed side-by-side or from the Store flight:

1. **AC8 smoke step 6** (`docs/smoke-tests/pending.md`): the AC8 launch menu lists "Launch offline, no
   anti-cheat". The seal strips the anti-cheat toggle, not this launch; this is the check that proves it.
2. **AC8 Play modded:** the game starts with no EasyAntiCheat process and UE4SS mods load.
3. **AC8 Play vanilla:** EasyAntiCheat runs, no FRAMEWORK chip, the menu offers only Launch via Steam.
4. **Bring my mods back** on AC8: `dwmapi.dll` returns beside the exe.

Rebuild only if the bundle changes, and re-read its identity and hash.

## What changed for a user

- **ACE COMBAT 8 plays modded** (#413): direct exe launch, offline, no EAC, `-SaveToUserDir` + SteamAppId,
  the args of Bandai's internal no-EAC config. Vanilla stays the protected Steam start.
- **UE4SS installs beside any game exe** (#412), not only `*-Shipping.exe`.
- **Home Play follows the game's mode** (#414) and hands launches that need a question to the game view.
- **Launches wait for Steam to sign in** (#417): found on the packaged smoke, where a closed Steam
  made AC8 report "you don't own this game". Ready is now steam.exe plus a signed-in account.
- **Vanilla mode reads honestly** (#415): held loaders are not "missing", the vanilla launch menu drops
  mod launchers and no-anti-cheat targets.

## What's new in this version

Paste [`store/whats-new-0.28.1.txt`](store/whats-new-0.28.1.txt), opening with the `Version 0.28.1` header.
Count it before pasting (limit 1,500).

## Short description, description, product features

Unchanged from 0.27.0.0. Reuse [`store/short-description-0.27.0.txt`](store/short-description-0.27.0.txt),
[`store/description-0.27.0.txt`](store/description-0.27.0.txt) and
[`store/product-features-0.27.0.txt`](store/product-features-0.27.0.txt). Keywords stay as the console
holds them: seven terms, no other manager's name (10.1.3, 2026-11-09).

One line to weigh, not change blind: product feature 5 says playing without mods means "the game starts
with its anti-cheat on". Still true of vanilla. Nothing in the feature list claims the app never skips
anti-cheat, so no feature line contradicts the letter.

## Certification notes

Paste the letter block of [`store/reviewer-letter-0.28.1.0.md`](store/reviewer-letter-0.28.1.0.md) after
Its anti-cheat section is the reason this submission needs a careful read:
it names all three launches that skip anti-cheat and corrects the 0.27.0.0 wording.

## Screenshots

The seven published screenshots carry forward unchanged.

## Store half of the release

As 0.27.0.0: copy into `store-listing-console/apps/mod-launcher/copy/`, config to `0.28.1.0`, then plan,
apply, captions, verify; the owner does Packages, Notes for certification and Properties; verify; commit
from the console. GitHub release `v0.28.1` (a draft, with notes) is published only once the public catalog
serves 0.28.1.0, retitled "v0.28.1 — Live on the Microsoft Store".
