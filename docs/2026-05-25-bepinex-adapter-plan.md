# Stage 4 — BepInEx Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]`.

**Goal:** Make BepInEx (Unity) games first-class in the coordination model: read each plugin's true enabled state and drive enable/disable through BepInEx's native mechanism (rename `Foo.dll` ↔ `Foo.dll.disabled`) — no file moves — with the same ownership/posture rules as UE4SS.

**Architecture:** A pure-core `BepInExPlugins` scans `BepInEx/plugins` for both enabled (`*.dll`) and disabled (`*.dll.disabled`) plugins and flips state by rename. `BuildModList` uses it for a BepInEx location (engine == "bepinex"), tagging mods `Loader = "bepinex"`; the enable/disable sinks route `Loader == "bepinex"` to `BepInExPlugins.SetEnabled`. Mirrors the UE4SS adapter shape exactly.

**Tech Stack:** .NET 10, C#, xUnit. Branch: `feat/bepinex-adapter` off `master` (already checked out). Core-only — do NOT touch the App ViewModels/XAML (keeps it conflict-free with the in-flight #21).

**Key fact:** BepInEx loads every `*.dll` in `BepInEx/plugins` (recursively). Disabling = rename to `*.dll.disabled` (BepInEx ignores non-`.dll`). So a disabled plugin's file does NOT match the `.dll` extension and must be scanned specially. Reversible rename, never a delete/move-to-holding.

**Test/build:** `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --nologo` (EXPLICIT project). PowerShell; `git -C "C:\Users\estev\Projects\626-mod-launcher" ...`. Trailer: `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>`.

Reference the UE4SS adapter for the established pattern: `Ue4ssManifest` (read/write), the `Loader` field on `Mod`, the `m.Loader == "ue4ss"` routes in `Scanner.DisableEntry`/`EnableMod`/`SetLoaderModEnabledAsync`, and `Ue4ssScanTests` for the scan-test fixture style.

---

## Task 1: `BepInExPlugins` — scan + rename-based enable/disable

**Files:** Create `tests/ModManager.Tests/BepInExPluginsTests.cs`, `src/ModManager.Core/BepInExPlugins.cs`.

- [ ] **Step 1: Failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

// BepInEx: a plugin is enabled when its file ends in .dll, disabled when renamed to .dll.disabled.
// Scan surfaces both; SetEnabled flips by rename (reversible, no move/delete).
public class BepInExPluginsTests
{
    private static string Dir()
    {
        var d = TestSupport.TempDir("bep-");
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Scan_lists_enabled_and_disabled_plugins_keyed_by_base_name()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "CoolMod.dll"), "x");
        File.WriteAllText(Path.Combine(d, "OffMod.dll.disabled"), "x");
        var scan = BepInExPlugins.Scan(d).OrderBy(p => p.Name).ToList();
        Assert.Equal(2, scan.Count);
        Assert.Equal(("CoolMod", "CoolMod.dll", true), (scan[0].Name, scan[0].File, scan[0].Enabled));
        Assert.Equal(("OffMod", "OffMod.dll.disabled", false), (scan[1].Name, scan[1].File, scan[1].Enabled));
    }

    [Fact]
    public void Scan_prefers_enabled_when_both_forms_exist()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "Dup.dll"), "x");
        File.WriteAllText(Path.Combine(d, "Dup.dll.disabled"), "x");
        var p = Assert.Single(BepInExPlugins.Scan(d));
        Assert.True(p.Enabled);
    }

    [Fact]
    public void Scan_ignores_non_plugin_files() // e.g. .json/.txt sidecars
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "Note.txt"), "x");
        Assert.Empty(BepInExPlugins.Scan(d));
    }

    [Fact]
    public void IsEnabled_reflects_the_present_form()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "A.dll"), "x");
        File.WriteAllText(Path.Combine(d, "B.dll.disabled"), "x");
        Assert.True(BepInExPlugins.IsEnabled(d, "A"));
        Assert.False(BepInExPlugins.IsEnabled(d, "B"));
        Assert.False(BepInExPlugins.IsEnabled(d, "Ghost"));
    }

    [Fact]
    public void SetEnabled_false_renames_dll_to_disabled_no_data_lost()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "A.dll"), "PAYLOAD");
        BepInExPlugins.SetEnabled(d, "A", false);
        Assert.False(File.Exists(Path.Combine(d, "A.dll")));
        Assert.True(File.Exists(Path.Combine(d, "A.dll.disabled")));
        Assert.Equal("PAYLOAD", File.ReadAllText(Path.Combine(d, "A.dll.disabled"))); // content preserved
    }

    [Fact]
    public void SetEnabled_true_renames_disabled_back_to_dll()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "A.dll.disabled"), "x");
        BepInExPlugins.SetEnabled(d, "A", true);
        Assert.True(File.Exists(Path.Combine(d, "A.dll")));
        Assert.False(File.Exists(Path.Combine(d, "A.dll.disabled")));
    }

    [Fact]
    public void SetEnabled_is_idempotent_when_already_in_target_state()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "A.dll"), "x");
        BepInExPlugins.SetEnabled(d, "A", true); // already enabled — no throw, no change
        Assert.True(File.Exists(Path.Combine(d, "A.dll")));
    }
}
```

- [ ] **Step 2: Run red.**

- [ ] **Step 3: Implement**

Create `src/ModManager.Core/BepInExPlugins.cs`:

```csharp
namespace ModManager.Core;

