# Main-window Toolbar Relabel + Regroup — Design Spec

**Date:** 2026-05-25
**Status:** Approved (Este, visual brainstorm)
**Related:** [feat/main-window-toolbar-relabel](https://github.com/estevanhernandez-stack-ed/626-mod-launcher/tree/feat/main-window-toolbar-relabel)

## Why

Two specific complaints from real-day use:

1. **"Too busy at the top."** The command bar runs 11 buttons in a row with no group labels, plus a 4-control right cluster. The eye has nothing to anchor on.
2. **"Missing labeling."** The lone `↻` toolbar icon is unlabeled (tooltip only). Per-row glyphs (📄 readme, ⚙️ config, 🗑️ uninstall) are unlabeled. The chip wall (`SP`/`MP?`/`BOTH`/`3x`/`MP-SAFE`) has no legend.

The fix is **both at once** — relabel every glyph and surface the implicit grouping that already exists in the XAML (vertical-rectangle dividers between control clusters at [MainWindow.xaml:102](src/ModManager.App/MainWindow.xaml#L102) and [:106](src/ModManager.App/MainWindow.xaml#L106)). No new functionality — every existing button does the same thing it does today.

## Scope

Four pieces. Each ships in one PR (`feat/main-window-toolbar-relabel`).

### 1 — Title bar: `⋯` absorbs the low-frequency command-bar items

The `⋯` menu at [MainWindow.xaml:43-73](src/ModManager.App/MainWindow.xaml#L43-L73) already holds Find-mods-on-Nexus, Find-mods-on-CurseForge, Backfill-metadata-from-Nexus-archives, Re-scan-mods-&-launchers, and Remove-this-game.

Add two more low-frequency items moved out of the command bar:

- **Fetch metadata** — periodic action, once per session at most.
- **+ Theme** — theme creation, once-in-a-blue-moon.

Net: title bar unchanged visually; the menu grows by two items in a "Maintenance" / similar sub-group.

### 2 — Command bar: labeled groups + verb-first labels

**Before** (11 controls + 4 right-cluster = 15):
`↻ All On  All Off | All  MP  SP | + Add  Fetch metadata  Unlock load order  Profiles  Saves` ··· `By source ▼  Aurora ▼  Nexus  + Theme`

**After** (8 + 3 = 11, grouped):

| Group label | Controls |
| --- | --- |
| **List** | `↻ Rescan` |
| **Toggle** | `Enable all` · `Disable all` |
| **Loadout** | Segmented control: `All / MP / SP` (one-of-three) |
| **Mods** | `+ Add mods` (primary affordance, accent-tinted border) |
| **Library** | `Reorder` · `Profiles` · `Saves` |
| *(flex spacer)* | |
| **View** | `By source ▼` · `🎨 Aurora ▼` |
| **Account** | `● Nexus` (status dot — green when authed, gray when not) |

**Labeling changes:**

- `↻` → `↻ Rescan` (icon + text)
- `All On` → `Enable all` (verb-object, not state-adjective)
- `All Off` → `Disable all`
- `All / MP / SP` → segmented control under the **Loadout** label (it's clearly "pick one of three" with the group label answering "of what")
- `+ Add` → `+ Add mods`
- `Unlock load order` → `Reorder` (verb, not state-toggle — the toggle behavior stays the same; the *label* now reads as the action it triggers)
- Theme dropdown gets a `🎨` prefix so the dropdown name reads as "theme picker", not just the theme name
- `Nexus` gets a 6px status dot prefix (green = authed, gray = not authed)

**Moved out** (to the `⋯` menu, see §1):

- `Fetch metadata`
- `+ Theme`

**Group label rendering:** small uppercase letter-spaced text above each cluster, same height as the existing divider gaps so the bar height doesn't grow. Color matches `ThemeMuted` (the existing low-contrast text color).

**Dividers:** keep the existing vertical-rectangle separators between groups.

### 3 — Per-row glyphs: tiny subscript labels

Each per-row button gets a 9px subscript label *under* the glyph, always visible. The glyph stays compact + scannable, the label removes mystery.

| Glyph | Label | Behavior |
| --- | --- | --- |
| 📄 | `readme` | Existing [`OnShowReadme`](src/ModManager.App/MainWindow.xaml#L286) — unchanged. |
| ⚙️ | `config` | Existing [`OnShowCockpit`](src/ModManager.App/MainWindow.xaml#L292) — shown only when the mod has configurable bindings. Unchanged. |
| 🗑️ | `uninstall` | Existing [`OnUninstall`](src/ModManager.App/MainWindow.xaml#L305) — unchanged. |

The toggle stays unlabeled — its meaning is obvious (on/off) and the row already shows the enabled/disabled state visually (opacity + accent color).

### 4 — Chip legend: better tooltips + `?` glossary popup

The mod-row chips (`SP`, `MP?`, `BOTH`, `MP-SAFE`, `MP-RISKY`, `3x` and friends) communicate compatibility + variant state. Two surfaces:

**(a) Hover tooltips** on every chip. Each chip gets a one-sentence explainer:

- `SP` — Active only in your single-player loadout.
- `MP?` — Author hasn't claimed an MP stance. Right-click to set.
- `BOTH` — Active in both your SP and MP loadouts.
- `MP-SAFE` — Author or verified-safe community list says this works in MP.
- `MP-RISKY` — Author or community list says this risks anti-cheat / desync in MP.
- `3x` (variant chips) — The active level of a variant family. Click another variant to switch.

**(b) Section-header `?` button** — a tiny rounded `?` on the right of the FIRST rendered section header (top of the list, regardless of which group-by is active). Clicking it opens a popup glossary listing every chip and glyph used on the list, with the same explainers as the hover tooltips. One-time discovery affordance; closes on click-outside, no nagging. Only on the first header to avoid noise across sectioned group-by views.

## Non-goals (this PR)

- No new functionality — every button still does what it does today.
- No icon-set change (Segoe MDL2 stays).
- No layout reshuffle below the toolbar (mod rows stay where they are).
- No theming-system changes.
- No chip *behavior* changes (right-click-to-set-MP-stance still works, etc.) — only their hover-help + a legend popup.
- No keyboard shortcut work (separate PR if Este wants it).

## Affected files

- [src/ModManager.App/MainWindow.xaml](src/ModManager.App/MainWindow.xaml) — the command bar, title-bar `⋯` menu, and per-row glyph templates.
- [src/ModManager.App/MainWindow.xaml.cs](src/ModManager.App/MainWindow.xaml.cs) — chip glossary popup handler.
- [src/ModManager.App/ViewModels/MainViewModel.cs](src/ModManager.App/ViewModels/MainViewModel.cs) — no logic change. (The `SetModeCommand` is reused by the segmented control; the existing `AllOnCommand`/`AllOffCommand` are reused, just with new button labels.)

## Risk

Low. This is a labeling + grouping pass over existing wired-up controls. No data-path changes, no Core changes, no test changes (existing tests pass through unchanged because the commands they reach are unchanged).

The one judgment call worth naming: **`Unlock load order` → `Reorder`.** Today's button is a toggle (its label changes to `Cancel reorder` while load-order mode is active). After: `Reorder` enters reorder mode; the row beneath it (Apply order / Cancel) handles the exit. That matches what the load-order-mode XAML already does at [MainWindow.xaml:116-122](src/ModManager.App/MainWindow.xaml#L116-L122) — no behavior change, just relabeling the entry button.

## Open questions

None — all four pieces approved in brainstorm 2026-05-25.

## Approval

- [x] Title bar `⋯` absorbs Fetch metadata + + Theme
- [x] Command bar relabeled + grouped under labels + segmented Loadout control
- [x] Per-row glyphs gain 9px subscript labels
- [x] Chip tooltips + section-header `?` glossary popup
