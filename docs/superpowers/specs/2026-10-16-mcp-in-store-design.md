# The agent tool ships inside the Store package

**Status:** design spec, no code. Written 2026-10-16 against `master` at `4b51e37`.
**Scope:** getting `ModManager.Mcp` (the stdio MCP server) into the Microsoft Store MSIX, wired so
an agent the user chose can reach it, without breaking the seal, the reversibility laws or the
privacy statement. The GitHub/Velopack SKU is out of scope except where a choice here constrains it.

## Where things stand today, verified

Every row was read out of the tree, not taken from the brief.

| Fact | Evidence |
|---|---|
| `ModManager.Mcp` is a separate console project (net10.0, stdio, `ModelContextProtocol` 1.3.0, `Microsoft.Extensions.Hosting` 10.0.8) that references only Core. | `src/ModManager.Mcp/ModManager.Mcp.csproj` |
| The App does not reference it. Nothing in `src/ModManager.App`, the release scripts or either release workflow mentions it. Only the solution, the tests and `scripts/smoke-run.ps1` (which runs the Debug exe) do. | `grep ModManager.Mcp` across `*.cs/*.xaml/*.csproj/*.ps1/*.yml` |
| It is not in the Store package. The 0.24.0.0 bundle holds `ModManager.App.exe` and the Windows App SDK's `RestartAgent.exe`. | `docs/store-listing-0.24.0.md`, "Not in the Store build" |
| The manifest declares `runFullTrust` only, and no `<Extensions>`. MinVersion 10.0.17763.0. | `src/ModManager.App/Package.appxmanifest` |
| Store builds are `-c Store`; FULL is undefined for App and Core. The seal script scans **only** `ModManager.App.dll` and `ModManager.Core.dll` in the App's output folder. | `src/ModManager.App/ModManager.App.csproj` (Store PropertyGroup), `scripts/check-store-seal.ps1` `$checks` |
| 21 tools: 12 read, 9 write. | `[McpServerTool]` attributes under `src/ModManager.Mcp/Tools/` |
| The MCP makes no network call of its own. No `HttpClient`, `WebRequest`, socket, listener or pipe anywhere in `src/ModManager.Mcp`; no tool calls Core's `CurseForgeClient` or `PluginFeedInstaller`. The feed is read from the on-disk cache only (`RemoteManifestCache.ApplyCached`). Intake says outright that it does not identify on Nexus/CurseForge. | grep over `src/ModManager.Mcp`; `McpManifest.Apply`; `IntakeTools.cs` hint text |
| `Program.cs` still opens with "Phase 1 is READ-ONLY". That comment is stale; nine write tools exist. | `src/ModManager.Mcp/Program.cs` line 5 |

**The tools, by what they do.**

- **Read (12):** `get_server_info`, `list_games`, `get_active_game`, `list_mods`, `get_mod_context`,
  `get_game_shape`, `list_profiles`, `list_save_mods`, `list_themes`, `get_app_settings`,
  `get_agent_log`, `dry_run_intake`.
- **Write (9):** `set_mod_enabled`, `apply_loadout`, `intake`, `register_game`, `uninstall_mod`,
  `install_save_mod`, `reset_save_mod`, `remove_save_mod`, `apply_theme`.
- **Gates already in place:** enabling anything on an unacknowledged high ban-risk game refuses, and
  `apply_loadout` refuses the whole batch before anything moves (`ProfileTools.cs`); an agent can
  never give that acknowledgment. `intake` installs frameworks only with `allowFrameworks`.
  `uninstall_mod`, `reset_save_mod` and `remove_save_mod` refuse without `confirm: true`. Every
  attempt, refusals included, appends to `agent-log.jsonl` (`Core/Agent/AgentAudit.cs`).

Two details that matter for privacy copy: `get_app_settings` returns the connected Nexus **user name**
(`NexusTokenStore.StoreSummary.ConnectedUser`), never a token; and `dry_run_intake` / `intake` open
any absolute path the agent names.

## The precedent, and why it does not port as-is

