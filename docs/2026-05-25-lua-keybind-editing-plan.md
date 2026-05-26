# Lua Keybind Editing + Safe-Hotkey Conflicts — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]`.

**Goal:** Make the Lua-hardcoded keybinds the cockpit surfaces *editable* (remap the key), with conflict detection so two binds never silently share a key. Config-driven keybinds are already editable via the config cockpit; this covers the ones hardcoded in Lua (`RegisterKeyBind(Key.F3, ...)`).

**Architecture:** `Hotkeys.Conflicts` (pure) flags keys bound more than once. `LuaScan` gains a `SourceFile` on each keybind (so we know which file to edit) and a pure `RemapKeyBind` that rewrites one bind's key token. The cockpit shows an editable key per Lua bind with a conflict marker; saving reads the file, remaps, backs up the original, and writes atomically. Confident parses only — anything ambiguous stays read-only (never guess-edit Lua).

**Tech Stack:** .NET 10, C#, xUnit. Branch: `feat/lua-keybind-editing` off `master` (already checked out).

**Test/build:** `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --nologo` (EXPLICIT). App: `dotnet build "...ModManager.App.csproj" -p:Platform=x64 --nologo`. PowerShell; `git -C "C:\Users\estev\Projects\626-mod-launcher" ...`. Trailer: `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>`.

Reference: `src/ModManager.Core/LuaScan.cs` (existing `LuaKeyBind`, `Keybinds`, `ScanFolder`), `MainWindow.xaml.cs` `OnShowCockpit` (where keybinds render read-only today), `Scanner.WriteModConfigAsync` (the backup-to-data-dir + atomic-write pattern to mirror).

---

## Task 1: `Hotkeys.Conflicts` — detect duplicate bindings

**Files:** Create `tests/ModManager.Tests/HotkeysTests.cs`, `src/ModManager.Core/Hotkeys.cs`.

- [ ] **Step 1: Failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class HotkeysTests
{
    private static LuaKeyBind K(string key, params string[] mods) => new(key, mods);

    [Fact]
    public void Conflicts_flags_a_key_bound_more_than_once()
    {
        var c = Hotkeys.Conflicts(new[] { K("F3"), K("F4"), K("F3") });
        Assert.Contains(Hotkeys.Signature(K("F3")), c);
        Assert.DoesNotContain(Hotkeys.Signature(K("F4")), c);
    }

    [Fact]
    public void Conflicts_treats_modifiers_as_part_of_the_combo()
    {
        // Ctrl+Y and plain Y do NOT conflict; two Ctrl+Y do.
        var c = Hotkeys.Conflicts(new[] { K("Y", "CONTROL"), K("Y"), K("Y", "CONTROL") });
        Assert.Contains(Hotkeys.Signature(K("Y", "CONTROL")), c);
        Assert.DoesNotContain(Hotkeys.Signature(K("Y")), c);
    }

    [Fact]
    public void Signature_is_case_and_order_insensitive_for_modifiers()
        => Assert.Equal(Hotkeys.Signature(K("y", "shift", "control")), Hotkeys.Signature(K("Y", "CONTROL", "SHIFT")));

    [Fact]
    public void Conflicts_empty_when_all_unique() => Assert.Empty(Hotkeys.Conflicts(new[] { K("F1"), K("F2") }));
}
```

- [ ] **Step 2: Run red.**

- [ ] **Step 3: Implement** `src/ModManager.Core/Hotkeys.cs`:

```csharp
namespace ModManager.Core;

/// <summary>Safe-hotkey helpers: a canonical signature for a key combo and detection of combos
/// bound more than once (so the UI can warn before two binds silently shadow each other). Pure.</summary>
public static class Hotkeys
{
    /// <summary>Canonical "KEY+MOD+MOD" signature — key upper-cased, modifiers upper-cased + sorted.</summary>
    public static string Signature(LuaKeyBind b)
    {
        var mods = b.Modifiers.Select(m => m.ToUpperInvariant()).OrderBy(x => x, StringComparer.Ordinal);
        return b.Key.ToUpperInvariant() + (b.Modifiers.Count > 0 ? "+" + string.Join("+", mods) : "");
    }

