# The smoke list — one pass to settle it

**Date:** 2026-08-18 · **Input:** all 105 catalogue cases, all 417 steps, read end to end
**Output:** what collapses without any work, and seven sittings for what is left

---

## The headline

**The 73 untriaged sections are not 73 pieces of work.** Read in full, they collapse roughly like
this — and the reason matters more than the number, because three of the four groups need a decision
from you rather than a test run:

| | Sections | What to do |
|---|---|---|
| Already settled, recorded elsewhere in the catalogue | ~10 | Mark them duplicates and move on |
| Answer themselves — their own text says PASSED or live-smoked | 4 | Mark verified, quoting their own line |
| The harness runs it today | ~12 | Mark harness-covered; stop asking a person |
| Visual conformance, wave by wave | 19 | One themed pass, not nineteen |
| Real work needing you and a fixture | ~22 | **Seven sittings, below** |
| Fixture we do not have | 2 | Decide: install the fixture, or drop the case |

The single biggest saving is that the prose file and the catalogue's human-only bucket describe the
**same cases twice**. `PR #49 — BND4 file-table walk` and `bnd4-save-walk` are one thing; so are the
two safe-clear sections and `safe-clear-round-trip`, the framework-intake section and
`framework-intake-elm`, the two ban-risk sections and `ban-risk-ack-gate`, and six more. You have
already settled most of those.

---

## Fixture inventory — checked on this machine, not assumed

Four of the "we cannot test this" claims turn out to be wrong, and two turn out to be right. Every
row below was checked on disk.

| Fixture | State | Consequence |
|---|---|---|
| **Vortex-managed game** | **You have one.** Windrose carries three markers: `vortex.deployment.windrose-root.json` at the game root, `R5/vortex.deployment.json`, and `R5/Binaries/Win64/vortex.deployment.windrose-ue4ss.json` | `vortex-takeover` is marked *"needs a Vortex-managed game staged on this box"*. That reason is wrong — the whole section is runnable today |
| **Elden Ring + Seamless + dinput8** | Present | The direct-inject, loader-row and ban-safe-loader cases run |
| **Elden Ring + Mod Engine 2** | **Absent** — no `modengine2_launcher.exe` | Every ME2 branch is blocked. Install ME2 or drop those steps |
| **Windrose + UE4SS** | Present, both halves | UE4SS chip cases and the A13 check run |
| **Monster Hunter Wilds** | Registered, high ban-risk, 13 Fluffy downloads, no `natives/` | A22 and A14 part 3 run here |
| **R.E.P.O. + BepInEx** | **BepInEx absent** | The BepInEx-flavour steps are blocked |
| **Witchfire** | Installed and registered (`Witchfire/Witchfire/Content/Paks`) | The paks-root base-game filter case runs |
| **Marvel Rivals** | Installed, **not registered** | The 2-level UE probe case runs the moment you add it |
| **Death Stranding 2 (Decima)** | **Installed and registered.** Every mod the section names is on disk: `Zipliner_v1.1.asi`, `DollmanMute.asi`, `ShaderToggler.addon64`, `ReShade.ini`, `renodx-deathstranding2.addon64`, `Chiral Clarity.ini`, `OptiScaler.ini`, plus `version.dll` as the proxy | The loose-root section is **fully runnable** — it is the exact fixture it was written against |

---

## Before you run anything: five sections that need no run

Each of these records its own outcome. Mark and close them.

1. **Still-open remediation backlog** — every row reads `✅ shipped` and four say *live-smoked*. It is
   a status table, not a test. **Obsolete.**
2. **2026-05-28 remediation fixes** — Task 1 *PASSED 2026-05-30*, Task 3 *PASSED 2026-05-30*, F3
   *PASSED*. **Three verified in its own text.** Task 2 is the still-open safe-clear refusal, which is
   already tracked as `safe-clear-refusal`.
3. **ReloadModsAsync unification** — its first item is `[x]` with your own words, *"looks the same as
   before"*. The rest is post-refactor regression checking from May, on a code path rewritten several
   times since. **Obsolete.**
4. **Loader visible + independently toggleable** — the remediation table records it live-smoked with
   the cascade deliberately dropped. **Verified.**
5. **Plugin slice B2b-2** — its steps are *"the grep is clean"* and *"the gate is green"*. Those are
   build-time facts the suite already enforces. **Not a smoke case at all.**

---

## The seven sittings

Ordered so each one leaves the machine ready for the next, and so the two with real risk come after
the cheap ones have proved the build is sane.