Sanduhr ships its MCP in its Store app. Read in
`C:\Users\estev\Projects\Sanduhr_f-r_Claude\windows-dotnet\`:

- **Packaging.** `src/Sanduhr.App/Sanduhr.App.csproj` (targets `CopyMcpServerOutput` and
  `PublishMcpServer`) publishes `Sanduhr.Mcp` self-contained, single-file and trimmed into the App's
  `mcp\` folder. The App never links the MCP assembly.
- **Launch path.** No App Execution Alias. `src/Sanduhr.Core/McpIntegrationInstaller.cs` copies the
  server out of the package into `%APPDATA%\Sanduhr\mcp\v<ver>-<stamp>\` and writes
  `%APPDATA%\Sanduhr\bin\sanduhr-mcp.cmd` pointing at it. New version, new folder: a running Claude
  Code session keeps its pinned copy, old folders are pruned once unlocked.
- **Registration.** `mcpServers.sanduhr` in the chosen Claude Code home's `.claude.json`
  (`%USERPROFILE%\.claude.json` for the default home, inside the home when `CLAUDE_CONFIG_DIR`
  selects it). Re-read, mutate only the owned key, timestamped backup, temp file plus atomic move,
  never overwrite a file that does not parse, deregister only an entry that still points at Sanduhr.
- **Backups.** Beside the config, as `<.claude.json>.sanduhr-backup-<yyyyMMddHHmmss>`
  (`WriteConfigSafely`). Not under `%APPDATA%\Sanduhr\backups\`; there is no such folder in the
  Windows code. `docs/PRIVACY.md` row 27 agrees: "a timestamped backup is saved beside it first".
- **Privacy.** `docs/PRIVACY.md` rows 26 and 27: what is written where, which home, how to remove it.
- **The cost.** The copy-out only works because Sanduhr 3.4.3 turned off AppData write
  virtualization: `desktop6:FileSystemWriteVirtualization` disabled plus
  `virtualization:ExcludedDirectories`, behind the restricted capability `unvirtualizedResources`,
  with MinVersion raised to 18362. Before that, the new `mcp\` folder was redirected into the
  package's `LocalCache` and Claude Code could not see it
  (`store-listing-console/docs/msix-capabilities.md`, "The cost of declining it").

So the consent dialog, the config-write safety and the privacy rows port. The copy-out does not,
unless we also take the restricted capability. `msix-capabilities.md` records that Microsoft scopes
`unvirtualizedResources` to "certain types of desktop PC games", that RoRoRo declined it, and that
nobody here has measured how a reviewer treats a new restricted-capability request.

## 1. Packaging: an App Execution Alias, pointing at a server inside the package

**Recommended default.** Publish `ModManager.Mcp` into the App's output under `mcp\`, and expose it
with an App Execution Alias, `626-mod-mcp.exe`, declared on the existing `<Application>`. The stable
command an MCP client uses is the alias, never a `WindowsApps\...\0.25.0.0...` path.

Why the alias wins here, in order of weight:

1. **It is the only option that solves the data split without a restricted capability** (see §2). A
   process started through a package's alias runs with that package's identity, so it should get the
   same AppData view as the App.
2. **The path is stable across Store updates.** Windows maintains the alias stub at
   `%LOCALAPPDATA%\Microsoft\WindowsApps\626-mod-mcp.exe` and re-targets it on update. That folder
   is on the user's PATH by default.
3. **Nothing is copied out of the package**, so there is no versioned-folder pruning, no stale
   launcher, and an uninstall removes the server with the app.

**Manifest shape** (to be validated by the build; the attribute placement follows Microsoft's
documented console-alias form and has not been compiled here yet):

```xml
<Package ...
  xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
  xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
  xmlns:desktop4="http://schemas.microsoft.com/appx/manifest/desktop/windows10/4"
  IgnorableNamespaces="uap rescap uap3 uap5 desktop4">
  ...
  <Application Id="App" Executable="ModManager.App.exe" EntryPoint="Windows.FullTrustApplication">
    <uap:VisualElements ... />
    <Extensions>
      <uap5:Extension Category="windows.appExecutionAlias"
                      Executable="mcp\ModManager.Mcp.exe"
                      EntryPoint="Windows.FullTrustApplication">
        <uap5:AppExecutionAlias desktop4:Subsystem="console">
          <uap5:ExecutionAlias Alias="626-mod-mcp.exe" />
        </uap5:AppExecutionAlias>
      </uap5:Extension>
    </Extensions>
  </Application>
