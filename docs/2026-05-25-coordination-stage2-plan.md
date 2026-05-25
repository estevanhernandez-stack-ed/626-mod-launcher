# Coordination Stage 2 — UE4SS Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Show the TRUE enabled state of UE4SS mods (reading UE4SS's own manifests) and, where the folder is unowned, drive enable/disable through those manifests with NO file moves.

**Architecture:** A pure-core `Ue4ssManifest` reads/writes UE4SS's `mods.json` / `mods.txt` / per-folder `enabled.txt`. `BuildModList` uses it to set each UE4SS-folder mod's real `Enabled` (always — even in owned folders, since reading is non-mutating) and sets `Mod.Loader = "ue4ss"` only where the posture is Conductor (unowned). The enable/disable sinks route loader-driven mods to `Ue4ssManifest.SetEnabled` instead of moving files. The owned-folder invariant is preserved: writes happen only via `Loader`, which is set only for unowned folders.

**Tech Stack:** .NET 10, C#, xUnit. Branch: a fresh `feat/ue4ss-adapter` off `master` AFTER PR #14 merges (do NOT stack). If #14 is not yet merged, branch off `feat/multi-location-coordination` and note it, but prefer waiting for #14.

**UE4SS enable rules (re-UE4SS docs, verified):**
- `mods.txt` lines: `ModName : 1` (enabled) / `ModName : 0` (disabled); `;` comments; file order = load order; a `; Built-in keybinds, do not move up!` section sits at the bottom with `Keybinds : 1`.
- `mods.json`: array of `{ "mod_name": string, "mod_enabled": bool }`; array order = load order. The modern equivalent of mods.txt. Both can coexist (they do in the live install, identical content).
- `enabled.txt`: an **empty** file in a mod's folder. Its PRESENCE force-enables the mod **irrespective of mods.txt/json** (v1.3.6+).
- **Effective enabled = `(enabled.txt present) OR (manifest entry == true)`.** A folder absent from the manifest with no `enabled.txt` = not loaded (disabled).

**Test/build commands:**
- Tests: `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --nologo` (EXPLICIT project — bare `dotnet test` hangs on WinUI). Scope with `--filter "FullyQualifiedName~<Name>"`.
- App build: `dotnet build "C:\Users\estev\Projects\626-mod-launcher\src\ModManager.App\ModManager.App.csproj" -p:Platform=x64 --nologo` (kill `ModManager.App.exe` first).
- PowerShell; use `git -C "C:\Users\estev\Projects\626-mod-launcher" ...`. Commit trailer: `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>`.

Reference: [docs/2026-05-25-mod-tool-coordination-design.md](2026-05-25-mod-tool-coordination-design.md) §2 (loader adapters).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/ModManager.Core/Ue4ssManifest.cs` (create) | Read/write UE4SS mods.json + mods.txt + enabled.txt; effective-enabled logic |
| `src/ModManager.Core/Mod.cs` (modify) | add `string? Loader` |
| `src/ModManager.Core/Scanner.cs` (modify) | `BuildModList` sets real `Enabled` + `Loader` for UE4SS folders; enable/disable sinks route loader-driven mods |
| `tests/ModManager.Tests/Ue4ssManifestTests.cs` (create) | read precedence + write reconciliation |
| `tests/ModManager.Tests/Ue4ssScanTests.cs` (create) | BuildModList shows true state; toggle drives the manifest (unowned) and is blocked (owned) |

---

## Task 1: `Ue4ssManifest` read — effective enabled state

**Files:**
- Create: `tests/ModManager.Tests/Ue4ssManifestTests.cs`
- Create: `src/ModManager.Core/Ue4ssManifest.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/ModManager.Tests/Ue4ssManifestTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

