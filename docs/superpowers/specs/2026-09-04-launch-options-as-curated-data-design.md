# Launch options as curated data

**Date:** 2026-09-04 · **Status:** design, not yet planned · **Sibling of:** `2026-09-04-non-steam-games-in-the-manifest-design.md`

## The problem, stated exactly

The knowledge of *how to launch a game so mods actually load* is a hardcoded `switch` in the binary,
keyed on Steam app id, containing **one entry**:

```csharp
static IReadOnlyList<LaunchOption> Catalog(string? appId) => appId switch
{
    "1245620" => new[] { /* Elden Ring: the EAC bootstrapper swap */ },
    _ => Array.Empty<LaunchOption>(),
};
```

Three consequences, in rising order of how much they cost:

1. **Adding a game's launch route needs an app release.** Every other kind of game knowledge became a
   data PR; this one did not.
2. **A game with no Steam app id can never have one**, because the switch keys on the id it does not
   have. This is the same wall the sibling spec is about, standing in a second place.
3. **The manifest can already say a safe route exists and cannot say how to take it.** `safeRoute` is
   published for 14 games — `offline` for 8 of them — but only Elden Ring can offer the toggle. The
   feed describes a door that only one game has a handle for.

That third point is the one that made this worth a spec of its own. The data layer is already ahead of
the code layer, and the gap is visible to users.

## The rule this design cannot break

The manifest's founding law, from `CLAUDE.md`:

> The manifest is descriptive only — it never says how to enable/disable a mod (that stays compiled
> code).

And the Store SKU's:

> `Configuration=Store` leaves `FULL` undefined: the off-Store plugin loader and the EAC-disable toggle
> are compiled out and **binary-verified absent**.

So the design has to move *descriptions* into data while leaving every *mechanism* in code, and it must
not give the sealed Store build any new ability — not even by accident, and not even if the feed says
so.

## What is there today

`LaunchOption` already separates the two cleanly, which is most of the work:

| Field | What it is |
|---|---|
| `Title`, `Detail` | copy shown to the user |
| `Kind` | `Internal` (app runs an exe), `External` (user pastes into Steam), `AntiCheatToggle` |
| `Exe`, `Args`, `WorkingSubdir` | parameters for `Internal` |
| `SteamOptions` | the string for `External` |
| `Bootstrapper`, `RealExe` | parameters for the reversible swap |
| `Recommended` | drives the library "needs a launch option" highlight |

Every one of those is **data**. None of them is code. The mechanism lives elsewhere:

- `AntiCheat.Disable/Enable` — the reversible swap, `#if FULL`, absent from the Store binary
- `LaunchOptions.For` — filters `AntiCheatToggle` out under `#if !FULL`, belt and braces
- `MainViewModel.SetAntiCheat` — `#if FULL` call site
- `check-store-seal.ps1` — fails the build if `AntiCheatState` appears in a Store binary

**So the safety property survives the move for free.** A Store build that receives a manifest entry
describing a bootstrapper swap does nothing with it, because the code that could act on it is not in
the binary. Data describing a swap, with no swap, is inert. The seal keeps working unchanged.

## The new risk, and it is real

Today a launch option is compiled in, so it was reviewed by whoever compiled it. As data it arrives
over the network, and **`Bootstrapper` / `RealExe` / `Exe` name files the launcher will rename, copy, or
execute inside a game folder.** That is a new class of remote-driven file operation.

Three things already contain it, and a fourth is needed:

1. **The feed is signed.** ECDSA P-256, verified against a key pinned in the binary. This is not
   arbitrary input; it is input from us.
2. **The swap is backup-first and reversible.** `AntiCheat.Disable` preserves the original as
   `.626off` and never deletes; `Enable` restores it. A wrong entry is undoable.
3. **`ManifestValidator` already gates the one trust-sensitive field.** `ModPath` is rejected when
   absolute, drive-qualified, or containing `..`, described in its own summary as "defense in depth."
4. **The new fields need the same gate, and a stricter one.** `Bootstrapper` and `RealExe` are
   *file names*, not paths: no separators at all, no `..`, no drive letter, and a `.exe` extension.
   `WorkingSubdir` and `Exe` are relative paths and take the existing `IsSafeRelativePath` rule.
   An entry failing any of these is **rejected**, the same as a bad `ModPath` — not skipped, not
   sanitised.

Beyond validation, the runtime already refuses to act on a name that is not there: `AntiCheat.Disable`
throws when the bootstrapper or the real exe is missing, and `State` returns `Unsupported`. A wrong
name is inert rather than destructive.

## Design

### L1. Launch options become a manifest field, keyed by game

`GameManifestEntry` gains:

```csharp
public IReadOnlyList<LaunchOptionEntry>? LaunchOptions { get; init; }
```

Keyed by the game, like everything else — **not by Steam app id**, so it works for the games the
sibling spec unblocks. `LaunchOptionEntry` mirrors `LaunchOption`'s existing fields one-for-one; no new
concepts are introduced by the move.

Additive and optional, so no `schemaVersion` bump and older binaries ignore it.

### L2. `LaunchOptions.For` reads the manifest, keyed by id

```csharp
public static IReadOnlyList<LaunchOption> For(string? gameId)
```

resolving through `EffectiveManifest` the way `KnownEngines` / `NexusDomains` / `PopularGames` already
do, and keeping the `#if !FULL` filter exactly as it is.

