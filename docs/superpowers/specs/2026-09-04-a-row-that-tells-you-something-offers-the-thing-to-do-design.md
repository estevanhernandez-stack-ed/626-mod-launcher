# A row that tells you something offers the thing to do about it

**Date:** 2026-09-04
**Status:** approved in outline (three product questions answered), pending spec review
**Ships in:** 0.22.0, alongside the Store submission that covers 0.21.0 and 0.22.0 together

## The through-line

Two surfaces name a fact and then offer nothing to do about it.

The updates list says *Faster Ships, 1.2 to 1.4* and cannot be clicked. The holding folder for a game
you removed sits on disk with your files in it and appears nowhere in the app at all. In both cases
the launcher knows enough to help and stops one step short, and in both cases the user's next move is
outside the app: a browser search, or File Explorer.

They ship together because they are the same defect twice, and because the Store features list has
exactly 20 of 20 slots used, so the next submission has to be worth displacing a line for.

---

## Part 1 — the updates list opens the mod page

### What it does today

`UpdatesView.xaml` renders each `UpdateRow` as a `Grid` of three `TextBlock`s: the mod name, the file
list for a grouped multi-file mod, and the version text. There is no button, no hyperlink, no
`Tapped` handler. The row is a label.

The information to act on is already in hand. `PendingUpdate` (`src/ModManager.Core/ModUpdateSummary.cs`)
carries `NexusModId` and `NexusDomain`, and the URL shape is already written down once, at
`SaveBundle.cs:334`:

```csharp
return $"https://www.nexusmods.com/{nexusDomain}/mods/{id}";
```

### What it does after

Each row gains a **Get update** button that opens the mod's Nexus page in the default browser.

**The mod page, not the Files tab.** Both were considered. The Files tab (`?tab=files`) is materially
better for a multi-option mod, which is the grouped-row case the updates list already goes out of its
way to name: "Faster Ships" stands for four separate downloads, and the overview page will not tell
you which one Nexus means. The mod page won anyway, because it is where the Get button in the in-app
catalogue already goes, and two doors to Nexus that land in different places cost more than the click
they save. Recorded so the trade is visible rather than rediscovered later.

### The edge that decides the shape

**A row can exist with no Nexus mod id.** Updates are matched several ways, and a row built from the
name index, or from a source that is not Nexus, has nothing to link to. That row must not render a
dead button. A button that does nothing is worse than the label it replaced, because the label never
promised anything.

So the button binds its visibility to `NexusModId is > 0`, and a row without one keeps exactly
today's appearance. It is not disabled-and-greyed: a greyed button invites a hover looking for a
tooltip that explains the refusal, and there is nothing useful to say beyond "we do not know which mod
this is," which the row already implies by having no version arrow.

### Surfaces

- `UpdatesView.xaml` — the standalone updates screen. The case this is about.
- `UpdateModsDialog.xaml` — the replace-these-files confirm. **Out of scope.** It is a dialog about
  files the user already has in hand; sending them to a browser mid-confirm is a different flow.

### Core change

Extract the URL builder into one pure place rather than adding a second copy:

```csharp
// src/ModManager.Core/Nexus/NexusModPage.cs
public static class NexusModPage
{
    /// <summary>The mod's page on Nexus, or null when we cannot name the mod. Both parts are
    /// required: a domain with no id is a game's whole mod list, which is not what the row
    /// promised.</summary>
    public static string? Url(string? nexusDomain, int? modId)
        => string.IsNullOrWhiteSpace(nexusDomain) || modId is not > 0
            ? null
            : $"https://www.nexusmods.com/{nexusDomain}/mods/{modId}";
}
```

`SaveBundle` is repointed at it, so there is one definition rather than two that can drift.

### Automation

The row `Grid` already carries `AutomationProperties.AutomationId="{x:Bind RowAutomationId}"`
(`UpdateRow.<modKey>`). The button gets a bound per-row name in the established convention:

```csharp
public string GetAutomationName => $"Get {ModName}";
public Visibility GetVisibility => Pending.NexusModId is > 0 ? Visibility.Visible : Visibility.Collapsed;
```

---

## Part 2 — leftover mod folders

### What is on disk right now

Disabling a mod moves its files to `<library>/_626mods/<game-id>/`, derived by
`Scanner.DataDirForGame`. Remove the game from the launcher and that folder stays: it holds the user's
files, is referenced by nothing, and is displayed nowhere.

Measured on the maintainer's machine, against 15 registered games:

| Folder | Files | Why it is orphaned |
|---|---|---|
| `demonologist` | 1 | game removed |
| `phasmophobia` | 1 | game removed |
| `ready-or-not` | 2 | game removed |
| `repo` | 1 | superseded by `r-e-p-o`, the id shape changed |
| `captain-of-industry` | 2 | game removed |
| `schedule-i` | 1 | game removed |
| `marvel-s-spider-man-2-2` | 2 | the duplicate-registration bug, fixed in 0.19 |

Seven folders, about ten files, well under a megabyte. **This is not a disk-space feature.** Two of
the seven exist because of launcher bugs since fixed, and the app has never once mentioned any of
them. The argument is honesty: the launcher's central promise is that disabling moves files rather
than deleting them, and a promise to keep your files implies telling you where they ended up.

### What it does

A new Settings section, **Leftover mod folders**, between *Restore points* and *Reset*. Reading order
across the dialog becomes: back it up, undo it, tidy it, reset it.

Each leftover lists its folder name, its file count and size, and the top-level names inside. Three
actions per row:

| Action | What it does | Risk |
|---|---|---|
| **Show files** | Opens the folder in Explorer | none, it only looks |
| **Save a copy…** | Folder picker, copies the whole tree out | none, it only writes elsewhere |
| **Remove** | Deletes the folder, behind a confirm | destructive, and the only one that is |

