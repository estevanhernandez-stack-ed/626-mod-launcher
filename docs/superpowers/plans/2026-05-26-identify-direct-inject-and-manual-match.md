# Direct-inject identify + audit + manual match — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:subagent-driven-development`. Steps use checkbox (`- [ ]`) syntax. Test command: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (NEVER bare `dotnet test` — hangs building WinUI). Build command: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Kill the running app first if the build complains about a locked Core DLL.

**Goal:** Execute the three-layer design at `docs/superpowers/specs/2026-05-26-identify-direct-inject-and-manual-match-design.md`:
(1) Elden Ring direct-inject mods get identified at drop time AND backfill time; (2) the per-engine identify-status audit doc names the structural gaps; (3) every row gains a "Match to a mod…" right-click escape hatch backed by URL paste + a manual-wins merge rule. Pure-core changes first (TDD), then VM glue, then the WinUI dialog, then the audit doc.

**Architecture:** Layer 1 turns `DirectInject.Catalog` into an archive recognizer (`MatchSignaturesInZip`), then `Scanner.Md5IdentifyArchivesAsync` branches on `c.Exts` emptiness — fromsoft uses the catalog matcher, everyone else stays on `ZipModKeys`. Layer 3 adds an `IsManual` field to `ModMeta` and a short-circuit at the top of `MergeMeta` so a manual entry locks the row against future auto-identify clobber. The URL parser is a new pure helper covering both Nexus and CurseForge mod-page URLs.

**Tech Stack:** .NET 10, xUnit, WinUI 3. No new NuGets.

---

## Task 1: Core — `ModMeta.IsManual` + manual-wins `MergeMeta`

**Files:**
- Modify: `src/ModManager.Core/Mod.cs` (one field on `ModMeta`)
- Modify: `src/ModManager.Core/Scanner.cs` (two-line guard at the top of `MergeMeta`)
- Create: `tests/ModManager.Tests/ManualMatchMergeTests.cs`

The merge today is field-by-field "existing wins per-field." That's manual-friendly except in `Md5IdentifyArchivesAsync` (Scanner.cs:1161), which intentionally inverts the parameter order so Nexus md5 wins over existing CF data. A manually-matched row would get clobbered by a later backfill there.

Fix: add an `IsManual` flag; have `MergeMeta` short-circuit when either side carries it. Auto-identify keeps its "Nexus authoritative" semantic for non-manual entries; manual locks the row.

- [ ] **Step 1: Write the failing tests**

```csharp
using ModManager.Core;
using System.Reflection;

namespace ModManager.Tests;

public class ManualMatchMergeTests
{
    // Use reflection to call the private MergeMeta — it's internal-by-design, but these tests pin
    // the manual-wins semantic against silent regressions in future refactors.
    private static ModMeta CallMergeMeta(ModMeta cf, ModMeta? curated)
    {
        var m = typeof(Scanner).GetMethod("MergeMeta",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (ModMeta)m.Invoke(null, new object?[] { cf, curated })!;
    }

    [Fact]
    public void Manual_curated_locks_the_row_against_incoming_cf()
    {
        var existing = new ModMeta { Title = "Manually Picked", Image = "manual.png", IsManual = true };
        var incoming = new ModMeta { Title = "Auto Found",      Image = "auto.png" };
        var merged = CallMergeMeta(cf: incoming, curated: existing);
        Assert.Equal("Manually Picked", merged.Title);
        Assert.Equal("manual.png", merged.Image);
        Assert.True(merged.IsManual);
    }

    [Fact]
    public void Manual_cf_side_locks_the_row_too()
    {
        // Md5IdentifyArchivesAsync passes existing as cf. Cover that direction.
        var existing = new ModMeta { Title = "Manually Picked", IsManual = true };
        var incoming = new ModMeta { Title = "Nexus Auto Match" };
        var merged = CallMergeMeta(cf: existing, curated: incoming);
        Assert.Equal("Manually Picked", merged.Title);
        Assert.True(merged.IsManual);
    }

    [Fact]
    public void Non_manual_merge_keeps_existing_field_wins_semantic()
    {
        var existing = new ModMeta { Title = "Existing Title" };
        var incoming = new ModMeta { Title = "Incoming Title", Image = "incoming.png" };
        var merged = CallMergeMeta(cf: incoming, curated: existing);
        Assert.Equal("Existing Title", merged.Title);   // existing per-field wins
        Assert.Equal("incoming.png", merged.Image);     // existing didn't have one — incoming fills
        Assert.False(merged.IsManual);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compile-fail counts as red)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManualMatchMergeTests"`
