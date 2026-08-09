# Registration repair — the Core primitives

**Date:** 2026-08-09
**Backlog:** A2 (*No in-app way to repair a game registration*)
**Scope:** Core only. Ships no UI.
**Status:** approved design, ready for a plan

---

## Why

A registration can be wrong, and today there is no way out of it that does not risk the user's files.

`AddGameDialog` is add-only. A user whose stored `gameRoot`, `modLocations`, `fileExtensions`, or
`groupingRule` is wrong has exactly one route: remove the game and add it again. That route is not
safe. `Scanner.DataDirForGame` derives the data-dir path from `(Id, GameRoot)`, and `Id` is derived
from `GameName` — so a remove-and-re-add with a corrected folder, or even a corrected *name*, lands
the data dir somewhere new. What gets orphaned is not metadata:

- `disabled\` — disabled mod files, moved off the game. The only copy.
- `direct-disabled\`, `loose-disabled\` — the same, for the loose lanes.
- `frameworks\<id>\disabled-proxy\` — held proxy DLLs.
- `vortex-takeover\<key>\` — archived files taken over from another manager.
- `tools\<toolId>\` — installed third-party tool binaries.

Those become invisible to the launcher while still occupying disk under a stale `_626mods\<id>`. The
remove-confirm text already half-admits this ("Any disabled mods remain in the launcher's data
folder"), and `RestoreReconcile` already exists to detect precisely this id/gameRoot conflict class
for restore points. The hazard is known; there is simply no safe repair path.

**Live motivating case.** Elden Ring declares a Mod Engine 2 `mod` folder that does not exist on
disk, while all eleven of its mods load by direct-inject under `Game\`. `get_game_shape` reports it
as `Drifted` and correctly says the install is healthy — but there is no way for the user to act on
that report.

---

## Scope

A2 decomposes into four pieces. **This spec covers the first two, plus the planner that composes
them.** The UI is spec 2.

| | piece | layer | this spec |
|---|---|---|---|
| 1 | `userSet` marker + `RegistrationRefresh` integration | Core | yes |
| 2 | Data-dir mover | Core | yes |
| 3 | Change planner | Core | yes |
| 4 | Repair surface + full edit dialog | App | **no — spec 2** |

Decided as decomposition rather than one document because 1–3 are pure and fully testable while 4 is
WinUI and lands on smoke tests. Speccing them together would produce a plan whose middle could not be
reviewed before the code existed.

**Nothing in this spec changes behavior for a user who never opens an edit dialog.** Nothing writes
`userSet`. Nothing calls `Execute`. No dialog exists.

---

## 1. The `userSet` marker

### On disk

A new `GameEntry` field, camelCase per the repo rule:

```json
{
  "id": "cyberpunk-2077",
  "fileExtensions": ["archive"],
  "userSet": ["fileExtensions"]
}
```

A string list rather than per-field booleans: one field instead of one per column, and it extends to
`groupingRule`, `modLocations`, and `gameRoot` without a schema change each time. Field names come
from `const` declarations so a typo is a compile error rather than a silent no-op.

**Back-compat.** An absent key deserializes to null, meaning "not recorded." Every registration
written before today behaves exactly as it does now. There is no migration and no backfill — which is
the whole reason this is cheap to do now and was not cheap during A1.

### In Core

`RegistrationRefresh` keeps its shape and gains one parameter:

```csharp
public static IReadOnlyList<string> Extensions(
    IReadOnlyList<string> stored, IReadOnlyList<string> presetDefault,
    IReadOnlyList<string>? manifest, bool userSet = false)
    => userSet ? stored
     : manifest is { Count: > 0 } && IsUntouched(stored, presetDefault) ? manifest
     : stored;