    /// <summary>Signatures that appear on more than one bind — i.e. real conflicts.</summary>
    public static IReadOnlySet<string> Conflicts(IEnumerable<LuaKeyBind> binds)
    {
        var counts = new Dictionary<string, int>();
        foreach (var b in binds) { var s = Signature(b); counts[s] = counts.GetValueOrDefault(s) + 1; }
        return counts.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToHashSet();
    }
}
```

- [ ] **Step 4: Run green** (filter), then full suite.
- [ ] **Step 5: Commit** `feat: Hotkeys.Conflicts — flag key combos bound more than once`

---

## Task 2: `LuaScan` source-file tracking + `RemapKeyBind`

**Files:** Modify `src/ModManager.Core/LuaScan.cs`; add tests to `tests/ModManager.Tests/LuaScanTests.cs`.

- [ ] **Step 1: Failing tests** (append):

```csharp
    [Fact]
    public void RemapKeyBind_rewrites_only_the_target_key()
    {
        var lua = "RegisterKeyBind(Key.F3, GetObjectName)\nRegisterKeyBind(Key.F4, Other)";
        var outp = LuaScan.RemapKeyBind(lua, "F3", System.Array.Empty<string>(), "F6");
        Assert.Contains("RegisterKeyBind(Key.F6, GetObjectName)", outp);
        Assert.Contains("RegisterKeyBind(Key.F4, Other)", outp); // sibling untouched
    }

    [Fact]
    public void RemapKeyBind_matches_modifiers()
    {
        var lua = "RegisterKeyBind(Key.Y, {ModifierKey.CONTROL}, CreatePlayer)\nRegisterKeyBind(Key.Y, DoThing)";
        // Remap the CTRL+Y one; the plain Y stays.
        var outp = LuaScan.RemapKeyBind(lua, "Y", new[] { "CONTROL" }, "U");
        Assert.Contains("RegisterKeyBind(Key.U, {ModifierKey.CONTROL}, CreatePlayer)", outp);
        Assert.Contains("RegisterKeyBind(Key.Y, DoThing)", outp);
    }

    [Fact]
    public void RemapKeyBind_no_match_returns_input_unchanged()
    {
        var lua = "RegisterKeyBind(Key.F3, X)";
        Assert.Equal(lua, LuaScan.RemapKeyBind(lua, "Z", System.Array.Empty<string>(), "F6"));
    }

    [Fact]
    public void ScanFolder_records_each_keybind_source_file()
    {
        var d = TestSupport.TempDir("luasrc-");
        Directory.CreateDirectory(Path.Combine(d, "Scripts"));
        var f = Path.Combine(d, "Scripts", "main.lua");
        File.WriteAllText(f, "RegisterKeyBind(Key.F3, X)");
        var (binds, _) = LuaScan.ScanFolder(d);
        Assert.Equal(f, binds.Single().SourceFile);
    }
