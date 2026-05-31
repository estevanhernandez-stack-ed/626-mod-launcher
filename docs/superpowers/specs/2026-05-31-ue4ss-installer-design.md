# UE4SS installable via framework intake — design

> Issue #108. Today UE4SS is detect-only; only Elden Mod Loader lives in the installable
> `KnownFramework.Catalog`. This spec makes UE4SS install through the existing framework-intake
> path — validate-then-extract, reversible, pure-Core.

Status: design. Implementation is TDD — every task below starts as a failing xUnit test in
`tests/ModManager.Tests/`. Core stays pure; `CorePurityTests` is the guard.

---

## 1. The verified UE4SS layout (ground truth)

Read off a real Windrose install. This is the only layout we build against — nothing past this is
assumed.

```
<GameRoot>/<ProjectSubfolder>/Binaries/Win64/
  dwmapi.dll                      loader proxy — sits NEXT TO the game exe
  ue4ss/
    UE4SS.dll                     signature file (UE4SS-unique)
    UE4SS-settings.ini            signature file (UE4SS-unique)
    Mods/...                      mods dir; mods.txt enable manifest
  Windrose-Win64-Shipping.exe     PROTECT — the game executable, must never be overwritten
```

Concrete instance: `C:\...\Windrose\R5\Binaries\Win64\`.

The install target is **not** the game root. It is `<GameRoot>/<ProjectSubfolder>/Binaries/Win64`,
where the project subfolder (`R5` for Windrose) is per-game. The game executable
(`<Project>-Win64-Shipping.exe`, generally `*-Shipping.exe`) lives in that exact folder — the proxy
DLL has to land beside it, which is precisely why a framework install can never clobber it.

Engine: `ue-pak`. UE4SS is a generic Unreal loader — it works for many ue-pak titles (Windrose
3041230, Palworld, Hogwarts Legacy, ...), unlike Elden Mod Loader, which is Elden-Ring-only and
rightly pins its SteamAppId.

---

## 2. The install-root gap (one paragraph)

`KnownFramework.InstallRoot` is a symbol string resolved by
`FrameworkInstaller.ResolveInstallRoot(installRootSymbol, gameRoot)`, which today knows exactly two
symbols: `"GameRoot"` → `gameRoot`, and `"PlayFolder"` → `<gameRoot>/Game` if it exists else
`gameRoot` (the FromSoft layout). Neither can express UE4SS's target, for two reasons: (a) it needs
the per-game **project subfolder** (`R5`), which the resolver can't derive because it never receives
the game context — only `(symbol, gameRoot)`; and (b) it needs the `/Binaries/Win64` suffix under
that subfolder. The project subfolder is already computed elsewhere —
`FrameworkDeps.ProjectSubfolder(relPath)` pulls `R5` out of a ue-pak mod-location path like
`R5/Content/Paks/~mods` (first segment, skips a `Content`-rooted fallback), and
`FrameworkDeps.ResolveProbeRoots` already walks `ctx.Game.ModLocations` to build UE probe roots. So
the data exists; the resolver just can't see it.

---

## 3. Chosen approach + why

**Approach A — new symbol, project-subfolder-aware `ResolveInstallRoot` via additive overload.**

Keep the symbol-string model. Add one `InstallRoot` symbol (`"UeProjectBinariesWin64"`) and widen
`ResolveInstallRoot` with an **optional third parameter** carrying the game's ue-pak mod-location
relative paths, so the new arm derives `<gameRoot>/<projectSubfolder>/Binaries/Win64` by reusing the
existing `FrameworkDeps.ProjectSubfolder` logic. No new record field, no manifest-schema change, no
uninstall change, no migration of the 600+ tests or the ELM call path.

### Why A over B (context-aware resolver that takes `GameContext`)

B is the cleaner long-term destination — it reroutes `ResolveInstallRoot` and `Install` to take the
whole `GameContext`, so any future project-relative framework needs zero installer churn. But at
v0.3.0 there are exactly two installable frameworks (ELM, UE4SS) and only UE4SS needs
project-relative resolution. B's signature change reroutes the **one shipping installable path**
(ELM) through two new signatures, forcing the 4 `ResolveInstallRoot` tests, every `Install` test, and
the sole `MainViewModel` caller to migrate in lockstep — mechanical risk concentrated exactly on the
thing that must not regress. A lands the feature additively: the existing 2-arg `ResolveInstallRoot`
and 4-arg `Install` keep compiling untouched (optional defaults), ELM resolution is byte-for-byte
identical, and the change is entirely upstream of the file-op block. Same end state is reachable
later under green tests; ELM is never at risk in the interim. **If the roadmap queues 3+
project-relative frameworks, refactor to B then.**

### What the adversary forced into the design (these are not optional)

The "smallest change" pitch survives, but only with the following folded into the same PR. They are
correctness, not polish.

1. **Signature set must drop the proxy DLL.** UE4SS ships the loader as `dwmapi.dll`,
   `xinput1_3.dll`, OR `d3d11.dll` depending on release/game, and users sometimes rename it.
   Requiring `dwmapi.dll` as a hard signature means xinput-variant zips classify as **unrecognized**
   and fall through to the detect-only nudge — the exact status quo #108 is trying to kill. The
   signature is the **UE4SS-unique pair** `UE4SS.dll` + `UE4SS-settings.ini` (basename, all-present).
   The proxy DLL becomes a `ZipFilenameHints` advisory, never a required signature.

2. **The `*-Shipping.exe` guard must actually be multi-game.** `PathGate.IsForbidden` matches by
   literal basename OR full-relative-path equality — **no glob** (verify before merge; see §6
   verification debt). A catalog entry of `["Windrose-Win64-Shipping.exe"]` protects exactly one
   game and is inert for Palworld/Hogwarts/etc. Because the entry is `SteamAppId: null` (multi-game),
   that ships a one-game guarantee dressed as N-game. Close it in this PR by **extending PathGate** to
   recognize a leading-`*` forbidden entry as a basename `EndsWith` match, then list `"*-Shipping.exe"`.

3. **The suffix matcher must not over-protect.** A blunt `EndsWith` on `*-Shipping.exe` would also
   refuse a legit mod file named `Cool-Shipping.exe` nested under `ue4ss/Mods/`. Scope the suffix
   match to the **install root only** (the resolved `Binaries/Win64`), not arbitrary depth — the
   guard's job is "don't overwrite the game exe sitting next to the proxy," not "ban a filename
   everywhere." Test both directions: protects the exe at the install root; does NOT refuse a
   same-suffix file deeper in the tree.

4. **Wrong-subfolder must fail loud, not silently no-op.** `ProjectSubfolder` takes the first
   mod-location yielding a non-null first segment. For a multi-mod-location ue-pak game, iteration
   order decides which subfolder wins. If it picks wrong (or a `Content/Paks/~mods`-only game with no
   project subfolder), the proxy lands where no exe will ever load it — the user reads "installed,"
   nothing loads. **Guard:** before extracting, require the resolved `Binaries/Win64` to exist and to
   contain a `*-Shipping.exe`. If not, return `FrameworkInstallResult.Fail` with a clear message.

5. **No-subfolder-resolvable must return `Fail`, not `throw`.** A thrown `InvalidOperationException`
   from the resolver on the UI path is a crash unless the caller wraps it. Return
   `FrameworkInstallResult.Fail(...)` so it renders as the same polite refusal dialog every other
   `Install` failure uses. (`ResolveInstallRoot` itself can still throw for a catalog typo on an
   unknown symbol — that's a programming error, not a user path; the **install** entry point converts
   the no-subfolder case to `Fail`.)

6. **Persisted `InstallPath` must be unconditionally absolute.** The whole uninstall-survives
   argument rests on `Install` freezing the resolved absolute path into `manifest.InstallPath`, and
   `Uninstall` resolving against it (never re-resolving the symbol). `Path.Combine(gameRoot, sub, ...)`
   is only absolute if `gameRoot` is. Wrap the new arm's return in `Path.GetFullPath` so the manifest
   is CWD-independent regardless.

The first three turn "fix one zip layout" into "fix #108 for real." 4–6 keep the reversibility and
fail-closed contracts honest under the new root. The blast-radius advantage over B holds for the
resolver mechanics; the surrounding correctness work (1–5) is identical regardless of A or B, so it
is not extra cost A imposes — it is the real shape of the feature.

---

## 4. The concrete KnownFramework catalog entry

Appended to `KnownFramework.Catalog` in `src/ModManager.Core/Catalog/KnownFramework.cs`. No new field
on the record type.

```csharp
new KnownFramework(
    FrameworkId: "ue4ss",
    DisplayName: "UE4SS",
    Engine: "ue-pak",
    SteamAppId: null,                                          // engine-only scope — multi-game
    GetUrl: "https://github.com/UE4SS-RE/RE-UE4SS/releases",   // matches the FrameworkDeps UE4SS GetUrl
    Author: "RE-UE4SS team",                                   // honor-the-builders
    ZipFilenameHints: new[] { "ue4ss", "zdev", "dwmapi", "xinput1_3", "d3d11" },  // proxy DLL = HINT
    // Signature: UE4SS-unique pair, basename, ALL-present. Classify lowercases basenames into a
    // HashSet and requires all present. The proxy DLL is deliberately NOT here — see §3 item 1.
    ZipSignatureFiles: new[] { "UE4SS.dll", "UE4SS-settings.ini" },
    InstallRoot: "UeProjectBinariesWin64",                     // the new symbol
    // Glob form — only meaningful once PathGate understands a leading-* basename suffix match
    // scoped to the install root. See §3 items 2-3 and §5 PathGate.
    ForbiddenPaths: new[] { "*-Shipping.exe" })
