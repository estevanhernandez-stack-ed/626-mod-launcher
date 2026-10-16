# The backlog, and three features that would make 626 the one people love

**Date:** 2026-10-16, after v0.25.0 shipped on both channels.
**Inputs:** the dashboard task list, `docs/2026-08-05-backlog.md`, and two research passes run today:
the manager landscape (Vortex, MO2, Nexus Mods App, CurseForge, r2modman, newcomers) and what modders
say they love, hate and wish for.
**Evidence quality, said first:** Reddit and YouTube comments were unreachable to the research tools,
and Nexus pages returned 403, so a lot of what follows comes from Steam discussions, Nexus forum
snippets, GamingOnLinux and guide sites, dated 2023 to 2026. The pains below recur across sources, but
nobody counted them. Read the ranking as directional.

## The backlog is small, and mostly follow-through

Ten open tasks on the dashboard, plus two partly built lines in the repo backlog.

**Correctness follow-ups (from today's fixes):**
- Intake drops companion files: a Fallout 4 zip loses its `.ba2`, a Cyberpunk zip its standalone `.xl`
  (medium). The scanner learned them today; drop-to-install did not.
- Direct-inject: turning off a 626-installed file leaves the game without the original (medium).
- Provenance outlives the file after a Steam verify (low).
- A folder mod containing a file whose name ends in a dot can't be turned back on (low).

**Bigger lines:**
- Bethesda load order: write `Plugins.txt` (medium). Blocked on having a real Bethesda install to verify.
- Ship the agent tool (MCP) in the Store package (medium). All six decisions made; the alias spike is
  the first step.
- E1 consent surface + E2 MCP bridge to the UI harness (repo backlog). The last pieces of agent access.
- User onboarding flow + supporting docs (medium, from July, not started).

**Small or outside the repo:**
- Fix the live privacy page at 626labs.dev: the "never proxies" line and the uninstall sentence.
- README/what's-new: "quit before updating from the Store".
- Catalog: a screenshot gallery in the mod detail view (low).

## What the world is saying

**The market.** Nexus is consolidating: its 2026 focus post folds the Nexus Mods App's ideas back into
Vortex (1.4M+ monthly users) and promises Vortex on SteamOS. The Nexus Mods App is deprioritized at
best. MO2 is still the power user's choice (its virtual file system keeps the game folder clean), with
no 2026 release found. r2modman owns the co-op indie niche on Thunderstore games. CurseForge owns
modpacks, mostly Minecraft. No manager was found shipping anything agent- or AI-shaped.

**The pains, roughly by how loud and how often:**
1. **A game patch breaks the loader or script extender, and nobody says when it's fixed.** Fallout 4
   Next Gen broke F4SE for weeks; Skyrim AE broke SKSE; BG3 patches broke Script Extender; Cyberpunk
   players "assume all mods are broken" after every update. The workarounds are folk remedies: turn off
   Steam auto-update, roll back through the Steam console, copy the whole game folder before patching.
2. **Beginners drown in load order and conflicts,** and give up ("I think I'm better off not trying to
   mod at all").
3. **Errors with no explanation.** The one thing people praised about the Nexus Mods App was "clearer
   feedback when something went wrong."
4. **The manager grabs the game folder** and the user can't tell what it changed.
5. **Updates are all-or-nothing.** People want to choose, see what changed, and hold back.
6. **Co-op mismatch.** Valheim "incompatible version", Palworld players hand-copying the same mod into
   every friend's install, Lethal Company friends on different versions. r2modman profile codes help on
   Thunderstore games only.
7. **Ban fear** on anti-cheat games.
8. **Linux and Steam Deck** are a mess outside Nexus's new promise.

**What people love and we must not lose:** MO2's clean game folder and per-profile mod sets;
r2modman's one-click profiles and "send my setup to a friend"; Vortex's easy start with Nexus.

## Where 626 already stands

A lot of the loudest pains already map onto shipped pieces that nobody else has together:
reversibility on every file op (nothing is deleted; turning off moves to holding), restore points and
Return to vanilla, the OLD LOADER chip (loader older than the game build), the Steam build-update
banner, launch verification (`LaunchVerification`: did the mods actually load: Ran / DidNotRun /
Unknown), ban-risk gates on enable, install and save writes, the profile archive (a whole setup in one
file), an Updates view, and the agent tool. The three bets below are mostly **assembly**, not invention.

## The three bets

### 1. Patch Day: 626 notices the game updated and keeps you playing

**The pain it answers:** number 1, the loudest, every patch, every big modded game.

**What a user sees:** Steam updates the game. Next time you open 626 (or press Play), it says plainly:
"Elden Ring updated last night. Your mod loader is from before that update and probably won't load
yet." Three buttons: **Play vanilla for now** (one click, fully reversible, mods come back with one
click), **Play modded anyway**, and **Tell me when it's fixed**. 626 watches the loader's release page
(the feed already carries loader links) and says so the moment a newer version is out, with a one-click
"update the loader" that snapshots first. A restore point is taken automatically before the first
modded launch on a new game build, so "go back to what worked" is always one click.

**Built from:** the Steam build-update banner (`lastKnownSteamBuildId`), the OLD LOADER chip, the
manifest's `loaders` list with release URLs, vanilla launch, restore points, launch verification (which
can confirm the loader actually ran after the user updates it).

