# The library home does not repaint after Add Game

**Date:** 2026-08-16
**Backlog:** proposed A10 (new)
**Scope:** App layer only. No Core change, no data-model change.
**Status:** spec, ready for a plan
**Found on:** Store SKU `0.17.0.0` (MSIX, `626LabsLLC.626ModLauncher_wz1chhb2h2v4a`), clean box, Windrose as the first game ever added.

---

## Why

On a fresh install a user added Windrose through `+ Game` → *Quick add from Steam*. The library kept
showing **"Your library is empty."** They tried twice more. After restarting the app, all three
copies appeared at once.

The add was never the thing that failed. `games.json` had all three entries, correctly engine-detected
as `ue-pak`, written to the MSIX-virtualized data root. Nothing threw: no `app-errors.log` in either
the packaged or unpackaged location.

**The registration commits. The home renders a stale snapshot until something navigates back to it.**

Three duplicate registrations are the user-visible cost, and they are the rational response to a
surface that reports nothing happened.

### What this was not

The first-write-under-package-identity theory is wrong, and worth recording so it is not re-filed:

| Suspected | Actual |
|---|---|
| MSIX redirect swallowed the first write | Redirect works. `LocalCache\Roaming\ModManagerBuilder\games.json`, 4312 bytes, correct content. `%APPDATA%\ModManagerBuilder` being absent is correct under package identity, not a symptom. |
| Engine detection failed on Windrose | Detected `ue-pak`. `R5\Content\Paks` present, default Steam library, `StateFlags` 4. |
| Something failed earlier and silently | Nothing failed. No error log, no swallowed exception on the add path. |
| Mod locations drifted (`paks-root` vs `~mods` / `LogicMods`) | Correct behavior. `EnginePresets.cs:127` selects `paks-root` when neither loader folder exists. A loader-less install resolves differently from a loader-present one by design. |

---

## Root cause

`LibraryViewModel.Load()` (`LibraryViewModel.cs:305`) re-reads the registry from disk and rebuilds
every home row. It is the only thing that does. Its own comment states the contract it depends on:

> `Load()` runs on every return to the home, so the snapshot refreshes exactly when the home does.

That contract holds for every path that reaches the home, and the add path does not reach the home.

`ShowLibrary()` (`MainWindow.xaml.cs:249`) is the sole caller of `_libraryVm.Load()`. Its callers:

| Site | Path | Repaints |
|---|---|---|
| `MainWindow.xaml.cs:167` | startup | yes |
| `MainWindow.xaml.cs:276` | Home button | yes |
| `MainWindow.xaml.cs:308` | back out of a game | yes |
| `MainWindow.xaml.cs:370` | discovery-lane add | yes, but see *Defect 2* |
| `MainWindow.xaml.cs:661-683` | **`+ Game` dialog** | **no** |

`OnAddGame` returns from both of its exits without repainting: the batch branch `return`s at 679, the
single-game branch falls off the end at 682.

The two refresh mechanisms that *do* run on an add are both pointed at the other surface:

- `AddGameAsync` (`MainViewModel.cs:3672`) awaits `LoadAsync()`, which reloads the **mod view**.
- The `RegistryChanged` subscription (`MainWindow.xaml.cs:207`) calls `ViewModel.RefreshAsync()`, also
  the **mod view**. `LauncherService.AddGame` does not raise it in any case.

So the home keeps the row set it built at startup, when the registry was empty. Restart hits site 167
and every registration appears at once, which is exactly the reported behavior.

**Falsifiable prediction.** Adding through the *"Installed games not added yet"* lane on the home
repaints immediately (site 370); adding the same game through `+ Game` does not. Verify this before
writing code. If it does not hold, this diagnosis is wrong and the plan stops.

---

## Defect 2: the discovery fallback cannot be awaited

Found while tracing the above, same function, ships with the same fix.

`OnAddGame` is `async void`. `AddDiscoveredGameAsync` calls it at `MainWindow.xaml.cs:390` as the
undetectable-engine fallback:

```csharp
// Undetectable engine — the full dialog lets the user pick it. Same handler the + Game button uses.
OnAddGame(this, new RoutedEventArgs());
```

`async void` cannot be awaited, so `AddDiscoveredGameAsync` returns immediately, and
`OnLibraryAddGameRequested` (`MainWindow.xaml.cs:367-371`) runs its `ShowLibrary()` **while the dialog
is still open**. The repaint on that branch fires before the user has chosen an engine, and is
therefore always too early to include the game being added.

Site 370's repaint is correct for the detectable-engine branch and useless for the fallback branch.
Fixing defect 1 without fixing this leaves a second path that still fails to repaint.

