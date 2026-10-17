# Rule: every interactive control is addressable by an agent

## The convention

Two rules, because a control that appears once and a control that appears per row need opposite
treatment.

### 1. Singleton controls get a static `AutomationProperties.AutomationId`

Anything that appears once on a surface — a toolbar button, a search box, a section expander:

```xml
<TextBox AutomationProperties.AutomationId="LibrarySearchBox"
         AutomationProperties.Name="Search games" ... />
```

Id format is `<Surface><Thing>` in PascalCase: `LibrarySearchBox`, `LibraryDiscoveryExpander`,
`LibraryUpdatesEntry`. The surface prefix is what keeps ids unique once the harness walks more than
one view.

### 2. Templated controls get a BOUND id from a stable key, plus a per-row name

A static id inside a `DataTemplate` repeats on every row, so thirty rows share one id and the harness
picks whichever the tree walker reached first. Bind instead, off a key that is **not display copy**:

```xml
<!-- in the row template -->
<Button AutomationProperties.AutomationId="{x:Bind RowAutomationId}"
        AutomationProperties.Name="{x:Bind Name}" ... />

<!-- the row's action buttons -->
<Button Content="Play" AutomationProperties.Name="{x:Bind PlayAutomationName}" ... />
```

backed by view-model properties following the established `*AutomationName` convention that
`ModRowViewModel` already uses:

```csharp
public string RowAutomationId  => $"GameRow.{Id}";
public string PlayAutomationName => $"Play {Name}";
```

**Prefix the same entity apart when it appears in two lists.** A game shows up in both the recent
strip and the all-games list. `GameRow.windrose` and `RecentCard.windrose` are distinct; a bare
`windrose` in both is a collision that surfaces as a flaky test months later.

## Why not just use the display text

Because it works right up until it doesn't. Before this rule the app had **zero**
`AutomationProperties.AutomationId` and every control was reachable only by its rendered label —
`Name='Enable all'`, `Name='+ Game'`. That fails in both directions:

- **Rename a button → a green suite goes red** for no behavioural reason. This repo's own voice
  guidance treats microcopy as something to keep improving, so renames are expected, not rare.
- **Move a button → the suite stays green.** Worse than the first, because nothing tells you.

The concrete failure that motivated the rule: searching the live tree for `Play` returned a **`Text`
element**, not the button. Eleven Play buttons existed and every one of them rendered the identical
string, so a harness would have asserted against a label it could never click. After the rule they
are `Play Windrose`, `Play ELDEN RING`, … each individually invokable.

## Why not rely on `x:Name`

WinUI promotes `x:Name` to `AutomationId` only partially. Measured on this app: **720 `x:Name`
attributes in the XAML, 8 reaching the live UIA tree.** It is not a mechanism you can plan a harness
around. Set the id explicitly. Where a control already has an `x:Name`, match the id to it rather
than inventing a second name for one thing.

## `AutomationProperties.Name` is not the same job

The app already had 92 `AutomationProperties.Name` entries. Those are **accessibility labels** — real
work, correct, and what a screen reader announces. They are not automation identity, and counting
them as coverage is the mistake that made this look done when it had not started. Both belong: `Name`
says what a human hears, `AutomationId` says what a harness asks for.

## Verifying it landed

Explicit ids do surface, unlike `x:Name`. After applying the rule to one view the app went from 8
app-authored ids to 31. Check with a UIA walk rather than trusting the XAML:

```powershell
Add-Type -AssemblyName UIAutomationClient
$p = Get-Process ModManager.App | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
# walk ControlViewWalker, print .Current.AutomationId
```

## Virtualization: absent is not not-there, and UIA has the answer

Collapsed and offscreen content is genuinely missing from the tree. `LibraryDiscoveryExpander`
reports `IsOffscreen=True` and has **one** child while collapsed. A harness that reads that as "the
discovery lane is empty" is wrong, and it is wrong in the direction that passes.