// UE4SS effective-enabled: (enabled.txt present) OR (manifest entry == true). enabled.txt overrides
// a mods.txt/json ":0". Absent from manifest + no enabled.txt = disabled.
public class Ue4ssManifestTests
{
    private static string ModsDir()
    {
        var d = TestSupport.TempDir("ue4ss-");
        Directory.CreateDirectory(d);
        return d;
    }
    private static void Folder(string modsDir, string name) => Directory.CreateDirectory(Path.Combine(modsDir, name));
    private static void EnabledTxt(string modsDir, string name) => File.WriteAllText(Path.Combine(modsDir, name, "enabled.txt"), "");

    [Fact]
    public void IsUe4ssFolder_true_when_a_manifest_exists()
    {
        var d = ModsDir();
        Assert.False(Ue4ssManifest.IsUe4ssFolder(d));
        File.WriteAllText(Path.Combine(d, "mods.txt"), "");
        Assert.True(Ue4ssManifest.IsUe4ssFolder(d));
    }

    [Fact]
    public void IsEnabled_reads_mods_txt_flag()
    {
        var d = ModsDir(); Folder(d, "Foo"); Folder(d, "Bar");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "Foo : 1\nBar : 0\n");
        Assert.True(Ue4ssManifest.IsEnabled(d, "Foo"));
        Assert.False(Ue4ssManifest.IsEnabled(d, "Bar"));
    }

    [Fact]
    public void IsEnabled_reads_mods_json_flag()
    {
        var d = ModsDir(); Folder(d, "Foo");
        File.WriteAllText(Path.Combine(d, "mods.json"),
            "[{\"mod_name\":\"Foo\",\"mod_enabled\":true}]");
        Assert.True(Ue4ssManifest.IsEnabled(d, "Foo"));
    }

    [Fact]
    public void IsEnabled_enabled_txt_overrides_a_disabled_manifest_entry()
    {
        var d = ModsDir(); Folder(d, "Foo");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "Foo : 0\n");
        EnabledTxt(d, "Foo");
        Assert.True(Ue4ssManifest.IsEnabled(d, "Foo")); // enabled.txt wins
    }

    [Fact]
    public void IsEnabled_enabled_txt_enables_a_mod_absent_from_the_manifest()
    {
        var d = ModsDir(); Folder(d, "PetBoarPlus");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "Other : 1\n");
        EnabledTxt(d, "PetBoarPlus");
        Assert.True(Ue4ssManifest.IsEnabled(d, "PetBoarPlus"));
    }

    [Fact]
    public void IsEnabled_absent_from_manifest_and_no_enabled_txt_is_disabled()
    {
        var d = ModsDir(); Folder(d, "Ghost");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "Other : 1\n");
        Assert.False(Ue4ssManifest.IsEnabled(d, "Ghost"));
    }

    [Fact]
    public void IsEnabled_ignores_comments_and_blank_lines()
    {
        var d = ModsDir(); Folder(d, "Keybinds");
        File.WriteAllText(Path.Combine(d, "mods.txt"),
            "; a comment\n\n; Built-in keybinds, do not move up!\nKeybinds : 1\n");
        Assert.True(Ue4ssManifest.IsEnabled(d, "Keybinds"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "...ModManager.Tests.csproj" --filter "FullyQualifiedName~Ue4ssManifestTests" --nologo`
Expected: FAIL — `Ue4ssManifest` does not exist.

- [ ] **Step 3: Write the implementation (read half)**

Create `src/ModManager.Core/Ue4ssManifest.cs`:

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Reads and writes UE4SS's own mod manifests so the launcher shows the TRUE enabled state and,
/// where we own the folder, drives it without moving files. Pure System.IO.
///
/// Rules (re-UE4SS): mods.txt / mods.json list "Name : 1|0" with file/array order = load order;
/// an empty 'enabled.txt' in a mod folder force-enables it irrespective of the manifest.
/// Effective enabled = (enabled.txt present) OR (manifest entry == true).
/// </summary>
public static class Ue4ssManifest
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly Regex TxtLine = new(@"^\s*([^;:\s][^:]*?)\s*:\s*([01])\s*$", RegexOptions.Compiled);

    public static bool IsUe4ssFolder(string modsDir)
        => File.Exists(Path.Combine(modsDir, "mods.txt")) || File.Exists(Path.Combine(modsDir, "mods.json"));

    /// <summary>Manifest enable flags by mod name (mods.json preferred, else mods.txt). Order-preserving.</summary>
    private static List<(string Name, bool Enabled)> ReadManifest(string modsDir)
    {
        var json = Path.Combine(modsDir, "mods.json");
        if (File.Exists(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(json));
                var list = new List<(string, bool)>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var name = el.TryGetProperty("mod_name", out var n) ? n.GetString() : null;
                    var en = el.TryGetProperty("mod_enabled", out var e) && e.ValueKind == JsonValueKind.True;
                    if (!string.IsNullOrEmpty(name)) list.Add((name!, en));
                }
                return list;
            }
            catch { /* fall through to mods.txt */ }
        }
        var txt = Path.Combine(modsDir, "mods.txt");
        if (File.Exists(txt))
        {
            var list = new List<(string, bool)>();
            foreach (var raw in File.ReadAllLines(txt))
            {
                var m = TxtLine.Match(raw);
                if (m.Success) list.Add((m.Groups[1].Value.Trim(), m.Groups[2].Value == "1"));
            }
            return list;
        }
        return new List<(string, bool)>();
    }

    private static bool HasEnabledTxt(string modsDir, string name)
        => File.Exists(Path.Combine(modsDir, name, "enabled.txt"));

    /// <summary>Effective enabled for one mod folder: enabled.txt overrides; else the manifest flag; else false.</summary>
    public static bool IsEnabled(string modsDir, string name)
    {
        if (HasEnabledTxt(modsDir, name)) return true;
        foreach (var (n, en) in ReadManifest(modsDir))
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return en;
        return false;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "...ModManager.Tests.csproj" --filter "FullyQualifiedName~Ue4ssManifestTests" --nologo`
Expected: PASS (7 passed).

- [ ] **Step 5: Commit**

```bash
git -C "C:\Users\estev\Projects\626-mod-launcher" add src/ModManager.Core/Ue4ssManifest.cs tests/ModManager.Tests/Ue4ssManifestTests.cs
git -C "C:\Users\estev\Projects\626-mod-launcher" commit -m "feat: Ue4ssManifest read — effective enabled state (mods.json/txt + enabled.txt)

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 2: `Ue4ssManifest.SetEnabled` — reconciled write (no file moves)

**Files:**
- Modify: `tests/ModManager.Tests/Ue4ssManifestTests.cs` (add write tests)
- Modify: `src/ModManager.Core/Ue4ssManifest.cs` (add `SetEnabled`)

Reconciliation rule: to truly DISABLE, the manifest entry must be `0` AND any `enabled.txt` must be removed (else it overrides). To ENABLE, the manifest entry must be `1` (add if missing). Update every manifest that exists (both mods.json and mods.txt when both are present) to avoid desync. Writes are atomic (temp file + replace).

- [ ] **Step 1: Add failing write tests** (append to `Ue4ssManifestTests.cs`):

```csharp
    [Fact]
    public void SetEnabled_false_flips_mods_txt_and_removes_enabled_txt()
    {
        var d = ModsDir(); Folder(d, "Foo");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "Foo : 1\nKeybinds : 1\n");
        File.WriteAllText(Path.Combine(d, "Foo", "enabled.txt"), "");

        Ue4ssManifest.SetEnabled(d, "Foo", false);

        Assert.False(Ue4ssManifest.IsEnabled(d, "Foo"));
        Assert.False(File.Exists(Path.Combine(d, "Foo", "enabled.txt"))); // removed, else it overrides
        Assert.Contains("Foo : 0", File.ReadAllText(Path.Combine(d, "mods.txt")));
        Assert.Contains("Keybinds : 1", File.ReadAllText(Path.Combine(d, "mods.txt"))); // untouched
    }

    [Fact]
    public void SetEnabled_true_flips_mods_txt()
    {
        var d = ModsDir(); Folder(d, "Foo");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "Foo : 0\n");
        Ue4ssManifest.SetEnabled(d, "Foo", true);
        Assert.True(Ue4ssManifest.IsEnabled(d, "Foo"));
        Assert.Contains("Foo : 1", File.ReadAllText(Path.Combine(d, "mods.txt")));
    }

    [Fact]
    public void SetEnabled_updates_mods_json_when_present()
    {
        var d = ModsDir(); Folder(d, "Foo");
        File.WriteAllText(Path.Combine(d, "mods.json"),
            "[{\"mod_name\":\"Foo\",\"mod_enabled\":true}]");
        Ue4ssManifest.SetEnabled(d, "Foo", false);
        Assert.False(Ue4ssManifest.IsEnabled(d, "Foo"));
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(d, "mods.json")));
        var foo = doc.RootElement.EnumerateArray().First(e => e.GetProperty("mod_name").GetString() == "Foo");
        Assert.False(foo.GetProperty("mod_enabled").GetBoolean());
    }

    [Fact]
    public void SetEnabled_keeps_mods_txt_and_json_in_lockstep()
    {
        var d = ModsDir(); Folder(d, "Foo");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "Foo : 1\n");
        File.WriteAllText(Path.Combine(d, "mods.json"), "[{\"mod_name\":\"Foo\",\"mod_enabled\":true}]");
        Ue4ssManifest.SetEnabled(d, "Foo", false);
        Assert.Contains("Foo : 0", File.ReadAllText(Path.Combine(d, "mods.txt")));
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(d, "mods.json")));
        Assert.False(doc.RootElement.EnumerateArray().First().GetProperty("mod_enabled").GetBoolean());
    }

    [Fact]
    public void SetEnabled_true_adds_a_missing_manifest_entry()
    {
        var d = ModsDir(); Folder(d, "New");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "; Built-in keybinds, do not move up!\nKeybinds : 1\n");
        Ue4ssManifest.SetEnabled(d, "New", true);
        Assert.True(Ue4ssManifest.IsEnabled(d, "New"));
        var txt = File.ReadAllText(Path.Combine(d, "mods.txt"));
        Assert.Contains("New : 1", txt);
        Assert.Contains("Keybinds : 1", txt); // keybinds section preserved
    }
