# Vortex → Nexus Metadata Identification — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]`.

**Goal:** Fill metadata for Vortex-deployed mods (esp. UE4SS folder mods) by reading Vortex's deployment manifest to recover each folder's Nexus modId, then fetching metadata from the Nexus API.

**Why:** The only auto-metadata path that runs on already-installed (extracted) mods is CurseForge name-search. UE4SS mods are Nexus-sourced; Nexus md5 needs the original archive (gone after Vortex extracts). Vortex's `vortex.deployment.*.json` maps each deployed folder to its source archive name, which encodes the Nexus modId (`PetBoarPlus V1.0-227-1-0-...` → mod 227). Fetch by id — no archive needed.

**Architecture:** Pure-core `VortexManifest` parses the deployment manifest (folder → source → Nexus modId). `Scanner.IdentifyVortexNexusAsync` fetches each id via the existing `INexusClient.GetModAsync(domain, modId)` and merges into `metadata.json` (curated/existing wins, Nexus fills gaps — same MergeMeta convention as the other identify paths). Wired into the App's `FetchMetadata`. Only READS the Vortex file + WRITES our own `metadata.json` — no game-folder writes.

**Tech Stack:** .NET 10, C#, xUnit. Branch: fresh `feat/vortex-nexus-metadata` off `master` (PR #17 merged). Do NOT stack.

**Real manifest shape (verified):**
```json
{ "targetPath": "...\\ue4ss\\Mods", "files": [
  { "relPath": "PetBoarPlus\\config.txt", "source": "PetBoarPlus V1.0-227-1-0-1777312199" },
  { "relPath": "ExpandedPickupRadius1.2-134-1-2-1776872771\\Scripts\\main.lua", "source": "ExpandedPickupRadius1.2-134-1-2-1776872771" } ]}
```
Folder = first segment of `relPath`. Nexus modId = the first integer group in the source's trailing `-{modId}-{version}-{timestamp}` run (timestamp = trailing 9+ digits).

**Test/build:** `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --nologo` (EXPLICIT project). App: `dotnet build "...ModManager.App.csproj" -p:Platform=x64 --nologo`. PowerShell; `git -C "C:\Users\estev\Projects\626-mod-launcher" ...`. Trailer: `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>`.

Reference the existing `Md5IdentifyTests.cs` for the `INexusClient` fake + fixture pattern, and `Scanner.Md5IdentifyArchivesAsync` / `MergeMeta` for the merge convention + metadata key derivation (`Variant.ParseVariant(key).Base`).

---

## Task 1: `VortexManifest` — parse folder → Nexus modId

**Files:** Create `tests/ModManager.Tests/VortexManifestTests.cs`, `src/ModManager.Core/VortexManifest.cs`.

- [ ] **Step 1: Failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

// Vortex's deployment manifest maps each deployed folder to its Nexus source archive name, which
// encodes the modId: "<name> <ver>-<modId>-<fileId>-<timestamp>". We recover folder -> modId.
public class VortexManifestTests
{
    [Theory]
    [InlineData("PetBoarPlus V1.0-227-1-0-1777312199", 227)]
    [InlineData("ExpandedPickupRadius1.2-134-1-2-1776872771", 134)]
    [InlineData("Some-Dashed-Name-988-3-1-1700000000", 988)]
    public void ParseNexusModId_reads_the_modid(string source, int expected)
        => Assert.Equal(expected, VortexManifest.ParseNexusModId(source));

    [Theory]
    [InlineData("PlainFolderNoId")]
    [InlineData("")]
    [InlineData("NameWithTrailingNumber123")]   // no -id-..-timestamp run
    public void ParseNexusModId_is_null_without_the_pattern(string source)
        => Assert.Null(VortexManifest.ParseNexusModId(source));