**This does not need an app change.** Do not disable virtualization to make tests easier — that is
changing what ships to serve the harness, and it costs real memory on a large library. WinUI 3
implements the UIA patterns built for this. Measured on this app:

| Situation | Use | Verified |
|---|---|---|
| Closed dropdown, want the current selection | `SelectionPattern.GetSelection()` | Returns `By source` **without opening it** |
| Want a specific virtualized row | `ItemContainerPattern.FindItemByProperty()` | Supported on `ModListView` and the ComboBox |
| Want to materialise an offscreen row | `VirtualizedItemPattern.Realize()` | **27** elements expose it — the full mod list, offscreen included |
| Collapsed container | `ExpandCollapsePattern.Expand()` then `.Collapse()` | 1 child → 15, 14 `+ Add` rows reachable, state restored |

The order matters. **Reach for reading before driving:** a closed ComboBox already answers "what is
selected", which is what most verification actually asks. Only enumerating every *option* needs the
popup open. Where you do have to drive, restore the state afterwards — `Expand()` then `Collapse()`
leaves the app as the user left it, and a harness that quietly reorganises the UI it just verified is
its own bug.

### Never read list ORDER off the tree either

The absence trap above has a sibling that is worse, because it produces a green run. Walking
`Get-Tree` for a virtualized list returns whatever window happened to be realised — **not** the list
from the top. On the 116-entry game picker a walk returned 105 items whose first element was list
index **38**. A harness comparing "the first option" against an expected ranking was comparing
against the middle of the list, and it reported the ranking broken when the ranking was correct.

Ask the item SOURCE, not the render. `scripts/uia-lib.ps1` carries the three that matter:

| Question | Use |
|---|---|
| The first N items, in true order | `Get-ItemsInOrder` |
| How many items are actually there | `Get-ItemCount` |
| One specific item, realised or not | `Find-ItemByName` |

All three sit on `ItemContainerPattern.FindItemByProperty`, which walks the bound collection and is
immune to what is on screen. **A count or an order from `Get-Tree` is a measurement of the viewport.**

Two traps sit directly behind them, and both cost a debugging round when this was written:

- **An unrealised container's `Name` is its bound object's `ToString()`, not the `DisplayMemberPath`
  text.** The picker's offscreen rows came back as the whole
  `PopularGame { Id = minecraft, Name = Minecraft, Engine = ... }` record, so matching a display
  string silently missed exactly the offscreen items you reached for `ItemContainerPattern` to get.
  `Get-ItemLabel` pulls the display name out of either shape.
- **`Select()` on an item of a CLOSED combo throws a bare "Unrecognized error."** Reading the source
  works shut; selecting does not. Open the popup, then `Select-Realized` (which realizes first —
  `Select()` on an unrealised container throws the same unhelpful string).

Reading beats driving, still: `Get-Selection` answers "what is chosen" on a shut combo, and that is
what most verification actually asks.

Rule of thumb: if an assertion is about absence, prove the container was realised first. `IsOffscreen`
plus a child count is the cheap check.

### Two ways this bites, both observed

**Never read a realised count as a total.** Counting `GameRow.*` in the tree and comparing to an
expected number is wrong whenever the list is longer than the viewport. A freshly-added game sorted
below the fold is simply not in the tree, and the harness reports it missing. Ask about the specific
row (`Test-RowPresent`, which falls back to `ItemContainerPattern`), or get the total from the source
of truth — the registry via MCP — never from the rendered tree. `Get-RealisedCount` is named the way
it is so the call site cannot pretend otherwise.

**Check for ANY modal, not a specific one.** Detecting "is the add-game dialog open" by looking for
`GameNameBox` answers a different question than "is the surface behind this assertable". A harness
reported *"no dialog - added directly"* with an adoption prompt sitting open, because it only knew
how to recognise one form. Use `Test-ModalOpen` / `Assert-NoModal`.

