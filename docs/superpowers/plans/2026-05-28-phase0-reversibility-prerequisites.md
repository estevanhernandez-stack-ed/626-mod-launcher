# Phase 0 — Reversibility Prerequisites Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the two pre-existing reversibility bugs and add the move/verify/space/path primitives that the Safe Clear engine (Phase 1) will stand on — all test-first, all in pure Core.

**Architecture:** Each item is an independent, shippable hardening of the *current* app. Bug fixes (`FrameworkRegistry.Uninstall`, `EnableMod` rollback) come with regression-proof round-trip tests; new primitives (`PathGate`, `SafeMove`, `SpaceCheck`) are small pure helpers with direct unit tests. No App/WinUI changes in this phase.

**Tech Stack:** .NET 10, C#, xUnit. `CorePurityTests` guards the namespace boundary. camelCase-on-disk rule governs the one new persisted shape (`ModMeta` additions).

**Spec:** [`2026-05-28-phase0-reversibility-prerequisites-design.md`](../specs/2026-05-28-phase0-reversibility-prerequisites-design.md)

**Test command (never bare `dotnet test` at the repo root — the WinUI App project hangs the build):**
`dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Single test: append `--filter "FullyQualifiedName~<TestName>"`.

**Scope note — what moved to Phase 1:** The spec's P0.6 had two halves. The **Core enabler** (`ModMeta.installedUtc` + `sourceConfidence` + round-trip test) ships here (Task 7). The **App-side intake prompt + opt-in backfill sweep** are App/WinUI work whose only consumer is the off-boarding sheet, so they move to Phase 1 alongside the rest of the App surface. This is a deliberate re-scope, not a silent gap.

---

## File Structure

| File | Responsibility | Action |
|---|---|---|
| `src/ModManager.Core/Frameworks/FrameworkRegistry.cs` | Resolve uninstall paths against the manifest's `InstallPath` | Modify |
| `src/ModManager.Core/PathGate.cs` | The one containment + forbidden-path + safe-relative gate (shared by install AND Phase-1 restore) | Create |
| `src/ModManager.Core/Frameworks/FrameworkInstaller.cs` | Use `PathGate` instead of inline checks (no behavior change) | Modify |
| `src/ModManager.Core/DirectInject.cs` | `SafeRelative` delegates to `PathGate.SafeRelative` | Modify |
| `src/ModManager.Core/SafeMove.cs` | Cross-volume-safe move: copy → verify size → delete; sharing violations surface | Create |
| `src/ModManager.Core/Scanner.cs` | `MoveAny` delegates to `SafeMove`; `EnableMod` gains rollback + `EnableOutcome`; `DisableEntry` Phase-2 snapshot-first | Modify |
| `src/ModManager.Core/SpaceCheck.cs` | Free-space pre-flight (testable bytes overload + DriveInfo production overload) | Create |
| `src/ModManager.Core/Mod.cs` | `ModMeta.installedUtc` + `sourceConfidence` | Modify |
| `tests/ModManager.Tests/Frameworks/FrameworkRegistryTests.cs` | PlayFolder uninstall round-trip | Modify |
| `tests/ModManager.Tests/PathGateTests.cs` | Containment / forbidden / safe-relative | Create |
| `tests/ModManager.Tests/SafeMoveTests.cs` | Verified copy + same-volume move | Create |
| `tests/ModManager.Tests/ScannerEnableRollbackTests.cs` | Enable rollback + structured outcomes + mirror round-trip | Create |
| `tests/ModManager.Tests/SpaceCheckTests.cs` | Headroom math + ok/short | Create |
| `tests/ModManager.Tests/ModMetaRoundTripTests.cs` | camelCase round-trip of new fields | Create |
| `.claude/rules/camelcase-json-on-disk.md` | Add `ModMeta` to governed-surfaces list | Modify |

---

## Task 1: Fix `FrameworkRegistry.Uninstall` path resolution

**Files:**
- Modify: `src/ModManager.Core/Frameworks/FrameworkRegistry.cs:44-77`
- Test: `tests/ModManager.Tests/Frameworks/FrameworkRegistryTests.cs` (add one method; `WriteManifest` helper already exists at line 106)

- [ ] **Step 1: Write the failing test** (add to `FrameworkRegistryTests`)

```csharp
[Fact]
public void Uninstall_resolves_files_against_InstallPath_not_gameRoot()
{
    // FromSoft case: framework installed under <gameRoot>\Game\ (PlayFolder). The manifest's
    // InstallPath points at Game\; the bug resolved against gameRoot and missed the files.
    var gameRoot = Path.Combine(_tmp, "GameRoot");
    var playFolder = Path.Combine(gameRoot, "Game");
    var gameData = Path.Combine(_tmp, "GameData");
    Directory.CreateDirectory(playFolder);
    File.WriteAllBytes(Path.Combine(playFolder, "dinput8.dll"), new byte[] { 1 });

    var fwDir = Path.Combine(gameData, "frameworks", "elden-mod-loader");
    WriteManifest(fwDir, new FrameworkInstallManifest(
        "elden-mod-loader", "Elden Mod Loader", "TechieW",
        playFolder,                                   // InstallPath = PlayFolder
        new[] { "dinput8.dll" }, DateTime.UtcNow, null));

    FrameworkRegistry.Uninstall(gameData, "elden-mod-loader", gameRoot);

    Assert.False(File.Exists(Path.Combine(playFolder, "dinput8.dll")));  // would have been left behind
    Assert.False(Directory.Exists(fwDir));
}
```

- [ ] **Step 2: Run it; verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~Uninstall_resolves_files_against_InstallPath"`
Expected: FAIL — the file under `Game\` is still present (uninstall looked at `<gameRoot>\dinput8.dll`).

- [ ] **Step 3: Fix the resolution** (in `Uninstall`, replace the delete loop and the restore loop)

```csharp
// Resolve against the manifest's recorded install root, not gameRoot. Old manifests that
// predate a reliable InstallPath fall back to gameRoot (the historic behavior).
var installRoot = string.IsNullOrEmpty(m.InstallPath) ? gameRoot : m.InstallPath;

