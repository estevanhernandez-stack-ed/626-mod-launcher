# UE config-tweak mods Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A dropped UE config mod (a bare `Engine.ini` zip like Witchfire's Performance Enhancer) merges into the game's `Saved/Config` tree — snapshot-first, reversible, managed as a toggleable row.

**Architecture:** Pure-Core detection (`ConfigMod`), a section-aware INI overlay (`ConfigMerge`), a snapshot-first installer with a camelCase registry (`ConfigModInstaller` + `ConfigModStore`), and thin App wiring (drop pre-check, config-root from saveDir, rows). Disable/uninstall restore the install-time snapshot; re-enable re-merges against current content.

**Tech Stack:** .NET 10 / C#, xUnit headless Core tests. Core does plain System.IO (like `SaveModStore`) — no WinUI/WinRT (`CorePurityTests` enforces). `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` on Core. camelCase JSON on disk via `AtomicJson`.

**Reference reading before starting:**
- Spec: `docs/superpowers/specs/2026-06-11-ue-config-mods-design.md`
- `src/ModManager.Core/Intake.cs` — `ClassifyDrop(filePath, exts)` returns `"zip" | "mod" | "skip"` (25 lines, whole file).
- `src/ModManager.Core/SaveModStore.cs` — the registry template: tolerant `Load`, `Upsert`/`Remove` via `AtomicJson.WriteJsonAtomic`, camelCase.
- `src/ModManager.Core/IniEdit/IniEditService.cs` — the snapshot pattern (`.bak` under `<dataDir>/.ini-history/<modId>/<name>.<timestamp>.bak`, prune to 10). The installer writes its OWN snapshots modeled on this (exact recorded path, not most-recent lookup) — read it for the shape, don't reuse it.
- `src/ModManager.Core/AtomicJson.cs` — camelCase + atomic temp-write+rename.
- `src/ModManager.App/ViewModels/MainViewModel.cs` — the drop pipeline pre-checks: Pre-check 0 framework (~line 1290), Pre-check 1 save-mods (~1358), Pre-check 3 tools (~1439). Direct-inject row construction (~line 387, `rep.Location == "direct-inject"`) is the model for registry-backed rows.
- Witchfire ground truth: saveDir `C:\Users\estev\AppData\Local\Witchfire\Saved\SaveGames`; config target `C:\Users\estev\AppData\Local\Witchfire\Saved\Config\WindowsNoEditor\Engine.ini`. UE5 games use `Config/Windows/` instead of `Config/WindowsNoEditor/` — support both.

**Build/test commands (Windows, from `c:\Users\estev\Projects\626-mod-launcher`):**
- Core tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
- One class: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~<Class>"`
- App build: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (MSB3027 file-lock from a running app is NOT a compile error — retry into temp output `-p:BaseOutputPath=obj/tmpbuild/ --output bin/tmpbuild/`, then delete temp dirs; never kill processes).
- NEVER bare `dotnet test`/`dotnet build` at the repo root (the WinUI project hangs).

---

## File Structure

| Path | Responsibility |
|---|---|
| `src/ModManager.Core/ConfigMods/ConfigMod.cs` | NEW. Detection: known UE config filenames, config-only payload, config-root/-dir resolution helpers. |
| `src/ModManager.Core/ConfigMods/ConfigMerge.cs` | NEW. Pure string→string section-aware INI overlay. |
| `src/ModManager.Core/ConfigMods/ConfigModStore.cs` | NEW. `ConfigModEntry`/`ConfigFileRecord` records + camelCase registry (`config-mods.json`), modeled on SaveModStore. |
| `src/ModManager.Core/ConfigMods/ConfigModInstaller.cs` | NEW. Validate → snapshot → merge → atomic write → register; Disable/Enable/Uninstall. |
| `src/ModManager.Core/Intake.cs` | MODIFY. `"config"` verdict for loose known-config files. |
| `src/ModManager.App/ViewModels/MainViewModel.cs` | MODIFY. Config drop pre-check; config-mod rows; toggle/uninstall dispatch. |
| `tests/ModManager.Tests/ConfigMods/` | NEW. `ConfigModTests`, `ConfigMergeTests`, `ConfigModStoreTests`, `ConfigModInstallerTests`. |
| `.claude/rules/camelcase-json-on-disk.md` | MODIFY. Add ConfigModStore to governed surfaces. |
| `docs/smoke-tests/pending.md` | MODIFY. Smoke entry. |

---

## Task 1: ConfigMod detection + ClassifyDrop "config" verdict

**Files:**
- Create: `src/ModManager.Core/ConfigMods/ConfigMod.cs`
- Modify: `src/ModManager.Core/Intake.cs`
- Test: `tests/ModManager.Tests/ConfigMods/ConfigModTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests.ConfigMods;

public class ConfigModTests
{
    [Theory]
    [InlineData("Engine.ini", true)]
    [InlineData("Scalability.ini", true)]
    [InlineData("Input.ini", true)]
    [InlineData("GameUserSettings.ini", true)]
    [InlineData("Game.ini", true)]
    [InlineData("engine.INI", true)]                 // case-insensitive
    [InlineData("Config/Engine.ini", true)]          // subfolder basename
    [InlineData(@"some\dir\GameUserSettings.ini", true)]
    [InlineData("settings.ini", false)]              // random ini is NOT a config mod
    [InlineData("Engine.txt", false)]
    [InlineData("mod_P.pak", false)]
    [InlineData("", false)]
    public void IsConfigFile_matches_known_UE_config_basenames(string name, bool expected)
        => Assert.Equal(expected, ConfigMod.IsConfigFile(name));

    [Fact]
    public void Config_only_payload_is_config()
        => Assert.True(ConfigMod.IsConfigOnlyPayload(
            new[] { "Engine.ini" }, new[] { "pak", "ucas", "utoc" }));

    [Fact]
    public void Payload_with_paks_is_not_config_even_if_it_bundles_a_config()
        => Assert.False(ConfigMod.IsConfigOnlyPayload(
            new[] { "CoolMod_P.pak", "Engine.ini" }, new[] { "pak", "ucas", "utoc" }));

    [Fact]
    public void Payload_with_random_ini_only_is_not_config()
        => Assert.False(ConfigMod.IsConfigOnlyPayload(
            new[] { "settings.ini" }, new[] { "pak" }));

    [Fact]
    public void Junk_entries_are_ignored_when_judging_config_only()
        // readme/screenshots alongside the config don't disqualify it
        => Assert.True(ConfigMod.IsConfigOnlyPayload(
            new[] { "README.txt", "preview.jpg", "Engine.ini" }, new[] { "pak" }));

    [Fact]
    public void Empty_payload_is_not_config()
        => Assert.False(ConfigMod.IsConfigOnlyPayload(Array.Empty<string>(), new[] { "pak" }));

    [Fact]
    public void ClassifyDrop_returns_config_for_a_loose_known_config_file()
        => Assert.Equal("config", Intake.ClassifyDrop(@"C:\downloads\Engine.ini", new[] { "pak" }));

    [Fact]
    public void ClassifyDrop_still_skips_a_random_ini()
        => Assert.Equal("skip", Intake.ClassifyDrop(@"C:\downloads\settings.ini", new[] { "pak" }));

    [Fact]
    public void ClassifyDrop_zip_and_mod_verdicts_unchanged()
    {
        Assert.Equal("zip", Intake.ClassifyDrop("a.zip", new[] { "pak" }));
        Assert.Equal("mod", Intake.ClassifyDrop("a.pak", new[] { "pak" }));
        Assert.Equal("skip", Intake.ClassifyDrop("a.exe", new[] { "pak" }));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ConfigModTests"`