```

No capability changes. `runFullTrust` stays the only one. This is the package's first manifest
extension, which the reviewer letter has to say (§6).

**Build shape.** Mirror Sanduhr's two targets in `ModManager.App.csproj`, gated to
`'$(Configuration)' == 'Store'`: build or publish `ModManager.Mcp` with the same Configuration and
RuntimeIdentifier, copy its output to `$(OutDir)mcp\`, and include `mcp\**` as package content.
Publish the server **self-contained, single-file, not trimmed**:

- Self-contained, because the Store App carries its own runtime and the user's machine may have no
  .NET 10 runtime for a framework-dependent exe.
- Single-file, so `mcp\` is one exe and the alias target is unambiguous.
- Not trimmed. `WithToolsFromAssembly()` discovers tools by reflection; trimming would need the
  annotation pass Sanduhr did (`ILLinkTreatWarningsAsErrors`) and a cold run of every tool. Take the
  size hit first, trim later as its own change.

A subfolder, not the App's own folder, because the MCP pins `Microsoft.Extensions.Hosting` 10.0.8
while the App pins 10.0.0. Two exes sharing one folder would share one copy of those dlls.

**What would change it.**

- The spike in §7 shows an alias-launched console process does **not** inherit the package's AppData
  view, or that Claude Code cannot spawn an alias stub with working stdio. Then fall back to option B
  in §2.
- Package size. Measure the bundle with and without `mcp\`. If the growth is unacceptable, either
  trim (with the annotation pass) or align the Hosting versions and share the App folder and runtime.
- A user who has turned the alias off in Settings, Apps, Advanced app settings, App execution
  aliases. The in-app connect flow must detect a missing stub and say how to turn it back on, not fail
  silently.

**Rejected:** registering the raw `WindowsApps` path (versioned, dies on the next update, and the
folder is ACL-restricted); copying out Sanduhr-style without the capability (redirected into
`LocalCache`, which is exactly Sanduhr's pre-3.4.3 bug).

## 2. Data location: the alias keeps the App and the agent on one view

**What the two processes read and write.**

| Data | Path | Under MSIX file-write virtualization? |
|---|---|---|
| `games.json`, `app-settings.json`, `themes\`, `nexus.json`, the global `agent-log.jsonl` | `%APPDATA%\ModManagerBuilder` (`McpConfig.DataRoot`; App services such as `AppSettingsService`, `LauncherService`, `ThemeService`) | Yes |
| Game-definition feed cache, held archives, launch log | `%LOCALAPPDATA%\ModManagerBuilder` (`McpManifest.CacheDir`, `RemoteManifestSource`, `HeldBackupsService`) | Yes |
| Per-game data: holding folder (`disabled\`), profiles, saves, ban-risk acknowledgment, per-game `agent-log.jsonl`, install records | `<library>\_626mods\<gameId>\` next to the Steam library, or a user-set `DataDir` (`Scanner.DataDirForGame`) | No. Outside AppData. |

One correction to the brief: the holding folders are **not** under `%APPDATA%`. They live per game
beside the library, so the toggle path, the ban-risk gate and the per-game audit log are shared
correctly no matter how the MCP is launched. The split risk is the AppData half: the registry, app
settings, themes and the feed cache.

**How the Store App sees it today.** It calls `Environment.GetFolderPath(ApplicationData)` like the
unpackaged build. With no `desktop6` opt-out declared, MSIX applies the rule recorded in
`msix-capabilities.md`: a path that already exists at the real location is written in place; a new
one is redirected to `%LOCALAPPDATA%\Packages\626LabsLLC.626ModLauncher_<hash>\LocalCache\Roaming\...`
(and `...\LocalCache\Local\...`). On Neb, 626ModLauncher shows **0** files in `LocalCache` because dev
and Velopack builds created the real folders long ago. On a customer's Store-only machine the App's
`games.json` is expected to live in `LocalCache`. That is inferred from the rule, which RoRoRo
confirmed for its own package on 2026-10-11; nobody has run this app on a Store-only machine.

**The split, concretely.** An MCP launched as an ordinary process (a copied-out exe, or the dev
build) sees only the real `%APPDATA%\ModManagerBuilder`. On a Store-only machine that is empty:
`list_games` returns nothing, and a `register_game` writes a `games.json` the App then reads only if
it never created its own. Two registries, each confident.

**Recommended default.** Launch only through the alias, so the server runs with the package identity
and the same merged view as the App. No restricted capability, no migration, and the uninstall story
stays the same as today. Make the agreement checkable: `get_server_info` reports the resolved data
root, whether the process has package identity, and the `games.json` path it actually opened. The
App's connect page shows the same three values, so a mismatch is visible instead of silent.

**What would change it.**

- **The spike fails** (alias process is not virtualized like the App). Then option B: request
  `unvirtualizedResources` with the `desktop6` / `ExcludedDirectories` opt-out for
  `$(KnownFolder:RoamingAppData)\ModManagerBuilder` and `$(KnownFolder:LocalAppData)\ModManagerBuilder`,
  raise MinVersion to 10.0.18362.0, and ship a one-time migration from `LocalCache` to the real path
  that copies and never deletes (reversibility law). Price it as a certification risk: approval is
  documented as scoped to games, and this is a utility that manages games.
- **Este wants the server usable by non-alias clients** (WSL, a sandboxed agent, the GitHub SKU's
  copy). Same answer: option B, because only real paths are visible outside the package.

## 3. Registration: consent-gated, one Claude Code home at a time

**Recommended default.** A new **Agent access** section in Settings, between *Restore points* and
*Folders left behind*, off until the user acts. Port `McpIntegrationInstaller`'s config half into
Core (`ModManager.Core/Agent/McpRegistration.cs`, pure, test-injectable home dir); the App wraps it.
Drop the copy-out half entirely; the alias replaces it.

- **Connect to Claude Code…** opens a consent dialog that names, in plain words, what an agent will
  be able to read and change (the §6 text), lists the Claude Code homes found under the profile
  (`.claude`, `.claude-*`), and asks for **one**. Confirm writes `mcpServers["626-mod-launcher"]`
  into that home's `.claude.json` (same path rule as Sanduhr's `ConfigPathFor`) with
  `"command": "<expanded %LOCALAPPDATA%>\\Microsoft\\WindowsApps\\626-mod-mcp.exe"` and empty
  `args`. Write safety exactly as Sanduhr: re-read, touch only our key, timestamped backup beside the
  file as `.claude.json.626-backup-<yyyyMMddHHmmss>`, temp plus atomic rename, refuse a file that
  does not parse. If a `626-mod-launcher` entry already exists, show what it points at before
  replacing it (Este's own dev registration will be the first one this meets).
- **Disconnect** removes the entry only when its command still points at our alias or a
  `ModManager.Mcp` exe. A foreign entry is left alone.
- **Claude Desktop and other clients:** a **Copy config** button, not a write. The snippet is the
  same `command`. Reason: Claude Desktop's config lives under `%APPDATA%\Claude\`, which *is*
  virtualized for our package. Editing an existing file there lands in place, but our temp file and
  backup are new files and would be redirected into our `LocalCache`, and a missing config would be
  created where Claude Desktop never looks. That is an inference from the redirection rule, not a
  measurement; until it is measured, copy-paste is the honest path.
- **`.claude.json` itself is safe to write.** It sits in `%USERPROFILE%`, outside AppData, so it
  is not virtualized. A home selected through `CLAUDE_CONFIG_DIR` is under the profile too.
- Every connect and disconnect appends a line to the global `agent-log.jsonl`, so the log records
  who was given access, not only what they did with it.
- All new controls ship with `AutomationId`s (`SettingsAgentAccessConnect`,
  `SettingsAgentAccessDisconnect`, `SettingsAgentAccessCopyConfig`, `SettingsAgentAccessHomePicker`)
  per `.claude/rules/automation-ids.md`.

**What would change it.** Measuring that a packaged write into `%APPDATA%\Claude\` lands in place
for an existing config would justify a one-click Desktop connect. Este choosing project-scoped
`.mcp.json` registration instead of user-scoped would change the target file, not the pattern.

## 4. Seal: the server is built Store and scanned like the App

**Recommended default.**

- The Store target builds `ModManager.Mcp` with `Configuration=Store`, which flows to its Core
  reference, so the Core inside the server is the sealed Core (FULL undefined, no `AntiCheat.cs`).
  The MCP itself has no `#if FULL` code today (`grep` is empty), and must not grow any.
