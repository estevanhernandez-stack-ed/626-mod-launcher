# Microsoft Store submission — 0.17.0.0 (the redesign)

> **This is a visual submission, not a capability submission.** Since the approved-line 0.15.0.0
> package, the app went through a full design campaign (the "vibe-glow" waves + the road-to-zero
> sweep): a new default theme, a coherent industrial design language across every view and dialog,
> keyboard access, and an accessibility pass. **No new capabilities, no new network endpoints, no new
> data collection.** The one behavioral addition a reviewer could notice: the chosen theme now
> persists across launches (a local `themeId` string in the app's own settings file — nothing leaves
> the machine).
>
> **Package to upload:** build fresh — `dotnet build src/ModManager.App/ModManager.App.csproj
> -c Store -p:StoreNexus=true -p:Platform=x64 -p:Version=0.17.0.0` after wiping
> `src/ModManager.App/AppPackages/` (the 0.15.0.0 test-package identity collision must not repeat).
> Run `pwsh scripts/check-store-seal.ps1`, then **verify the identity out of the bundle itself**
> (`626LabsLLC.626ModLauncher` / `0.17.0.0`) before uploading. Reviewer notes:
> [`store/reviewer-letter-0.17.0.0.md`](store/reviewer-letter-0.17.0.0.md).
>
> **Screenshots: REPLACED this submission.** The live listing's screenshots show the old navy/cyan
> look; the app now ships gunmetal + amber ("Forge") by default. A reviewer comparing screenshots to
> the running app would see a different product. New 1920×1080 set:
> [`store-assets/screenshots-0.17/`](store-assets/screenshots-0.17/) — upload in numbered order
> (home, mods view, Browse Nexus, Updates, Add game, Settings, Save snapshots).
>
> Category (**Utilities & tools**), privacy policy URL
> (`https://github.com/estevanhernandez-stack-ed/626-mod-launcher/blob/master/PRIVACY.md`), and the
> description's factual claims are unchanged from 0.15.0.0.

---

## What's new in this version  *(max 1500 chars)*

```text
The launcher got a new face. This version ships Forge, a gunmetal-and-amber default theme, and a
full redesign behind it: squared corners, a denser mod list, stencil-labeled dialogs, and one rule
carried everywhere — things that are live, glow. An enabled mod's toggle carries a soft halo; the
button that launches your game does too.

Your theme choice now sticks. Pick any of the eight built-in themes (or import your own) and the
launcher remembers it next launch — until now it reset every time.

The keyboard works the way you'd hope: Ctrl+F jumps to the mod filter, Ctrl+R refreshes, Esc backs
out, and Space toggles the mod you've got focused. The filter itself grew up too — it matches the
file name you see on the row, tells you plainly when nothing matches, and resets when you switch
games.

Adding a game now leads with what we detected: your Steam library, with a filter box, and the
games we can set up automatically at the top.

And an accessibility pass throughout: every button reads properly to a screen reader ("Load
profile X", not just "Load"), status updates are announced, contrast holds in every built-in
theme, and if Windows animation effects are off, the launcher's animations turn off with them.

Everything reversible stays reversible. Disabling still moves files aside, saves still snapshot
first, and nothing your files depend on changed shape.
```

*(1,289 chars — fits.)*

---

## Description — no changes

The description's claims (what it does, what it is not, reversibility) are all still true and none
changed. Do not touch the description this submission — the screenshots and what's-new carry the
redesign story. The only tempting edit would be a themes bullet, and the existing "full theming"
bullet already covers it.

## Notes for certification

Use the paste-ready block in
**[`store/reviewer-letter-0.17.0.0.md`](store/reviewer-letter-0.17.0.0.md)**. It leads with "visual
redesign, no capability changes" and names the one behavior a reviewer could trip on (theme
persistence = local settings string), so nothing reads as undisclosed.

## Age rating

**Carried over, correctly this time.** 0.15.0.0 re-ran the questionnaire for the Nexus content
change; nothing about this submission changes any answer (same content sources, same exclusions,
same everything). Carry the 0.15.0.0 answers forward.

## Before you upload

- [ ] `src/ModManager.App/AppPackages/` wiped before the submission build (no test-identity leftovers)
- [ ] Bundle built: `-c Store -p:StoreNexus=true -p:Platform=x64 -p:Version=0.17.0.0`
- [ ] `pwsh scripts/check-store-seal.ps1` passes on this exact build
- [ ] Identity read OUT OF the bundle: `626LabsLLC.626ModLauncher` / `0.17.0.0`
- [ ] All 7 screenshots from `store-assets/screenshots-0.17/` uploaded, old set removed
- [ ] "What's new" pasted
- [ ] Reviewer letter block pasted into Notes for certification
- [ ] Privacy policy URL confirmed unchanged (GitHub-hosted PRIVACY.md)