Expected: FAIL — `ConfigMod` does not exist (CS0103/CS0246 compile errors).

- [ ] **Step 3: Write minimal implementation**

`src/ModManager.Core/ConfigMods/ConfigMod.cs`:

```csharp
namespace ModManager.Core;

/// <summary>
/// Detection for UE config-tweak mods — mods that are a bare known config file (Engine.ini etc.)
/// merged into the game's Saved/Config tree, not a pak. Known-filename match only: a random .ini
/// is never a config mod, and a payload that contains pak/mod files is a pak mod even if it
/// bundles a config (config-only is the trigger; we never hijack normal mod zips).
/// </summary>
public static class ConfigMod
{
    /// <summary>UE config files a mod may legitimately target. Basename match, case-insensitive.</summary>
    public static readonly string[] KnownConfigFiles =
        { "Engine.ini", "Scalability.ini", "Input.ini", "GameUserSettings.ini", "Game.ini" };

    public static bool IsConfigFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var baseName = Path.GetFileName(fileName.Replace('\\', '/'));
        return KnownConfigFiles.Any(k => string.Equals(k, baseName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when the payload's recognized entries are all known config files and none are
    /// pak/mod files. Junk (readme, images) is ignored; an empty payload is not a config mod.</summary>
    public static bool IsConfigOnlyPayload(IEnumerable<string> names, IEnumerable<string>? pakExts)
    {
        var exts = (pakExts ?? Enumerable.Empty<string>()).Select(e => e.ToLowerInvariant()).ToHashSet();
        var sawConfig = false;
        foreach (var n in names)
        {
            if (IsConfigFile(n)) { sawConfig = true; continue; }
            var dot = n.LastIndexOf('.');
            var ext = dot >= 0 ? n[(dot + 1)..].ToLowerInvariant() : "";
            if (exts.Contains(ext)) return false; // pak/mod file present -> pak mod wins
        }
        return sawConfig;
    }
}
```

In `src/ModManager.Core/Intake.cs`, `ClassifyDrop`, insert the config check between the archive check and the extension check (the current body is 8 lines — after `if (ArchiveExtensions.Any(...)) return "zip";` add):

```csharp
        if (ConfigMod.IsConfigFile(filePath)) return "config";
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ConfigModTests"` → PASS.
Then the FULL suite — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` → ALL pass. CRITICAL regression check: the new `"config"` verdict must not break existing intake (`Scanner.cs` consumes `ClassifyDrop` at ~lines 902/956/981/1042 — a loose `Engine.ini` previously classified `"skip"` there; it now classifies `"config"`, which those call sites treat as not-`"mod"`/not-`"zip"`/not-`"skip"`. Read each call site and confirm a `"config"` verdict falls through harmlessly (treated like skip) in the Core scanner paths — the App routes config BEFORE those paths run. If any call site explicitly branches `!= "skip"` into mod handling (line ~956 does: `if (ClassifyDrop(f, c.Exts) != "skip") outList.Add(f)`), config files would now be ADDED where they previously weren't — check whether that path (WalkFiles on a dropped folder) should treat config as skip, and if so use `is not ("skip" or "config")` there. Report what you found.)

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/ConfigMods/ConfigMod.cs src/ModManager.Core/Intake.cs tests/ModManager.Tests/ConfigMods/ConfigModTests.cs
git commit -m "feat(config-mods): detect known UE config files + config drop verdict"
```

---

## Task 2: ConfigMerge — the section-aware INI overlay

**Files:**
- Create: `src/ModManager.Core/ConfigMods/ConfigMerge.cs`
- Test: `tests/ModManager.Tests/ConfigMods/ConfigMergeTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests.ConfigMods;

public class ConfigMergeTests
{
    [Fact]
    public void Plain_key_in_existing_section_is_replaced()
    {
        var existing = "[Core.System]\r\nFoo=1\r\nBar=2\r\n";
        var mod = "[Core.System]\r\nFoo=99\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.Contains("Foo=99", merged);
        Assert.DoesNotContain("Foo=1", merged);
        Assert.Contains("Bar=2", merged); // untouched key survives
    }

    [Fact]
    public void Missing_key_is_added_to_the_existing_section()
    {
        var existing = "[Core.System]\r\nBar=2\r\n";
        var mod = "[Core.System]\r\nFoo=1\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.Contains("Foo=1", merged);
        Assert.Contains("Bar=2", merged);
    }

    [Fact]
    public void Missing_section_is_appended_whole()
    {
        var existing = "[A]\r\nX=1\r\n";
        var mod = "[SystemSettings]\r\nr.Lumen=0\r\nr.Shadow=1\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.Contains("[SystemSettings]", merged);
        Assert.Contains("r.Lumen=0", merged);
        Assert.Contains("r.Shadow=1", merged);
        Assert.Contains("X=1", merged);
    }

    [Fact]
    public void UE_array_syntax_appends_instead_of_replacing()
    {
        var existing = "[Audio]\r\n+Mix=Default\r\n";
        var mod = "[Audio]\r\n+Mix=Loud\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.Contains("+Mix=Default", merged); // existing array line survives
        Assert.Contains("+Mix=Loud", merged);    // mod array line appended
    }

    [Fact]
    public void Identical_array_line_is_not_duplicated_on_remerge()
    {
        var existing = "[Audio]\r\n+Mix=Loud\r\n";
        var mod = "[Audio]\r\n+Mix=Loud\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        var count = merged.Split("+Mix=Loud").Length - 1;
        Assert.Equal(1, count); // idempotent re-merge
    }

    [Fact]
    public void Comments_and_blank_lines_in_existing_are_preserved()
    {
        var existing = "; user comment\r\n\r\n[A]\r\n; keep me\r\nX=1\r\n";
        var mod = "[A]\r\nX=2\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.Contains("; user comment", merged);
        Assert.Contains("; keep me", merged);
        Assert.Contains("X=2", merged);
    }

    [Fact]
    public void Existing_newline_style_is_preserved_LF()
    {
        var existing = "[A]\nX=1\n";
        var mod = "[A]\r\nY=2\r\n";   // mod is CRLF, existing is LF -> output stays LF
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.DoesNotContain("\r\n", merged);
        Assert.Contains("Y=2", merged);
    }

    [Fact]
    public void Empty_existing_becomes_the_mod_content()
    {
        var merged = ConfigMerge.Merge("", "[A]\r\nX=1\r\n");
        Assert.Contains("[A]", merged);
        Assert.Contains("X=1", merged);
    }

    [Fact]
    public void Section_match_is_case_insensitive()
    {
        var existing = "[systemsettings]\r\nX=1\r\n";
        var mod = "[SystemSettings]\r\nX=2\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.Contains("X=2", merged);
        Assert.DoesNotContain("X=1", merged);
        // does NOT create a second section
        Assert.Equal(1, merged.Split('[').Length - 1);
    }

    [Fact]
    public void Mod_comments_are_not_merged_in()
    {
        var existing = "[A]\r\nX=1\r\n";
        var mod = "[A]\r\n; mod chatter\r\nY=2\r\n";
        var merged = ConfigMerge.Merge(existing, mod);
        Assert.DoesNotContain("mod chatter", merged);
        Assert.Contains("Y=2", merged);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ConfigMergeTests"`