```

- [ ] **Step 2: Run red.**

- [ ] **Step 3: Implement.**
- Add `SourceFile` to the record (optional, default null so `Keybinds(lua)` callers are unaffected):
  ```csharp
  public sealed record LuaKeyBind(string Key, IReadOnlyList<string> Modifiers, string? SourceFile = null);
  ```
- In `ScanFolder`, set `SourceFile` for each bind it adds (it already iterates files — carry the path):
  ```csharp
  binds.AddRange(Keybinds(t).Select(b => b with { SourceFile = f }));
  ```
- Add `RemapKeyBind`. Match the FIRST `RegisterKeyBind(Key.{fromKey} ...)` whose modifier set equals `fromMods`, and replace just `Key.{fromKey}` -> `Key.{toKey}` in that call:
  ```csharp
  /// <summary>Rewrite the key of the first RegisterKeyBind matching (fromKey, fromMods). Only the
  /// Key.X token is changed; everything else (callback, modifiers, surrounding code) is untouched.
  /// Returns the input unchanged if no confident match. Caller backs up the file before writing.</summary>
  public static string RemapKeyBind(string lua, string fromKey, IReadOnlyList<string> fromMods, string toKey)
  {
      var want = fromMods.Select(m => m.ToUpperInvariant()).OrderBy(x => x).ToList();
      foreach (Match m in KeyBindRe.Matches(lua))
      {
          if (!string.Equals(m.Groups[1].Value, fromKey, StringComparison.OrdinalIgnoreCase)) continue;
          var mods = new List<string>();
          if (m.Groups[2].Success)
              foreach (Match mm in ModRe.Matches(m.Groups[2].Value)) mods.Add(mm.Groups[1].Value.ToUpperInvariant());
          if (!want.SequenceEqual(mods.OrderBy(x => x))) continue;
          // Replace the "Key.{from}" inside this match only.
          var keyTokenStart = m.Index + m.Value.IndexOf("Key." + m.Groups[1].Value, StringComparison.Ordinal);
          var keyToken = "Key." + m.Groups[1].Value;
          return lua[..keyTokenStart] + "Key." + toKey + lua[(keyTokenStart + keyToken.Length)..];
      }
      return lua;
  }
  ```
  (`KeyBindRe`, `ModRe` already exist in LuaScan. Confirm `KeyBindRe` group 1 = key, group 2 = the optional `{...}` modifier block. Adjust the token-replace if the existing regex differs.)

- [ ] **Step 4: Run green** (filter), then full suite.
- [ ] **Step 5: Commit** `feat: LuaScan source-file tracking + RemapKeyBind (targeted key rewrite)`

---

## Task 3: Cockpit — editable keybinds + conflict marker + safe write

**Files:** Modify `src/ModManager.App/MainWindow.xaml.cs` (`OnShowCockpit`), `src/ModManager.App/ViewModels/MainViewModel.cs` (a remap method). Build-verified.

The cockpit already lists keybinds (read-only). Make each Lua bind with a `SourceFile` editable: a small key `TextBox` (or ComboBox of common keys) prefilled with the current key, a conflict marker when its signature is in `Hotkeys.Conflicts(allBinds)`, and a Save. Binds without a `SourceFile` stay read-only.

- [ ] **Step 1: VM method** in `MainViewModel.cs`:

```csharp
/// <summary>Remap a Lua-hardcoded keybind: back up the source .lua, rewrite the one key token,
/// write atomically. No-op (with a status note) if the rewrite finds no confident match.</summary>
public async Task RemapKeyBindAsync(LuaKeyBind bind, string newKey)
{
    if (_ctx is null || string.IsNullOrEmpty(bind.SourceFile) || string.IsNullOrWhiteSpace(newKey)) return;
    try
    {
        var lua = System.IO.File.ReadAllText(bind.SourceFile);
        var updated = LuaScan.RemapKeyBind(lua, bind.Key, bind.Modifiers, newKey.Trim());
        if (updated == lua) { StatusText = $"Couldn't find {bind.Key} to remap (left unchanged)."; return; }
        await Scanner.WriteModConfigAsync(bind.SourceFile, updated, _ctx); // reuse: backup-to-data-dir + atomic
        StatusText = $"Remapped {bind.Key} -> {newKey.Trim().ToUpperInvariant()}. Restart the mod/UE4SS to apply.";
    }
    catch (Exception e) { StatusText = $"Couldn't remap {bind.Key}: {e.Message}"; }
}
```

(`Scanner.WriteModConfigAsync` already backs the original up to the data dir and writes atomically with temp cleanup — reuse it for the .lua too; it's a generic safe text write.)

- [ ] **Step 2: Cockpit UI** in `OnShowCockpit` — read the existing keybind-list rendering and replace the read-only key text with: a `TextBox` (Width ~80) prefilled with `bind.Key`, the modifiers shown as a read-only prefix (e.g. "CTRL +"), a conflict glyph/tooltip when `conflicts.Contains(Hotkeys.Signature(bind))`, and a "Set" button → `await ViewModel.RemapKeyBindAsync(bind, box.Text)` then rebuild the cockpit. Compute `var conflicts = Hotkeys.Conflicts(keybinds);` once. Binds with `SourceFile == null` (dynamic/unparsed) render read-only as today. Render all mod-supplied strings as TEXT only.

- [ ] **Step 3: Build** the App (x64), 0 errors.
- [ ] **Step 4: Commit** `feat: editable Lua keybinds in the cockpit with conflict warnings (backup + atomic)`

---

## Task 4: Verify + push
- [ ] Full suite green (only 7z/rar SKIPs). App builds x64.
- [ ] Push `feat/lua-keybind-editing`; open PR vs `master`.

## Deferred
- Editing modifiers (add/remove Ctrl/Shift) — this pass remaps the key only.
- Cross-mod conflict detection across the whole game (this pass detects within the mod's surfaced binds; the cockpit is per-mod).

## Self-Review
**Coverage:** conflict detection (T1), source-file + targeted rewrite (T2), editable cockpit + safe write (T3). Modifier editing + cross-mod conflicts deferred. ✅
**Safety:** Lua write goes through `WriteModConfigAsync` (backup + atomic + temp cleanup); RemapKeyBind is confident-match-only (no match -> unchanged, never a blind edit); conflict surfaced before the user commits. ✅
**Type consistency:** `LuaKeyBind(Key, Modifiers, SourceFile?)`, `Hotkeys.Signature/Conflicts`, `LuaScan.RemapKeyBind(lua, fromKey, fromMods, toKey)`, `MainViewModel.RemapKeyBindAsync(LuaKeyBind, string)`. ✅