### 1. Windrose — the richest real install · ~30 min

The one game with years of hand-installed history, 27 mods, UE4SS, and now Vortex markers.

- **UE4SS chip states.** Move `R5/Binaries/Win64/ue4ss/UE4SS.dll` aside → **A13's new behaviour**: the
  banner should read *"UE4SS — loader present, runtime missing"*, not *"Missing: UE4SS"*. Restore it.
  Then move `dwmapi.dll` aside → the mirror sentence. Restore.
- **Your open loose end from the last triage.** You saw the chip go red while the mods kept working,
  which would mean a UE4SS living somewhere else. Worth ten minutes: with the chip red, does the game
  actually load Lua mods? If it does, we have a second UE4SS on that box and the chip is right about
  the one it can see.
- **Vortex takeover — now runnable.** Open Windrose. Expect the owned-folder banner and read-only
  rows. Take them over, confirm the markers are archived under `vortex-takeover/` and the rows become
  managed. **This settles a case the catalogue currently calls impossible.**
- **Tools panel + INI editor.** Drop the WSE zip, confirm the catalog match, then the pencil on a mod
  with `.ini` files → edit → `.bak` lands in `.ini-history/`.
- **Discovery sweep on the richest case.** Run *Find existing mods*, adopt everything, confirm the
  write landed rather than silently doing nothing.

### 2. Monster Hunter Wilds — the two new gates · ~20 min

Everything here is from this week and none of it has been exercised by a person.

- **A22 (6 steps).** Clear `<gameRoot>/_626mods/monster-hunter-wilds/ban-risk-acks.json` first, or you
  will not see the gate. Drop a zip → warning before anything is installed → cancel → *"Nothing was
  installed."* and an untouched folder. Then accept. Then a third drop with no warning. Then ten files
  at once expecting exactly one warning. Then a framework or tool install expecting the same.
- **A14 part 3 (5 steps).** Run *Identify my mods*. Expect the heading *"Mods you've downloaded"*,
  *"Adopt 0 mods"* disabled, *"Install 13 downloads"* enabled. Press Install, expect the ban-risk
  warning, then a normal intake. Re-run the sweep and expect those same archives now offered as
  adoptable.
- **A19/A20 provenance rows** while you are here — the nested-tree intake and the seeded mod folder.

### 3. Elden Ring — the destructive one · ~40 min, needs your explicit go-ahead

Do this when you are willing to have the folder rearranged, and after sitting 1 has shown the build is
healthy.

- **Safe Clear game-running refusal** — the one genuinely open item from the May remediation. Launch
  ER via Seamless, then Settings → Reset → Clear. It must **refuse**, keep the dialog open, and write
  nothing.
- **Safe Clear round-trip** — vanilla clear with a restore point, then restore. Two drives if you can.
- **Safe Clear success confirmation** — the green InfoBar, the friendly timestamp, and the
  restore-point-OFF wording.
- **Ban-safe loaders** — the gate dialog surfacing *Launch Seamless Co-op*. **The Mod Engine 2 branch
  is blocked** until ME2 is installed; skip it or install ME2 first.
- **Direct-inject config** — the Seamless pencil opens the real `SeamlessCoop/ersc_settings.ini`, and
  the CRLF check (edit, save, confirm no bare CR and a byte-exact `.bak`).

### 4. Nexus, live account · ~25 min

- **Endorse round trip** — the one still unconfirmed: fill the heart, verify on the website, empty it,
  verify again. Then endorse on the website and confirm a bulk sync fills it in the launcher.
- **Storefront and detail** — most of this was verified 2026-08-03; what was not is the awkward half:
  rapid filter clicking, *Load more* five times without duplicates, broken thumbnails degrading, and
  badge legibility on Obsidian.
- **Updates surface with Nexus disconnected** — the proof there is no network dependency, and the two
  empty states that must never read as each other.
- **The A10/A11 change while you are here.** The updates view should no longer show `unknown → 1.2.1`
  anywhere, and Faster Ships should be **one** row naming its four files.

### 5. New registrations · ~15 min

Two cases that only need a game added.

- **Marvel Rivals** — installed but not registered. Adding it exercises the 2-level UE probe: engine
  `ue-pak`, mod path under `MarvelGame/Marvel/Content/Paks`, and no Engine-sibling mis-detection.
- **Witchfire** — already registered; confirm the base-game paks stay invisible and the two real mods
  toggle.
- **The duplicate-add guard** — add a registered game a second time and confirm it switches rather
  than creating `windrose-2`.