    [Fact]
    public void Read_maps_each_folder_to_its_source_and_modid_deduped()
    {
        var dir = TestSupport.TempDir("vtx-");
        File.WriteAllText(Path.Combine(dir, "vortex.deployment.windrose.json"), """
        { "files": [
          { "relPath": "PetBoarPlus\\config.txt", "source": "PetBoarPlus V1.0-227-1-0-1777312199" },
          { "relPath": "PetBoarPlus\\Scripts\\main.lua", "source": "PetBoarPlus V1.0-227-1-0-1777312199" },
          { "relPath": "ExpandedPickupRadius1.2-134-1-2-1776872771\\enabled.txt", "source": "ExpandedPickupRadius1.2-134-1-2-1776872771" }
        ]}
        """);
        var refs = VortexManifest.Read(dir);
        Assert.Equal(2, refs.Count);                                        // deduped by folder
        Assert.Equal(227, refs.First(r => r.Folder == "PetBoarPlus").NexusModId);
        Assert.Equal(134, refs.First(r => r.Folder == "ExpandedPickupRadius1.2-134-1-2-1776872771").NexusModId);
    }

    [Fact]
    public void Read_returns_empty_when_no_manifest()
        => Assert.Empty(VortexManifest.Read(TestSupport.TempDir("vtx-none-")));
}
```

- [ ] **Step 2: Run red.**

- [ ] **Step 3: Implement**

Create `src/ModManager.Core/VortexManifest.cs`:

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>A folder Vortex deployed, with its source archive name and the Nexus modId we parsed from it.</summary>
public sealed record VortexModRef(string Folder, string Source, int? NexusModId);

/// <summary>
/// Reads Vortex's deployment manifest (vortex.deployment.*.json) to recover, for each deployed
/// folder, the Nexus modId encoded in its source archive name — the only reliable handle on a
/// Vortex-extracted mod (its download archive is gone). Pure System.IO + regex.
/// </summary>
public static class VortexManifest
{
    // Nexus download name: "<name> <ver>-<modId>-<rest>-<timestamp>"; modId = first int group in the
    // trailing run, timestamp = trailing 9+ digits. Requires that trailing run to avoid false hits.
    private static readonly Regex ModIdRe = new(@"-(\d+)-.*-\d{9,}$", RegexOptions.Compiled);

    public static int? ParseNexusModId(string? source)
    {
        if (string.IsNullOrEmpty(source)) return null;
        var m = ModIdRe.Match(source);
        return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    public static IReadOnlyList<VortexModRef> Read(string modsDir)
    {
        string[] manifests;
        try { manifests = Directory.GetFiles(modsDir, "vortex.deployment.*.json"); }
        catch { return Array.Empty<VortexModRef>(); }

        var byFolder = new Dictionary<string, VortexModRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in manifests)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("files", out var files)) continue;
                foreach (var el in files.EnumerateArray())
                {
                    var rel = el.TryGetProperty("relPath", out var rp) ? rp.GetString() : null;
                    var src = el.TryGetProperty("source", out var sp) ? sp.GetString() : null;
                    if (string.IsNullOrEmpty(rel) || string.IsNullOrEmpty(src)) continue;
                    var folder = rel.Replace('/', '\\').Split('\\')[0];
                    if (folder.Length == 0 || byFolder.ContainsKey(folder)) continue;
                    byFolder[folder] = new VortexModRef(folder, src!, ParseNexusModId(src));
                }
            }
            catch { /* skip a malformed manifest */ }
        }
        return byFolder.Values.ToList();
    }
}
```

- [ ] **Step 4: Run green** (filter), then full suite.
- [ ] **Step 5: Commit** `feat: VortexManifest — recover folder -> Nexus modId from the deployment manifest`

---

## Task 2: `Scanner.IdentifyVortexNexusAsync` — fetch by id + merge

**Files:** Modify `src/ModManager.Core/Scanner.cs`; create `tests/ModManager.Tests/VortexNexusIdentifyTests.cs`.