Expected: FAIL — `IsManual` field doesn't exist yet.

- [ ] **Step 3: Add the `IsManual` field**

In `src/ModManager.Core/Mod.cs`, add to `ModMeta`:

```csharp
/// <summary>True when this entry was set by the user via the manual-match dialog. Auto-identify
/// (Nexus md5, CF fingerprint, name search) never clobbers a manual entry — the row is locked
/// to whatever the user pasted, even when a later rescan would match the same key.</summary>
public bool IsManual { get; set; }
```

Place it right after `Category` so the field order is stable for JSON serialization.

- [ ] **Step 4: Short-circuit `MergeMeta`**

In `src/ModManager.Core/Scanner.cs`, update `MergeMeta`:

```csharp
private static ModMeta MergeMeta(ModMeta cf, ModMeta? curated)
{
    // Manual entries lock the row. Auto-identify (Nexus md5 / CF fingerprint / name search) never
    // overrides what the user pasted via "Match to a mod…". Covers both parameter directions —
    // Scanner.cs has call sites with existing on either side.
    if (curated?.IsManual == true) return curated;
    if (cf.IsManual) return cf;

    if (curated is null) return cf;
    return new ModMeta
    {
        Title = curated.Title ?? cf.Title,
        Description = curated.Description ?? cf.Description,
        Author = curated.Author ?? cf.Author,
        AuthorUrl = curated.AuthorUrl ?? cf.AuthorUrl,
        Url = curated.Url ?? cf.Url,
        Source = curated.Source ?? cf.Source,
        Donate = curated.Donate ?? cf.Donate,
        Image = curated.Image ?? cf.Image,
        Downloads = curated.Downloads ?? cf.Downloads,
        CurseforgeId = curated.CurseforgeId ?? cf.CurseforgeId,
        Category = curated.Category ?? cf.Category,
    };
}
```

(Also added `Category` to the merge body — it's an existing field that wasn't being merged, drive-by fix.)

- [ ] **Step 5: Run tests to verify they pass**

Expected: 3/3 PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/Mod.cs src/ModManager.Core/Scanner.cs tests/ModManager.Tests/ManualMatchMergeTests.cs
git commit -m "feat: ModMeta.IsManual locks the row against auto-identify clobber"
```

---

## Task 2: Core — `DirectInject.MatchSignaturesInZip`

**Files:**
- Modify: `src/ModManager.Core/DirectInject.cs`
- Create: `tests/ModManager.Tests/DirectInjectMatchSignaturesTests.cs`

The existing `DirectInject.Catalog` recognizes installed mods by on-disk signatures (Files / Dirs / FileContains). Mirror that recognition against an archive's entry list so we can map a zip to the catalog-named DI mods it installs.

- [ ] **Step 1: Write the failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class DirectInjectMatchSignaturesTests
{
    [Fact]
    public void Recognizes_Seamless_Co_op_archive()
    {
        // A representative Seamless Co-op archive layout: ersc.dll + ersc_settings.ini at root + a
        // seamlesscoop folder for assets + the launcher exe.
        var entries = new[]
        {
            "ersc.dll",
            "ersc_settings.ini",
            "launch_elden_ring_seamlesscoop.exe",
            "seamlesscoop/.gitkeep",
            "readme.md",
        };
        var matches = DirectInject.MatchSignaturesInZip(entries);
        Assert.Contains("Seamless Co-op", matches);
    }

    [Fact]
    public void Recognizes_ReShade_archive_by_files_and_dir()
    {
        var entries = new[]
        {
            "reshade.ini",
            "reshadepreset.ini",
            "reshade-shaders/Shaders/CRT.fx",
            "reshade-shaders/Textures/lut.png",
        };
        var matches = DirectInject.MatchSignaturesInZip(entries);
        Assert.Contains("ReShade", matches);
    }

    [Fact]
    public void Recognizes_Modded_regulation_bin_archive()
    {
        var matches = DirectInject.MatchSignaturesInZip(new[] { "regulation.bin", "readme.txt" });
        Assert.Contains("Modded regulation.bin", matches);
    }

    [Fact]
    public void Matches_FileContains_pattern_for_ultrawide_filenames_that_vary()
    {
        // Ultrawide mods ship as ULTRAWIDESCREENFIX.DLL, EldenRing_Ultrawide.dll, WidescreenFix.dll...
        // Verify the FileContains fragment hits regardless of the exact name.
        var matches = DirectInject.MatchSignaturesInZip(new[] { "EldenRing_Ultrawide.dll" });
        Assert.Contains("Ultrawide / Widescreen Fix", matches);
    }

    [Fact]
    public void Returns_distinct_results_when_multiple_signatures_match()
    {
        var entries = new[]
        {
            "ersc.dll", "ersc_settings.ini",  // Seamless Co-op
            "regulation.bin",                  // Modded regulation.bin
        };
        var matches = DirectInject.MatchSignaturesInZip(entries);
        Assert.Contains("Seamless Co-op", matches);
        Assert.Contains("Modded regulation.bin", matches);
        Assert.Equal(matches.Count, matches.Distinct().Count());
    }

    [Fact]
    public void Empty_or_unrecognized_archive_returns_empty()
    {
        Assert.Empty(DirectInject.MatchSignaturesInZip(Array.Empty<string>()));
        Assert.Empty(DirectInject.MatchSignaturesInZip(new[] { "random.txt", "screenshot.png" }));
    }

    [Fact]
    public void Case_insensitive_filename_match()
    {
        // Some uploaders ship UPPERCASE filenames. The on-disk recognizer is case-insensitive on
        // Windows; the archive-name recognizer must be too.
        var matches = DirectInject.MatchSignaturesInZip(new[] { "ERSC.DLL", "ERSC_SETTINGS.INI" });
        Assert.Contains("Seamless Co-op", matches);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~DirectInjectMatchSignaturesTests"`
