# Microsoft Store submission — 0.18.1.0 (adding a game actually works)

> **This is a bug-fix submission, not a capability submission.** Four fixes to one flow — adding a
> game — plus one visual reversal. **No new capabilities, no new network endpoints, no new data
> collection.** `runFullTrust` remains the only declared capability; verified out of the built bundle.
>
> **Package to upload:** built and verified 2026-08-16 from master `17b618b`.
>
> ```text
> Identity : 626LabsLLC.626ModLauncher
> Publisher: CN=177BCE59-0966-4975-9962-10E36652141F
> Version  : 0.18.1.0
> Capability: runFullTrust (only)
> Size     : 84 MB
> Seal     : OK — plugin loader + EAC-disable mechanism absent from Store binaries
> ```
>
> Reviewer notes: [`store/reviewer-letter-0.18.1.0.md`](store/reviewer-letter-0.18.1.0.md).

---

## Screenshots: the listing shows Forge, this build opens navy

**Answered 2026-08-16 — 0.17.0.0 is live on the Store and is the newest published version.** So the
seven screenshots in `docs/store-assets/screenshots-0.17/` are what a shopper sees today, and they
are all Forge: gunmetal chrome, amber buttons. This build opens navy.

**This is not a certification problem.** Requirement 10.1 asks that screenshots accurately represent
the app. Forge is a theme this build genuinely ships, one click away in Settings — the app really can
look exactly like those shots. Nothing in the listing becomes false. What it becomes is a
first-impression mismatch: amber on the Store page, navy on first launch.

**Recommended: submit now with the Forge screenshots, retake navy afterwards.** The reason this
release exists is a user who added a game three times and got three copies. That fix should not wait
on photography. Screenshots can be changed later in a listing-only submission — no new package, and
those certify quickly.

If you would rather retake first, they must be shot on the main box, not the clean test PC: the 0.17
set shows six games with real cover art, and the test box has almost nothing in it. An empty library
would be a worse listing than a differently-coloured one. All seven or none — a half-swapped set
reads as a rendering bug.

**Screenshot debt is tracked as D2 in `docs/2026-08-05-backlog.md`.** Do not let it close silently.

## Why the default look changed back

0.17.0 made "Forge" (gunmetal + amber) the first-run default as the finish of a UI campaign. That is
reverted here: the app opens on the original navy theme again. Forge is unchanged and still one click
away in Settings.

The reasoning, for the record: the default is the one theme choice the app makes *for* a user rather
than *by* them, and a flagship look is a strong opinion to impose on someone who has not asked for
one. A user who already picked a theme keeps it — the revert only affects installs that never chose.

## What's new in this version  *(max 1500 chars)*

```text
Adding a game now works the way you'd expect.

- The home screen updates immediately when you add a game. Before this, the
  game was added correctly but the screen didn't redraw, so it looked like
  nothing happened — and adding it again just made a duplicate.
- Adding a game you already have now takes you to it, instead of creating a
  second copy of it in your library.
- A fresh install now shows the games it found on your PC straight away.
  Previously that list only appeared after you'd added your first game by
  hand, which is exactly backwards.
- The app opens on the original dark blue theme again. The gunmetal "Forge"
  theme is still there in Settings if you prefer it — this only changes what
  you get before you've picked.

Nothing about what the app does with your files has changed.
```

## Description — no changes

Unchanged from the approved listing. Category (**Utilities & tools**) and the privacy policy URL
(`https://github.com/estevanhernandez-stack-ed/626-mod-launcher/blob/master/PRIVACY.md`) are
unchanged. No factual claim in the description is affected by this release.

## Notes for certification

Paste [`store/reviewer-letter-0.18.1.0.md`](store/reviewer-letter-0.18.1.0.md) into the
certification-notes box. It states plainly what changed, what did not, and why a reviewer comparing
this build to the last approved one may see a different default colour scheme.

## Age rating

Unchanged. No user-generated content is displayed in-app beyond mod names and descriptions the user
already has on disk or fetches from a source they connected themselves.

## Before you upload

1. **Screenshots: reuse the existing Forge set** (see above). Do not upload a mixed set.
2. Confirm the submodule pointer is what you intend — `external/626-mod-plugins` is pinned at
   **`nexus-v0.14.0`** for this build. The Store SKU compiles Nexus in from that pin; the GitHub SKU
   downloads a signed plugin from the feed. They do not advance together. See *Decide, every plugin
   release, whether the Store SKU follows* in `docs/release-msstore.md`.
3. Wipe `src/ModManager.App/AppPackages/` before building, so a side-load test package cannot
   overwrite the submission bundle (this bit us on 0.15.0.0).
4. **Bump `Version` in `src/ModManager.App/Package.appxmanifest`.** `-p:Version` does NOT set the
   MSIX package version — it is hardcoded in that file. This was discovered while cutting 0.18.1: a
   build passing `-p:Version=0.18.1.0` produced a `0.17.0.0` package.
5. Build: `dotnet build src/ModManager.App/ModManager.App.csproj -c Store -p:StoreNexus=true
   -p:Platform=x64`
6. `pwsh scripts/check-store-seal.ps1`
7. **Verify the identity by reading it out of the bundle**, never off the manifest on disk. Done for
   this build; re-do it if you rebuild.
