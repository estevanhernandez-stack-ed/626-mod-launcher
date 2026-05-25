# Coordination Stage 3 — Config Cockpit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Read and edit a mod's structured config (`.ini` / `config.txt` / `.cfg`) with round-trip fidelity, and surface (read-only) the keybinds and console commands a mod registers in Lua.

**Architecture:** A pure-core `ModConfig` parses/edits INI-style config as a string (round-trip safe: preserves comments, order, sections), with file IO + backup in a thin Scanner wrapper. A pure-core `LuaScan` regex-extracts `RegisterKeyBind` / `RegisterConsoleCommandHandler` for read-only display. The App shows a per-mod cockpit: editable config fields (with an "owned by another tool" warning when applicable) + read-only keybind/command lists.

**Tech Stack:** .NET 10, C#, xUnit. Branch: fresh `feat/config-cockpit` off `master` (Stage 2 / PR #15 merged). Do NOT stack.

**DESIGN DECISION (user-approved, important for the safety review):** Config-VALUE edits are allowed even on a folder another tool (Vortex/MO2) owns — config is user-data and editing a setting is the file's purpose. This is a DELIBERATE, scoped exception to the owned-folder invariant, surfaced with a warning ("managed by another tool — your edit may be overwritten on its next deploy"). It does NOT touch mod *content*: the enable/disable/uninstall/intake/move/rename sinks remain fully gated. `ModConfig` writes only modify the bytes of an existing config file in place (atomic) and add nothing else to the owned folder; the backup goes to OUR data dir.

**Real formats (from the live install):**
- `config.txt` (PetBoarPlus): `#` comments, blank lines, `key = value`, no sections; rich per-setting comment blocks; includes a `dismiss_key` hotkey.
- `UE4SS-settings.ini`: `[Section]` headers, `;` comments, `key = value` (values may be empty).
- Lua: `RegisterKeyBind(Key.F3, GetObjectName)`, `RegisterKeyBind(Key.Y, {ModifierKey.CONTROL}, CreatePlayer)`, `RegisterConsoleCommandHandler("summon", function...)`; dynamic forms like `RegisterKeyBindAsync(Keybinds[name].Key, ...)` are NOT statically resolvable — skip them (best-effort, read-only).

**Test/build commands:**
- Tests: `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --nologo` (EXPLICIT project; bare `dotnet test` hangs on WinUI). Filter: `--filter "FullyQualifiedName~<Name>"`.
- App build: `dotnet build "C:\Users\estev\Projects\626-mod-launcher\src\ModManager.App\ModManager.App.csproj" -p:Platform=x64 --nologo` (kill `ModManager.App.exe` first).
- PowerShell; `git -C "C:\Users\estev\Projects\626-mod-launcher" ...`. Trailer: `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>`.