Expected: FAIL — `MatchSignaturesInZip` doesn't exist.

- [ ] **Step 3: Implement `MatchSignaturesInZip`**

In `src/ModManager.Core/DirectInject.cs`, add (as a `public static`, near the `Catalog`):

```csharp
/// <summary>
/// Recognize which catalog-named direct-inject mods a zip archive INSTALLS, by running the same
/// signature rules (<see cref="Signature"/>) the on-disk recognizer uses against the archive's
/// entry list. Case-insensitive. Returns distinct mod names that matched; empty if nothing did.
/// Pure - takes the entry names only, no IO.
/// </summary>
public static IReadOnlyList<string> MatchSignaturesInZip(IEnumerable<string> zipEntryNames)
{
    var entries = (zipEntryNames ?? Enumerable.Empty<string>())
        .Where(n => !string.IsNullOrEmpty(n))
        .Select(n => n.Replace('\\', '/'))
        .ToList();
    if (entries.Count == 0) return Array.Empty<string>();

    // Pre-compute the things each Signature predicate looks at.
    var basenamesLower = entries
        .Select(n => System.IO.Path.GetFileName(n).ToLowerInvariant())
        .Where(n => n.Length > 0)
        .ToHashSet();
    var dirSegmentsLower = entries
        .SelectMany(n => n.Split('/', StringSplitOptions.RemoveEmptyEntries).SkipLast(1))
        .Select(s => s.ToLowerInvariant())
        .ToHashSet();

    var hits = new List<string>();
    foreach (var sig in Catalog)
    {
        if (sig.Files.Any(f => basenamesLower.Contains(f.ToLowerInvariant()))) { hits.Add(sig.Name); continue; }
        if (sig.Dirs.Any(d => dirSegmentsLower.Contains(d.ToLowerInvariant())))  { hits.Add(sig.Name); continue; }
        if (sig.FileContains.Any(f =>
            basenamesLower.Any(b => b.Contains(f, StringComparison.OrdinalIgnoreCase))))
        {
            hits.Add(sig.Name);
        }
    }
    return hits.Distinct().ToList();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Expected: 7/7 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/DirectInject.cs tests/ModManager.Tests/DirectInjectMatchSignaturesTests.cs
git commit -m "feat: DirectInject.MatchSignaturesInZip turns the on-disk catalog into an archive recognizer"
```

---

## Task 3: Core — `Md5IdentifyArchivesAsync` fromsoft branch

