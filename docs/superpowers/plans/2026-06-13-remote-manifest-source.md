# Game Manifest — App fetch glue, slice B: RemoteManifestSource (App shell)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The thin App shell that drives the remote-feed path: apply the cached signed manifest at startup (before any facade reads), and refresh the cache from the feed URL in the background (debounced), gated by an "auto-update definitions" setting. Ships **dark** — the feed URL is empty until the `626-game-manifest` repo exists.

**Architecture:** A new `ModManager.App.Services.RemoteManifestSource`: a static `ApplyCachedAtStartup()` called from `Program.Main` (after Velopack, before `Application.Start`, so the cached feed applies before the UI/facades), and an instance `RefreshAsync()` called fire-and-forget from `OnLaunched` (mirrors `UpdateChecker`: 24h debounce, swallow errors). Both gate on `AppSettingsService.AutoUpdateDefinitions` (new, default on). All the verify/apply/cache logic lives in the already-tested Core `RemoteManifestCache` + `ManifestLoader`; this shell only does HttpClient + wiring.

**Tech Stack:** .NET 10, WinUI 3, `System.Net.Http` (App-only), xUnit (Core unaffected). **App-layer — not coverable by the headless test suite; verified by `dotnet build` (warnings-as-errors) + the Core suite staying green + review.**

**Spec:** roadmap §4/§5/§6; runbook `docs/manifest-feed-runbook.md` (item 2: the App-side `RemoteManifestSource`).

---

## Scope

**In:** `RemoteManifestSource` (App), the `AutoUpdateDefinitions` setting (field + persistence, default on), and the `Program.Main` / `App.OnLaunched` / DI wiring. **Out:** the SettingsDialog UI toggle (a tiny XAML follow-up — the field defaults on and the feature is dark, so there's nothing to switch off yet); the feed URL (empty until the feed repo exists); the feed repo + signing CI (your go-live actions).

**Ships dark:** `RemoteManifestSource.FeedUrl` is `""`, so `RefreshAsync` no-ops (no network). `ApplyCachedAtStartup` finds no cache → no-op → embedded. Zero behavior change until a release sets the URL and a feed exists.

**No Core change.** This slice is entirely `src/ModManager.App/`. The Core suite must stay green and unchanged.

## Current shapes this builds on (on `master`)

- `RemoteManifestCache.ApplyCached(string cacheDir, Version binaryVersion, byte[]? publicKey = null) → bool` and `WriteCache(string, byte[], byte[])` (Core; the tested heart).
- `Program.Main` (`src/ModManager.App/Program.cs`): `VelopackApp.Build()...Run()` then `Application.Start(() => new App())`. The pre-WinUI hook is between those.
- `App.OnLaunched` (`src/ModManager.App/App.xaml.cs:52`): creates `MainWindow`, then `_ = AppHost.Services.GetRequiredService<UpdateChecker>().CheckForUpdatesAsync();`. DI `ConfigureServices` already registers `HttpClient` + `AppSettingsService`.
- `UpdateChecker` (`src/ModManager.App/Services/UpdateChecker.cs`): the debounce pattern to mirror — `%LOCALAPPDATA%\ModManagerBuilder\last-update-check.txt`, `ShouldCheck()`/`StampNow()`, swallow-all errors.
- `AppSettingsService` (`src/ModManager.App/Services/AppSettingsService.cs`): `%APPDATA%\ModManagerBuilder\app-settings.json`, tolerant `Load()`, hand-rolled `Save()` (camelCase keys). Currently persists only `backdrop`.

---

## File Structure

- Create: `src/ModManager.App/Services/RemoteManifestSource.cs`
- Modify: `src/ModManager.App/Services/AppSettingsService.cs` — add `AutoUpdateDefinitions` (default true).
- Modify: `src/ModManager.App/Program.cs` — call `RemoteManifestSource.ApplyCachedAtStartup()`.
- Modify: `src/ModManager.App/App.xaml.cs` — register `RemoteManifestSource` in DI + call `RefreshAsync()` in `OnLaunched`.

**Build (the verification gate):** `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
**Core suite (must stay green):** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

---

### Task 1: AutoUpdateDefinitions setting (default on)

**Files:**
- Modify: `src/ModManager.App/Services/AppSettingsService.cs`

- [ ] **Step 1: Add the field + persistence**

In `AppSettingsService`, add an `AutoUpdateDefinitions` bool defaulting to **true**, persisted alongside `backdrop` (camelCase key `autoUpdateDefinitions`, per the on-disk-JSON rule). Edits:

Add field + property + setter (near `_backdrop`):

```csharp
    private bool _autoUpdateDefinitions;

    /// <summary>Whether the launcher fetches + applies remote game-definition updates (default on).
    /// When off, the embedded manifest is used and no manifest fetch occurs.</summary>
    public bool AutoUpdateDefinitions => _autoUpdateDefinitions;

    public void SetAutoUpdateDefinitions(bool enabled)
    {
        if (_autoUpdateDefinitions == enabled) return;
        _autoUpdateDefinitions = enabled;
        Save();
    }
```

In the constructor, after `_backdrop = Load();`, load the new field:

```csharp
        _autoUpdateDefinitions = LoadAutoUpdate();
```

Add the loader (tolerant; **absent → true**):

```csharp
    private bool LoadAutoUpdate()
    {
        try
        {
            if (!File.Exists(Path)) return true;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path));
            if (doc.RootElement.TryGetProperty("autoUpdateDefinitions", out var v)
                && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False))
                return v.GetBoolean();
        }
        catch { /* missing / corrupt — default on */ }
        return true;
    }
