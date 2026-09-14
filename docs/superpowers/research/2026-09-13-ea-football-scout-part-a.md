# EA football scout, part A: results

Scouting round, 2026-09-13. It sits next to [the grand plan](../plans/2026-09-13-ea-football-grand-plan.md) and [the research](2026-09-13-ea-football-research.md). Evidence ids (K8, B10, S7a…) are rows in the plan's section 4 tables. Item ids (A3, R1, Q4…) are rows in its sections 5 and 6.

Everything this round did was read-only. No game, anti-cheat, or EA app launch happened. No setting or registry value changed, nothing was uploaded, and no original save was opened. All byte work ran on hash-verified copies in the scratchpad, and working files stayed there. None of your personal save values appear here, only structure.

Where an adversarial check refuted an item or only partly confirmed it, the check's verdict is what's written below.

---

## 1. The short version

- **The cloud-save toggle probably doesn't exist, but that isn't proven.** EA's help center documents no per-game or global toggle. EA staff said in 2023 that the EA app has "no settings" for cloud save. Today's cached EA app UI text has no toggle string, only a conflict prompt and a local-backup restore. B10 stays **UNKNOWN, leaning no toggle**. Offline mode is not a documented stand-in: EA doesn't say it pauses sync, and the app goes back online every time it reopens. **As things stand, option 2 has no mechanism**, and the plan's own rule sends it back to option 1 or 3. The one thing that could still change this is you looking at the live EA app UI.
- **The container round trip works, in memory, for all three save kinds.** RTG, PROFILE and ROSTER re-inflate byte-identical after a .NET 10 rebuild. The rebuild keeps the exact file size, leaves the tail at its absolute offsets, and a recomputed roster CRC matches the stored one. Whether the game *accepts* a re-packed file is still untested (Q5, Part B).
- **RTG capacity is tight.** Only 431–637 zero bytes sit between the stream end and the first non-zero tail byte, not megabytes. Only .NET `CompressionLevel.SmallestSize` fits, with about 179–200 KB to spare. `Optimal` and `Fastest` both overflow. A single-field edit barely moves the size. A random overwrite fits at every position tested up to about **131 KB**. At 262 KB it only fits at some positions.
- **Stock zlib reproduces EA's streams byte for byte** (level 6 for RTG, level 9 for PROFILE and ROSTER). That makes a byte-exact repack possible, which is a second compressor route next to SmallestSize. It needs a binding or a managed port, so it's your dependency call.
- **Launching through the EA app has a working key.** `origin2://game/launch/?offerIds=<contentID>&cmdParams=` goes through Windows' registered handler. This machine's EA app logs show it launching College Football 27 successfully. For Madden it's INFERRED, since Madden hasn't been launched. EA doesn't document the scheme.
- **The finished Madden install matches College Football.** K4's conflict is explained: the installer writes `installerdata.xml` at the start and the registry key at the end. K5 holds on two completed installs, with three wording fixes. Both Steam ids are correct (4032350, 3940610).
- **Licensing is stricter than B8 says.** FMT plugin, FrostyToolsuite and DatapathFixPlugin are CC BY-NC-ND 4.0. Several others have no license at all. The schemas and zstd dictionaries in `madden-franchise` are almost certainly EA-derived, so no community MIT label covers them, and they can't ship. A clean-room rule is drafted for your sign-off.
- **No online/offline marker exists in the College saves** (B12 stays UNKNOWN). What we have is a positive fingerprint for Road to Glory saves, so a writer can accept only that shape and refuse everything else. Of PROFILE-COLLEGE's 13 summary fields, 9 are now tied to RTG sources, and the field that looked like an overall rating is **not** one.

---

## 2. What this changes for the decision

This section doesn't pick an option. It lays out how the R1 finding lands on each one.

**Option 1, accept it knowingly.** Nothing in R1 blocks it. It adds facts the disclosure should carry:
- A local-vs-cloud conflict prompt exists. In an EA app build cached in 2025, each choice explicitly overwrites the other side: "continue with local" overwrites the cloud, "replace local with cloud" overwrites local. That text is VERIFIED for the 2025 build and INFERRED for today's build (see R1's verdict).
- The app keeps a local backup it can restore ("Restore latest local save").
- What triggers the prompt is UNKNOWN. So restoring your A1 backup after a push is still the unobserved case, but now you know what the prompt looks like if it appears.
- A8 confirms that launching through the EA app keeps the app's pre-launch gates, including the sync step (B1).

**Option 2, keep saves out of sync first.** As written, this depends on "a user-controlled way to stop cloud sync for these games". The evidence doesn't support that such a control exists:
- EA's docs are silent.
- EA staff said there are no such settings. That was in 2023.
- Today's cached UI has no toggle text. The cache is partial, so this is weaker evidence than it sounds.
- Local config stores no such setting. Those files hold few user-facing settings anyway.
- Offline mode isn't a documented substitute. EA doesn't say it skips sync, it resets to online when the app reopens, and what reconnecting does is undocumented.

