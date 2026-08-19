# Wave 10 — one word, one thing

**Date:** 2026-08-18 · **Items:** the round table's #7 and #8 · **Size:** two small ones, one wave

## Item 7 — the vocabulary

The round table's rule was written about controls: *if two controls do the same job, one of them is
wrong.* Item 7 extends it to nouns. The inventory came out worse than the memo said.

### "Profile" named THREE different things

| Where | What it meant |
|---|---|
| `ProfilesDialog` | a saved set of enabled mods — the meaning every other mod manager uses |
| `AddGameDialog` — *"Apply profile"*, *"Profiles that pass register on Add"* | an agent-authored **game definition** |
| Core `GameProfile` / `GameProfiles` | an engine's declared **save types** |

The first two are user-visible **one click apart**. The third is invisible from the UI, which is worse,
not better — nobody trips over it until they are reading two files at once and one of them is lying.

The launcher already had the right word for the second: the **game-definition** layer under
`Manifest/`, the signed feed, `CLAUDE.md`'s own wording. So:

- `GameProfileDraft` → `GameDefinitionDraft`, `GameProfileImport` → `GameDefinitionImport`,
  `GameProfilePrompt` → `GameDefinitionPrompt`, `ProfileImportResult` → `GameDefinitionImportResult`,
  `Services/GameProfileResolver` → `GameDefinitionResolver`. *"Apply profile"* → *"Apply definition"*.
- `GameProfile` → `GameSaveTypes`, `GameProfiles` → `GameSaveTypesCatalog`.
- **Profile** now means exactly one thing: a saved set of enabled mods.

### "Loadout" named a thing that stopped existing

A `LOADOUT` toolbar heading, a `Profiles` tooltip reading *"Saved loadouts"*, a `ProfileList` whose
screen-reader name was *"Saved loadouts"*, and a ProfilesDialog using five labels for two words.

And the heading had been wrong since **wave 6**, which made those three segments a **filter** — they
change what is listed and move no files. A heading reading `LOADOUT` over a view control tells exactly
the lie the control itself used to tell.

### `LIBRARY` named two things one click apart

Your games on the home, and four per-game actions in the game view.

### The result

| Was | Is | Why |
|---|---|---|
| `LOADOUT` (All / MP / SP) | **`SHOW`** | it decides which rows are listed |
| `LIBRARY` (game view) | **`MANAGE`** | four per-game actions; the library is your games |
| `VIEW` (group-by) | **`GROUP BY`** | it would have become a synonym for SHOW the moment SHOW arrived |

## Item 8 — the accelerators

Three in the whole app (Ctrl+F, Ctrl+R, Esc) plus Space on a focused row, and none of them reached
what a keyboard-first user presses most.

| Key | Does |
|---|---|
| **Ctrl+,** | Settings — the platform convention |
| **Ctrl+O** | Add mods |
| **Ctrl+P** | Profiles |
| **Ctrl+1 / 2 / 3** | Show All / MP / SP |

**Ctrl+1/2/3 is only safe to bind now.** Until wave 6 those segments enabled and disabled every mod in
the game. A number key that did that silently would have been the worst control in the app.

**Ctrl+, is wired in the constructor, not the XAML.** The comma key is `VK_OEM_COMMA` (188), the
`VirtualKey` enum has no named member for it, and the XAML compiler will not take the number. It is
also the only accelerator here **not** gated on a live game view — Settings is reachable from the home.

**Every new shortcut is named in its control's own tooltip.** The app sets
`KeyboardAcceleratorPlacementMode="Hidden"`, so if the key is not in the tooltip it is said nowhere,
and an accelerator nobody can discover is a shortcut for the person who wrote it.

## Why a test, not just an edit

`VocabularyTests` reads the XAML and Core and holds the decisions. An edit fixes this once; copy gets
improved constantly here — that is the *stated* reason automation ids are not keyed on labels — so a
retired word comes back in six weeks unless something says no.

**What it deliberately does not police:** `AutomationProperties.AutomationId` and `x:Name`, exempt
*together*, because the automation-ids rule says an id should match its `x:Name` rather than invent a
second name for one thing. `LoadoutAllSegment` keeps its name on purpose, beside a binding that now
reads `ShowAllBrush`. Identity outlives copy; renaming ids to chase a copy change is the exact mistake
that rule exists to prevent. Comments are exempt too — two files say *"was GameProfile until wave 10"*
deliberately, because a rename whose reason is not written down gets undone by the next person who
finds the old name in a git log and assumes it read better.

**It earned its keep immediately:** it caught `AutomationProperties.Name="Saved loadouts"` on the
profile list — a screen-reader label the eye slides past, which is precisely the copy a manual sweep
misses.

## Done

- One word per thing, across UI copy, Core types and view-model properties.
- Seven accelerators, each named where its control is.
- 2210 tests green (18 new), harness **32/32**.
- Verified on the rig: the toolbar reads **MODS · SHOW · MANAGE · GROUP BY · THEME**.