Both of these produced a **confident wrong answer rather than an error**, which is the failure mode
that matters here: a harness that throws gets fixed, a harness that lies gets believed. Together they
manufactured a repaint bug that did not exist — the add had completed, the prompt was awaiting a
human, and the new row was below the fold.

## The build trap

Editing XAML while the app runs leaves stale generated code and the app then dies at `Connect()` with
an `InvalidCastException`. Stop the app and delete `obj/` before building after any XAML edit.

## Surfaces converted so far

- `LibraryView.xaml` — the reference implementation. Empty state, recent cards, updates entry, search
  box, game rows, Play buttons, discovery expander, discovery rows, `+ Add` buttons.
- `MainWindow.xaml` — title-bar chrome, game picker, the options flyout, the whole mod toolbar,
  loadout segments, group/theme pickers, the Vortex and setup banners, the mod list, and per-row
  identity on all 27 mod rows (`ModRow.<on-disk name>`).

- `AddGameDialog.xaml` — the Steam picker, the manual fields, the AI-profile and batch panes.
- `SettingsDialog.xaml` — identity, backdrop, Nexus account, plugin controls, reset, plus bound names
  on the tool / framework / direct-inject-config / restore-point rows.
- `SavesDialog.xaml`, `GameSetupDialog.xaml`, `NexusCatalogView.xaml`, `CharacterEditDialog.xaml`,
  `ProfilesDialog.xaml`.

- The small dialogs — `SafeClearDialog`, `NexusModDetailDialog`, `NexusCatalogDialog`,
  `NewThemeDialog`, `UpdateModsDialog`, `ManualMatchDialog`, `UpdatesView`, plus bound approve-names
  on `IdentifyReviewDialog` and `DiscoveryReviewDialog`.
- `Tools/ToolsPanel.xaml` — tool chips, get-links, detected loaders, framework chips.
- `SavesDialog.xaml` save-mod rows: `SaveModRow.<guid>` on the title `TextBlock`, a `Name` on the row, and bound
  `Reset <world>` / `Remove <world>` button names off the App-side `SaveModRow`. The list item no longer reads as
  the record's `ToString()` (which carried the kept-zip path).

**141 `AutomationProperties.AutomationId` in the XAML, from zero. The sweep is complete.**

Added since, with Patch Day:

- State-strip detail row (`MainWindow.xaml`): singletons `PatchDaySecondaryAction` and `PatchDayTertiaryAction`
  beside `StateChipAction`, each visible only when the chip has that action.
- `PatchDayDialog.xaml`: `PatchDayDialog` on the sentence `TextBlock` (not a panel), then `PatchDayPlayVanilla`,
  `PatchDayPlayModded`, `PatchDaySnooze` and `PatchDayNotNow` on real content buttons. The dialog's built-in
  buttons are unused because this repo gives them no ids.
- Library row (`LibraryView.xaml`): bound `GameRowPatchDay.<id>` on the Patch Day line's `TextBlock`, from
  `LibraryViewModel.PatchDayAutomationId`.
- The interrupted-restore notice (built in `MainWindow.xaml.cs`): `InterruptedRestoreDialog`,
  `InterruptedRestoreMessage` on the message `TextBlock`, and `InterruptedRestoreAcknowledgeButton`, an
  in-content button for the same reason as the Patch Day dialog.

None of these has been walked on a live tree yet. Verify with a UIA walk before a harness relies on them.

## An id on a `Border` does nothing

Found the hard way giving the ban-risk warning an id before wave 7 moved it. The id went on the
`Border` that wrapped it, the app built and looked correct, and a UIA walk came back with **no such
element**. `Border` is not a control-view element, so it never reaches the tree an agent walks — the
same class of trap as `x:Name`, and quieter, because you wrote the id yourself and it is right there
in the XAML.

Put the id on something that is actually a control. Here it moved one level in, onto the `TextBlock`
(a `Text` element), and surfaced immediately. **Verify with a walk, never by reading the markup.**

## A `Grid` is not a control either, and neither is a row