Reference: [docs/2026-05-25-mod-tool-coordination-design.md](2026-05-25-mod-tool-coordination-design.md) §4.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/ModManager.Core/ModConfig.cs` (create) | INI/config parse (entries w/ section,key,value,description), round-trip `SetValue`, `Discover`, `ReadFile` |
| `src/ModManager.Core/LuaScan.cs` (create) | regex-extract keybinds + console commands from Lua (read-only) |
| `src/ModManager.Core/Scanner.cs` (modify) | `WriteModConfig` — backup-to-data-dir + atomic write of a config file |
| `tests/ModManager.Tests/ModConfigTests.cs` (create) | parse + round-trip edit + discover |
| `tests/ModManager.Tests/LuaScanTests.cs` (create) | keybind/command extraction |
| `tests/ModManager.Tests/WriteModConfigTests.cs` (create) | backup + atomic write |
| App: `ModRowViewModel`/`MainViewModel`/`MainWindow.xaml` (modify) | per-mod cockpit panel (build-verified) |

---

## Task 1: `ModConfig.Parse` — read config entries

**Files:** Create `tests/ModManager.Tests/ModConfigTests.cs`, `src/ModManager.Core/ModConfig.cs`.

- [ ] **Step 1: Failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

// INI/config parse: section + key=value, '#' and ';' comments, each key's immediately-preceding
// contiguous comment block becomes its Description. Blank lines and section headers reset the block.
public class ModConfigTests
{
    [Fact]
    public void Parse_reads_key_value_pairs()
    {
        var entries = ModConfig.Parse("pet_name = Truffle\nenable_rename = true\n");
        Assert.Equal(2, entries.Count);
        Assert.Equal("pet_name", entries[0].Key);
        Assert.Equal("Truffle", entries[0].Value);
        Assert.Equal("true", entries[1].Value);
        Assert.Null(entries[0].Section);
    }

    [Fact]
    public void Parse_tracks_sections()
    {
        var entries = ModConfig.Parse("[Overrides]\nModsFolderPath =\n[General]\nEnableHotReloadSystem = 0\n");
        Assert.Equal("Overrides", entries[0].Section);
        Assert.Equal("ModsFolderPath", entries[0].Key);
        Assert.Equal("", entries[0].Value);
        Assert.Equal("General", entries[1].Section);
    }

    [Fact]
    public void Parse_attaches_preceding_comment_block_as_description()
    {
        var entries = ModConfig.Parse("# What name to display.\n# Keep it short.\npet_name = Truffle\n");
        Assert.Single(entries);
        Assert.Equal("What name to display. Keep it short.", entries[0].Description);
    }

    [Fact]
    public void Parse_blank_line_resets_the_comment_block()
    {
        var entries = ModConfig.Parse("# Banner divider\n\n# Real description\nfoo = bar\n");
        Assert.Equal("Real description", entries[0].Description); // banner dropped after the blank
    }

    [Fact]
    public void Parse_supports_semicolon_comments()
    {
        var entries = ModConfig.Parse("; a setting\nKey = 1\n");
        Assert.Equal("a setting", entries[0].Description);
    }

    [Fact]
    public void Parse_ignores_lines_without_equals_and_handles_crlf()
    {
        var entries = ModConfig.Parse("just a line\r\nKey = Val\r\n");
        Assert.Single(entries);
        Assert.Equal("Val", entries[0].Value);
    }
}
```

- [ ] **Step 2: Run red.** Expected FAIL — `ModConfig` missing.

- [ ] **Step 3: Implement parse**

Create `src/ModManager.Core/ModConfig.cs`:

```csharp
namespace ModManager.Core;

/// <summary>One editable config setting: its section (null for top-level), key, current value, and
/// the human description we lift from the comment lines immediately above it.</summary>
public sealed record ConfigEntry(string? Section, string Key, string Value, string Description);

/// <summary>
/// Round-trip-safe reader/editor for INI-style config (.ini / .cfg / config.txt): supports
/// '[Section]' headers, 'key = value', and '#'/';' comments. Editing changes only the target
/// value's bytes; comments, ordering, spacing, and every other line are preserved. Pure string ops;
/// file IO lives in <see cref="ReadFile"/> / <see cref="Discover"/> and the Scanner write wrapper.
/// </summary>
public static class ModConfig
{
    private static bool IsComment(string t) => t.StartsWith('#') || t.StartsWith(';');

    public static IReadOnlyList<ConfigEntry> Parse(string content)
    {
        var entries = new List<ConfigEntry>();
        string? section = null;
        var comments = new List<string>();
        foreach (var raw in content.Replace("\r\n", "\n").Split('\n'))
        {
            var t = raw.Trim();
            if (t.Length == 0) { comments.Clear(); continue; }
            if (IsComment(t)) { comments.Add(t.TrimStart('#', ';', ' ', '\t')); continue; }
            if (t.StartsWith('[') && t.EndsWith(']')) { section = t[1..^1].Trim(); comments.Clear(); continue; }
            var eq = raw.IndexOf('=');
            if (eq < 0) { comments.Clear(); continue; }
            var key = raw[..eq].Trim();
            var value = raw[(eq + 1)..].Trim();
            if (key.Length > 0) entries.Add(new ConfigEntry(section, key, value, string.Join(" ", comments)));
            comments.Clear();
        }
        return entries;
    }
}
```