### 6. Death Stranding 2 — loose root, the mod shape nothing else here has · ~25 min

Every other registered game keeps its mods in a folder. DS2 keeps them **loose in the game root**,
mixed in with the game's own files and the GPU vendor DLLs — which is exactly why this section exists
and why its failure mode is the scary one: listing a game file as a mod, or worse, letting you toggle
one off.

- **Categorised listing.** Open DS2. Expect `ReShade` under SHADERS, `Zipliner` and `DollmanMute` as
  plugins, and `version.dll` recognised as the LOADER the others ride on.
- **Game files stay invisible — the one that matters.** `OptiScaler.ini`, `Chiral Clarity.ini`,
  `Real colors SMRT.ini`, `SDR+.ini`, `PORTER 1.2.ini` and the `sl.*.dll` / `amd_fidelityfx_*` /
  `dlssg_to_fsr3_*` families must NOT be listed. A false positive here is a row that offers to disable
  part of the game.
- **Toggle off is a reversible move.** Toggle one plugin off — its files leave the root for the holding
  folder. Toggle back — byte-identical return.
- **Loader warning.** Toggle `version.dll` off and expect the "this mod is a loader" dialog rather than
  a silent move that takes ReShade down with it.
- **Drop install.** Drop a new `.asi` and confirm it lands in the root, not in a folder that does not
  exist here.
- **Vanilla step-aside.** Play vanilla should move every enabled loose mod aside, loader included, and
  Play modded should bring back exactly the set that was on.

There is also a `_MODS_STAGING` folder in that root. Worth a look while you are there — if 626 is
listing anything out of it, that is a finding.

### 7. One themed pass, replacing nineteen visual sections · ~20 min

The glow waves and road-to-zero sections were per-PR conformance checks, and the later waves re-verified
the earlier ones. What is still worth a human eye, once, across Forge / Obsidian / Matrix:

- Cold start lands on Forge with no navy flash frame.
- The safe-clear primary reads filled danger red **and stays red under the pointer** (the one that
  shipped wrong once, and the reason `.claude/rules/vsm-danger-buttons.md` exists).
- Toggle a mod: the accent halo and its fade; then with Windows animation effects OFF, no motion.
- Import a deliberately low-contrast theme: the warning names the pair and the ratio, and the import
  still succeeds.
- Chip legibility on Obsidian — the tightest pair — for BAN RISK, UPDATE, LOADER and VARIANT.

**Everything else in those nineteen sections is a screenshot question**, and the harness already takes
one per case. That is the cheaper channel and it is already wired.

---

## Permanently human-only, and why

These stay named rather than quietly dropped. The list should never shrink to zero — if it does, it is
likelier someone deleted the awkward entries than that a harness learned to play a game.

- **The ban-risk acknowledgment.** An agent must reach the gate and must never satisfy it. No dev
  bypass, ever.
- **A game actually launched and played** — vanilla versus modded is only true in-engine.
- **A live Nexus account action** — endorse and track write to someone's real account.
- **Steam actually updating a game** — the build-update banner needs Steam to do the thing.
- **Real saves** — the BND4 walk needs a genuine `.co2` with characters in it.

---

## What I recommend dropping

- ~~**Dark Souls II / Decima loose-root**~~ — **withdrawn. I misread "DS2".** The section says DS2 and
  I read Dark Souls II; its own vocabulary says otherwise — *Zipliner*, *Chiral Clarity*, *DollmanMute*
  are Death Stranding mods, and Death Stranding 2 is the Decima game, installed and registered here.
  Nothing to drop: it gets a sitting of its own below.
- **Plugin flavour sealing (B1, B2a, 5c-consumer, delivery UX)** — five sections of STORE-versus-FULL
  checks from June, on a surface that has shipped four plugin versions since. Worth **one** current
  check ("the Store build shows no Nexus surface at all") rather than five historical ones.
- **The May regression sections** — ReloadModsAsync unification and the remediation status tables.
  Superseded by everything since.

---

## Recording the result

Each sitting ends by updating `docs/smoke-tests/smoke.json`: set `status`, and fill `lastVerified`
with the release, the date, and who. `SmokeCatalogueTests` fails the build if anything claims verified
without saying when and by whom, so the record cannot rot the way the checkboxes did.

**The number to watch is the denominator.** The harness prints *"N of 105 catalogue cases were
executed, M still awaiting triage"*. When these seven sittings are done that second number should be
close to zero — not because the cases were run, but because each one was given an answer.