// Delete every installed file. Idempotent — already-gone files are fine.
foreach (var rel in m.InstalledFiles)
{
    var abs = Path.Combine(installRoot, rel);
    try { if (File.Exists(abs)) File.Delete(abs); } catch { /* leave for manual */ }
}

// Restore the backup (if any) back over the install root.
if (!string.IsNullOrEmpty(m.BackupSnapshotPath) && Directory.Exists(m.BackupSnapshotPath))
{
    foreach (var src in Directory.EnumerateFiles(m.BackupSnapshotPath, "*", SearchOption.AllDirectories))
    {
        var rel = Path.GetRelativePath(m.BackupSnapshotPath, src);
        var dst = Path.Combine(installRoot, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(src, dst, overwrite: true);
    }
}
```

- [ ] **Step 4: Run the new test + the existing registry tests; verify all pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FrameworkRegistryTests"`
Expected: PASS (the existing `Uninstall_removes_installed_files_and_restores_backup` still passes because there `InstallPath == gameRoot`).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Frameworks/FrameworkRegistry.cs tests/ModManager.Tests/Frameworks/FrameworkRegistryTests.cs
git commit -m "fix(frameworks): resolve uninstall paths against manifest InstallPath, not gameRoot"
```

---

## Task 2: Extract `PathGate` and route install through it

**Files:**
- Create: `src/ModManager.Core/PathGate.cs`
- Modify: `src/ModManager.Core/Frameworks/FrameworkInstaller.cs:98-121`
- Modify: `src/ModManager.Core/DirectInject.cs:338-352`
- Test: `tests/ModManager.Tests/PathGateTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class PathGateTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "pg-root"));

    [Theory]
    [InlineData("Game/dinput8.dll", true)]
    [InlineData("a/b/c.txt", true)]
    [InlineData("../../Windows/System32/x.dll", false)]
    [InlineData("./x", false)]
    [InlineData("", false)]
    [InlineData("C:/x", false)]
    public void IsContained_accepts_inside_rejects_escapes(string rel, bool expected)
        => Assert.Equal(expected, PathGate.IsContained(rel, Root));

    [Fact]
    public void IsForbidden_matches_basename_and_full_relative_case_insensitively()
    {
        var forbidden = new[] { "eldenring.exe", "Game/regulation.bin" };
        Assert.True(PathGate.IsForbidden("Game/ELDENRING.EXE", forbidden));   // basename match
        Assert.True(PathGate.IsForbidden("game/regulation.bin", forbidden));  // full-relative match
        Assert.False(PathGate.IsForbidden("Game/mod.dll", forbidden));
    }

    [Fact]
    public void SafeRelative_strips_wrapper_and_rejects_traversal()
    {
        Assert.Equal(Path.Combine("inner", "f.dll"), PathGate.SafeRelative("wrap/inner/f.dll", "wrap"));
        Assert.Null(PathGate.SafeRelative("wrap/../escape.dll", "wrap"));
        Assert.Null(PathGate.SafeRelative("dir/", null));   // directory entry
    }
}
```

- [ ] **Step 2: Run it; verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~PathGateTests"`
Expected: FAIL — `PathGate` does not exist (compile error).

- [ ] **Step 3: Create `PathGate`**