- [ ] **Step 4: Run green.** Expected PASS (6).

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\estev\Projects\626-mod-launcher" add src/ModManager.Core/ModConfig.cs tests/ModManager.Tests/ModConfigTests.cs
git -C "C:\Users\estev\Projects\626-mod-launcher" commit -m "feat: ModConfig.Parse — read INI/config entries with comment descriptions

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 2: `ModConfig.SetValue` — round-trip edit

**Files:** Modify `ModConfigTests.cs`, `ModConfig.cs`.

- [ ] **Step 1: Failing tests** (append):

```csharp
    [Fact]
    public void SetValue_changes_only_the_target_value()
    {
        var src = "# desc\npet_name = Truffle\nenable_rename = true\n";
        var outp = ModConfig.SetValue(src, null, "pet_name", "Rocky");
        Assert.Contains("pet_name = Rocky", outp);
        Assert.Contains("# desc", outp);              // comment preserved
        Assert.Contains("enable_rename = true", outp); // other key untouched
    }

    [Fact]
    public void SetValue_respects_section_scoping()
    {
        var src = "[A]\nKey = 1\n[B]\nKey = 2\n";
        var outp = ModConfig.SetValue(src, "B", "Key", "9");
        Assert.Contains("[A]\r\nKey = 1", outp.Replace("\n", "\r\n").Replace("\r\r", "\r")); // A.Key untouched
        Assert.Equal("9", ModConfig.Parse(outp).First(e => e.Section == "B" && e.Key == "Key").Value);
        Assert.Equal("1", ModConfig.Parse(outp).First(e => e.Section == "A" && e.Key == "Key").Value);
    }

    [Fact]
    public void SetValue_preserves_key_indentation_and_spacing_prefix()
    {
        var outp = ModConfig.SetValue("pet_name = Truffle\n", null, "pet_name", "Rocky");
        Assert.StartsWith("pet_name =", outp); // left of '=' preserved
    }

    [Fact]
    public void SetValue_appends_when_key_absent()
    {
        var outp = ModConfig.SetValue("existing = 1\n", null, "newkey", "v");
        Assert.Equal("v", ModConfig.Parse(outp).First(e => e.Key == "newkey").Value);
        Assert.Equal("1", ModConfig.Parse(outp).First(e => e.Key == "existing").Value);
    }
```

- [ ] **Step 2: Run red.**

- [ ] **Step 3: Implement** (add to `ModConfig`):

```csharp
    /// <summary>Set a key's value in the given section (null = top-level), preserving every other byte.
    /// If the key is absent it is appended (under the section header if one is named and present).</summary>
    public static string SetValue(string content, string? section, string key, string value)
    {
        var nl = content.Contains("\r\n") ? "\r\n" : "\n";
        var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        string? cur = null;
        var sectionEndIdx = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[') && t.EndsWith(']')) { cur = t[1..^1].Trim(); if (cur == section) sectionEndIdx = i; continue; }
            if (t.Length == 0 || IsComment(t)) continue;
            var eq = lines[i].IndexOf('=');
            if (eq < 0) continue;
            if (string.Equals(cur, section, StringComparison.Ordinal) &&
                string.Equals(lines[i][..eq].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = lines[i][..(eq + 1)] + " " + value;   // keep "key =", replace RHS
                return string.Join(nl, lines);
            }
            if (cur == section) sectionEndIdx = i;
        }
        // not found -> append (after the section's last line if a section was named, else at end)
        var insert = section is not null && sectionEndIdx >= 0 ? sectionEndIdx + 1 : lines.Count;
        // trim a single trailing empty line so we don't grow blank lines unboundedly
        if (insert == lines.Count && lines.Count > 0 && lines[^1].Trim().Length == 0) insert = lines.Count - 1;
        lines.Insert(insert, $"{key} = {value}");
        return string.Join(nl, lines);
    }
```

- [ ] **Step 4: Run green** (filter), then full suite.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\estev\Projects\626-mod-launcher" add src/ModManager.Core/ModConfig.cs tests/ModManager.Tests/ModConfigTests.cs
git -C "C:\Users\estev\Projects\626-mod-launcher" commit -m "feat: ModConfig.SetValue — round-trip edit preserving comments/order/sections

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 3: `Discover` + `ReadFile` + `Scanner.WriteModConfig` (backup + atomic)