- Extend `scripts/check-store-seal.ps1` with a third row and a presence check:
  - **Absent:** in the `mcp\` output, the same Core terms (`.626off`, `AntiCheatState`) plus the
    loader markers (`PluginFeedSource`, `WirePluginFeed`, `LoadVerified`, `AssemblyLoadContext`).
    Scan the single-file exe as raw bytes; bundled assemblies are stored uncompressed by default
    (`EnableCompressionInSingleFile` off), so the existing UTF-8 and UTF-16LE byte search still finds
    them. Assert compression stays off, because a compressed bundle would pass this scan empty.
  - **Present:** `mcp\ModManager.Mcp.exe` exists in the output, and the produced `AppxManifest.xml`
    carries the `626-mod-mcp.exe` alias pointing at it. This is the lesson of the Nexus presence
    check: absence-only certifies an empty box.
  - **Network:** fail if `ModManager.Mcp.dll`'s own metadata references `System.Net.Http.HttpClient`,
    `HttpListener`, `TcpListener` or `NamedPipeServerStream`. That pins "no network endpoints of its
    own" to the binary, not to a sentence. Core is excluded because the App's Core legitimately
    carries `CurseForgeClient`.
- Run it from `release-msstore.yml` exactly where it runs now (`-SkipBuild` after the versioned
  build), and scan the **bundle** contents once, not just `bin\`, because what ships is the bundle.

**What would change it.** Trimming the server later means re-proving the byte scan on the trimmed
exe. A future live-app channel (the named pipe in the 2026-05-26 sketch) would move a pipe type into
the MCP on purpose, and the network row would need a narrow, named exception.

## 5. Write-tool posture for the first Store cut: read-only

Both options, side by side.

| | **A. Read-only first cut** | **B. Full surface** |
|---|---|---|
| Tools | The 12 read tools. Write tools are not registered in the Store build. | All 21. |
| What a reviewer reads | "An agent you connect can see your games and mods. It cannot change anything." One sentence, testable in a minute. | The full gate story: ban-risk refusal, `confirm`, `allowFrameworks`, the audit log, reversible toggles, and one tool that deletes files. |
| What a user gets | Questions answered: what is installed, what is on, what a drop would do, what an agent did before. | The agent can act: toggle, apply loadouts, install, uninstall. The reason the server exists. |
| Risk | Low. The store build does what the letter says. | Higher, and front-loaded into the first submission that also adds the package's first extension. Two new things for one review. |
| Cost to build | A Store-config switch: `WithTools<...>()` for the read types instead of `WithToolsFromAssembly()`, plus a test that pins the Store tool list. | None beyond §1 to §4. |

**Recommended default: A for the first Store cut, B as the very next submission.** The packaging,
alias and data-view questions are the novel parts and they are what certification and the spike need
to prove. Shipping read-only first proves them with the smallest possible blast radius, and the
write surface then arrives in a letter that says "the package did not change; nine tools were turned
on, and here is how each is gated." The gates are already in code and already tested; holding them
back one submission costs a release cycle, not a redesign.

Implementation note: gate the write tools on the build flavor, not on a runtime setting, so the
seal can assert their absence (`set_mod_enabled`, `uninstall_mod` and friends must not appear in the
Store `ModManager.Mcp.dll` for cut A). A runtime toggle would ship the code and ask the reviewer to
trust a switch.

**What would change it.** If Este wants the first Store cut to be the useful one, take B and lead
the letter with `uninstall_mod` the way 0.24.0.0 led with Uninstall: name it, say it refuses without
`confirm: true`, say it lists every file first. A middle ground (B minus `uninstall_mod`,
`reset_save_mod`, `remove_save_mod`, the three that delete) is available if the deletes are the only
worry.

## 6. Reviewer letter and privacy text

### Reviewer letter paragraph (for the submission that adds the server)

> **New in this package: a local agent tool, off until you turn it on.** The package now contains a
> second executable, `mcp\ModManager.Mcp.exe`, reachable through one App Execution Alias,
> `626-mod-mcp.exe`. This is the package's first manifest extension. **No capability changed:**
> `runFullTrust` is still the only one; `broadFileSystemAccess` and `unvirtualizedResources` are not
> declared. The tool speaks the Model Context Protocol over **standard input and output only**. It
> opens no port, starts no listener, and makes no network request of its own; the seal script fails
> the build if the server's binary references an HTTP client or listener. It runs only when an agent
> program the user has chosen starts it, and that agent is registered only after the user clicks
> Connect in Settings, Agent access, and confirms a dialog naming what the agent can see.
> [Cut A:] It can read the user's games, mods, loadouts, themes and settings; **it cannot change
> anything.** [Cut B: replace the last sentence with the gate list from §5.] To test: Settings, Agent
> access, Connect to Claude Code; or run `626-mod-mcp.exe` in a terminal and send an MCP
> `initialize` request.

### PRIVACY.md text

`PRIVACY.md` is one paragraph of prose, not a table, so this lands as one added paragraph after the
first, plus a correction to the uninstall sentence.

> **The agent tool (off until you connect it).** 626 Mod Launcher includes a local agent tool that an
> AI agent on your computer, such as Claude Code, can talk to once you connect it in Settings, Agent
> access. It runs only when that agent starts it, talks to it only through standard input and output
> on your machine, and never contacts the internet itself. It can read what the app already knows:
> your added games and their folders, your mods and whether each is on, loadouts, save-world mods,
> themes, your settings, the name of a connected Nexus account (never its token), and any file or
> archive the agent asks it to inspect. [Cut B: It can also turn mods on and off, apply loadouts,
> install and uninstall mods and save-world mods, add games and change the theme, through the same
> reversible paths the app uses; it can never turn on mods in a game you have not acknowledged as a
> ban risk, and uninstalling or resetting needs an explicit confirmation.] Everything it does is
> written to `agent-log.jsonl` in the app's data folder, refusals included. What the tool tells the
> agent becomes part of that agent's conversation, and is then governed by that agent's own privacy
> terms. Connecting writes one entry, `626-mod-launcher`, into the one Claude Code `.claude.json`
> you pick, after saving a dated backup beside it; Disconnect removes only that entry.

Uninstall sentence, corrected so it is true for Store-only installs too:

> Uninstalling doesn't automatically wipe your stored Nexus token or app data. Delete
> `%APPDATA%\ModManagerBuilder\` and `%LOCALAPPDATA%\ModManagerBuilder\` to remove everything; on a
> Microsoft Store install that never had another copy of the app, that data may instead live inside
> the app's package folder, which uninstalling removes. If you connected an agent, Disconnect first,
> or remove the `626-mod-launcher` entry from your `.claude.json`.

The site version at `626labs.dev/privacy.html#privacy-mod-launcher` governs, so it changes in the
same release.