**Files:**
- Modify: `src/ModManager.Core/Scanner.cs` (one branch in `Md5IdentifyArchivesAsync`)

The existing method uses `ZipModKeys(zip, c)` to get keys from the archive. For fromsoft, `c.Exts` is empty → `ZipModKeys` returns nothing → metadata gets attached to nothing. Branch on `c.Exts` emptiness: empty → use the catalog matcher; non-empty → today's path unchanged.

- [ ] **Step 1: Read the current method body**

In `src/ModManager.Core/Scanner.cs`, find `Md5IdentifyArchivesAsync` (around line 1140). The body that needs the branch is the inner `foreach (var key in ZipModKeys(path, c))` line.

- [ ] **Step 2: Add the branch**

Replace the inner `foreach` block with a branched key source:

```csharp
// Get the mod keys this archive INSTALLS. Extension-based engines (pak/dll/jar) name mods
// after their files, so ZipModKeys (filter by c.Exts + strip variants) is right. Catalog-based
// engines (fromsoft direct-inject — c.Exts empty) name mods from DirectInject.Catalog, so
// fall back to the signature matcher against the archive's entries.
IReadOnlyList<string> keys;
if (c.Exts.Count > 0)
{
    keys = ZipModKeys(path, c);
}
else
{
    using var zipForKeys = Archive.Open(path);
    keys = DirectInject.MatchSignaturesInZip(zipForKeys.EntryNames);
}

foreach (var key in keys)
{
    // A Nexus archive-md5 match is exact provenance (this file IS the Nexus upload),
    // so it is AUTHORITATIVE: Nexus identity (title/author/url/image) wins over any
    // existing CurseForge match; CF only fills the fields Nexus lacks (downloads,
    // source-code link). This is what makes backfill override a CF-won collision.
    // Manual matches (ModMeta.IsManual) still lock the row — MergeMeta short-circuits.
    meta[key] = MergeMeta(meta.GetValueOrDefault(key) ?? new ModMeta(), match.Meta);
    matchedKeys.Add(key);
}
```

- [ ] **Step 3: Write a test for the fromsoft branch**

Create `tests/ModManager.Tests/Md5IdentifyFromsoftTests.cs`:

```csharp
using System.IO;
using System.IO.Compression;
using ModManager.Core;

namespace ModManager.Tests;

public class Md5IdentifyFromsoftTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "md5fs-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    [Fact]
    public async Task Fromsoft_archive_md5_match_attaches_metadata_via_DirectInject_catalog()
    {
        // Build a tiny Seamless Co-op archive. The Nexus client is stubbed to return a known
        // ModMeta for ANY md5 — we're testing the FROMSOFT BRANCH (catalog-based keys), not the
        // wire format.
        Directory.CreateDirectory(_root);
        var archive = Path.Combine(_root, "Seamless.zip");
        using (var fs = File.Create(archive))
        using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(z.CreateEntry("ersc.dll").Open())) w.Write("x");
            using (var w = new StreamWriter(z.CreateEntry("ersc_settings.ini").Open())) w.Write("x");
        }

        var ctx = TestContexts.MinimalFromSoftContext(_root);
        var stub = new StubNexus(("any", new ModMeta { Title = "Seamless Co-op (Nexus)" }));

        var result = await Scanner.Md5IdentifyArchivesAsync(ctx, stub, new[] { archive });

        Assert.Equal(1, result.Matched);
        var meta = TestContexts.LoadMetadata(ctx);
        Assert.True(meta.ContainsKey("Seamless Co-op"), "expected key 'Seamless Co-op' from DI catalog");
        Assert.Equal("Seamless Co-op (Nexus)", meta["Seamless Co-op"].Title);
    }
}
```

The test depends on a `TestContexts.MinimalFromSoftContext(dir)` helper and a `StubNexus` implementing `INexusClient`. If those don't exist, create them in `tests/ModManager.Tests/TestContexts.cs` (or extend the existing helpers) — they should return a `GameContext` with `Game.Engine = "fromsoft"`, `Game.NexusGameDomain = "eldenring"`, empty `Exts`, a real `DataDir` rooted at `dir`. The stub returns the given `ModMeta` for any `GetByMd5Async` call.

If the test helpers are non-trivial to author, the implementer should stop and ask before fabricating the surface.