**The Elden Ring entry moves to `overrides/elden-ring.json` unchanged** — same bootstrapper, same real
exe, same copy. That entry is the migration test: the behaviour after the move must be
indistinguishable.

### L3. The signature stays `gameId`, and the callers change with it

Four call sites pass `SteamAppId` today and would pass `Game.Id`:

- `LaunchScan.cs:46` — finds internal-option exes
- `MainViewModel.ActiveLaunchOptions` — the dialog
- `MainViewModel` line ~988 — `NeedsAttention`, the library highlight
- `MainWindow` — the dialog host

This is a mechanical change, and it is the same slug join the sibling spec makes explicit. **The two
specs share that dependency**: launch options keyed by slug are only as reliable as the slug join, so
L3 should land after (or with) the sibling's C5.

### L4. `Recommended` needs a companion the catalog cannot express

Today the library highlight is *not* pure data:

```csharp
LaunchNeedsAttention = LaunchOptions.NeedsAttention(_ctx.Game.SteamAppId)
    && !_direct.SeamlessFullyInstalled(_ctx.Game);
```

The suppression is real and correct — a user with Seamless Co-op fully installed does not need the
vanilla anti-cheat toggle, because Seamless brings its own bypass. But it is Elden-Ring-shaped logic
sitting in the view-model, reached via a Seamless-specific service.

Moving the catalog to data does not move that, and **should not**: "is this specific framework fully
installed" is exactly the kind of judgement the manifest is forbidden from making. The spec's position
is that `Recommended` stays a data field meaning *"this game needs a launch option in principle"*, and
suppression stays code. What the move should do is make that boundary explicit in the comment, because
right now it reads as an accident rather than a rule.

### L5. `safeRoute` and launch options should cross-reference, not merge

`safeRoute` says *whether* a documented safe route exists (`offline`, `private-server`,
`official-mods`, `none`, `unclear`); launch options say *how to take one*. They are complementary and
should stay separate fields:

- a game can have a documented offline route the launcher cannot automate (College Football today)
- a game can have an automatable option and no ban risk at all (a plain `Internal` exe)

The useful addition is a **validation warning, not an error**: a game with `safeRoute: "offline"` and no
`AntiCheatToggle` option is a curation gap worth reporting in the build summary. It is not wrong — it is
a to-do that is currently invisible.

## What this does not do

**It does not make College Football's anti-cheat bypassable.** The proven Elden Ring technique is a
bootstrapper swap: EAC is started by `start_protected_game.exe`, so putting the real exe in its place
launches the game without it. EA's anti-cheat is a kernel-mode service
(`EAAntiCheat.GameServiceLauncher.exe`), not a bootstrapper the game launches through. Whether any
reversible, file-level technique exists there is **unknown and deliberately not guessed at in this
spec**. What the work does allow is describing the game honestly: ban risk, `safeRoute: "offline"`, and
no toggle — instead of the game being absent.

**It does not add new mechanisms.** No new `LaunchOptionKind`. If a future game needs a different
technique, that is compiled code and its own decision.

**It does not change the Store SKU's behaviour.** Same filter, same absent mechanism, same seal.

## Risks

**A wrong entry renames a file in a game folder.** Contained by: signed feed, the strict filename
validation in L2/section above, backup-first reversibility, and a runtime that throws rather than
guesses when a name is not on disk. Worth stating in the spec because it is the first time manifest
data drives a file operation outside the mod folder.

**Slug-join reliability.** Inherited from the sibling spec; L3 makes launch options depend on it.

**Copy quality moves to data.** `Detail` is a user-facing paragraph, and the Elden Ring one is careful:
it names the trade, names the Seamless exception, and says "fully reversible." A data PR can now write
that badly. The build summary should flag an `AntiCheatToggle` with an empty or very short `Detail`.

## Testing

Core (pure):

- an entry with a `Bootstrapper` containing a separator, `..`, or a drive letter is **rejected**
- an entry with a non-`.exe` `Bootstrapper` or `RealExe` is rejected
- `Exe` / `WorkingSubdir` go through the existing `IsSafeRelativePath` rule
- a Store-flavour build never returns an `AntiCheatToggle` option, even when the manifest supplies one
- `For(gameId)` resolves through `EffectiveManifest` and reflects a remote override
- **migration: the Elden Ring entry as data produces a `LaunchOption` identical to today's compiled one**

Miner:

- a launch option round-trips through the override → manifest → public generator path
- a game with `safeRoute: "offline"` and no toggle is reported in the build summary, not failed

App (smoke, untestable headless):

- the anti-cheat toggle on Elden Ring still reads its state, flips, and reverses after the move

## Open questions

1. **Should `LaunchOption.Detail` be in the manifest at all, or should data carry a key and the binary
   carry the copy?** Data-carried copy cannot be corrected without a feed push, but binary-carried copy
   cannot describe a game the binary has never heard of — which is the whole point. Leaning data, with
   a length/quality check in the build.
2. **Does `NeedsAttention` belong in Core at all** once suppression is a code-side concern? It may be
   better expressed as "the manifest says this game has a recommended option" plus a separate App-side
   "and nothing already satisfies it."
3. **Should the public surfaces publish anything about launch options?** A "needs a launch option" or
   "offline route automated" column is tempting and would need the same care the saves column got — a
   page claiming an automated route the binary cannot take is the failure that column's design memo
   warned about.