/// <summary>
/// BepInEx (Unity) plugin loader support. BepInEx loads every *.dll under BepInEx/plugins; a plugin
/// is disabled by renaming it to *.dll.disabled (BepInEx ignores non-.dll). We read true state and
/// flip it by rename — reversible, never a move-to-holding or delete. Pure System.IO.
/// </summary>
public static class BepInExPlugins
{
    private const string Dll = ".dll";
    private const string Disabled = ".dll.disabled";

    /// <summary>Enabled (*.dll) + disabled (*.dll.disabled) plugins, keyed by base name; enabled wins on dup.</summary>
    public static IReadOnlyList<(string Name, string File, bool Enabled)> Scan(string pluginsDir)
    {
        var byName = new Dictionary<string, (string File, bool Enabled)>(StringComparer.OrdinalIgnoreCase);
        string[] files;
        try { files = Directory.GetFiles(pluginsDir); } catch { return Array.Empty<(string, string, bool)>(); }
        foreach (var path in files)
        {
            var file = Path.GetFileName(path);
            string name; bool enabled;
            if (file.EndsWith(Disabled, StringComparison.OrdinalIgnoreCase)) { name = file[..^Disabled.Length]; enabled = false; }
            else if (file.EndsWith(Dll, StringComparison.OrdinalIgnoreCase)) { name = file[..^Dll.Length]; enabled = true; }
            else continue;
            if (name.Length == 0) continue;
            // Enabled form wins if both exist.
            if (!byName.TryGetValue(name, out var cur) || (enabled && !cur.Enabled))
                byName[name] = (file, enabled);
        }
        return byName.Select(kv => (kv.Key, kv.Value.File, kv.Value.Enabled)).ToList();
    }

    public static bool IsEnabled(string pluginsDir, string name)
        => File.Exists(Path.Combine(pluginsDir, name + Dll));