Expected: FAIL — `ConfigMerge` does not exist.

- [ ] **Step 3: Write minimal implementation**

`src/ModManager.Core/ConfigMods/ConfigMerge.cs`:

```csharp
using System.Text;

namespace ModManager.Core;

/// <summary>
/// Section-aware INI overlay for UE config-tweak mods. The mod's [Section] Key=Value pairs are
/// applied onto the existing file: plain keys replace (or add); UE array ops (+Key / -Key / .Key)
/// append — replacing them positionally would corrupt UE's list semantics. Sections the file lacks
/// are appended whole. Everything the mod doesn't mention — other keys, comments, blank lines —
/// survives untouched, and the existing file's newline style is preserved (the bare-CR lesson).
/// Pure string -> string; idempotent (re-merging the same mod adds nothing twice).
/// </summary>
public static class ConfigMerge
{
    public static string Merge(string existingIni, string modIni)
    {
        var nl = DetectNewline(existingIni) ?? DetectNewline(modIni) ?? "\r\n";
        var lines = SplitLines(existingIni);
        var modSections = ParseSections(modIni);

        foreach (var (section, entries) in modSections)
        {
            var headerIdx = FindSectionHeader(lines, section);
            if (headerIdx < 0)
            {
                // Section missing: append whole (header + entries) at end.
                if (lines.Count > 0 && lines[^1].Length != 0) lines.Add("");
                lines.Add("[" + section + "]");
                foreach (var e in entries) lines.Add(e.Raw);
                continue;
            }

            var endIdx = FindSectionEnd(lines, headerIdx);
            foreach (var e in entries)
            {
                if (e.IsArrayOp)
                {
                    // Append unless the identical line already exists in the section (idempotence).
                    if (!RangeContainsExact(lines, headerIdx + 1, endIdx, e.Raw))
                    { lines.Insert(endIdx, e.Raw); endIdx++; }
                }
                else
                {
                    var keyIdx = FindKeyInRange(lines, headerIdx + 1, endIdx, e.Key);
                    if (keyIdx >= 0) lines[keyIdx] = e.Raw;
                    else { lines.Insert(endIdx, e.Raw); endIdx++; }
                }
            }
        }

        return string.Join(nl, lines) + nl;
    }

    private static string? DetectNewline(string s)
        => s.Contains("\r\n") ? "\r\n" : s.Contains('\n') ? "\n" : null;

    private static List<string> SplitLines(string s)
    {
        if (string.IsNullOrEmpty(s)) return new List<string>();
        var list = s.Replace("\r\n", "\n").Split('\n').ToList();
        // A trailing newline yields one empty tail element; drop it (re-added by the final join).
        if (list.Count > 0 && list[^1].Length == 0) list.RemoveAt(list.Count - 1);
        return list;
    }

    private sealed record ModEntry(string Raw, string Key, bool IsArrayOp);

    private static List<(string Section, List<ModEntry> Entries)> ParseSections(string modIni)
    {
        var result = new List<(string, List<ModEntry>)>();
        List<ModEntry>? current = null;
        foreach (var raw in SplitLines(modIni))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue; // mod comments don't merge
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = new List<ModEntry>();
                result.Add((line[1..^1].Trim(), current));
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0 || current is null) continue; // keyless / pre-section junk: ignore
            var key = line[..eq].Trim();
            var isArray = key.Length > 0 && key[0] is '+' or '-' or '.' or '!';
            result[^1].Item2.Add(new ModEntry(line, isArray ? key : key, isArray));
        }
        return result;
    }

    private static int FindSectionHeader(List<string> lines, string section)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[') && t.EndsWith(']')
                && string.Equals(t[1..^1].Trim(), section, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>Index just past the section's last content line (before trailing blanks / next header).</summary>
    private static int FindSectionEnd(List<string> lines, int headerIdx)
    {
        var end = lines.Count;
        for (var i = headerIdx + 1; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[') && t.EndsWith(']')) { end = i; break; }
        }
        // Back up over trailing blank lines so inserts land inside the section block.
        while (end > headerIdx + 1 && lines[end - 1].Trim().Length == 0) end--;
        return end;
    }

    private static bool RangeContainsExact(List<string> lines, int start, int end, string raw)
    {
        for (var i = start; i < end && i < lines.Count; i++)
            if (string.Equals(lines[i].Trim(), raw, StringComparison.Ordinal)) return true;
        return false;
    }

    private static int FindKeyInRange(List<string> lines, int start, int end, string key)
    {
        for (var i = start; i < end && i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith(';') || t.StartsWith('#')) continue;
            var eq = t.IndexOf('=');
            if (eq <= 0) continue;
            var k = t[..eq].Trim();
            if (k.Length > 0 && k[0] is '+' or '-' or '.' or '!') continue; // array lines never key-match
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }
}
```

