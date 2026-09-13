# The grand plan: EA SPORTS College Football 27 and Madden NFL 27

*Date: 2026-09-13. Status: plan, revised after a completeness review. Nothing here is built yet. Suggested home: `docs/superpowers/plans/2026-09-13-ea-football-grand-plan.md`. The repository is public, so this document describes save structure only. It carries no personal save data, and none may be added before it lands.*

Every claim in this document carries one of three labels:

- **VERIFIED** means someone read the named file or source, or saw the bytes.
- **INFERRED** means it was reasoned out, and the text says from what.
- **UNKNOWN** means nobody knows yet.

When the research was silent or not confirmed, this plan says so. It does not fill the gap with a guess.

---

## 1. What this is

You want the launcher to handle EA's two football games, and to move players between them offline. There are four sub-projects and one standing workstream:

1. **Register both games.** Both are installed through the EA app. The launcher only reads Steam today, so it can't see them.
2. **Fix the stale-data pain.** An experienced player reports that with today's mod tools, you have to move mods out and clear old data paths by hand before the game will start.
3. **Move one character offline, both directions.** A Road to Glory player goes into Madden, and a Madden player goes back to College Football.
4. **Move whole rosters offline, both directions, with league-level translation.** An NFL roster goes back to college. A college team gets spread across Madden's offline modes. Full roster files are a first-class landing spot, next to franchise saves.
5. **The Superstar workstream (section 8).** Madden's Superstar mode stays a target, with its own dedicated workflow once a Superstar save exists.

Sub-projects 1 and 2 are ordinary launcher work. Sub-projects 3 and 4 depend on facts about the save format that nobody has proven yet, and on a decision only you can make (section 2). A feasibility spike (section 6) sits between them and any code that writes a save. Most of that spike is read-only and can start now.

---

## 2. The decision you need to make first

### Why this comes back to you

You set the boundary as: *fully offline, nothing touches EA's servers, nothing touches the running game or the anti-cheat.* Two verified findings mean that **no route that writes a save and then plays it can satisfy that boundary literally**:

- **The EA app cloud-syncs all five College Football saves** — the three Road to Glory saves, `PROFILE-COLLEGE` and `ROSTER-Official` — to EA's S3 storage. It locks and compares before launch and pushes after exit (VERIFIED from EA's own logs, adversarially confirmed; B1). An edited save reaches EA's servers under your account unless sync is off for that game. The sync also deletes remote files that are no longer present locally (VERIFIED; B11).
- **The game only starts through `EAAntiCheat.GameServiceLauncher.exe`** (VERIFIED; K7). So any edited save is loaded with EA Javelin active, and EA says Javelin may monitor file storage while it runs (VERIFIED; B4).

The first draft of this plan disclosed both facts, then contradicted itself: its boundary section said nothing touches the network or the anti-cheat, while its spike loaded edited saves under Javelin and deliberately pushed a modified save to EA's cloud to watch what happened. That is fixed below. The fix does not choose for you.

### What stays true under every option

Whichever option you pick, the launcher itself:

- writes saves only at rest, with the game, the anti-cheat launcher and the EA app all closed;
- never sends anything to EA, never contacts EA's servers, and takes no account action;
- never starts, attaches to, or probes the anti-cheat — it reads the process list by name, and that's all;
- never tests whether EA can detect an edit;
- never uses an EA-imported character as the source of a transport (section 3);
- never writes EA's import state into a save.

Part A of the spike (section 6) is inside the boundary under every option, and can start now.

### Context, stated fairly

**This isn't new territory for the launcher.** The existing Elden Ring save editor already writes saves that Steam Cloud syncs, and those saves are loaded by a game that runs anti-cheat. So "an edited save reaches the platform's cloud and is loaded by a protected game" is something the launcher already does today. (This is how the shipped feature works; it wasn't re-researched for this plan. K10 also records that the FromSoft editor has no ban-risk check at all.)

**What is genuinely worse here, just as plainly:**

- **EA's User Agreement names modified files as evidence EA may collect** (VERIFIED; B3). It has no exception for offline or single-player play.
- **Javelin is kernel-level anti-cheat**, and it belongs to the same company whose terms name modified files as evidence and whose cloud receives the save. For Elden Ring, the cloud is Valve's.
- **Javelin reportedly blocks mods and trainers in Madden 24–26 offline Franchise** (INFERRED — the forum threads this came from returned 403, so nobody read them in full; B9). If those reports are accurate, EA's anti-cheat cares about what happens in offline Franchise, not only in online play. The reports concern mods and trainers, not saves edited at rest. Whether the same attention extends to save files is UNKNOWN.
- **An EA account holds every EA title you own**, so an enforcement action would not obviously be scoped to one game (INFERRED; EA's enforcement scope wasn't researched).
- **Nobody knows whether EA validates Road to Glory or roster data on its servers**, or has ever acted on an offline save edit (UNKNOWN; B6). No public evidence either way.

### The three options

**Option 1 — accept it knowingly.**

- *What you get:* the whole program, including Part B's load tests and, if they pass, the writers in sub-projects 3 and 4.
- *What it means:* with sync left on, every edited save — including the disposable ones in Part B — is pushed to EA's cloud under your account after the next session (B1). You accept the Terms-of-Service risk with your eyes open, and the UI tells every user the same thing.
- *What stays unknown:* what the sync does when a file changed outside the game (B2), whether Javelin looks at saves (B5), and whether EA ever acts on it (B6). Part B does not try to find out; it just lives with the answer.

**Option 2 — keep saves out of sync first.**

- *What you get:* edited saves don't reach EA's servers, if a user-controlled way to stop cloud sync for these games exists.
- *What it costs:* it's unresearched. Part A item R1 answers it from EA's own documentation, without uploading anything. If no such control exists, this option collapses into option 1 or 3.
- *What it doesn't fix:* the anti-cheat half. With sync off, an edited save is still loaded by a game running Javelin (K7, B4). Option 2 addresses EA's servers, not the anti-cheat.
- *What it leaves open:* EA's cloud keeps an older copy. Turning sync back on later is exactly the conflict case nobody has observed (B2, UNKNOWN). Any launcher disclosure has to say that.

**Option 3 — cap the program at read-only.**

- *What you get:* detection and registration (sub-project 1), the stale-data fix (sub-project 2), and reading and exporting players, characters and rosters from your own saves. Nothing written, so nothing edited ever reaches EA or the anti-cheat.
- *What it costs:* no character or roster transport. Sub-projects 3 and 4 stop at their read-only halves.
- *What it keeps:* every Part A result, so choosing option 3 now doesn't close options 1 or 2 later.

This decision is logged to the 626Labs decisions log when you make it (section 9), including which option and why.

---

## 3. The boundary

**The rule, as you decided it:** you don't fix, defeat, bypass or get around EA's online character import, or any entitlement or online check. You go around it completely.

**The rule, sharpened so it's true.** The launcher writes save files **at rest**, with **the game, the anti-cheat launcher and the EA app closed**. The launcher:

- never itself sends anything to EA or contacts EA's servers;
- never starts, attaches to, or probes the anti-cheat (EA Javelin);
- never touches the running game;
- never takes an account action;
- never tests EA's detection.

What the launcher *can't* promise — because of facts outside it — is decided in section 2: the EA app's own sync may carry a written save to EA (B1), and the game loads it under Javelin (K7, B4).

### The source rule

**Never use an EA-imported character as the source of an offline transport.** A Madden player created by EA's online College-to-Madden import carries a `CollegeRtgImportInfo` record with `IsImported` set (VERIFIED against public schema 620; X7a). Reading that player out of a Madden save and moving it into an offline mode would undo the exact online-only restriction you described as the pain point. That's defeating EA's entitlement, which this boundary routes around rather than undoes.

The legitimate source is always **the original game's native save**:

- a Road to Glory character read from the College Football save; or
- a Madden player that was not created by EA's import.

How to tell an imported player apart on disk is not yet known (R3). Until it is, the launcher refuses Madden-side character sources that it can't positively show were not imported.

**The companion rule, unchanged from the draft.** The launcher never writes EA's import state into a save: `CollegeRtgImportInfo`, `IsImported` or `RtgImportNarrativeTags`. It writes ordinary player data. It never imitates the output of EA's online import.

### How this compares with EA's import

**What the launcher route avoids:**

- The launcher itself never touches the process, the anti-cheat or the network. EA's import is an online, account-bound transfer (INFERRED from press; EA's help pages timed out).
- Every write takes a snapshot first and can be undone locally (the reversibility law). Section 6 covers what "undone" means once the cloud has a copy.
- It does not add to the circumvention problem. Every existing asset-modding route for these games replaces EA's anti-cheat launcher (VERIFIED; P1). This route doesn't go near it.

**What it can do that EA's import can't:**

- **The edit is made offline, and the destination can be an offline mode.** EA's import needs an internet connection in both games (INFERRED from secondary press — Gamerant, Operation Sports; EA's help pages timed out). The imported character can't be used offline (your report).
- **It can carry the whole player.** EA's import lands in Madden as a `CollegeRtgImportInfo` record holding one overall number (`CollegeOVR`), position, stats, awards and `IsImported`. It holds **no attribute ratings** (VERIFIED against public schema 620). EA rebuilds the pro player (INFERRED). A save-level transfer can carry the full rating set, dev trait and identity.
- **It goes both directions, and it handles rosters.** EA's import does neither.

**What the boundary does not buy you. Say this plainly to anyone who uses the feature.**

- **The edit is made offline. It doesn't stay offline** unless sync is off (section 2).
- **It isn't something EA permits.** EA's User Agreement (updated 2026-05-14) has no offline or single-player exception, and names modified files as evidence (VERIFIED). This is a Terms-of-Service risk the user accepts. The launcher discloses it and never calls it "safe".
- **"Game closed" doesn't make an edit invisible.** Javelin runs only while a protected game runs, and EA says that while it runs it may monitor "file storage" (VERIFIED). Whether it looks at save files is UNKNOWN.

