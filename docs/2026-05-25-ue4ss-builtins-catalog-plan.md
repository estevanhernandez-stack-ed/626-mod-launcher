# UE4SS Built-in Catalog — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]`.

**Goal:** The UE4SS framework folders that ship with UE4SS (`BPModLoaderMod`, `ConsoleEnablerMod`, `shared`, …) currently read as bare/unidentified. Bundle a curated catalog so they show a proper title + description + a "UE4SS BUILT-IN" badge + a docs link — offline, no API.

**Architecture:** Pure-core `Ue4ssBuiltins` is a static curated table (folder name → title/description/docs URL). `Mod.Builtin` is set during the scan for UE4SS folder mods that match the catalog. `Metadata.MergeMetadata` applies the catalog as a FALLBACK only when a mod has no real CF/Nexus metadata (real metadata always wins). The App shows a "UE4SS BUILT-IN" badge.

**Tech Stack:** .NET 10, C#, xUnit. Branch: fresh `feat/ue4ss-builtins` off `master` (PR #18 merged). No stacking. No file/IO risk (pure catalog + display).

**Test/build:** `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --nologo` (EXPLICIT project). App: `dotnet build "...ModManager.App.csproj" -p:Platform=x64 --nologo`. PowerShell; `git -C "C:\Users\estev\Projects\626-mod-launcher" ...`. Trailer: `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>`.

---

## Task 1: `Ue4ssBuiltins` catalog

**Files:** Create `tests/ModManager.Tests/Ue4ssBuiltinsTests.cs`, `src/ModManager.Core/Ue4ssBuiltins.cs`.

- [ ] **Step 1: Failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class Ue4ssBuiltinsTests
{
    [Theory]
    [InlineData("BPModLoaderMod")]
    [InlineData("ConsoleEnablerMod")]
    [InlineData("Keybinds")]
    [InlineData("shared")]
    [InlineData("bpmodloadermod")]   // case-insensitive
    public void IsBuiltin_recognizes_ue4ss_framework_folders(string name)
        => Assert.True(Ue4ssBuiltins.IsBuiltin(name));

    [Theory]
    [InlineData("PetBoarPlus")]
    [InlineData("ExpandedPickupRadius1.2-134-1-2-1776872771")]
    [InlineData("")]
    public void IsBuiltin_is_false_for_user_mods(string name)
        => Assert.False(Ue4ssBuiltins.IsBuiltin(name));

    [Fact]
    public void Lookup_returns_title_description_and_docs_for_a_builtin()
    {
        var b = Ue4ssBuiltins.Lookup("BPModLoaderMod");
        Assert.NotNull(b);
        Assert.False(string.IsNullOrWhiteSpace(b!.Title));
        Assert.False(string.IsNullOrWhiteSpace(b.Description));
        Assert.StartsWith("https://", b.DocsUrl);
    }

    [Fact]
    public void Lookup_is_null_for_a_user_mod() => Assert.Null(Ue4ssBuiltins.Lookup("PetBoarPlus"));
}
```

- [ ] **Step 2: Run red.**

- [ ] **Step 3: Implement**

Create `src/ModManager.Core/Ue4ssBuiltins.cs`:

```csharp
namespace ModManager.Core;

/// <summary>Curated info for a UE4SS framework mod that ships with the loader (not a downloaded mod).</summary>
public sealed record Ue4ssBuiltin(string Title, string Description, string DocsUrl);

/// <summary>
/// Bundled catalog of the mods that ship inside UE4SS itself (its default Mods folder). These have
/// no CurseForge/Nexus page, so we describe them from the UE4SS docs rather than leave them bare.
/// Keyed by folder name, case-insensitive. Pure data — no IO.
/// </summary>
public static class Ue4ssBuiltins
{
    private const string Docs = "https://docs.ue4ss.com";