```csharp
namespace ModManager.Core;

/// <summary>
/// The one containment + forbidden-path + safe-relative gate shared by every site that writes
/// archive or restore-point contents into a target folder. Extracted from FrameworkInstaller and
/// DirectInject so install and restore enforce identical rules. Pure path math — no disk touch.
/// </summary>
public static class PathGate
{
    /// <summary>Normalize an archive entry to a safe relative path under the destination, or null
    /// for a directory entry or any path that escapes via traversal / absolute / drive-root.
    /// Optionally strips a single wrapper prefix (the zip's top folder).</summary>
    public static string? SafeRelative(string entryName, string? stripPrefix = null)
    {
        var n = entryName.Replace('\\', '/').TrimStart('/');
        if (n.Length == 0 || n.EndsWith("/")) return null;     // directory entry
        if (stripPrefix is not null)
        {
            var p = stripPrefix.TrimEnd('/') + "/";
            if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) n = n[p.Length..];
        }
        if (n.Length == 0) return null;
        var segs = n.Split('/');
        if (segs.Any(s => s is "" or "." or "..")) return null;  // traversal / empty segment
        if (n.Length > 1 && n[1] == ':') return null;            // drive-rooted
        return n.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>True iff <paramref name="relNorm"/> resolves to a path strictly inside
    /// <paramref name="installRootFull"/> (which MUST be fully-qualified — caller passes
    /// Path.GetFullPath(installRoot)). Rejects empty, ".", "..", drive-rooted, and traversal.</summary>
    public static bool IsContained(string relNorm, string installRootFull)
    {
        if (string.IsNullOrEmpty(relNorm)) return false;
        var n = relNorm.Replace('\\', '/').TrimStart('/');
        if (n.Length == 0) return false;
        if (n.Split('/').Any(s => s is "" or "." or "..")) return false;
        if (n.Length > 1 && n[1] == ':') return false;
        var abs = Path.GetFullPath(Path.Combine(installRootFull, n.Replace('/', Path.DirectorySeparatorChar)));
        return abs.StartsWith(installRootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(abs, installRootFull, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True iff <paramref name="relNorm"/> hits a forbidden basename OR full relative
    /// path (case-insensitive).</summary>
    public static bool IsForbidden(string relNorm, IReadOnlyList<string> forbidden)
    {
        var n = relNorm.Replace('\\', '/');
        return forbidden.Any(f =>
            string.Equals(Path.GetFileName(n), f, StringComparison.OrdinalIgnoreCase)
            || string.Equals(n, f, StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 4: Route `FrameworkInstaller.Install` through `PathGate`** (replace the inline containment + forbidden checks at lines 98-121 with this; keep `absTarget` for extraction)

```csharp
var relNorm = entry.FullName.Replace('\\', '/');
if (!PathGate.IsContained(relNorm, installRootFull))
    throw new InvalidOperationException(
        $"Archive entry '{entry.FullName}' resolves outside the install root — refusing install.");
if (PathGate.IsForbidden(relNorm, framework.ForbiddenPaths))
    throw new InvalidOperationException(
        $"Archive contains a forbidden path '{entry.FullName}' — refusing install. " +
        $"Frameworks must never overwrite the game's protected files.");
var absTarget = Path.GetFullPath(Path.Combine(installRoot, relNorm.Replace('/', Path.DirectorySeparatorChar)));
plannedEntries.Add((entry, relNorm, absTarget));
```

- [ ] **Step 5: Delegate `DirectInject.SafeRelative`** (replace its body at lines 338-352)

```csharp
public static string? SafeRelative(string entryName, string? stripPrefix)
    => PathGate.SafeRelative(entryName, stripPrefix);
```

- [ ] **Step 6: Run PathGate tests + the existing framework + direct-inject suites; verify all pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~PathGateTests|FullyQualifiedName~FrameworkInstaller|FullyQualifiedName~DirectInject"`
Expected: PASS — the refactor is behavior-preserving; existing install/traversal tests still guard it.

- [ ] **Step 7: Commit**

```bash
git add src/ModManager.Core/PathGate.cs src/ModManager.Core/Frameworks/FrameworkInstaller.cs src/ModManager.Core/DirectInject.cs tests/ModManager.Tests/PathGateTests.cs
git commit -m "refactor(core): extract shared PathGate (containment + forbidden + safe-relative)"
```

---

## Task 3: `SafeMove` — verified cross-volume move