```

- [ ] **Step 2: Run them red**

Run: `dotnet test "...ModManager.Tests.csproj" --filter "FullyQualifiedName~Ue4ssManifestTests" --nologo`
Expected: FAIL — `SetEnabled` does not exist.

- [ ] **Step 3: Implement `SetEnabled`** (add to `Ue4ssManifest`):

```csharp
    /// <summary>
    /// Enable/disable a UE4SS mod WITHOUT moving files: set the flag in every present manifest
    /// (add the entry if missing), and on disable remove any enabled.txt (else it overrides the 0).
    /// Atomic writes. No-op-safe if the folder has no manifest.
    /// </summary>
    public static void SetEnabled(string modsDir, string name, bool enabled)
    {
        var txt = Path.Combine(modsDir, "mods.txt");
        if (File.Exists(txt)) WriteAtomic(txt, SetInModsTxt(File.ReadAllLines(txt), name, enabled));

        var json = Path.Combine(modsDir, "mods.json");
        if (File.Exists(json)) WriteAtomic(json, SetInModsJson(File.ReadAllText(json), name, enabled));

        // enabled.txt overrides the manifest — it must go when disabling.
        if (!enabled)
        {
            var et = Path.Combine(modsDir, name, "enabled.txt");
            if (File.Exists(et)) File.Delete(et);
        }
    }

    private static string SetInModsTxt(string[] lines, string name, bool enabled)
    {
        var flag = enabled ? "1" : "0";
        var outLines = new List<string>();
        var found = false;
        var insertAt = -1; // before the keybinds comment, if any
        for (var i = 0; i < lines.Length; i++)
        {
            var m = TxtLine.Match(lines[i]);
            if (m.Success && string.Equals(m.Groups[1].Value.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                outLines.Add($"{name} : {flag}");
                found = true;
            }
            else
            {
                if (insertAt < 0 && lines[i].TrimStart().StartsWith(";") &&
                    lines[i].Contains("keybind", StringComparison.OrdinalIgnoreCase))
                    insertAt = outLines.Count;
                outLines.Add(lines[i]);
            }
        }
        if (!found)
        {
            if (insertAt >= 0) outLines.Insert(insertAt, $"{name} : {flag}");
            else outLines.Add($"{name} : {flag}");
        }
        return string.Join("\r\n", outLines) + "\r\n";
    }

    private static string SetInModsJson(string content, string name, bool enabled)
    {
        var list = new List<Dictionary<string, object>>();
        try
        {
            using var doc = JsonDocument.Parse(content);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var n = el.TryGetProperty("mod_name", out var nn) ? nn.GetString() ?? "" : "";
                var en = el.TryGetProperty("mod_enabled", out var ee) && ee.ValueKind == JsonValueKind.True;
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) en = enabled;
                list.Add(new Dictionary<string, object> { ["mod_name"] = n, ["mod_enabled"] = en });
            }
        }
        catch { list.Clear(); }
        if (!list.Any(d => string.Equals((string)d["mod_name"], name, StringComparison.OrdinalIgnoreCase)))
            list.Add(new Dictionary<string, object> { ["mod_name"] = name, ["mod_enabled"] = enabled });
        return JsonSerializer.Serialize(list, Json);
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
```

- [ ] **Step 4: Run them green** (filter), then full suite. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C "C:\Users\estev\Projects\626-mod-launcher" add src/ModManager.Core/Ue4ssManifest.cs tests/ModManager.Tests/Ue4ssManifestTests.cs
git -C "C:\Users\estev\Projects\626-mod-launcher" commit -m "feat: Ue4ssManifest.SetEnabled — reconciled manifest write, no file moves

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 3: `Mod.Loader` + wire true state into `BuildModList`

**Files:**
- Modify: `src/ModManager.Core/Mod.cs` (add `Loader`)
- Modify: `src/ModManager.Core/Scanner.cs` (`BuildModList` folder branch)
- Create: `tests/ModManager.Tests/Ue4ssScanTests.cs`

- [ ] **Step 1: Write failing scan tests**

Create `tests/ModManager.Tests/Ue4ssScanTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

