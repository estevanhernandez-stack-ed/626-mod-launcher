# Save-type offer at Play: "you have no characters in this mode"

Status: decided, ready to plan, 2026-11-07. Origin: the live Play vanilla smoke for PR #407. Elden Ring started clean and showed no characters, because every character Este has lives in Seamless Co-op's `.co2` or Reforged's `.err`, and vanilla reads `.sl2`.

## The problem

Elden Ring keeps one save per mode, side by side in `%APPDATA%\EldenRing\<steamid>\`:

| File | Mode | On Este's machine |
|---|---|---|
| `ER0000.sl2` | Vanilla | Written 2026-11-07 9:13 by the vanilla run. Profile only, no characters. |
| `ER0000.co2` | Seamless Co-op | Last played Sep 3. |
| `ER0000.err` | Reforged | Last played Jan 6. |

A player who has only ever played modded presses Play vanilla, which is exactly what Patch Day now steers them toward, and lands on an empty title screen. It reads as "626 lost my saves." It didn't, but nothing tells them that, and the fix is a file copy they don't know exists.

## What already exists

- `GameSaveTypesCatalog` declares `.sl2` / `.co2` / `.err` for `fromsoft` (layout `TypedFiles`).
- `SaveManager.CloneToType(saveDir, source, targetExt, overwrite)` copies a save to another type. The source is never touched, and it refuses to overwrite unless asked.
- The Saves dialog's "Clone to…" already uses it. When the target exists it labels the action "Replace (snapshots first)" and calls `SaveManager.Backup(..., "before-clone", auto: true)` first.
- `EldenRingSave.ReadCharacters(path)` reads populated slots (name, class, level) from all three formats. It has been verified on `.sl2`, `.co2` and `.err`, including an `.sl2` with no characters.

So the copy and the safety net are done. What's missing is noticing the moment and asking.

## The behavior

**When:** at Play, after the Patch Day question (if any) and the ban-risk gate, and before handing off to the launch target.

**Condition**, all of which must hold:

1. The game declares `TypedFiles` save types, and the save folder is known.
2. The launch mode maps to a known target type (see *Which file a launch reads*).
3. The target file has zero characters, or doesn't exist.
4. At least one other declared type has one or more characters.
5. Save writes aren't refused (`SaveWritePolicy`).
6. The player hasn't dismissed it for this game and this target type.

If `ReadCharacters` throws on any file, treat that file as unknown: it isn't a source, and if it's the target, there is no offer. No guessing at a save we can't read.

**The prompt** (a ContentDialog, in-content buttons with AutomationIds, same pattern as `PatchDayDialog`):

> **No vanilla characters yet**
> Your characters are in your Seamless Co-op save. Vanilla Elden Ring reads a different file, so you'd start with an empty title screen.
> Copy your Seamless Co-op save over? \<first character's name\>, level \<n\>, and \<k\> more come with it. Your Seamless save stays as it is.
>
> [Copy and play] [Play without copying] [Don't ask for this game]

- When more than one source has characters, list each one as a choice with its newest write date and its first character. Default to the most recently written.
- **Copy and play:** snapshot the save folder (`before-clone`, auto), then `CloneToType(..., overwrite: true)`, then launch. If the copy throws, show `ErrorRemedy.Describe` and don't launch; the snapshot is already there.
- **Play without copying:** launch, change nothing.
- **Don't ask for this game:** store a per-game, per-target-type dismissal; launch. It's undone from game setup.
- Closing the dialog launches nothing, the same as Patch Day's Not now.

## Which file a launch reads

| Launch | Target type | Known how |
|---|---|---|
| Vanilla (Patch Day or the vanilla split) | `.sl2` | `LaunchMode.Vanilla` |
| Modded with Seamless installed | `.co2` | `SeamlessFullyInstalled`, the same signal `LaunchTargetPicker` uses |
| Reforged (launch target is `ReforgedLauncher.exe`, or a `.BAT` beside `internals\launcher\ReforgedLauncher.exe`) | `extension` from `<reforged>\mod\altsaves.toml`, else `.err` | The launcher (decision 3) |
| Reforged with `--alt-save-profile-N` | unknown | No offer until an alt profile is measured on a real install. |
| Anything else modded | none | No offer. |

Reforged installs outside the game folder (Este's is `D:\ERRv2.1.2.3`) and starts through its own launcher, so the launch target is the signal, not anything in the play folder. Its `altsaves.toml` is user-editable (`extension`, `seamless_extension`), which is why the target type is read from it rather than assumed. On Este's install both keys are `.err`.

## Save types come from what the game and its mods write (decision 4)

"If the game or its mods can do it, we should too." Today `GameSaveTypesCatalog` hardcodes three FromSoft types by engine. Two changes:

- **Declared types move to the signed game manifest** per game, next to `saveLayout`, so a new game or a new mod's save type is a feed data PR, not an app release. The embedded snapshot carries today's three for Elden Ring so nothing regresses offline.
- **Add `.mod` ("Alt Saves")** for Elden Ring. Reforged's README lists `ER0000.mod` beside `.sl2` / `.co2` as a save players migrate from. It's a source only until its own launch signal is known.

Another FromSoft title (Dark Souls III, Sekiro, Armored Core VI) gets an entry only after its mods' save extensions are seen on a real install. The logic itself is engine-agnostic.

## Warnings on the way into vanilla (decisions 1 and 2)

Copying into the vanilla save puts a character where FromSoftware's servers and anti-cheat can see it. The two modded sources get different weight:

| Source → vanilla | Treatment |
|---|---|
| Seamless Co-op (`.co2`) | **Warn.** A line in the dialog: "This character goes online with anti-cheat on. If it picked up anything a mod added, that could flag your account." Copy and play is still one click. |
| Reforged (`.err`) or Alt Saves (`.mod`) | **Warn and acknowledge.** Same line, plus a checkbox ("I understand this character may carry Reforged items into online play") that must be ticked before Copy and play enables. Same shape as the ban-risk gate. |
| Vanilla → any modded type | No warning. Modded co-op runs on its own servers (Reforged's README says it never connects to FromSoftware's), so this direction carries nothing new. |

Reforged also migrates vanilla saves into `.err` on its own. That's the precedent for the offer: the mod already does it, so doing it in 626 is consistent with how the mod expects to be used.

## Operating laws

- **Reversible:** the existing target is snapshotted before it's replaced; the source is never written to. "Restore" in the Saves dialog brings the old target back.
- **Never forced:** nothing copies without a click. Dismissal is per game and undoable.
- **Ban risk:** this doesn't enable a mod, so `BanRiskRules.ShouldGateEnable` doesn't apply directly. Copies into vanilla carry the warnings above; Reforged and Alt Saves sources need an explicit acknowledgment.
- **Pure core:** the decision ("should we offer, and what") is a pure Core function over file facts, `SaveOffer.Evaluate(types, files, mode, dismissed) → SaveOffer?`, fully unit-tested. The App reads the files, shows the dialog and runs the copy.

## Decisions (Este, 2026-11-07)

1. **Reforged into vanilla:** offer it, behind warn-and-acknowledge.
2. **Seamless into vanilla:** warn.
3. **Reforged as a target:** detected by its launcher.
4. **Other games and save types:** if the game or its mods can do it, 626 should too. Types become manifest data; `.mod` joins Elden Ring's list.

Still open, not blocking: which file an alt save profile writes. Measure on a real install before offering on it.

## Risks

- **Steam Cloud.** `steam_autocloud.vdf` is in the save folder, so Steam syncs `.sl2`. The copy happens before the launch hands off to Steam, so Steam should see a newer local file and upload it. That needs one live check, watching for a cloud-conflict prompt.
- **Whole-file copy.** `ER0000.sl2` holds every slot and the profile, so the copy replaces the target's whole profile, not one character. That's fine when the target has no characters (the only case this offers), but the copy must never offer when the target has even one character.

## Tests (Core)

- Offers `.co2 → .sl2` on a vanilla launch when `.sl2` has 0 characters and `.co2` has some.
- Offers when `.sl2` doesn't exist.
- No offer when `.sl2` has 1 or more characters.
- No offer when no other type has characters.
- No offer when a file can't be read, for the target or for every source.
- Picks the most recently written source as the default when several have characters.
- Respects the dismissal per game and per target type.
- No offer for non-`TypedFiles` games, and when writes are refused.
- Seamless modded launch targets `.co2`; other modded launches get no offer.
- A Reforged launch target (exe, or `.BAT` beside the launcher) targets the `altsaves.toml` extension; a missing or unreadable toml falls back to `.err`; an `--alt-save-profile-N` target gets no offer.
- Into vanilla: a `.co2` source carries the warning; an `.err` or `.mod` source needs the acknowledgment before Copy enables; a vanilla source into a modded type carries neither.
- Manifest-declared save types round-trip as camelCase and override the embedded set by id; an older feed without them keeps the embedded three.
- (App, smoke) Copy and play snapshots first, copies, launches; the old target restores from the Saves dialog.

## Smoke

On Este's machine, with the current `.sl2` (no characters) in place: Play vanilla → offer appears naming the Seamless and Reforged saves → Copy and play from `.co2` → character on the title screen → close → Saves dialog shows the `before-clone` snapshot → restore it → `.sl2` is byte-identical to before.