**Files:**
- Create: `src/ModManager.Core/SafeMove.cs`
- Modify: `src/ModManager.Core/Scanner.cs:300-312` (`MoveAny` delegates)
- Test: `tests/ModManager.Tests/SafeMoveTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class SafeMoveTests
{
    [Fact]
    public void Move_same_volume_renames_file()
    {
        var root = TestSupport.TempDir("safemove-");
        var src = Path.Combine(root, "a.txt"); var dest = Path.Combine(root, "b.txt");
        File.WriteAllText(src, "DATA");
        SafeMove.Move(src, dest);
        Assert.False(File.Exists(src));
        Assert.Equal("DATA", File.ReadAllText(dest));
    }

    [Fact]
    public void CopyFileVerified_copies_bytes_and_leaves_source_intact()
    {
        var root = TestSupport.TempDir("safemove-");
        var src = Path.Combine(root, "a.bin"); var dest = Path.Combine(root, "sub", "a.bin");
        File.WriteAllBytes(src, new byte[] { 1, 2, 3, 4 });
        SafeMove.CopyFileVerified(src, dest);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(dest));
        Assert.True(File.Exists(src));   // a verified copy NEVER deletes the source
    }

    [Fact]
    public void CopyFileVerified_throws_and_preserves_a_preexisting_dest()
    {
        var root = TestSupport.TempDir("safemove-");
        var src = Path.Combine(root, "a.bin"); var dest = Path.Combine(root, "a.bin.copy");
        File.WriteAllBytes(src, new byte[] { 1 });
        File.WriteAllBytes(dest, new byte[] { 9 });
        Assert.ThrowsAny<IOException>(() => SafeMove.CopyFileVerified(src, dest));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(dest));  // pre-existing file untouched
    }

    [Fact]
    public void CopyDirVerified_reproduces_a_nested_tree()
    {
        var root = TestSupport.TempDir("safemove-");
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(Path.Combine(src, "inner"));
        File.WriteAllText(Path.Combine(src, "top.txt"), "T");
        File.WriteAllText(Path.Combine(src, "inner", "deep.txt"), "D");
        var dest = Path.Combine(root, "dest");
        SafeMove.CopyDirVerified(src, dest);
        Assert.Equal("T", File.ReadAllText(Path.Combine(dest, "top.txt")));
        Assert.Equal("D", File.ReadAllText(Path.Combine(dest, "inner", "deep.txt")));
    }
}
```

- [ ] **Step 2: Run it; verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SafeMoveTests"`
Expected: FAIL — `SafeMove` does not exist.

- [ ] **Step 3: Create `SafeMove`**

```csharp
namespace ModManager.Core;

/// <summary>
/// Move a file or directory with cross-volume safety. Same-volume is a fast rename. Cross-volume
/// (or any other movable IOException) copies, VERIFIES per-file size, then deletes the source — an
/// unverified copy never deletes the original. A sharing violation (file in use / game running) is
/// NOT swallowed: it surfaces so the caller can tell the user to close the game, instead of being
/// retried as a doomed copy. Pure System.IO; runs headless.
/// </summary>
public static class SafeMove
{
    private const int HrSharingViolation = unchecked((int)0x80070020);

    public static void Move(string src, string dest)
    {
        try
        {
            if (Directory.Exists(src)) Directory.Move(src, dest);
            else File.Move(src, dest);
        }
        catch (IOException ex) when (ex.HResult != HrSharingViolation)
        {
            if (Directory.Exists(src)) { CopyDirVerified(src, dest); Directory.Delete(src, recursive: true); }
            else { CopyFileVerified(src, dest); File.Delete(src); }
        }
    }

    public static void CopyFileVerified(string src, string dest)
    {
        var srcLen = new FileInfo(src).Length;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(src, dest, overwrite: false);
        if (new FileInfo(dest).Length != srcLen)
            throw new IOException($"Verification failed copying \"{src}\" -> \"{dest}\" (size mismatch); source left intact.");
    }

    public static void CopyDirVerified(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src))
            CopyFileVerified(f, Path.Combine(dest, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src))
            CopyDirVerified(d, Path.Combine(dest, Path.GetFileName(d)));
    }
}
```

- [ ] **Step 4: Delegate `Scanner.MoveAny`** (replace lines 300-312)

```csharp
private static void MoveAny(string src, string dest) => SafeMove.Move(src, dest);
```

(Leave `Scanner.CopyDir` and `Scanner.DeleteDir` in place — `EnableMod` still uses them.)

