# Ban risk for games without a Steam id, and a gate on save writes

**Date:** 2026-09-13
**Status:** approved 2026-09-14 (three questions answered by the owner)
**Program:** EA football, prerequisite zero (see `docs/superpowers/plans/2026-09-13-ea-football-grand-plan.md`, A3)
**Blocks:** EA app detection, and anything that writes a College Football or Madden file

## The defect, verified

Every ban-risk decision in the launcher looks the game up by **Steam app id and nothing else**:

```csharp
// src/ModManager.Core/BanRiskCatalog.cs
if (level != GameBanRisk.None && g.Stores.SteamAppId is { } appId)
    map[appId] = level;
...
public static GameBanRisk ByAppId(string? steamAppId) => ...
```

Five call sites pass `GameEntry.SteamAppId`:

| Site | What it decides |
|---|---|
| `MainViewModel.cs:406` | the ban-risk warning shows |
| `MainViewModel.cs:486` | the ban-risk state chip shows |
| `MainViewModel.cs:1309` | `GateBanRiskEnableAsync`, the enable prompt |
| `LibraryViewModel.cs:465` | the risk label on the game row |
| `Mcp/Tools/WriteTools.cs:64` | whether an agent may enable a mod |

A game registered from the EA app has no Steam app id. For that game every one of these reads
`None`: no warning, no chip, no prompt, and an agent can enable mods with no acknowledgment. The two
games the EA football program exists for are both flagged `high` in the feed, and both would be
added from the EA app. So the first thing the program builds would switch the operating law off for
exactly the games it is about.

It is not a hypothetical path. Any game added manually, from Xbox, or from a folder, has the same
hole today. It has not bitten yet because every high-risk game that is registered came from Steam.

A second, smaller gap sits behind it: the **embedded** manifest snapshot carries no `banRisk` at all.
On a first run with no network, every high-risk game reads `None` until the feed arrives.

## What changes

### 1. One resolver that takes the game, not its Steam id

Mirror `NexusDomains.Effective(GameEntry)`, which already solved the same shape of problem for Nexus
domains:

```csharp
// src/ModManager.Core/BanRiskCatalog.cs
/// <summary>The ban risk the launcher acts on for this game: the highest of what the feed says by
/// Steam app id, what it says by manifest id, and the compiled floor. Highest wins, so no single
/// source can lower another.</summary>
public static GameBanRisk Effective(GameEntry game)
    => BanRiskRules.Max(BanRiskRules.Max(ByAppId(game.SteamAppId), ById(game.Id)),
                        CompiledFloor.For(game.Id, game.SteamAppId));
```

- **`ById`** is a second map built in the same `Build()` pass, keyed by manifest entry id,
  case-insensitive. It shares the generation check, so a feed update still protects players who
  already added the game, with no migration. That property is why the catalog resolves live, and it
  has to survive this change.
- **Max, not first-match.** If the Steam id says `medium` and the id says `high`, the answer is
  `high`. A resolver that stops at the first hit lets whichever source is checked first lower the
  other.

### 2. A compiled floor for the two games the program writes to

```csharp
internal static class CompiledFloor
{
    // Keyed by manifest id AND by Steam app id, so a Steam copy registered under an older id still
    // hits. Only games the launcher itself writes files for belong here; everything else stays
    // data in the feed.
    ["ea-sports-college-football-27"] / "4032350" -> High
    ["madden-nfl-27"]                 / "3940610" -> High
}
```

**Why compiled, when the rule is that game facts live in the feed:** the feed is the right home for a
fact about a game. This is a fact about **the launcher's own write features**. The launcher is about
to ship code that writes roster files for these two titles, and the protection that goes with that
code should not depend on a network fetch having succeeded, or on the embedded snapshot gap above.
The floor ships with the writer, in the same binary.

**The floor can only raise.** A feed that says `low` for Madden still resolves `High`. Lowering a
compiled floor is a code change and a release, on purpose.

The list stays short. A game only joins it when the launcher gains a feature that writes that game's
files. It is not a second catalogue.

### 3. Every call site goes through it

All five sites above switch to `BanRiskCatalog.Effective(game)`. The row label, the chip, the warning,
the enable gate and the agent gate then cannot disagree about the same game.

`ByAppId` stays public because its tests use it, and a **source-scan test** keeps it from coming
back: no file under `src/ModManager.App/` or `src/ModManager.Mcp/` may contain
`BanRiskCatalog.ByAppId(`. `SmokeCatalogueTests` already reads repo files the same way. Without the
guard, the next new surface copies whichever call it finds first, and half of them take a Steam id.

### 4. Save writes on a high-risk game ask first

Today the acknowledgment gate covers **enabling mods**. Writing a save is not enabling a mod, so the
Elden Ring character editor writes a modified save on an anti-cheat game with no prompt at all. The
EA football program's first write feature is a roster file, which is the same kind of act. The
decision recorded in the grand plan is that save writes on high-risk games sit behind an
acknowledgment. Three questions came out of that, answered here.

#### Separate acknowledgment, not the enable one — decided 2026-09-14

