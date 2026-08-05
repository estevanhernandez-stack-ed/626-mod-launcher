# Find What's Already There — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the launcher find, identify, and adopt mods the user installed by hand before the launcher existed — without moving a single file.

**Architecture:** Five stages (sweep → classify → match → propose → adopt). All classification and matching is pure Core over data the App supplies; the App owns enumeration, hashing, network, and persistence. Adoption writes metadata only. A per-game Nexus name index (seeded once, grown free from normal use) turns identification of extracted mods from a network round-trip into a local lookup.

**Tech Stack:** .NET 10, C# (nullable enabled, warnings-as-errors), xUnit, WinUI 3 for the App layer, System.Text.Json via `AtomicJson`.

**Spec:** `docs/superpowers/specs/2026-08-04-find-whats-already-there-design.md`

## Global Constraints

- **Pure-core law.** Nothing under `src/ModManager.Core/` may reference WinUI/WinRT/`Microsoft.UI.*`/`Windows.UI.*`. `CorePurityTests` enforces this. Core takes data in and returns data out; the App does I/O.
- **Reversibility law.** Adoption writes **no files** — metadata only. The first file move is the user's first toggle, through the existing move-to-holding path.
- **camelCase JSON on disk.** Every persisted shape uses `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` and ships a round-trip test asserting the lowercase key string. Write through `AtomicJson.WriteJsonAtomic`.
- **Never bundle.** The index stores facts about mods (id, name, author, endorsements). Never mod content.
- **Never accuse a game file.** Anything unmatched by a known signature or engine-shaped rule is invisible. False silence is acceptable; false accusation is not.
- **Build commands.** Tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. App build: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. **Never** run bare `dotnet build`/`dotnet test` at the repo root — the WinUI project hangs it.
- **Commits.** Conventional commits, area = Core sub-namespace or UI surface: `feat(discovery)`, `test(discovery)`, `feat(intake)`.
- **Branch.** All work on `feat/discovery-sweep` off `master`. PR into master; never commit directly to master.

---

### Task 1: DiscoveryCandidate + the pure classifier

**Files:**
- Create: `src/ModManager.Core/Discovery/DiscoveryCandidate.cs`
- Create: `src/ModManager.Core/Discovery/DiscoverySweep.cs`
- Test: `tests/ModManager.Tests/Discovery/DiscoverySweepTests.cs`

**Interfaces:**
- Consumes: `ModManager.Core.LooseMods.LooseModScan` (proxy-name/`.asi` signature rules, reference only — do not duplicate the list), `ModManager.Core.Manifest.EffectiveManifest` (not called here; the App passes the modPath in).
- Produces: `DiscoveryCandidate(string RelativePath, string FileName, DiscoveryKind Kind)`, `enum DiscoveryKind { Signature, EngineShaped, Archive }`, `DiscoverySweep.Classify(IReadOnlyList<string> relativePaths, DiscoverySweepOptions options) -> IReadOnlyList<DiscoveryCandidate>`, `DiscoverySweepOptions(string? ModPath, IReadOnlyList<string> EngineExtensions, IReadOnlyList<string> SkipFolders)`.

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core.Discovery;

namespace ModManager.Tests.Discovery;

// The classifier is the safety line: a game file must NEVER be proposed as a mod. These
// fixtures mirror a real UE game folder — engine binaries and shipped paks alongside a
// hand-installed mod, a leftover archive, and an ASI proxy.
public class DiscoverySweepTests
{
    private static DiscoverySweepOptions UeOptions() => new(
        ModPath: "Content/Paks/~mods",
        EngineExtensions: new[] { "pak", "utoc", "ucas" },
        SkipFolders: new[] { "_626mods", "disabled" });

    [Fact]
    public void Game_files_are_never_claimed()
    {
        var listing = new[]
        {
            "Binaries/Win64/Game.exe",
            "Engine/Content/Slate/Common.uasset",
            "Content/Paks/Game-WindowsNoEditor.pak",   // shipped pak, NOT in the mod path
            "README.txt",
        };

        Assert.Empty(DiscoverySweep.Classify(listing, UeOptions()));
    }

    [Fact]
    public void Engine_shaped_files_in_the_mod_path_are_candidates()
    {
        var listing = new[] { "Content/Paks/~mods/FasterShips_P.pak" };

        var found = DiscoverySweep.Classify(listing, UeOptions());

        var one = Assert.Single(found);
        Assert.Equal("FasterShips_P.pak", one.FileName);
        Assert.Equal(DiscoveryKind.EngineShaped, one.Kind);
    }

    [Fact]
    public void Signature_files_are_candidates_anywhere()
    {
        var listing = new[] { "dinput8.dll", "mods/Zipliner.asi" };

        var found = DiscoverySweep.Classify(listing, UeOptions());

        Assert.Equal(2, found.Count);
        Assert.All(found, f => Assert.Equal(DiscoveryKind.Signature, f.Kind));
    }

    [Fact]
    public void Archives_are_candidates_anywhere()
    {
        var listing = new[] { "Downloads/FasterShips10.zip", "old/backup.7z", "notes.rar" };

        var found = DiscoverySweep.Classify(listing, UeOptions());

        Assert.Equal(3, found.Count);
        Assert.All(found, f => Assert.Equal(DiscoveryKind.Archive, f.Kind));
    }

    [Fact]
    public void Skip_folders_are_not_swept()
    {
        var listing = new[]
        {
            "_626mods/anything.pak",
            "disabled/OldMod.asi",
            "Content/Paks/~mods/Real_P.pak",
        };

        var one = Assert.Single(DiscoverySweep.Classify(listing, UeOptions()));
        Assert.Equal("Real_P.pak", one.FileName);
    }