---

## 4. What we know and what we don't

This is the most important section after the decision. The first two tables settle sub-projects 1 and 2. The rest are what sub-projects 3 and 4 stand or fall on.

### Detection and registration

| # | Claim | Status | Source or reason |
|---|---|---|---|
| K1 | College Football 27 writes `HKLM\SOFTWARE\[WOW6432Node\]EA Sports\EA SPORTS College Football 27`, holding `Install Dir`, `DisplayName`, `Locale` and `Product GUID`. | VERIFIED | Registry read, read-only |
| K2 | The registry parent differs by title: `EA Sports` for these two, `EA Games` for others. The reliable pointer is the registry path written inside `installerdata.xml`. | VERIFIED | Registry enumeration; Madden's `installerdata.xml` |
| K3 | `<install>\__Installer\installerdata.xml` holds contentID, title, gameVersion and launcher paths. College Football 27: contentID 16425899, version 1.0.140.17622. Madden NFL 27: contentID 16425895, version 1.0.139.61898. | VERIFIED | Read from both installs |
| K4 | Madden's install state changed during the research. One read found every file with a `_DiP_Staged` suffix and no registry key. Two later reads found the `EA Sports\Madden NFL 27` key and `installerdata.xml` in place. | VERIFIED, but the reads conflict in time | Folder and registry reads at different moments |
| K5 | "Installed" means: the registry key has `Install Dir`, that folder exists, and `installerdata.xml` exists without the `_DiP_Staged` suffix. | INFERRED | Rests on **one** completed install compared with one in-progress install, and the Madden reads conflict (K4). Re-check on the finished Madden before relying on it. |
| K6 | `C:\ProgramData\EA Desktop\InstallData` is not a list of installed games. Battlefield 6 Event is listed there but isn't installed. | VERIFIED | Folder listing plus registry |
| K7 | The official entry point is `EAAntiCheat.GameServiceLauncher.exe`, not `CollegeFB27.exe` or `Madden27.exe`. | VERIFIED | `installerdata.xml` launcher entries |
| K8 | `BanRiskCatalog` resolves ban risk **only** by Steam app id, and a missing id resolves to `GameBanRisk.None`. Registering the EA install *with* a Steam id makes `EnginePresets` set `LaunchUrl = steam://rungameid/...`, which launches through the wrong store. | VERIFIED (confirmed) | `BanRiskCatalog.cs:34-48`, `EnginePresets.cs:90-94` |
| K9 | The embedded manifest has no `banRisk` data at all. Ban risk exists only in the remote feed. | VERIFIED (confirmed) | `src/ModManager.Core/Manifest/games-manifest.json` |
| K10 | The ban-risk gate covers only paths that enable mods. The FromSoft save editor has no ban-risk check. | VERIFIED (confirmed) | `MainViewModel.cs:1311`, `AgentWriteRules.cs:61`, `SaveEditorService.cs` |
| K11 | College Football 27 is curated with `banRisk: high`, `safeRoute: offline` and Steam id `4032350`. | VERIFIED | `626-game-manifest/overrides/ea-sports-college-football-27.json` |
| K12 | Steam id 4032350 really belongs to College Football 27. | UNKNOWN | Not checked against the Steam store |
| K13 | Madden NFL 27 has no curated entry. The miner draft has Steam id 3940610 with `banRisk: null`. It is missing from the published feed. | VERIFIED (confirmed) | `tools/ManifestMiner/out/manifest-draft.json`; feed repo |
| K14 | Steam id 3940610 really belongs to Madden NFL 27. | UNKNOWN | Mined data only |
| K15 | Only `SteamService` implements `IStoreLibrary`. `MainWindow`, `GameDefinitionResolver`, `MainViewModel` and `AddGameDialog` call Steam directly or key on the Steam id. | VERIFIED | Grep of call sites |
| K16 | `SaveManager.ListSaveFiles` filters by file extension. The College Football saves have no extension, so the saves panel would list nothing. | VERIFIED | `SaveManager.cs:24-33` |
| K17 | For a game under `C:\Program Files\EA Games`, the launcher's data folder resolves inside that folder, and `BUILTIN\Users` only has read/execute there. | VERIFIED (ACL). The write failure itself wasn't tested. | `Scanner.cs:39`, `icacls` |
| K18 | Build-change detection only works for Steam. EA games always resolve to Unknown. `gameVersion` in `installerdata.xml` could serve as the EA build id. | VERIFIED (current behavior). The EA use is INFERRED. | `SteamBuildCheck.cs` |
| K19 | The contentID stays the same across patches, regions and editions. | UNKNOWN | Stable within one build, one locale |
| K20 | How to ask the EA app to launch a specific game — a launch URI (`origin2`/`link2ea` were named), the contentID as a launch key, or anything else. | UNKNOWN | Out of scope for the research. No launch mechanism was researched. |

### The stale-data pain

| # | Claim | Status | Source or reason |
|---|---|---|---|
| P1 | Both current mod toolchains replace `EAAntiCheat.GameServiceLauncher.exe` with a community fake. These are MMC Frosty Modding Tools and the FMT Madden26Plugin. FMT also drops `CryptBase.dll` and `dpapi.dll` into the game folder. | VERIFIED | MMC README; FMT source (`CFB27AssetCompiler.cs`, cleanup functions) |
| P2 | FMT backs up the real anti-cheat exe only when no `.backup` exists. After an EA update, a later cleanup can restore the *old* exe. | INFERRED | Read from FMT source. Not observed on a machine. |
| P3 | The pain is some mix of: a stale `ModData` folder in the game root, the `C:\ProgramData\Frostbite\<game>\LCU` cache, leftover proxy DLLs, a stale anti-cheat backup, and a machine-wide `GAME_DATA_DIR` variable. | INFERRED | Community guides and Frosty source. **Which of these your friend meant is not established.** |
| P4 | Your College Football install is clean of mod residue today, and `GAME_DATA_DIR` is not set. | VERIFIED | Folder listing, read of environment variables |
| P5 | An LCU cache exists for College Football 27. It was written 10 minutes after the Sep 10 title update. | VERIFIED | Read-only listing |
| P6 | A vanilla launch actually fails when that residue is present, and the EA app's Repair fixes it. | UNKNOWN | Can't be observed without launching the game |
| P7 | Whether the EA app re-downloads the LCU cache from EA's servers if it's moved aside, and whether moving it breaks anything. | UNKNOWN | Not researched |

### Save format (College Football 27)