## 7. Test and smoke plan

**Spike first, before any of §1 to §5 is built.** One throwaway branch, one machine:

1. Add the alias and `mcp\` to a sideload build, install it.
2. **Data view, parked-folder method** (from `msix-capabilities.md`): close the app, rename
   `%APPDATA%\ModManagerBuilder` and `%LOCALAPPDATA%\ModManagerBuilder` aside, launch the packaged
   App, add one game. Confirm `games.json` landed in the package `LocalCache`. Then run
   `626-mod-mcp.exe` from a plain terminal and call `list_games`. **Pass:** the game is listed.
   **Fail:** empty list, which sends §1 and §2 to option B.
3. **Reverse direction:** `register_game` through the alias, then confirm the App shows it.
4. **Spawn from the real client:** register the alias in a scratch Claude Code home, start Claude
   Code, call `get_server_info`. Confirms node's spawn of an alias stub keeps stdio intact.
5. **Update survival:** install a second version over the first while a Claude Code session holds
   the server open. Record whether the update is blocked, deferred or applied, and whether the next
   session gets the new server. (Unmeasured today; `msix-capabilities.md` lists a related
   `0x80073D02` question.)
6. Put the parked folders back.

Note the known trap: a sideload cannot be installed where the Store build already is (`0x80073CFB`,
same identity). Run the spike on the machine without the Store install, or remove it first.

**Unit tests (Core, xUnit, failing first):**

- `McpRegistration`: writes only our key; preserves every other byte of a fixture `.claude.json`;
  makes a backup; refuses a malformed file and leaves it untouched; reports a prior entry; deregister
  leaves a foreign `626-mod-launcher` entry alone; default-home vs `CLAUDE_CONFIG_DIR` path rule.
- Registration JSON is camelCase on disk where we own the shape (`command`, `args` already are);
  per `.claude/rules/camelcase-json-on-disk.md`, add a round-trip test if any new persisted shape
  appears (for example a connect receipt).
- Store tool list pinned: a test that builds the server's tool registry the way the Store flavor
  does and asserts exactly the 12 read tools (cut A). Tests run FULL, so the flavor choice must be a
  testable function, not only an `#if`.
