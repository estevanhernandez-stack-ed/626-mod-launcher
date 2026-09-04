# Games that are not on Steam

**Date:** 2026-09-04 · **Status:** design, not yet planned · **Repos:** `626-game-manifest`, `626-mod-launcher`
**Sibling:** `2026-09-04-launch-options-as-curated-data-design.md` — the same Steam-only coupling in the
launch path. That spec's L3 depends on this one's C5 (the explicit slug join).

## The problem, stated exactly

**Every one of the 154 games in the published manifest carries a Steam app id.** Not most — all of
them. A game bought from the EA app, Epic, GOG or the Microsoft Store cannot be curated at all: not
with a wrong engine, not partially, not at all. It is absent.

That surfaced while adding EA SPORTS College Football 27, which installs through the EA app. But the
game is not the point; it is the first one to hit a wall that stands in front of a whole category.

## What is already right, and it is most of it

The instinct is that this needs a new shape. It mostly does not.

**The schema is already multi-store.** `StoreIds` is
`{ SteamAppId?, GogId?, EpicAppName?, XboxStoreId? }`, and its own summary says the rest "exist so
GOG/Epic/Game Pass slot in later without a schema migration." Somebody already thought about this.

**The launcher already resolves by slug, not by store.** `Scanner.cs:63`:

```csharp
var manifestEntry = EffectiveManifest.Current.Games.FirstOrDefault(g => g.Id == game.Id);
```

Nothing at runtime asks where a game was bought. A curated entry with no Steam id would be picked up
today, if one could exist.

**The public generator already tolerates it.** `generate-public.py` writes `steamAppId` / `steamUrl`
only when there is one, and the `saves` work established that an absent optional field is read as
"nobody has curated it" rather than as a limitation.

## Where the constraint actually lives

Three places, and only the first one blocks curation.

**1. The miner drops the override — twice.** `OverridesLoader.Load` refuses to load an entry with no
Steam id, and `OverridesMerge.Apply` skips it again if it somehow arrived:

```csharp
// OverridesLoader.Load
if (entry is not null && !string.IsNullOrWhiteSpace(entry.SteamAppId))
    result.Add(entry);

// OverridesMerge.Apply
if (string.IsNullOrWhiteSpace(ov.SteamAppId)) continue;
```

Found while planning the build; the spec originally claimed one line. Everything downstream of both
checks is already keyed by slug.

**2. Discovery only enumerates Steam.** `AddGameDialog` walks Steam libraries and calls
`SteamGameImport.Plan`. There is no EA / Epic / GOG equivalent, so a non-Steam game is only ever added
through the manual form.

**3. The quick-pick catalog assumes a Steam id.** `PopularGames.Build()` projects
`g.Stores.SteamAppId!` into a **non-nullable** `PopularGame.SteamAppId`. A manifest entry without one
puts a null into a slot typed as present, and `OnPopularSelected` then feeds it to
`InstalledGameMatch.ByAppId`.

Only (1) prevents a game from being curated. (2) and (3) affect how conveniently it can be added.

## The join nobody wrote down

This is the part worth the most attention, because it is load-bearing and implicit.

A registered game's id comes from `EnginePresets.BuildGameEntry`:

```csharp
var id = UniqueId(Slugify(input.Id ?? input.Name), existingIds);
```

and `AddGameDialog` **never sets `input.Id`**. So in practice:

> a registered game's id is `Slugify(whatever the user typed as the name)`

and curation reaches it only when that equals the manifest's slug. For Steam quick-add it usually
does, because both sides derive from the same Steam name — but nothing enforces it and nothing reports
when it fails. The failure is silent and looks like "the manifest doesn't have this game."

`UniqueId` also suffixes on collision, which is how `marvel-s-spider-man-2-2` exists on the rig. **A
game that collides silently loses its curation**, because the suffixed id matches no manifest entry.

For Steam games this is a latent sharp edge. For non-Steam games it becomes the *primary* join, since
there is no store id to fall back on — so it has to be made explicit as part of this work, not after.

## Options considered

### A. One manifest per launcher — rejected

Este's opening suggestion, and worth stating why not.

- **A game is not owned by one store.** Cyberpunk 2077 is on Steam, GOG and Epic. Per-launcher files
  duplicate it and force a "which copy wins" rule that does not exist today.
- **Consumers multiply.** The hub site and the Discord bot fetch one file. They would fetch N, merge
  them, and reimplement the precedence rule — the same reasoning that rejected raw manifest fields in
  the public contract and published one summarised value instead.