**Save a copy comes before Remove in the markup.** The one irreversible action is last, and is the
only one styled as danger.

### The naming problem, stated rather than glossed

`DataDirForGame`'s own summary says the folder holds "disabled mods, profiles, classification,
metadata." So **it is not only mods**, and calling the section "leftover mods" would be the kind of
lie that gets someone to click Remove on their profiles.

The section copy names all of it, and the per-row detail lists what is actually inside rather than
counting files abstractly. **Save a copy takes the whole folder**, never a filtered subset: deciding
for the user which parts of their own data are worth keeping is exactly the judgment this feature
exists to stop making.

### Detection, and the one way it can be wrong

In Core, split the way `DirectInject` and `DirectInjectListing` already split: a pure decision over
names, and a thin layer that does the walking.

```csharp
// src/ModManager.Core/LeftoverHoldings.cs
public sealed record LeftoverHolding(string Path, string FolderName, int FileCount, long Bytes,
                                     IReadOnlyList<string> TopLevelNames);

/// <summary>Pure: which of these folder names belong to no registered game. The whole judgment,
/// with no filesystem in it.</summary>
public static IReadOnlyList<string> Orphans(
    IEnumerable<string> registeredIds, IEnumerable<string> folderNames);

/// <summary>Walks the roots the registered games point at and describes what Orphans picked out.</summary>
public static IReadOnlyList<LeftoverHolding> Find(IReadOnlyList<GameEntry> registered);
```

**No filesystem interface.** An earlier draft of this spec called for one. That was wrong for this
codebase: Core already calls `Directory.GetDirectories` directly in `DataDirMove`, `DirectInject`,
`DirectInjectListing` and `ModListing`, `CorePurityTests` forbids only WinUI and WinRT, and the test
suite exercises this kind of code against real temp directories through `TestSupport.TempDir`. A seam
nothing else uses is a seam the next person has to justify.

The roots come from the registered games themselves, each one the parent of that game's
`DataDirForGame`. Never from scanning drives. A folder under a known root whose name matches no
registered game id is a leftover.

**The failure mode this shape accepts:** if a `_626mods` root holds only orphans, because every game
that ever used that library has since been removed, no registered game points at it and the root is
invisible. Those leftovers are not listed.

That is the correct trade. The alternative is walking the filesystem looking for a folder name, which
is how a tool ends up offering to delete a directory it merely recognised, on a drive it was never
pointed at. **A cleanup feature that can only see what the app itself created is a cleanup feature
that cannot delete a stranger's folder.** Said plainly in the section copy rather than hidden: it
lists leftovers beside games you still have.

### Reversibility

Remove deletes, permanently, which the operating laws otherwise forbid. It is admissible here for the
same reason deleting a restore point already is: the user is acting on launcher-managed state, saying
so explicitly, having been shown what it holds and offered a copy first.

The guardrails, all required:

1. **Never bulk.** No "remove all." Seven rows means seven decisions. A single button that clears
   everything is how someone loses a profile they meant to keep.
2. **Never default.** No preselection, no checkbox pre-ticked.
3. **The confirm names the folder and what it holds**, not "this item."
4. **Outlined danger at the entry, filled danger only inside the confirm**, per
   `.claude/rules/vsm-danger-buttons.md`, including the element-scoped `ButtonBackgroundPointerOver`
   and `ButtonBackgroundPressed` treatment. Without it the button reads danger until you reach for it,
   which is backwards.
5. **A registered game is never listed**, at any point, for any reason.

### Automation

`SettingsGroup.leftovers` on the section, matching the existing `SettingsGroup.*` convention. Per-row
bound names off the folder name, which is a stable key rather than display copy:

```csharp
public string ShowAutomationName   => $"Show files for {FolderName}";
public string CopyAutomationName   => $"Save a copy of {FolderName}";
public string RemoveAutomationName => $"Remove {FolderName}";
```

Plus `LeftoversEmptyText`, because a section that vanishes when empty cannot be asserted absent by a
harness that cannot tell "no leftovers" from "the section did not render."

---

## Testing

**Core, xUnit, before any App work:**

- `NexusModPage.Url` returns null for a null domain, an empty domain, a null id, a zero id and a
  negative id, and returns the exact expected string for a good pair.
- `SaveBundle` produces byte-identical URLs after being repointed at it.
- `LeftoverHoldings.Find`: a folder matching a registered id is never returned; a folder matching no
  id is; a root no registered game points at is not scanned; an empty root yields nothing; a folder
  whose name differs from a registered id only in case is treated as that same game.
- `CorePurityTests` keeps passing. `LeftoverHoldings` adds no WinUI or WinRT reference
  and touches only `System.IO`, which Core already uses throughout.

**Smoke, in `docs/smoke-tests/pending.md`, run rather than written:**

- The updates list shows a Get update button, it opens the right page, and a row with no Nexus id
  shows no button at all.
- The leftovers section lists exactly the seven known orphans and none of the fifteen registered games.
- Save a copy produces a byte-identical tree, verified by hash before and after.
- Remove is exercised **on a throwaway folder created for the test**, never on one of the real seven.

**The real seven are the maintainer's files, not test fixtures.** The harness proves the list is
right. What to do about each folder is his call, made in the UI, once.

## Out of scope

- Automatic cleanup, on a schedule or at startup. The app does not delete anything the user did not
  just ask it to.
- Re-adopting the game a leftover belongs to. Considered and declined for now: it is a good idea and a
  different feature, and folding it in makes this one about registration rather than about tidying.
- The Files tab landing for updates, per Part 1.
- `UpdateModsDialog`, per Part 1.