    /// <summary>Flip a plugin's enabled state by rename. Idempotent; no-op if the source isn't there.</summary>
    public static void SetEnabled(string pluginsDir, string name, bool enable)
    {
        var dll = Path.Combine(pluginsDir, name + Dll);
        var off = Path.Combine(pluginsDir, name + Disabled);
        if (enable)
        {
            if (!File.Exists(dll) && File.Exists(off)) File.Move(off, dll);
        }
        else
        {
            if (File.Exists(dll)) File.Move(dll, off, overwrite: true);
        }
    }
}
```

- [ ] **Step 4: Run green** (filter), then full suite.
- [ ] **Step 5: Commit** `feat: BepInExPlugins — scan + rename-based enable/disable for Unity plugins`

---

## Task 2: Wire BepInEx into the scan + enable/disable sinks

**Files:** Modify `src/ModManager.Core/Scanner.cs`; create `tests/ModManager.Tests/BepInExScanTests.cs`.

- [ ] **Step 1: Failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class BepInExScanTests
{
    private static GameContext Ctx(string root, bool owned)
    {
        var plugins = Path.Combine(root, "BepInEx", "plugins");
        Directory.CreateDirectory(plugins);
        File.WriteAllText(Path.Combine(plugins, "On.dll"), "x");
        File.WriteAllText(Path.Combine(plugins, "Off.dll.disabled"), "x");
        if (owned) File.WriteAllText(Path.Combine(plugins, "vortex.deployment.x.json"), "{}");
        return Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root, Engine = "bepinex",
            FileExtensions = new[] { "dll" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "plugins", "BepInEx/plugins") },
        });
    }

    [Fact]
    public async Task BuildModList_shows_true_state_and_tags_bepinex_loader()
    {
        var mods = await Scanner.BuildModListAsync(Ctx(TestSupport.TempDir("bepscan-"), owned: false));
        Assert.True(mods.First(m => m.Name == "On").Enabled);
        Assert.False(mods.First(m => m.Name == "Off").Enabled);   // disabled plugin still listed
        Assert.Equal("bepinex", mods.First(m => m.Name == "On").Loader);
    }

    [Fact]
    public async Task Disabling_a_bepinex_plugin_renames_and_moves_no_files()
    {
        var root = TestSupport.TempDir("bepdis-");
        var c = Ctx(root, owned: false);
        var plugins = Path.Combine(root, "BepInEx", "plugins");

        await Scanner.DisableModAsync("On", c);

        Assert.False(File.Exists(Path.Combine(plugins, "On.dll")));
        Assert.True(File.Exists(Path.Combine(plugins, "On.dll.disabled")));   // renamed, not moved to holding
        Assert.False(Directory.Exists(Path.Combine(c.DisabledRoot, "On")));
    }

    [Fact]
    public async Task Enabling_a_bepinex_plugin_renames_back()
    {
        var root = TestSupport.TempDir("bepen-");
        var c = Ctx(root, owned: false);
        await Scanner.EnableModAsync("Off", c);
        Assert.True(File.Exists(Path.Combine(root, "BepInEx", "plugins", "Off.dll")));
    }

    [Fact]
    public async Task Owned_bepinex_folder_reads_state_but_is_read_only()
    {
        var mods = await Scanner.BuildModListAsync(Ctx(TestSupport.TempDir("bepown-"), owned: true));
        var on = mods.First(m => m.Name == "On");
        Assert.True(on.ReadOnly);                 // Vortex/MO2 owns it -> coexist
        Assert.True(on.Enabled);                  // true state still read (non-mutating)
    }
}
```

- [ ] **Step 2: Run red.**

- [ ] **Step 3: Wire into `BuildModList`.** At the top of the per-location loop (where `owner`, `isUe4ss`, `posture` are computed), add a BepInEx flag and include it in `loaderCanConduct`:

```csharp
            var owner = ToolOwnership.Detect(loc.Abs);
            var isUe4ss = loc.Form == "folders" && Ue4ssManifest.IsUe4ssFolder(loc.Abs);
            var isBepInEx = loc.Form != "folders" && string.Equals(c.Game.Engine, "bepinex", StringComparison.OrdinalIgnoreCase);
            var posture = Coordination.PostureFor(owner, loc.Managed, loaderCanConduct: isUe4ss || isBepInEx);
            var readOnly = posture == Posture.Coexist;
            var managedLabel = owner?.ToString().ToLowerInvariant() ?? (readOnly ? loc.Managed : null);
```

Then in the file-form branch (the `else` that calls `ListPakFiles`), handle BepInEx first:

```csharp
            else if (isBepInEx)
            {
                foreach (var (name, file, enabled) in BepInExPlugins.Scan(loc.Abs))
                {
                    if (outMap.ContainsKey(name)) continue;
                    outMap[name] = new Mod
                    {
                        Name = name, Location = loc.Name, Enabled = enabled, IsFolder = false,
                        Files = new List<string> { file }, OnServer = false,
                        Managed = managedLabel, ReadOnly = readOnly, Loader = "bepinex",
                    };
                }
            }
            else
            {
                foreach (var f in ListPakFiles(loc.Abs, c)) { /* existing pak logic unchanged */ }
            }
```

Also exclude loader-driven mods from the mirror/OnServer pass (they have no pak mirrors). Change the guard in that loop from `if (m.IsFolder) continue;` to:

```csharp
            if (m.IsFolder || m.Loader is not null) continue;
```

- [ ] **Step 4: Route the enable/disable sinks.** In `DisableEntry`, after the existing `if (m.Loader == "ue4ss") { ... return; }` block, add:

```csharp
        if (m.Loader == "bepinex")
        {
            try { BepInExPlugins.SetEnabled(LocByName(m.Location, c).Abs, m.Name, enabled: false); }
            catch (Exception e) { throw new InvalidOperationException($"Couldn't disable \"{m.Name}\" ({e.Message})", e); }
            return;
        }
```

In `EnableMod`, after the existing `if (live?.Loader == "ue4ss") { ... return; }` block (and AFTER the `if (live is { ReadOnly: true }) return;` guard), add the symmetric:

```csharp
        if (live?.Loader == "bepinex")
        {
            try { BepInExPlugins.SetEnabled(LocByName(live.Location, c).Abs, name, enabled: true); }
            catch (Exception e) { throw new InvalidOperationException($"Couldn't enable \"{name}\" ({e.Message})", e); }
            return;
        }
```

In `SetLoaderModEnabledAsync`, generalize the loader check so the explicit per-row path also drives BepInEx. Where it currently does `if (m?.Loader == "ue4ss") { Ue4ssManifest.SetEnabled(...); ... }`, add a sibling branch:

```csharp
        if (m?.Loader == "bepinex")
        {
            try { BepInExPlugins.SetEnabled(LocByName(m.Location, c).Abs, name, enable); }
            catch (Exception e) { throw new InvalidOperationException($"Couldn't {(enable ? "enable" : "disable")} \"{name}\" ({e.Message})", e); }
            return Task.CompletedTask;
        }
```

- [ ] **Step 5: Run the full suite.** New BepInEx tests pass; existing UE4SS/pak/owned-folder tests unaffected (the bepinex branch only triggers when `Engine == "bepinex"`).

- [ ] **Step 6: Commit** `feat: scan + drive BepInEx plugins via the loader sinks (Conductor where unowned)`

---

## Deferred (note, not this plan)
- **BepInEx config surfacing in the cockpit:** config lives centrally in `BepInEx/config/*.cfg` (INI — `ModConfig` already parses), but the file is named by the plugin GUID, not the dll. Mapping dll → cfg is a follow-on; the cockpit's generic `.cfg` editing already works once pointed at a file.
- App wiring is unchanged: BepInEx plugins flow through the same mod list + toggle path, so no ViewModel changes are needed here.

## Self-Review
**Coverage:** scan both states + rename enable/disable (T1); scan wiring + Loader tag + posture + sink routing (T2). Config surfacing explicitly deferred. ✅
**Safety:** enable/disable is a reversible rename (no delete/move-to-holding); owned (Vortex/MO2) BepInEx folders read state but the content invariant holds (writes only via the sanctioned loader path, like UE4SS); errors surface (wrapped + thrown). Mirror/OnServer pass excludes loader mods. ✅
**Type consistency:** `BepInExPlugins.Scan(string)->IReadOnlyList<(string Name,string File,bool Enabled)>`, `IsEnabled(string,string)`, `SetEnabled(string,string,bool)`, `Mod.Loader == "bepinex"`, `Coordination.PostureFor(owner, declaredManaged, loaderCanConduct)`. ✅