- [ ] **Step 5: Run SafeMove tests + the disable/enable suites; verify all pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SafeMoveTests|FullyQualifiedName~ScannerDisableTests"`
Expected: PASS — same-volume behavior is unchanged (still a rename), and disable/enable round-trips still work.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/SafeMove.cs src/ModManager.Core/Scanner.cs tests/ModManager.Tests/SafeMoveTests.cs
git commit -m "feat(core): verified cross-volume SafeMove; scope MoveAny to surface locked files"
```

> **Smoke (not unit-coverable):** the true cross-volume path (game on D:, `%APPDATA%` on C:) and the sharing-violation-surfaces path (game running) are recorded in `docs/smoke-tests/pending.md` in Phase 1.

---

## Task 4: `EnableMod` rollback + structured `EnableOutcome`

**Files:**
- Modify: `src/ModManager.Core/Scanner.cs:329` (add `EnableModWithOutcomeAsync`), `:462-519` (`EnableMod`)
- Test: `tests/ModManager.Tests/ScannerEnableRollbackTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class ScannerEnableRollbackTests
{
    private static (string modsDir, GameContext c) Fixture(params string[] exts)
    {
        if (exts.Length == 0) exts = new[] { "pak" };
        var root = TestSupport.TempDir("enable-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = exts, GroupingRule = "filename_no_ext",
        });
        return (modsDir, c);
    }

    [Fact]
    public async Task Enable_rolls_back_and_keeps_holding_when_a_live_copy_fails()
    {
        var (modsDir, c) = Fixture("pak", "ucas");
        File.WriteAllText(Path.Combine(modsDir, "mod.pak"), "PAK");
        File.WriteAllText(Path.Combine(modsDir, "mod.ucas"), "UCAS");
        await Scanner.DisableModAsync("mod", c);   // mod now sits in holding

        // Block one live destination: a non-empty directory where the file must land.
        Directory.CreateDirectory(Path.Combine(modsDir, "mod.ucas"));
        File.WriteAllText(Path.Combine(modsDir, "mod.ucas", "blocker"), "x");

        await Assert.ThrowsAnyAsync<Exception>(() => Scanner.EnableModAsync("mod", c));

        // Holding folder intact — nothing stranded.
        Assert.Equal("PAK", TestSupport.Read(Path.Combine(c.DisabledRoot, "mod", "mod.pak")));
        Assert.Equal("UCAS", TestSupport.Read(Path.Combine(c.DisabledRoot, "mod", "mod.ucas")));
        // The mod.pak live copy (if it was created before the failure) was rolled back.
        Assert.False(File.Exists(Path.Combine(modsDir, "mod.pak")));
    }

    [Fact]
    public async Task Enable_returns_enabled_outcome_on_success()
    {
        var (modsDir, c) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "DATA");
        await Scanner.DisableModAsync("cool", c);

        var outcome = await Scanner.EnableModWithOutcomeAsync("cool", c);

        Assert.True(outcome.Enabled);
        Assert.False(outcome.Skipped);
        Assert.Equal("DATA", TestSupport.Read(Path.Combine(modsDir, "cool.pak")));
    }

    [Fact]
    public async Task Enable_returns_skipped_outcome_when_no_disabled_metadata()
    {
        var (_, c) = Fixture();
        var outcome = await Scanner.EnableModWithOutcomeAsync("ghost", c);
        Assert.True(outcome.Skipped);
        Assert.False(outcome.Enabled);
        Assert.NotNull(outcome.Reason);
    }
}
```

- [ ] **Step 2: Run them; verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ScannerEnableRollbackTests"`
Expected: FAIL — `EnableModWithOutcomeAsync` / `EnableOutcome` don't exist; the rollback test strands `mod.pak`.

- [ ] **Step 3: Add the public outcome entry point** (next to `EnableModAsync` at `Scanner.cs:329`)

```csharp
public static Task<EnableOutcome> EnableModWithOutcomeAsync(string name, GameContext c)
    => Task.FromResult(EnableMod(name, c));
```

- [ ] **Step 4: Replace `EnableMod` (lines 462-519) with the rollback + outcome version**

```csharp
/// <summary>Result of an enable attempt — lets bulk / Safe-Clear callers see WHY a mod didn't
/// re-enable instead of getting a silent no-op.</summary>
public sealed record EnableOutcome(string Name, bool Enabled, bool Skipped, string? Reason);

private static EnableOutcome EnableMod(string name, GameContext c)
{
    var live = BuildModList(c).FirstOrDefault(x => x.Name == name);
    if (live is { ReadOnly: true })
        return new EnableOutcome(name, false, true, "managed by another tool");
    if (live?.Loader == "ue4ss")
    {
        try { Ue4ssManifest.SetEnabled(LocByName(live.Location, c).Abs, name, enabled: true); }
        catch (Exception e) { throw new InvalidOperationException($"Couldn't enable \"{name}\" ({e.Message})", e); }
        return new EnableOutcome(name, true, false, null);
    }
    if (live?.Loader == "bepinex")
    {
        try { BepInExPlugins.SetEnabled(LocByName(live.Location, c).Abs, name, enable: true); }
        catch (Exception e) { throw new InvalidOperationException($"Couldn't enable \"{name}\" ({e.Message})", e); }
        return new EnableOutcome(name, true, false, null);
    }

    var src = Path.Combine(c.DisabledRoot, name);
    DisabledMeta? meta;
    try { meta = JsonSerializer.Deserialize<DisabledMeta>(File.ReadAllText(Path.Combine(src, "meta.json")), Json); }
    catch { return new EnableOutcome(name, false, true, "no readable disabled metadata"); }
    if (meta is null) return new EnableOutcome(name, false, true, "empty disabled metadata");
    var loc = LocByName(meta.Location, c);
    if (ToolOwnership.Detect(loc.Abs) is not null)
        return new EnableOutcome(name, false, true, "target folder now owned by another tool");
    var hadOnServer = meta.HadOnServer ?? new Dictionary<string, bool>();
    Directory.CreateDirectory(loc.Abs);
    foreach (var mp in loc.Mirrors) Directory.CreateDirectory(mp);

    // Copy every entry into the live + mirror locations FIRST, tracking what we create, so a
    // mid-loop failure rolls back to the pre-enable state (holding folder untouched) instead of
    // stranding the mod half-enabled. Only after every copy succeeds do we clear the holding folder.
    var created = new List<string>();
    try
    {
        foreach (var entry in Directory.GetFileSystemEntries(src))
        {
            var entryName = Path.GetFileName(entry);
            if (entryName == "meta.json") continue;
            if (Directory.Exists(entry))
            {
                var liveDest = Path.Combine(loc.Abs, entryName);
                CopyDir(entry, liveDest); created.Add(liveDest);
                if (hadOnServer.TryGetValue(entryName, out var v) && v)
                    foreach (var mp in loc.Mirrors) { var md = Path.Combine(mp, entryName); CopyDir(entry, md); created.Add(md); }
            }
            else
            {
                var liveDest = Path.Combine(loc.Abs, entryName);
                File.Copy(entry, liveDest); created.Add(liveDest);   // overwrite:false — a collision is a real conflict
                if (!(hadOnServer.TryGetValue(entryName, out var v) && v == false))
                    foreach (var mp in loc.Mirrors) { var md = Path.Combine(mp, entryName); File.Copy(entry, md); created.Add(md); }
            }
        }
    }
    catch (Exception e)
    {
        foreach (var p in created)
        {
            try { if (Directory.Exists(p)) Directory.Delete(p, recursive: true); else if (File.Exists(p)) File.Delete(p); }
            catch { /* best effort */ }
        }
        throw new InvalidOperationException($"Couldn't enable \"{name}\" ({e.Message})", e);
    }

    // All live/mirror copies succeeded — now tear down the holding folder.
    foreach (var entry in Directory.GetFileSystemEntries(src))
    {
        if (Path.GetFileName(entry) == "meta.json") continue;
        try { if (Directory.Exists(entry)) DeleteDir(entry); else File.Delete(entry); } catch { /* best effort */ }
    }
    try { File.Delete(Path.Combine(src, "meta.json")); } catch { /* best effort */ }
    try { Directory.Delete(src); } catch { /* may be non-empty on partial */ }
    return new EnableOutcome(name, true, false, null);
}
```

