# Wave 4 — what the launcher knows about a folder it doesn't own

**Date:** 2026-08-18 · **Items:** A25, A23, A24
**Why now:** all three were found in one sitting on Death Stranding 2, they share a root, and two of
them are the reason the third can't be fixed.

## The theme

Every file operation on that game was correct. Every *claim* about those operations had something
wrong with it. That is the shape of all three entries: **a loose-root game puts mods in a folder the
launcher does not own, and the launcher keeps reasoning about it as though it did.**

- **A25** — it doesn't write down what it placed there, so it can't tell its own work from the game's.
- **A23** — so it guesses, and calls five of the game's own folders "libraries".
- **A24** — and it names a loader from its filename, then states a consequence that isn't true.

Fix them in that order, because A25 is the evidence A23 needs and A24 is independent.

---

## A25 — record what a loose-root intake placed · `S`

**Observed.** Dropped a zip on DS2. It installed correctly. No `installs/` directory was written, and
the row read *"Detected: loose .asi in game root"* about a file 626 had placed thirty seconds earlier.

**Cause, one early return.** `Scanner.ExecuteIntake` writes the manifest at the very end — deliberately,
so *"a manifest never claims a file that is not on disk."* Two branches return before reaching it:
`form == "loose-root"` (into `DirectInject.Execute`) and a folder another manager owns.
`ModInstallRegistry.Save` has exactly one call site, past both.

**Fix.** Build the per-archive claim map from the plan and the RESULT — `result.Added` and
`result.Updated` are precisely what succeeded — then write the manifest on the loose-root branch too.
`DirectInject.Execute`'s signature does not need to change, and the last-write-wins ordering survives
because the result is only available after every copy has settled.

**Why it is worth more than bookkeeping.** A loose-root row never shows the trash can:
`canUninstall: !directInject && !looseRoot && !rep.ReadOnly`, because *"we never delete loose files in
the game's exe folder."* That refusal is right — deleting is too dangerous when you cannot prove which
files are the mod's. **Provenance is that proof.** This is the precondition for ever offering uninstall
on these games, for tracked mods only, with hand-installed ones keeping the toggle and no delete.

**Tests.** A loose-root intake writes a manifest naming exactly the files it placed; a skipped file is
never claimed; a replaced file IS claimed (we wrote it); two archives in one drop produce two
manifests; the folder path keeps behaving exactly as it does now.

---

## A23 — stop inferring libraries in a folder the game owns · `M`

**Observed.** DS2 lists 15 mods. Nine are mods. `LocalCacheWinGame`, `steaminput`, `tools`, `uds` and
`_MODS_STAGING` are the game's, and `reshade-shaders` is ReShade's — already listed inside the ReShade
row. The status line reads `15 of 15 enabled`.

**Two sub-bugs, and the second is the interesting one.**

1. **Library inference runs in the game root.** `LibraryRowsFor` scans the primary mod location for
   unpaired directories and calls them libraries. That inference is sound when the folder is dedicated
   to mods — an unexplained folder in `~mods` really is probably a library. It is unsound when the mod
   location IS the game root, where an unexplained folder is simply the game.

2. **A lane's claim is matched by row NAME, not by the files the row owns.** `listed` is built from
   `alreadyListed.Select(m => m.Name)`, so the ReShade row — named `ReShade`, owning `reshade-shaders`
   — never suppresses the directory it holds. That is why the same folder appears twice.

**Fix.** Suppress library inference when the primary mod location resolves to the game root, and build
the already-listed set from every row's `Files` as well as its `Name`. In a folder the game owns, a
directory becomes a row only on evidence: a catalog hit, an install manifest (A25), or a listed mod
claiming it.

**What the live run taught, and what it means for severity.** Play vanilla left all five game folders
alone — but `reshade-shaders` DID move, correctly, because the ReShade row owns it. So the guard is
not `ReadOnly` alone, as a code read suggested: it is `ReadOnly` **plus nothing else claiming the
directory**. A directory belonging to two rows is protected only as strongly as its weakest claim.
Fixing sub-bug 2 removes the double claim, which removes the question.

**Tests.** A loose-root game lists no library rows for the game's own directories; a directory owned by
a listed row is never also its own row; a normal game with a dedicated mod folder still infers
libraries exactly as it does today (the `_CatLib` cases must pass unmodified); the counts follow.

---

## A24 — a name is not an identity · `S`

**Observed.** Toggling `version.dll` off warns: *"Version (ASI Loader)" is the loader other mods inject
through — disabling it disables every ASI plugin.* `FileVersionInfo` says `version.dll` is **DLSS
Enabler 4.5.2.2** and `dxgi.dll` is **ReShade 6.7.3.2148**. Neither is an ASI loader. Disabling the
first costs DLSS and frame generation; the three `.addon64` mods are ReShade addons and ride on the
second.

**Cause.** `LooseModScan` matches a filename against `ProxyNames` and appends `" (ASI loader)"`. The
dialog then states a consequence derived from that label rather than from the file.

**The app already knows better one surface over.** The discovery review dialog says of exactly this:
*"Several different loaders ship under this filename, so it can't be named from the file alone."* That
copy is careful. This one makes the opposite move from identical evidence.

**Fix, and the constraint that shapes it.** The row's `Name` is a KEY — the holding folder for the
disabled loader was `version-asi-loader`, derived from it — so **`Name` must not change** or existing
disabled state is orphaned. Put the identity in the display name, the description and the warning:

- Read `FileVersionInfo` where the scan already touches the file. Product plus version when present.
- Say what it is: *"version.dll — DLSS Enabler 4.5.2.2"*.
- Say what disabling costs, and only when we know: a known product gets its real consequence; an
  unidentified proxy falls back to the discovery dialog's honest line rather than to a confident
  sentence about ASI plugins.

**Keep the parts that were right.** The warning fires rather than moving a loader silently, Cancel is
the default with the destructive option secondary, and cancelling writes nothing. All three verified on
the rig. The mechanism is sound; only the sentence is wrong.

**Tests.** The naming decision is pure — product name in, label out — so it is tested directly:
identified product yields product-and-version; a blank or missing product falls back to the honest
line and never to the ASI-plugin claim; the mod KEY is unchanged by any of it, which is the test that
protects existing holding folders.

---

## Sequence

A25 first — it is the smallest and it is the evidence A23's "only on evidence" rule leans on. Then
A23. A24 is independent and can land any time.

## Done when

- A loose-root intake records what it placed, and the record names only files that are on disk.
- A loose-root game lists its mods and nothing else, and both counts agree with what a person counts.
- No dialog states a consequence the launcher cannot support, and every loader row says what it is
  where the file will say.
- Full suite green, `CorePurityTests` green.
- Verified on the rig the way the findings were found — drop a zip on Death Stranding 2 and look for
  the record, not the row.