```

Update `Save()` to write **both** fields (keep camelCase keys):

```csharp
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var json =
                $"{{\"backdrop\":\"{_backdrop.ToString().ToLowerInvariant()}\","
                + $"\"autoUpdateDefinitions\":{(_autoUpdateDefinitions ? "true" : "false")}}}";
            File.WriteAllText(Path, json);
        }
        catch { /* best-effort persist; in-memory state still holds */ }
    }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 Errors. (Confirm `backdrop` round-trip still intact: `SetBackdrop` then `SetAutoUpdateDefinitions` both write both keys.)

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.App/Services/AppSettingsService.cs
git commit -m "feat(settings): AutoUpdateDefinitions preference (default on)"
```

---

### Task 2: RemoteManifestSource service

**Files:**
- Create: `src/ModManager.App/Services/RemoteManifestSource.cs`

- [ ] **Step 1: Implement the service**

`src/ModManager.App/Services/RemoteManifestSource.cs`:

```csharp
using System.IO;
using System.Net.Http;
using System.Reflection;
using ModManager.Core.Manifest;

namespace ModManager.App.Services;

/// <summary>
/// Drives the remote game-definition feed. At startup, <see cref="ApplyCachedAtStartup"/> applies the
/// last-fetched manifest from the on-disk cache (verified against the pinned key in Core) BEFORE the
/// UI / facades read — so a slow or offline network never blocks launch. In the background,
/// <see cref="RefreshAsync"/> re-fetches the feed (debounced 24h) into the cache for the NEXT launch.
/// Both are gated on the "auto-update definitions" setting. Ships dark: <see cref="FeedUrl"/> is empty
/// until the feed repo exists, so nothing is fetched. Auto-update is comfort, not load-bearing —
/// every failure is swallowed; the embedded manifest is always the floor.
/// </summary>
public sealed class RemoteManifestSource
{
    // Empty until the 626-game-manifest feed is published. Set this (manifest URL) at go-live;
    // the .sig URL is derived as <FeedUrl>.sig. Empty => RefreshAsync no-ops (ships dark).
    private const string FeedUrl = "";

    private static readonly TimeSpan DebounceWindow = TimeSpan.FromHours(24);

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModManagerBuilder");
    private static readonly string StampPath = Path.Combine(CacheDir, "last-manifest-fetch.txt");

    private static Version AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private readonly HttpClient _http;

    public RemoteManifestSource(HttpClient http) => _http = http;

    /// <summary>Apply the cached feed (if the setting is on and a cache exists). Static so
    /// <c>Program.Main</c> can call it before the WinUI app / DI host spin up — i.e. before any
    /// facade reads. Never throws.</summary>
    public static void ApplyCachedAtStartup()
    {
        try
        {
            if (!new AppSettingsService().AutoUpdateDefinitions) return;
            RemoteManifestCache.ApplyCached(CacheDir, AppVersion);
        }
        catch { /* never block launch on the cache */ }
    }