**What's new to build:** the patch-day flow that ties them together; a release-watch for loaders
(GitHub releases for REFramework, UE4SS, SKSE/F4SE pages; politely polled, cached, through the feed so
it can be fixed without an app release); the automatic restore point keyed to a build change.

**Why us:** everyone else leaves the user to folk remedies. We already detect both halves (game build
and loader date) and can undo anything.

**First slice:** Steam games only, the five loaders already in the feed, banner plus "Play vanilla for
now" plus the automatic restore point. Release-watch second.

### 2. Party Sync: make a friend's game match yours

**The pain it answers:** number 6, co-op mismatch, in exactly the communities nobody serves outside
Thunderstore: Palworld, Valheim, R.E.P.O., Lethal Company, Content Warning (several already registered
in 626 today).

**What a user sees:** **Share setup** on a game makes a short code or a small file: the game build,
each mod with its source (Nexus mod and file id, Thunderstore package, or "you'll need this one from
<page>"), pinned versions, and the config files that matter. The friend opens it in 626 and gets a
plain checklist: what they have, what they're missing, what's a different version, with a link or a
one-click fetch for each through **their own** account. Once both sides match, the game row says
"Matches Este's setup" and warns the moment either side drifts ("you updated Valheim Plus, your group
didn't").

**Built from:** the profile archive's manifest, Nexus identify (mod and file ids already recorded per
install), the install records, profiles and loadouts, the Updates view.

**The rule it must keep:** 626 never bundles anyone's mods (NOTICE). The share is a **recipe**, not a
zip: ids, versions, hashes and links. Downloads stay on the source site under the friend's account, so
mod authors keep their download counts and endorsements.

**Why us:** r2modman does this for Thunderstore games only; Nexus collections are Nexus-only and
gate some actions behind Premium. A recipe that spans sources and warns on drift doesn't exist.

**First slice:** BepInEx co-op games (Valheim, Lethal Company, R.E.P.O., Content Warning) plus
Palworld; share as a file and a code; match/mismatch checklist; drift warning on the game row.

### 3. What broke: a plain-words doctor, with an undo button

**The pains it answers:** 2, 3 and 4: beginners giving up, errors with no explanation, not knowing what
the manager changed.

**What a user sees:** the game crashed, or the mods didn't load. One button on the game: **What
broke?** 626 answers in plain words from evidence it already has: "Your mods didn't load. The loader
ran, but SomeMod needs Framework X, which isn't installed." Or: "Since the last time this worked, you
turned on 3 mods and the game updated. Here's the list." Each answer comes with the fix it can do
(install the missing piece from a file you have, turn those 3 back off, go back to the last setup that
launched cleanly) and every fix is undoable.

**Built from:** launch verification (did it load), restore points (a "last setup that launched
cleanly" is a restore point plus a Ran verdict), the state strip and its chips (missing framework, old
loader, setup problems), dependency detection, install records, the agent log.

**What's new to build:** the "last known good" marker (stamp a restore point when a launch verifies
`Ran`), a diff from that point to now in plain words, and the doctor surface that ranks causes.

**The agent angle, and the unique part:** the same evidence is already exposed to agents through the
MCP. With the Store MCP shipped, "Ask Claude what broke" becomes a real button: the agent reads the
diff, the verdicts and the logs through read-only tools and explains, and nothing changes without the
user. No manager ships anything agent-shaped today; this would be the first, and it would be the
honest kind (read-only first, every write gated and logged).

**First slice:** last-known-good stamp on a verified launch, the plain-words diff, one-click "go back
to last good". The doctor's cause ranking and the agent button follow.

## What I didn't pick, and why

- **Linux and Steam Deck.** The clearest underserved group, but 626 is WinUI 3 and Windows-only by
  design. Doing it right is a port, not a feature. Worth its own strategic call, not a slot in this list.
- **Collections/modpacks in the Nexus sense.** Party Sync covers the part users actually ask for
  (match my friend) without becoming a hosting service or a Nexus competitor.
- **Automatic load-order sorting (LOOT-style).** Real value for Bethesda players, but it waits on the
  `Plugins.txt` writer and a real install to verify against.
- **Beginner onboarding** is on the backlog and stays there; the doctor does more for beginners than a
  tour does, because it shows up at the moment they are about to give up.

## Suggested order

Patch Day first: it's the loudest pain, the most assembly and the least new risk. What broke second:
it reuses Patch Day's restore-point stamp and is where the Store MCP pays off. Party Sync third: the
biggest new surface (sharing, matching, drift), and the one that grows the user base through friends.
