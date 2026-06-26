# Request a game Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Per-task implement → review → fix gated, then a whole-branch review. Steps use `- [ ]`.

**Goal:** When a user adds a game whose engine the launcher can't detect, give them a one-click "Request this game" action that opens a prefilled GitHub issue against the `626-game-manifest` feed repo — closing the coverage loop with zero manual typing.

**Architecture:** A pure Core URL-builder (`GameRequestUrl.Build`) constructs the prefilled `issues/new` URL — escaping, the template's exact field ids, and the engine-key→dropdown-option mapping — and is fully unit-tested. The App surfaces a "Request this game" affordance in `AddGameDialog`, visible when the engine isn't auto-detected, that builds the URL from the dialog's live state and opens it via `Windows.System.Launcher.LaunchUriAsync` — mirroring the existing `FrameworkUnrecognizedNudgeDialog` precedent.

**Tech Stack:** .NET 10 / C# (Core: pure + xUnit; App: WinUI 3). Both flavors (no `#if FULL`).

## Global Constraints

- **Both STORE and FULL** — no `#if FULL` anywhere in this feature. The add-game path has no flavor gating today; keep it that way. Verify the STORE build still seals (`scripts/check-store-seal.ps1`).
- **The feed repo is `estevanhernandez-stack-ed/626-game-manifest`** (NOT the launcher repo). The issue template is `game-request.yml`; it auto-applies the `game-request` label (don't add `&labels=`). Field ids (verified against the live template): `name` (required), `steam-app-id`, `store-link`, `engine` (dropdown, required), `nexus`, `notes`.
- **The engine dropdown is a required field with fixed options** — a prefill value that isn't an exact option string is silently dropped. The Core builder MUST map our engine key to the exact option string, defaulting to `Not sure` (which satisfies the required field for the undetected case). The option strings, verbatim from `game-request.yml`: `Not sure`, `ue-pak (Unreal .pak)`, `bethesda (Creation Engine — esp/esl/bsa)`, `bepinex (Unity — BepInEx)`, `melonloader (Unity — MelonLoader)`, `smapi (Stardew)`, `source (Source engine — vpk)`, `fromsoft (Souls / Mod Engine)`, `minecraft (jar mods)`, `custom / other`. (The em-dash is U+2014.) Keep this map in sync with the template.
- **URL safety** — the builder returns an `https://github.com/...` URL; the App opens it through `Windows.System.Launcher.LaunchUriAsync` (the dialog precedent). All dynamic values escaped via `Uri.EscapeDataString`.
- **Never bare `dotnet` at repo root.** Core tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. App build: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (FULL) + `-p:Configuration=Store` (STORE); kill `ModManager.App` first. Conventional commits + trailer `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`. Branch `feat/request-a-game`.

---

## Task 1: `GameRequestUrl` builder (Core, TDD)

**Files:**
- Create: `src/ModManager.Core/GameRequestUrl.cs`
- Test: `tests/ModManager.Tests/GameRequestUrlTests.cs`

**Interfaces:**
- Consumes: nothing (pure).
- Produces: `static class GameRequestUrl { string Build(string name, string? steamAppId, string? engineKey, string? notes); }` returning a prefilled `https` issue URL. Internally maps `engineKey` → the template's dropdown option string (default `Not sure`).

- [ ] **Step 1: Write the failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class GameRequestUrlTests
{
    [Fact]
    public void Build_targets_the_feed_repo_template_and_prefills_name_and_title()
    {
        var url = GameRequestUrl.Build("Baldur's Gate 3", "1086940", null, null);
        Assert.StartsWith("https://github.com/estevanhernandez-stack-ed/626-game-manifest/issues/new?", url);
        Assert.Contains("template=game-request.yml", url);
        // name + title escaped (apostrophe + spaces)
        Assert.Contains("name=Baldur%27s%20Gate%203", url);
        Assert.Contains("title=%5Bgame%5D%20Baldur%27s%20Gate%203", url);
        Assert.Contains("steam-app-id=1086940", url);
        Assert.True(SafeUrl.IsHttpUrl(url));
    }

    [Fact]
    public void Build_defaults_engine_to_Not_sure_when_unknown_and_omits_blank_fields()
    {
        var url = GameRequestUrl.Build("Some Game", null, null, null);
        Assert.Contains("engine=Not%20sure", url);     // required field — always set
        Assert.DoesNotContain("steam-app-id=", url);    // omitted when blank
        Assert.DoesNotContain("notes=", url);           // omitted when blank
    }

    [Theory]
    [InlineData("fromsoft", "fromsoft%20(Souls%20%2F%20Mod%20Engine)")]
    [InlineData("ue-pak", "ue-pak%20(Unreal%20.pak)")]
    [InlineData("bethesda", "bethesda%20(Creation%20Engine%20%E2%80%94%20esp%2Fesl%2Fbsa)")]
    [InlineData("minecraft", "minecraft%20(jar%20mods)")]
    [InlineData("custom", "custom%20%2F%20other")]
    [InlineData("totally-unknown", "Not%20sure")]
    public void Build_maps_engine_key_to_the_exact_dropdown_option(string key, string expectedEncoded)
    {
        var url = GameRequestUrl.Build("G", null, key, null);
        Assert.Contains($"engine={expectedEncoded}", url);
    }

    [Fact]
    public void Build_includes_notes_when_present()
    {
        var url = GameRequestUrl.Build("G", null, null, "Mod path: Mods");
        Assert.Contains("notes=Mod%20path%3A%20Mods", url);
    }
}
```

- [ ] **Step 2: Run them, verify they fail** — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter GameRequestUrl` → FAIL (GameRequestUrl not defined).
- [ ] **Step 3: Implement `src/ModManager.Core/GameRequestUrl.cs`:**

```csharp
namespace ModManager.Core;

/// <summary>Builds a prefilled GitHub issue URL against the 626-game-manifest feed repo's
/// game-request.yml issue form. Pure + escaped — the App just opens the returned URL. The engine
/// dropdown is a REQUIRED form field whose options are fixed strings; a prefill value that isn't an
/// exact option is silently dropped, so we map our engine key to the option verbatim (default
/// "Not sure"). Keep <see cref="EngineOption"/> in sync with .github/ISSUE_TEMPLATE/game-request.yml
/// in the feed repo.</summary>
public static class GameRequestUrl
{
    private const string Base =
        "https://github.com/estevanhernandez-stack-ed/626-game-manifest/issues/new";

    // Exact dropdown option strings from game-request.yml (em-dash = U+2014). Keep in sync.
    private static string EngineOption(string? engineKey) => engineKey switch
    {
        "ue-pak"      => "ue-pak (Unreal .pak)",
        "bethesda"    => "bethesda (Creation Engine — esp/esl/bsa)",
        "bepinex"     => "bepinex (Unity — BepInEx)",
        "melonloader" => "melonloader (Unity — MelonLoader)",
        "smapi"       => "smapi (Stardew)",
        "source"      => "source (Source engine — vpk)",
        "fromsoft"    => "fromsoft (Souls / Mod Engine)",
        "minecraft"   => "minecraft (jar mods)",
        "custom"      => "custom / other",
        _             => "Not sure",
    };

    public static string Build(string name, string? steamAppId, string? engineKey, string? notes)
    {
        var q = new List<string>
        {
            "template=game-request.yml",
            "title=" + Esc("[game] " + name),
            "name=" + Esc(name),
            "engine=" + Esc(EngineOption(engineKey)),   // required field — always set
        };
        if (!string.IsNullOrWhiteSpace(steamAppId)) q.Add("steam-app-id=" + Esc(steamAppId));
        if (!string.IsNullOrWhiteSpace(notes)) q.Add("notes=" + Esc(notes));
        return Base + "?" + string.Join("&", q);
    }

    private static string Esc(string s) => Uri.EscapeDataString(s);
}
```

- [ ] **Step 4: Run the tests, verify they pass** — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter GameRequestUrl` → PASS (all cases). Note: `Uri.EscapeDataString` encodes space as `%20`, `'` as `%27`, `/` as `%2F`, `[`/`]` as `%5B`/`%5D`, `:` as `%3A`, em-dash as `%E2%80%94` — matching the test expectations.
- [ ] **Step 5: Run the full Core suite** — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` → all green.
- [ ] **Step 6: Commit** — `feat(request): GameRequestUrl builder for prefilled feed-repo issues`.

## Task 2: "Request this game" affordance in AddGameDialog (App)

**Files:**
- Modify: `src/ModManager.App/AddGameDialog.xaml` (add a "Request this game" button/hyperlink near the engine row)
- Modify: `src/ModManager.App/AddGameDialog.xaml.cs` (visibility toggle in `ApplyDetectedEngine` ~line 349; a click handler that builds + opens the URL)
- Modify: `docs/smoke-tests/pending.md`

**Interfaces:**
- Consumes: `GameRequestUrl.Build` (Task 1); the dialog's live state — `NameBox.Text`, `SteamBox.Text`, `EngineBox.SelectedItem as EngineOption` (`.Key`, null when unselected), `ModPathBox.Text`; the `FrameworkUnrecognizedNudgeDialog` open-URL precedent (`Windows.System.Launcher.LaunchUriAsync`).
- Produces: nothing downstream (terminal UI affordance).

- [ ] **Step 1:** In `AddGameDialog.xaml`, near the engine picker row (the same area as `EngineHint`), add a `HyperlinkButton` named `RequestGameLink` with content "Can't find the engine? Request this game" and `Visibility="Collapsed"` by default, `Click="OnRequestGame"`. Match the dialog's existing spacing/style (mirror how `EngineHint` is laid out).
- [ ] **Step 2:** In `AddGameDialog.xaml.cs` `ApplyDetectedEngine()` (~line 349-366), set the link's visibility as the inverse of detection: where `EngineHint.Visibility = Visible` is set (engine detected) also set `RequestGameLink.Visibility = Collapsed`; in the `else` branch (no engine resolved) set `RequestGameLink.Visibility = Visible`. So the request affordance appears exactly in the request-worthy state.
- [ ] **Step 3:** Add the click handler in `AddGameDialog.xaml.cs`:

```csharp
private void OnRequestGame(object sender, RoutedEventArgs e)
{
    var name = NameBox.Text?.Trim();
    if (string.IsNullOrWhiteSpace(name)) return;   // name is the one required field
    var engineKey = (EngineBox.SelectedItem as EngineOption)?.Key;   // null when unselected -> "Not sure"
    var modPath = ModPathBox.Text?.Trim();
    var notes = string.IsNullOrWhiteSpace(modPath) ? null : $"Mod path: {modPath}";
    var url = ModManager.Core.GameRequestUrl.Build(name, SteamBox.Text?.Trim(), engineKey, notes);
    if (ModManager.Core.SafeUrl.IsHttpUrl(url))
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url));
}
```

- [ ] **Step 4: Build both flavors** — `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (FULL) then `-p:Configuration=Store` (STORE). Both 0 errors. Run `pwsh scripts/check-store-seal.ps1` → seal OK (no new forbidden symbols — `GameRequestUrl`/`SafeUrl` are benign).
- [ ] **Step 5: Smoke entry** — append to `docs/smoke-tests/pending.md`: add a game whose engine isn't detected (a non-catalog title, or click "Set up" on an undetected Steam game) → a "Request this game" link appears; clicking it opens the browser to the `626-game-manifest` new-issue page with the game name, Steam id, and `Not sure` engine prefilled, `game-request` label applied. For a game where an engine IS picked, the link is hidden (detection succeeded).
- [ ] **Step 6: Commit** — `feat(request): surface "Request this game" in AddGameDialog when engine is undetected`.

