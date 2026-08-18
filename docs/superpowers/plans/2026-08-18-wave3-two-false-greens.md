# Wave 3 — two false greens

**Date:** 2026-08-18 · **Items:** A13, A14
**Why now:** Waves 1 and 2 fixed surfaces that *described* state wrongly. These two fix surfaces that
declare a state **the disk does not support** — and both fail in the direction nobody notices.

## The theme

A false red is noise: the user sees a warning, checks, shrugs. A false **green** is a user who
believes something is done. Both entries here are green when they should not be:

- **A13** — "framework present" when only half the chain is on disk. The game refused to start its
  mods while 626 read `27 of 27 enabled`.
- **A14** — "Mods already installed" over thirteen archives, on a game with no deployed mods at all,
  which the app itself flags as high ban-risk.

Neither is a display bug this time. A13 is a wrong predicate; A14 is a dialog offering an action that
cannot do what its own heading implies.

---

## A13 — a chain is not a choice · `M`

**Observed live** on Este's Windrose, 2026-08-17. `UE4SS.dll` moved aside, `dwmapi.dll` left in place:

| | |
|---|---|
| 626 status line | `27 of 27 enabled` |
| `NEEDS UE4SS` chips | **0** |
| The game, on launch | **"Failed to load UE4SS.dll"** — its own dialog |
| `UE4SS.log` | never written |

Twelve ue4ss-lane mods were dead and the launcher said everything was fine.

**Cause.** `FrameworkDep.DetectRelativePaths` is documented as *"if ANY exists … the framework is
considered present. Multiple paths cover loader variants"*, and `IsAnyPathPresent` implements exactly
that. For UE4SS the two paths are the **runtime** (`Binaries/Win64/ue4ss/UE4SS.dll`) and the **proxy
loader that loads it** (`Binaries/Win64/dwmapi.dll`). The game loads the proxy, the proxy loads the
runtime — a chain, not a choice. OR semantics let either half alone read as present.

### The shape of the fix

The backlog guessed "probably two lists". One list of lists is better, because it expresses both cases
in one structure and the caller never has to know which kind it got:

```
components: [ [ runtime paths… ], [ loader paths… ] ]
             AND across groups, OR within a group
```

A single group reproduces today's behaviour exactly, so entries that are genuinely alternatives need
no special case and no migration.

**The payoff beyond correctness:** a component can be named, so the banner can say *"UE4SS — runtime
present, loader missing"* instead of *"Missing: UE4SS"*. That sentence is the one that would have
ended the live investigation in a second.

### The audit — no entry changes without evidence

Changing OR to AND on a guess trades a dangerous failure for an annoying one, but it still ships a
lie. Every catalog entry gets a decision and a reason:

| Entry | Paths | Call |
|---|---|---|
| **UE4SS** | `ue4ss/UE4SS.dll`, `dwmapi.dll` | **Two components.** Proven live — this is the entry. |
| **BepInEx** | `BepInEx/core/BepInEx.dll`, `winhttp.dll` | Same runtime-plus-proxy shape, **not verified here**. Doorstop also ships under other proxy names, so the loader group is not just `winhttp.dll`. Change only against a real BepInEx install; otherwise record and leave. |
| **Elden Mod Loader** | `dinput8` / `version` / `winhttp` / `ersc.dll` | **Stays OR.** These are real proxy-name variants and the existing comment already records the Seamless Co-op evidence. |
| **Forge or Fabric** | forge, fabric library trees | **Stays OR.** The name says it. |
| **Mod Engine 2** | `modengine2_launcher.exe`, `mod/config_eldenring.toml` | Neither variants nor a chain — an executable and its config. Worth a look, but a toml alone is not ME2. Decide with a real install in hand. |
| **SMAPI** | one path | Nothing to decide. |

Where we cannot evidence a change, the entry keeps today's behaviour and gains a comment saying so.
An unverified entry marked "reviewed, unchanged, here is why" is worth more than a confident edit.

### The half we are deliberately not doing

The backlog also notes that **Refresh did not notice** — the check runs on scan and the file vanished
underneath it. Once detection is right the chip turns red on the next scan, which is most of the
value. A "this game changed underneath you, recheck" banner is a separate surface with its own
triggering question, and folding it in here would double the wave. **Recorded, not scheduled.**

**Tests.** A two-component framework with only the runtime reads missing; with only the loader reads
missing; with both reads present. A single-component entry behaves exactly as today.

**Correction, found while building this.** The line above used to promise that every existing
`FrameworkDepsCheckPresentTests` case would pass unmodified, and called that the proof the change was
additive. One of them did not, and could not: `Ue_pak_with_ue4ss_dll_under_project_subfolder_is_present`
wrote the runtime alone and asserted UE4SS was **not** missing. That assertion is the bug, written
down — a runtime with no proxy beside it is inert, and every ue4ss-lane mod is dead. It was flipped,
renamed, and carries a comment saying what it used to claim and why that was wrong. The other fourteen
passed untouched.

---

## A14 — adopt is not install · `M`

**Observed.** Adding Monster Hunter Wilds raised the adoption dialog against Fluffy's library. Thirteen
individual Nexus downloads in `Games/MonsterHunterWilds/Mods/`, each with Nexus's
`-<modId>-<version>-<timestamp>` filename. **No `natives/` directory exists**, so not one of them is
deployed. Este read this correctly and I did not — I called them another manager's archive store and
advised cancelling. They are the user's own mods, downloaded and waiting.

The dialog is headed **"Mods already installed"** and reads *"These look like mods you already
installed by hand."* On a game the app flags high ban-risk, it told the user mods were live when
nothing was deployed.