- [ ] **Step 4: Run + verify**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~Md5IdentifyFromsoftTests"`
Expected: 1/1 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Scanner.cs tests/ModManager.Tests/
git commit -m "feat: Md5IdentifyArchivesAsync recognizes fromsoft direct-inject archives via catalog"
```

---

## Task 4: Core — Nexus + CurseForge URL parser

**Files:**
- Create: `src/ModManager.Core/ModSiteUrl.cs`
- Create: `tests/ModManager.Tests/ModSiteUrlTests.cs`

The manual-match dialog accepts a Nexus mod URL or a CF mod URL and parses out (`provider`, `gameDomain`, `modId-or-slug`). Pure URL parsing — no HTTP.

- [ ] **Step 1: Write the failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class ModSiteUrlTests
{
    [Theory]
    [InlineData("https://www.nexusmods.com/eldenring/mods/510", "eldenring", 510)]
    [InlineData("https://nexusmods.com/skyrimspecialedition/mods/12345", "skyrimspecialedition", 12345)]
    [InlineData("https://www.nexusmods.com/eldenring/mods/510?tab=description", "eldenring", 510)]
    [InlineData("https://www.nexusmods.com/eldenring/mods/510/", "eldenring", 510)]
    public void Parse_Nexus_url(string url, string expectedDomain, int expectedModId)
    {
        var p = ModSiteUrl.Parse(url);
        Assert.NotNull(p);
        Assert.Equal(ModSiteProvider.Nexus, p!.Provider);
        Assert.Equal(expectedDomain, p.GameKey);
        Assert.Equal(expectedModId.ToString(), p.ModRef);
    }

    [Theory]
    [InlineData("https://www.curseforge.com/eldenring/mods/seamless-coop", "eldenring", "seamless-coop")]
    [InlineData("https://curseforge.com/minecraft/mc-mods/jei", "minecraft", "jei")]
    [InlineData("https://www.curseforge.com/eldenring/mods/seamless-coop/files", "eldenring", "seamless-coop")]
    public void Parse_CurseForge_url(string url, string expectedGameSlug, string expectedModSlug)
    {
        var p = ModSiteUrl.Parse(url);
        Assert.NotNull(p);
        Assert.Equal(ModSiteProvider.CurseForge, p!.Provider);
        Assert.Equal(expectedGameSlug, p.GameKey);
        Assert.Equal(expectedModSlug, p.ModRef);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("https://example.com/something")]
    [InlineData("https://nexusmods.com/")]
    [InlineData("https://nexusmods.com/eldenring/")]
    [InlineData("https://nexusmods.com/eldenring/mods/")]
    [InlineData("https://nexusmods.com/eldenring/mods/notanumber")]
    public void Parse_returns_null_for_unrecognized_input(string url)
    {
        Assert.Null(ModSiteUrl.Parse(url));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement the parser**

Create `src/ModManager.Core/ModSiteUrl.cs`:

```csharp
namespace ModManager.Core;

public enum ModSiteProvider { Nexus, CurseForge }

/// <summary>The pieces a manual-match URL paste resolves to: which site, which game on that site,
/// which mod (numeric id for Nexus, slug for CurseForge).</summary>
public sealed record ModSiteUrlParts(ModSiteProvider Provider, string GameKey, string ModRef);

/// <summary>
/// Pure URL parser for Nexus Mods and CurseForge mod-page URLs. Tolerant of trailing slashes,
/// query strings, the optional <c>www.</c> subdomain, and extra path segments (e.g. <c>/files</c>).
/// Returns null when the URL isn't a recognized mod page.
/// </summary>
public static class ModSiteUrl
{
    public static ModSiteUrlParts? Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return null;
        if (u.Scheme is not "http" and not "https") return null;

        var host = u.Host.ToLowerInvariant().TrimStart('.');
        if (host.StartsWith("www.")) host = host[4..];