    [Fact]
    public void Null_or_empty_mod_path_still_finds_signatures_and_archives()
    {
        var options = new DiscoverySweepOptions(null, Array.Empty<string>(), Array.Empty<string>());
        var listing = new[] { "dinput8.dll", "Mod.zip", "Content/Paks/Game.pak" };

        var found = DiscoverySweep.Classify(listing, options);

        Assert.Equal(2, found.Count);   // the shipped pak has no mod path to sit in — not claimed
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~DiscoverySweepTests"`
Expected: FAIL — `DiscoverySweep` / `DiscoveryCandidate` do not exist (CS0246).

- [ ] **Step 3: Write minimal implementation**

`src/ModManager.Core/Discovery/DiscoveryCandidate.cs`:

```csharp
namespace ModManager.Core.Discovery;

/// <summary>Why a path was claimed as a possible mod. Drives how it is matched later:
/// archives can be md5-identified; the rest fall to name matching.</summary>
public enum DiscoveryKind { Signature, EngineShaped, Archive }

/// <summary>One thing the sweep claims might be a mod. Paths are RELATIVE to the swept root,
/// so Core never sees an absolute path (pure-core law).</summary>
public sealed record DiscoveryCandidate(string RelativePath, string FileName, DiscoveryKind Kind);

/// <summary>What the classifier needs to know about this game. Supplied by the App from the
/// effective manifest; Core never reads the manifest here.</summary>
public sealed record DiscoverySweepOptions(
    string? ModPath,
    IReadOnlyList<string> EngineExtensions,
    IReadOnlyList<string> SkipFolders);
```

`src/ModManager.Core/Discovery/DiscoverySweep.cs`:

```csharp
namespace ModManager.Core.Discovery;

/// <summary>
/// Pure classification of a swept file listing into mod candidates. The caller enumerates the
/// disk (same contract as <see cref="ModManager.Core.LooseMods.LooseModScan"/>); this decides
/// what is plausibly a mod.
///
/// THE SAFETY LINE: anything not matched by a signature, an engine-shaped rule, or an archive
/// extension is INVISIBLE. A game file must never be proposed as a mod — false silence is the
/// acceptable failure, false accusation is not.
/// </summary>
public static class DiscoverySweep
{
    // The proxy-loader names + .asi convention: a game never ships these, so they are mods
    // regardless of location. Mirrors LooseModScan's by-nature rules.
    private static readonly string[] ProxyNames =
        { "dinput8.dll", "version.dll", "winmm.dll", "d3d11.dll", "dxgi.dll", "winhttp.dll" };

    private static readonly string[] ArchiveExtensions = { "zip", "7z", "rar" };

    public static IReadOnlyList<DiscoveryCandidate> Classify(
        IReadOnlyList<string> relativePaths, DiscoverySweepOptions options)
    {
        var found = new List<DiscoveryCandidate>();
        foreach (var path in relativePaths)
        {
            var normalized = path.Replace('\\', '/');
            if (IsSkipped(normalized, options.SkipFolders)) continue;

            var fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
            var extension = Extension(fileName);

            if (ArchiveExtensions.Contains(extension))
            {
                found.Add(new DiscoveryCandidate(normalized, fileName, DiscoveryKind.Archive));
                continue;
            }

            if (IsSignature(fileName, extension))
            {
                found.Add(new DiscoveryCandidate(normalized, fileName, DiscoveryKind.Signature));
                continue;
            }

            if (IsEngineShaped(normalized, extension, options))
                found.Add(new DiscoveryCandidate(normalized, fileName, DiscoveryKind.EngineShaped));
        }
        return found;
    }

    private static bool IsSkipped(string path, IReadOnlyList<string> skipFolders)
        => skipFolders.Any(folder =>
            path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/" + folder + "/", StringComparison.OrdinalIgnoreCase));

    private static bool IsSignature(string fileName, string extension)
        => extension == "asi" || ProxyNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);

    // Engine-typical extension AND inside this game's mod folder. Both halves are required:
    // the same .pak extension is a shipped game file one directory up.
    private static bool IsEngineShaped(string path, string extension, DiscoverySweepOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ModPath)) return false;
        if (!options.EngineExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return false;
        var modPath = options.ModPath.Replace('\\', '/').Trim('/');
        return path.StartsWith(modPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string Extension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot < 0 ? "" : fileName[(dot + 1)..].ToLowerInvariant();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~DiscoverySweepTests"`
Expected: PASS — 6 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Discovery tests/ModManager.Tests/Discovery
git commit -m "feat(discovery): pure sweep classifier — signatures, engine-shaped, archives"
```

---

### Task 2: ModNameIndex — shape, merge, bound, and the matcher

**Files:**
- Create: `src/ModManager.Core/Discovery/ModNameIndex.cs`
- Test: `tests/ModManager.Tests/Discovery/ModNameIndexTests.cs`

**Interfaces:**
- Consumes: `ModManager.Core.NameMatch.CleanModName(string?)` and `NameMatch.PickBestMatch<T>(string query, IEnumerable<T>? candidates, Func<T,string?> name, double threshold = 0.5)` — reuse, do not write a second normalizer.
- Produces: `ModNameIndexEntry(int ModId, string Name, string? Author, int? Endorsements)`, `ModNameIndex` with `Entries` (`IReadOnlyList<ModNameIndexEntry>`), `static ModNameIndex Merge(ModNameIndex existing, IEnumerable<ModNameIndexEntry> incoming, int cap = 5000)`, `ModNameIndexEntry? Match(string fileName)`.

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core.Discovery;

namespace ModManager.Tests.Discovery;

// The index turns "which of THIS game's mods is this file?" into a local lookup. Scoped to one
// game's domain, so the haystack is small — but a wrong hit still must not outrank a right one.
public class ModNameIndexTests
{
    private static ModNameIndex Index(params ModNameIndexEntry[] entries)
        => ModNameIndex.Merge(ModNameIndex.Empty, entries);

    private static ModNameIndexEntry Entry(int id, string name, int endorsements = 10)
        => new(id, name, "Author", endorsements);

    [Fact]
    public void Matches_a_file_name_to_a_known_mod()
    {
        var index = Index(Entry(1, "Faster Ships"), Entry(2, "More Stacks"));

        var hit = index.Match("FasterShips10.pak");

        Assert.NotNull(hit);
        Assert.Equal(1, hit!.ModId);
    }

    [Fact]
    public void Version_suffixes_do_not_defeat_the_match()
    {
        var index = Index(Entry(1, "Faster Ships"));

        Assert.NotNull(index.Match("Faster_Ships_v1.2.3.zip"));
    }

    [Fact]
    public void An_unrelated_name_matches_nothing()
    {
        var index = Index(Entry(1, "Faster Ships"), Entry(2, "More Stacks"));

        Assert.Null(index.Match("SomeRandomEngineFile.pak"));
    }

    [Fact]
    public void Merge_deduplicates_by_mod_id_and_keeps_the_newer_entry()
    {
        var first = Index(Entry(1, "Faster Ships", endorsements: 10));

        var merged = ModNameIndex.Merge(first, new[] { Entry(1, "Faster Ships Redux", endorsements: 99) });

        var only = Assert.Single(merged.Entries);
        Assert.Equal("Faster Ships Redux", only.Name);
        Assert.Equal(99, only.Endorsements);
    }

    [Fact]
    public void Merge_caps_the_index_dropping_lowest_endorsement_first()
    {
        var incoming = new[] { Entry(1, "Keep Me", 500), Entry(2, "Drop Me", 1), Entry(3, "Keep Me Too", 400) };

        var merged = ModNameIndex.Merge(ModNameIndex.Empty, incoming, cap: 2);

        Assert.Equal(2, merged.Entries.Count);
        Assert.DoesNotContain(merged.Entries, e => e.Name == "Drop Me");
    }

    [Fact]
    public void Empty_index_matches_nothing_and_never_throws()
    {
        Assert.Null(ModNameIndex.Empty.Match("Anything.pak"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ModNameIndexTests"`
Expected: FAIL — `ModNameIndex` does not exist (CS0246).

- [ ] **Step 3: Write minimal implementation**

`src/ModManager.Core/Discovery/ModNameIndex.cs`:

```csharp
namespace ModManager.Core.Discovery;

/// <summary>One remembered mod: enough to name a file and credit its author, nothing more.
/// Facts only — never mod content (never-bundle law).</summary>
public sealed record ModNameIndexEntry(int ModId, string Name, string? Author, int? Endorsements);

/// <summary>
/// A per-game cache of mod names, used to identify extracted mods the launcher finds on disk.
/// Nexus md5 lookup matches the PUBLISHED ARCHIVE hash, so an extracted mod's loose files can
/// never be md5-identified — this index is what makes those identifiable at all.
///
/// A cache, never a database: bounded, lossy, and safe to delete. Pure — the App fetches and
/// persists; matching happens here with no I/O.
/// </summary>
public sealed record ModNameIndex(IReadOnlyList<ModNameIndexEntry> Entries)
{
    public const int DefaultCap = 5000;

    public static ModNameIndex Empty { get; } = new(Array.Empty<ModNameIndexEntry>());

    /// <summary>Fold new entries in: dedupe by mod id (incoming wins — it is fresher), then cap,
    /// dropping the lowest-endorsement entries first so the mods people actually have survive.</summary>
    public static ModNameIndex Merge(
        ModNameIndex existing, IEnumerable<ModNameIndexEntry> incoming, int cap = DefaultCap)
    {
        var byId = existing.Entries.ToDictionary(e => e.ModId);
        foreach (var entry in incoming) byId[entry.ModId] = entry;

        var kept = byId.Values
            .OrderByDescending(e => e.Endorsements ?? 0)
            .Take(cap)
            .ToList();

        return new ModNameIndex(kept);
    }

    /// <summary>Best known mod for a file name, or null when nothing clears the threshold.
    /// Uses the SAME cleaning + scoring as loose-root identify so both surfaces agree.</summary>
    public ModNameIndexEntry? Match(string fileName)
    {
        if (Entries.Count == 0) return null;
        var query = NameMatch.CleanModName(fileName);
        if (string.IsNullOrWhiteSpace(query)) return null;
        return NameMatch.PickBestMatch(query, Entries, e => e.Name);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ModNameIndexTests"`
Expected: PASS — 6 tests. If `Version_suffixes_do_not_defeat_the_match` fails, `NameMatch.CleanModName` is not stripping the suffix; fix by cleaning BOTH sides in `Match` (clean the entry name too) rather than lowering the threshold.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Discovery/ModNameIndex.cs tests/ModManager.Tests/Discovery/ModNameIndexTests.cs
git commit -m "feat(discovery): per-game mod-name index with bounded merge + pure matcher"
```

---

### Task 3: Index persistence shape — camelCase round-trip

**Files:**
- Create: `tests/ModManager.Tests/Discovery/ModNameIndexJsonTests.cs`
- Modify: `.claude/rules/camelcase-json-on-disk.md` (append the new shape to the governed-surfaces list)

**Interfaces:**
- Consumes: `ModNameIndex`, `ModNameIndexEntry` (Task 2).
- Produces: the pinned on-disk contract for `<dataDir>/nexus-name-index.json` consumed by Task 7.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using ModManager.Core.Discovery;

namespace ModManager.Tests.Discovery;

// camelCase-on-disk law. The string-contains assertion is what protects it — STJ reads
// case-insensitively, so a round-trip alone passes even with PascalCase keys.
public class ModNameIndexJsonTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    [Fact]
    public void Index_round_trips_as_camelCase()
    {
        var original = ModNameIndex.Merge(
            ModNameIndex.Empty,
            new[] { new ModNameIndexEntry(510, "Seamless Co-op", "LukeYui", 42000) });

        var json = JsonSerializer.Serialize(original, JsonOpts);

        Assert.Contains("\"modId\"", json);
        Assert.Contains("\"endorsements\"", json);
        Assert.DoesNotContain("\"ModId\"", json);
        Assert.DoesNotContain("\"Endorsements\"", json);

        var back = JsonSerializer.Deserialize<ModNameIndex>(json, JsonOpts);
        Assert.NotNull(back);
        var only = Assert.Single(back!.Entries);
        Assert.Equal(510, only.ModId);
        Assert.Equal("Seamless Co-op", only.Name);
        Assert.Equal("LukeYui", only.Author);
    }
}
```

- [ ] **Step 2: Run test to verify it fails or passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ModNameIndexJsonTests"`
Expected: PASS if `ModNameIndex` deserializes cleanly. If it FAILS with a constructor/deserialization error, add a parameterless-friendly shape: give `ModNameIndex` a `[JsonConstructor]` on its primary constructor. Do not change the property names.

- [ ] **Step 3: Record the shape in the rule file**

Append one line to the "Surfaces this rule already governs" list in `.claude/rules/camelcase-json-on-disk.md`:

```markdown
- `ModNameIndex` nexus-name-index.json (`src/ModManager.Core/Discovery/ModNameIndex.cs` — per-game Nexus name cache; written via `AtomicJson` by `ModNameIndexSource`)
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS, no regressions.

- [ ] **Step 5: Commit**

```bash
git add tests/ModManager.Tests/Discovery/ModNameIndexJsonTests.cs .claude/rules/camelcase-json-on-disk.md
git commit -m "test(discovery): pin the name-index on-disk shape as camelCase"
```

---

### Task 4: AdoptionProposal — the reviewable row and the honest projection

**Files:**
- Create: `src/ModManager.Core/Discovery/AdoptionProposal.cs`
- Test: `tests/ModManager.Tests/Discovery/AdoptionProposalTests.cs`

**Interfaces:**
- Consumes: `DiscoveryCandidate`/`DiscoveryKind` (Task 1), `ModNameIndexEntry` (Task 2), `ModManager.Core.ModMeta`, `ModManager.Plugins.Abstractions.SourceIdentifyResult` — **only its metadata fields are used; do not add a plugin dependency to Core beyond what `LooseIdentify` already takes.**
- Produces: `enum AdoptionEvidence { Md5, NameIndex, None }`, `AdoptionProposal(DiscoveryCandidate Candidate, AdoptionEvidence Evidence, int? ModId, string? Title, string? Author, int? Endorsements)`, `AdoptionProposal.FromIndex(...)`, `AdoptionProposal.Unidentified(...)`, `AdoptionProposal.ToMeta()`.

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;
using ModManager.Core.Discovery;

namespace ModManager.Tests.Discovery;

// Adoption writes METADATA ONLY — no file op — and it must record how sure we are. A name-index
// hit is weaker evidence than an md5 hit and can never masquerade as one, so a later stronger
// identify (or the manual-match dialog) can still supersede it.
public class AdoptionProposalTests
{
    private static DiscoveryCandidate Candidate(string name = "FasterShips10.pak")
        => new($"Content/Paks/~mods/{name}", name, DiscoveryKind.EngineShaped);

    [Fact]
    public void Index_evidence_records_nameSearch_confidence()
    {
        var proposal = AdoptionProposal.FromIndex(
            Candidate(), new ModNameIndexEntry(1, "Faster Ships", "Kingtology", 240));

        var meta = proposal.ToMeta();

        Assert.Equal("nameSearch", meta.SourceConfidence);
        Assert.Equal("Faster Ships", meta.Title);
        Assert.Equal("Kingtology", meta.Author);
        Assert.Equal(1, meta.NexusModId);
        Assert.Equal(240, meta.EndorsementCount);
    }

    [Fact]
    public void Md5_evidence_records_md5_confidence()
    {
        var proposal = AdoptionProposal.FromMd5(
            Candidate("FasterShips10.zip"), modId: 7, title: "Faster Ships", author: "Kingtology", endorsements: 240);

        Assert.Equal("md5", proposal.ToMeta().SourceConfidence);
    }

    [Fact]
    public void Adoption_never_marks_an_entry_manual()
    {
        var fromIndex = AdoptionProposal.FromIndex(Candidate(), new ModNameIndexEntry(1, "Faster Ships", null, null));
        var fromMd5 = AdoptionProposal.FromMd5(Candidate(), 7, "Faster Ships", null, null);

        Assert.False(fromIndex.ToMeta().IsManual);
        Assert.False(fromMd5.ToMeta().IsManual);
    }

    [Fact]
    public void An_unidentified_find_is_still_adoptable_with_no_false_identity()
    {
        var proposal = AdoptionProposal.Unidentified(Candidate("MysteryThing.pak"));

        var meta = proposal.ToMeta();

        Assert.Equal(AdoptionEvidence.None, proposal.Evidence);
        Assert.Null(meta.SourceConfidence);
        Assert.Null(meta.NexusModId);
        Assert.Null(meta.Title);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~AdoptionProposalTests"`
Expected: FAIL — `AdoptionProposal` does not exist (CS0246).

- [ ] **Step 3: Write minimal implementation**

`src/ModManager.Core/Discovery/AdoptionProposal.cs`:

```csharp
namespace ModManager.Core.Discovery;

/// <summary>How strongly a discovered file was identified. Ordered best-first — this is what
/// <see cref="ModMeta.SourceConfidence"/> records, so a weak match can never pass as a strong one.</summary>
public enum AdoptionEvidence { Md5, NameIndex, None }

/// <summary>
/// One reviewable row: what was found, what we think it is, and how sure we are. Adoption writes
/// METADATA ONLY — nothing here moves, renames, or deletes a file. The first file move is the
/// user's first toggle, through the existing reversible path.
/// </summary>
public sealed record AdoptionProposal(
    DiscoveryCandidate Candidate,
    AdoptionEvidence Evidence,
    int? ModId,
    string? Title,
    string? Author,
    int? Endorsements)
{
    /// <summary>A leftover archive matched by md5 — exact, authoritative.</summary>
    public static AdoptionProposal FromMd5(
        DiscoveryCandidate candidate, int modId, string? title, string? author, int? endorsements)
        => new(candidate, AdoptionEvidence.Md5, modId, title, author, endorsements);

    /// <summary>An extracted mod matched by name against this game's index — a proposal, not a fact.</summary>
    public static AdoptionProposal FromIndex(DiscoveryCandidate candidate, ModNameIndexEntry entry)
        => new(candidate, AdoptionEvidence.NameIndex, entry.ModId, entry.Name, entry.Author, entry.Endorsements);

    /// <summary>Found, unidentified. Still worth adopting: visible and toggleable beats invisible.</summary>
    public static AdoptionProposal Unidentified(DiscoveryCandidate candidate)
        => new(candidate, AdoptionEvidence.None, null, null, null, null);

    /// <summary>The metadata to merge in on approval. Never sets <see cref="ModMeta.IsManual"/> —
    /// an approved proposal is not a manual paste, so a stronger identify can still supersede it.</summary>
    public ModMeta ToMeta() => new()
    {
        Title = Title,
        Author = Author,
        NexusModId = ModId,
        EndorsementCount = Endorsements,
        SourceConfidence = Evidence switch
        {
            AdoptionEvidence.Md5 => "md5",
            AdoptionEvidence.NameIndex => "nameSearch",
            _ => null,
        },
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~AdoptionProposalTests"`
Expected: PASS — 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Discovery/AdoptionProposal.cs tests/ModManager.Tests/Discovery/AdoptionProposalTests.cs
git commit -m "feat(discovery): adoption proposals with honest confidence, metadata-only projection"
```

---

### Task 5: Generalize LooseIdentify beyond loose-root

**Files:**
- Modify: `src/ModManager.Core/LooseMods/LooseIdentify.cs` (the location gate inside `Candidates`)
- Modify: `tests/ModManager.Tests/LooseMods/LooseIdentifyTests.cs` (add cases; change nothing existing)

**Interfaces:**
- Consumes: nothing new.
- Produces: `LooseIdentify.Candidates` now returns unidentified rows from **any** location. Signature is unchanged, so every existing caller keeps working.

- [ ] **Step 1: Write the failing test**

Append to `tests/ModManager.Tests/LooseMods/LooseIdentifyTests.cs` (inside the existing class):

```csharp
    // The name-search offer used to be loose-root only, so an unidentified mod in a Bethesda
    // Data folder or a UE Paks folder could never be identified. Widening the scope must NOT
    // weaken any other candidate rule.
    [Fact]
    public void Candidates_include_rows_outside_loose_root()
    {
        var rows = new List<Mod>
        {
            new() { Base = "SkyUI_5_2_SE", Name = "SkyUI_5_2_SE", Location = "Data", Class = "plugin" },
            new() { Base = "FasterShips_P", Name = "FasterShips_P", Location = "Content/Paks/~mods", Class = "pak" },
        };

        var candidates = LooseIdentify.Candidates(rows, new Dictionary<string, ModMeta>());

        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public void Widening_does_not_weaken_the_other_candidate_rules()
    {
        var rows = new List<Mod>
        {
            new() { Base = "Manual", Name = "Manual", Location = "Data", Class = "plugin" },
            new() { Base = "Identified", Name = "Identified", Location = "Data", Class = "plugin" },
            new() { Base = "Loader", Name = "Loader", Location = "Data", Class = "loader" },
        };
        var meta = new Dictionary<string, ModMeta>
        {
            ["Manual"] = new() { IsManual = true },
            ["Identified"] = new() { NexusModId = 42 },
        };

        Assert.Empty(LooseIdentify.Candidates(rows, meta));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~LooseIdentifyTests"`
Expected: FAIL on `Candidates_include_rows_outside_loose_root` (returns 0, expected 2). The second new test should already pass.

- [ ] **Step 3: Write minimal implementation**

In `src/ModManager.Core/LooseMods/LooseIdentify.cs`, delete this line from `Candidates`:

```csharp
            if (row.Location != LooseRootListing.LooseRootLocation) continue;
```

and update the XML doc above `Candidates` to read:

```csharp
    /// <summary>Rows worth proposing a search for, in ANY location (loose-root, a Bethesda Data
    /// folder, a UE Paks folder — an unidentified mod is worth identifying wherever it sits): not
    /// a loader row (dinput8/dxgi/version proxies aren't "mods" a user would search Nexus for),
    /// not manually pinned by the user (<see cref="ModMeta.IsManual"/> locks the entry —
    /// auto-identify never clobbers it), and not already identified (a Nexus id or any prior
    /// source confidence means a search would be redundant, and could overwrite a stronger match
    /// with a weaker name-search one).</summary>
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — including every pre-existing `LooseIdentifyTests` case. If a loose-root test now fails, the fixture depended on the gate for filtering; fix the fixture, not the rule.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/LooseMods/LooseIdentify.cs tests/ModManager.Tests/LooseMods/LooseIdentifyTests.cs
git commit -m "feat(discovery): offer name-search identify for unidentified mods in any location"
```

---

### Task 6: DiscoveryScanService — enumeration and archive hashing (App)

**Files:**
- Create: `src/ModManager.App/Services/DiscoveryScanService.cs`

**Interfaces:**
- Consumes: `DiscoverySweep.Classify` + `DiscoverySweepOptions` (Task 1), `ModManager.Core.Md5Hash.OfFile(string)`, `ModManager.Core.TakenOverStore` (skip folders another manager owns).
- Produces: `DiscoveryScanService.Sweep(string root, DiscoverySweepOptions options) -> IReadOnlyList<DiscoveryCandidate>` and `DiscoveryScanService.Md5Of(string root, DiscoveryCandidate candidate) -> string?`.

- [ ] **Step 1: Write the implementation**

`src/ModManager.App/Services/DiscoveryScanService.cs`:

```csharp
using System.IO;
using ModManager.Core;
using ModManager.Core.Discovery;

namespace ModManager.App.Services;

/// <summary>
/// The I/O half of discovery: enumerate a root into relative paths, hand them to the pure
/// classifier, and hash archives on request. READ-ONLY — this service never writes, moves, or
/// deletes anything. Depth-capped so a deep game tree can't stall the UI.
/// </summary>
public sealed class DiscoveryScanService
{
    private const int MaxDepth = 6;
    private const int MaxFiles = 20000;

    /// <summary>Enumerate + classify. Unreadable folders are skipped, never fatal.</summary>
    public IReadOnlyList<DiscoveryCandidate> Sweep(string root, DiscoverySweepOptions options)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Array.Empty<DiscoveryCandidate>();

        var relative = new List<string>();
        Walk(root, root, 0, relative);
        return DiscoverySweep.Classify(relative, options);
    }

    private static void Walk(string root, string dir, int depth, List<string> into)
    {
        if (depth > MaxDepth || into.Count >= MaxFiles) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (into.Count >= MaxFiles) return;
                into.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
            }
            foreach (var sub in Directory.EnumerateDirectories(dir))
                Walk(root, sub, depth + 1, into);
        }
        catch (UnauthorizedAccessException) { /* skip locked folders — never fatal */ }
        catch (IOException) { /* same */ }
    }

    /// <summary>MD5 of a discovered archive for Nexus md5 lookup, or null if unreadable.
    /// Only meaningful for <see cref="DiscoveryKind.Archive"/> — Nexus hashes published
    /// archives, so extracted files never match.</summary>
    public string? Md5Of(string root, DiscoveryCandidate candidate)
    {
        if (candidate.Kind != DiscoveryKind.Archive) return null;
        try { return Md5Hash.OfFile(Path.Combine(root, candidate.RelativePath)); }
        catch { return null; }
    }
}
```

- [ ] **Step 2: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Verify Core purity is intact**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~CorePurityTests"`
Expected: PASS — the I/O stayed in the App layer.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/Services/DiscoveryScanService.cs
git commit -m "feat(discovery): read-only sweep service — depth-capped enumeration + archive md5"
```

---

### Task 7: ModNameIndexSource — seed, grow, persist (App)

**Files:**
- Create: `src/ModManager.App/Services/ModNameIndexSource.cs`

**Interfaces:**
- Consumes: `ModNameIndex`/`ModNameIndexEntry`/`ModNameIndex.Merge` (Task 2), `ModManager.Core.AtomicJson.WriteJsonAtomic<T>(string file, T value)`, `ModManager.Plugins.Abstractions.IModCatalogBrowse.BrowseCatalogAsync(CatalogQuery)`, `CatalogQuery(GameDomain, Text, Sort, Category, Offset, Count)`, `CatalogSort.MostEndorsed`, `SourceSearchHit(GameDomain, ModId, Name, Author, Summary, EndorsementCount, Url)`.
- Produces: `ModNameIndexSource.Load(string dataDir)`, `SaveAsync/Save(string dataDir, ModNameIndex index)`, `Task<ModNameIndex> SeedAsync(string dataDir, string gameDomain, object source)`, `ModNameIndex Grow(string dataDir, IEnumerable<SourceSearchHit> hits)`.

- [ ] **Step 1: Write the implementation**

`src/ModManager.App/Services/ModNameIndexSource.cs`:

```csharp
using System.IO;
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Discovery;
using ModManager.Plugins.Abstractions;

namespace ModManager.App.Services;

/// <summary>
/// Fetches, grows, and persists the per-game Nexus name index. Seed once (bounded), then grow
/// for free from every catalog page browsed, search run, and update poll. camelCase on disk via
/// AtomicJson — the launcher's on-disk JSON law.
///
/// Every failure is non-fatal: a missing, corrupt, or unreachable index resolves to
/// <see cref="ModNameIndex.Empty"/>, and discovery degrades to found-but-unidentified.
/// </summary>
public sealed class ModNameIndexSource
{
    private const int SeedTarget = 500;
    private const int PageSize = 50;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static string PathFor(string dataDir) => Path.Combine(dataDir, "nexus-name-index.json");

    public ModNameIndex Load(string dataDir)
    {
        try
        {
            var file = PathFor(dataDir);
            if (!File.Exists(file)) return ModNameIndex.Empty;
            return JsonSerializer.Deserialize<ModNameIndex>(File.ReadAllText(file), JsonOpts)
                   ?? ModNameIndex.Empty;
        }
        catch { return ModNameIndex.Empty; }
    }

    public void Save(string dataDir, ModNameIndex index)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            AtomicJson.WriteJsonAtomic(PathFor(dataDir), index);
        }
        catch { /* best-effort cache; in-memory state still serves this session */ }
    }

    /// <summary>One bounded seed: the top mods by endorsements — the ones people actually have.
    /// A source without catalog browse (sealed build, old plugin) simply seeds nothing.</summary>
    public async Task<ModNameIndex> SeedAsync(string dataDir, string gameDomain, object source)
    {
        var index = Load(dataDir);
        if (source is not IModCatalogBrowse browse) return index;

        try
        {
            for (var offset = 0; offset < SeedTarget; offset += PageSize)
            {
                var page = await browse.BrowseCatalogAsync(
                    new CatalogQuery(gameDomain, Sort: CatalogSort.MostEndorsed, Offset: offset, Count: PageSize));
                if (page.Hits.Count == 0) break;
                index = ModNameIndex.Merge(index, page.Hits.Select(ToEntry));
            }
            Save(dataDir, index);
        }
        catch { /* offline / rate-limited — keep whatever we already had */ }

        return index;
    }

    /// <summary>Fold hits the app saw during normal use into the index. Free — no extra calls.</summary>
    public ModNameIndex Grow(string dataDir, IEnumerable<SourceSearchHit> hits)
    {
        var index = ModNameIndex.Merge(Load(dataDir), hits.Select(ToEntry));
        Save(dataDir, index);
        return index;
    }

    private static ModNameIndexEntry ToEntry(SourceSearchHit hit)
        => new(hit.ModId, hit.Name, hit.Author, hit.EndorsementCount);
}
```

- [ ] **Step 2: Build the App**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 errors. If `CatalogSort.MostEndorsed` does not resolve, check the enum's actual member name in `src/ModManager.Plugins.Abstractions` and use it verbatim.

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.App/Services/ModNameIndexSource.cs
git commit -m "feat(discovery): seed + grow + persist the per-game Nexus name index"
```

---

### Task 8: DiscoveryReviewDialog — the review surface (App)

**Files:**
- Create: `src/ModManager.App/DiscoveryReviewDialog.xaml`
- Create: `src/ModManager.App/DiscoveryReviewDialog.xaml.cs`
- Reference (read before writing, match its shape): `src/ModManager.App/LooseIdentifyDialog.xaml` + `.xaml.cs`

**Interfaces:**
- Consumes: `AdoptionProposal`/`AdoptionEvidence` (Task 4).
- Produces: `DiscoveryReviewDialog(IReadOnlyList<AdoptionProposal> proposals)` and `IReadOnlyList<AdoptionProposal> Approved { get; }` — populated only when the primary button was used.

- [ ] **Step 1: Read the model dialog**

Read `src/ModManager.App/LooseIdentifyDialog.xaml.cs` in full. Copy its posture exactly: a `Row` wrapper class with `Approve`, per-row `Visibility` helpers, `Apply` as the ONLY write path, a live count on the primary button, and `DialogTheming.Apply(this)` in the constructor.

- [ ] **Step 2: Write the dialog**

`src/ModManager.App/DiscoveryReviewDialog.xaml` — model on `LooseIdentifyDialog.xaml`, with the dialog shell every XAML dialog now carries:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ContentDialog
    x:Class="ModManager.App.DiscoveryReviewDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:ModManager.App"
    PrimaryButtonText="Adopt"
    CloseButtonText="Cancel"
    DefaultButton="Close"
    AutomationProperties.Name="Mods already installed"
    PrimaryButtonClick="OnApply">

    <ContentDialog.Title>
        <StackPanel Spacing="6">
            <Border Height="3" Background="{StaticResource ThemeAccent}" Margin="-24,0,-24,4"
                    AutomationProperties.AccessibilityView="Raw" />
            <TextBlock Text="LIBRARY // ALREADY INSTALLED" FontFamily="{StaticResource MonoFontFamily}"
                       FontSize="{StaticResource TagFontSize}" CharacterSpacing="80"
                       AutomationProperties.AccessibilityView="Raw"
                       Foreground="{StaticResource ThemeInkDim}" />
            <TextBlock Text="Mods already installed" FontSize="{StaticResource ViewTitleFontSize}" FontWeight="SemiBold" />
        </StackPanel>
    </ContentDialog.Title>

    <ScrollViewer MaxHeight="480" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <StackPanel Spacing="{StaticResource SpaceM}" Width="460">
            <TextBlock TextWrapping="Wrap" Foreground="{StaticResource ThemeInkSoft}"
                       FontSize="{StaticResource BodyFontSize}"
                       Text="These look like mods you already installed by hand. Adopting them lists them here so you can turn them on and off — your files are not moved." />
            <ItemsControl x:Name="RowList">
                <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="local:DiscoveryReviewDialog+Row">
                        <Grid ColumnSpacing="{StaticResource SpaceS}" Padding="0,6">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <CheckBox Grid.Column="0" IsChecked="{x:Bind Approve, Mode=TwoWay}"
                                      AutomationProperties.Name="{x:Bind Headline}" VerticalAlignment="Top" />
                            <StackPanel Grid.Column="1" Spacing="2">
                                <TextBlock Text="{x:Bind Headline}" TextWrapping="Wrap"
                                           Foreground="{StaticResource ThemeInk}" />
                                <TextBlock Text="{x:Bind Detail}" TextWrapping="Wrap"
                                           FontSize="{StaticResource MetaFontSize}"
                                           Foreground="{StaticResource ThemeInkDim}" />
                            </StackPanel>
                        </Grid>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </ScrollViewer>
</ContentDialog>
```

`src/ModManager.App/DiscoveryReviewDialog.xaml.cs`:

```csharp
using Microsoft.UI.Xaml.Controls;
using ModManager.Core.Discovery;

namespace ModManager.App;

/// <summary>
/// Review-before-adopt for discovered mods: one row per proposal, checked by default when we
/// identified it, unchecked when we didn't. Apply is the ONLY path that returns approvals;
/// Cancel returns nothing. Adoption writes metadata only — no file is touched either way.
/// </summary>
public sealed partial class DiscoveryReviewDialog : ContentDialog
{
    public sealed class Row
    {
        public AdoptionProposal Proposal { get; init; } = null!;
        public string Headline { get; init; } = "";
        public string Detail { get; init; } = "";
        public bool Approve { get; set; }
    }

    private readonly List<Row> _rows = new();

    public IReadOnlyList<AdoptionProposal> Approved { get; private set; } = Array.Empty<AdoptionProposal>();

    public DiscoveryReviewDialog(IReadOnlyList<AdoptionProposal> proposals)
    {
        InitializeComponent();
        ModManager.App.Services.DialogTheming.Apply(this);

        foreach (var proposal in proposals)
        {
            var identified = proposal.Evidence != AdoptionEvidence.None;
            _rows.Add(new Row
            {
                Proposal = proposal,
                Headline = identified
                    ? $"{proposal.Candidate.FileName} — {proposal.Title}"
                    : $"{proposal.Candidate.FileName} — not identified",
                Detail = proposal.Evidence switch
                {
                    AdoptionEvidence.Md5 => $"Matched exactly by file hash. {proposal.Candidate.RelativePath}",
                    AdoptionEvidence.NameIndex => $"Matched by name{(proposal.Author is null ? "" : $" · by {proposal.Author}")}. {proposal.Candidate.RelativePath}",
                    _ => $"Found at {proposal.Candidate.RelativePath}. Adopt it to manage it anyway.",
                },
                Approve = identified,
            });
        }

        RowList.ItemsSource = _rows;
        IsPrimaryButtonEnabled = _rows.Count > 0;
    }

    private void OnApply(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        => Approved = _rows.Where(r => r.Approve).Select(r => r.Proposal).ToList();
}
```

- [ ] **Step 3: Clean-build the App (XAML codegen)**

Run: `rm -rf src/ModManager.App/obj src/ModManager.App/bin && dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded. (A stale `obj` after a XAML edit causes an `InvalidCastException` at `Connect()` — always clean after adding XAML.)

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/DiscoveryReviewDialog.xaml src/ModManager.App/DiscoveryReviewDialog.xaml.cs
git commit -m "feat(discovery): review-before-adopt dialog"
```

---

### Task 9: Wire the pipeline into the VM and trigger it

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs`
- Modify: `src/ModManager.App/MainWindow.xaml.cs` (dialog host, mirroring the `ConfirmBanRiskEnable` delegate pattern)
- Modify: `src/ModManager.App/App.xaml.cs` (DI registration for the two new services)

**Interfaces:**
- Consumes: everything from Tasks 1–8.
- Produces: `MainViewModel.DiscoverExistingModsAsync(bool auto)` and `Func<IReadOnlyList<AdoptionProposal>, Task<IReadOnlyList<AdoptionProposal>>>? ReviewDiscoveries { get; set; }` (the view supplies the dialog, exactly like `ConfirmBanRiskEnable`).

- [ ] **Step 1: Register the services**

In `src/ModManager.App/App.xaml.cs`, beside the other `AddSingleton` registrations:

```csharp
        services.AddSingleton<Services.DiscoveryScanService>();
        services.AddSingleton<Services.ModNameIndexSource>();
```

- [ ] **Step 2: Add the VM orchestration**

In `MainViewModel`, add the delegate beside `ConfirmBanRiskEnable`:

```csharp
    /// <summary>The view supplies the review dialog (dialog + XamlRoot live in the code-behind).
    /// Returns the approved subset; unwired or cancelled -> nothing is adopted.</summary>
    public Func<IReadOnlyList<AdoptionProposal>, Task<IReadOnlyList<AdoptionProposal>>>? ReviewDiscoveries { get; set; }
```

and the orchestration method:

```csharp
    /// <summary>Sweep this game for mods the launcher didn't install, identify what we can, and
    /// offer them for adoption. READ-ONLY until the user approves; adoption writes metadata only.
    /// <paramref name="auto"/> true = the silent first-add run (says nothing when it finds nothing).</summary>
    public async Task DiscoverExistingModsAsync(bool auto)
    {
        if (_ctx is null) return;

        var options = new DiscoverySweepOptions(
            ModPath: _ctx.Game.ModLocations.FirstOrDefault()?.Path,
            EngineExtensions: EngineExtensionsFor(_ctx.Game.Engine),
            SkipFolders: new[] { "_626mods", "loose-disabled", "disabled" });

        var candidates = _discovery.Sweep(_ctx.Game.GameRoot, options);
        if (candidates.Count == 0)
        {
            if (!auto) StatusText = "No unmanaged mods found in this game's folder.";
            return;
        }

        var index = _nameIndex.Load(_ctx.DataDir);
        var proposals = new List<AdoptionProposal>();
        foreach (var candidate in candidates)
        {
            var hit = index.Match(candidate.FileName);
            proposals.Add(hit is not null
                ? AdoptionProposal.FromIndex(candidate, hit)
                : AdoptionProposal.Unidentified(candidate));
        }

        if (ReviewDiscoveries is null) return;
        var approved = await ReviewDiscoveries(proposals);
        if (approved.Count == 0) { StatusText = "Nothing adopted."; return; }

        // Adoption is metadata-only — no file is moved, renamed, or deleted.
        foreach (var proposal in approved)
            MergeMeta(proposal.Candidate.FileName, proposal.ToMeta());

        StatusText = approved.Count == 1
            ? "Adopted 1 mod. Your files were not moved."
            : $"Adopted {approved.Count} mods. Your files were not moved.";
        await ReloadModsAsync();
    }
```

Implement `EngineExtensionsFor(string? engine)` as a small private switch returning the engine's typical extensions (`ue-pak` → `pak/utoc/ucas`, `bethesda` → `esp/esl/esm/bsa`, `fromsoft` → `dcx/bnd`, default → empty), and reuse the VM's existing meta-merge/persist helper for `MergeMeta` (find the method the manual-match path already uses; do not write a second persistence route).

- [ ] **Step 3: Wire the dialog in the window**

In `MainWindow`'s constructor, beside the other delegate wiring:

```csharp
        ViewModel.ReviewDiscoveries = async proposals =>
        {
            var dialog = new DiscoveryReviewDialog(proposals) { XamlRoot = Content.XamlRoot };
            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? dialog.Approved
                : Array.Empty<AdoptionProposal>();
        };
```

- [ ] **Step 4: Trigger it — auto on first add, manual after**

Call `await ViewModel.DiscoverExistingModsAsync(auto: true);` at the end of the add-game success path (where `MainWindow` finishes registering a newly added game), and add a "Find existing mods" item to the game's **More** menu that calls `DiscoverExistingModsAsync(auto: false)`.

- [ ] **Step 5: Clean-build and launch**

Run: `rm -rf src/ModManager.App/obj src/ModManager.App/bin && dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Version=0.17.0.0`
Then launch the produced exe and confirm it starts (a XAML resource typo only shows at runtime).
Expected: app launches; More menu shows "Find existing mods".

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs src/ModManager.App/MainWindow.xaml.cs src/ModManager.App/MainWindow.xaml src/ModManager.App/App.xaml.cs
git commit -m "feat(discovery): wire the sweep, review, and metadata-only adoption into the shell"
```

---

### Task 10: Smoke checklist + full verification

**Files:**
- Modify: `docs/smoke-tests/pending.md`

- [ ] **Step 1: Run everything**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```
Expected: all tests pass (including `CorePurityTests`), 0 build errors.

- [ ] **Step 2: Append the smoke section**

```markdown
---

## feat/discovery-sweep — find what's already there

**Shipped:** read-only sweep of the game folder (depth-capped) classifying signatures,
engine-shaped files, and archives; per-game Nexus name index (seed top-500 by endorsements,
grows from normal use, 5k cap, camelCase on disk); review-before-adopt dialog; adoption writes
METADATA ONLY. Name-search identify now offered for unidentified mods in any location, not just
loose-root.

**Smoke needed:**
1. Windrose (the richest case — years of hand-installed history): run "Find existing mods".
   Candidates appear with names where the index knows them. Confirm NO game file is listed.
2. Adopt everything, then confirm on disk that not one file moved. The mods appear in the list
   and can be toggled; the first toggle is the first file move.
3. Cancel the dialog — nothing is adopted, nothing is written.
4. Sign out of Nexus (or use the sealed build) and re-run: the sweep still finds mods and lists
   them as "not identified", still adoptable. The feature must not silently do nothing.
5. Drop a known mod's ORIGINAL archive into the game folder and re-run: it should match exactly
   by hash (md5 tier), not by name.
6. Add a brand-new game and confirm the sweep runs automatically once, and says nothing when it
   finds nothing.
```

- [ ] **Step 3: Commit and open the PR**

```bash
git add docs/smoke-tests/pending.md
git commit -m "docs(smoke): discovery sweep smoke checklist"
git push -u origin feat/discovery-sweep
gh pr create --fill --title "feat(discovery): find what's already there"
```

- [ ] **Step 4: Review gate**

Before merging, dispatch a fresh reviewer (and `core-purity-reviewer`, since Core gained a namespace). The two questions worth asking hardest: **can any input make the classifier claim a game file**, and **is there any path where adoption touches disk**.

---

## Self-Review

**Spec coverage:** sweep boundaries → Task 6 (depth cap, skip folders) + Task 9 (root + skip list); classify → Task 1; three evidence tiers → Task 4 (`AdoptionEvidence`), md5 wired in Tasks 6/9, index in Tasks 2/7; index seed/grow/bound/persist → Tasks 2, 3, 7; propose/review/adopt → Tasks 4, 8, 9; generalize LooseIdentify → Task 5; degradation → Task 7 (empty index on any failure) + smoke item 4; trigger → Task 9 step 4; testing → Tasks 1–5 unit, Task 10 smoke.

**Known gap, deliberate:** the user-pointed second path (a Downloads folder) is designed in the spec but not wired in Task 9 — `DiscoveryScanService.Sweep` already accepts any root, so it is one file-picker call. Ship the game-folder sweep first; add the picker in a follow-up once the review flow has been used on Windrose.

**Type consistency:** `DiscoveryCandidate(RelativePath, FileName, Kind)`, `DiscoverySweepOptions(ModPath, EngineExtensions, SkipFolders)`, `ModNameIndexEntry(ModId, Name, Author, Endorsements)`, `ModNameIndex.Merge/Match/Empty`, `AdoptionProposal.FromMd5/FromIndex/Unidentified/ToMeta`, `AdoptionEvidence.{Md5,NameIndex,None}` — used identically in every task that references them.