// BuildModList shows the TRUE UE4SS enable state (even in an owned folder) and tags unowned
// UE4SS mods with Loader="ue4ss" (Conductor) so the toggle drives the manifest, not a file move.
public class Ue4ssScanTests
{
    private static GameContext Ctx(string root, bool owned)
    {
        var mods = Path.Combine(root, "ue4ss", "Mods");
        Directory.CreateDirectory(Path.Combine(mods, "On"));
        Directory.CreateDirectory(Path.Combine(mods, "Off"));
        File.WriteAllText(Path.Combine(mods, "mods.txt"), "On : 1\nOff : 0\n");
        if (owned) File.WriteAllText(Path.Combine(mods, "vortex.deployment.x.json"), "{}");
        return Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            FileExtensions = new[] { "pak" }, GroupingRule = "strip_underscore_p_suffix",
            ModLocations = new[] { new ModLocation("ue4ss", "UE4SS", "ue4ss/Mods") { Form = "folders" } },
        });
    }

    [Fact]
    public async Task BuildModList_reflects_true_enabled_state_from_the_manifest()
    {
        var mods = await Scanner.BuildModListAsync(Ctx(TestSupport.TempDir("ue4ss-on-"), owned: false));
        Assert.True(mods.First(m => m.Name == "On").Enabled);
        Assert.False(mods.First(m => m.Name == "Off").Enabled);
    }

    [Fact]
    public async Task Unowned_ue4ss_mods_are_tagged_loader_ue4ss()
    {
        var mods = await Scanner.BuildModListAsync(Ctx(TestSupport.TempDir("ue4ss-un-"), owned: false));
        Assert.Equal("ue4ss", mods.First(m => m.Name == "On").Loader);
    }

    [Fact]
    public async Task Owned_ue4ss_folder_still_reads_true_state_but_is_not_loader_driven()
    {
        var mods = await Scanner.BuildModListAsync(Ctx(TestSupport.TempDir("ue4ss-own-"), owned: true));
        var off = mods.First(m => m.Name == "Off");
        Assert.False(off.Enabled);   // true state still read (non-mutating)
        Assert.True(off.ReadOnly);   // owned -> read-only
        Assert.Null(off.Loader);     // not loader-driven (we won't write an owned folder)
    }
}
```

- [ ] **Step 2: Run them red.** Expected: FAIL — `Mod.Loader` missing; `Enabled` hardcoded true.

- [ ] **Step 3a: Add `Loader` to `Mod`** — in `src/ModManager.Core/Mod.cs`, after `public bool ReadOnly { get; set; }`:

```csharp
    // Set to a loader id ("ue4ss") when this mod's enable state is driven through a loader manifest
    // (Conductor posture) rather than by moving files. Null = file-move model.
    public string? Loader { get; set; }