- [ ] **Step 5: Run the new tests + the existing disable/enable round-trip; verify all pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ScannerEnableRollbackTests|FullyQualifiedName~ScannerDisableTests"`
Expected: PASS — `Disable_moves_to_holding_and_enable_restores` still green (the copy-then-clear order preserves the happy path).

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/Scanner.cs tests/ModManager.Tests/ScannerEnableRollbackTests.cs
git commit -m "fix(core): EnableMod rolls back a half-enable and reports structured skip outcomes"
```

---

## Task 5: `DisableEntry` Phase-2 — write meta.json before clearing mirrors

**Files:**
- Modify: `src/ModManager.Core/Scanner.cs:444-459` (Phase 2 of `DisableEntry`)
- Test: `tests/ModManager.Tests/ScannerEnableRollbackTests.cs` (add one method — fixture supports mirrors)

- [ ] **Step 1: Write the failing test** (add to `ScannerEnableRollbackTests`)

```csharp
[Fact]
public async Task Disable_then_enable_round_trips_a_mirrored_mod()
{
    var root = TestSupport.TempDir("mirror-");
    var gameRoot = Path.Combine(root, "game");
    var modsDir = Path.Combine(gameRoot, "mods");
    var mirrorDir = Path.Combine(gameRoot, "mirror");
    Directory.CreateDirectory(modsDir);
    Directory.CreateDirectory(mirrorDir);
    var c = Scanner.GameContext(new GameEntry
    {
        Id = "t", GameName = "T", GameRoot = gameRoot,
        ModLocations = new[] { new ModLocation("mods", "mods", "mods") { Mirrors = new[] { "mirror" } } },
        FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
    });
    File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "DATA");
    File.WriteAllText(Path.Combine(mirrorDir, "cool.pak"), "DATA");   // mirror present

    await Scanner.DisableModAsync("cool", c);
    Assert.True(File.Exists(Path.Combine(c.DisabledRoot, "cool", "meta.json")));  // record written
    Assert.False(File.Exists(Path.Combine(mirrorDir, "cool.pak")));               // mirror cleared

    await Scanner.EnableModAsync("cool", c);
    Assert.Equal("DATA", TestSupport.Read(Path.Combine(modsDir, "cool.pak")));
    Assert.Equal("DATA", TestSupport.Read(Path.Combine(mirrorDir, "cool.pak")));  // mirror restored via hadOnServer
}
```

- [ ] **Step 2: Run it; verify it passes or fails meaningfully**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~Disable_then_enable_round_trips_a_mirrored_mod"`
Expected: PASS against current code (the round-trip already works). This test is the **regression guard** that locks the mirror behavior before we reorder the writes — keep it and make sure Step 4 keeps it green.

- [ ] **Step 3: Reorder Phase 2 — snapshot-first** (replace lines 444-459)