| # | Claim | Status | Source or reason |
|---|---|---|---|
| S1 | Every save is an `FBCHUNKS` container: a small plaintext header, one zlib stream, a fixed total size. None are encrypted. | VERIFIED | Bytes of the copies; entropy measured |
| S2 | RTG (Road to Glory) saves inflate to a 31 MB `FrTk` table database of about 2,455 tables. The tables include Player (17,500 slots), CharacterVisuals, Coach, Team and Recruit. The character's name is stored in the Player table's string area. | VERIFIED | Decoded copies |
| S3 | The RTG compressed length is a u32 at `0x4A`. The zlib stream starts at `0x52`. The file is a fixed 9,646,981 bytes, which is `0x52` plus a stream capacity of `0x933333`. | VERIFIED (confirmed) | Header bytes match the measured stream. **Superseded reports:** the architecture-fit report placed the length at `0x42`, and the format-community report called it a u24 that `madden-franchise` patches as 3 bytes. The adversarial C2 check settled it as a u32 at `0x4A`. The two readings produce identical bytes for any stream that fits, because the capacity `0x933333` is below `0x1000000`, so the top byte is always zero. The writer patches the full u32. |
| S4 | The three save kinds are laid out differently. PROFILE: zlib at `0x46`, decompressed length at `0x3E`, no compressed-length field. ROSTER: zlib at `0x4A`, decompressed length at `0x12`, CRC at `0x1A`, no compressed-length field. "Patch `0x4A`" applies to RTG (and dynasty) only. | VERIFIED (confirmed, as a correction) | Adversarial re-check |
| S5 | The ROSTER u32 at `0x1A` is **CRC-32/BZIP2** (MSB-first, polynomial `0x04C11DB7`, init and xorout `0xFFFFFFFF`, unreflected) over the uncompressed data. It does not match standard reflected CRC-32, CRC-32/MPEG-2, or any CRC over the compressed bytes. | VERIFIED (confirmed) on one unmodified official roster | Computed and matched. **Superseded:** the save-forensics report had called this field unknown; the format-community match replaces that. |
| S5a | The FMT plugin authors reported edited rosters being rejected, and called this field "not CRC32". The most likely reason is that they recomputed the common reflected CRC-32 instead of the MSB-first BZIP2 variant. | INFERRED | Their "not CRC32" fits exactly what S5 found: it isn't the reflected variant. No second hash was found in the local FMT copies. |
| S6 | RTG saves carry no app-level checksum, HMAC or signature beyond zlib's own Adler-32. | INFERRED, strongly | All header fields accounted for; no hash in table headers. The 2-byte timestamp diff only rules out a hash over the header. The public writer computing none is weaker evidence than it looks: eric-levinson's notes say that same writer corrupts CFB27 saves (INFERRED, not reproduced). |
| S7 | The ~2.8 MB after the RTG zlib stream is **not padding**. It is u16-length-prefixed zstd frames separated by zeros. PROFILE and ROSTER tails are all zeros. | VERIFIED | Bytes |
| S7a | In the one RTG copy measured, at least one non-zero tail frame starts about 430 bytes past the end of the zlib stream. So a re-deflated stream that grows by more than that would overwrite tail data if the tail stays at its absolute offsets. | VERIFIED on one copy (arithmetic on two verified offsets; not necessarily the first frame) | C2 check offsets |
| S8 | Whether the game reads that tail, and whether it checks position there. | UNKNOWN | The two public writers lay it out incompatibly: `madden-franchise` keeps tail bytes at their absolute offsets (overwriting the tail's front when the stream grows); DaiyronW shifts the tail and pads or trims trailing zeros. Both reportedly load. |
| S8a | Compressor output size matters. The research reports that Node's zlib output overruns CFB27 saves and that libdeflate level 12 is needed to fit. | INFERRED | Community reports, not reproduced. **.NET's built-in deflate output size against this capacity is unmeasured.** |
| S9 | The game accepts a re-packed save (unchanged or edited), or a roster with a recomputed CRC. | **UNKNOWN / untested** | FMT's rejected edited rosters are explained by S5a (INFERRED). DaiyronW reports a save 175,232 bytes short hanging on load. |
| S10 | ROSTER-Official is a different format from the FrTk saves: a **TDB2** database of **11,730 player records** with EA's classic 4-letter field codes (PFNA, PLNA, POVR, PSPD, PPOS, TGID...), plus small gzip blobs holding each player's look. Roster transport and character transport need separate converters. | VERIFIED | Decoded copy (official EA roster data) |
| S10a | This is the same roster layout Madden has used since Madden 21. `bep713/madden-file-tools` (MIT), through its `MaddenRosterHelper`, already reads and writes it. Its header layout — year at `0x16`, zlib at `0x4A`, uncompressed length at `0x12`, CRC at `0x1A` — matches the ROSTER-Official copy exactly, and it writes CRC-32/BZIP2. | VERIFIED (header match and CRC algorithm); "since Madden 21" is the library's own description | format-community report |
| S11 | PROFILE-COLLEGE holds a save-slot index with a pipe-separated summary line per career. Its fields appear to include the slot's file name, a numeric id, the team, the character's name, the week, and a number that looks like an overall rating. **Any character write has to keep this consistent.** A new RTG file probably needs a profile entry to show in the menu. | VERIFIED (index and field layout). Field meanings and the menu dependency are INFERRED. Whether the game rebuilds the summary from the RTG file or trusts the profile is UNKNOWN. | Decoded copy |
| S12 | CharacterVisuals frames use zstd dictionary ID 1711034507. That matches `data/zstd-dicts/c27/dict.bin` in `bep713/madden-franchise`. | VERIFIED (header ID match only) | Frame headers; first bytes of the dictionary |
| S13 | Those frames actually decompress with that dictionary. | UNKNOWN | No decoder was installed or run |
| S14 | Your RTG saves declare schema major 833. The public College schema is 468. Earlier community notes saw 809. | VERIFIED | Header `0x3E` |
| S15 | For the **College Player table**, schema 468's layout still fits your 833 build. The save's own table header declares 288 fields, the same count as 468. Offsets match field widths for 276 of 283 checkable fields (a random baseline scores 46–69); the 7 misses are computed fields, one location field and end-of-record padding. Decoded values fall in plausible ranges for overall, height, age, position, jersey number and class year across every in-use record in all three RTG copies. | VERIFIED (confirmed) | Adversarial decode (C3) |
| S16 | Field **meanings** survived too. Same-width renames would not show up in offsets. | UNKNOWN | Needs ground truth from the game UI |

### Cross-game and Madden

| # | Claim | Status | Source or reason |
|---|---|---|---|
| X1 | Public schemas show the two games share 255 Player fields, including the core gameplay ratings. `Position`, `PlayerType` and `TraitDevelopment` enums are identical. Not every rating is shared: `ProspectStarRating` is College-only, and 56 `Original*Rating` fields plus `PersonalityRating` are Madden-only. | VERIFIED (confirmed) against public schemas 468 and 620 | Schema diff, with the C3 corrections |
| X2 | 8 of those 255 shared fields differ in type or range: `ConfidenceRating` (0–127 vs 0–99), `TeamIndex` (0–255 vs 0–32), `PrevTeamIndex` (0–255 vs 0–32), `PLYR_PREVTEAMID` (11 vs 10 bits), `SkillPoints` (0–16384 vs 0–1000000), `GenericHeadAssetName` (max length 33 vs 26), `PLYR_HOME_STATE` (StateName vs PlaceName), `Role` (FranchiseRole vs BTCareerPersonaRole). A translator must clamp or re-map them, never copy. | VERIFIED (confirmed) | Adversarial re-check |
| X3 | `TraitDevelopment` gives `College_Impact`/`College_Star`/`College_Elite` the same integers (1/2/3) as `Star`/`Superstar`/`XFactor`. A raw copy turns every College Elite into an X-Factor. | VERIFIED (confirmed) | Schema enums |
| X4 | Madden 27 saves use the same `FBCHUNKS`/zlib/`FrTk` family. | INFERRED, on firm ground | The public library claims full Madden 27 support and runs both games through one code path. **No Madden 27 save has been examined.** |
| X5 | Schema 620 fits your installed Madden build. | UNKNOWN | No Madden save exists yet |
| X6 | `Documents\Madden NFL 27` does not exist yet. | VERIFIED at the time of research | `Test-Path` |
| X7 | EA's import lands only in Superstar. | INFERRED | Press |
| X7a | `CollegeRtgImportInfo` holds `CollegeOVR`, `CollegePosition`, `CollegePositionName`, stats and awards arrays, and `IsImported`. It hangs off `FranchiseUser.CollegeImportInfo`, next to Superstar fields. | VERIFIED | Public schema 620 |
| X8 | Superstar needs to be online. | INFERRED | Search excerpt of an EA help page that timed out. Section 8 has the free test that settles it. |
| X9 | Madden 27 Superstar creation offers 8 positions: QB, HB, WR, TE, LB, CB, EDGE, FS. | VERIFIED (press) | Operation Sports, ClutchPoints |
| X10 | Some offline Madden mode (Franchise player career, or a draft class) accepts a player row written at rest, either overwritten or inserted. | UNKNOWN | The Madden schema has draft-class import machinery (VERIFIED). Whether it works offline is UNKNOWN. |
| X11 | Madden recomputes `OverallRating` on load, or trusts the stored value. | UNKNOWN | |
| X12 | There is a published formula for rescaling college ratings to pro ratings. | UNKNOWN; none found | |
| X13 | CharacterVisuals (appearance) moves between the two titles byte-for-byte. | UNKNOWN | Madden uses a different zstd dictionary (VERIFIED) |
| X14 | What Madden's `Original*Rating` fields represent and when they refresh. | UNKNOWN | Named by the roster-semantics research; no source |
| X15 | Which other tables point at a Madden Player row — Team, DepthChart, DraftPlayer, contract rows — and what breaks if an overwrite leaves them inconsistent. | UNKNOWN | Flagged by the research; nothing examined |
| X16 | Which offline Madden 27 modes can start from a saved custom roster file. | UNKNOWN | Not researched. A free look at the setup menus answers it (Band B). |
| X17 | Whether a roster downloaded through EA's in-game roster share lands on disk as an ordinary `ROSTER-*` file. | UNKNOWN | Open question in the research |
| X18 | Whether Superstar keeps a local save file at all, if it is online-only. | UNKNOWN | No Superstar save exists |

### Boundary and policy

| # | Claim | Status | Source or reason |
|---|---|---|---|
| B1 | The EA app cloud-syncs 5 College Football files: the three RTG saves, PROFILE-COLLEGE and ROSTER-Official. It does not sync UserSettings.dat. It locks and compares before launch, and pushes to EA's S3 storage after exit. A new RTG slot would probably join the set, since every existing RTG file is in it (INFERRED). | VERIFIED (confirmed) | `EADesktop.log`, `EADesktopVerbose.log`, `cloudsync\16425899_16425899.lastsync` |
| B2 | What the sync does when a file changed outside the game (push local, pull cloud, or prompt), and what it does in EA app offline mode and on reconnect. | UNKNOWN | Only the "identical" and "push after game" branches appear in the logs |
| B3 | EA's User Agreement has no offline or single-player exception, and names modified files as collectable evidence. | VERIFIED | ea.com/legal/user-agreement |
| B4 | Javelin runs only while a protected game runs, and may then monitor file storage. | VERIFIED | User Agreement 7.C; EA anti-cheat progress report |
| B5 | Javelin inspects save files. | UNKNOWN | |
| B6 | EA validates Road to Glory or roster data on its servers, or has banned anyone for offline save edits. | UNKNOWN | No public evidence either way |
| B7 | Microsoft Store policy has no save-editor or anti-cheat clause. The exposure is 10.1.1 (accurate description), 11.2 (third-party IP) and the reviewer-letter promise. | VERIFIED (policy text). The certification outcome is UNKNOWN. | learn.microsoft.com store policies 7.19 |
| B8 | License status of the public sources. `bep713/madden-franchise`: MIT in `package.json`, no LICENSE file. `madden-file-tools`: MIT. `DaiyronW/tool` and `eric-levinson/cfb27-dynasty-modding`: no license. FMT plugin: NOASSERTION. Redistribution terms for the zstd dictionaries: UNKNOWN. | VERIFIED (as listed) | GitHub API |
| B9 | Javelin blocks mods and trainers in Madden 24–26 offline Franchise. | INFERRED | Boundary-policy research; the forum threads returned 403 |
| B10 | The EA app exposes a user-facing per-game cloud-save toggle, and what it does. | UNKNOWN | Unresearched. Part A item R1. |
| B11 | After pushing, the sync deletes remote files that are no longer present locally. | VERIFIED | "Deleting unused files from remote" log lines with S3 DELETE returning 204 |
| B12 | A signal in the files that tells an online-connected save (online dynasty, cloud franchise) from an offline one. | UNKNOWN | Every College Football save is cloud-synced (B1), so "synced" is not that signal. Part A item R2. |

---

## 5. Prerequisites

Owners:

- **Owner** is you.
- **Friend** is the player who reported the pain.
- **Agent** means read-only research work.
- **Code** means a branch with tests.

Do them roughly in this order. Rows in the same band can run side by side. Screenshots and save copies stay in the scratchpad and never enter the repository.

### Band A: now, no code

| # | What | Owner | Why |
|---|---|---|---|
| A1 | Back up your whole `Documents\EA SPORTS College Football 27` folder to a place the EA app doesn't sync (an external drive or a zip outside Documents). | Owner | A local way back. **It is not independent of the cloud:** restoring it after the EA app has pushed newer saves leaves local older than cloud, which is the unobserved conflict case (B2). Section 6 covers cleanup. |
| A2 | Ask your friend the stale-data questions in section 12 (friend questions 1–4). | Owner → Friend | P3 is inferred. Sub-project 2 can't be designed against a guess. |
| A3 | Decide how ban risk resolves for games with no Steam id: by manifest id (recommended), by an EA id, or both. | Owner | K8. It blocks sub-project 1. |
| A4 | Decide the EA store key: `installerdata.xml` contentID (recommended, INFERRED to be the most stable local signal) or the `Origin.SFT.50.xxxxxxx` offer id. | Owner | K3, K19. The schema, `EffectiveManifest.MergeStores` and the `EveryOptionalFieldMerges` test must change together. |
| A5 | Check that Steam ids 4032350 (College Football 27) and 3940610 (Madden NFL 27) match their Steam store pages. | Agent | K12, K14. The existing ban-risk flag depends on 4032350. |
| A6 | License review of every public source (B8), and a written clean-room rule: unlicensed repos are for study only, nothing gets copied, and the dictionaries and schema files don't ship unless the terms allow it. | Agent, Owner signs off | NOTICE and never-bundle laws. |
| A7 | From EA's own documentation only: does the EA app expose a user-facing per-game cloud-save toggle, what does it do, and what do EA's docs say offline mode and reconnect do to cloud saves? | Agent | B2, B10. This is Part A item R1, listed here because it can start today and option 2 depends on it. |
| A8 | From EA's own documentation and the local `installerdata.xml` only: how does the EA app launch a specific game? | Agent | K20. Sub-project 1 needs a launch path that only invokes the EA app. No launching during research. |

### Band B: once Madden 27 finishes installing

| # | What | Owner | Why |
|---|---|---|---|
| B1 | Re-check the finished Madden install read-only: `EA Sports\Madden NFL 27` key, `installerdata.xml` without the `_DiP_Staged` suffix, an Uninstall GUID key, contentID still 16425895, the runtime exe name. | Agent | K4, K5. This is the second completed example the install-state rule needs. |
| B2 | **Create the Madden saves listed below.** Note anything that forced you online. | Owner | X4–X10, X14–X17. The Madden side has zero observed bytes. |
| B3 | After Madden has been played and closed once, read the EA app logs read-only to record Madden's synced file set and its save folder. | Agent | B1 applies to Madden too. UNKNOWN until observed. |
| B4 | While Madden is running, note its process name in Task Manager. | Owner | The fail-closed "game is running" check reads the process list for it. |
| B5 | With the game closed, copy all new Madden saves and rosters into `scratchpad\madden27-saves-copy\`, read-only, with a SHA-256 match against each original. | Agent, with your go-ahead | Byte work happens on copies only. |

**Which Madden saves to create.** Use clear slot names so the copies can be matched to what you did. Play only offline modes. Don't touch Ultimate Team or online franchise. This is ordinary play of your own game; nothing here is edited outside it.

1. **`626-FR-PLAYER-QB`**: an offline Franchise, **player career**, as a created QB. Save right after the league is created, before playing a game.
2. **`626-FR-COACH-START`**: an offline Franchise, **coach career**. Save right after league creation.
3. **`626-FR-COACH-WK2`**: continue save 2 into the regular season and save at week 2. Two points in one league show which tables move with progression, and give R6 a before-and-after for `Original*Rating`, contract and `YearsPro`.
4. **`626-FR-COACH-WK2-JERSEY`**: from save 3, change **one** thing, one player's jersey number, and save to a new slot. This one-change pair pins the Player record and field layout without trusting the public schema.
5. **A saved official roster.** Save the official roster, or save a copy of it from the roster menu, so a Madden `ROSTER-*` file exists.
6. **A custom roster after one trivial edit.** From that roster, change one thing (one player's jersey number) and save it under a new roster name. This is the roster one-change pair.
7. **If possible, download a shared roster** through the game's own roster-share menu, to learn whether it lands on disk as an ordinary roster file (X17). That's EA's feature and your own choice, and it goes through EA's servers. Don't upload anything.
8. **Look at the setup screens** for offline Franchise and Play Now, and note which let you pick a saved roster (X16). Looking is enough.
9. **Screenshots** of the player you created in save 1: ratings page, dev trait, abilities, archetype.

Superstar saves are not in this list anymore. They have their own workstream and capture order (section 8).

**Also on the College Football side** (Owner, offline, game closed before copying):

10. **Screenshots** of your Road to Glory character in your most recent RTG autosave: full ratings page, dev trait, abilities, class year, redshirt status, archetype. This checks S16.
11. **A one-change RTG pair.** Load an RTG save, change exactly one visible thing (one attribute point or the jersey number), and save to a **new** slot. Then have the copy made the same way as B5. This pair also feeds R4: it shows which PROFILE-COLLEGE summary fields move when the RTG file changes.

### Band C: code that can start right away, no dependency on the spike

| # | What | Owner | Why |
|---|---|---|---|
| C1 | Write a failing test that shows the gap: register College Football 27 with `SteamAppId = null`, apply the cached feed, and assert risk is `None` and the enable gate lets it through. Then fix it. | Code | Turns K8 from reasoning into an observed failure. |
| C2 | Make ban-risk resolution aware of the whole `GameEntry` (Steam id, then manifest slug, then EA id) at every call site: `MainViewModel`, `LibraryViewModel`, `WriteTools` and the state chip. | Code | K8, K10. |
| C3 | Compile in a ban-risk **floor** for these two titles (High) that the feed can raise but never lower. | Code | K9. An offline first run or a failed feed fetch would otherwise read as None. |
| C4 | Curate a Madden NFL 27 override in `626-game-manifest` (`banRisk: high`, `safeRoute: offline`, `saveDirHint` once B3 has observed the folder), and add a College Football `saveDirHint`. This is a data PR in that repo, not a hand edit here. | Owner (approves), Agent (drafts) | K13. Without it the law can't protect Madden. |
| C5 | Build a synthetic `FBCHUNKS` fixture generator (the analog of `FromSoftFixture`), plus a synthetic TDB2 roster fixture, plus an opt-in test that reads real copies from an environment-variable path and skips when that path is absent. | Code | Real EA saves can never be committed. |

### Band D: decisions that must land before any save-writing code merges

| # | What | Owner | Why |
|---|---|---|---|
| D1 | Should save transport ship in the Store build, or only in the FULL build like the anti-cheat toggle? | Owner | B7. Cheap to decide now, expensive to change later. |
| D2 | Is the save-write acknowledgement separate from the mod-enable acknowledgement? (Recommended: separate. It's a different act with a different risk.) | Owner | K10. |
| D3 | May the launcher delete or move third-party residue (`ModData`, proxy DLLs, the LCU cache), or only detect it and point at EA's Repair? | Owner | Sub-project 2, P1–P7. |
| D4 | **The section 2 decision: option 1, 2 or 3.** It must land before Part B of the spike runs and before any writer merges. | Owner | B1, B3, B4, B9, K7. |
| D5 | Where field names come from if A6 says the public schema files can't be used. See the options under Part A item R8. | Owner | S14–S16. Blocks shipping a reader with named fields, not the research. |

The draft's D4 — a secondary EA account for the load tests — is removed. It is an account action, and a second account would need a purchase or subscription to own these games, which is itself an entitlement action.

---

## 6. The spike

The spike decides whether sub-projects 3 and 4 get built as writers at all. It has two parts.

- **Part A is read-only and inside the boundary under every option.** It runs on copies in the scratchpad and in Core tests. It can start now.
- **Part B is the write-and-load tests.** It runs only if you choose option 1 or 2 in section 2. Only you run it. No agent launches either game, and no agent writes to a real save.

### Part A — read-only, starts now

| # | Question | Method | Owner | Done when |
|---|---|---|---|---|
| Q1 | Do Madden 27 saves use the same `FBCHUNKS` → zlib → `FrTk` container? | Parse the B5 copies. Read the header, schema major at `0x3E`, table count and the Player table header. | Agent | Same container family, tables parse, Player table found. |
| Q2 | Does the Madden Player layout match public schema 620, the way College matched 468 (S15)? | The same width-equals-gap test and plausible-value decode used in C3, then checked against the save-1 screenshots and the save 3/4 one-change pair. | Agent | Width matches far above the random baseline, and the screenshot values are found in the decoded record. |
| Q3 | Do College field meanings match 468 in your 833 build? | Decode your RTG character and compare with the screenshots. Diff the one-change RTG pair. | Agent | Every screenshotted rating, dev trait and class year decodes correctly. The one-change diff lands on the expected field. |
| Q4 | Can the launcher build a well-formed container, in Core, without the game? | A Core round trip on copies for all three save kinds: inflate, re-deflate, patch the right field for that kind (the u32 at `0x4A` for RTG; the decompressed length for PROFILE and ROSTER if it changes), keep the exact file size, re-inflate, compare decoded tables. For rosters, recompute CRC-32/BZIP2 and check it matches the stored value on the unchanged copy. **Also measure:** .NET's built-in deflate output size against capacity (S8a), and the slack between the stream end and the first non-zero tail byte across every RTG copy (S7a), before and after a one-field edit. | Code | Decoded content is identical after the round trip for all three kinds; the recomputed roster CRC equals the stored one; and there is a measured answer for whether .NET's deflate fits inside the slack. |
| Q8 | Can appearance be decoded? | Get a .NET zstd decoder (a vetted NuGet package, license-checked). In a test, decode one CharacterVisuals slot with the c27 dictionary, only if A6 allowed its use. | Code | Frames decode. Not a go condition: appearance can be deferred. |
| R1 | Does the EA app expose a user-facing per-game cloud-save toggle, and what does it do? | EA's own documentation only (help pages, EA app release notes). Also what EA's docs say offline mode and reconnect do to cloud saves. **Answering it must not require uploading a modified file, or changing a setting to see what happens.** If the docs are silent, it stays UNKNOWN. | Agent | A cited answer, or a recorded UNKNOWN. |
| R2 | What tells an online-connected save from an offline one? | Compare save kinds on the copies: file names, header fields, and any table that names a mode or a server league. | Agent | A positive signal for each save kind the writer would target, or a recorded UNKNOWN (in which case the writer refuses everything it can't positively identify). |
| R3 | How do you tell an EA-imported Madden character from one that wasn't? | Needs the section 8 captures (1) and (2). Diff them: where `CollegeRtgImportInfo` / `IsImported` sits, and how it links to the Player row. Structure only — never read ratings out of the imported character for any purpose but locating fields. | Agent | A signal the source guard can check. Until then, Madden-side character sources are refused unless positively non-imported. |
| R4 | Which PROFILE-COLLEGE summary fields mirror RTG contents? | Diff the one-change RTG pair against the matching PROFILE-COLLEGE copies. | Agent | A list of mirrored fields, so a writer either updates them together or refuses. Part B's field edit (Q5b) is picked from the fields that are **not** mirrored. |
| R5 | Do Madden and College roster files share the TDB2 layout, and does the one-change roster pair pin a player record? | Decode both roster copies. Diff Band B saves 5 and 6. Recompute CRC-32/BZIP2 on both. | Agent, Code | Same layout (or the differences listed), the changed field found, and CRCs matching on both originals. |
| R6 | How does a Madden Player row connect to the rest of the save, and what do `Original*Rating`, contract and `YearsPro` hold? | On the franchise copies: find every table that references a Player row (Team, DepthChart, DraftPlayer, contract rows). Compare `Original*Rating` against current ratings across saves 2 and 3. | Agent | A reference map for an overwrite target, and enough evidence to propose policies for those fields (or a recorded UNKNOWN). |
| R7 | Does translation lose only what it says it loses? | A pure Core test: decode a College player from a copy, translate to Madden, translate back, and compare shared fields against the loss report. The same in the other direction once Madden copies exist. No save is written. | Code | Every difference is declared in a loss report. |
| R8 | Where do field names come from if A6 fails? | Only if needed. The options, each with a cost: **(a)** derive a generic schema from each save's own table headers — that gives layout (field count, offsets, widths) but not names, so names come from the launcher's own clean-room map built from one-change diffs and screenshots, one field at a time; **(b)** extract schema data read-only from the game's CAS files, which live in the anti-cheat-owned install folder, with the game closed — reading only, never writing, but whether reading in that folder is in bounds is your call; **(c)** ship a read-only viewer that names only the fields confirmed by ground truth, and defer the rest. | Owner decides (D5), Agent researches the chosen route | A chosen route, or the explicit deferral in (c). |

### Compressor and tail layout: deferred, with a rule

The compressor and tail layout are **explicitly deferred** until Q4's measurements exist. Until then the writer design follows one conservative rule:

- **Capacity is the distance from `0x52` to the first non-zero tail byte, not to the end of the file.** A stream that doesn't fit is refused, never truncated, and never allowed to overwrite tail bytes. The tail stays at its absolute offsets.
- **The compressor is chosen by measurement.** If .NET's built-in deflate fits real edits inside that capacity, use it. If it doesn't, evaluate a license-checked libdeflate binding (S8a). Node's zlib is not a candidate.
- Part B tests only this conservative layout. Testing the tail-shifting layout in the running game is not planned. It would only be worth considering if the conservative layout can't fit real edits, and it would come back to you first.

### Part B — write-and-load tests, gated on option 1 or 2

Part B runs only after D4 is decided as option 1 or 2, and only after Part A's Q1–Q4, R2 and R4 are done for the game being tested. Before it runs, add its steps to `docs/smoke-tests/pending.md` (structure only, no save data) and log it to the decisions log as owner-run testing under the chosen option.

**How the tests stay inside the sharpened boundary.** You run them by playing your own game normally. The launcher is closed or idle while the game runs. Every file is written at rest with the game and the EA app closed. Every test writes only to a **disposable slot you created in the game first**, so the game itself wrote that slot's profile entry and no real career is touched locally or in the cloud. "Offline" here means offline game modes. Whether you put the EA app in its own offline mode is your choice and affects sync in ways nobody has observed (B2). The launcher never forces it.

**Under option 1**, the disposable edited saves will be pushed to EA's cloud after the session. That's what option 1 accepts. Part B doesn't turn it into an experiment on the sync. **Under option 2**, Part B waits until R1 has found a user-controlled toggle and you've turned sync off for that game yourself.

| # | Question | Method | Owner | Passes when |
|---|---|---|---|---|
| Q5 | Does each game **accept** a re-packed save? | On a disposable slot. **(a)** An unchanged RTG, re-deflated with the Q4 compressor so the bytes differ, inside the capacity rule. **(b)** The same with one field changed — a field R4 shows is not mirrored in PROFILE-COLLEGE. **(c)** A roster with unchanged content and a recomputed CRC-32/BZIP2. **(c2)** A roster with one edited field and a recomputed CRC. Repeat (a) and (b) on a Madden Franchise save, and (c) and (c2) on a Madden roster. | Owner | (a) and (b) load and the change shows in the game. (c) and (c2) load, and the (c2) change shows. |
| Q7 | Is there an offline Madden landing spot for a player? | Two landing spots, tested separately. **(i) Roster file:** write a Madden roster with one existing player overwritten (changed ratings and name), then start an offline mode that X16 showed accepts a saved roster, and see whether the player is there and holds through one game or week. **(ii) Franchise save:** on a disposable offline Franchise, overwrite one existing free agent or draft prospect, keeping every reference R6 found intact, and see whether it loads and holds through one week advance. Superstar is not a Q7 landing spot; section 8 decides whether it can be one. | Owner and Agent | At least one of (i) or (ii) takes an **overwritten** player and keeps it. |

**What a pass does and does not prove.** A roster that loads with a correct CRC shows the CRC recompute was enough for that roster. It does not prove the CRC is the only check anywhere — a rejected roster would only show that *something* was checked. That's why (c2) edits content rather than relying on an unchanged roster.

**The stop rule.** If anything unexpected appears on its own during Part B — an anti-cheat message, an EA app prompt about saves or a sync conflict, a warning, an account notice — stop Part B, write down exactly what appeared, and bring it back to the section 2 decision. Don't retry, don't vary the test to find out why, and don't go looking for signs. Given B9, this applies with extra weight to anything in offline Franchise.

### Removed from the spike, under every option

These were in the draft. They are probing, not normal use, and are out of bounds regardless of the decision.

- **Q5(d), a roster with a deliberately corrupted CRC loaded into the running game.** That probes the game's integrity handling while anti-cheat is active. The draft also read too much into it: a rejected bad CRC proves only that the CRC is checked, not that it's the only check.
- **Q6's deliberate push of a modified synced save** to watch push, pull or prompt. Replaced by the documentation-only item R1.
- **Q9, "watch for account notices, warnings or anything unusual."** That uses your account to test whether EA notices edited saves. Replaced by the stop rule, which reacts to what shows up on its own and never goes looking.
- **D4, the secondary EA account.** An account action, and owning the games on it is an entitlement action.

### Go

Every writer needs D4 decided as option 1 or 2, plus:

- **College same-game writes** (for example sub-project 3 step 2): Q3, Q4, R2, R4, and Q5(a) and Q5(b) on College.
- **College → Madden character:** Q2 **and** Q3, Q4, R2, R4, R6, R7, Q5(a) and Q5(b) on **both** games, and Q7 passing for the landing spot being built. For roster-file landing, Q5(c) and Q5(c2) on Madden.
- **Madden → College character:** Q2 and Q3, Q4, R2, R3 (so the source guard works), R4, R7, and Q5(a) and Q5(b) on College. The Madden side is only read, so it needs no Part B test of its own.
- **Roster files, either game:** Q4, R5, and Q5(c) and Q5(c2) on the game being written.

### No-go, per branch

| What happens | What it means | What is still worth building |
|---|---|---|
| You choose option 3 | Writing is off by decision, not by failure. | A **read-only** save viewer: character card, roster browser, and a local export of your own data to JSON/CSV. Plus sub-projects 1 and 2, extensionless save snapshots, and the ban-risk fixes. Part A's results stay on file. |
| R1 finds no user-controlled sync toggle, under option 2 | Option 2 isn't available as described. | Back to the decision: option 1 or 3. |
| Q5(a) fails (the game rejects even an unchanged re-pack) | Writing is off. There's a check nobody has found. | The same read-only set. |
| Q5(b) fails but (a) passes | There's a check on content, or a cross-table link was broken. | The same read-only set. Stop investigating until you have a new lead. |
| Q5(c) or (c2) fails | Roster-file transport is off for that game. Nobody knows why, and the plan doesn't vary the file to find out. | Character transport and franchise-level rosters (FrTk) can survive. |
| Q7 fails for both landing spots | No offline Madden destination. | Madden → College direction only, or a read-only "your college player as a Madden card" export. |
| The stop rule triggers | Stop the program's write path. | Read-only features, after a fresh boundary review with you. |
| Q2 fails (Madden schema doesn't fit 620) | Translation needs schema data per build. | A read-only College viewer. R8's routes become their own spike. |

The spike **does not** try to answer rating rescaling, league balance or appearance portability. Those are design work inside sub-projects 3 and 4, calibrated against data read from real saves.

### Cleanup after Part B, accounting for the cloud copy

A local backup is not a clean way back once the cloud has a newer copy. Restoring A1 after a sync has pushed edited saves leaves local older than cloud — the conflict case nobody has observed (B2).

- **Part B never writes to a slot that holds real progress.** Only disposable slots you created in the game. Your real careers are never edited, locally or in the cloud, so they never need restoring.
- **Remove disposable slots with the game's own delete-save menu.** That's normal use. The sync then removes files no longer present locally from EA's storage (B11, VERIFIED as behavior; that it happens for these slots is INFERRED).
- **Treat restoring A1 as a last resort** for something unexpected, and know that it's the unobserved conflict case. If you ever need it, make that call yourself, with the EA app's behavior unknown.
- **Under option 2**, turning sync back on for a game whose local saves differ from the cloud is the same unobserved conflict case. Decide how to handle it before turning sync back on, not after.

---

## 7. The sub-projects

### Sub-project 1: register both games

**Scope**

- An `EaLibrary : IStoreLibrary` adapter. It reads the registry (both parents, both registry views) and `installerdata.xml`, and filters out staged installs.
- A composite store library in DI, and changes at the call sites in K15.
- `StoreIds.EaContentId` (camelCase `eaContentId`) in the manifest schema and merge.
- Import sets the manifest slug as the game id and does **not** set a Steam id. (Or it keeps the Steam id as a fact that doesn't drive launch, per A3/A4.)
- **Launch invokes the EA app only.** The mechanism is UNKNOWN (K20) until A8 answers it. The launcher never starts `EAAntiCheat.GameServiceLauncher.exe` directly, never starts the raw game exe, and never uses `steam://`. If A8 finds no supported way to ask the EA app for a specific game, EA rows offer "Open the EA app" instead of Play.
- The running check reads the process list by name — game exe, anti-cheat launcher, `EADesktop` — and does nothing else. It never opens a handle to, queries, or signals any of them.
- Data folder outside `Program Files` (for example `%LOCALAPPDATA%\626mods\<id>`) through the existing DataDir override.
- Name-pattern save types (`RTG-*`, `ROSTER-*`, `PROFILE-*`) so snapshots work.
- An EA build-change warning keyed on `gameVersion`.
- EA rows have no cover art (no local EA art cache was found).
- Merge the two rows if the same game is detected in both stores (a Steam-bought copy may also write EA keys, UNKNOWN).

**Depends on:** A3, A4, A8, and C1–C4 must land **before** EA detection ships. B1 confirms the install-state rule.

**Parallel with the spike:** yes, fully. Nothing here depends on the section 2 decision.

**Order:** C1 → C2/C3 → C4 → the pure installer parser and install-state rule in Core → the App adapter → call sites → DataDir → save types → build-change warning → launch once A8 answers.

### Sub-project 2: the stale-data pain

**Scope, honest version.** The launcher **cannot** offer a mod-enable path for these games. Both current toolchains replace EA's anti-cheat launcher (P1), and that is outside the boundary. What it can offer:

- A read-only **"is this install vanilla?"** check that flags: `ModData` in the game root; `CryptBase.dll` / `dpapi.dll`; `EAAntiCheat.GameServiceLauncher.exe.backup` or a renamed `_EAAntiCheat*` exe; the `ProgramData\Frostbite\<game>\LCU` cache; and `GAME_DATA_DIR` at user or machine scope.
- The signature list lives in curated manifest data, not compiled code, because the tools change weekly.
- A warning when `gameVersion` changed since residue was last seen.
- Plain guidance: close the game and the EA app, then use the EA app's own Repair.
- **Only if D3 allows it:** a reversible, user-started cleanup of launcher-writable items. Moving the LCU cache aside is a candidate, but whether the EA app re-downloads it from EA's servers, and whether moving it breaks anything, is UNKNOWN (P7). It is not "launcher-writable and harmless" until that's known. Anything inside `Program Files` needs elevation the launcher doesn't have.
- The launcher **never** restores an anti-cheat `.backup` (P2).

**Depends on:** A2 (the friend's answer) and D3. The data-folder fix from sub-project 1.

**Parallel with the spike:** yes, once A2 is answered. Nothing here depends on the section 2 decision.

**Order:** friend interview → a detector in Core against fixtures → a state chip in the App → guidance copy → cleanup only if approved.

### Sub-project 3: move one character, both directions

**Scope**

- **Source rule first.** The source is always the original game's native save: a Road to Glory character read from the College Football save, or a Madden player not created by EA's import. A Madden player with `CollegeRtgImportInfo` / `IsImported` state is refused as a source, and so is any Madden-side source the launcher can't positively show was not imported (R3). This check runs in the plan step, before anything is translated.
- Read the RTG character (Player row plus the linked tables: `PrologueProgress`, `NarrativePlayer`, CharacterVisuals, CharacterGameplay) into a league-neutral `FootballPlayer`.
- Translate by field map:
  - dev trait remapped **by name** under a named policy, never by integer (X3);
  - the 8 differing fields clamped or re-mapped, never copied (X2);
  - `ProspectStarRating` dropped going to Madden (College-only); `Original*Rating` and `PersonalityRating` handled by policy going to College (Madden-only) (X1);
  - every translation returns a **loss report**.
- **Madden overwrite policy**, proposed after R6 and signed off by you:
  - `Original*Rating` fields: meaning UNKNOWN (X14). Proposed candidates are "set to the translated ratings" or "keep the target row's". No default until R6 shows what they do.
  - Contract and `YearsPro`: keep the target row's values unless a policy says otherwise, so the overwrite doesn't create a contract the league didn't sign.
  - Cross-table references (Team, DepthChart, DraftPlayer, contract rows): the plan step checks every reference R6 mapped and refuses if an overwrite would leave one dangling or inconsistent (X15).
- **Landing spots for College → Madden**, first-class and tested separately in Q7:
  1. **A full Madden roster file** with one existing player overwritten, then an offline mode started from that roster (S10, S10a, X16).
  2. **An offline Franchise save**, overwriting an existing free agent or draft prospect.
  3. **Superstar**, only if section 8 finds Superstar runs offline. Otherwise it is not a destination.
- **Landing spots for Madden → College:** an RTG character overwrite, or a College roster file (same TDB2 family, S10), under the same model.
- **Keep PROFILE-COLLEGE consistent.** Any RTG write that changes a field R4 shows is mirrored in the profile summary updates the profile in the same capture-all → seal → change-all set, or refuses.
- Overwrite an existing row first. Insertion comes later, if at all.
- Appearance is optional and gated on Q8 and X13.
- **The translate-and-back round-trip test** (R7) is part of the test suite, not a one-off.

**Depends on:** D4 as option 1 or 2, spike go for the direction being built, A6, D1, D2, D5 if A6 failed.

**Parallel with the spike:** the **read-only** half only: `FBCHUNKS` reader, `FrTk` table reader, TDB2 roster reader, character card in the saves panel. All of it is inside the boundary under option 3 too.

**Order**

1. Read-only character card.
2. Writer proven on a **same-game** move: overwrite a character between two College RTG copies, keeping PROFILE-COLLEGE consistent.
3. College → Madden, roster-file landing.
4. College → Madden, Franchise overwrite.
5. Madden → College.
6. Appearance.

### Sub-project 4: whole rosters with league translation

**Scope**

- Two separate converters: **TDB2 roster files** (S10, S10a, with CRC-32/BZIP2) and **franchise-level `FrTk` rosters**. Both are first-class landing spots.
- **The roster file is ours; EA's roster share is not.** The launcher reads and writes local roster files only. EA's online roster share is EA's feature, the user's own choice, and goes through EA's servers. The launcher never uploads or downloads through it.
- League translation needs policies **you** set and can see in the UI:
  - eligibility and class year;
  - 138 college teams to 32 NFL teams and back;
  - about 85-man rosters to 53;
  - draft versus free agent;
  - how a college team is spread across Madden modes;
  - how filler players are generated or kept.
- Rating rescaling comes from **distributions measured** in your own saves and rosters, never invented constants (X12).
- The Madden overwrite policy and cross-table checks from sub-project 3 apply to every overwritten row.
- Multi-file writes use the capture-all → seal → change-all order.
- No roster data from EA, the NFL or college teams ever ships. Only the user's own files are read and written.

**Depends on:** D4 as option 1 or 2, sub-project 3's writer, R5, Q5(c)/(c2) for roster files, and your policy decisions.

**Parallel with the spike:** the read-only roster browser and the population dump used for calibration.

**Order**

1. Read-only roster browser, both games.
2. Rating distribution dump per position, both games.
3. Policies signed off by you.
4. Roster-file writer: one overwritten team, round-tripped in Core, then an offline mode started from it.
5. NFL → College direction, roster file or offline dynasty.
6. College team → Madden, roster file and offline Franchise.

---

## 8. The Superstar workstream

You want to keep aiming at Madden's Superstar mode, with its own dedicated workflow once you have a Superstar save. This section states what that workflow does, so launching it later is one step.

### Where Superstar sits against the boundary

- **As a research target, Superstar is fully in bounds.** Reading Superstar saves, understanding the format, and running the offline test below are all fine under every option in section 2.
- **Writing a transported character into Superstar is in bounds only if Superstar runs offline.** If it's online-only by design, the workstream's job is to find that out, not to work around it, and Superstar stays a read-only research target.
- **The Superstar position-conversion policy is conditional on that answer.** X9 (the 8 Superstar positions) and open question 9 only matter if Superstar runs offline. If it doesn't, there's no Superstar position policy to set.
- **The source rule applies in full.** A Superstar character made through EA's import is never a transport source (section 3).

### The free, decisive test

Put the EA app in its own offline mode and try to start Superstar.

- This is you using your own game's normal offline mode. It isn't the launcher forcing anything. The "don't force the EA app offline" rule in sections 10 and 13 is about the launcher, not about you.
- No edited file is involved, so nothing modified is at stake.
- If Superstar refuses to start offline, write down exactly what it said. That settles X8, and the workstream becomes read-only. Don't try anything to get past it.
- If Superstar starts offline, note whether it offered to create a new player offline, and whether it lets you continue an existing one.
- What offline mode does to the sync on reconnect is UNKNOWN (B2). With no edited files involved, that doesn't change anything here.

### Captures, in order

Make these as ordinary play, then have them copied the same way as Band B item B5.

1. **A Superstar save made through EA's official import.** Valuable as a **format reference only**: it shows what a real imported character looks like on disk and where ratings live, given the import record carries none (X7a). By the source rule it must never be a transport source. Making it uses EA's online import, which is EA's feature and your own choice.
2. **A fresh Superstar save made without importing.** Diff it against capture 1. This is what R3 needs: the on-disk signal that tells an imported character apart.
3. **The same save as capture 2, before and after a small bit of progress.** This shows which tables move as a Superstar career advances.

Also note whether a local Superstar save file exists at all (X18), and whether the EA app's logs show it in the synced set (the same read-only log check as B3).

### What the workflow produces

- A read-only Superstar format map: container family, schema major, Player row, where ratings and dev trait live, where import state lives.
- R3's import signal, feeding the source guard in sub-projects 3 and 4.
- The answer to X8. If Superstar runs offline, a proposal for Superstar as a sub-project 3 landing spot, with its own Part B tests brought back to you under the section 2 decision. If it doesn't, a note that Superstar stays read-only.

---

## 9. Architecture outline

Everything fits the existing pure-core / thin-shell split. `CorePurityTests` won't catch registry access in Core, so keep it out on purpose: IO lives in the App.

**Core (pure, headless, tested)**

- `Stores/Ea/`: `EaInstallerData.Parse(xml)` gives content ids, `gameVersion`, title and relative launcher path. Also `EaInstallState.IsInstalled(...)` and `EaGameImport.Plan(...)`, which produces a `GameInput` with the manifest slug as id.
- `BanRiskCatalog`: resolution by `GameEntry` (Steam id, then slug, then EA id) with a compiled floor for these titles.
- `SaveEditor/Frostbite/`:
  - `FbChunksReader`: a header per save kind, never one assumed layout.
  - `FbChunksWriter`: patches the full u32 at `0x4A` for RTG and the decompressed length for PROFILE and ROSTER; capacity ends at the first non-zero tail byte; throws on overflow, never truncates, never overwrites tail bytes; compressor chosen by Q4's measurement.
  - `FbBuildSupport.CanWrite(buildTag, schemaMajor)`: a fail-closed allowlist.
  - `FrTkTableReader`: reads the per-table field-offset header, so a layout fingerprint can be checked per build without a public schema.
- `SaveEditor/Frostbite/Tdb2/`: `Tdb2RosterReader` and `Tdb2RosterWriter`, with CRC-32/BZIP2 (MSB-first) — **not** the common reflected CRC-32. A test pins the algorithm against the value stored in an unmodified roster fixture.
- `SaveEditor/Ea/CollegeFootball27/` and `/Madden27/`: typed records, a `Read` / `Apply` pair, `SaveCharacter` listing, and a PROFILE-COLLEGE consistency map from R4.
- `SaveTransport/Football/`:
  - league-neutral `FootballPlayer` / `FootballTeam` / `FootballRoster`;
  - pure translators returning `TranslationResult<T>{ Value, Losses }`;
  - `LeagueMapping` and `MaddenOverwritePolicy` data;
  - `ImportedSourceGuard`, which refuses EA-import-created characters and anything it can't positively clear;
  - `CrossGameTransferPlan.Build(source, target, selection)`, which validates the source guard, build, capacity, target mode, cross-table references and profile consistency **before** any write.
- `SaveTransportRules.CanWrite(risk, acked, gameRunning, antiCheatRunning, eaAppRunning, snapshotTaken, sourceCleared, targetKindIdentified)`: calls `BanRiskRules`, fails closed, and returns an agent refusal that no agent can satisfy.
- `Frostbite/ResidueScan`: pure matching of a file listing against curated signatures.

**App (thin, side effects)**

- `Services/EaLibrary`: registry and file IO.
- A composite `IStoreLibrary` in DI.
- `Services/FootballSaveService`:
  - running check that reads the process list by name (game exe, anti-cheat launcher, `EADesktop`) and fails closed if the list can't be read — no handles, no queries, no signals;
  - snapshot target with `auto: false` so pruning never removes it;
  - apply, then re-read and verify **decoded** content.
- EA launch: invokes the EA app only, by whatever mechanism A8 finds (K20).
- `SaveTransferDialog`: source, target, loss-report preview, disclosure, acknowledgement, apply. AutomationIds on every control, per the repo rule.
- Character dispatch in `SavesDialog` keyed by **manifest id**, not Steam app id.

**Where the laws apply**

- **Reversibility.**
  - Snapshot first, atomic temp-and-rename, decoded verification after every write.
  - Multi-file changes follow the `RestorePointOrchestrator` order.
  - **Never** reuse `SaveBundle.Restore` or `ProfileRestore`: they refuse other game ids and clear the saves folder first (VERIFIED).
  - **Temp files and sync.** An atomic rename needs the temp file on the same volume as the target, and the sync set shouldn't pick it up. The resolution: write the temp file to a launcher-owned folder **outside** the synced saves folder but **on the same volume**, checked at plan time. If no such folder exists (for example, Documents redirected to another volume), fail closed rather than writing a temp file inside the saves folder. The EA app is already required to be closed during a write, so sync can't run mid-write; what the sync would do with a stray file left behind by a crash is UNKNOWN, which is why the temp file stays out of that folder.
  - Snapshots are kept outside the EA-synced folder, so a cloud pull can't wipe the way back locally. That local way back does not undo a copy the cloud already has (section 6, cleanup).
- **Ban risk.**
  - Every save write passes `SaveTransportRules`.
  - These titles resolve to High even without the feed.
  - The acknowledgement is a human act. Agents are always refused.
- **Validate then write.** `CrossGameTransferPlan` covers reading, source clearance, validating, translating and planning. Only then does it snapshot and write.
- **camelCase on disk.** Any new persisted shape (transfer records, residue state, acknowledgements, the section 2 decision if persisted) uses camelCase JSON, has a round-trip test, and is added to the rule's list.
- **Marking transported saves.** Nothing is written into the save, and no sidecar goes in the saves folder — either one would be an extra write that syncs to EA. The launcher keeps a transfer record in its own data folder, keyed by the written file's hash, and shows the online-mode warning from there.
- **Never bundle.** No schemas, dictionaries or EA data ship unless A6 clears them. The launcher's own field maps are written clean-room.
- **Store SKU.** Whatever D1 decides is wired as a compile flag, and `StoreSkuTests` is extended.
- **Decisions log.** Log to the 626Labs dashboard: the section 2 decision and its option; Part B as owner-run testing under that option; the source rule; the ban-risk resolution change; the EA store key; the save-write gate; the Store-SKU call; the field-name source (D5); and the compressor choice once Q4 measures it.
- **Smoke tests.** Part B's steps and the section 8 offline test go into `docs/smoke-tests/pending.md` before they run, with structure only and no save data.

---

## 10. The safe envelope

**Do**

- Read and write saves only while the game, the anti-cheat launcher and the EA app are all closed. Check by reading the process list, and refuse if it can't be read.
- Take a snapshot outside the EA-synced folder before every write, and verify decoded content after.
- Gate every save write on ban risk resolved without depending on Steam, with a compiled High floor for these two titles.
- Require a human acknowledgement for save writes. An agent can never give it.
- Refuse to write for any build tag or schema major not on the allowlist.
- Take sources only from the original game's native save. Refuse EA-imported characters, and refuse any Madden-side source that can't be positively cleared.
- Write only to save kinds positively identified as offline (R2). Refuse anything unidentified.
- Tell the user, in plain words, before every write:
  - the edit is made offline;
  - if EA's cloud sync is on, the EA app will carry the edited save to EA, or replace it, the next time you play;
  - EA's terms don't make an exception for offline edits, and name modified files as evidence;
  - the game loads the edited save with EA's anti-cheat running;
  - whether any of this can be detected is unknown.
- Record transported saves in the launcher's own data folder, and warn that using them in any online mode is cheating under EA's rules.
- Keep residue signatures and per-game facts in curated manifest data.
- Do byte research only on copies in the scratchpad. Study public source; don't run it.
- Launch these games only by invoking the EA app.

**Don't**

- Don't start, touch, rename, replace, hash-and-restore or recommend anything that swaps `EAAntiCheat.GameServiceLauncher.exe`, or drop DLLs into the game folder.
- Don't add an anti-cheat toggle, a launch option or a tool-catalog link for MMC, FMT, Frosty or any fake launcher. A test should assert that no anti-cheat toggle exists for these game ids.
- Don't force the EA app offline, edit `hosts`, disable adapters or add firewall rules for these games. (This is about the launcher. You using the EA app's own offline mode is your call.)
- Don't change cloud-sync settings for the user. If a user turns sync off, that's their act in EA's app.
- Don't use an EA-imported character as a transport source.
- Don't write `CollegeRtgImportInfo`, `IsImported`, `RtgImportNarrativeTags` or any other EA import state.
- Don't write into online-connected saves (online dynasty, cloud franchise, Ultimate Team). Because every College Football save is cloud-synced, "online-connected" means saves for modes played online, told apart by R2's signal — not "synced".
- Don't write a transport marker into the save or a sidecar into the saves folder.
- Don't upload or download through EA's roster share from the launcher.
- Don't ship EA, NFL or college roster, name or likeness data in the binary or the feed.
- Don't use the words "safe" or "undetectable" in the UI.
- Don't launch either game from an agent, don't probe Javelin, don't feed a protected game a deliberately malformed file, don't test whether EA notices, don't modify the registry, don't take any account action.
- Don't run third-party editors or mod tools on this machine.
- Don't restore an anti-cheat `.backup` file.
- Don't write to `Program Files` without an explicit, elevated, user-started action that you've approved (D3).
- Don't put personal save data — character names, schools, schedules, weeks, ratings, slot ids — into anything committed to this public repository.

---

## 11. Risks

1. **The launcher silently breaks its own ban-risk law.** An EA registration without a Steam id reads as no risk (K8). This is the most serious failure in the program, and it's fully in your control. C1–C3 exist for it.
2. **Account action.** Javelin may look at file storage while the game runs (B4), and every edited save is loaded under it (K7). Javelin reportedly blocks mods and trainers in Madden 24–26 **offline Franchise** (INFERRED, B9), which suggests EA's anti-cheat attends to offline Franchise, not just online play. Whether it checks saves (B5), and whether EA validates player data on its servers (B6), is UNKNOWN. An EA account spans every EA title you own, so the consequence might not stay with one game (INFERRED). This is why section 2 is a decision and not a disclosure, why Part B has a stop rule, and why the Franchise landing spot is tested separately from the roster-file one.
3. **Cloud sync.** With sync on, edits get uploaded to EA as modified files, or overwritten by the cloud copy. Or a mismatched RTG/PROFILE pair after a sync conflict leaves a broken set (B1, B2).
4. **The way back collides with the cloud.** Restoring a local backup after a sync has pushed edited saves leaves local older than cloud, which is unobserved (B2). Section 6's cleanup keeps real careers out of Part B for this reason.
5. **Using an EA-imported character as a source** would undo EA's online-only restriction — the one entitlement the program promises to route around. The source guard fails closed until R3 finds the on-disk signal.
6. **Hidden integrity check.** S6 is a strong inference, not a proof, and the public writer behind part of it is reported to corrupt saves. The first real load test (Q5) could fail.
7. **Schema drift.** 468 → 809 → 833 across one season of patches (S14). A translator built for one build can write plausible garbage after the next. The allowlist must fail closed.
8. **Size, tail and compressor mistakes.** A stream that grows past capacity, a changed file size, or overwritten tail frames can hang the game on load (S7–S9, S7a, S8a, the DaiyronW report). The slack on the one copy measured is small.
9. **PROFILE-COLLEGE drift.** An RTG write that changes a mirrored field without updating the profile summary could show stale data or break the menu (S11, R4).
10. **The wrong CRC variant.** Recomputing the common reflected CRC-32 instead of CRC-32/BZIP2 produces rosters the game rejects, which is the most likely explanation for FMT's reports (S5a).
11. **Dev-trait aliasing and the differing shared fields** hand out X-Factors or out-of-range values (X2, X3).
12. **Broken cross-table references** when overwriting a Madden row — Team, DepthChart, DraftPlayer, contracts (X15).
13. **No offline Madden destination** (X8, X10, X16). The roster-file landing spot reduces this risk; it doesn't remove it.
14. **Madden format surprises.** Everything on the Madden side is inferred (X4, X5).
15. **Stored versus recomputed OVR, and Madden's `Original*Rating` copies,** could make translated players show one rating and play like another, or regress unexpectedly (X11, X14).
16. **Uncalibrated translation** gives results that look reasonable and aren't fun. Whole-league moves produce superteams or empty rosters.
17. **Scope creep into circumvention.** The whole community workflow runs through a fake anti-cheat launcher. A "one-click mods" request is a bypass request, however it's phrased.
18. **Licensing.** Unlicensed community repos, schema files and dictionaries with unclear terms. Copying from them would taint Core. R8 covers the case where the schema files can't be used.
19. **Store listing exposure.** A reviewer or an EA IP complaint could read the feature as defeating EA's import. That puts the whole listing at risk, not just this feature (B7).
20. **Stale-data design aimed at the wrong folders** if the friend's actual pain differs from P3.
21. **Elevation.** Every cleanup under `Program Files` needs admin rights the launcher deliberately doesn't have.
22. **Detecting the same game twice** when a Steam copy also writes EA registry keys (UNKNOWN).
23. **Personal data in a public repo.** Save copies, screenshots and decoded values are personal. They stay in the scratchpad; plans, tests and smoke entries describe structure only.

---

## 12. Open questions

These are asked one at a time, in this order.

**For you**

1. **Section 2: option 1 (accept it knowingly), option 2 (keep saves out of sync first), or option 3 (read-only)?**
2. Should ban risk for games with no Steam id be looked up by the manifest id, by an EA id, or both?
3. Which EA id do you want stored: the numeric content id from the installer file (recommended), or the offer id from the EA app's data folder?
4. May the launcher ever delete or move third-party mod leftovers, or should it only detect them and point you at the EA app's Repair?
5. Should save transport ship in the Microsoft Store version, or only in the GitHub version?
6. Should the acknowledgement for writing saves be separate from the one for enabling mods?
7. If the schema files can't be used, which field-name route: names built clean-room from diffs and screenshots, a read-only schema read from the game's install folder, or a viewer that names only confirmed fields?
8. For dev traits going College → Madden, what should College Elite become: X-Factor, Superstar, or your call per player?
9. *Only if section 8 finds Superstar runs offline:* when a player's position doesn't exist in Madden Superstar (a lineman, a kicker), should the launcher convert the position, refuse, or send them to a roster file or Franchise instead?
10. Is it fine for real NFL player names to end up in your private college saves, as long as nothing is ever shared or shipped?
11. Should transport only overwrite existing players (safer), or should adding new player rows be a goal if the spike shows it's possible?
12. When overwriting a Madden player, should contract and years-pro stay as the target row had them? (Proposed: yes.) The `Original*Rating` policy waits for R6.
13. If the spike says no-go on writing, or you choose option 3, do you want the read-only viewer and export built anyway?

**For your friend**

1. When the game wouldn't start, which mod tool were you using: MMC Frosty Modding Tools, the FMT plugin, or something else?
2. What exactly did you have to move or delete? Folder and file names if you remember them (for example `ModData`, `CryptBase.dll`, a folder under `ProgramData\Frostbite`).
3. What did "won't start" look like: nothing happened, a crash-report popup, an anti-cheat window, or an error message?
4. Did this happen right after a game update, or even without one?
5. Do you ever play these games online on the same PC and account you mod with?
6. If the launcher could only **tell** you "this install has leftover mod files, here's where they are, use the EA app's Repair", would that solve most of it? Or do you need it to clean up for you?
7. For moving a college player to Madden: which offline landing would you actually use — a custom roster file you start a mode with, Franchise as a player, Franchise as a drafted prospect, or something else?
8. For rosters: when you send a college team into Madden, do you picture them all on one NFL team, spread through the draft, or something else?

---

## 13. What we are deliberately not doing

- **Not** fixing, imitating, speeding up or getting around EA's online College-to-Madden import, any entitlement, or any online check.
- **Not** using an EA-imported character as the source of any transport.
- **Not** writing EA's import flags or import records into any save.
- **Not** touching Javelin in any way: no starting it directly, no probing, no toggle, no rename, no replacement launcher, no proxy DLLs, no restoring backups of it.
- **Not** feeding a protected game a deliberately malformed save, and not testing whether EA notices an edit.
- **Not** using a second EA account, or taking any other account or entitlement action.
- **Not** integrating with, automating, bundling or linking to MMC, FMT, Frosty, FrostyFix or any tool that ships an anti-tamper bypass.
- **Not** offering a mod-enable path for College Football 27 or Madden NFL 27, as long as enabling mods there requires replacing the anti-cheat launcher.
- **Not** editing anything while the game, the anti-cheat launcher or the EA app is running.
- **Not** forcing the EA app offline, blocking the network, or changing cloud-sync settings for the user. What you do in EA's own app is your call.
- **Not** writing to online-connected saves or online modes, and not promising that edits stay offline.
- **Not** uploading or downloading through EA's roster share from the launcher.
- **Not** making the section 2 decision on your behalf, or running Part B before you make it.
- **Not** shipping roster, name, likeness or schema data from EA, the NFL or college teams, and not copying code from unlicensed repos.
- **Not** hardcoding rating conversion constants that weren't measured from real saves.
- **Not** running third-party editors, roster tools or mod managers on this machine.
- **Not** building any writer before the spike's go criteria are met.
- **Not** treating `C:\ProgramData\EA Desktop\InstallData` as proof a game is installed, or the EA app's encrypted install-state file as something to read.
- **Not** flagging other Frostbite games' leftovers (Battlefield 6) unless they are registered games.
- **Not** committing save copies, screenshots or decoded personal values to this public repository.

---

## 14. What changed from the draft

- The cloud-sync and anti-cheat findings moved from a disclosure to an explicit three-option decision (section 2). The boundary section no longer claims anything those findings contradict.
- The spike split into Part A (read-only, starts now) and Part B (gated on the decision). Q5(d), Q6's deliberate push, Q9 and the secondary-account decision are removed under every option; a stop rule and documentation-only research replace them.
- Added: the source rule against EA-imported characters; full roster files as a first-class landing spot; the Superstar workstream; PROFILE-COLLEGE consistency; the cleanup plan for the cloud copy; the temp-file resolution; the field-name fallback routes; the Madden overwrite policy, cross-table checks and translate-and-back test; the online-versus-offline save signal; what marking a transported save means.
- Corrected: EA's import connection requirement is now INFERRED from secondary press; the RTG length offset disagreement is recorded as superseded; the roster CRC is pinned to CRC-32/BZIP2 with the likely reason for FMT's rejections; the compressor and tail layout are explicitly deferred behind a capacity rule; launching is limited to invoking the EA app, with the mechanism marked UNKNOWN; the C3 corrections are in X1 and X2.
- Removed: all personal save data. The document describes save structure only.
