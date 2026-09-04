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
installed" is exactly the kind of judgement the manifest is forbidden from making.

**Decided 2026-09-04:** `Recommended` stays a data field meaning *"this game needs a launch option in
principle"*, suppression stays code, and **the two get different names so they cannot be confused**.
Core answers *the manifest lists a recommended option for this game* — a fact about the game. The App
answers *and nothing on this machine already satisfies it* — a fact about this install. Same behaviour
as today; the boundary becomes visible instead of accidental.

### L5. `safeRoute` and launch options should cross-reference, not merge

`safeRoute` says *whether* a documented safe route exists (`offline`, `private-server`,
`official-mods`, `none`, `unclear`); launch options say *how to take one*. They are complementary and
should stay separate fields:

- a game can have a documented offline route the launcher cannot automate (College Football today)
- a game can have an automatable option and no ban risk at all (a plain `Internal` exe)

The useful addition is a **validation warning, not an error**: a game with `safeRoute: "offline"` and no
`AntiCheatToggle` option is a curation gap worth reporting in the build summary. It is not wrong — it is
a to-do that is currently invisible.

## Two safeguards raised separately, assessed here

Both came out of a set of notes on protecting users from tripping EA's anti-cheat. Most of that set is
declined below; these two survive, and they belong in this spec because they are launch-time concerns.

### L6. A pre-flight check before a modded launch — worth doing, and narrower than proposed

The proposal: refuse a modded launch while a store client is running, because a running client is
assumed to be online, and mods plus online is what gets accounts actioned.

The shape is right and the app already works this way elsewhere — `LaunchGuard` gates a vanilla launch
when a required launcher is in force, and the profile restore refuses while the game is running and
fails closed. A launch-time pre-flight is the same idea one step earlier.

**But it must not be "any launcher is running".** `LaunchGuard.NeedsSteamRunning` requires Steam to be
*up* for a `steam://` launch, so a blanket rule would refuse every Steam game the launcher currently
launches correctly. The check is only meaningful for a client whose *running* state implies an online
session the game will join — the EA App case — and it has to be scoped per game, from the same curated
data this spec moves.

The honest limit, which the proposal states plainly and which should reach the user: the app cannot
tell an online client from one already in offline mode. It can only see that the client is running. So
the copy says *"the EA App is running and we cannot tell whether it is offline"*, not *"you are
online"* — the second is a claim the app cannot support.

### L7. A firewall kill-switch — viable off-Store, and only with a marker

Blocking one executable's outbound traffic for the duration of a session is the only network idea in
that set with a small enough blast radius to consider: one exe, one rule, added and removed.

It needs administrator rights, which the app does not have — `runFullTrust` is its only declared
capability and there is no elevation manifest. The proposal's answer is a short-lived elevated helper
(`runas` + `netsh advfirewall`) rather than elevating the shell, which keeps the Store submission's
posture unchanged. That is the right shape, and it makes this **FULL-only**, alongside the anti-cheat
toggle.

**A rule outliving a crash is the real risk**, and the answer is the pattern this app already uses.
The anti-cheat toggle parks the original bootstrapper as `.626off`, and *the presence of that file is
the signal* that something needs undoing. A firewall rule needs the same: write a marker when the rule
goes up, remove it when the rule comes down, and sweep on startup **only when the marker survived**.

That ordering matters for more than tidiness. A sweep that runs unconditionally on every launch
prompts for UAC every time, which trains people to click through the prompt that is supposed to mean
something.

One correction to the proposed sweep, which would not have worked as written:

```
netsh advfirewall firewall delete rule name=all program="626_ModManager_Block_*"
```

`program=` filters by executable path, not by rule name, and `netsh` does not wildcard rule names. The
sweep has to delete by the exact rule name it recorded — which the marker file is the natural place to
keep.

### What is declined, and why

| Proposed | Why not |
|---|---|
| Rewriting the Windows `hosts` file | A global system file. A crash mid-session leaves it modified and the user's EA access broken with nothing saying why. Antivirus flags `hosts` edits routinely. |
| Forcing Steam offline via `registry.vdf` | Steam rewrites that file on exit, so an edit made while it runs is clobbered — the same "changed under a running app is silently undone" trap Palworld's saves taught. It is also global, affecting every other game. |
| Forcing the EA App offline via its config | Same class, same reasons. |
| Disabling the network adapter | System-wide, and catastrophic if the restore does not run. |
| DLL injection to bypass EA's anti-cheat | Genuine circumvention. The Elden Ring toggle declines to *launch* the anti-cheat bootstrapper; it does not defeat a running kernel driver. The Store build has that mechanism sealed out and binary-verified absent, and building an actual bypass is a different category of thing. |

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

## Questions, answered 2026-09-04

1. ~~Should `Detail` live in the manifest, or should the binary carry the copy?~~ **In the feed.** The
   whole point is describing games the binary has never heard of; if the app ships the wording, it can
   only ever explain games somebody already thought about.

   **The quality bar is comprehension, not length.** Este's framing: *"we just need to make sure the
   user understands the launch options."* So the build check is a shape check rather than a character
   count. The Elden Ring copy is the model, and it works because it names three things:

   - **what you gain** — Play launches with mods loaded
   - **what you lose** — official online multiplayer stops working
   - **that it is reversible** — said in those words

   An `AntiCheatToggle` whose `Detail` is missing, or does not carry that shape, is flagged in the
   build summary. A toggle is the most consequential thing this app offers to describe; it does not get
   to ship a one-line explanation.

2. ~~Does `NeedsAttention` belong in Core?~~ **Yes, renamed.** Split so Core states the manifest fact
   and the App applies the machine-specific suppression. Written into L4.

3. **Still open, deliberately: should the public surfaces publish anything about launch options?**
   Decision deferred until we can see how many games actually end up with a toggle.

   *"Needs a launch option"* is a fact about the game and would be honest today. *"Has an automated
   offline route"* is a claim about what the **launcher** can do — and with one game able to deliver
   it, publishing that would advertise a one-game feature as a category. That is precisely the failure
   the saves column was designed against: a page claiming a capability the binary cannot deliver is
   worse than a page that says nothing. Revisit when the curation exists and the width is visible.