```csharp
// Phase 2: primary files are safely held. Snapshot-first — write meta.json BEFORE clearing any
// mirror, with hadOnServer provisionally true for every file. A crash mid-clear then errs toward
// "had a mirror" (which enable safely recreates) rather than losing the record entirely. Rewrite
// with confirmed values once the clear completes. Mirrors IniEditService.SaveWithBackup ordering.
var metaPath = Path.Combine(dest, "meta.json");
var disabledAt = DateTime.UtcNow.ToString("o");
var hadOnServer = files.ToDictionary(f => f, _ => true);
void WriteMeta() => File.WriteAllText(metaPath, JsonSerializer.Serialize(
    new DisabledMeta { Location = m.Location, HadOnServer = hadOnServer, DisabledAt = disabledAt, IsFolder = m.IsFolder }, Json));

WriteMeta();   // provisional record exists before any mirror is touched

foreach (var f in files)
{
    var hadAny = false;
    foreach (var mp in loc.Mirrors)
    {
        var sPath = Path.Combine(mp, f);
        if (Directory.Exists(sPath)) { hadAny = true; DeleteDir(sPath); }
        else if (File.Exists(sPath)) { hadAny = true; File.Delete(sPath); }
    }
    hadOnServer[f] = hadAny;
}

WriteMeta();   // confirmed record
```

- [ ] **Step 4: Run the mirror round-trip + the full disable suite; verify all pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ScannerDisableTests|FullyQualifiedName~ScannerEnableRollbackTests"`
Expected: PASS — mirror round-trip still green, rollback tests still green.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Scanner.cs tests/ModManager.Tests/ScannerEnableRollbackTests.cs
git commit -m "fix(core): write disabled meta.json before clearing mirrors (crash-safe ordering)"
```

---

## Task 6: `SpaceCheck` free-space pre-flight

**Files:**
- Create: `src/ModManager.Core/SpaceCheck.cs`
- Test: `tests/ModManager.Tests/SpaceCheckTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class SpaceCheckTests
{
    [Fact]
    public void Evaluate_ok_when_available_exceeds_payload_plus_floor()
    {
        var r = SpaceCheck.Evaluate("C:\\", payloadBytes: 100, availableBytes: 5L << 30);
        Assert.True(r.Ok);
        Assert.Equal(100 + (1L << 30), r.RequiredBytes);   // 1 GiB floor dominates a tiny payload
    }

    [Fact]
    public void Evaluate_not_ok_when_short()
    {
        var r = SpaceCheck.Evaluate("C:\\", payloadBytes: 10L << 30, availableBytes: 1L << 30);
        Assert.False(r.Ok);
    }

    [Fact]
    public void RequiredWithHeadroom_uses_percentage_when_it_exceeds_floor()
    {
        var payload = 20L << 30;                                   // 20 GiB
        var req = SpaceCheck.RequiredWithHeadroom(payload);        // 10% = 2 GiB margin > 1 GiB floor
        Assert.Equal(payload + (long)(payload * 0.10), req);
    }
}
```

- [ ] **Step 2: Run it; verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SpaceCheckTests"`
Expected: FAIL — `SpaceCheck` does not exist.

- [ ] **Step 3: Create `SpaceCheck`**

```csharp
namespace ModManager.Core;

/// <summary>
/// Free-space pre-flight for archive / restore operations. The bytes overloads are fully testable
/// with no disk; <see cref="Require"/> reads DriveInfo (System.IO — Core-legal) in production.
/// </summary>
public static class SpaceCheck
{
    public sealed record Result(bool Ok, long RequiredBytes, long AvailableBytes, string VolumeRoot);

    /// <summary>Required headroom = payload + max(marginPct of payload, floorBytes).</summary>
    public static long RequiredWithHeadroom(long payloadBytes, double marginPct = 0.10, long floorBytes = 1L << 30)
        => payloadBytes + Math.Max((long)(payloadBytes * marginPct), floorBytes);

    /// <summary>Testable core: compare a required figure against a known available figure.</summary>
    public static Result Evaluate(string volumeRoot, long payloadBytes, long availableBytes,
                                  double marginPct = 0.10, long floorBytes = 1L << 30)
    {
        var required = RequiredWithHeadroom(payloadBytes, marginPct, floorBytes);
        return new Result(availableBytes >= required, required, availableBytes, volumeRoot);
    }

    /// <summary>Production entry: reads DriveInfo for the volume hosting <paramref name="anyPathOnVolume"/>.</summary>
    public static Result Require(string anyPathOnVolume, long payloadBytes,
                                 double marginPct = 0.10, long floorBytes = 1L << 30)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(anyPathOnVolume)) ?? anyPathOnVolume;
        var available = new DriveInfo(root).AvailableFreeSpace;
        return Evaluate(root, payloadBytes, available, marginPct, floorBytes);
    }
}
```