```

`SteamAppId: null` makes `KnownFramework.Classify` skip the app-id gate, so any `ue-pak` drop matches
regardless of which game is active. Correct for a generic UE loader; ELM keeps its pinned SteamAppId.

---

## 5. The exact code changes (files + signatures)

### `src/ModManager.Core/Catalog/KnownFramework.cs`
- Append the catalog row from §4. No record-shape change.

### `src/ModManager.Core/FrameworkDeps.cs`
- Promote `ProjectSubfolder(string relPath)` from `private static` to `internal static` so the
  installer reuses the one true "first segment, skip `Content`" rule. Detection and install can never
  drift on where `R5` is. No new public surface.

### `src/ModManager.Core/Frameworks/FrameworkInstaller.cs`
- Add the overload (keep the 2-arg one for `GameRoot`/`PlayFolder` and all existing callers/tests):

  ```csharp
  public static string ResolveInstallRoot(
      string installRootSymbol, string gameRoot,
      IReadOnlyList<string>? modLocationRelPaths = null)
  ```

- Add the third switch arm:

  ```csharp
  "UeProjectBinariesWin64" => ResolveUeProjectBinariesWin64(gameRoot, modLocationRelPaths),
  ```

- New resolver. Returns an **absolute** path; returns `null` (not throw) when no subfolder resolves,
  so `Install` can convert it to a `Fail` (see §3 item 5). Keep the unknown-symbol arm throwing.

  ```csharp
  private static string? ResolveUeProjectBinariesWin64(
      string gameRoot, IReadOnlyList<string>? modLocationRelPaths)
  {
      // Same rule as FrameworkDeps.ResolveProbeRoots: first ue-pak mod-location whose first path
      // segment is a real project subfolder (skips a "Content/..."-rooted fallback location).
      // e.g. "R5/Content/Paks/~mods" -> "R5".
      var sub = (modLocationRelPaths ?? Array.Empty<string>())
          .Select(FrameworkDeps.ProjectSubfolder)   // promoted internal helper
          .FirstOrDefault(s => s is not null);
      if (sub is null)
          return null;
      return Path.GetFullPath(Path.Combine(gameRoot, sub, "Binaries", "Win64"));  // absolute, CWD-independent
  }
  ```

- Widen `Install` with the same optional parameter and forward it:

  ```csharp
  public static FrameworkInstallResult Install(
      string archivePath, KnownFramework framework, string gameRoot, string gameDataDir,
      IReadOnlyList<string>? modLocationRelPaths = null)
  ```

  Inside, where it currently does `ResolveInstallRoot(framework.InstallRoot, gameRoot)`:

  ```csharp
  string? installRoot = ResolveInstallRoot(framework.InstallRoot, gameRoot, modLocationRelPaths);
  if (installRoot is null)
      return FrameworkInstallResult.Fail(
          "UE4SS install needs a project subfolder from the game's ue-pak mod-locations, " +
          "but none was resolvable. Nothing was changed.");
  ```

- **Wrong-subfolder precondition (§3 item 4)** — after resolving, before the validate/backup/extract
  block: require `installRoot` to exist and to contain a file whose basename matches `*-Shipping.exe`.
  If not, `return FrameworkInstallResult.Fail("Resolved install folder has no game executable to
  proxy. Nothing was changed.")`. This runs before any disk write, consistent with validate-then-extract.

- Everything downstream of the resolved `installRoot` is unchanged: `PathGate.IsContained` /
  `PathGate.IsForbidden` validation loop, the backup-overwrites pass, the extract pass, and the
  camelCase `install.json` manifest write with `InstallPath = installRoot` (now a deeper absolute
  path under the same key — same round-trip, no schema change).

### `src/ModManager.Core/.../PathGate.cs`
- Extend `IsForbidden` to recognize a forbidden entry beginning with `*` as a **basename `EndsWith`**
  match, scoped to the **install root** (top-level entries), not arbitrary depth. Literal entries keep
  their current exact basename / full-relative-path behavior. This is the only behavior change to the
  gate; it is additive and ELM-neutral (ELM lists no glob entries).

### `src/ModManager.App/ViewModels/MainViewModel.cs`
- In `TryInstallFrameworksAsync` / `AddModsAsync`, two spots, no new context plumbing — `_ctx.Game`
  already holds `ModLocations`:

  ```csharp
  var relPaths = _ctx.Game.ModLocations.Select(l => l.Path).ToList();

  // dialog / overwrite-preview resolution (was: ResolveInstallRoot(match.InstallRoot, _ctx.GameRoot))
  var resolvedInstallRoot =
      FrameworkInstaller.ResolveInstallRoot(classify.Match.InstallRoot, _ctx.GameRoot, relPaths);

  // install call (was: Install(src, match, _ctx.GameRoot, _ctx.DataDir))
  var r = FrameworkInstaller.Install(src, classify.Match, _ctx.GameRoot, _ctx.DataDir, relPaths);
  ```

  The `willOverwrite` check and dialog preview consume `resolvedInstallRoot`, so they now show the
  truthful `.../R5/Binaries/Win64`. Cosmetic: the status string hardcoded "at game root" should read
  `r.InstallPath` instead — for a UE4SS install "at game root" is a lie. Not load-bearing.

- For `resolvedInstallRoot` being `null` in the preview path (no subfolder): render the same refusal
  the install would, rather than dereferencing null.

### Uninstall — ZERO change
`FrameworkRegistry.Uninstall` resolves files against `manifest.InstallPath` (falling back to
`gameRoot` only when `InstallPath` is empty), never re-resolving the symbol. Because `Install` freezes
the resolved **absolute** `installRoot` into `InstallPath`, a UE4SS manifest carries the full
`<gameRoot>/R5/Binaries/Win64` string; `Uninstall` deletes `InstalledFiles` relative to it and
restores any timestamped backup over the same root. The symbol never re-enters at uninstall time —
one-shot resolution + persisted-absolute path is the load-bearing invariant that lets the symbol model
survive a project-aware root.

---

## 6. TDD task list (mirrors the existing FrameworkInstaller test matrix)

Land each as a **failing** headless xUnit test first, then implement. Files under
`tests/ModManager.Tests/Frameworks/` (and `Catalog/`). No WinUI/WinRT — `CorePurityTests` stays green.

1. **Resolver — project subfolder.**
   `ResolveInstallRoot("UeProjectBinariesWin64", gameRoot, new[]{"R5/Content/Paks/~mods"})`
   == `Path.GetFullPath(<gameRoot>/R5/Binaries/Win64)`. Pure string function, one assertion.

2. **Resolver — no subfolder → null.**
   `ResolveInstallRoot("UeProjectBinariesWin64", gameRoot, new[]{"Content/Paks/~mods"})` == `null`
   (fallback-rooted location yields no project subfolder).

3. **Happy-path project-subfolder install.** Fixture zip laying down `dwmapi.dll` + `ue4ss/UE4SS.dll`
   + `ue4ss/UE4SS-settings.ini`, into a `gameRoot` whose `R5/Binaries/Win64` exists and contains a
   stub `Windrose-Win64-Shipping.exe`. Assert files land under the resolved root, the exe is
   untouched, manifest `InstallPath` is the absolute `.../R5/Binaries/Win64`.

4. **Forbidden `*-Shipping.exe` refusal.** Hostile zip dropping `Foo-Win64-Shipping.exe` at the
   install root → `Refused`/`Fail`, game folder byte-for-byte unchanged (validate-then-extract).

5. **Forbidden matcher does NOT over-protect.** Zip with a legit `ue4ss/Mods/Cool-Shipping.exe`
   (same suffix, nested) → installs fine; the deep file is not refused. Pins both directions of the
   PathGate suffix matcher.

6. **Wrong-framework no-match.** A zip missing one of the UE4SS-unique pair (e.g. only `UE4SS.dll`,
   no `UE4SS-settings.ini`) → `Classify` returns no UE4SS match; no write.

7. **Directory-traversal refusal.** Zip entry `../../Windows/System32/x.dll` →
   `PathGate.IsContained` fails → `Refused`, game folder unchanged.

8. **Missing-subfolder returns Fail (not throw).** `Install` on a game whose only mod-location is
   `Content/Paks/~mods` → `FrameworkInstallResult.Fail`, no exception, no write.

9. **Wrong-subfolder precondition.** Resolved `Binaries/Win64` exists but contains no `*-Shipping.exe`
   → `Fail` before any write.

10. **Round-trip install → uninstall byte-for-byte.** Install into the §6.3 fixture, snapshot the
    folder, `Uninstall` via the persisted manifest, assert the game folder matches pre-install state
    byte-for-byte (proves persisted-absolute `InstallPath` drives reversal).

11. **Multi-game classify (no SteamAppId scoping).**
    `Classify(ue4ss-pair-names, "ue-pak", "3041230")` matches AND
    `Classify(ue4ss-pair-names, "ue-pak", "1623730" /* Palworld */)` also matches — proves `SteamAppId:
    null` gives engine-only, N-game matching.

12. **Proxy-is-hint variant.** `Classify` matches a zip whose proxy is `xinput1_3.dll` (not
    `dwmapi.dll`) so long as the UE4SS-unique pair is present — proves the proxy is a hint, not a
    required signature.

### Verification debt — re-confirm before merge
The survival verdict leans on code citations not all re-read this session. Before merge, independently
confirm: (1) `PathGate.IsForbidden` is literal basename / full-relative-path only (no existing glob),
so the suffix-matcher addition is genuinely additive; (2) `FrameworkRegistry.Uninstall` joins
`InstalledFiles` against the persisted absolute `InstallPath` for a nested root; (3) the
`MainViewModel` caller renders a resolver `Fail`/null cleanly rather than crashing.

---

## 7. Honor-the-builders

UE4SS is built by the **RE-UE4SS team**. The catalog entry is metadata only — `FrameworkId`,
`DisplayName`, `Author: "RE-UE4SS team"`, `GetUrl:
https://github.com/UE4SS-RE/RE-UE4SS/releases`, and fingerprint filenames. **Never bundle the
binary.** No `UE4SS.dll`, no proxy DLL, no `.zip` ships in the repo or release — the launcher points
the user at the official release page and installs what the user supplies.

Add a UE4SS row to `NOTICE` alongside the existing framework attributions: name, author/team, project
URL, and the catalog-metadata-only / never-bundled language already used for every other entry. The
on-disk manifest stays camelCase (`installPath`, `installedFiles`, ...) — no new persisted shape, same
round-trip as ELM.