**Files:** Modify `ModConfig.cs` (add `Discover`, `ReadFile`); modify `Scanner.cs` (add `WriteModConfigAsync`); create `tests/ModManager.Tests/WriteModConfigTests.cs`.

- [ ] **Step 1: Failing tests**

Create `tests/ModManager.Tests/WriteModConfigTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class WriteModConfigTests
{
    [Fact]
    public void Discover_finds_config_files_excluding_manifest_files()
    {
        var d = TestSupport.TempDir("disc-");
        File.WriteAllText(Path.Combine(d, "config.txt"), "k = v");
        File.WriteAllText(Path.Combine(d, "settings.ini"), "[A]\nx = 1");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "");        // manifest, excluded
        File.WriteAllText(Path.Combine(d, "enabled.txt"), "");     // excluded
        File.WriteAllText(Path.Combine(d, "readme.md"), "");       // not config
        var found = ModConfig.Discover(d).Select(Path.GetFileName).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "config.txt", "settings.ini" }, found);
    }

    [Fact]
    public async Task WriteModConfig_backs_up_original_to_data_dir_then_writes()
    {
        var root = TestSupport.TempDir("wmc-");
        var modsDir = Path.Combine(root, "game", "mods", "Foo");
        Directory.CreateDirectory(modsDir);
        var cfg = Path.Combine(modsDir, "config.txt");
        File.WriteAllText(cfg, "pet_name = Truffle\n");
        var c = Scanner.GameContext(new GameEntry { Id = "t", GameName = "T", GameRoot = Path.Combine(root, "game") });

        var newContent = ModConfig.SetValue(File.ReadAllText(cfg), null, "pet_name", "Rocky");
        await Scanner.WriteModConfigAsync(cfg, newContent, c);

        Assert.Contains("pet_name = Rocky", File.ReadAllText(cfg));                 // written
        var backups = Directory.GetFiles(Path.Combine(c.DataDir, "config-backups"), "*", SearchOption.AllDirectories);
        Assert.Single(backups);                                                    // one backup
        Assert.Contains("Truffle", File.ReadAllText(backups[0]));                  // backup holds the original
    }
}
```

- [ ] **Step 2: Run red.**

- [ ] **Step 3a: Add `Discover` + `ReadFile` to `ModConfig`:**

```csharp
    private static readonly string[] ConfigExts = { ".ini", ".cfg" };
    private static readonly string[] ConfigNames = { "config.txt", "settings.txt" };
    private static readonly HashSet<string> NotConfig =
        new(StringComparer.OrdinalIgnoreCase) { "mods.txt", "enabled.txt", "mods.json" };

    /// <summary>Config files directly inside a mod folder: *.ini / *.cfg plus known names; never the
    /// UE4SS manifest files. Returns absolute paths.</summary>
    public static IReadOnlyList<string> Discover(string modDir)
    {
        try
        {
            return Directory.GetFiles(modDir)
                .Where(p =>
                {
                    var name = Path.GetFileName(p);
                    if (NotConfig.Contains(name)) return false;
                    return ConfigExts.Contains(Path.GetExtension(p).ToLowerInvariant())
                        || ConfigNames.Contains(name.ToLowerInvariant());
                })
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    public static IReadOnlyList<ConfigEntry> ReadFile(string path)
    {
        try { return Parse(File.ReadAllText(path)); } catch { return Array.Empty<ConfigEntry>(); }
    }
```

- [ ] **Step 3b: Add `WriteModConfigAsync` to `Scanner`** (place near the other public async wrappers):