```

- [ ] **Step 3b: Wire into `BuildModList`** — in `src/ModManager.Core/Scanner.cs`, the folder-form branch currently reads:

```csharp
            if (loc.Form == "folders")
            {
                foreach (var f in ListSubfolders(loc.Abs))
                {
                    if (outMap.ContainsKey(f)) continue;
                    outMap[f] = new Mod
                    {
                        Name = f, Location = loc.Name, Enabled = true, Files = new List<string> { f },
                        OnServer = false, IsFolder = true, Managed = managedLabel, ReadOnly = readOnly,
                    };
                }
            }
```

Replace it with (compute UE4SS-ness once per folder location, set real `Enabled` + `Loader`):

```csharp
            if (loc.Form == "folders")
            {
                var isUe4ss = Ue4ssManifest.IsUe4ssFolder(loc.Abs);
                foreach (var f in ListSubfolders(loc.Abs))
                {
                    if (outMap.ContainsKey(f)) continue;
                    // Reading the manifest is non-mutating, so we surface true state even in an
                    // owned folder. Loader-drive (writing) is granted only where Conductor (unowned).
                    var enabled = isUe4ss ? Ue4ssManifest.IsEnabled(loc.Abs, f) : true;
                    outMap[f] = new Mod
                    {
                        Name = f, Location = loc.Name, Enabled = enabled, Files = new List<string> { f },
                        OnServer = false, IsFolder = true, Managed = managedLabel, ReadOnly = readOnly,
                        Loader = (isUe4ss && posture == Posture.Conductor) ? "ue4ss" : null,
                    };
                }
            }