A user who ticked *Don't warn me again* when enabling mods on Elden Ring agreed to one thing. Silently
carrying that into save edits would be treating two different risks as one tick-box. Save writes get
their own record:

```csharp
public enum BanRiskAck { EnableMods, WriteSaves }

// src/ModManager.Core/BanRiskAckStore.cs — existing calls keep EnableMods as the default
public static bool IsAcked(string dataDir, string gameId, BanRiskAck kind = BanRiskAck.EnableMods);
public static void Ack(string dataDir, string gameId, BanRiskAck kind = BanRiskAck.EnableMods);
```

`EnableMods` keeps `ban-risk-acks.json`, byte for byte, so nobody's existing acknowledgment moves.
`WriteSaves` uses `ban-risk-save-acks.json`, the same plain JSON array of game ids through
`AtomicJson`.

The policy stays in Core beside the one it mirrors:

```csharp
/// <summary>A save write on a high-risk game asks every time, until the user ticks "don't ask again"
/// for this game's saves. Separate from ShouldGateEnable so the two can diverge without a rename.</summary>
public static bool ShouldGateSaveWrite(GameBanRisk level, bool saveWritesAcked)
    => level == GameBanRisk.High && !saveWritesAcked;
```

#### Which paths are gated — decided 2026-09-14

The owner's rule, in the owner's words: *don't make it difficult for users to fix things and go back to normal
play; only verify when they may be taking an action that could be harmful.* It is the same asymmetry
the enable gate already uses: **putting something new into a save asks; fixing it, or getting back to
what you had, never does.**

| Path | Gated | Why |
|---|---|---|
| Edit character (`SavesDialog.OnEditCharacter`) | **yes** | puts a new change into a save |
| Save-mod drop install (`SaveModFlow.TryHandleDrops` → `InstallWorld`) | **yes** | puts a new mod into a save |
| Bring a save in (`SaveBundle.Restore`) | **yes** | puts save content from somewhere else into the save |
| Reset a save mod (`SaveModInstaller.ResetWorld`) | no | a fix: puts back the starting state of a mod that is already installed, nothing new |
| Remove a save mod (`SaveModInstaller.RemoveWorld`) | no | moves toward vanilla, like disabling a mod |
| Restore a snapshot, a world, or a save type (`SaveManager.Restore*`) | no | undo is never gated |
| Restore a profile archive (`ProfileRestore.Restore`) | no | the user's own backup, and undo |
| Clone to another save type, or Replace (`SaveManager.CloneToType`) | no | moves your own progress between the game's own save types |
| The EA roster writer, when it exists | **yes** for writing an edited roster | the contract it is built against |
| Removing or restoring a roster, when that exists | no | back to normal play |

Bundle import and clone were classified after the final review, applying the owner's rule; clone/replace
is the call most open to reversal.

**The edge this accepts:** a snapshot can hold a save that was edited. Restoring it puts back an edit
that was either acknowledged when it was made, or made outside the launcher. Gating undo to close
that would make the safety net itself something you have to click through, which is the worse trade.

**Ask before the work, not after it.** The character editor asks before its form opens, not after
the user has typed a new name and pressed save. A prompt that refuses finished input teaches people
to click through prompts.

**The drop path stays pure.** `SaveModFlow.TryHandleDrops` gains a `bool writeAllowed`. When it is
false and a drop is a save mod, the verdict is a new `SaveModDropOutcome.NeedsAcknowledgment` and
nothing is written. The view-model asks once for the whole drop, then re-runs only those paths with
`writeAllowed: true`. One prompt per drop, not one per file, and the decision is testable in Core
with no dialog in it. On an un-acked high-risk game, a save-mod drop meets the enable prompt first and
then the save prompt, because the two acknowledgments are separate by decision.

**Agents.** No MCP tool writes saves today. When one does, it follows `AgentWriteRules`: an un-acked
high-risk save write is refused with a named refusal, and an agent never answers the prompt on the
user's behalf. Stated now so the rule exists before the tool does.

#### How often it asks — decided 2026-09-14

**Every time**, on every gated write, until the user ticks *Don't ask again for this game's saves*.
The tick stops it for that one game and no other. It is never a one-time notice that quietly goes
away, and an acknowledgment on one game never covers another.

This includes people already using the editor, which makes it a behaviour change. Elden Ring is the
only high-risk game with a save writer today (Cyberpunk 2077's character list is read-only and not
high-risk; Monster Hunter Wilds has no writer), so the next time an Elden Ring player edits a
character, the prompt appears.

That is new friction on a shipped feature, and it is right: the editor has been writing modified saves
on an anti-cheat game without saying so. It goes in the release notes as a change, not a fix.

### The prompt

Built the way `ConfirmBanRiskEnableAsync` is built, in `MainWindow` for the drop path and in
`SavesDialog` for the character editor, through a shared helper so the copy exists once.

- **Title:** *Write to a save on {game}?*
- **Body:** *This game uses anti-cheat, and its publisher's rules can treat a modified save as a
  reason to ban an account. The launcher snapshots the save first, so you can put it back. It can't
  tell you whether an edited save is safe to take online, and it won't guess.*