```csharp
    /// <summary>
    /// Write edited config back to a mod's config file: first copy the current file to a timestamped
    /// backup under our data dir (NEVER into the mod folder), then atomically replace the file.
    /// Editing a config VALUE is allowed even in a tool-owned folder (user-data); callers warn the user.
    /// </summary>
    public static Task WriteModConfigAsync(string configPath, string content, GameContext c)
    {
        if (File.Exists(configPath))
        {
            var backupDir = Path.Combine(c.DataDir, "config-backups",
                Path.GetFileName(Path.GetDirectoryName(configPath)) ?? "mod");
            Directory.CreateDirectory(backupDir);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            File.Copy(configPath, Path.Combine(backupDir, $"{Path.GetFileName(configPath)}.{stamp}.bak"), overwrite: true);
        }
        var tmp = configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, configPath, overwrite: true);
        return Task.CompletedTask;
    }
```

- [ ] **Step 4: Run green** (filter), then full suite.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\estev\Projects\626-mod-launcher" add src/ModManager.Core/ModConfig.cs src/ModManager.Core/Scanner.cs tests/ModManager.Tests/WriteModConfigTests.cs
git -C "C:\Users\estev\Projects\626-mod-launcher" commit -m "feat: ModConfig.Discover/ReadFile + Scanner.WriteModConfig (backup to data dir, atomic)

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 4: `LuaScan` — keybinds + console commands (read-only)

**Files:** Create `tests/ModManager.Tests/LuaScanTests.cs`, `src/ModManager.Core/LuaScan.cs`.

- [ ] **Step 1: Failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

// Best-effort, read-only extraction of UE4SS Lua registrations. Static Key.X forms are captured;
// dynamic forms (RegisterKeyBindAsync(Keybinds[x].Key, ...)) are intentionally skipped.
public class LuaScanTests
{
    [Fact]
    public void Keybinds_extracts_simple_key()
    {
        var b = LuaScan.Keybinds("RegisterKeyBind(Key.F3, GetObjectName)");
        Assert.Single(b);
        Assert.Equal("F3", b[0].Key);
        Assert.Empty(b[0].Modifiers);
    }

    [Fact]
    public void Keybinds_extracts_modifiers()
    {
        var b = LuaScan.Keybinds("RegisterKeyBind(Key.Y, {ModifierKey.CONTROL}, CreatePlayer)");
        Assert.Equal("Y", b[0].Key);
        Assert.Equal(new[] { "CONTROL" }, b[0].Modifiers.ToArray());
    }

    [Fact]
    public void Keybinds_skips_dynamic_registration()
    {
        var b = LuaScan.Keybinds("RegisterKeyBindAsync(Keybinds[name].Key, Keybinds[name].ModifierKeys, cb)");
        Assert.Empty(b);
    }

    [Fact]
    public void Commands_extracts_quoted_names()
    {
        var c = LuaScan.Commands("RegisterConsoleCommandHandler(\"summon\", function() end)\nRegisterConsoleCommandHandler(\"set\", f)");
        Assert.Equal(new[] { "summon", "set" }, c.Select(x => x.Name).ToArray());
    }

    [Fact]
    public void ScanFolder_aggregates_across_lua_files()
    {
        var d = TestSupport.TempDir("lua-");
        Directory.CreateDirectory(Path.Combine(d, "Scripts"));
        File.WriteAllText(Path.Combine(d, "Scripts", "main.lua"), "RegisterKeyBind(Key.INS, f)\nRegisterConsoleCommandHandler(\"dump\", g)");
        var (binds, cmds) = LuaScan.ScanFolder(d);
        Assert.Contains(binds, x => x.Key == "INS");
        Assert.Contains(cmds, x => x.Name == "dump");
    }
}
```

- [ ] **Step 2: Run red.**

- [ ] **Step 3: Implement**

Create `src/ModManager.Core/LuaScan.cs`:

```csharp
using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>A keybind a UE4SS Lua mod registers statically (Key.NAME + optional ModifierKey.X list).</summary>
public sealed record LuaKeyBind(string Key, IReadOnlyList<string> Modifiers);
/// <summary>A console command a UE4SS Lua mod registers.</summary>
public sealed record LuaConsoleCommand(string Name);

