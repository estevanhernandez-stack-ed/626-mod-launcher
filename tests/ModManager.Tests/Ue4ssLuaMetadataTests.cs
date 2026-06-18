using System.IO.Compression;
using ModManager.Core;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests;

// Symptom from the field: a Lua mod installs but lands with NO metadata (bare row), and neither the
// backfill nor fetch-all buttons fix it — because the existing md5-identify keys mods via ZipModKeys,
// which filters to pak/ucas/utoc and so returns ZERO keys for a Scripts-only Lua archive. The fix:
// identify the dropped archive at install time and bind the metadata under the mod-FOLDER key (the
// same key the auto-located row uses), via the mod-source plugin (IModSource) — no .pak required.
public class Ue4ssLuaMetadataTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "ue4ss-meta-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    // A Windrose-shaped context (ue-pak, real Nexus domain) with a temp data dir so metadata.json lands here.
    private GameContext Ctx()
    {
        var dataDir = Path.Combine(_tmp, "data");
        Directory.CreateDirectory(dataDir);
        var game = new GameEntry
        {
            Id = "windrose", GameName = "Windrose", Engine = "ue-pak",
            GameRoot = Path.Combine(_tmp, "GameRoot"),
            FileExtensions = new[] { "pak", "ucas", "utoc" },
            DataDir = dataDir,
            NexusGameDomain = "windrose",
        };
        return Scanner.GameContext(game);
    }

    private string ShantiesZip()
    {
        var zipPath = Path.Combine(_tmp, "shanties.zip");
        using var stream = File.Create(zipPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        using (var e = zip.CreateEntry("Windrose Shanties Anywhere v1/Windrose Shanties Anywhere/Scripts/main.lua").Open())
            e.Write(new byte[] { 1, 2, 3 }, 0, 3);
        return zipPath;
    }

    // A mod-source stub that returns a fixed identify hit for any md5 (simulating the archive being a known Nexus upload).
    private sealed class StubModSource : IModSource
    {
        public int Calls;
        public string Id => "nexus";
        public bool RequiresApiKey => true;
        public Task<SourceIdentifyResult?> IdentifyByHashAsync(string domain, string md5)
        {
            Calls++;
            return Task.FromResult<SourceIdentifyResult?>(new SourceIdentifyResult(
                new SourceModRef("nexus", domain, 465, ""),
                new SourceModMetadata(null, null, null, null, null,
                    Title: "Windrose Shanties Anywhere",
                    Author: "SomeModder",
                    ModUrl: "https://www.nexusmods.com/windrose/mods/465")));
        }
        public Task<SourceModMetadata?> FetchMetadataAsync(SourceModRef modRef) => throw new NotSupportedException();
        public Task<bool> IsUpdateAvailableAsync(SourceModRef modRef, string installedVersion) => throw new NotSupportedException();
        public Task<EndorseResult> SetEndorsedAsync(SourceModRef modRef, bool endorsed) => throw new NotSupportedException();
    }

    [Fact]
    public async Task IdentifyInstalledLuaMod_binds_nexus_metadata_under_the_mod_folder_key()
    {
        var ctx = Ctx();
        var source = new StubModSource();

        var matched = await Ue4ssLuaInstaller.IdentifyMetadataAsync(
            ctx, source, archivePath: ShantiesZip(), modName: "Windrose Shanties Anywhere");

        Assert.True(matched);
        Assert.Equal(1, source.Calls);

        // The metadata must be keyed by the mod-FOLDER name — the key the auto-located row uses.
        var meta = Scanner.LoadMetadata(ctx);
        Assert.True(meta.ContainsKey("Windrose Shanties Anywhere"));
        Assert.Equal("Windrose Shanties Anywhere", meta["Windrose Shanties Anywhere"].Title);
        Assert.Equal("md5", meta["Windrose Shanties Anywhere"].SourceConfidence);
    }

    [Fact]
    public async Task IdentifyInstalledLuaMod_is_a_safe_noop_when_nexus_has_no_match()
    {
        var ctx = Ctx();
        var noHit = new NoMatchModSource();

        var matched = await Ue4ssLuaInstaller.IdentifyMetadataAsync(
            ctx, noHit, archivePath: ShantiesZip(), modName: "Windrose Shanties Anywhere");

        Assert.False(matched);
        Assert.False(Scanner.LoadMetadata(ctx).ContainsKey("Windrose Shanties Anywhere"));
    }

    [Fact]
    public async Task IdentifyInstalledLuaMod_does_not_clobber_a_manual_entry()
    {
        var ctx = Ctx();
        Scanner.WriteOneMeta(ctx, "Windrose Shanties Anywhere",
            new ModMeta { Title = "My Hand-Picked Title", IsManual = true });

        await Ue4ssLuaInstaller.IdentifyMetadataAsync(
            ctx, new StubModSource(), archivePath: ShantiesZip(), modName: "Windrose Shanties Anywhere");

        // Manual entries are locked — auto-identify must not overwrite the user's pick.
        Assert.Equal("My Hand-Picked Title", Scanner.LoadMetadata(ctx)["Windrose Shanties Anywhere"].Title);
    }

    private sealed class NoMatchModSource : IModSource
    {
        public string Id => "nexus";
        public bool RequiresApiKey => true;
        public Task<SourceIdentifyResult?> IdentifyByHashAsync(string domain, string md5) => Task.FromResult<SourceIdentifyResult?>(null);
        public Task<SourceModMetadata?> FetchMetadataAsync(SourceModRef modRef) => throw new NotSupportedException();
        public Task<bool> IsUpdateAvailableAsync(SourceModRef modRef, string installedVersion) => throw new NotSupportedException();
        public Task<EndorseResult> SetEndorsedAsync(SourceModRef modRef, bool endorsed) => throw new NotSupportedException();
    }
}
