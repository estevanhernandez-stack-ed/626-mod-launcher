# Richer Steam detection — grounding + design space

> Research / grounding doc for the brainstorm. Fuses two lenses: first-principles
> (what's actually on disk + what clean architecture allows) and competitive/vibe-iterate
> (what's shipped elsewhere, and our differentiated angle as a *mod manager*, not a library
> launcher). Confidence tags are honest — `observed-on-disk` means I (or the research stream)
> read the actual file on this machine; anything weaker says so.

**Date:** 2026-06-14
**Author:** The Architect (research synthesis)
**Status:** pre-spec. Scope is the human's call; the recommendation is below.

---

## The one-line problem

We make the user Browse to a folder we already parsed, and we throw away most of the
Steam state we already open the file to read. Steam writes everything a mod manager needs to
local disk — install path, build id, install-state, size, last-updated, artwork — with no
login, no Web API, no key. Other apps (the Xbox app, Playnite, Epic) read it. We read three
fields of it and tell the user to go find the rest.

---

## Ground truth — verified against the repo

Every claim below was checked against the actual source on 2026-06-14, not assumed.

| Claim | Status | Evidence |
|---|---|---|
| `ParseAppManifest` pulls only appid/name/installdir | verified | `src/ModManager.Core/SteamParse.cs:45-52` — three regex captures, nothing else |
| `AppManifest` record is 3-field | verified | `SteamParse.cs:56` — `record AppManifest(string? AppId, string? Name, string? InstallDir)` |
| `SteamGame` record is 3-field | verified | `src/ModManager.App/Services/SteamService.cs:10` |
| Popular-pick leaves the folder blank | verified | `AddGameDialog.xaml.cs:239-273` — sets Name/ModPath/Steam, never `FolderBox`. Comment at line 237-238 says so out loud. **`steamGames` is NOT stashed as a field** — the ctor consumes it locally (lines 57-83), so the autofill fix must stash it first. |
| `SteamUserData` save branch returns null | verified — *but* | `GameProfileResolver.cs:111`. **Caveat:** this is a fallback-of-a-fallback — `saveDir ??= ExpandSaveRoot(...)` at line 59 only runs after `SaveLocator.DetectAsync` (Ludusavi-first) misses. The comment "Ludusavi covers these games" is accurate. Lower value than a raw "dead branch" read implies. |
| Steam list / popular picker are text-only | verified | `AddGameDialog.xaml:73` (`DisplayMemberPath="Display"`) and `:66` (`DisplayMemberPath="Name"`) |
| Test fixture already carries unparsed `StateFlags` | verified | `tests/ModManager.Tests/SteamParseTests.cs:41` — `"StateFlags" "4"` sits in the ACF, untouched by the parser |
| `GameEntry` is the right home for a persisted build id | verified | `src/ModManager.Core/GameEntry.cs:42` already holds `SteamAppId`; no build-id field exists |

---

## On-disk data catalog (what's actually there)

All `observed-on-disk` — read on this machine. Steam path: `C:\Program Files (x86)\Steam`
(plus a second library at `G:\SteamLibrary` via `libraryfolders.vdf`).

### appmanifest_*.acf — per-installed-app state  `observed-on-disk`
`<lib>/steamapps/appmanifest_<appid>.acf`. We open this file *already*. It carries far more
than we read:

- **appid / name / installdir** — what we use today.
- **buildid** — the exact installed build. This is what mod/version compatibility actually
  hinges on. **Free** to capture (one more regex).
- **StateFlags** — bitmask. `4` = fully installed/playable; update-required and missing-files
  states differ. Lets us *not* let a user mod a game mid-update or corrupt.
- **SizeOnDisk** (bytes), **LastUpdated** (unix epoch), **LastPlayed** (unix epoch, 0 = never).
- LastOwner (a SteamID64 — account identifier; do **not** surface/persist without reason),
  BytesToDownload/Downloaded, InstalledDepots, UserConfig/MountedConfig language.

Reliability: **High.** Stable Valve KeyValues text format, one file per app, line-oriented
regex parse already proven in `SteamParse`. Caveat: ACF is undocumented Valve format — treat
new fields as best-effort, tolerate absence (the existing parser already does).

Privacy: Low-medium. installdir/name/size/buildid benign. LastPlayed reveals usage timing.
LastOwner is an account id.

### libraryfolders.vdf — library roots + per-app size map  `observed-on-disk`
`<steam>/steamapps/libraryfolders.vdf`. We parse `path` already. We ignore the `apps{}` map
(appid -> size-on-disk bytes) that's also in there — a cheap cross-index of *what's installed
where + how big* without opening every ACF. Privacy: none (install topology + sizes).

### Library artwork cache  `observed-on-disk`
`<steam>/appcache/librarycache/<appid>/<sha1>/<assetname>`. This machine uses the **newer
per-appid hashed-subfolder layout**, NOT the legacy flat `<appid>_header.jpg`. Confirmed
assets: `library_capsule.jpg` (the 600x900 grid/portrait art), `library_header.jpg` (wide),
`library_hero.jpg` (banner) + `library_hero_blur.jpg`, `logo.png` (transparent overlay).

Reliability: **Medium.** Asset filenames are stable; the parent sha1 folder is NOT predictable
— you must enumerate subfolders and match by trailing filename (`glob <appid>/**/library_*.jpg|logo.png`).
Older clients / not-yet-cached apps may be missing assets — fall back gracefully. Legacy flat
layout still exists in the wild; support both. Privacy: none (public store art cached locally).

This is exactly the "looks like a real launcher" lever the user noticed in the Xbox app.

### Per-user data — HIGH privacy, mostly out of lane

- **localconfig.vdf** `observed-on-disk` — `<steam>/userdata/<accountid>/config/localconfig.vdf`.
  The only on-disk source for cumulative **Playtime** (minutes) + per-app **LaunchOptions**;
  also carries LastPlayed (but the ACF is a safer last-played source — localconfig is big,
  deeply nested under `UserLocalConfigStore/Software/Valve/Steam/apps/<appid>`, and Steam
  rewrites it **live** — observed modified mid-session, so read-only-snapshot + tolerate-locked
  only). HIGH privacy: playtime, last-played, custom launch options (can contain local
  paths/tokens). Reliability for parse: medium-low.
- **librarycache/<appid>.json** `observed-on-disk` — per-user achievement progress + unlock
  timestamps. HIGH privacy. No reason for a mod launcher to read this. At most `nAchieved/nTotal`
  aggregate, with intent. Skip.
- **loginusers.vdf** `observed-on-disk` — AccountName + PersonaName per SteamID64. HIGH privacy.
  Only useful to resolve which `userdata/<id>` is current — and HKCU `ActiveProcess\ActiveUser`
  (already used by `CurrentUserId64()`) is the cleaner source. Fallback only.
- **screenshots.vdf + screenshots/** `observed-on-disk` — personal screenshots + timestamps.
  No launcher use. Listed for completeness only. Do not read.

**The local-first boundary:** owned-but-not-installed games are NOT free from local files — that
needs the Steam Web API + the user's key. Out of lane for a mod manager. Call it out, don't build it.

---

## Competitive scan — what's shipped, what we copy, what we don't

| Tool | What it does with Steam local data | What we take |
|---|---|---|
| **Steam itself** | The source of truth. install path, buildid, StateFlags, SizeOnDisk, LastUpdated, playtime/last-played, cached artwork — all local, zero login. | Read it. All of it that we need. |
| **Vortex** (Nexus) | Auto-detects Steam/GOG/Epic installs with no account, via the ACF appid. Treats game version as a first-class modding concern — but leans on a **manual** version wizard. | The no-account detection (we already do this). The version concern — but read `buildid` *automatically* instead of asking. That's our edge. |
| **Mod Organizer 2** | Detects via the ACF + a hard requirement the folder contains the **game executable** — errors "No game identified" otherwise. | The belt-and-suspenders exe-presence check before acting. Don't trust a manifest blindly. |
| **Playnite** | The reference design for reading local Steam state richly. Explicitly parses `localconfig.vdf` for last-played; offers playtime import as a **user-controlled setting**, not always-on. | The opt-in posture for behavioral data. Default conservative. |
| **GOG Galaxy 2.0** | Playtime/library backbone from local VDF, but achievements/friends need account-linking. | The local part. Skip the phone-home half entirely. |
| **Heroic** | "Import an already-installed game" — detect, then **let the user confirm-and-adopt**. | The confirm-and-adopt pattern. We detect to mod, we don't silently own. |
| **Steam ROM Manager** | `artworkCache.json` maps choices durably + portably (appid-keyed JSON); local-image backup hedges against source takedown. | The durable-choice model *if* we ever let users override a tile (camelCase JSON via AtomicJson). |

### Anti-patterns (the lines we don't cross)

- **No login / no account.** The full picture (achievements, friends, owned-not-installed) is
  exactly the part that forces phone-home. That's library-launcher territory. Skip it.
- **No Steam Web API for what's on disk.** Needs key + network + rate limits, for zero gain.
- **Don't own or mirror the library.** Playnite/Galaxy/Heroic are library managers. A mod
  manager that grows a full library view is scope creep against the launcher the user already runs.
- **No continuous background scan.** Read on demand (game add / refresh), not via a persistent
  filesystem watcher. Heavy watchers thrash disk.
- **Don't trust the manifest blindly.** ACF can be stale or hand-edited. Verify installdir +
  exe presence before any intake (MO2's check).
- **Don't surface/persist playtime/last-played silently.** It's the user's machine, but reading
  behavioral data and putting it on screen (or in JSON) is a deliberate, ideally opt-in call.
- **Don't hardcode one artwork filename scheme.** Probe known patterns, degrade to no-image.

---

## The design space — ranked

Effort/value/risk are my read; the lens column says which side surfaced it. YAGNI applied: two
candidates below are cut outright with reasons.

### 1. Auto-fill the game folder on a popular-game pick  `recommend: v0.6.2-now`
**Value:** HIGH — directly kills the user's complaint ("you make me Browse to a path you know").
**Effort:** trivial. **Risk:** none (read-only, pre-fills an editable textbox). **Lens:** both.

The data already flows into the dialog. `OnPopularSelected` sets Name/ModPath/Steam but not the
folder; `steamGames` reaches the ctor but isn't kept as a field. Fix: stash `steamGames`, and on
a popular pick look up the installed game by `SteamAppId` and set `FolderBox.Text` to its
`InstallDir` (already verified-to-exist by `InstalledGames`). The match logic is a pure one-liner
that belongs in Core (e.g. `PopularGameMatch.ResolveFolder(steamAppId, installed)` returning the
GameRoot or null), covered by a Core test. App side is one wire-up + stash a field. No XAML change.

Bonus reachable in the same change: when the popular pick *is* installed, it can flip to a
one-click add instead of a manual-folder form.

### 2. Widen the appmanifest parse (buildid, LastUpdated, SizeOnDisk, StateFlags, LastPlayed)  `recommend: v0.6.2-now (enabler)`
**Value:** HIGH as an enabler — it's the bottleneck every richer feature sits behind. Low value
*standalone* until a consumer surfaces the fields. **Effort:** small. **Risk:** low. **Lens:** first-principles.

Squarely Core. Add `GeneratedRegex` captures + widen `AppManifest` with nullable fields (parse as
strings, expose typed accessors: long for buildid/size, `DateTimeOffset.FromUnixTimeSeconds` for
epochs, a flags read for StateFlags -> is-fully-installed). Every field optional so a sparse
manifest still parses. Tests beside the existing ones — the fixture already has `StateFlags "4"`
waiting. Ship this *with* #1 because the immediately-useful win is the **StateFlags "fully
installed" gate**: today `InstalledGames` silently includes update-pending/partial installs.

Privacy note: read `LastPlayed` only when a feature needs it; do **not** persist it (read live).

### 3. Surface local Steam artwork in the picker + game tiles  `recommend: next-feature`
**Value:** HIGH on "feels like a real launcher" — the single biggest perceived-polish lever.
**Effort:** medium. **Risk:** low. **Lens:** competitive.

Core returns a candidate-path resolver (`<steam>/appcache/librarycache/<appid>/` glob, "first
that exists" via an injected `Func<string,bool>`), pure + testable. App turns the path into an
`ImageSource` — `BitmapImage`/`ImageSource` are WinUI types and **must stay App-side**
(`CorePurityTests` enforces it; same reason `ModRowViewModel.Thumbnail` lives in App). The
art-vs-monogram fallback is the one seam that spans the boundary — but the existing `Thumbnail`
code (`ModRowViewModel.cs:285` + `MainWindow.xaml:350-359`) is the proven template. New
DataTemplates replace the text-only rows (`AddGameDialog.xaml:73`, `:66`) and the bare game
switcher ComboBox (`MainWindow.xaml:41-44`, backed by `GameOption(Id, Name)` which gains an
ImageSource). Bigger because it touches multiple XAML surfaces, not because the logic is hard.

### 4. Enriched installed-games picker (art + sort-by-last-played + show-all)  `recommend: later`
**Value:** medium-high. **Effort:** large. **Risk:** medium (privacy — sorting by last-played
makes behavioral data the most prominent thing in the picker). **Lens:** both.

Composes #2 + #3 plus a planning-shape change so undetected-engine games render as "Set up" rows
that pre-fill the manual wizard (instead of today's comma-joined `SteamManualNote`). This is the
full AddGameDialog rework. Worth doing — but only *after* #1/#2/#3 land and we've made the
conscious call on the privacy posture. Don't bundle it into the anchor spec.

### 5. Build-id awareness — warn when the game updated under installed mods  `recommend: next-feature`
**Value:** HIGH and genuinely novel — beats Vortex's manual version wizard by reading the truth.
**Effort:** medium. **Risk:** low. **Lens:** competitive.

Depends on #2's buildid parse. Persist the buildid observed when mods were last set up
(`GameEntry`, camelCase `lastKnownSteamBuildId` + round-trip test — `GameEntry.cs:42` already
holds `SteamAppId`), compare on scan, flag "the game updated since your mods were set up — some may
need re-checking." Comparator + staleness verdict are pure Core. App side is light: read current
buildid (#2), call the comparator, toggle a banner (the Vortex-banner pattern at
`MainWindow.xaml:284-305` is the template). Old registries without the field read as null = "no
baseline, no false warning." Fully reversible; buildid is a public version stamp, not behavioral.

### Cut

- **SteamUserData cloud-save resolver fill** `recommend: cut`. The audit framed
  `ExpandSaveRoot`'s null SteamUserData branch as a dead end. It isn't — it's a
  fallback-of-a-fallback (`saveDir ??=` at `GameProfileResolver.cs:59`, only after Ludusavi
  misses), and the comment "Ludusavi covers these games" is accurate. `CurrentUserId64()` returns
  the +offset SteamID64, but the `userdata/<id>` folder needs the **32-bit account id**, so it's
  not even a free reuse. Low value, real footgun. Cut unless a concrete game proves Ludusavi
  misses it.
- **localconfig.vdf playtime / achievements / appinfo.vdf display names** `recommend: cut`. HIGH
  privacy, live-rewritten or undocumented-binary parse, and none of it serves *modding*. Playtime
  is a library-launcher feature. Cut.

---

## Store generalization — the shape, not the implementations

The schema already did the hard part: `StoreIds` carries SteamAppId/GogId/EpicAppName/XboxStoreId,
and the code comment says the other three exist "so GOG/Epic/Game Pass slot in later without a
schema migration." Don't widen it — lean on it.

**Recommendation: generalize the SHAPE now (cheap), the IMPLEMENTATIONS on demand.** Define one
small App-side seam mirroring the existing `SteamService`/`SteamParse` split — an `IStoreLibrary`
adapter (`DetectInstalledGames()` -> records of `{storeId, name, installDir, optional richer
fields}`) with IO/registry/SQLite/Appx messiness in `App\Services` and pure parsers in Core.

- **Steam** — `feasibility: HIGH`, mostly free. The only store with users today. Implement fully.
- **Epic** — `feasibility: HIGH`. Plain JSON `.item` manifests at
  `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests` (InstallLocation, AppName, DisplayName,
  AppVersionString, LaunchExecutable). A near-clone of the Steam path, no new dependency. Build
  next when a real user needs it. *(`confidence: corroborated across sources`, not observed on this machine.)*
- **GOG Galaxy** — `feasibility: MEDIUM`. Single SQLite DB at
  `C:\ProgramData\GOG.com\Galaxy\storage\galaxy-2.0.db`. Needs a SQLite read dependency
  (Microsoft.Data.Sqlite), opened read-only/copy-first. Hold behind the seam until demand justifies
  it. Schema undocumented + version-dependent — pin column names defensively. *(`confidence: corroborated`, not observed.)*
- **Xbox / Game Pass** — `feasibility: LOW for our job`. Detectable via PackageManager API, but
  `InstallLocation` points into ACL-locked `WindowsApps` — the location we can read is frequently
  NOT one we can mod. Treat as **detect-and-route** ("this is a Game Pass install — modding is
  limited"), never a write target. *(`confidence: HIGH that it's hard`, corroborated by MS GDK docs.)*

**The trap to avoid:** four half-working detectors built now. One clean seam + one complete Steam
implementation beats four stubs that all need rework when their first real user shows up.

---

## Recommended scope for the first spec

**v0.6.2 anchor = #1 (folder auto-fill on popular pick) + #2 (widen the manifest parse, ship the
StateFlags fully-installed gate as the first consumer).**

Small, shippable, high-value, low-risk. #1 kills the user's actual complaint; #2 is the cheap
enabler that everything else stands on, and pairing it with the StateFlags gate gives #2 an
immediate reason to exist instead of being dead enabler code. Both are pure-Core-clean, both are
read-only (zero reversibility surface), nothing new is persisted in the anchor. Artwork (#3) and
build-id awareness (#5) are the obvious next-feature follow-ups; the full picker rework (#4) is
later.

---

## Open questions for the human (crisp either/or)

1. **Manifest parse depth** — capture *only* what the anchor needs now (buildid + StateFlags), or
   widen to the full set (buildid + StateFlags + SizeOnDisk + LastUpdated + LastPlayed) while
   we're in the file? Wider is near-free but pulls LastPlayed (behavioral) into Core earlier than
   any feature needs it.
2. **Artwork now or next?** — fold artwork (#3) into the v0.6.2 anchor for the full "real launcher"
   feel, or keep the anchor tiny (folder-fill + parse) and ship artwork as the immediate next feature?
3. **Store generalization** — define the `IStoreLibrary` seam now (Steam-complete, others as
   contract-satisfying stubs), or stay Steam-shaped and refactor to the seam when Epic's first real
   user shows up? (My lean: define the seam now — the schema already assumes it, the cost is small.)
4. **Privacy posture on last-played / playtime** — never read behavioral data (cut it entirely),
   read last-played live-only for picker sort but never persist, or make it an opt-in setting
   (Playnite's pattern)? This decides whether #4's sort-by-last-played is even on the table.
5. **Build-id awareness commitment** — is the "game updated under your mods" warning (#5) a
   committed next-feature, or speculative? It's the one candidate that persists a new field, so
   knowing it's coming shapes whether #2's parse should land buildid now even if the anchor doesn't
   use it yet.
