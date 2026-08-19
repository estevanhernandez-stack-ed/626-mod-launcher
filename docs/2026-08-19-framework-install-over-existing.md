# Installing a framework over one that is already there, in a different layout

**Date:** 2026-08-19 · **Found:** driving the wave 8 `NEEDS` chip end to end on Windrose · **Status:** needs a product decision

## What happened

Windrose had UE4SS installed under `R5\Binaries\Win64\ue4ss\` — the 3.1+ layout, originally deployed by
Vortex and later taken over by the launcher (19 files recorded in the install manifest).

Uninstalling it, then reinstalling from **`UE4SS_v3.0.1.zip`**, extracted to a *different place*:

```
R5\Binaries\Win64\UE4SS.dll            ← 3.0.x layout: files at the root
R5\Binaries\Win64\UE4SS-settings.ini
R5\Binaries\Win64\Mods\...             (21 files in total)
```

The `ue4ss\` tree from before was still present. So the game folder ended up holding **two UE4SS
installs in two layouts**, and the launcher reported success: *"Installed UE4SS (21 files…)"*.

Nothing detected the collision. `FrameworkInstallDialog` showed the file list and the destination —
accurately — and the overwrite preview found nothing to warn about, because at those paths there was
genuinely nothing to overwrite.

## Why it is not obviously a bug

UE4SS 3.0.x really does ship flat, and 3.1+ really does ship under `ue4ss/`. Extracting each per its
own layout is defensible. On a clean install either one works.

The problem is only visible when one is already there.

## What is actually missing

The installer asks *"will I overwrite anything at the paths I am about to write?"* It never asks
**"is this framework already installed here, anywhere?"** Those are different questions, and the
second one is the one a user cares about.

The launcher already knows the answer: `FrameworkRegistry.List(dataDir)` would have said UE4SS was
installed, and the manifest records exactly where. Nothing consults it at install time.

## Options

1. **Refuse, and say why.** If the framework is already in the registry, the install dialog says
   *"UE4SS is already installed here (19 files under `ue4ss\`). Remove it first, or continue and you
   will have two copies."* Safest; costs a click on a genuine upgrade.
2. **Offer to replace.** Detect it, and make the primary action *uninstall the recorded copy, then
   install this one*. Closest to what a user pressing this actually wants — and reversibility already
   holds on both halves, so it is composable from things that exist.
3. **Warn only.** Install anyway, but name the existing copy in the dialog first.

**Recommendation: 2, falling back to 1** when the recorded copy cannot be cleanly removed. It is the
only option where "install the framework" leaves the folder in a state the launcher can still describe.

## What this does not change

The uninstall itself is correct and was verified by diff: it deleted all 19 recorded files, then
restored the 18 that pre-dated its own install from the snapshot it took, leaving only the file it had
actually added removed. Reversibility holds. The gap is in **install**, not uninstall.

## Reproducing

Requires a game with a framework installed in one layout and an archive of the same framework in
another. On this machine that is Windrose + a UE4SS 3.0.x zip; the 3.1+ zip would not collide.