/// <summary>
/// Best-effort, READ-ONLY extraction of UE4SS Lua registrations for display. Static `Key.NAME`
/// keybinds and quoted console-command names are captured; dynamic/computed forms are skipped
/// (we never guess). Pure regex over file text — no Lua execution.
/// </summary>
public static class LuaScan
{
    private static readonly Regex KeyBindRe =
        new(@"RegisterKeyBind\s*\(\s*Key\.(\w+)\s*(?:,\s*\{([^}]*)\})?", RegexOptions.Compiled);
    private static readonly Regex CmdRe =
        new("RegisterConsoleCommandHandler\\s*\\(\\s*\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex ModRe = new(@"ModifierKey\.(\w+)", RegexOptions.Compiled);

    public static IReadOnlyList<LuaKeyBind> Keybinds(string lua)
    {
        var result = new List<LuaKeyBind>();
        foreach (Match m in KeyBindRe.Matches(lua))
        {
            var mods = new List<string>();
            if (m.Groups[2].Success)
                foreach (Match mm in ModRe.Matches(m.Groups[2].Value)) mods.Add(mm.Groups[1].Value);
            result.Add(new LuaKeyBind(m.Groups[1].Value, mods));
        }
        return result;
    }

    public static IReadOnlyList<LuaConsoleCommand> Commands(string lua)
        => CmdRe.Matches(lua).Select(m => new LuaConsoleCommand(m.Groups[1].Value)).ToList();

    public static (IReadOnlyList<LuaKeyBind> Keybinds, IReadOnlyList<LuaConsoleCommand> Commands) ScanFolder(string modDir)
    {
        var binds = new List<LuaKeyBind>();
        var cmds = new List<LuaConsoleCommand>();
        IEnumerable<string> files;
        try { files = Directory.GetFiles(modDir, "*.lua", SearchOption.AllDirectories); }
        catch { return (binds, cmds); }
        foreach (var f in files)
        {
            string t;
            try { t = File.ReadAllText(f); } catch { continue; }
            binds.AddRange(Keybinds(t));
            cmds.AddRange(Commands(t));
        }
        return (binds, cmds);
    }
}
```

- [ ] **Step 4: Run green** (filter), then full suite.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\estev\Projects\626-mod-launcher" add src/ModManager.Core/LuaScan.cs tests/ModManager.Tests/LuaScanTests.cs
git -C "C:\Users\estev\Projects\626-mod-launcher" commit -m "feat: LuaScan — read-only keybind + console-command extraction from UE4SS Lua

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 5: App cockpit panel (build-verified)

**Files:** `src/ModManager.App/ViewModels/ModRowViewModel.cs`, `MainViewModel.cs`, `MainWindow.xaml`. No headless test (UI) — the Core tests above prove the data; verify by build + manual smoke.

The cockpit is a per-mod expandable panel that shows: (1) editable config fields (key, value textbox, description as tooltip/subtext) for each `ConfigEntry` from each discovered config file; (2) a read-only list of keybinds (`Key` + modifiers); (3) a read-only list of console commands. Saving a field calls `Scanner.WriteModConfigAsync`. When `Mod.ReadOnly` (owned), show a warning line: "Managed by {Managed} — edits may be overwritten on its next deploy." (Editing is still allowed per the design decision.)

- [ ] **Step 1: Add a cockpit view-model.** In `ModRowViewModel.cs`, expose lazy cockpit data (only built when the panel opens, to avoid scanning every mod up front). Add:

```csharp
    // Cockpit (config + Lua surfacing). Built on demand by the parent VM, which holds the GameContext.
    public string ModFolderAbs { get; init; } = "";   // set by parent: the mod's folder (for folder mods)
    public bool HasCockpit => IsFolder && !string.IsNullOrEmpty(ModFolderAbs);
    public string OwnedConfigWarning =>
        ReadOnly && !string.IsNullOrEmpty(Mod.Managed)
            ? $"Managed by {Mod.Managed!.ToUpperInvariant()} — config edits may be overwritten on its next deploy."
            : "";
    public Visibility OwnedConfigWarningVisibility =>
        string.IsNullOrEmpty(OwnedConfigWarning) ? Visibility.Collapsed : Visibility.Visible;
```

(Parent sets `ModFolderAbs` when constructing rows: for a folder mod, `Path.Combine(LocByName(m.Location, ctx).Abs, m.Name)`.)

- [ ] **Step 2: Wire data assembly in `MainViewModel`.** Add a method the panel calls on expand:

```csharp
    public sealed record CockpitConfigFile(string FileName, string Path, IReadOnlyList<ConfigEntry> Entries);