```

NOTE: `posture` is already computed at the top of the loop (`Coordination.PostureFor(owner, loc.Managed, loaderCanConduct: false)`). For a UE4SS folder we want Conductor when unowned — change that line at the top of the loop to:

```csharp
            var owner = ToolOwnership.Detect(loc.Abs);
            var loaderCanConduct = loc.Form == "folders" && Ue4ssManifest.IsUe4ssFolder(loc.Abs);
            var posture = Coordination.PostureFor(owner, loc.Managed, loaderCanConduct);
            var readOnly = posture == Posture.Coexist;
            var managedLabel = owner?.ToString().ToLowerInvariant() ?? (readOnly ? loc.Managed : null);
```

(Then the folder branch can reuse `Ue4ssManifest.IsUe4ssFolder(loc.Abs)` or capture it in a local — capture it once: `var isUe4ss = loaderCanConduct || (loc.Form == "folders" && Ue4ssManifest.IsUe4ssFolder(loc.Abs));` — simplest is to compute `var isUe4ss = loc.Form == "folders" && Ue4ssManifest.IsUe4ssFolder(loc.Abs);` at the top and pass it to both `loaderCanConduct` and the folder branch.)

Final shape of the top-of-loop block:

```csharp
            var owner = ToolOwnership.Detect(loc.Abs);
            var isUe4ss = loc.Form == "folders" && Ue4ssManifest.IsUe4ssFolder(loc.Abs);
            var posture = Coordination.PostureFor(owner, loc.Managed, loaderCanConduct: isUe4ss);
            var readOnly = posture == Posture.Coexist;
            var managedLabel = owner?.ToString().ToLowerInvariant() ?? (readOnly ? loc.Managed : null);
