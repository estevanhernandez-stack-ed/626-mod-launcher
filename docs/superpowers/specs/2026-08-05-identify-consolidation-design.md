# Identify consolidation — one action instead of six

> Design, 2026-08-05. Brainstormed with Este during the `feat/discovery-sweep` live smoke, after a
> newly-added menu item turned out to be the sixth button meaning roughly "figure out what my mods
> are." The trigger was UX, but the root cause is architectural: the menu exposes our *evidence
> sources* as separate user-facing verbs.

## The problem, measured

Six actions in the game's More menu and toolbar. Four of them are the same user intent, separated
only by which lookup the launcher happens to use:

| Label | What it actually does | Evidence |
|---|---|---|
| Backfill metadata from Nexus archives… | user picks a downloads folder; md5-match archives | Nexus md5 |
| Identify loose mods on Nexus… | name-search unidentified rows | Nexus name search |
| Get details from Nexus… | fetch by mod id for identified rows missing description/art | Nexus by id |
| Fetch metadata for all mods | CurseForge name search + Vortex manifest read | CurseForge |

Two further collisions:

- **"Find mods"** (toolbar → opens the mod site to get NEW mods) vs **"Find existing mods"** (menu →
  inventories what is already on disk). Near-identical labels, opposite meanings.
- **"Refresh"** (toolbar → reloads the mod list) vs **"Re-scan mods & launchers"** (menu →
  re-detects launchers/frameworks, then reloads). Genuinely different; labels don't say so.

There are only three real intents in this surface: *get me new mods*, *what is on my disk*, and
*what are these things*. The third has four buttons.

**The principle being violated:** which evidence tier resolves a mod is an implementation detail.
The user cannot be expected to know that an extracted `.archive` can never md5-match, that a
name-search hit carries no description, or that CurseForge is irrelevant to a Cyberpunk library.
Asking them to pick a lookup is asking them to debug our internals.

## Decisions

| Question | Decision |
|---|---|
| Consolidate or relabel? | **One primary action + an Advanced submenu** keeping the individual passes |
| Downloads folder (needs input) | **Offer once up front**, then run uninterrupted |
| Review | **One dialog** at the end, covering the whole run |
| Discovery sweep | **Folded into the same action** — kills the "Find mods" collision by removing one side |
| Primary action name | **"Identify my mods…"** |

## The menu, after

```
Identify my mods…                    <- the whole ladder, one review
Re-detect launchers & frameworks     <- renamed: it is about launchers, not mods
Advanced >
    Match against my downloads folder…
    Refresh details from Nexus
    Check CurseForge
Remove this game…
```

The toolbar is untouched. "Find mods" keeps its name because it now unambiguously means *acquire
new mods* — the item it collided with no longer exists as a separate entry.

Advanced exists for the case where a user wants exactly one pass (re-running only the downloads
match after adding downloads, say) without paying for the full ladder. It is not a fallback for the
primary failing; it is the same code paths, individually addressable.

## The run

One prompt, before anything starts:

> **Also check a downloads folder?** Archives give exact matches.
> `[Choose folder]` `[Skip]`

Then four passes, uninterrupted, best evidence first:

1. **Sweep** the game folder for files not represented in the mod list (`DiscoverySweep`).
2. **md5** any archives found by the sweep, plus every archive in the chosen downloads folder.
   Exact and authoritative — this is why the folder is worth asking for.
3. **Fill blanks by mod id** for rows already identified but missing description or cover art
   (`NexusRefresh.SelectEnrichmentCandidates`).
4. **Name search** everything still unnamed — both swept candidates and existing unidentified rows.

Live progress line and a Stop button throughout, reusing the machinery already built for the
identify sweep (`IsCancellable`, `CancelLongOperation`, `IProgress<T>` constructed on the UI
thread). Stopping keeps completed work; nothing in passes 1–4 writes on its own.

Pass ordering is not cosmetic — it is precedence. A row resolved by md5 in pass 2 must not be
re-proposed by name search in pass 4, mirroring the existing `IsAlreadyIdentified` discipline.

## The review, and one deliberate asymmetry

One dialog, two sections, **identity claims only**:

```
NEW TO YOUR LIST (4)
  [x] Equipment-EX              exact match (file hash)
  [x] ArchiveXL                 by name

NOW IDENTIFIED (43)
  [x] #CorpoCat      ->  Corpo Cat          by name
  [x] #GoneAway      ->  Gone Away          by name

  [Apply 47]  [Cancel]
```

**Pass 3 does not appear in this dialog.** Filling in a description for a row whose Nexus mod id we
already hold is not a claim about identity — it is retrieving detail about an identity already
established (and, for md5-identified rows, established more strongly than the user could by hand).
Listing 98 rows of "we added a description" would bury the four rows that genuinely need judgment.
It runs unconditionally and is reported in the summary line.

The rule, stated once so it survives future additions to this surface:

> **Approve identity. Do not approve detail.**

Anything that asserts *which mod this is* goes in the dialog. Anything that enriches a mod we have
already correctly named does not.

## Components

Reuses existing pieces; the new work is orchestration and one merged dialog.

| Piece | Change |
|---|---|
| `MainViewModel.IdentifyMyModsAsync(string? downloadsFolder)` | NEW — sequences passes 1–4, owns progress + cancellation |
| `DiscoverySweep`, `LooseIdentify`, `NexusRefresh` | unchanged; called in sequence |
| `DiscoveryReviewDialog` + `LooseIdentifyDialog` | merged into one two-section dialog |
| `MainWindow.xaml` More menu | six items to one + Advanced submenu |
| `DiscoverExistingModsAsync`, `ProposeLooseIdentifyAsync`, `EnrichMetadataAsync`, `FetchMetadata`, `OnNexusBackfill` | retained as the Advanced entry points; the primary composes them |

The merged dialog takes **two typed lists** (`IReadOnlyList<AdoptionProposal>` and
`IReadOnlyList<LooseIdentifyProposal>`) rather than a new unified proposal type. Both existing apply
paths stay exactly as they are — this is a presentation merge, not a data-model merge, which keeps
the write paths (and their tests) untouched.

## Invariants preserved

- Metadata-only writes. No file is moved, renamed, or deleted by any pass. The first file move is
  still the user's first toggle.
- The classifier never claims a game file (`PakClassifier.IsBaseGamePak`, the forbidden-paths gate).
- Every identity write is review-gated.
- Already-identified rows are never downgraded by a weaker evidence tier.
- Cancellation returns completed work rather than discarding it.

## Non-goals

- No change to the toolbar beyond leaving it alone.
- No new evidence source. This reorganizes access to what already exists.
- Not addressing the `.archive.xl` sidecar problem (backlog A3) or stale registrations (A1) — both
  independent, both still open.

## Availability: guard, don't hide

Advanced items are **not** gated on transient state. They are always visible, and their existing
precondition guards do the talking.

The reasoning, since the opposite is the tempting default:

- **The guards already exist and say more than absence does.** Every one of these paths opens with
  checks that write a specific, actionable line — `"Connect Nexus first (toolbar -> Nexus)."`,
  `"This game has no Nexus domain set."`. An item that *vanishes* because the user is signed out
  teaches them nothing; they just cannot find something they remember. An item that is there and
  explains itself on click names the next step.
- **Gating duplicates the precondition into a second place that can drift** — the visibility
  property and the guard must agree forever, and nothing enforces it.
- **Its failure mode is silence.** This surface has already produced exactly that bug once: a
  computed visibility property with no change notification evaluated `Collapsed` at startup and
  never re-evaluated, so a shipped, working menu item was invisible in a clean build.

The line worth drawing is transient vs permanent:

| Condition | Treatment | Why |
|---|---|---|
| Signed out, no domain set, no rows yet | **Show**, guard explains | Temporary — it will work shortly, and the guard names the fix |
| This game has no presence in that catalog at all | **Hide** | Permanent property of the game; the item can never do anything |

Sign-in state comes and goes; a game's catalog presence does not.

**Consequence for the plan:** `OnNexusBackfill` currently has *no* precondition guard — it opens
the folder picker immediately, so a signed-out user can browse to a downloads folder and only then
discover nothing can be matched. That item needs a guard added, checked before the picker opens.
Any visibility binding this work does introduce ships with its `OnPropertyChanged` sites and a test
that would fail if they were dropped.