    private static readonly Dictionary<string, Ue4ssBuiltin> Catalog = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BPModLoaderMod"] = new("Blueprint Mod Loader",
            "Loads Blueprint (LogicMods) pak mods. Required for blueprint mods to load. Ships with UE4SS.", Docs),
        ["BPML_GenericFunctions"] = new("BP ModLoader Generic Functions",
            "Helper library used by the Blueprint Mod Loader. Ships with UE4SS.", Docs),
        ["ConsoleEnablerMod"] = new("Console Enabler",
            "Enables the in-game Unreal console. Ships with UE4SS.", Docs),
        ["ConsoleCommandsMod"] = new("Console Commands",
            "Adds extra UE4SS console commands. Ships with UE4SS.", Docs),
        ["CheatManagerEnablerMod"] = new("Cheat Manager Enabler",
            "Enables Unreal's CheatManager so cheat commands work. Ships with UE4SS.", Docs),
        ["LineTraceMod"] = new("Line Trace",
            "Debug tool: line-traces to identify the object under your crosshair. Ships with UE4SS.", Docs),
        ["SplitScreenMod"] = new("Split Screen",
            "Enables local split-screen support. Ships with UE4SS.", Docs),
        ["Keybinds"] = new("Keybinds",
            "UE4SS's built-in keybind registration. Ships with UE4SS.", Docs),
        ["ActorDumperMod"] = new("Actor Dumper",
            "Debug tool: dumps actor data. Ships with UE4SS.", Docs),
        ["jsbLuaProfilerMod"] = new("Lua Profiler",
            "Profiles Lua mod performance. Ships with UE4SS.", Docs),
        ["shared"] = new("Shared (UE4SS internal)",
            "Shared Lua library imported by other UE4SS mods — not a standalone mod.", Docs),
    };

    public static bool IsBuiltin(string name) => !string.IsNullOrEmpty(name) && Catalog.ContainsKey(name);

    public static Ue4ssBuiltin? Lookup(string name)
        => !string.IsNullOrEmpty(name) && Catalog.TryGetValue(name, out var b) ? b : null;
}
```

- [ ] **Step 4: Run green** (filter), then full suite.
- [ ] **Step 5: Commit** `feat: Ue4ssBuiltins — bundled catalog of UE4SS framework mods`

---

## Task 2: `Mod.Builtin` + scan tag + metadata fallback

**Files:** Modify `src/ModManager.Core/Mod.cs`, `src/ModManager.Core/Scanner.cs` (BuildModList folder branch), `src/ModManager.Core/Metadata.cs` (MergeMetadata); create `tests/ModManager.Tests/BuiltinMetadataTests.cs`.

- [ ] **Step 1: Failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class BuiltinMetadataTests
{
    [Fact]
    public async Task BuildModList_tags_ue4ss_builtin_folders()
    {
        var root = TestSupport.TempDir("bi-");
        var mods = Path.Combine(root, "ue4ss", "Mods");
        Directory.CreateDirectory(Path.Combine(mods, "ConsoleEnablerMod"));
        Directory.CreateDirectory(Path.Combine(mods, "PetBoarPlus"));
        File.WriteAllText(Path.Combine(mods, "mods.txt"), "ConsoleEnablerMod : 1\n");
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            ModLocations = new[] { new ModLocation("ue4ss", "UE4SS", "ue4ss/Mods") { Form = "folders" } },
        });
        var list = await Scanner.BuildModListAsync(c);
        Assert.True(list.First(m => m.Name == "ConsoleEnablerMod").Builtin);
        Assert.False(list.First(m => m.Name == "PetBoarPlus").Builtin);
    }

    [Fact]
    public void MergeMetadata_fills_builtin_description_when_no_real_metadata()
    {
        var mod = new Mod { Name = "ConsoleEnablerMod", Base = "ConsoleEnablerMod", Builtin = true };
        var merged = Metadata.MergeMetadata(new[] { mod }, null).First();
        Assert.Equal("Console Enabler", merged.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(merged.Description));
        Assert.StartsWith("https://", merged.Source);
    }

    [Fact]
    public void MergeMetadata_real_metadata_wins_over_builtin_catalog()
    {
        var mod = new Mod { Name = "ConsoleEnablerMod", Base = "ConsoleEnablerMod", Builtin = true };
        var map = new Dictionary<string, ModMeta> { ["ConsoleEnablerMod"] = new ModMeta { Title = "Real Title", Description = "real" } };
        var merged = Metadata.MergeMetadata(new[] { mod }, map).First();
        Assert.Equal("Real Title", merged.DisplayName);   // CF/Nexus wins
        Assert.Equal("real", merged.Description);
    }
}
```

- [ ] **Step 2: Run red.**

- [ ] **Step 3a: `Mod.Builtin`** — in `src/ModManager.Core/Mod.cs`, after `public string? Loader { get; set; }`:

```csharp
    // True for a UE4SS framework folder that ships with the loader (described from the bundled catalog).
    public bool Builtin { get; set; }
```

- [ ] **Step 3b: Tag in `BuildModList`** — in `Scanner.cs`, the folder branch `new Mod { ... }` (the one with `Loader = isUe4ss ? "ue4ss" : null`), add:

```csharp
                        Builtin = isUe4ss && Ue4ssBuiltins.IsBuiltin(f),
```

- [ ] **Step 3c: Fallback in `MergeMetadata`** — in `src/ModManager.Core/Metadata.cs`, after the block that sets `m.DisplayName`/`m.Description`/… from `e` and `m.HasMeta = e is not null;` (around line 55-64), insert a built-in fallback BEFORE `list.Add(m)`:

```csharp
            if (e is null && m.Builtin && Ue4ssBuiltins.Lookup(m.Name) is { } b)
            {
                m.BaseTitle = b.Title;
                m.DisplayName = !string.IsNullOrEmpty(m.Variant) ? $"{b.Title} ({m.Variant})" : b.Title;
                m.Description = b.Description;
                m.Source = b.DocsUrl;   // shows as the source/docs link
                m.HasMeta = true;        // so the credit line (with the docs link) renders
            }
```

- [ ] **Step 4: Run green** (filter), then full suite (confirm no regression — existing MergeMetadata tests for non-builtin mods unaffected since `m.Builtin` defaults false).
- [ ] **Step 5: Commit** `feat: tag + describe UE4SS built-in mods (scan flag + metadata fallback)`

---

## Task 3: "UE4SS BUILT-IN" badge (App, build-verified)

**Files:** `src/ModManager.App/ViewModels/ModRowViewModel.cs`, `MainWindow.xaml`.

- [ ] **Step 1: VM** — add to `ModRowViewModel.cs` (near the Managed/VARIANT badge members):

```csharp
    // Ships with UE4SS (framework mod) — shown with a quiet built-in badge, described from the catalog.
    public bool IsBuiltin => Mod.Builtin;
    public Visibility BuiltinVisibility => Mod.Builtin ? Visibility.Visible : Visibility.Collapsed;
```

- [ ] **Step 2: XAML** — in `MainWindow.xaml`, in the chip `StackPanel` (where the `ManagedVisibility`/`VARIANT` badges live), add a quiet badge mirroring the managed-badge style (ThemePanel background, ThemeInkSoft border), bound to `BuiltinVisibility`:

```xml
                            <Border CornerRadius="3" Padding="6,2" Background="{StaticResource ThemePanel}"
                                    BorderThickness="1" BorderBrush="{StaticResource ThemeInkSoft}"
                                    ToolTipService.ToolTip="Ships with UE4SS — framework mod, not a downloaded mod"
                                    Visibility="{x:Bind BuiltinVisibility, Mode=OneWay}">
                                <TextBlock Text="UE4SS BUILT-IN" FontFamily="Consolas" FontSize="11" Opacity="0.7" />
                            </Border>
```

(Read the existing badge block first and match placement/style; the description + docs link already render via the existing Description/Source bindings now that MergeMetadata fills them.)

- [ ] **Step 3: Build** the App (x64), 0 errors.
- [ ] **Step 4: Commit** `feat: UE4SS BUILT-IN badge on framework mod rows`

---

## Task 4: Verify + push
- [ ] Full suite green (only 7z/rar SKIPs). App builds x64, 0 errors.
- [ ] Push `feat/ue4ss-builtins`; open PR vs `master`.

## Self-Review
**Coverage:** catalog (T1), scan tag + metadata fallback with real-metadata-wins precedence (T2), badge (T3). ✅
**No IO/safety risk:** pure catalog + display; no file writes, no scan-path mutation beyond setting a bool. Owned-folder invariant untouched. ✅
**Type consistency:** `Ue4ssBuiltins.IsBuiltin(string)->bool`, `Lookup(string)->Ue4ssBuiltin?`, `Ue4ssBuiltin(Title,Description,DocsUrl)`, `Mod.Builtin` (bool), `ModRowViewModel.BuiltinVisibility`. MergeMetadata fallback guarded by `e is null && m.Builtin` so CF/Nexus always wins. ✅