```

- [ ] **Step 4: Run the full suite.** Expected: the 3 new scan tests pass; existing `ScannerLocationFormTests`/`CoordinationScanTests` still pass (their folder fixtures have NO mods.txt/json, so `isUe4ss` is false → `Enabled` stays true, `Loader` null — unchanged).

- [ ] **Step 5: Commit**

```bash
git -C "C:\Users\estev\Projects\626-mod-launcher" add src/ModManager.Core/Mod.cs src/ModManager.Core/Scanner.cs tests/ModManager.Tests/Ue4ssScanTests.cs
git -C "C:\Users\estev\Projects\626-mod-launcher" commit -m "feat: surface true UE4SS enable state in the scan + tag Conductor mods (Loader)

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 4: Route enable/disable through the manifest for loader-driven mods

**Files:**
- Modify: `src/ModManager.Core/Scanner.cs` (`EnableMod` and the disable sink `DisableEntry`)
- Modify: `tests/ModManager.Tests/Ue4ssScanTests.cs` (add drive tests)

The enable/disable sinks must: for a loader-driven mod (`m.Loader == "ue4ss"`), flip the manifest via `Ue4ssManifest.SetEnabled` and return — NO file move. This sits AFTER the owned-folder `ReadOnly` guard (owned mods never reach here), so it only fires for Conductor mods.

- [ ] **Step 1: Add failing drive tests** (append to `Ue4ssScanTests.cs`):

```csharp
    [Fact]
    public async Task Disabling_an_unowned_ue4ss_mod_flips_the_manifest_and_moves_no_files()
    {
        var root = TestSupport.TempDir("ue4ss-dis-");
        var c = Ctx(root, owned: false);
        var modDir = Path.Combine(root, "ue4ss", "Mods", "On");

        await Scanner.DisableModAsync("On", c);

        Assert.True(Directory.Exists(modDir));                       // folder NOT moved
        Assert.False(Ue4ssManifest.IsEnabled(Path.Combine(root, "ue4ss", "Mods"), "On")); // manifest flipped
        Assert.False(Directory.Exists(Path.Combine(c.DisabledRoot, "On"))); // nothing in holding
    }

    [Fact]
    public async Task Enabling_an_unowned_ue4ss_mod_flips_the_manifest()
    {
        var root = TestSupport.TempDir("ue4ss-en-");
        var c = Ctx(root, owned: false);
        await Scanner.EnableModAsync("Off", c);
        Assert.True(Ue4ssManifest.IsEnabled(Path.Combine(root, "ue4ss", "Mods"), "Off"));
    }
```

- [ ] **Step 2: Run them red.** Expected: FAIL — `DisableModAsync` tries to MOVE the folder (it exists in disabled root / no longer in place), manifest unchanged.

- [ ] **Step 3: Route in the sinks.** In `src/ModManager.Core/Scanner.cs`:

In `DisableEntry(Mod m, GameContext c)`, immediately AFTER the existing `if (m.ReadOnly) return;` guard, add:

```csharp
        if (m.Loader == "ue4ss")
        {
            Ue4ssManifest.SetEnabled(LocByName(m.Location, c).Abs, m.Name, enabled: false);
            return;
        }
```

In `EnableMod(string name, GameContext c)`, after it resolves the mod's location and AFTER the existing owned-folder guard (`if (ToolOwnership.Detect(loc.Abs) is not null) return;`), determine whether the target is a loader folder and route. Insert (adjust to the local variable names already present — `loc` is the resolved `ModLocationCtx`):