---

## The fix

### Shape

Extract the body of `OnAddGame` into an awaitable method, repaint at its end, and let the fallback
await it.

```csharp
private async void OnAddGame(object sender, RoutedEventArgs e) => await AddGameViaDialogAsync();

private async Task AddGameViaDialogAsync()
{
    // ... existing body, both branches ...
    // single exit: repaint the home's row set
    _libraryVm.Load();
}
```

and at `MainWindow.xaml.cs:390`:

```csharp
await AddGameViaDialogAsync();
```

The event handler stays `async void` because a XAML `Click` handler must be. Everything it does moves
into a `Task` the other caller can await.

### Repaint with `_libraryVm.Load()`, not `ShowLibrary()`

`ShowLibrary()` also navigates: it calls `HideCatalog()`, `HideUpdates()`, shows `LibraryHost`, and
hides the game-context title-bar controls. `+ Game` is reachable from inside a game's mod view, so
calling it there would yank the user out of the game they are managing. `Load()` refreshes the data
without touching navigation, which is the whole of what is missing.

When the user is already on the home (the reported case) the rows repaint in place. When they are in
a game view the reload is invisible and harmless, and site 276/308 would have reloaded on the way home
regardless.

### Rejected: route it through `RegistryChanged`

The structurally tidier option is to make `LauncherService.AddGame` raise `NotifyRegistryChanged()`
and extend the `MainWindow` handler to reload the library VM too, so every registry mutation repaints
both surfaces.

Rejected for this fix. `AddGameAsync` already calls `LoadAsync()`, so the handler's `RefreshAsync()`
would run a second full refresh inside `AddGameAsync`'s busy window. This subsystem has already shipped
one long-operation-slot re-entrancy fix (C1, `8d8a17d`); adding a re-entrant refresh to the add path is
the wrong trade for a cosmetic gain.

Worth revisiting as its own change if a third add-path appears. Note it in the backlog, do not bundle it.

---

## Blast radius

Confined to `MainWindow.xaml.cs`. One method extracted, one call site changed, one added `Load()` call.

- `LibraryViewModel.Load()` is already called on every home entry, so calling it once more is a path
  the code takes constantly. It re-reads `games.json` and rebuilds rows; no writes, nothing destructive.
- No Core change, so `CorePurityTests` and the security-guard suites are untouched.
- The batch branch's `StatusText` message must survive the refactor. `Load()` goes after it.

**Pre-existing, deliberately not changed:** `LauncherService.AddGame` sets `ActiveGameId` to the newly
added game (`LauncherService.cs:82`), so adding from the home silently changes the active game
underneath. That is current behavior on every add path, unrelated to the repaint, and changing it here
would smuggle a behavior change into a bugfix.

---

## Verification

**No failing test is honestly available for this fix.** `tests/ModManager.Tests.csproj` references
Core, Mcp, Plugins.Abstractions, and ManifestMiner. It does not reference `ModManager.App`, so
`MainWindow` and `LibraryViewModel` are unreachable from the suite. Adding that reference would pull
WinUI into the test project, which is the exact thing that hangs a bare `dotnet test` at the repo root.

Writing a Core test here would prove something other than the fix and close the item green while the
bug survives. That is the failure mode A6 already warns about in this backlog. So:

1. Confirm the falsifiable prediction above before writing code.
2. `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` stays at 1862 passing / 2 skipped.
3. Manual, on the packaged SKU, from a genuinely empty registry:
   - `+ Game` → Quick add from Steam → one game → **row appears without restart**.
   - `+ Game` → batch, two games → **both rows appear**, batch status text still shown.
   - Discovery lane add of a detectable-engine game → still repaints.
   - Discovery lane add of an **undetectable**-engine game → dialog opens, engine chosen, **row appears
     without restart** (defect 2).
   - `+ Game` invoked from inside a game's mod view → **stays in that game**, no navigation jump.

If a test seam is wanted later, the honest one is an App-layer test project referencing
`ModManager.App`. That is its own piece of work and does not belong in this fix.

---

## Out of scope

- **The `+ Game` dialog does not scroll.** Reported in the same session. `AddGameDialog.xaml:33`
  already wraps the body in `ScrollViewer MaxHeight="640"`, landed in `05836ee` (2026-06-14) and
  **shipped in v0.17.0**, so this is a live failure on top of an existing fix rather than pending work.
  Not yet root-caused, and it is not getting a guess. Separate investigation, separate spec.
- **The three duplicate Windrose registrations** on the reporting box. User-side cleanup in the app.
  Whether `AddGame` should refuse a duplicate `steamAppId` + `gameRoot` is a product question, not part
  of this fix.