    public (IReadOnlyList<CockpitConfigFile> Configs, IReadOnlyList<LuaKeyBind> Keybinds, IReadOnlyList<LuaConsoleCommand> Commands)
        BuildCockpit(string modFolderAbs)
    {
        var configs = ModConfig.Discover(modFolderAbs)
            .Select(p => new CockpitConfigFile(System.IO.Path.GetFileName(p), p, ModConfig.ReadFile(p)))
            .ToList();
        var (binds, cmds) = LuaScan.ScanFolder(modFolderAbs);
        return (configs, binds, cmds);
    }

    public async Task SaveConfigValueAsync(string configPath, string? section, string key, string value)
    {
        var content = System.IO.File.ReadAllText(configPath);
        var updated = ModConfig.SetValue(content, section, key, value);
        await Scanner.WriteModConfigAsync(configPath, updated, _ctx!);
        StatusText = $"Saved {key} in {System.IO.Path.GetFileName(configPath)}.";
    }
```

- [ ] **Step 3: XAML.** Add an expander/flyout to the mod row (mirror the existing readme/flyout pattern in `MainWindow.xaml`) showing the config fields (bound to `ConfigEntry` — key label, value `TextBox`, description as subtext via `textContent`-equivalent binding), the `OwnedConfigWarning` line, and two read-only `ItemsControl`s for keybinds (`Key` + modifiers) and commands (`Name` + a Copy button). Render all mod-supplied strings via bound text (never raw HTML). Match existing theme resources.

- [ ] **Step 4: Build** — `Stop-Process -Name ModManager.App -Force -ErrorAction SilentlyContinue; dotnet build "...ModManager.App.csproj" -p:Platform=x64 --nologo`. Expected `0 Error(s)`.

- [ ] **Step 5: Commit**
```bash
git -C "C:\Users\estev\Projects\626-mod-launcher" add src/ModManager.App
git -C "C:\Users\estev\Projects\626-mod-launcher" commit -m "feat: per-mod config cockpit — edit config (owned-folder warning) + view keybinds/commands

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

## Task 6: Full verification + push

- [ ] **Step 1:** Full suite green: `dotnet test "...ModManager.Tests.csproj" --nologo` (only 7z/rar SKIPs).
- [ ] **Step 2:** App builds x64, 0 errors.
- [ ] **Step 3:** Push `git -C "C:\Users\estev\Projects\626-mod-launcher" push -u origin feat/config-cockpit`; open PR #16 vs `master`.

---

## Deferred to a follow-on (NOT this plan)
- **Lua keybind EDITING + safe-hotkey conflict detection** across all binds (this pass surfaces them read-only).
- **`UE4SS-settings.ini` global editor** as a first-class screen (the parser already handles it; this pass scopes to per-mod config).

## Self-Review
**Spec coverage (§4):** INI/cfg round-trip read+edit → Tasks 1-3; atomic + backup → Task 3; Lua keybind + console-command surfacing (read-only) → Task 4; cockpit UI + owned-folder warning → Task 5. Lua editing + hotkey-conflict explicitly deferred. ✅
**Owned-folder design decision** is documented at the top and in `WriteModConfigAsync`'s comment; the mod-content invariant is untouched (no change to enable/disable/uninstall/intake). ✅
**Placeholder scan:** none — full code in core tasks; the UI task (no headless test) gives concrete VM code + a precise XAML description following existing patterns. ✅
**Type consistency:** `ConfigEntry(Section?, Key, Value, Description)`, `ModConfig.Parse/SetValue/Discover/ReadFile`, `Scanner.WriteModConfigAsync(path, content, c)`, `LuaScan.Keybinds/Commands/ScanFolder`, `LuaKeyBind(Key, Modifiers)`, `LuaConsoleCommand(Name)` — consistent across tasks. ✅