Note `!` is included with the array ops (UE's clear-array op) — append semantics are correct for it too.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ConfigMergeTests"` → PASS (10). Then full suite → all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/ConfigMods/ConfigMerge.cs tests/ModManager.Tests/ConfigMods/ConfigMergeTests.cs
git commit -m "feat(config-mods): section-aware INI merge (plain keys replace, UE array ops append)"
```

---

## Task 3: ConfigModStore — records + camelCase registry

**Files:**
- Create: `src/ModManager.Core/ConfigMods/ConfigModStore.cs`
- Test: `tests/ModManager.Tests/ConfigMods/ConfigModStoreTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using ModManager.Core;

namespace ModManager.Tests.ConfigMods;

public class ConfigModStoreTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "cfgstore-" + Guid.NewGuid().ToString("n"));
    public ConfigModStoreTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private static ConfigModEntry Entry(string id = "perf-enh") => new(
        Id: id, Name: "Performance Enhancer",
        Files: new[] { new ConfigFileRecord("Engine.ini", ExistedAtInstall: true,
            SnapshotPath: @"C:\snap\Engine.ini.123.bak", Payload: "[S]\r\nX=1\r\n") },
        InstalledUtc: new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc), Enabled: true);

    [Fact]
    public void Upsert_then_Load_round_trips()
    {
        ConfigModStore.Upsert(_tmp, Entry());
        var loaded = ConfigModStore.Load(_tmp).Single();
        Assert.Equal("perf-enh", loaded.Id);
        Assert.True(loaded.Enabled);
        var f = loaded.Files.Single();
        Assert.Equal("Engine.ini", f.FileName);
        Assert.True(f.ExistedAtInstall);
        Assert.Equal(@"C:\snap\Engine.ini.123.bak", f.SnapshotPath);
        Assert.Equal("[S]\r\nX=1\r\n", f.Payload);
    }

    [Fact]
    public void On_disk_json_is_camelCase()
    {
        ConfigModStore.Upsert(_tmp, Entry());
        var json = File.ReadAllText(Path.Combine(_tmp, "config-mods.json"));
        Assert.Contains("\"id\"", json);
        Assert.Contains("\"existedAtInstall\"", json);
        Assert.Contains("\"snapshotPath\"", json);
        Assert.DoesNotContain("\"Id\"", json);
        Assert.DoesNotContain("\"ExistedAtInstall\"", json);
    }

    [Fact]
    public void Upsert_replaces_same_id()
    {
        ConfigModStore.Upsert(_tmp, Entry());
        ConfigModStore.Upsert(_tmp, Entry() with { Enabled = false });
        var loaded = ConfigModStore.Load(_tmp);
        Assert.Single(loaded);
        Assert.False(loaded[0].Enabled);
    }

    [Fact]
    public void Remove_deletes_by_id()
    {
        ConfigModStore.Upsert(_tmp, Entry());
        ConfigModStore.Remove(_tmp, "perf-enh");
        Assert.Empty(ConfigModStore.Load(_tmp));
    }

    [Fact]
    public void Missing_or_corrupt_file_loads_empty()
    {
        Assert.Empty(ConfigModStore.Load(_tmp));
        File.WriteAllText(Path.Combine(_tmp, "config-mods.json"), "{not json");
        Assert.Empty(ConfigModStore.Load(_tmp));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ConfigModStoreTests"`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Write minimal implementation**

`src/ModManager.Core/ConfigMods/ConfigModStore.cs` (modeled line-for-line on `SaveModStore`):

```csharp
using System.Text.Json;

namespace ModManager.Core;

/// <summary>One config file a config mod touched: whether it existed pre-install (false -> undo
/// deletes it), the exact snapshot taken before our write, and the mod's raw ini payload for that
/// file (kept so re-enable can re-merge against the file's CURRENT content).</summary>
public sealed record ConfigFileRecord(string FileName, bool ExistedAtInstall, string? SnapshotPath, string Payload);

/// <summary>One installed config-tweak mod (Engine.ini & friends), with per-file records.</summary>
public sealed record ConfigModEntry(
    string Id, string Name, IReadOnlyList<ConfigFileRecord> Files, DateTime InstalledUtc, bool Enabled);

/// <summary>
/// The installed-config-mods registry: a small JSON file (<c>config-mods.json</c>) under the game's
/// data dir, written atomically (camelCase). Tolerant load — missing/corrupt reads as empty, never
/// throws. Modeled on <see cref="SaveModStore"/>.
/// </summary>
public static class ConfigModStore
{
    public const string FileName = "config-mods.json";

    private static readonly JsonSerializerOptions ReadJson = new() { PropertyNameCaseInsensitive = true };

    private static string PathFor(string dataDir) => System.IO.Path.Combine(dataDir, FileName);

    public static IReadOnlyList<ConfigModEntry> Load(string dataDir)
    {
        var path = PathFor(dataDir);
        if (!File.Exists(path)) return Array.Empty<ConfigModEntry>();
        try
        {
            var list = JsonSerializer.Deserialize<List<ConfigModEntry>>(File.ReadAllText(path), ReadJson);
            return list?.Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Id)).ToList()
                   ?? (IReadOnlyList<ConfigModEntry>)Array.Empty<ConfigModEntry>();
        }
        catch { return Array.Empty<ConfigModEntry>(); }
    }

    public static void Upsert(string dataDir, ConfigModEntry entry)
    {
        Directory.CreateDirectory(dataDir);
        var list = Load(dataDir)
            .Where(e => !string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        list.Add(entry);
        AtomicJson.WriteJsonAtomic(PathFor(dataDir), list);
    }

    public static void Remove(string dataDir, string id)
    {
        var list = Load(dataDir)
            .Where(e => !string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Directory.CreateDirectory(dataDir);
        AtomicJson.WriteJsonAtomic(PathFor(dataDir), list);
    }
}
```

CONFIRM `AtomicJson.WriteJsonAtomic(path, value)` is the exact signature (read `src/ModManager.Core/AtomicJson.cs`) — SaveModStore calls it this way, so it is.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ConfigModStoreTests"` → PASS (5). Full suite → all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/ConfigMods/ConfigModStore.cs tests/ModManager.Tests/ConfigMods/ConfigModStoreTests.cs
git commit -m "feat(config-mods): camelCase config-mod registry (config-mods.json)"
```

---

## Task 4: ConfigModInstaller — install path (validate → snapshot → merge → register)

**Files:**
- Create: `src/ModManager.Core/ConfigMods/ConfigModInstaller.cs`
- Modify: `src/ModManager.Core/ConfigMods/ConfigMod.cs` (add the two resolution helpers)
- Test: `tests/ModManager.Tests/ConfigMods/ConfigModInstallerTests.cs`

- [ ] **Step 1: Add the resolution helpers to `ConfigMod.cs` (with tests in ConfigModTests.cs)**

Add tests to `tests/ModManager.Tests/ConfigMods/ConfigModTests.cs`:

```csharp
    [Theory]
    [InlineData(@"C:\Users\u\AppData\Local\Witchfire\Saved\SaveGames", @"C:\Users\u\AppData\Local\Witchfire\Saved\Config")]
    [InlineData(@"C:\x\Game\Saved\SaveGames\profile1", @"C:\x\Game\Saved\Config")]
    [InlineData(@"C:\no\saved\segment", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ConfigRootFromSaveDir_walks_to_the_Saved_sibling(string? saveDir, string? expected)
        => Assert.Equal(expected, ConfigMod.ConfigRootFromSaveDir(saveDir));
```

(Run filtered — FAIL: method missing.) Then add to `ConfigMod.cs`:

```csharp
    /// <summary>The game's Saved/Config root derived from its save dir (both live under .../Saved/).
    /// "C:\...\Witchfire\Saved\SaveGames" -> "C:\...\Witchfire\Saved\Config". Null when the save dir
    /// is unset or has no Saved segment — the caller refuses with a clear message.</summary>
    public static string? ConfigRootFromSaveDir(string? saveDir)
    {
        if (string.IsNullOrWhiteSpace(saveDir)) return null;
        var norm = saveDir.Replace('/', '\\');
        var idx = norm.LastIndexOf(@"\Saved\", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return norm.EndsWith(@"\Saved", StringComparison.OrdinalIgnoreCase)
                ? norm + @"\Config" : null;
        return norm[..(idx + @"\Saved".Length)] + @"\Config";
    }

    /// <summary>The platform config dir under a config root: UE4 uses WindowsNoEditor, UE5 uses
    /// Windows. Returns the one that exists on disk, or null when neither does (game never ran —
    /// the caller refuses; the game must own the tree first).</summary>
    public static string? ResolveConfigDir(string configRoot)
    {
        if (string.IsNullOrWhiteSpace(configRoot)) return null;
        var ue4 = Path.Combine(configRoot, "WindowsNoEditor");
        if (Directory.Exists(ue4)) return ue4;
        var ue5 = Path.Combine(configRoot, "Windows");
        if (Directory.Exists(ue5)) return ue5;
        return null;
    }
```

`ResolveConfigDir` is exercised through the installer tests (it needs a disk fixture). Run the ConfigRootFromSaveDir theory → PASS.

- [ ] **Step 2: Write the failing installer tests**

`tests/ModManager.Tests/ConfigMods/ConfigModInstallerTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests.ConfigMods;

public class ConfigModInstallerTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "cfginst-" + Guid.NewGuid().ToString("n"));
    private readonly string _configDir;   // .../Saved/Config/WindowsNoEditor
    private readonly string _dataDir;

    public ConfigModInstallerTests()
    {
        _configDir = Path.Combine(_tmp, "Saved", "Config", "WindowsNoEditor");
        _dataDir = Path.Combine(_tmp, "data");
        Directory.CreateDirectory(_configDir);
        Directory.CreateDirectory(_dataDir);
    }
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private static readonly (string Name, string Content)[] PerfPayload =
        { ("Engine.ini", "[SystemSettings]\r\nr.Lumen=0\r\n") };

    [Fact]
    public void Install_merges_into_existing_file_and_snapshots_first()
    {
        var target = Path.Combine(_configDir, "Engine.ini");
        File.WriteAllText(target, "[SystemSettings]\r\nr.Shadow=1\r\n");

        var entry = ConfigModInstaller.Install(PerfPayload, _configDir, _dataDir, "Performance Enhancer");

        var written = File.ReadAllText(target);
        Assert.Contains("r.Lumen=0", written);   // mod key in
        Assert.Contains("r.Shadow=1", written);  // existing key survives (merge, not replace)

        var f = entry.Files.Single();
        Assert.True(f.ExistedAtInstall);
        Assert.NotNull(f.SnapshotPath);
        Assert.True(File.Exists(f.SnapshotPath));
        Assert.Contains("r.Shadow=1", File.ReadAllText(f.SnapshotPath!)); // snapshot = pre-merge content
        Assert.DoesNotContain("r.Lumen=0", File.ReadAllText(f.SnapshotPath!));

        var stored = ConfigModStore.Load(_dataDir).Single();
        Assert.Equal(entry.Id, stored.Id);
        Assert.True(stored.Enabled);
    }

    [Fact]
    public void Install_into_missing_target_records_existedAtInstall_false()
    {
        var entry = ConfigModInstaller.Install(PerfPayload, _configDir, _dataDir, "Perf");
        var f = entry.Files.Single();
        Assert.False(f.ExistedAtInstall);
        Assert.Null(f.SnapshotPath);
        Assert.True(File.Exists(Path.Combine(_configDir, "Engine.ini")));
    }

    [Fact]
    public void Install_refuses_unknown_filenames_nothing_written()
    {
        var bad = new[] { ("evil.ini", "[A]\r\nX=1\r\n") };
        Assert.ThrowsAny<InvalidOperationException>(
            () => ConfigModInstaller.Install(bad, _configDir, _dataDir, "Bad"));
        Assert.Empty(Directory.GetFiles(_configDir));
        Assert.Empty(ConfigModStore.Load(_dataDir));
    }

    [Fact]
    public void Install_refuses_path_traversal_nothing_written()
    {
        var bad = new[] { (@"..\Engine.ini", "[A]\r\nX=1\r\n") };
        Assert.ThrowsAny<InvalidOperationException>(
            () => ConfigModInstaller.Install(bad, _configDir, _dataDir, "Bad"));
        Assert.Empty(Directory.GetFiles(_configDir));
    }

    [Fact]
    public void Install_refuses_empty_payload()
    {
        Assert.ThrowsAny<InvalidOperationException>(
            () => ConfigModInstaller.Install(Array.Empty<(string, string)>(), _configDir, _dataDir, "Empty"));
    }

    [Fact]
    public void Redrop_restores_then_remerges_no_stacking()
    {
        var target = Path.Combine(_configDir, "Engine.ini");
        File.WriteAllText(target, "[SystemSettings]\r\nr.Shadow=1\r\n");
        ConfigModInstaller.Install(PerfPayload, _configDir, _dataDir, "Perf");
        // Re-drop the same mod (same name -> same id).
        ConfigModInstaller.Install(PerfPayload, _configDir, _dataDir, "Perf");

        var written = File.ReadAllText(target);
        Assert.Equal(1, written.Split("r.Lumen=0").Length - 1); // exactly once, no stacking
        Assert.Contains("r.Shadow=1", written);
        Assert.Single(ConfigModStore.Load(_dataDir));           // one entry, not two
    }
}
```

- [ ] **Step 3: Run to verify FAIL** — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ConfigModInstallerTests"` → compile error, `ConfigModInstaller` missing.

- [ ] **Step 4: Write the installer (install path only — lifecycle is Task 5)**

`src/ModManager.Core/ConfigMods/ConfigModInstaller.cs`:

```csharp
namespace ModManager.Core;

/// <summary>
/// Installs UE config-tweak mods into a game's Saved/Config platform dir. Validate-then-write:
/// nothing touches the config tree until the whole payload is validated. Snapshot-first: every
/// existing target file is backed up (exact recorded path, modeled on IniEditService's .bak
/// layout) BEFORE the merged content is written. Registered in ConfigModStore so the row, toggle,
/// and uninstall are driven from one record. Disable restores the snapshot; re-enable re-merges
/// the stored payload against the file's CURRENT content; uninstall restores and removes.
/// </summary>
public static class ConfigModInstaller
{
    /// <summary>Stable id from the display name: lowercase, non-alnum -> '-'.</summary>
    public static string IdFor(string name)
    {
        var slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Length > 0 ? slug : "config-mod";
    }

    public static ConfigModEntry Install(
        IReadOnlyList<(string Name, string Content)> payload, string configDir, string dataDir, string modName)
    {
        // ---- Validate (no writes yet) ----
        if (payload.Count == 0)
            throw new InvalidOperationException("Nothing to apply — the archive has no config settings.");
        foreach (var (name, content) in payload)
        {
            var baseName = Path.GetFileName(name.Replace('\\', '/'));
            if (name.Contains("..") || Path.IsPathRooted(name))
                throw new InvalidOperationException($"\"{name}\" escapes the config folder — refusing. Nothing was changed.");
            if (!ConfigMod.IsConfigFile(baseName))
                throw new InvalidOperationException($"\"{baseName}\" isn't a known UE config file — refusing. Nothing was changed.");
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException($"\"{baseName}\" is empty — nothing to apply.");
        }
        if (!Directory.Exists(configDir))
            throw new InvalidOperationException(
                "The game's config folder doesn't exist yet — run the game once so it creates its config, then drop the mod again.");

        var id = IdFor(modName);

        // Re-drop of the same mod: restore its previous state first so merges never stack.
        var prior = ConfigModStore.Load(dataDir).FirstOrDefault(e => e.Id == id);
        if (prior is not null) RestoreFiles(prior, configDir);

        // ---- Snapshot + merge + write, per file ----
        var records = new List<ConfigFileRecord>();
        var written = new List<(string Target, ConfigFileRecord Rec)>();
        try
        {
            foreach (var (name, content) in payload)
            {
                var baseName = Path.GetFileName(name.Replace('\\', '/'));
                var target = Path.Combine(configDir, baseName);
                var existed = File.Exists(target);
                string? snapshot = null;
                if (existed)
                {
                    snapshot = SnapshotPathFor(dataDir, id, baseName);
                    Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
                    File.Copy(target, snapshot, overwrite: true); // snapshot BEFORE any write
                }
                var merged = existed ? ConfigMerge.Merge(File.ReadAllText(target), content) : content;
                WriteAtomic(target, merged);
                var rec = new ConfigFileRecord(baseName, existed, snapshot, content);
                records.Add(rec);
                written.Add((target, rec));
            }
        }
        catch
        {
            // Roll back whatever was written so the config tree is never left mid-state.
            foreach (var (target, rec) in written) RestoreOne(target, rec);
            throw;
        }

        var entry = new ConfigModEntry(id, modName, records, DateTime.UtcNow, Enabled: true);
        ConfigModStore.Upsert(dataDir, entry);
        return entry;
    }

    internal static string SnapshotPathFor(string dataDir, string id, string baseName)
        => Path.Combine(dataDir, ".config-history", id, $"{baseName}.{DateTime.UtcNow.Ticks}.bak");

    internal static void RestoreFiles(ConfigModEntry entry, string configDir)
    {
        foreach (var f in entry.Files)
            RestoreOne(Path.Combine(configDir, f.FileName), f);
    }

    private static void RestoreOne(string target, ConfigFileRecord rec)
    {
        if (rec.ExistedAtInstall && rec.SnapshotPath is not null && File.Exists(rec.SnapshotPath))
            File.Copy(rec.SnapshotPath, target, overwrite: true);
        else if (!rec.ExistedAtInstall && File.Exists(target))
            File.Delete(target); // the ONLY delete: a file the mod itself created
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("n");
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
```

- [ ] **Step 5: Run to verify PASS** — filtered (6 tests) then full suite → all pass.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/ConfigMods/ tests/ModManager.Tests/ConfigMods/
git commit -m "feat(config-mods): snapshot-first installer (validate -> snapshot -> merge -> register)"
```

---

## Task 5: Lifecycle — Disable / Enable / Uninstall

**Files:**
- Modify: `src/ModManager.Core/ConfigMods/ConfigModInstaller.cs`
- Test: `tests/ModManager.Tests/ConfigMods/ConfigModInstallerTests.cs` (add cases)

- [ ] **Step 1: Write the failing tests (add to ConfigModInstallerTests)**

```csharp
    [Fact]
    public void Disable_restores_the_preinstall_file_and_keeps_the_entry()
    {
        var target = Path.Combine(_configDir, "Engine.ini");
        File.WriteAllText(target, "[SystemSettings]\r\nr.Shadow=1\r\n");
        var entry = ConfigModInstaller.Install(PerfPayload, _configDir, _dataDir, "Perf");

        ConfigModInstaller.Disable(entry, _configDir, _dataDir);

        var restored = File.ReadAllText(target);
        Assert.DoesNotContain("r.Lumen=0", restored);
        Assert.Contains("r.Shadow=1", restored);
        var stored = ConfigModStore.Load(_dataDir).Single();
        Assert.False(stored.Enabled); // entry kept, disabled
    }

    [Fact]
    public void Disable_deletes_a_file_the_mod_created()
    {
        var entry = ConfigModInstaller.Install(PerfPayload, _configDir, _dataDir, "Perf");
        ConfigModInstaller.Disable(entry, _configDir, _dataDir);
        Assert.False(File.Exists(Path.Combine(_configDir, "Engine.ini")));
    }

    [Fact]
    public void Enable_remerges_against_the_files_CURRENT_content()
    {
        var target = Path.Combine(_configDir, "Engine.ini");
        File.WriteAllText(target, "[SystemSettings]\r\nr.Shadow=1\r\n");
        var entry = ConfigModInstaller.Install(PerfPayload, _configDir, _dataDir, "Perf");
        ConfigModInstaller.Disable(entry, _configDir, _dataDir);

        // The game rewrites its config while the mod is off.
        File.WriteAllText(target, "[SystemSettings]\r\nr.Shadow=1\r\nr.NewGameSetting=5\r\n");

        var reenabled = ConfigModInstaller.Enable(ConfigModStore.Load(_dataDir).Single(), _configDir, _dataDir);

        var content = File.ReadAllText(target);
        Assert.Contains("r.Lumen=0", content);          // mod keys back
        Assert.Contains("r.NewGameSetting=5", content); // game's new setting SURVIVES (re-merge, not old-merge restore)
        Assert.True(ConfigModStore.Load(_dataDir).Single().Enabled);
        Assert.NotNull(reenabled.Files.Single().SnapshotPath); // fresh snapshot taken
    }

    [Fact]
    public void Uninstall_restores_and_removes_the_entry()
    {
        var target = Path.Combine(_configDir, "Engine.ini");
        File.WriteAllText(target, "[SystemSettings]\r\nr.Shadow=1\r\n");
        var entry = ConfigModInstaller.Install(PerfPayload, _configDir, _dataDir, "Perf");

        ConfigModInstaller.Uninstall(entry, _configDir, _dataDir);

        Assert.DoesNotContain("r.Lumen=0", File.ReadAllText(target));
        Assert.Empty(ConfigModStore.Load(_dataDir));
        Assert.False(Directory.Exists(Path.Combine(_dataDir, ".config-history", entry.Id))); // snapshots pruned
    }

    [Fact]
    public void Uninstall_after_disable_does_not_restore_twice()
    {
        var target = Path.Combine(_configDir, "Engine.ini");
        File.WriteAllText(target, "[SystemSettings]\r\nr.Shadow=1\r\n");
        var entry = ConfigModInstaller.Install(PerfPayload, _configDir, _dataDir, "Perf");
        ConfigModInstaller.Disable(entry, _configDir, _dataDir);

        // User edits the file while the mod is off; uninstall must NOT clobber it with the old snapshot.
        File.WriteAllText(target, "[SystemSettings]\r\nr.Shadow=1\r\nr.UserEdit=7\r\n");
        ConfigModInstaller.Uninstall(ConfigModStore.Load(_dataDir).Single(), _configDir, _dataDir);

        Assert.Contains("r.UserEdit=7", File.ReadAllText(target));
        Assert.Empty(ConfigModStore.Load(_dataDir));
    }
```

- [ ] **Step 2: Run to verify FAIL** — `Disable`/`Enable`/`Uninstall` missing.

- [ ] **Step 3: Implement the lifecycle (add to ConfigModInstaller)**

```csharp
    /// <summary>Toggle OFF: restore each touched file to its install-time snapshot (delete files the
    /// mod created), keep the registry entry with enabled=false.</summary>
    public static void Disable(ConfigModEntry entry, string configDir, string dataDir)
    {
        RestoreFiles(entry, configDir);
        ConfigModStore.Upsert(dataDir, entry with { Enabled = false });
    }

    /// <summary>Toggle ON after a disable: re-merge the stored payload against each file's CURRENT
    /// content (the game may have rewritten it while the mod was off), fresh snapshot first.</summary>
    public static ConfigModEntry Enable(ConfigModEntry entry, string configDir, string dataDir)
    {
        var payload = entry.Files.Select(f => (f.FileName, f.Payload)).ToList();
        // Remove the disabled entry so Install doesn't run its re-drop restore (the files are
        // already at their no-mod state; restoring the OLD snapshot would clobber newer content).
        ConfigModStore.Remove(dataDir, entry.Id);
        return Install(payload, configDir, dataDir, entry.Name);
    }

    /// <summary>Remove entirely: restore (only if currently enabled — a disabled mod's files are
    /// already clean, and restoring the stale snapshot would clobber newer game/user content),
    /// then drop the entry and prune its snapshots.</summary>
    public static void Uninstall(ConfigModEntry entry, string configDir, string dataDir)
    {
        if (entry.Enabled) RestoreFiles(entry, configDir);
        ConfigModStore.Remove(dataDir, entry.Id);
        var history = Path.Combine(dataDir, ".config-history", entry.Id);
        try { if (Directory.Exists(history)) Directory.Delete(history, recursive: true); } catch { /* best effort */ }
    }
```

NOTE the `Enable` subtlety the tests pin: it goes through `Install` for the fresh snapshot+merge+register, but must FIRST remove the disabled entry — otherwise Install's re-drop path would restore the old snapshot over the game's newer content. The `Enable_remerges_against_the_files_CURRENT_content` test fails if you get this wrong.

- [ ] **Step 4: Run to verify PASS** — filtered (11 total now) then full suite → all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/ConfigMods/ConfigModInstaller.cs tests/ModManager.Tests/ConfigMods/ConfigModInstallerTests.cs
git commit -m "feat(config-mods): disable/enable/uninstall — snapshot restore + current-content re-merge"
```

---

## Task 6: App wiring — drop route, rows, toggle/uninstall

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs`
- (App VM — build-verified, no unit tests.)

This task is judgment-heavy: read the existing pre-checks and the direct-inject row path before writing. The shapes below are the contract; adapt member names to what's really there and report deviations.

- [ ] **Step 1: Read the drop pipeline + direct-inject row path**

Read `MainViewModel.cs` ~lines 1280-1480 (pre-checks 0/1/3) and ~lines 376-470 (row construction incl. `rep.Location == "direct-inject"`). Identify: the `remaining` list the pre-checks carve from; how archives are enumerated (`ArchiveReader` / zipEntries, see the framework pre-check ~1624); how a status line is set; how direct-inject rows get toggle dispatch.

- [ ] **Step 2: Add the config pre-check (after save-mods, before tools)**

Insert a "Pre-check 2: config-tweak mods" block. Shape (adapt to real locals):

```csharp
            // Pre-check 2: config-tweak mods (Engine.ini & friends). A drop whose payload is
            // entirely known UE config files merges into the game's Saved/Config tree — snapshot-
            // first, registered, reversible. Config-only is the trigger: a zip with paks stays a
            // pak mod even if it bundles a config.
            if (string.Equals(_ctx!.Game.Engine, "ue-pak", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var p in remaining.ToList())
                {
                    List<(string Name, string Content)>? payload = null;
                    var kind = Intake.ClassifyDrop(p, _ctx.Game.FileExtensions);
                    if (kind == "config")
                    {
                        payload = new() { (Path.GetFileName(p), File.ReadAllText(p)) };
                    }
                    else if (kind == "zip")
                    {
                        using var archive = ArchiveReader.Open(p);   // match the framework pre-check's reader usage
                        var names = archive.Entries.Select(e => e.RelativePath).ToList();
                        if (ConfigMod.IsConfigOnlyPayload(names, _ctx.Game.FileExtensions))
                            payload = archive.Entries
                                .Where(e => ConfigMod.IsConfigFile(e.RelativePath))
                                .Select(e => (Path.GetFileName(e.RelativePath.Replace('\\', '/')), ReadEntryText(e)))
                                .ToList();
                    }
                    if (payload is null) continue;

                    var configRoot = ConfigMod.ConfigRootFromSaveDir(_ctx.Game.SaveDir);
                    var configDir = configRoot is null ? null : ConfigMod.ResolveConfigDir(configRoot);
                    if (configDir is null)
                    {
                        SetStatus("Couldn't find the game's config folder — set the save folder (or run the game once), then drop again.");
                        remaining.Remove(p);
                        continue;
                    }
                    try
                    {
                        var modName = Path.GetFileNameWithoutExtension(p);
                        var entry = ConfigModInstaller.Install(payload, configDir, _ctx.DataDir, modName);
                        SetStatus($"Merged {string.Join(", ", entry.Files.Select(f => f.FileName))} into {_ctx.Game.GameName}'s config — your previous config is snapshotted.");
                    }
                    catch (InvalidOperationException ex) { SetStatus(ex.Message); }
                    remaining.Remove(p);
                }
            }
```

Notes for the implementer: `SetStatus` stands for however the surrounding pre-checks publish a status line — use that exact mechanism. `ReadEntryText(e)` stands for the archive-entry read the codebase uses (the framework installer reads entry bytes — find and reuse; if entries expose a stream, `new StreamReader(stream).ReadToEnd()`). `_ctx.Game.SaveDir` vs `_ctx.SaveDir` — line ~1083 uses `_ctx.SaveDir`; use whichever carries the saveDir. The engine gate keeps this UE-pak-only per the spec.

- [ ] **Step 3: Append config-mod rows + toggle/uninstall dispatch**

In `ReloadModsAsync` where rows are assembled, after the regular rows, append one row per `ConfigModStore.Load(_ctx.DataDir)` entry, modeled on the direct-inject row construction (`Location = "config"`, `Name = entry.Name`, `Enabled = entry.Enabled`, not a folder, no files glob). In the toggle dispatch (wherever `ModRowViewModel` toggles route — find the direct-inject branch), add a `Location == "config"` branch:

```csharp
        // Config-mod rows: toggle = snapshot-restore / re-merge via the installer, never file moves.
        if (row.Location == "config")
        {
            var entry = ConfigModStore.Load(_ctx!.DataDir).FirstOrDefault(e => e.Name == row.Name);
            if (entry is null) return;
            var configDir = ConfigMod.ResolveConfigDir(ConfigMod.ConfigRootFromSaveDir(_ctx.Game.SaveDir) ?? "");
            if (configDir is null) { SetStatus("Couldn't find the game's config folder."); return; }
            if (enable) ConfigModInstaller.Enable(entry, configDir, _ctx.DataDir);
            else ConfigModInstaller.Disable(entry, configDir, _ctx.DataDir);
            await ReloadModsAsync();
            return;
        }
```

And the uninstall path likewise routes `Location == "config"` to `ConfigModInstaller.Uninstall`. Match the real method signatures/dispatch style — report what you adapted.

- [ ] **Step 4: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` → 0 compile errors (file-lock → temp-output retry).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(config-mods): drop route + managed rows + toggle/uninstall wiring"
```

---

## Task 7: Verify + docs + smoke + PR

**Files:** `.claude/rules/camelcase-json-on-disk.md`, `docs/smoke-tests/pending.md`

- [ ] **Step 1: Full Core suite** — all pass. **CorePurity** — `--filter "FullyQualifiedName~CorePurityTests"` → pass.
- [ ] **Step 2: reversibility-auditor** — dispatch on `ConfigModInstaller.cs` (+ the App toggle wiring): snapshot precedes every write; the only delete is a file the mod created (`ExistedAtInstall:false`); mid-write failure rolls back; uninstall-after-disable doesn't double-restore. Fix any Critical/Important finding.
- [ ] **Step 3: camelCase rule doc** — add `ConfigModStore config-mods.json (src/ModManager.Core/ConfigMods/ConfigModStore.cs)` to the governed-surfaces list in `.claude/rules/camelcase-json-on-disk.md`.
- [ ] **Step 4: Smoke entry** in `docs/smoke-tests/pending.md` (match format): drop the real `Peformance Enhancer - UE - Witchfire-9-1-0-0-1727451230.zip` on Witchfire → status says merged + snapshotted → `%LOCALAPPDATA%\Witchfire\Saved\Config\WindowsNoEditor\Engine.ini` contains the mod's settings AND the pre-existing ones → a "Performance Enhancer" row appears → toggle off → file restored byte-equal to pre-install → toggle on → re-merged → uninstall → clean. Also: drop a pak+config zip → still installs as a pak mod (no config hijack).
- [ ] **Step 5: Commit docs, push, PR** — `feat/ue-config-mods → master`, title `feat(config-mods): UE config-tweak mods (Engine.ini & friends) — merge, snapshot-first, reversible`. Decision log to the dashboard (project `DP1YCsh7iAN1yAiR8sAd`): the 7th mod class, merge-not-replace, the Enable-must-not-restore-stale-snapshot subtlety, wholesale-restore bluntness accepted.

---

## Self-review notes

- **Spec coverage:** detection + config-only trigger (T1); merge semantics incl. `+`/`!` array ops, newline preservation, comment preservation (T2); registry camelCase (T3); validate→snapshot→merge→register + refusals + re-drop no-stacking (T4); disable/re-enable-re-merge/uninstall + the disabled-uninstall-no-clobber edge (T5); App drop route + rows + status line + UE-pak gate (T6); reversibility audit + camelCase rule + smoke (T7). The spec's "locked target file" edge falls out of T4's try/rollback (an IOException mid-write restores written files and surfaces); "two mods same file" chaining needs no code (snapshots are per-mod-id) — the warn-on-older-disable is NOT built (accepted: surfaced in the smoke doc as known bluntness, matches spec's honesty requirement; flagged as a conscious cut).
- **Placeholder scan:** `SetStatus`/`ReadEntryText` in T6 are explicitly named stand-ins with instructions to use the real mechanisms — judgment task by design, not a placeholder.
- **Type consistency:** `ConfigModEntry(Id, Name, Files, InstalledUtc, Enabled)` + `ConfigFileRecord(FileName, ExistedAtInstall, SnapshotPath, Payload)` consistent across T3/T4/T5/T6. `Install(payload, configDir, dataDir, modName)` consistent T4/T5/T6. `ConfigRootFromSaveDir`/`ResolveConfigDir` consistent T4/T6.
- **One deliberate cut from the spec:** the "disabling the older of two overlapping config mods warns about the newer's keys" UI warning — deferred (rare case, wholesale-restore already surfaced in the row tooltip/smoke). Listed in the PR body as a known limitation.