    /// <summary>Background refresh for the NEXT launch: fetch the feed + signature into the cache,
    /// debounced once per 24h. No-op when the setting is off or the feed URL is empty (dark).</summary>
    public async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(FeedUrl)) return;          // ships dark until go-live
        if (!new AppSettingsService().AutoUpdateDefinitions) return;
        if (!ShouldFetch()) return;

        try
        {
            var manifestBytes = await _http.GetByteArrayAsync(FeedUrl).ConfigureAwait(false);
            var sigBytes = await _http.GetByteArrayAsync(FeedUrl + ".sig").ConfigureAwait(false);
            RemoteManifestCache.WriteCache(CacheDir, manifestBytes, sigBytes);
        }
        catch
        {
            // Swallow — the cached/embedded manifest is the floor.
        }
        finally
        {
            StampNow();
        }
    }

    private static bool ShouldFetch()
    {
        try
        {
            if (!File.Exists(StampPath)) return true;
            var last = File.ReadAllText(StampPath).Trim();
            if (!DateTime.TryParse(last, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t))
                return true;
            return DateTime.UtcNow - t >= DebounceWindow;
        }
        catch { return true; }
    }

    private static void StampNow()
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllText(StampPath, DateTime.UtcNow.ToString("O"));
        }
        catch { /* best-effort */ }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 Errors.

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.App/Services/RemoteManifestSource.cs
git commit -m "feat(app): RemoteManifestSource — apply cached feed at startup + background refresh (dark)"
```

---

### Task 3: Wire into startup + DI

**Files:**
- Modify: `src/ModManager.App/Program.cs`
- Modify: `src/ModManager.App/App.xaml.cs`

- [ ] **Step 1: Apply the cached feed before WinUI starts**

In `src/ModManager.App/Program.cs`, add the apply call after Velopack, before `Application.Start` (so the cached feed is effective before any facade/UI):

```csharp
        VelopackApp.Build().SetArgs(args).Run();

        // Apply the cached remote game-definition manifest (if enabled) BEFORE WinUI / the facades
        // read it. Verified against the pinned key in Core; no-op when disabled or no cache. Dark
        // until a feed exists.
        ModManager.App.Services.RemoteManifestSource.ApplyCachedAtStartup();

        Microsoft.UI.Xaml.Application.Start((p) =>
        {
```

- [ ] **Step 2: Register + background-refresh in App**

In `src/ModManager.App/App.xaml.cs`, register the service in `ConfigureServices` (near `UpdateChecker`):

```csharp
                services.AddSingleton<UpdateChecker>();
                services.AddSingleton<RemoteManifestSource>();
```

And in `OnLaunched`, after the `UpdateChecker` call, kick the background refresh:

```csharp
        _ = AppHost.Services.GetRequiredService<UpdateChecker>().CheckForUpdatesAsync();

        // Refresh the remote game-definition cache for the next launch (debounced, dark until a
        // feed URL is set). Fire-and-forget; failures are swallowed.
        _ = AppHost.Services.GetRequiredService<RemoteManifestSource>().RefreshAsync();
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 Errors.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/Program.cs src/ModManager.App/App.xaml.cs
git commit -m "feat(app): wire RemoteManifestSource into startup (apply cached) + OnLaunched (refresh)"
```

---

### Task 4: Build + Core suite + scope verification

**Files:** none (verification only).

- [ ] **Step 1: App builds clean**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 Errors. (Pre-existing `MVVMTK0045` warnings on unrelated ViewModels are fine — they predate this slice; do not "fix" them.)

- [ ] **Step 2: Core suite unaffected**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: same green as master (1159 passed / 2 skipped) — this slice changes no Core or test code.

- [ ] **Step 3: Scope**

Run: `git diff --name-only master..HEAD -- src/`
Expected: only the four App files (`Services/RemoteManifestSource.cs`, `Services/AppSettingsService.cs`, `Program.cs`, `App.xaml.cs`). No `src/ModManager.Core` change. No new test project changes.

- [ ] **Step 4: Final commit (if needed)**

```bash
git add -A && git commit -m "chore(app): RemoteManifestSource slice — App builds, Core green"
```

(Skip if clean.)

---

## Self-Review

**Spec coverage:** runbook item 2 (App `RemoteManifestSource`: fetch → cache → apply at startup + toggle) → Tasks 1–3. The SettingsDialog UI toggle is deferred (noted in Scope); the feed URL + repo + signing are the go-live actions. ✓

**Placeholder scan:** `FeedUrl = ""` is intentional (ships dark), documented — not a TODO. ✓

**Type consistency:** `RemoteManifestSource.ApplyCachedAtStartup()` (static, called from `Program.Main`) + `RefreshAsync()` (instance, DI, called from `OnLaunched`) + `AppSettingsService.AutoUpdateDefinitions`/`SetAutoUpdateDefinitions` consistent across the files. Uses real Core members (`RemoteManifestCache.ApplyCached`/`WriteCache`). ✓

**Testability honesty:** App-layer, not in the headless suite — verified by `dotnet build` (0 errors, warnings-as-errors) + the Core suite staying green + review. All the *logic* that matters (verify/validate/apply/cache, atomic write) is in Core and already unit-tested; this shell is HttpClient + wiring. `AppSettingsService` persistence is untested like its existing `backdrop` field (App convention) — camelCase key ensured.

**Judgment flagged:** `ApplyCachedAtStartup` constructs a transient `AppSettingsService` in `Program.Main` (pre-DI) to read the toggle — intentional, cheap, reads the same file as the DI singleton. The startup apply is synchronous (fast: a local file read + verify) so it completes before `Application.Start`; the network refresh is fully async/background. Ships dark (`FeedUrl` empty) — zero behavior change until go-live sets the URL.