- [ ] **Step 4: Run the test; verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SpaceCheckTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/SpaceCheck.cs tests/ModManager.Tests/SpaceCheckTests.cs
git commit -m "feat(core): SpaceCheck free-space pre-flight helper"
```

---

## Task 7: `ModMeta.installedUtc` + `sourceConfidence` (Core enabler)

**Files:**
- Modify: `src/ModManager.Core/Mod.cs:50-67` (`ModMeta`)
- Create: `tests/ModManager.Tests/ModMetaRoundTripTests.cs`
- Modify: `.claude/rules/camelcase-json-on-disk.md` (governed-surfaces list)

- [ ] **Step 1: Write the failing round-trip test**

```csharp
using System.Text.Json;
using ModManager.Core;

namespace ModManager.Tests;

public class ModMetaRoundTripTests
{
    // Mirrors Scanner's on-disk options: camelCase write, case-insensitive read.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    [Fact]
    public void ModMeta_round_trips_installedUtc_and_sourceConfidence_as_camelCase()
    {
        var original = new ModMeta
        {
            Url = "https://www.nexusmods.com/eldenring/mods/510",
            InstalledUtc = new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc),
            SourceConfidence = "fingerprint",
            IsManual = true,
        };

        var json = JsonSerializer.Serialize(original, Json);
        Assert.Contains("\"installedUtc\"", json);       // camelCase key on disk
        Assert.DoesNotContain("\"InstalledUtc\"", json);
        Assert.Contains("\"sourceConfidence\"", json);
        Assert.DoesNotContain("\"SourceConfidence\"", json);

        var rt = JsonSerializer.Deserialize<ModMeta>(json, Json)!;
        Assert.Equal(original.InstalledUtc, rt.InstalledUtc);
        Assert.Equal("fingerprint", rt.SourceConfidence);
        Assert.True(rt.IsManual);
        Assert.Equal(original.Url, rt.Url);
    }
}
```

- [ ] **Step 2: Run it; verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ModMetaRoundTripTests"`
Expected: FAIL — `ModMeta` has no `InstalledUtc` / `SourceConfidence` (compile error).

- [ ] **Step 3: Add the fields to `ModMeta`** (after `IsManual` at `Mod.cs:66`)

```csharp
/// <summary>When this mod first landed (set by the App at intake). Drives the off-boarding sheet's
/// "installed on" line. Nullable: mods that predate this field have no recorded date.</summary>
public DateTime? InstalledUtc { get; set; }

/// <summary>How the source Url was derived: "manual" | "fingerprint" | "md5" | "nameSearch" | null.
/// Lets the off-boarding sheet hedge a low-confidence name-search match ("likely source:")
/// versus a high-confidence one ("source:").</summary>
public string? SourceConfidence { get; set; }
```

- [ ] **Step 4: Run the test; verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ModMetaRoundTripTests"`
Expected: PASS.

- [ ] **Step 5: Add `ModMeta` to the camelCase governed-surfaces list** (in `.claude/rules/camelcase-json-on-disk.md`, under "Surfaces this rule already governs")

```markdown
- `ModMeta` `installedUtc` + `sourceConfidence` (`src/ModManager.Core/Mod.cs`)
```

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/Mod.cs tests/ModManager.Tests/ModMetaRoundTripTests.cs .claude/rules/camelcase-json-on-disk.md
git commit -m "feat(core): ModMeta.installedUtc + sourceConfidence for off-boarding provenance"
```

---

## Final verification

- [ ] **Run the entire Core suite; confirm green**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS, including `CorePurityTests` (no new UI/WinRT references entered Core).

---

## Self-Review

**1. Spec coverage** — every Phase-0 spec item maps to a task:
- P0.1 FrameworkRegistry.Uninstall → Task 1
- P0.2 EnableMod rollback + structured skips → Task 4
- P0.3 MoveAny scope + verified cross-volume → Task 3 (`SafeMove`)
- P0.4 PathGate extraction → Task 2
- P0.5 SpaceCheck → Task 6
- P0.6 (Core half) ModMeta fields → Task 7; (App half) intake prompt + backfill → re-scoped to Phase 1 (stated in the scope note)
- P0.7 DisableEntry mirror snapshot-first → Task 5

**2. Placeholder scan** — no TBD/TODO; every code step shows complete code and exact commands.

**3. Type consistency** — `EnableOutcome(Name, Enabled, Skipped, Reason)` defined in Task 4 and consumed via `EnableModWithOutcomeAsync` in the same task; `SafeMove.{Move,CopyFileVerified,CopyDirVerified}` defined and called consistently; `PathGate.{SafeRelative,IsContained,IsForbidden}` names match across Tasks 2's create + refactor steps; `SpaceCheck.{RequiredWithHeadroom,Evaluate,Require}` consistent. `MoveAny` delegates to `SafeMove.Move` (Task 3) and is still the helper `DisableEntry`/`EnableMod` call.

**Note for the executor:** Tasks are largely independent, but run in order — Task 3 changes `MoveAny` (used by Task 4/5's enable/disable paths), so landing it first keeps each subsequent test run clean.