        // Trim leading/trailing slashes; split into segments.
        var segments = u.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3) return null;

        // Nexus: /<gameDomain>/mods/<id>(/.../?...)
        if (host == "nexusmods.com")
        {
            if (!segments[1].Equals("mods", StringComparison.OrdinalIgnoreCase)) return null;
            if (!int.TryParse(segments[2], out var id)) return null;
            return new ModSiteUrlParts(ModSiteProvider.Nexus, segments[0].ToLowerInvariant(), id.ToString());
        }

        // CurseForge: /<gameSlug>/mods/<modSlug>(/...)  OR /<gameSlug>/mc-mods/<modSlug>
        if (host == "curseforge.com")
        {
            if (segments.Length < 3) return null;
            // accept "mods" or game-specific path like "mc-mods"
            var bucket = segments[1].ToLowerInvariant();
            if (bucket is not "mods" and not "mc-mods") return null;
            var slug = segments[2].ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(slug)) return null;
            return new ModSiteUrlParts(ModSiteProvider.CurseForge, segments[0].ToLowerInvariant(), slug);
        }

        return null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Expected: 7+ Theory cases PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/ModSiteUrl.cs tests/ModManager.Tests/ModSiteUrlTests.cs
git commit -m "feat: ModSiteUrl parses Nexus + CurseForge mod-page URLs for the manual-match flow"
```

---

## Task 5: VM — direct-inject identifies + `ManualMatchAsync`

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs`

Two pieces:

**5a — Direct-inject branch fires the three identifies after install.** Mirrors the regular intake branch verbatim.

In `AddModsAsync`, find the `if (DirectInjectBacked)` block. After the existing `await ReloadModsAsync();` line and BEFORE the `StatusText = $"..."` line, insert:

```csharp
// Identify what just got installed — same chain the regular intake branch uses. Direct-inject
// mods are named from DirectInject.Catalog (e.g. "Seamless Co-op"); Md5IdentifyArchivesAsync's
// fromsoft branch maps the archive's md5 → Nexus → those catalog names. Best-effort: a Nexus
// miss / outage / unreachable CF proxy never breaks the install that already succeeded.
var identified = 0;
var nexusIdentified = 0;
if (r.Added.Count > 0 || r.Updated.Count > 0)
{
    try { identified = (await Scanner.FingerprintIdentifyAsync(_ctx, _svc.CurseForge, r.Added.Concat(r.Updated))).Matched; }
    catch { }
    try { if (_nexus.IsConnected) nexusIdentified = (await Scanner.Md5IdentifyArchivesAsync(_ctx, _nexus.Client!, paths)).Matched; }
    catch { }
    try { await Scanner.RefreshMetadataByNameAsync(_ctx, _svc.CurseForge); }
    catch { }
    if (identified > 0 || nexusIdentified > 0) await ReloadModsAsync();
}
```

Update the existing direct-inject `StatusText` to surface the identify result, mirroring the regular branch's pattern:

```csharp
StatusText = $"Updated {r.Updated.Count}, added {r.Added.Count}, skipped {r.Skipped.Count}"
    + (r.Updated.Count > 0 ? " — old versions kept, revert anytime." : "")
    + (identified > 0 ? $". Identified {identified} on CurseForge" : "")
    + (nexusIdentified > 0 ? $", {nexusIdentified} on Nexus" : "");
```

**5b — `ManualMatchAsync(ModRowViewModel row, string url)`.** Parses the URL, calls the right provider, writes the metadata with `IsManual = true`. Surface result via `StatusText`.

Add anywhere near `BackfillNexusAsync` (it lives next to the other identify-style methods):

```csharp
/// <summary>Manual-match escape hatch: user pastes a Nexus or CurseForge URL for a row whose
/// auto-identify didn't land. We parse it, fetch metadata from the named provider, write it
/// against this mod's key with IsManual=true so future rescans can't clobber it. Result via
/// StatusText.</summary>
public async Task<bool> ManualMatchAsync(ModRowViewModel row, string url)
{
    if (_ctx is null) return false;
    var parts = ModSiteUrl.Parse(url);
    if (parts is null)
    {
        StatusText = "That doesn't look like a Nexus or CurseForge mod URL.";
        return false;
    }

    try
    {
        ModMeta? hit = parts.Provider switch
        {
            ModSiteProvider.Nexus when _nexus.IsConnected =>
                await _nexus.Client!.GetModAsync(parts.GameKey, int.Parse(parts.ModRef)),
            ModSiteProvider.Nexus =>
                throw new InvalidOperationException("Connect Nexus first (Settings → Nexus Mods)."),
            ModSiteProvider.CurseForge =>
                await Scanner.LookupCurseForgeSlugAsync(_svc.CurseForge, parts.GameKey, parts.ModRef),
            _ => null,
        };
        if (hit is null)
        {
            StatusText = $"Couldn't find that mod on {parts.Provider}.";
            return false;
        }
        hit.IsManual = true;
        Scanner.WriteOneMeta(_ctx, row.Mod.Name, hit);
        await ReloadModsAsync();
        StatusText = $"Matched \"{row.DisplayName}\" to {hit.Title ?? "the pasted URL"}.";
        return true;
    }
    catch (Exception e) { StatusText = e.Message; return false; }
}
```