- [ ] **Step 1: Failing test** (mirror `Md5IdentifyTests` fake `INexusClient`):

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class VortexNexusIdentifyTests
{
    private sealed class FakeNexus : INexusClient
    {
        private readonly Func<string, int, Task<ModMeta?>> _byId;
        public FakeNexus(Func<string, int, Task<ModMeta?>> byId) => _byId = byId;
        public Task<ModMeta?> GetModAsync(string gameDomain, int modId) => _byId(gameDomain, modId);
        public Task<NexusMd5Match?> GetByMd5Async(string g, string m) => throw new NotSupportedException();
        public Task<NexusUser?> ValidateAsync() => throw new NotSupportedException();
    }

    private static (string modsDir, GameContext c) Fixture(string? domain)
    {
        var root = TestSupport.TempDir("vtxid-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "ue4ss", "Mods");
        Directory.CreateDirectory(Path.Combine(modsDir, "PetBoarPlus"));
        File.WriteAllText(Path.Combine(modsDir, "vortex.deployment.windrose.json"), """
        { "files": [ { "relPath": "PetBoarPlus\\Scripts\\main.lua", "source": "PetBoarPlus V1.0-227-1-0-1777312199" } ]}
        """);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("ue4ss", "UE4SS", "ue4ss/Mods") { Form = "folders" } },
            NexusGameDomain = domain,
        });
        return (modsDir, c);
    }

    [Fact]
    public async Task Identifies_a_vortex_folder_by_nexus_modid()
    {
        var (_, c) = Fixture("windrose");
        int? seenId = null; string? seenDomain = null;
        var fake = new FakeNexus((d, id) => { seenDomain = d; seenId = id;
            return Task.FromResult<ModMeta?>(new ModMeta { Title = "Pet Boar Plus", Author = "IceBox", Url = "https://www.nexusmods.com/windrose/mods/227" }); });

        var r = await Scanner.IdentifyVortexNexusAsync(c, fake);

        Assert.Equal("windrose", seenDomain);
        Assert.Equal(227, seenId);
        Assert.Equal(1, r.Matched);
        var meta = Scanner.LoadMetadata(c);
        var key = Variant.ParseVariant("PetBoarPlus").Base;
        Assert.Equal("Pet Boar Plus", meta[key].Title);
        Assert.Equal("IceBox", meta[key].Author);
    }

    [Fact]
    public async Task Returns_zero_without_a_nexus_domain()
    {
        var (_, c) = Fixture(null);
        var fake = new FakeNexus((_, _) => Task.FromResult<ModMeta?>(new ModMeta { Title = "X" }));
        Assert.Equal(0, (await Scanner.IdentifyVortexNexusAsync(c, fake)).Matched);
    }

    [Fact]
    public async Task Skips_folders_without_a_parseable_modid()
    {
        var (modsDir, c) = Fixture("windrose");
        File.WriteAllText(Path.Combine(modsDir, "vortex.deployment.windrose.json"), """
        { "files": [ { "relPath": "CleanName\\main.lua", "source": "CleanName" } ]}
        """);
        Assert.Equal(0, (await Scanner.IdentifyVortexNexusAsync(c, new FakeNexus((_, _) => Task.FromResult<ModMeta?>(new ModMeta { Title = "X" })))).Matched);
    }
}
```

- [ ] **Step 2: Run red.**

- [ ] **Step 3: Implement** — add to `Scanner` (place near `Md5IdentifyArchivesAsync`; reuse `MergeMeta` + key convention). NOTE: a Nexus-id match is exact provenance, so it should override an existing weaker match the same way the archive-md5 path does (`MergeMeta(existing, nexusMeta)` → Nexus wins, existing fills gaps — check the exact `MergeMeta` arg order used by `Md5IdentifyArchivesAsync` and match it):

```csharp
    /// <summary>
    /// Identify already-installed Vortex-deployed mods by the Nexus modId recorded in Vortex's
    /// deployment manifest, fetching metadata from Nexus by id (no archive needed). Merges Nexus as
    /// authoritative provenance (same as the archive-md5 path); curated/CF fills what Nexus lacks.
    /// </summary>
    public static async Task<IdentifyResult> IdentifyVortexNexusAsync(GameContext c, INexusClient client)
    {
        var domain = c.Game.NexusGameDomain;
        if (string.IsNullOrEmpty(domain)) return new IdentifyResult(0);

        var meta = LoadMetadata(c);
        var matched = 0;
        foreach (var loc in c.Locations)
        {
            foreach (var r in VortexManifest.Read(loc.Abs))
            {
                if (r.NexusModId is not int id) continue;
                ModMeta? hit;
                try { hit = await client.GetModAsync(domain!, id); } catch { continue; }
                if (hit is null) continue;
                var key = Variant.ParseVariant(r.Folder).Base;
                meta[key] = MergeMeta(meta.GetValueOrDefault(key) ?? new ModMeta(), hit); // Nexus wins, existing fills
                matched++;
            }
        }
        if (matched > 0) SaveMetadata(c, meta);
        return new IdentifyResult(matched);
    }