The `Border` note above has a sibling that cost a full debugging round on the leftover-folders rows.
**The precise rule, measured in one session on two Grids:** a layout panel with an `AutomationId`
and **no** `AutomationProperties.Name` never reaches the control view. Give it a `Name` as well and
it is promoted and both surface. `UpdatesView.xaml`'s row Grid carries both and a walk finds every
`UpdateRow.*`; the leftovers Grid carried the id alone and a walk found none. So the safe habit is
still *put the id on a control* — but if a panel id must stay, it needs a Name beside it.

The row template bound `AutomationProperties.AutomationId` to its **row `Grid`**. It compiled, it
looked right, and a UIA walk found every row's *buttons* and **not one row id** — a `Grid` is a
layout panel, so it never reaches the control view. Moving the id onto the row's name `TextBlock`
surfaced all seven immediately.

Two consequences follow, and the second is the one that bites twice:

- **Put a row id on the row's LABEL**, not on its panel. `StackPanel`, `Grid` and `Border` are
  all invisible here.
- **An id-bearing element is then a label, not a container.** A harness that walks *down* from the id
  to find the row's buttons finds nothing, because they are its siblings. Search the whole section for
  the bound `*AutomationName`s instead. The first cut of `smoke-leftovers.ps1` did exactly this and
  reported every row "missing" all three of its actions.

`RestorePointRow` still declares a `RowAutomationId` that reaches nothing, for the same reason.

## Do not deduplicate what you are counting

Same harness, same run. It gathered row detail strings into a `HashSet` and asserted seven. Five of
the seven folders hold one file of the same size, so their detail lines are byte-identical and
collapsed to one entry — it reported *"only 3 rows state a file count"* about a section where all
seven did. Assert the SHAPE of each distinct string, or count something that is actually unique.
## Id the fact, not the widget

Same episode, the general lesson. The chip in the game-state strip is `StateChip.ban-risk`, and that
id was on the *old inline warning* one commit before the strip existed. So the assertion proving the
highest-consequence line in the app is reachable was green **before** the move and stayed green
**after** it — the layout change was verified at the one place that mattered.

Had the id arrived with the move, the harness would have been asserting a brand-new control against a
brand-new expectation: green, and worth nothing.

Ids are `StateChip.<condition>`, bound off the pure `GameStateChip.Id` that Core hands over — a
stable kebab-case key, never the label.

**The detail row's controls are singletons: `StateChipDetail`, `StateChipAction`, `StateChipDismiss`,
with no condition suffix.** Only one chip is expanded at a time, so there is exactly one of each in
the tree and a suffix would be a lie. This paragraph used to claim `StateChipAction.<condition>`, and
a harness written from it looked for an id that has never existed — the failure took a tree walk to
see, because the markup it was checked against was this file rather than the app.
Two of these already outlived a copy change during the wave itself.

## Restore the fixture in a `finally`

A smoke case that navigates somewhere of its own choosing must put the app back **including when it
fails**. The first run of `ban-risk-chip-addressable` threw its assertion before reaching the tidy-up,
left the app parked on a game with no mods, and **five later cases went red for a fixture reason** —
reading exactly like five new bugs next to the one real one. Tidying only on success hides the mess
precisely when there is one.

### One thing ToolsPanel settled

Its rows bind Core types directly (`core:ToolEntry`, `core:KnownTool`). A `*AutomationName` property
on those would have compiled and would not have tripped `CorePurityTests` — it is only a string. Do
not do it. Presentation naming does not belong in the pure layer, and "the purity test still passes"
is not the same as "this belongs here". Bind the `DisplayName` those records already carry, or wrap
them in an App-side view-model.

`ToolConfigureDialog`, `IniEditorDialog` and `FrameworkInstallDialog` have no interactive controls of
their own beyond the ContentDialog buttons, so there was nothing to id.

## New controls

Any new interactive control ships with its id in the same commit. A control added without one is a
control the smoke harness cannot reach, and it will be found by someone writing a test months later
with no idea what the control was supposed to be called.