```csharp
        if (Ue4ssManifest.IsUe4ssFolder(loc.Abs))
        {
            Ue4ssManifest.SetEnabled(loc.Abs, name, enabled: true);
            return;
        }
```

If `EnableMod` resolves location differently (e.g., from the disabled meta.json), instead look up the live mod: `var m = BuildModList(c).FirstOrDefault(x => x.Name == name); if (m?.Loader == "ue4ss") { Ue4ssManifest.SetEnabled(LocByName(m.Location, c).Abs, name, true); return; }` — read `EnableMod` first and choose the form that matches how it already finds the target. Prefer keying on `m.Loader` for symmetry with `DisableEntry`.

- [ ] **Step 4: Run full suite green.** Confirm existing enable/disable tests (file-move mods, `ScannerCoreTests`) still pass — those mods have `Loader == null`, so the move path is unchanged.

- [ ] **Step 5: Commit**

```bash
git -C "C:\Users\estev\Projects\626-mod-launcher" add src/ModManager.Core/Scanner.cs tests/ModManager.Tests/Ue4ssScanTests.cs
git -C "C:\Users\estev\Projects\626-mod-launcher" commit -m "feat: drive UE4SS enable/disable via the manifest (Conductor), no file moves

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 5: App build + manual smoke

- [ ] **Step 1: App builds** — `Stop-Process -Name ModManager.App -Force -ErrorAction SilentlyContinue; dotnet build "...ModManager.App.csproj" -p:Platform=x64 --nologo`. Expected `0 Error(s)`. (No VM change is expected — the existing toggle calls `EnableModAsync`/`DisableModAsync`, which now route internally. If `canToggle` needs to allow loader-driven mods, confirm: a Conductor UE4SS mod has `ReadOnly == false`, so `canToggle: !ReadOnly` is already true — no change.)

- [ ] **Step 2: Full suite** — `dotnet test "...ModManager.Tests.csproj" --nologo`. All green except the 2 known 7z/rar SKIPs.

- [ ] **Step 3: Manual smoke (live Windrose)** — launch the app. Windrose's `ue4ss` folder is Vortex-owned (Coexist), so: UE4SS mods now show their TRUE state (SplitScreenMod/ConsoleEnabler etc. as OFF, PetBoarPlus/ExpandedPickupRadius/Keybinds as ON), still read-only with the VORTEX badge, toggles disabled. To see Conductor drive, test on a UE4SS folder WITHOUT a Vortex marker.

- [ ] **Step 4: Push** — `git -C "C:\Users\estev\Projects\626-mod-launcher" push -u origin feat/ue4ss-adapter`. Then open PR #15 against `master`.

---

## Deferred to a follow-up (NOT this plan)
- **Load order** (`SetOrder` via mods.json/txt array order) — UE4SS Lua mods rarely conflict on order; the live pain is enable-state. Add when needed.
- **`enabled.txt`-only enable** as an alternative to manifest entries (we standardize on the manifest for order-stability).

## Self-Review

**Spec coverage (§2 UE4SS adapter):** read true state (mods.json/txt + enabled.txt union) → Task 1; reconciled write no-moves → Task 2; surface in scan + Conductor tag → Task 3; drive enable/disable via manifest only where unowned → Task 4. Load order explicitly deferred with rationale. ✅

**Placeholder scan:** none — full code in every code step. The one judgment point (how `EnableMod` resolves its target) is called out with both acceptable forms and a rule to pick (key on `m.Loader`). ✅

**Type consistency:** `Ue4ssManifest.IsUe4ssFolder/IsEnabled/SetEnabled(string modsDir, string name, bool enabled)`, `Mod.Loader` (string?, value `"ue4ss"`), `Coordination.PostureFor(owner, declaredManaged, loaderCanConduct)` (existing signature), `Posture.Conductor`. The owned-folder invariant holds: `SetEnabled` is reached only via `m.Loader == "ue4ss"`, set only when `posture == Conductor`, which requires `owner == null` (unowned). Owned folders → `ReadOnly`, `Loader == null`, blocked by the existing `ReadOnly` guards. ✅