```

`Grouping` takes the same parameter on the same terms. Both stay pure — they take primitives, never a
`GameEntry` — so they remain trivially testable. The `false` default means every existing call site
and all ten existing `RegistrationRefreshTests` pass unchanged.

`Scanner.GameContext` passes `game.UserSet?.Contains(GameEntry.UserSetFileExtensions) == true`.

### Precedence

`userSet` is checked **first** and wins outright. It is the only signal in the system that is not an
inference. The untouched-preset-default heuristic stays underneath as the fallback for registrations
that predate the marker, and it becomes *less* load-bearing over time rather than more.

### Recorded for every edited field; consulted for two

`RegistrationRefresh` covers `fileExtensions` and `groupingRule` — those are the only two fields
anything self-heals today. But the marker records **every** field the user edits, including ones
nothing currently reads.

That is deliberate, not an oversight. The manifest already carries `modPath`, `nexusDomain`,
`curseforgeGameId`, and `saveDirHint` (`GameManifestEntry`), so the set of self-healing fields is
expected to grow. Recording intent at the moment the user expresses it costs nothing; reconstructing
it later is impossible. A `userSet` entry for a field no rule consults is inert — it is a fact
waiting for a reader, not dead weight.

The corollary is a rule for whoever adds the next self-healing field: **check `userSet` before
trusting the heuristic**, because the marker may already know the answer.

### What this fixes beyond the dialog

A1 shipped with one documented blind spot: a user who deliberately picks a value that happens to
equal the preset default is silently overridden by the manifest. The marker closes it, because the
edit path is the only place that can distinguish deliberate choice from frozen default — and there,
it knows for certain.

---

## 2. The data-dir mover

Follows the repo's `validate-then-extract` law: enumerate, validate, plan, and only then write.

### Split

- **`DataDirMove.Plan(from, to)`** — pure inspection. Walks the source, returns file count, total
  bytes, and a refusal reason if there is one. Writes nothing, so the UI may call it to populate a
  prompt with a real path and a real size.
- **`DataDirMove.Execute(plan)`** — the only thing that touches disk.

### Refusals, all decided in `Plan`

| condition | outcome |
|---|---|
| source does not exist | no-op success — nothing to move |
| target exists and is non-empty | **refuse** — same stance as the legacy `MigrateDataDir`; never merge two data dirs |
| free space on the target volume < source bytes | **refuse** before a single byte is copied |
| source and target resolve to the same path | no-op success |

### Two execution paths

- **Same volume, target absent** → `Directory.Move`. An atomic rename: instant, and there is no
  window in which the data exists in neither place.
- **Cross-volume** → copy into a staging folder beside the target, verify, rename staging into place,
  *then* delete the source.

### The ordering is the safety

The source is never deleted until the target is verified in place. If the copy throws midway, the
staging folder is removed and the source is untouched — the user is exactly where they started.

A failure to delete the source at the end is deliberately **non-fatal**: it leaves a harmless
duplicate rather than putting the surviving copy at risk in order to tidy up.

### What "verify" means

Same set of relative paths, same byte length for each file.

That catches the failures that actually occur: a truncated copy, a file that did not make it, a disk
that filled. It does **not** hash contents. Hashing gigabytes of disabled mods would add minutes to
every move to catch a class of silent corruption that the rename path does not have at all. This
limit is stated in the doc comment rather than implied away — the code must not suggest a guarantee
it does not provide.

### What it deliberately is not

Not a general file-mover. Not reachable from intake. Not exposed to the MCP. One caller, one purpose.

---

## 3. The change planner

`RegistrationChange.Plan(GameEntry stored, GameEntry proposed) → RegistrationChangePlan`

Reads the filesystem — it checks that a proposed game folder exists, and delegates to
`DataDirMove.Plan` to size the move — and writes nothing. Planning must never change an install:
someone who reads the consequences and clicks Cancel ends up exactly where they started.

**As built, `FieldsToPin` can be shorter than `FieldsChanged`.** On an engine change, a changed field
whose proposed value equals the NEW preset's own default is the preset speaking, not the user, and is
deliberately not pinned — otherwise picking an engine from a dropdown would silently opt the game out
of every future manifest correction, which is the failure this whole feature exists to prevent. The
two lists answer different questions: what changed, and what gets locked in.

It answers one question: *if
this edit is saved, what actually happens?*

```text
fieldsChanged     [gameRoot, modLocations]
fieldsToPin       [modLocations]          -> written to userSet on save
dataDir           required: true
                  from  G:\_626mods\elden-ring   (2.1 GB, 1,204 files)
                  to    D:\_626mods\elden-ring
                  refusal: none
blockers          (none)
```

The UI renders this plan and never computes consequences itself. The move-or-pin prompt has to name a
real folder, a real byte count, and a real refusal reason — that is a decision, not a rendering
detail, and this repo has repeatedly learned that decisions parked in `MainViewModel` (14 concrete
service deps, unconstructible in tests) accumulate defects until someone extracts them.

### The identity rule

**`Id` is immutable across an edit.**

`Id` is derived from `GameName` via `Slugify` at add time, and it is half of the data-dir key. If
renaming a game re-slugged its id, every disabled mod, profile, and installed tool would orphan —
silently, from a cosmetic rename. The planner never proposes an id change; renaming `GameName` leaves
`Id` alone.

This is the most dangerous thing an edit surface could do and the cheapest to prevent.

### The non-obvious consequence it surfaces

Changing the **engine** changes which preset defaults apply. A field that reads as "untouched
default" under `fromsoft` may read as customised under `ue-pak`, so an engine change can silently
alter whether future manifest corrections reach that game. The planner reports this; it does not
decide for the user.

---

## Testing

All three pieces are pure Core, so all of it is covered for real. No smoke tests in this spec.

**`userSet`**

- Round-trips as camelCase, with the string-contains assertion the repo rule requires.
- An absent key behaves exactly as today (the back-compat guarantee).
- A listed field wins over the heuristic; an unlisted field still self-heals.
- The existing ten `RegistrationRefresh` tests pass unchanged.

**Mover**

- Happy path, same volume.
- Happy path, cross-volume.
- Refuses on a non-empty target.
- Refuses on insufficient free space.
- No-ops on a missing source.
- **Failure mid-copy leaves the source byte-identical and the target absent.** This is the
  reversibility test, and it is the reason `Execute` is shaped the way it is.

**Planner**

- A rename leaves `Id` and the data dir alone.
- A `gameRoot` change produces a move plan with real numbers.
- A refusal from `DataDirMove.Plan` surfaces as a blocker rather than being swallowed.
- An unchanged field pins nothing.

---

## Laws this design is bound by

- **Reversibility.** No delete before a verified copy. Rollback on any mid-flight failure. A tidy-up
  failure never risks the surviving copy.
- **Pure core.** All three pieces live in `src/ModManager.Core/`. No WinUI, no WinRT.
- **camelCase JSON on disk**, with a string-contains round-trip test on the new field.
- **validate-then-extract.** Enumerate, validate, plan, then write — `Plan` cannot write, `Execute`
  cannot decide.

---

## Open questions

None. The three that arose during design were resolved:

- *Marker shape* — string list over per-field booleans, for extension without schema churn.
- *Data-dir on a folder change* — offer to move, pin as the fallback (`GameEntry.DataDir` already
  exists and is honored by `DataDirForGame`; nothing has ever written it).
- *Scope* — decomposed; UI is spec 2.