- **Second line:** *If your saves sync to the cloud, the edited one syncs too.* True for Steam Cloud
  and the EA app both, and the part people most often do not know.
- **Checkbox:** *Don't ask again for this game's saves*
- **Buttons:** *Write the save* / *Cancel*, with Cancel as the default button.

Not danger-filled. This is an informed choice about the user's own file, not a destructive act, and
the snapshot is taken either way.

The prompt never says a write is safe, in any wording, for any game. That comes from the grand plan's
decision and it binds this copy.

**Automation:** the checkbox is `SaveWriteRiskDontAsk`, the body text is `SaveWriteRiskBody`. The
nested-dialog trap in `SavesDialog` applies: one `ContentDialog` per `XamlRoot`, so the prompt opens
after `this.Hide()`, and `SavesDialog` re-shows after, the way `OnEditCharacter` already does.

## Testing

**Core, xUnit, failing first:**

1. **The defect itself.** A feed entry `ea-sports-college-football-27`, `banRisk: high`, with a Steam
   id. A `GameEntry` with that `Id` and `SteamAppId = null`. `Effective` returns `High`. Today it would
   return `None` and the test fails, which is the point of writing it first.
2. **Steam games unchanged.** Every existing `ByAppId` case gives the same answer through `Effective`.
3. **Max wins, both directions.** Steam id says medium, id says high: high. Steam id says high, id
   says low: high.
4. **The floor with no feed.** `SetRemote(null)`, a `GameEntry` for each floor game by id only and by
   Steam id only: `High` in all four.
5. **The feed cannot lower the floor.** Feed says `low` for `madden-nfl-27`: still `High`.
6. **Id matching is case-insensitive** and a different id with the same prefix does not match
   (`madden-nfl-27-2` is not `madden-nfl-27`).
7. **Generation.** Change the feed after a first resolve and `Effective` sees the change.
8. **`ShouldGateSaveWrite`:** High and not acked gates; High and acked does not; Medium, Low and None
   never gate. Calling it twice with nothing acked gates twice: there is no hidden "already shown" state.
9. **Ack kinds are independent.** Acking `EnableMods` leaves `WriteSaves` un-acked and the reverse.
   `EnableMods` still reads and writes `ban-risk-acks.json`, and an existing file from before the
   change is still honoured.
10. **The drop path.** `TryHandleDrops` with `writeAllowed: false` on a save-mod zip returns
    `NeedsAcknowledgment` and the save folder is byte-identical afterwards; a non-save-mod zip is
    unaffected by the flag.
11. **The guard.** No file under `src/ModManager.App/` or `src/ModManager.Mcp/` contains
    `BanRiskCatalog.ByAppId(`.

**Smoke, run rather than written, in `docs/smoke-tests/pending.md`:**

- Monster Hunter Wilds and Elden Ring still show the ban-risk chip and still gate enable.
- Elden Ring: Edit character prompts before the editor opens; Cancel opens nothing and writes nothing;
  without the box ticked it asks again on the next edit; ticking the box stops the prompt for Elden
  Ring and not for any other game; Reset and Remove on a save mod never prompt.
- A throwaway manually registered game with the College Football id and no Steam id shows the chip
  and gates enable. Registered against an empty folder, never the real install, and removed after.

Character edits in smoke run against a **copy** of a save in a throwaway folder, never a real save
directory.

## Out of scope

- **The embedded snapshot's missing `banRisk`.** The floor covers the two EA titles only. The general
  fix belongs in the manifest miner, so the snapshot carries what the feed carries. Its own change.
- **A game registered under a different id, or typed in by name.** `Effective` matches by manifest id
  and Steam id only - it does none of its own name matching. A typed manual add slugifies the display
  name ("EA SPORTS Madden NFL 27" -> `ea-sports-madden-nfl-27`), which is not the manifest id
  (`madden-nfl-27`), so a game with no Steam id added that way still reads `None`. The fix belongs at
  registration, not in the resolver, and it is a requirement rather than an aside: EA app detection
  (sub-project 1) **must** register EA Sports College Football 27 and Madden NFL 27 with
  `GameInput.Id` set to the manifest id (`ea-sports-college-football-27` / `madden-nfl-27`), not a
  slugified display name.
- **EA app detection and registration.** Sub-project 1, which this unblocks. The other Steam-id-only
  facades (`SaveDirHints`, `KnownEngines`, `KnownModPaths`, `SaveLayoutCatalog`) have the same shape
  and will miss an EA game too. Those are lookups that make a game less helpful, not less safe, so
  they belong to that sub-project rather than to this fix.
- **The roster writer.** It inherits the save gate as a contract, and gets its own spec.
- **Legacy duplicate ids** such as a `-2` suffix from before duplicate registration was refused. A
  duplicate with no Steam id still misses the id lookup. New duplicates are already refused
  (`Registry.cs:39`, `LauncherService.cs:76`), and none of the high-risk games are affected today.