```

- [ ] **Step 4: Run green** (filter), then full suite.
- [ ] **Step 5: Commit** `feat: IdentifyVortexNexusAsync — fill metadata for Vortex mods via Nexus modid`

---

## Task 3: Wire into `FetchMetadata` (App)

**Files:** Modify `src/ModManager.App/ViewModels/MainViewModel.cs` (`FetchMetadata`). Build-verified.

- [ ] **Step 1:** In `FetchMetadata`, after the CF name-search, when Nexus is connected, also run the Vortex→Nexus pass and fold its count into the status. Read the current method (around line 456) and adapt:

```csharp
            var r = await Scanner.RefreshMetadataByNameAsync(_ctx, _svc.CurseForge);
            var vtx = 0;
            if (_nexus.IsConnected)
            {
                try { vtx = (await Scanner.IdentifyVortexNexusAsync(_ctx, _nexus.Client!)).Matched; }
                catch { /* best-effort; CF result still stands */ }
            }
            await ReloadModsAsync();
            StatusText = r.GameId is null
                ? (vtx > 0 ? $"Filled {vtx} Vortex mod(s) from Nexus." : "Couldn't resolve this game on CurseForge.")
                : $"Matched {r.Matched} of {r.Total} on CurseForge" + (vtx > 0 ? $", +{vtx} from Vortex/Nexus." : ".");
```

(Match the real method's structure — keep its `IsBusy`/try-finally. Ensure a reload so the new metadata shows. If `FetchMetadata` already reloads, don't double-reload.)

- [ ] **Step 2: Build** the App (x64), 0 errors.
- [ ] **Step 3: Commit** `feat: FetchMetadata also fills Vortex-deployed mods via Nexus modid`

---

## Task 4: Verify + push
- [ ] Full suite green (only 7z/rar SKIPs). App builds x64.
- [ ] Push `feat/vortex-nexus-metadata`; open PR vs `master`.

## Deferred / follow-on
- Tag UE4SS built-in framework mods ("BPModLoaderMod", "ConsoleEnablerMod", "shared", etc.) as built-in so they don't read as unidentified.
- Folder-name modId fallback (when no Vortex manifest but the folder name itself carries the id) — `VortexManifest.ParseNexusModId(folderName)`.

## Self-Review
**Coverage:** parse folder→modId (T1), fetch+merge (T2), wire (T3). Built-in tagging + folder-name fallback explicitly deferred. ✅
**No game-folder writes:** `IdentifyVortexNexusAsync` only reads the Vortex manifest + writes our `metadata.json` (data dir) — owned-folder invariant untouched. ✅
**Type consistency:** `VortexManifest.ParseNexusModId(string?)->int?`, `Read(string)->IReadOnlyList<VortexModRef>`, `VortexModRef(Folder,Source,NexusModId)`, `Scanner.IdentifyVortexNexusAsync(c, INexusClient)->IdentifyResult`, `INexusClient.GetModAsync(domain,modId)`. Verify `MergeMeta` arg order against `Md5IdentifyArchivesAsync` (Nexus authoritative). ✅