- `get_server_info` reports data root, package identity and the registry path it opened.

**Seal:** `check-store-seal.ps1` gains the §4 rows; prove each one red once with a deliberate leak
(a FULL-built Core copied into `mcp\`, an `HttpClient` field in the server, a missing alias) before
trusting it green.

**UI Automation smoke** (`scripts/smoke-run.ps1`): the Agent access section is reachable by id;
Connect with a scratch home writes the entry and Disconnect removes it, restored in a `finally`;
Copy config puts the alias path on the clipboard. Add each to `docs/smoke-tests/pending.md` with
steps and expected outcome.

**Packaged smoke before submission:** install the built bundle, connect a scratch Claude Code home,
run every read tool once (cut A) and confirm a write tool is absent from `tools/list`. Read the
identity, capabilities and alias **out of the bundle**, as the 0.24.0.0 listing did.

## Decisions for Este

1. **First Store cut: read-only or full?** Recommend **read-only (cut A)**, write tools in the next
   submission with an unchanged package.
2. **Alias name.** Recommend **`626-mod-mcp.exe`**. Short, ours, unlikely to collide. The
   `mcpServers` key stays `626-mod-launcher`.
3. **If the spike fails, take `unvirtualizedResources`?** Recommend **not in the same submission**.
   Ship nothing until it is priced; if taken, it is its own release with a migration and its own
   letter.
4. **Claude Desktop one-click, or copy-paste?** Recommend **copy-paste** until a packaged write into
   `%APPDATA%\Claude\` is measured.
5. **Does the GitHub/Velopack SKU ship the server too?** It does not today either. Recommend **yes,
   later**, as a plain `mcp\` folder in the Velopack payload with the same registration code pointing
   at the install path; no alias needed off-Store.
6. **Fix the stale "Phase 1 is READ-ONLY" comment and the PRIVACY.md uninstall sentence now**, ahead
   of this work? Recommend **yes**: the uninstall sentence may already be wrong for Store-only users.