**And the words are the smaller half.** `ApplyDiscoveriesAsync` resolves an archive's write keys from
its **contents** — `Scanner.ArchiveModKeysFor` — so an archive whose contents map to nothing installed
expands to **zero writes**. The comment at the call site already says the zero-key case is *the
downloads-folder norm*. So under a heading claiming the mods are installed sits a button reading
*"Adopt 13 mods"* that writes nothing, and the most honest outcome available today is a status line
afterwards explaining that nothing happened.

Adoption attaches metadata to mods that **are** installed. Nothing here is installed, so there is
nothing to attach to. Cancelling did not keep them off the list — accepting would not have put them
on it.

### The fix, in three parts

**1. Say which kind of thing each row is.** The dialog already does this once: a sweep that found only
proxy loaders swaps the blurb, and the `dinput8.dll` row explains that several loaders ship under one
filename. That is the pattern; `DiscoveryKind.Archive` gives us the discriminator for free. An archive
row is *found, downloaded, not deployed* — never *installed by hand*.

**2. Stop counting what adoption cannot write.** `DiscoveryWriteKeysAsync` runs at apply time, so the
dialog cannot know at open which archives resolve to installed keys. Ask earlier: resolving before the
dialog opens lets *"Adopt 13 mods"* become an honest count, and lets a row adoption cannot help say so
next to itself instead of in a status line after the fact.

**The cost, measured rather than guessed.** The scaling dimension is *archives found in one game's
sweep* — not the number of games, and not the number of mods, since an `EngineShaped` candidate needs
no archive read at all. On Este's actual machine, twelve registered games:

| | |
|---|---|
| Games with zero archives | **10 of 12** |
| Windrose | 4 archives, 0.1 MB |
| Monster Hunter Wilds | **13 archives, 41.9 MB**, largest 14.1 MB |

`ArchiveModKeysFor` opens the archive and reads `EntryNames` — the central directory, no decompression.
Measured against those thirteen: **482 entries across all 13, 22 ms.** For comparison, md5-ing the same
thirteen takes **164 ms**, and the propose phase *already does that*, plus a Nexus round-trip per
archive (`Md5Of` then `IdentifyByHashAsync`). The read this part adds is roughly a seventh of a cost
already being paid, before counting the network.

So there is no cost question, and the plan carries no fallback for one. The bounds are already in the
code if a pathological folder ever appears: `DiscoveryMd5TierCap = 25` in-game and
`DownloadsMd5Cap = 100` for the opt-in downloads pass. Even 500 archives would be under a second at
the measured rate. If a cap turns out to be wanted here, it rides those, and it gets logged rather
than silently truncating — a dialog that quietly stopped resolving at row 25 would understate the
count in the same direction this entry is about.

**3. Offer the action that actually helps.** For archives that adoption cannot touch, the user's route
is to install them — which is `AddModsAsync`, the same intake path a drag-and-drop uses and one Este
has already confirmed works end to end. The dialog should offer that rather than leaving the user to
discover it.

### What part 3 uncovers, and why it gates itself

`GateBanRiskEnableAsync` is called from seven places — every toggle, every enable-all, every loadout
apply. **`AddModsAsync` is not one of them.** Intake places files into the mod folder without ever
consulting the ban-risk gate.

If that reading holds, dropping a zip on a high-risk game puts mods live with no warning, and the rule
is explicit that the enable path must warn and take an explicit ack. It is also exactly the game in
this entry.

**So part 3 does not ship until that is settled**, because an Install button on this dialog would
inherit the gap and put it behind a second door. Two honest outcomes:

**Answered, 2026-08-18: intake is ungated.** `Scanner.ExecuteIntake` copies into `primary.Abs` — the
live mod folder — and all four `AddModsAsync` call sites (drop handler, file picker, tools panel,
internal) reach it without a gate call. A dropped zip installs enabled on a high-risk game with no
warning and no ack. Filed as **A22**, which now blocks part 3.

### Also here, and cheap

The sweep offered `Vortex Extension Update - … v0.1.4.zip`. That is a Vortex **extension**, not a game
mod. Sitting in the same folder is not enough to make something a mod, and the filename says what it
is.

### Not in scope

**Fluffy awareness.** 626 knows Vortex's territory (`VortexTakeover`, `taken-over.json`) and does not
know Fluffy's — which is the default manager for every RE Engine game, so this recurs for Monster
Hunter and Resident Evil alike. That is B3's shape, not a copy fix, and it deserves its own design.

**Tests.** Archive proposals are framed as downloaded-not-deployed rather than installed-by-hand; a
mixed sweep frames each row by its own kind rather than by the majority; the approve count never
exceeds what the apply can write; a Vortex extension is not proposed as a mod; an all-loaders sweep
keeps the copy it already has. The App-side dialog is headless-untestable, so the classification
decision lands in Core as a pure function and the dialog reads it — the same split that made the
library-row states testable in Wave 1.

---

## Sequence

A13 first. It is self-contained, it is the one with a live repro, and it is the dangerous one — a
user whose mods are silently dead learns about it from the game, not from us.

A14 then, in its three parts, with part 3 blocked on the intake-gate question. Parts 1 and 2 stand on
their own and are worth shipping even if part 3 waits.

## Done when

- A framework whose chain is half-installed reads missing, and the banner names which half.
- Every catalog entry has a recorded decision, and none was changed on a guess.
- The existing `FrameworkDepsCheckPresentTests` pass unmodified.
- No dialog says "already installed" about a file that is not installed, and no button promises a
  count it will not write.
- The intake ban-risk question is answered with a live check, not a grep.
- Full suite green, `CorePurityTests` green, and the A13 fix verified the way it was found — move
  `UE4SS.dll` aside on Windrose and watch the chips go red.