This depends on two new Scanner helpers — `LookupCurseForgeSlugAsync` (a thin wrapper over the existing CF client + slug → mod-id lookup) and `WriteOneMeta` (load → set key → save, atomic). Add both to `Scanner.cs`:

```csharp
/// <summary>Resolve a CurseForge mod-page slug to a ModMeta. Goes through the existing proxy.
/// Returns null on no-match / proxy error.</summary>
public static async Task<ModMeta?> LookupCurseForgeSlugAsync(ICurseForgeClient client, string gameSlug, string modSlug)
{
    try
    {
        // CF's API doesn't expose game-slug → game-id without a search. We use the proxy's
        // existing search endpoint with the mod slug as the query and pick an exact slug match.
        // (If CF adds direct slug lookup later, swap to that.)
        var hits = await client.SearchModsByNameAsync(gameSlug, modSlug);
        var best = hits?.FirstOrDefault(m => string.Equals(m.Slug, modSlug, StringComparison.OrdinalIgnoreCase));
        return best is null ? null : CurseForgeRequests.MapMod(best);
    }
    catch { return null; }
}

/// <summary>Write a single entry into the per-game metadata.json, leaving everything else
/// untouched. Atomic via AtomicJson. Used by the manual-match flow.</summary>
public static void WriteOneMeta(GameContext c, string modKey, ModMeta meta)
{
    var existing = LoadMetadata(c);
    var next = new Dictionary<string, ModMeta>(existing, StringComparer.OrdinalIgnoreCase) { [modKey] = meta };
    SaveMetadata(c, next);
}
```

If `SearchModsByNameAsync` or `Slug` aren't shaped quite as assumed, the implementer should adapt to the existing client surface and update this section of the plan with the actual signatures.

- [ ] **Step 1: Make the edits** to `MainViewModel.cs` and `Scanner.cs` as above. Confirm the CurseForge client method names against the actual `ICurseForgeClient` interface; adjust if needed.

- [ ] **Step 2: Build the App**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Run the test suite**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

Expected: all green.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs src/ModManager.Core/Scanner.cs
git commit -m "feat: direct-inject drop fires the identify chain; ManualMatchAsync writes IsManual"
```

---

## Task 6: App — Manual-match dialog + per-row right-click

**Files:**
- Create: `src/ModManager.App/ManualMatchDialog.xaml` + `.xaml.cs`
- Modify: `src/ModManager.App/MainWindow.xaml` (per-row right-click menu)
- Modify: `src/ModManager.App/MainWindow.xaml.cs` (handler)

A small dialog: text intro, single URL input, Apply / Close. Result flows through `ViewModel.ManualMatchAsync`.

- [ ] **Step 1: Create `ManualMatchDialog.xaml`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<ContentDialog
    x:Class="ModManager.App.ManualMatchDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Match to a mod"
    PrimaryButtonText="Apply"
    CloseButtonText="Close"
    DefaultButton="Primary">

    <StackPanel Spacing="12" Width="440">
        <TextBlock x:Name="IntroText" TextWrapping="Wrap" Opacity="0.85" FontSize="12" />
        <TextBox x:Name="UrlBox" Header="Mod page URL"
                 PlaceholderText="https://www.nexusmods.com/eldenring/mods/510 or https://www.curseforge.com/.../mods/..." />
        <TextBlock TextWrapping="Wrap" Opacity="0.6" FontSize="11"
                   Text="Paste the URL from the mod's Nexus or CurseForge page. We'll pull the title, author, downloads, and icon. The match locks — future rescans won't override it." />
    </StackPanel>
</ContentDialog>
```

- [ ] **Step 2: Code-behind**