## Self-review

- **Spec coverage** (growth spec Phase 2 — request line): undetected-engine detection (`plan.Addable == false && plan.Engine == null` / `EngineHint` collapsed) → Task 2 visibility toggle. Prefilled issue against the feed repo with the real field ids + the required engine dropdown defaulting to `Not sure` → Task 1. Mirror the nudge-dialog open-URL pattern, both flavors, no `#if FULL` → Task 2 + Global Constraints.
- **Correctness pin:** the engine map values are copied verbatim from the live `game-request.yml` (em-dash U+2014); the `[InlineData]` cases lock the exact encodings so drift from the template fails the test.
- **No placeholders:** Task 1 is full TDD code with the exact escaped expectations; Task 2 carries the exact XAML element + handler against grounded line numbers (the dialog UI is verified by build + seal + smoke, since WinUI dialogs aren't unit-testable).
- **Type consistency:** `GameRequestUrl.Build(name, steamAppId, engineKey, notes)` signature matches between Task 1 (definition) and Task 2 (call). `EngineOption.Key` is the existing dialog type (per the grounding).
- **Positional-ctor risk:** none — `AddGameDialog`'s constructor is unchanged; the affordance reads the dialog's own controls, no new ctor param (the single call site at `MainWindow.xaml.cs:329` is untouched).
- **Scope note (not a task):** this first cut surfaces "request" only from `AddGameDialog`'s undetected-engine state; surfacing it elsewhere (e.g. a right-click on an already-added unknown game) is a future add — out of scope here.