- **Signing multiplies.** One detached signature becomes N, each independently verifiable and
  independently able to go stale.
- **It fragments a key that already works.** The launcher joins by slug. Splitting by store makes the
  store matter at exactly the layer that had stopped caring.

### B. Add an EA store id and keep requiring *a* store id — partial

Add `EaContentId` to `StoreIds`, and let the override key on whichever store id is present.

Better, and still wrong at the edges: it keeps the merge keyed on where a game was *bought*, so a game
sold nowhere the launcher knows (itch, a standalone installer, a Game Pass title before Xbox probing
exists) stays uncurateable. It also means adding a store is a code change in the miner every time.

### C. Key overrides on the slug, with store ids as optional facts — recommended

The manifest is a catalog of **games**. A game has a name, an engine, a mod path — and, incidentally,
some places you can buy it. Key on the thing that identifies the game, and let store ids be data.

This is what the launcher already does.

## The design

### C1. Overrides key on `id`, and `steamAppId` becomes optional

`OverrideEntry.SteamAppId` stops being required. `OverridesMerge.Apply` resolves an override to an
existing entry in this order:

1. by `steamAppId`, when the override has one — today's behaviour, unchanged for all 149 existing files
2. otherwise by `id`

An override matching nothing still adds a new entry, exactly as now. `NewFrom` needs no change; it
already builds from the override alone.

**Existing overrides are untouched.** All 149 have a Steam id and keep matching by it.

### C2. The build fails on a duplicate key

Keying on slug makes duplicate slugs the new failure mode, and there is no second key to disambiguate.
This is not hypothetical — **two overrides today both claim Steam app id `20920`**
(`the-witcher-2-assassins-of-kings.json` and `…-enhanced-edition.json`). The richer one wins by
iteration order alone; if that order ever flips, The Witcher 2 silently drops to nexus-only with no
engine and no mod path.

So: the miner validates, before merging, that no two overrides share an `id` and no two share a
`steamAppId`, and **fails the build** naming both files. That closes the existing hole and the one C1
would open, in one gate.

An override with neither an `id` nor a `name` to slugify is also a build failure — today it is a silent
skip.

### C3. `StoreIds` gains an EA identifier

Additive, no schema-version bump, unread by older binaries:

```csharp
public string? EaContentId { get; init; }   // "Origin.SFT.50.0001619"
```

That format is what the EA app writes on disk — College Football 27 installs to
`InstallData/<name>/base-Origin.SFT.50.0001619/`. It is recorded as a **fact about the game**, not as a
key: nothing in C1 depends on it. It becomes useful when EA-app discovery arrives.

### C4. The catalog stops assuming a Steam id

`PopularGame.SteamAppId` becomes `string?`, and `PopularGames.Build()` drops the `!`. The pick handler
fills the Steam box only when there is an id, and skips the installed-folder match rather than calling
`ByAppId(null)`.

`PopularGames.Build()` also filters on the `popular-games` provenance tag today, so a newly curated
non-Steam game would not appear in the picker at all until it carries that tag.

**Decided 2026-09-04: the picker shows every curated game that is INSTALLED on this machine.** Not all
154, and not just the tagged ones. The tag made sense when the list was hand-written; now it is the
difference between "we curated your game and you can find it" and "we curated your game and you
cannot" — and for a non-Steam game the picker is the only route, because no detection finds it.

**This pulls a light form of discovery into scope**, and the boundary is worth stating precisely. It
does NOT require integrating with each launcher's manifest database (EA Desktop's `InstallData`,
Epic's `.item` files, GOG's registry keys). It requires answering one question — *is there a folder
for this game on the disk* — which a probe of each launcher's conventional install root answers:

| Launcher | Conventional root |
|---|---|
| EA app | `C:\Program Files\EA Games\<name>` |
| Epic | `C:\Program Files\Epic Games\<name>` |
| GOG | `C:\GOG Games\<name>` |

A folder probe is a fraction of real discovery and enough for the picker. Real enumeration — reading
each launcher's own database so a non-default install location is found — stays out of scope.

**The +Game screen needs a rethink rather than another control.** It already carries a Steam list, a
manual-setup list, a popular-games box, an AI expander and a batch expander; adding a fourth list to
it is how a screen becomes unusable. Flagged as its own design pass, not folded in here.

### C5. The slug join becomes explicit