```csharp
using Microsoft.UI.Xaml.Controls;

namespace ModManager.App;

public sealed partial class ManualMatchDialog : ContentDialog
{
    /// <summary>The URL the user pasted, or empty if they cancelled.</summary>
    public string Url => UrlBox.Text ?? "";

    public ManualMatchDialog(string displayName)
    {
        InitializeComponent();
        IntroText.Text = $"Pick a Nexus or CurseForge mod page for \"{displayName}\".";
    }
}
```

- [ ] **Step 3: Per-row right-click menu**

In `src/ModManager.App/MainWindow.xaml`, find the mod-row `DataTemplate`. The row needs a `ContextFlyout` so right-click pops a menu. If the row Grid already has one, add the item to it; otherwise add a fresh one. Append:

```xml
<Grid.ContextFlyout>
    <MenuFlyout>
        <MenuFlyoutItem Text="Match to a mod…" Click="OnManualMatch" />
    </MenuFlyout>
</Grid.ContextFlyout>
```

(If a ContextFlyout already exists, extend it with the new MenuFlyoutItem.)

- [ ] **Step 4: Handler**

In `src/ModManager.App/MainWindow.xaml.cs`, add:

```csharp
private async void OnManualMatch(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement fe || fe.DataContext is not ModRowViewModel row) return;
    var dialog = new ManualMatchDialog(row.DisplayName) { XamlRoot = Content.XamlRoot };
    if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
    await ViewModel.ManualMatchAsync(row, dialog.Url);
}
```

- [ ] **Step 5: Build + test**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

Expected: BUILD SUCCEEDED, all tests still pass.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.App/ManualMatchDialog.xaml src/ModManager.App/ManualMatchDialog.xaml.cs \
       src/ModManager.App/MainWindow.xaml src/ModManager.App/MainWindow.xaml.cs
git commit -m "feat(ui): per-row 'Match to a mod…' right-click action with URL paste"
```

---

## Task 7: Docs — `identify-paths-audit.md`

**Files:**
- Create: `docs/identify-paths-audit.md`

The per-engine status table from the spec, expanded with one paragraph per engine: what the on-disk mod looks like, which identify path covers it today, what the "next move" would be to harden it.

- [ ] **Step 1: Write the doc**

Use the spec's table as the spine. For each row, add a short paragraph naming:
- The on-disk mod naming convention
- Which identify path applies (extension-based ZipModKeys / catalog / Vortex manifest / manual / N/A)
- Status today (works / would silently no-op / stubbed / broken / fixed in this PR)
- The next move (e.g., for ME2: "wire drop-to-install + identify via top-level folder name in the archive")

Include a section "When you add a new engine" that lists the checklist of identify-path concerns to update.

- [ ] **Step 2: Commit**

```bash
git add docs/identify-paths-audit.md
git commit -m "docs: identify-paths-audit names each engine's metadata identify status"
```

---

## Final integration: smoke + PR

- [ ] **Smoke locally:**
  1. Run the app (`bin/x64/Debug/.../ModManager.App.exe`).
  2. Open Settings → Nexus Mods → confirm connected.
  3. Drop a Seamless Co-op archive on Elden Ring. Confirm the row shows a Nexus icon + author + downloads.
  4. Drop a random unrecognized archive. Right-click row → Match to a mod… → paste a known Nexus URL → confirm metadata appears.
  5. Re-run a rescan. Confirm the manual match survives.

- [ ] **PR description includes:**
  - The three-layer summary from the spec
  - Test counts before/after
  - A note for the landing-page agent: "Match to a mod…" is now a documented escape hatch when auto-identify fails

---

## Self-Review Notes

- **Spec coverage:** Layer 1 = Tasks 2 + 3 + 5a. Layer 2 = Task 7. Layer 3 = Tasks 1 + 4 + 5b + 6. The `IsManual` field (Task 1) lands before everything that depends on it.
- **TDD discipline:** Tasks 1, 2, 3, 4 are pure Core with failing tests first. Tasks 5, 6 are App-side glue (no unit tests, consistent with WinUI 3 convention in this repo).
- **Risk concentration:** Task 5's `LookupCurseForgeSlugAsync` assumes a CF client method signature that might not exist as written. If the implementer hits that, they should adapt to the actual surface and note it.
- **Manual-match round-trip:** the `IsManual` flag is persisted via existing JSON serialization (`ModMeta` round-trips). The merge rule's tests pin both call-site directions explicitly (the curated-side and the cf-side checks).
- **No new NuGets.** All wires use existing services + clients.