The plan's no-go table says: *R1 finds no user-controlled sync toggle, under option 2 → back to the decision: option 1 or 3.* The one open door is that nothing reachable read-only could show the live UI. If you open College Football 27's **View Properties → Saved data** and the EA app **Settings** (look, don't click) and find a toggle, option 2 comes back. Otherwise it's effectively gone. If you want an authoritative answer, an EA support ticket is an account action only you can take.

**Option 3, cap at read-only.** R1 doesn't affect it. The work that actually landed this round serves it directly: launch key, install detection, ban-risk brief, PROFILE mapping, the RTG fingerprint. Choosing it doesn't throw any of that away.

**Under 1 or 2, R2 narrows the writer's scope regardless.** With no online/offline marker, a College writer can only accept the verified RTG fingerprint. The disclosure must not claim a save is proven offline.

---

## 3. Results by item

### R1 (also A7): the EA app cloud-save toggle

**Answer.** EA documents no cloud-save toggle, per game or global. The toggle question stays **UNKNOWN, leaning "no toggle"**. EA's docs say nothing about what offline mode or reconnecting does to cloud saves. Only the app's own cached UI text describes the conflict prompt.

**Status.** Partially answered. **Adversarial verdict: partially confirmed.**

**Key findings**

- **VERIFIED.** The help center implies an enabled/disabled state but gives no location: "If you've enabled cloud saves on your PC, your saved game data will transfer automatically." and "Some games don't have cloud save capabilities…" The page has no other cloud mention (lastUpdated 2026-04-23). The verifier re-checked both quotes word for word. Source: https://help.ea.com/en/articles/platforms/how-to-update-operating-system-ea-app/
- **UNKNOWN.** No help.ea.com article documents a toggle. `https://help.ea.com/en/articles/ea-app/cloud-saves/` returns 404 (verifier confirmed). Not finding it in search doesn't prove it's absent.
- **VERIFIED, dated.** EA staff on EA's forums, about 2023:
  - EA_Atic, DICE Team, marked as solution, edited 2023-01-10: "there are currently no settings in the EA app that handels Cloud Save, its a automatic system." Thread [7494245](https://forums.ea.com/discussions/ea-app-general-discussion-en/re-cloud-save---where-can-i-find-settings-or-info-about-cloud-save-status-/7494245).
  - EA_Shepard, Community Manager: "The EA App saves your data to the cloud. If you uninstall a game and reinstall it, your saved would show up after logging in." Thread [7533790](https://forums.ea.com/discussions/ea-app-general-discussion-en/re-ea-app-cloud-saves/7533790).
  - A third quote ("no way to sync the cloud saves other than a reinstall or a repair") is in thread [7619892](https://forums.ea.com/discussions/ea-app-technical-issues-en/re-cloud-save-retrieval/7619892). The verifier got HTTP 403 and could **not** re-check it.
- **INFERRED.** Today's EA app has no toggle in per-game Properties or Settings.
  - The cached UI bundle under `%LOCALAPPDATA%\Electronic Arts\EA Desktop\CEF\2\…` (file dates 2026-08-30 to 2026-09-10) has a "Saved data" properties section, "Local data: Backed up %monthDayYearTime%" and "Restore backup". No toggle key or string appears among 132 settings/properties keys.
  - The verifier independently found none of `Enable Cloud Storage`, `Cloud Storage`, `cloud_save`, `CLOUD_SAVE`, `cloudSave` or `SETTINGS_CLOUD`. All 120 distinct "cloud" strings are conflict, error or confirmation text.
  - **Verdict caveat:** the cache is visibly partial. Some 2026 keys have no English text. A missing toggle string is weaker evidence than "strong".
- **VERIFIED, weak as evidence.** No cloud or sync setting is stored in `user_*.ini`, `machine.ini` or `backgroundservice.ini`.
  - `cloudsync\` holds per-game sync *state* files, not settings. Logs show a server-side gate, `cloud_conflict_resolution … is enabled`.
  - **Verdict caveat:** those ini files hold almost none of the app's user-facing settings, so the absence says little. It's supporting context only.
- **VERIFIED.** EA's offline-mode docs never mention cloud saves. Quotes: "A game will work offline for 30 days since it was last launched online." "If you close the EA app, you'll automatically go online the next time you open it." The verifier counted zero occurrences of "cloud" on that page. Offline mode doesn't persist, so it can't stand in for a sync-off setting. Source: https://help.ea.com/en/articles/platforms/download-and-play-ea-app-games/
- **Conflict prompt, VERIFIED as UI strings only (not as behavior, not EA documentation).** Split by build, per the verdict:
  - **In today's cache (2026):** "Continue with local data", "Continue without local data", "Replace local save with cloud save", "Restore latest local save", plus bridge calls `resolveCloudSyncConflict` and `restoreLocalDataBackupRequest`.
  - **Only in the old 2025 cache** (`CEF\BrowserCache\EADesktop\Code Cache\js`, dated 2025-08-09 to 2025-12-28): "Are you sure you want to continue with your local save?" with "This will overwrite your existing cloud save for %gameName%.", its cloud-side mirror, and the `CLOUD_CONFIRMATION-MODAL_DATA-OVERWRITE_*` keys.
  - **So "each choice overwrites the other side" is VERIFIED for a 2025 build and INFERRED for today's build.**
- **UNKNOWN.** What triggers the conflict prompt, and which side wins when there's no prompt. Local logs show only 9 "LocalState and RemoteState are identical" lines and 11 "Pushing post-game local state" lines, with no conflict event.
- **UNKNOWN, secondary.** "Choosing local deletes all cloud saves" comes from a non-staff Mass Effect forum post, 2025-05-04 ([12148048](https://forums.ea.com/discussions/mass-effect-franchise-discussion-en/cloud-save-deletion/12148048)). It fits B11 but isn't EA's word.
- **UNKNOWN, likely wrong.** A third-party article (tech-insider.org) claims a "Settings > Gameplay > Cloud Storage > Enable Cloud Storage" toggle. Neither string exists in the app's cached bundle. A 2023 non-staff forum solution says there is no way to disable cloud save. Origin had a per-game checkbox; the EA app replaced Origin.

---

### A8: how to ask the EA app to launch a specific game

**Answer.** Open `origin2://game/launch/?offerIds=<contentID>&cmdParams=` through the shell, so the registered handler gives it to the EA app. The contentIDs come from `installerdata.xml`: **16425899** (College Football 27) and **16425895** (Madden NFL 27). Confidence is high for College Football and medium for Madden.

**Status.** Answered. No adversarial check ran.

**Key findings**

- **VERIFIED.** Both `installerdata.xml` files list two launchers:
  - uid 1-2 is the Trial (`-trial`, trial=1); uid 2-2 is the full game (empty parameters, trial=0).
  - Both point at `[HKLM\SOFTWARE\EA Sports\<title>\Install Dir]EAAntiCheat.GameServiceLauncher.exe`, and their per-launcher `<contentID/>` is empty.
  - The only top-level contentIDs are the two above. Neither file has a launch URI, offer id or slug.
  - Feature flags include `allowMultipleInstances=0` and `enableOriginInGameAPI=1`.
- **VERIFIED** (read-only `reg query`). Registered handlers: `origin2://` and `origin://` → `EA Desktop\EALauncher.exe "%1"`; `link2ea://` → `Link2EA.exe`; `ealink://` → `legacyPM\OriginLegacyCLI.exe "%1" -wait`. No `eadesktop://` or `ea://`.
- **VERIFIED** (`EADesktop.log`, `EALaunchHelper.log`, 2026-08-31 to 2026-09-13).
  - The EA app launched College Football 27 from `origin2://game/launch/?offerIds=16425899&title=…&authCode=&cmdParams=`. It resolved the contentID to offer `Origin.OFR.50.0006233` / slug `college-football-27`, logged `handleExternalGameLaunchRequest contentIds=[16425899]`, then "Successful launch".
  - One 2026-09-10 request failed with `GamePreLaunchError [GameUpdateRequired]`, so the app keeps its update, license and sync gates on this path.
- **INFERRED.** That URI was probably built by the game-side stub, not a user. One logged URI carries `title=DisplayName field missing from registry.`, EA's desktop shortcuts point at raw exes, and `EALaunchHelper.exe`, not the shell handler, received it.
- **VERIFIED.** EA's Public Desktop shortcuts target the raw `CollegeFB27.exe` and `Madden27.exe`. The launcher must not copy that.
- **VERIFIED.** The EA app knows Madden as `Origin.OFR.50.0006150` / `madden-27-base-game`. No Madden launch has ever been logged.
- **INFERRED.** `offerIds=16425895` will launch Madden: same installer generation (EAInstaller-5.08.02.00), same manifest shape, contentID in the same range. Not tested.
- **VERIFIED, community only.** Lutris [#4996](https://github.com/lutris/lutris/issues/4996) states that contentID is the id to use. BoilR [#306](https://github.com/PhilipK/BoilR/discussions/306) shows the older Origin.OFR form. EA forum thread 7510331 was read from a search snippet only (403); poster roles UNKNOWN.
- **UNKNOWN.** EA publicly documents no launch URI scheme.
- **VERIFIED.** `link2ea://launchgame/<SteamAppId>?platform=steam` is the Steam-bought bridge, seen once in `Link2EA.log`. It doesn't fit EA-app installs.
- **VERIFIED** (code read).
  - `LauncherService.Launch` takes launch targets, then `LaunchUrl` (or a built `steam://` URL), then `LaunchExe`, all via `Process.Start` with `UseShellExecute=true`. An `origin2://` string would route to the handler with no code change.
  - Steam URLs come from `EnginePresets.cs:90-94` and `LaunchScan.cs:69-73`.
  - `MainViewModel.cs:1814` and `LaunchGuard.cs:33-35` know only Steam and exe.
- **INFERRED.** Seam risk. An EA registration with `LaunchExe` set, or with nothing set, would start the raw exe (`LauncherService.cs:154-161`). One with a Steam id launches through Steam (K8).
- **UNKNOWN.** Whether a bare `offerIds` launch picks the Trial launcher, whether it works with the EA app closed or signed out, and whether EA keeps accepting contentIDs as `offerIds`.

**Plan impact the item proposed** (for sub-project 1):
- Add an `ea` launch target that holds only the `origin2` URI, with `authCode` empty and no parameters.
- Open it via the shell handler, never by pathing `EALaunchHelper.exe`. That's a design judgement, not a proven difference.
- Guard the seam:
  - An EA registration never sets `LaunchExe` or `SteamAppId` as a launch driver, and `LaunchScan` adds no exe targets for these ids.
  - A Core test asserts EA targets are only `origin2` URIs.
  - `LaunchGuard` and the mechanism label get an "EA app" case that doesn't reuse the Steam client gate.
- Keep an "Open the EA app" fallback.

Note: B1's scout, working from the logs alone, said the launch mechanism wasn't identified. A8 identified it. A8 is the more specific result.

---

### A5: Steam ids

**Answer.** Both are correct. No corrections needed.

**Status.** Answered. No adversarial check ran.

- **VERIFIED.** 4032350 is "EA SPORTS™ College Football 27": appdetails `success:true`, type game, matching `steam_appid`, DLC [4113870]; store page title matches. https://store.steampowered.com/api/appdetails?appids=4032350
- **VERIFIED.** 3940610 is "EA SPORTS™ Madden NFL 27": appdetails `success:true`, type game, DLC [4113760]; store page title matches. https://store.steampowered.com/api/appdetails?appids=3940610

---

### A6: license review and a clean-room rule

**Answer.** Licenses per repo, read from the actual license text:

| Repo | License as read | Use |
|---|---|---|
| `bep713/madden-file-tools` | MIT, real LICENSE file, "Copyright (c) 2021 Matthew Panetta" | May be followed closely, with the notice carried |
| `bep713/madden-franchise` | MIT declared only in `package.json` and npm. No LICENSE file, no copyright line | Code: MIT as declared. `data/`: EA-derived, not coverable |
| `FMTDev/FMT.Madden26Plugin` | CC BY-NC-ND 4.0 (LICENSE.md); GitHub shows NOASSERTION | Study only |
| `CadeEvs/FrostyToolsuite` | CC BY-NC-ND 4.0 (README License section; no LICENSE file) | Study only |
| `Dyvinia/DatapathFixPlugin` | CC BY-NC-ND 4.0 (LICENSE.md) | Study only |
| `Dyvinia/FrostyFix` | None, all rights reserved | Study only |
| `bphit4/MMC-Frosty-Modding-Tools` | None (only a README; tool ships as .rar) | Study only |
| `DaiyronW/tool` | None | Study only; its schema JSON is EA-derived too |
| `eric-levinson/cfb27-dynasty-modding` | None (includes the `docs/save-format.md` the research used) | Study only |
| `ebiggers/libdeflate` | MIT | Candidate dependency |
| `facebook/zstd` | BSD (LICENSE); COPYING also present (GPLv2, not read) | Candidate dependency; any .NET binding needs its own check |

**Status.** Answered, awaiting your sign-off. No adversarial check ran.

**Key findings**

- **VERIFIED.** `madden-franchise` has no LICENSE, COPYING or NOTICE file. The GitHub API reports license null. Its `package.json` `files` includes `data/**/*`, so the npm package ships the schemas (`C27_468_2.gz`, `M27_620_0.gz`), zstd dictionaries (`26`, `27`, `c27/dict.bin`, `c27/dict-cga.bin`) and interned-string tables under that MIT label.
- **INFERRED.** Those schemas are EA game data. The README says schemas "are located within the game files and can be found using a tool like Frosty". eric-levinson's README says the schema was "extracted from Frostbite CAS". An MIT label from someone who doesn't own the data can't license it. Whether conversion adds copyrightable work is a legal question nobody here has answered.
- **INFERRED.** The c27 zstd dictionary is almost certainly EA's own, not community-trained. Save frames carry dictID 1711034507 and so does `c27/dict.bin`, and a zstd frame names the exact dictionary its encoder used. **The research doc's "from the community, not EA" (around line 287) is probably backwards.** S13 (does it actually decompress) is still UNKNOWN.
- **VERIFIED.** FMT.Madden26Plugin's repo root commits EA roster binaries (`ROSTER-Official.bin`, `ROSTER-Community.bin`, 15+ edited or test rosters) and `Madden26TypeHashDump.json`. `madden-franchise` `tests/data` holds EA save and franchise binaries. None may ever be fetched as fixtures.
- **VERIFIED, needs re-check against the page.** EA User Agreement, Section 2: "You may not reverse engineer or attempt to extract or otherwise use source code or other data from EA Services, unless expressly authorized by EA or permitted by law." Section 7.C's Unauthorized Third-Party Program definition includes programs that "mine" information. The quotes came through a summarizing fetch. https://www.ea.com/legal/user-agreement
- **INFERRED, not legal advice.** Porting format logic in your own words, even from NC-ND or unlicensed sources, is generally lower risk than copying, because facts and methods aren't protected the way expression is. The EA agreement's anti-extraction clause is a separate, contract-based limit.
- **UNKNOWN.** gridirongc.com and news/forum pages: no license found. They're cited as evidence only and nothing from them could ship.

**Proposed clean-room rule (you sign off):**

1. **Study.** Any public source, including unlicensed and NC-ND ones, may be read to learn facts about the format. Nobody pastes their code into Core, a spec or a test.
2. **Port.** Rewrite logic in our own words from a written format note, never side by side with their code. Every rule in the note is confirmed against bytes from your own saves, or cites a file under an explicit license. Only MIT or BSD sources may be followed closely, with their notice added to `THIRD_PARTY_NOTICES`.
3. **Never copy or ship:**
   - schemas, zstd dictionaries, interned-string tables, or anyone's schema JSON
   - anyone's test saves or rosters
   - any EA, NFL or college roster, name or likeness data

   This holds even when a repo is labelled MIT.
4. **Runtime.** Any schema or dictionary the launcher uses must come from the user's own machine or a tool the user installed. The launcher records only where it came from.
5. **Attribution.** Add a NOTICE block, "EA football save research (studied, never bundled)", crediting bep713/Matthew Panetta and others, with URL, license as read, and "never bundled".
6. **CAS extraction (R8(b))** is a separate question for you, given EA UA Section 2.

---

### B1: the finished Madden install, and A4 facts

**Answer.** The finished Madden install and College Football 27 have the same shape. K4 is explained, and K5 holds on two completed installs with corrections. Both contentID and the SFT software id are on disk for both games, and neither is in the registry.

**Status.** Answered. No adversarial check ran.

**Key findings**

- **VERIFIED.** `HKLM\SOFTWARE\WOW6432Node\EA Sports\Madden NFL 27` and the 64-bit view both hold DisplayName, Locale `en_US`, Product GUID `{9B4BB986-25C2-488E-A832-8FD45A907EDB}`, and Install Dir with a trailing backslash. `InstallOptSelect=1` exists only in the WOW6432Node view.
- **VERIFIED.** College Football's key is named **`EA SPORTS College Football 27`**, not `College Football 27` (that query returns not found). It has the same four values: Product GUID `{4BA2CEAA-83EE-4FE0-A688-BF0AE3184EEC}`. The key name equals `<gameTitle>` in `installerdata.xml`.
- **VERIFIED.** Uninstall keys named by Product GUID exist **only** under `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall`, not in the 64-bit view or HKCU.
  - Madden: DisplayVersion 1.0.139.61898, DisplayIcon `Madden27.exe`, UninstallString `…\EAInstaller\Madden NFL 27\Cleanup.exe uninstall_game`.
  - College Football: DisplayVersion 1.0.140.17622, DisplayIcon `CollegeFB27.exe`.
- **VERIFIED.** Zero `_DiP_Staged` files in either tree (Madden 571 files, College Football 564). `installerdata.xml` is present in both.
- **VERIFIED.** contentIDs are unchanged: Madden 16425895 (1.0.139.61898), College Football 16425899 (1.0.140.17622). Both files come from EAInstaller-5.08.02.00, each with exactly one `<contentID>`.
- **VERIFIED.** Every launcher points at `EAAntiCheat.GameServiceLauncher.exe`. The real binaries are `Madden27.exe` / `Madden27_Trial.exe` and `CollegeFB27.exe` / `CollegeFB27_Trial.exe`.
- **VERIFIED.** Why K4's reads conflicted:
  - Madden's `installerdata.xml` LastWriteTime is 17:51:29, the install start.
  - `InstallLog.txt` shows "Installation registry missing. Game not yet installed." at 17:57:53 and 17:58:26, "Setting up registry…" at 17:58:27, and exit code 0 at 17:58:29.
  - `EADesktop.log` shows NotInstalled → Active → Installed at matching UTC times.
- **INFERRED, from two completed installs.** K5 corrections:
  - (a) Look up the key by `<gameTitle>` or by enumerating `EA Sports\*`. Never hardcode a short name.
  - (b) The Install Dir key or the Uninstall GUID key is the completion signal. `installerdata.xml` only confirms.
  - (c) Read the WOW6432Node view.
  - (d) Recommended, untested: also require no `*_DiP_Staged` file. Whether that suffix can coexist with the key, for example during a title update, wasn't observed.
- **VERIFIED, A4.** Where each id lives:
  - contentID: in `<install>\__Installer\installerdata.xml`.
  - SFT software id (Madden `Origin.SFT.50.0001595`, College Football `Origin.SFT.50.0001619`): only as the folder name `C:\ProgramData\EA Desktop\InstallData\<Title>\base-<offerId>\`, which holds `map.eacrc`.
- **VERIFIED.** Neither id is in the registry (`reg query /s /f` over the EA and EA Sports hives: zero matches). No readable persistent EA app state file contains either id or either Product GUID. Only logs do. Encrypted or compressed stores could still hold them unseen.
- **VERIFIED.** EA app logs key the game by contentID almost everywhere: IGO `masterTitleId=[16425895]`, license `contentId[16425895]`, launch `contentIds=[16425899]`, CloudSync `cloudId: [16425899_16425899]`.
  - The SFT id appears only in InstallStore lines.
  - A third family, the OFR offer id (`Origin.OFR.50.0006150` Madden, `Origin.OFR.50.0006233` College Football), appears only in logs, alongside slugs.
  - No Madden cloudId has been logged yet.
- **INFERRED.** contentID looks stable: unchanged for Madden across the in-progress and finished reads, unchanged for College Football across its 9/10 title update, and EA's cloud namespace is built on it. Stability across editions and regions (K19) is still UNKNOWN.
- **INFERRED.** The SFT `base-<offerId>` folder survived the College Football title update. But it lives in ProgramData, not the game folder, so a moved install or an EA app data reset would lose it. A full uninstall/reinstall wasn't observed for either id.

---

### R2: what tells an online-connected save from an offline one

**Answer.** No field in the College Football 27 copies works as an online flag, server league, cloud id or online-mode marker. **The direct question stays UNKNOWN**: there's no online or dynasty sample to contrast against. The copies do give a **positive Road to Glory fingerprint**. It identifies the save kind. It does not prove the save is offline.

**Status.** Partially answered. No adversarial check ran.

**Key findings**

- **VERIFIED.** The build tag is the build that wrote the file, not a mode. Same-kind saves carry different tags (`College-27-RL3_5-9171310` vs `College-27-RL4-9192662`). It sits at 0x22 in RTG and PROFILE, and 0x2E in ROSTER.
- **VERIFIED.** The header length u16 at 0x0A identifies the kind: PROFILE 0x34, ROSTER 0x38, RTG 0x40. Streams start at 0x46, 0x4A and 0x52. Decompressed sizes: PROFILE 15,743; ROSTER 14,258,628; RTG 31,164,890 and 31,174,280.
- **VERIFIED.** The u32 at 0x3E means schema major (833) only in RTG. In PROFILE it's the decompressed length. In ROSTER it falls inside the build string.
- **VERIFIED.** The header timestamp is the last write time (matches mtime, as C2a found).
- **INFERRED.** `DynastyServer_*Flow` / `FranchiseServer_*Flow` / `RequestServer` table names are the local franchise-server architecture, not network connections. They're in every RTG save and defined as ordinary flow records in C27_468_2.
- **VERIFIED.** Schema C27_468_2 has no attribute or enum named Online, IsOnline, Cloud (weather aside), Nucleus, OwnerId or GameMode. `LeagueSetting.LeagueType` is roster scope (ALL, COACHES_ONLY…), not online vs offline.
- **VERIFIED.** The decoder is trustworthy:
  - `LeagueSetting` (143), `Franchise` (49) and `FranchiseUser` (36) have exactly the 468 member counts. Widths match except for chunk-end padding cases.
  - `Franchise.LeagueSetting` resolves to the save's own LeagueSetting table.
  - The decoder is a port of public `madden-franchise` offset-table logic, used as a study/scratch tool only.
- **VERIFIED** (league configuration values, not career data). The active LeagueSetting row reads: `IsSuperstar=1`, `MaxUsers=1`, `IsCrossplayEnabled=0`, `IsPublicEnabled=1`, `FranchiseStyle=Legacy`, `LeagueType=ALL`, `ScheduleManagerType=GeneratorType`, `LeagueStartingPoint=PRESEASON`, `IsPrologueActive=0`.
- **INFERRED.** `IsPublicEnabled=1` is not an online signal: the schema default is True and it's set in a single-player RTG save.
- **VERIFIED.** Account-bound ids appear even in single-player RTG:
  - `Franchise.LeagueID` is non-zero.
  - 1 of 32 `FranchiseUser` rows has non-zero user ids and `AdminLevel=Owner`.

  So a non-zero LeagueID or user id is **not** evidence of an online league. Values are not reproduced here.
- **INFERRED.** Candidate RTG fingerprint for a fail-closed allowlist:
  - header length 0x40, stream at 0x52, schema 833 at 0x3E
  - LeagueSetting, Franchise and FranchiseUser at 468 member counts
  - active `IsSuperstar=1` and `MaxUsers=1`
  - exactly one populated FranchiseUser

  Only one career was sampled.
- **UNKNOWN.** Any online/offline discriminator for RTG, dynasty or online dynasty. `MaxUsers>1`, crossplay or a server-issued LeagueID might differ; untested.
- **VERIFIED.** The RTG tail has no further zlib streams but carries 5,794 and 5,715 zstd frame magics. Their contents are UNKNOWN (no stdlib zstd in Python 3.13).
- **VERIFIED.** PROFILE-COLLEGE decompresses to a Blaze-TDF-style tagged tree that parses to the end. It has no franchise tables.
- **INFERRED.** PROFILE's `RGGT` list (11 slots) has an `RGGM` int. It is 16 on the two RTG entries, 4 on one unnamed entry, -1 on unused ones. Probably a game-mode code with 16 = Road to Glory. Usable as a secondary cross-check, not as an online marker.
- **UNKNOWN.** PROFILE `USTA.USON = 0`, plausibly an "online" counter. Meaning not established.
- **INFERRED.** ROSTER-Official has no franchise tables or mode structure. Keyword hits (Blaze, Cloud) are public player, school and place names.
- **VERIFIED.** `UserSettings.dat` (144 bytes) has no identifiable mode field.
- **VERIFIED** (research B1/B12, not re-probed). All five synced files share one cloudId, so sync isn't a signal.

**Plan impact:**
- A College writer accepts only the RTG fingerprint. It refuses dynasty saves, `MaxUsers>1`, `IsCrossplayEnabled=1`, and any unknown header length, build or schema.
- Drop LeagueID, user id and `IsPublicEnabled` from any proposed online test.
- Add zstd tail decoding to the backlog.
- Repeat R2 on Madden once saves exist.

---

### R4: which PROFILE-COLLEGE summary fields mirror RTG contents

**Answer.** From the existing copies, 9 of the 13 summary fields map to an RTG source. Field 10, which the research read as a rating, is **not** `OverallRating`. Fields 6, 7, 11 and 12 are still open.

**Status.** Partially answered: only one independent before/after comparison exists, because RTG-E has no profile entry and is byte-identical in payload to the dated autosave. No adversarial check ran.

**Field map**

| Field | Source | Label |
|---|---|---|
| 0 | On-disk RTG file name (not stored in the payload; also the profile's `ca7ba9` tag) | VERIFIED |
| 1 | `Franchise.LeagueID`: stored bits = value XOR 0x80000000 (biased signed int); also the profile's career-id tag; shared by every save of a career | VERIFIED (bias as the game's encoding: INFERRED) |
| 2 | `Team.DisplayName` (equal to `LongName`) of the row where `TeamIndex` = user Player's `TeamIndex` | VERIFIED (which of the two names: UNKNOWN) |
| 3 | Opponent in the `SeasonGame` at current year and week involving the user team; `" @ "` prefix exactly when the user team is away | VERIFIED |
| 4, 5 | User Player's `FirstName` / `LastName`, found via `PrologueProgress.SuperstarPlayer`; the same strings are in `PrologueProgress.OriginalFirstName/LastName` | VERIFIED (which is the true source: UNKNOWN) |
| 6 | Empty in both saves | UNKNOWN |
| 7 | Probably `SeasonInfo.CurrentWeekType` or `CurrentStage`; unchanged in both | INFERRED |
| 8 | `SeasonInfo.CurrentWeek + 1` (didn't move between saves) | VERIFIED |
| 9 | `SeasonInfo.CurrentYear + 1` (moved with CurrentYear) | VERIFIED |
| 10 | **Not** `Player.OverallRating`: it differs from it in both saves, and stayed put while OverallRating changed | VERIFIED |
| 10 | Probably the user team's logo id (`TEAM_LOGO` and alternates; unique to 1 of 143 teams) | INFERRED |
| 11, 12 | No source; 52 and 342 candidate fields match in both saves | UNKNOWN |

**Other findings**

- **VERIFIED.** The profile slot index is a TDF list of 11 structs, each with 6 tagged fields: career id string, a state int (16 on occupied, 4 on one empty slot, -1 on unused), RTG file name, slot index 0–10, an int that's 63 everywhere, and the 13-field pipe summary. Meanings of the state int and 63 are UNKNOWN.
- **VERIFIED.** Only 2 of 11 slots hold a career. RTG-E has no entry.
- **VERIFIED.** RTG-E and the dated autosave have byte-identical decompressed payloads (same SHA-256).
- **INFERRED.** Each profile entry is a snapshot written when that slot saves. The older entry still describes its own payload even though the profile was rewritten later.
- **INFERRED.** Decoding used public schema 468 against save schema 833 with no member-count mismatches. Same-width renames can't be ruled out.

**Plan impact:**
- A same-game writer that moves a character must rewrite the matching summary (names, and school if `TeamIndex` changes) in the same change set, or refuse.
- Q5(b)'s field edit should be a Player attribute or rating. Names, `TeamIndex`, LeagueID and season/schedule fields are mirrored and must not be picked.
- A writer that adds slots should refuse until the state int's meaning is known.

---

### Q4: container round trip in Core

**Answer.** Yes, in memory, for all three kinds. For RTG, only `SmallestSize` fits.

**Status.** Answered. **Adversarial verdict: partially confirmed.** The verdict corrects the stress claim, the slack label and the compressor conclusion.

**Key findings**

- **VERIFIED** (verifier re-measured). Header offsets:
  - u32 at 0x0E = file size − data offset in every file: RTG 9,646,899; PROFILE 87,632; ROSTER 12,582,912.
  - The only zlib headers: `78 9C` at 0x52 (RTG), `78 DA` at 0x46 (PROFILE) and 0x4A (ROSTER).
  - u16 at 0x0A + 0x12 = data offset.
- **VERIFIED.** RTG: u32 at 0x4A is the exact compressed length (6,798,409 and 6,834,733). Adler-32 matches. No header u32 equals the decoded length.
- **VERIFIED.** PROFILE: decompressed length at 0x3E (15,743), no compressed-length field. The 5,891-byte stream is followed by zeros only.
- **INFERRED.** PROFILE u32 at 0x42 (`EB 07 05 00`) reads as u16 2027 + u16 5, likely a version tag. The roster has `EB 07` at 0x16.
- **VERIFIED.** ROSTER: decompressed length at 0x12 (14,258,628). u32 at 0x1A `0x0D5B58BC` = CRC-32/BZIP2 of the decoded data (hand-rolled CRC passes the `123456789` check value `0xFC891918`). u32 at 0x1E = capacity + 0x38. Stream 9,763,919 bytes, then 2,818,993 zero bytes.
- **VERIFIED.** RTG zero gap to the first non-zero tail byte:
  - 431 bytes for RTG-E and the dated autosave (0x67BC9B → 0x67BE4A).
  - 637 bytes for RTG-E-AUTOSAVE (0x684A7F → 0x684CFC).
  - The tail is 2,848,490 / 2,812,166 bytes long, of which only 524,482 / 544,939 are non-zero.
  - **"About 2.8 MB after the stream" is the tail length, not slack.**
- **Verdict correction, INFERRED risk.** The 431/637 figure is an **upper bound**, not verified slack. The tail begins with a u16 (`0x0064` / `0x0061`) then zstd magic `28 B5 2F FD`, and no u32 anywhere points at it. Some of those zero bytes could belong to a structure; meaning UNKNOWN. This doesn't change the SmallestSize fit, which has a 179–200 KB margin.
- **VERIFIED.** .NET re-deflate sizes (verifier's numbers match exactly):

  | File | Original | Optimal | SmallestSize | Fastest | Fits (budget) |
  |---|---|---|---|---|---|
  | RTG-E | 6,798,409 | 6,826,286 | 6,619,977 | 9,054,351 | SmallestSize only (178,863 headroom) |
  | RTG-E-AUTOSAVE | 6,834,733 | 6,853,595 | 6,635,461 | 9,168,179 | SmallestSize only (199,909 headroom) |
  | PROFILE | 5,891 | 5,965 | 5,908 | 7,594 | All |
  | ROSTER | 9,763,919 | 9,798,068 | 9,748,805 | 11,335,443 | All |

- **VERIFIED.** Every level that fits gives the same file size and an identical tail from the first non-zero byte to EOF.
  - Re-inflated payloads are byte-identical.
  - RTG changes only 2–3 bytes at 0x4A; PROFILE and ROSTER headers are unchanged.
  - The roster CRC still matches after the rebuild.
- **VERIFIED.** A one-byte edit moves SmallestSize output by about −2 to +13 bytes.
- **Verdict correction.** The item's "a 262,144-byte random overwrite still fits RTG with 91–110 KB left" **only holds at the payload midpoint**, the one position the item tested.
  - At 0.10, 0.25, 0.33, 0.75 and 0.90 of the payload, a 262 KB overwrite **overflows**: RTG-E from −11,512 to −84,756 bytes, RTG-E-AUTOSAVE from −59 to −61,304.
  - 131,072 bytes fit at every position tested (minimum headroom 45,805), as does 65,536.
  - **The safe edit-size bound observed is about 131 KB, not 262 KB.** Single-character transport is well inside it.
- **VERIFIED.** SmallestSize output is deterministic.
  - .NET `ZLibStream` throws on a flipped Adler bit but **does not throw on a stream truncated by one byte**.
  - So a Core reader must verify the Adler-32 trailer and exact stream length itself.
- **Verdict correction, VERIFIED.** The item only inferred that EA's compressor isn't .NET's. The verifier found that **stock zlib 1.3.1 reproduces EA's streams byte for byte**: level 6 for both RTG files, level 9 for PROFILE and ROSTER. A byte-exact repack of unchanged data is therefore possible with stock zlib. "Pin SmallestSize, close S8a" is **one option**, the no-dependency one. A stock-zlib binding or managed port is another, and that's your dependency decision.
- **UNKNOWN.** The Player table's location in the decoded RTG wasn't pinned. The edit sites aren't proven to be inside it. Headroom conclusions don't depend on that.
- **UNKNOWN.** Whether either game accepts a re-packed file. That's Q5, Part B.

---

### A3: ban risk for non-Steam games (evidence)

**Answer.** K8 holds against today's code. Every ban-risk decision keys on the Steam id, and a College Football 27 registered without one resolves to None. The choice is laid out in section 4.

**Status.** Answered (as a brief). No adversarial check ran.

**Key findings**

- **VERIFIED.** `BanRiskCatalog.Build` maps only `g.Stores.SteamAppId`. `ByAppId(null/empty)` returns `GameBanRisk.None`. Cached per `EffectiveManifest.Generation` (`BanRiskCatalog.cs:34-48`).
- **VERIFIED.** Exactly five callers, all passing the Steam id:
  - banner `MainViewModel:406`
  - state chip `MainViewModel:486`
  - enable gate `MainViewModel:1309`
  - library badge and filter `LibraryViewModel:465` (filtered at `:506`)
  - MCP agent enable `WriteTools:64`

  `ShouldGateEnable` is called only from `MainViewModel:1311` and `AgentWriteRules:61`.
- **VERIFIED.** Acks are already stored by `GameEntry.Id` in `ban-risk-acks.json`. No option needs an ack migration if the Id doesn't change.
- **VERIFIED.** The code already joins registration to manifest by id (`Scanner.cs:63`). `ManifestIdLookup.BySteamAppId` has the generation-cached map shape a `ById` map would copy.
- **VERIFIED.** `GameEntry.Id` isn't guaranteed to equal the manifest id:
  - `UniqueId` appends `-2`, `-3` on a collision (`EnginePresets.cs:49-61`).
  - Older ids were slugified from typed names (`ManifestIdLookup.cs:8-16`).
  - `EffectiveManifest.Merge` folds snapshot aliases away (`EffectiveManifest.cs:49-72`).
- **VERIFIED, this machine only.**
  - Every registered game the feed flags already has an id equal to its feed id, so id-based resolution drops nothing locally. Other users' registries: unknown.
  - The feed has 156 entries, 32 flagged, none flagged without a Steam id, and no duplicate Steam ids.
- **VERIFIED.** No runtime code reads `SafeRoute` or `SafeRouteHint`.
- **VERIFIED.** The feed row `ea-sports-college-football-27` carries Steam id 4032350, `banRisk: high`, `safeRoute: offline`. Nothing in launcher `src` names that id.
- **VERIFIED.** `StoreIds` has four fields and `MergeStores` lists them by hand (`GameManifest.cs:7-13`, `EffectiveManifest.cs:123-129`). A fifth field that misses `MergeStores` would be silently dropped.
- **VERIFIED.** The save editor has no game or ban-risk input (`SaveEditorService.cs:22-53`). Its only caller, `SavesDialog.OnEditCharacter`, holds `_game` and already runs a running-game gate.
- **VERIFIED.** `SavesDialog` has other save-tree write paths: `SaveModInstaller.ResetWorld`/`RemoveWorld`, `SaveManager.RestoreType`/`Restore`/`RestoreWorld`, `SaveBundle.Restore`, and Core's `SaveModFlow.InstallWorld`. No MCP tool writes saves.
- **INFERRED.** A save-editor gate keyed on High adds a new prompt for current Elden Ring users (feed: high).
- **UNKNOWN.** Whether the EA contentID is stable across patches, regions and the trial build.
- **INFERRED.** Older binaries would ignore a new `eaContentId` feed field (System.Text.Json skips unknown properties, and `ManifestJson.Options` doesn't change that). Not tested against `ManifestValidator`.

---

## 4. Decision briefs

### A3: how ban risk resolves for games with no Steam id

**The problem.** Register College Football 27 or Madden from the EA app and it has no Steam id. Under today's code it gets no banner, no chip and no enable gate, even though the feed says high. If you register it *with* a Steam id, it launches through Steam (K8). C1 (the failing test) comes first under every option.

| | **A: manifest id, Steam id kept** | **B: EA content id** | **C: both** |
|---|---|---|---|
| Resolver | `For(GameEntry)` = max(ByAppId(SteamAppId), ById(Id)) | max(ByAppId, ByEaContentId) | max over all three |
| Schema / on-disk change | None | `StoreIds.EaContentId` + a `MergeStores` line + merge test; `GameEntry.EaContentId` persisted camelCase + round-trip test + camelCase rules list; ManifestMiner overrides; feed overrides for both games | Union of A and B |
| Call sites | Same five | Same five | Same five |
| Ack migration | None | None | None |
| Steam games | Unchanged, *only if* the Steam path is kept. If manifest id **replaces** Steam id, registrations whose Id differs from their manifest id (typed-name slugs, `-2` suffixes, folded aliases) lose their risk | Unchanged | Unchanged |
| Where it can miss | EA import must set Id to the exact feed slug; a `-2` collision (Steam + EA copies of one game) misses, so the C3 compiled floor stays necessary; the slug must exist in the feed or embedded manifest | Depends on the feed carrying the id and the id staying stable (K19, UNKNOWN); manual or non-detected EA adds get nothing | Least likely to miss |
| Side effect | A custom game whose slug matches a flagged id gains a gate (fails safe) | None known | Same as A |
| Surface changed | Smallest | Medium | Largest |

**Tests** under every option: the resolver lives in Core so the App call sites stay thin.
- **A:** ById resolves; Steam id and Id both set, max wins; Id differs from manifest but Steam id matches, still high; folded alias still resolves via Steam id; `-2` Id misses (pins why C3 exists); `AgentWriteRules.CanEnable` refuses an EA registration with null Steam id.
- **B:** the same shapes keyed on content id, plus the camelCase round-trip and merge tests.

**Riding along with A3, still open for you:**
- **D2.** Is the save-write ack separate from the mod-enable ack?
- **Which save paths the gate covers.** Only the character editor, or also restore, reset, remove, bundle restore and `InstallWorld`? Restoring your own snapshot arguably makes things safer.
- **Elden Ring.** Is a new save-editor prompt for Elden Ring users acceptable?
- **Under B or C:** approve `eaContentId` in the manifest schema and the `626-game-manifest` overrides.

The plan recommended A. The evidence doesn't overturn that, but A is only safe as "Steam id **or** manifest id, higher wins".

### A4: the EA store key

**The problem.** The schema, `EffectiveManifest.MergeStores` and the `EveryOptionalFieldMerges` test change together, so pick once.

| | **contentID** (e.g. 16425899) | **SFT software id** (`Origin.SFT.50.xxxxxxx`) | **OFR offer id** (`Origin.OFR.50.xxxxxxx`) |
|---|---|---|---|
| Where it lives on disk | `<install>\__Installer\installerdata.xml`, inside the game folder (VERIFIED) | Folder name under `C:\ProgramData\EA Desktop\InstallData\<Title>\base-<id>\` (VERIFIED) | EA app logs only (VERIFIED) |
| In the registry | No (VERIFIED) | No (VERIFIED) | No |
| What EA's runtime keys on | License, overlay (`masterTitleId`), launch (`offerIds`), cloudId base (VERIFIED, logs) | Install state only (VERIFIED, logs) | Store offer, resolved from contentID at launch (VERIFIED, logs) |
| Survived a title update | Yes, College Football 9/10 (INFERRED) | Folder persisted through it (INFERRED) | Not observed |
| Survives a moved install / EA app data reset | Travels with the game folder (INFERRED) | Would be lost (INFERRED) | n/a |
| Doubles as the launch key | Yes, `origin2://…offerIds=<contentID>` (VERIFIED College Football, INFERRED Madden) | Not observed | Older Origin-era form (community) |
| Cross-edition / region stability (K19) | UNKNOWN; the name `masterTitleId` hints at one id per title across offers | UNKNOWN | UNKNOWN |

**What would settle K19.** A second edition or locale install. Not available here.

The plan's recommendation was contentID. This round's facts lean the same way. The OFR id is weak as a local detection key because it lives only in logs.

---

## 5. What still needs the owner

**Decisions**

1. **D4, the section 2 option.** Choose with B10 still UNKNOWN (leaning no toggle). Option 2 needs a toggle the evidence doesn't support.
2. **A3.** Choose A, B or C. Under A, confirm the Steam-id path is **kept**, not replaced.
3. **A4.** contentID, SFT id, or (weakly) OFR id.
4. **A6 sign-off** on the clean-room rule (section 3, A6).
5. **Runtime use of EA-derived data.** May the launcher use a schema or zstd dictionary the user obtained themselves, given EA User Agreement Section 2? This decides Q8 and R8(b).
6. **D5 / R8.** Where field names come from. A6 makes route (a), layout from each save's own table headers plus a clean-room name map, the only route with no licensing exposure. Route (b), reading the CAS files, is yours to rule on.
7. **Legal opinion.** Whether to get one before anything depending on EA-derived schema or dictionary data ships, especially in a Store build (policy 11.2).
8. **Compressor.** Pin .NET `SmallestSize` (no dependency, fits with 179–200 KB margin), or take a stock-zlib binding or port for byte-exact repacks. Log whichever you pick to the decisions log.
9. **D2 and the gate's reach.** Separate save-write ack? Which `SavesDialog` write paths count? Is a new Elden Ring save-editor prompt acceptable?
10. **Under A3 option B or C.** Approve `eaContentId` in the schema and the feed overrides.
11. **EA row UX.** Does an EA row show Play (the undocumented `origin2` URI) or only "Open the EA app"?
12. **R1 evidence bar.** Are 2023 EA-staff answers enough for "no toggle", or is an EA support ticket (an account action) worth filing?
13. **Optional.** Ask bep713 / Matthew Panetta to add a LICENSE file with a copyright line to `madden-franchise`. That firms up the code grant only, not the EA-derived data.

**Look, don't click**

14. In the EA app, open College Football 27's three-dot menu → **View Properties → Saved data**, and the app **Settings**. Note whether any cloud-save toggle exists in today's build. This is the only thing that could revive option 2.

**Madden (plan Band B)**

15. **B2.** Create the Madden saves 1–9 from the plan's list (player career, coach career start and week 2, the jersey one-change pair, official roster, the custom-roster one-change pair, a shared-roster download if you choose, the setup-screen look, and screenshots). Note anything that forced you online.
16. **B4.** Madden's process name in Task Manager while it runs.
17. **B5.** Your go-ahead for read-only, hash-verified copies of the new Madden files.
18. **Launch smoke.** One launch of Madden via `origin2://game/launch/?offerIds=16425895&cmdParams=`, by you, to confirm the contentID works. Add it to `docs/smoke-tests/pending.md` when sub-project 1 lands.

**College Football (plan items 10 and 11, plus what this round added)**

19. **Item 10.** Screenshots of your Road to Glory character: full ratings, dev trait, abilities, class year, redshirt status, archetype. Checks S16 and Q3.
20. **Item 11, the one-change RTG pair.** Preferably **one attribute point** saved to a **new slot**, plus copies of the PROFILE-COLLEGE written at each save. That gives R4 a null control (attribute edits shouldn't move summary fields) and shows how the game writes a new slot entry.
21. **Optional.** A second pair that differs only by advancing one week, or crossing a week type. Shows field 8 moving and could identify fields 7, 11 and 12.
22. **Optional.** A name or team edit on a disposable slot. Separates `Player` vs `PrologueProgress.Original*` as the source for fields 4 and 5, and tests field 10 against the team logo.
23. **Optional.** An offline College Football dynasty save (game closed before copying). Gives dynasty its own kind fingerprint and pins its `RGGM` code.
24. **Outside the current boundary.** A true online/offline discriminator needs an online dynasty save to contrast with. Creating one is an online account action, so it waits on your section 2 decision. Until then the writer refuses every league save except the RTG fingerprint.

---

## 6. Not done this round, and why

| Plan item | Why not |
|---|---|
| A1 (back up the College Football folder) | Owner task |
| A2 (friend's stale-data questions) | Owner task |
| B2 (create Madden saves), B4 (Madden process name) | Owner tasks; no Madden save or roster bytes exist yet |
| B3 (Madden synced file set and save folder from logs) | Needs Madden played and closed once; no Madden save folder exists |
| B5 (copy Madden saves) | Nothing to copy yet; needs your go-ahead |
| Q1 (Madden container) | Needs Madden bytes (B5) |
| Q2 (Madden Player layout vs schema 620) | Needs Madden bytes and your save-1 screenshots and one-change pair |
| Q3 (College field meanings in build 833) | Needs your RTG screenshots (item 10) and the one-change RTG pair (item 11) |
| Q8 (appearance decode) | Needs a NuGet zstd package (not allowed this round) and a dictionary A6 now says can't be bundled; waits on your runtime-use decision |
| R3 (imported vs non-imported Madden character) | Needs the section 8 captures (1) and (2) |
| R5 (roster TDB2 layout across games, roster one-change pair) | Needs Madden roster copies (Band B saves 5 and 6) |
| R6 (Madden Player references, `Original*Rating`) | Needs Madden franchise copies (saves 2 and 3) |
| R7 (translation loss report) | A Core test that needs field meanings (Q3) and, for the reverse direction, Madden copies. It also means writing into the repo, which this read-only round didn't do |
| R8 (field-name route) | Owner decision (D5); A6's findings are the input |
| Band C (C1–C5) | Code in the repo; this round was read-only on the repo. C4 is a data PR in `626-game-manifest`, also read-only this round |
| Band D (D1–D5) | Owner decisions |
| Part B (Q5, Q7) | Gated on option 1 or 2 and owner-run; not in scope |

Items that failed to return: none.

---

## 7. Plan rows this updates

### Section 4 evidence

| Id | Old status | New status | Change |
|---|---|---|---|
| K3 | VERIFIED | VERIFIED (re-confirmed on the finished Madden) | Add: College Football's registry key and InstallData folder use the full title `EA SPORTS College Football 27`. |
| K4 | VERIFIED, but the reads conflict in time | VERIFIED | Explained: `installerdata.xml` is written at install start, the registry key in the installer's last step (Madden `InstallLog.txt` + file times). |
| K5 | INFERRED on one completed install | INFERRED on two completed installs | Reword: key lookup by `<gameTitle>`; Install Dir key or Uninstall GUID key (WOW6432Node only) is the completion signal; `installerdata.xml` is supporting; also require no `*_DiP_Staged` (untested). |
| K7 | VERIFIED | VERIFIED (re-confirmed on both games) | Real binaries: `Madden27.exe` / `_Trial`, `CollegeFB27.exe` / `_Trial`. |
| K8 | VERIFIED (confirmed) | VERIFIED (re-confirmed) | Exactly five call sites: `MainViewModel:406, 486, 1309`, `LibraryViewModel:465`, `WriteTools:64`. |
| K12 | UNKNOWN | VERIFIED | Steam appdetails + store page. |
| K13 | VERIFIED (confirmed) | VERIFIED | Steam id 3940610 is now verified, not mined-only; the curated override is still missing. |
| K14 | UNKNOWN | VERIFIED | Steam appdetails + store page. |
| K19 | UNKNOWN | UNKNOWN | Add INFERRED support: contentID survived a College Football title update and is EA's license, launch and cloudId key. Needs a second edition or locale. |
| K20 | UNKNOWN | VERIFIED (College Football) / INFERRED (Madden) | `origin2://game/launch/?offerIds=<contentID>&cmdParams=` via the HKCR handler; not EA-documented. |
| S3 | VERIFIED (confirmed) | VERIFIED (re-confirmed across all RTG copies) | Compressed length at 0x4A matches in all three. |
| S4 | VERIFIED (confirmed) | VERIFIED (re-confirmed) | Add: PROFILE u32 at 0x42 reads as 2027 + 5 (INFERRED version tag); ROSTER u32 at 0x1E = capacity + 0x38. |
| S5 | VERIFIED on one unmodified roster | VERIFIED, and still matches after a .NET rebuild | |
| S7 | VERIFIED | VERIFIED, corrected | The tail is ~2.8 MB long but only 524–545 KB is non-zero. It starts with a u16 then zstd magic; no pointer to it found. |
| S7a | VERIFIED on one copy | VERIFIED on all three RTG copies, as an upper bound | 431 / 431 / 637 zero bytes to the first non-zero byte; whether any belong to a structure is UNKNOWN. |
| S8a | INFERRED; .NET unmeasured | VERIFIED (measured) | .NET SmallestSize fits RTG (179–200 KB headroom); Optimal and Fastest don't. Stock zlib reproduces EA's streams byte for byte (level 6 RTG, level 9 PROFILE and ROSTER). Observed edit bound ~131 KB at every position tested. |
| S9 | UNKNOWN / untested | UNKNOWN / untested | Unchanged; Part B. |
| S11 | Index and layout VERIFIED; meanings INFERRED | 9 of 13 fields VERIFIED to RTG sources; the "overall rating" field corrected | Field 10 is not `OverallRating` (VERIFIED); probably team logo id (INFERRED). Fields 6, 7, 11 and 12 open. Slot struct layout VERIFIED. |
| S12 / S13 | VERIFIED (ID match) / UNKNOWN | Unchanged | Add: the dictionary is INFERRED to be EA's own. Research line ~287 ("from the community, not EA") should flip. |
| S14 | VERIFIED | VERIFIED (re-confirmed) | |
| B2 | UNKNOWN | UNKNOWN | Add: a conflict prompt exists (2026 UI strings VERIFIED). "Each choice overwrites the other side" is VERIFIED for a 2025 build, INFERRED for today's. A "Restore latest local save" backup exists. Trigger UNKNOWN. EA's offline docs are silent on cloud saves. |
| B3 | VERIFIED | VERIFIED | Add Section 2's anti-extraction clause and 7.C "mine" (quotes to re-check against the page). |
| B8 | VERIFIED (as listed) | VERIFIED, corrected | FMT plugin CC BY-NC-ND 4.0 (not NOASSERTION); FrostyToolsuite and DatapathFixPlugin CC BY-NC-ND 4.0; FrostyFix, MMC unlicensed; `madden-franchise` MIT with no copyright line. Dictionary and schema redistribution: UNKNOWN → not shippable (EA-derived, INFERRED). |
| B10 | UNKNOWN | UNKNOWN, leaning no toggle | EA docs silent; 2023 staff say no settings; no toggle string in today's partial UI cache. |
| B12 | UNKNOWN | UNKNOWN | Add: RTG kind fingerprint (INFERRED). LeagueID, user id and `IsPublicEnabled` are **not** online signals (all set in single-player RTG). |

### Sections 5 and 6 items

| Item | Old status | New status |
|---|---|---|
| A3 | Owner decision | Brief ready (section 4); still owner |
| A4 | Owner decision | Brief ready (section 4); still owner |
| A5 | Agent, not started | Done, no corrections |
| A6 | Agent, owner signs off | Review done, rule drafted; awaiting owner sign-off |
| A7 / R1 | Agent, not started | Partially answered: recorded UNKNOWN, leaning no toggle. Owner's live-UI look still open. The no-go row "R1 finds no user-controlled sync toggle, under option 2" applies unless that look finds one |
| A8 | Agent, not started | Answered; sub-project 1 launch design unblocked |
| B1 (Band B) | Agent, waiting on install | Done |
| Q4 | Code, not started | Done in a throwaway app: go criterion met; compressor choice passed to the owner (SmallestSize vs stock zlib) |
| R2 | Agent, not started | Partially answered: RTG fingerprint, online state UNKNOWN; writer refuses all else |
| R4 | Agent, not started | Mostly mapped (9 of 13); needs the one-change pair for the rest |
| R8 | Only if needed | Now likely needed: A6 makes route (a) the only licensing-clean route; owner decides (D5) |
| Q8 | Code, conditional on A6 | Blocked: A6 rules out a bundled dictionary; waits on the runtime-use decision |
| "Compressor and tail layout: deferred" | Deferred until Q4 measures | Measured: SmallestSize fits under the conservative rule; stock zlib is byte-exact; the refuse-on-overflow rule stands, plus explicit Adler-32 and stream-length verification in the reader |