`GameInput` gains an `Id`, and `AddGameDialog` sets it when the user picks a catalogued game — from the
Steam quick-pick, the popular list, or a future non-Steam picker. `BuildGameEntry` already prefers
`input.Id ?? input.Name`, so this is a one-line change at each call site and no change in Core.

This is the difference between "curation reaches the game because the user typed the name we expected"
and "curation reaches the game because we said which game it is."

**Decided 2026-09-04: a colliding add is REFUSED**, and the launcher says the game is already in the
library rather than silently making a second one. In practice a collision is a duplicate add, not two
genuinely different games — and the current behaviour is the worst of both, because the suffixed id
matches no manifest entry and the game quietly loses every curated setting with nobody told.

This is the same fix as the already-open duplicate-registration issue, so the two land together:
`LauncherService.AddGame` has no already-registered guard, which is how `marvel-s-spider-man-2-2`
exists on the rig.

## Explicitly out of scope

**Discovery of non-Steam libraries.** Enumerating installed EA / Epic / GOG games so they appear in
the Add Game list is a separate, larger piece of App-side work, with a separate answer per launcher.
This spec makes such games *curateable*; it does not make them *discoverable*.

That split is deliberate and it is most of the value. Because the launcher joins by slug, a manually
added non-Steam game picks up its curated engine, mod path, ban risk and safe route the moment the
data exists. Quick-pick is a convenience on top.

**Launch options and the anti-cheat toggle.** `LaunchOptions.Catalog` is a hardcoded switch on Steam
app id with a single entry, so it has this same disease in a second place — a curated non-Steam game
still could not be offered an offline route. That is the sibling spec, not this one.

**Frostbite, and College Football specifically.** Nothing here makes the launcher able to manage
Frostbite mods — those are `.fbmod` files applied through FrostyModManager, and the engine list has no
Frostbite key. This spec lets College Football be *described* honestly (ban risk, safe route, no engine)
rather than being absent. Whether the launcher should speak Frostbite at all, or point at Frosty
through the tool catalog, is its own question — and the tool catalog is itself Steam-keyed
(`KnownTool.SteamAppId`), so it would need the same treatment.

## Risks

**Slug drift between the manifest and a registration.** The mitigation is C2 (no duplicates) plus C5
(explicit id). Residual risk stays for games added by typing a name, which is unavoidable without a
picker.

**A curated game nobody can find.** C1 makes an entry exist; without discovery or a catalog pick, a
user has to know to type the matching name. C5 helps only where there is something to pick *from*. This
is the strongest argument for revisiting the `popular-games` filter.

**An override that matches nothing, silently.** Today a bad `steamAppId` "just doesn't match — it's
reported in the build summary, never corrupts." Slug-keyed overrides must keep that property: an
override that neither matches nor validly adds should be *reported*, and C2 makes the worst version of
it fatal.

## Testing

Miner (pure, xUnit):

- an override with no `steamAppId` adds an entry keyed by its `id`
- an override with a `steamAppId` still matches by it, in preference to the slug — all 149 existing files
- two overrides sharing an `id` fail the build, naming both files
- two overrides sharing a `steamAppId` fail the build — **the Witcher 2 case, which fails today**
- an override with neither `id` nor `name` fails rather than being skipped
- an entry with no Steam id round-trips through the public generator with no `steamAppId` / `steamUrl`

Core:

- `PopularGames` projects an entry with no Steam id without throwing, and reports it as absent
- a registered game whose id matches a non-Steam manifest entry receives its curated engine and mod path

App (smoke, since it is untestable headless):

- add a non-Steam game by hand with an id matching a curated entry; its engine, mod path, ban risk and
  safe route come from the manifest

## Questions, answered 2026-09-04

1. ~~Should `PopularGames` keep filtering on the provenance tag?~~ **No — show every curated game that
   is installed on this machine.** Written into C4, along with the folder-probe scope boundary it
   implies and the note that +Game needs its own design pass.
2. ~~What happens on a slug collision at add time?~~ **Refuse the add**, saying the game is already in
   the library. Written into C5; lands with the existing duplicate-registration issue.
3. **Still open: is `EaContentId` the right EA key?** One EA game observed
   (`Origin.SFT.50.0001619`), and sports titles ship yearly, so there is no way yet to tell whether
   that id is stable across releases or whether the store's offer id is better. **Dropped from phase
   one** rather than answered — nothing keys on it, so waiting costs nothing.
