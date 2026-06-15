using System.IO.Compression;
using ModManager.Core;

namespace ModManager.Tests;

// Symptom from the field: a Lua mod installs but lands with NO metadata (bare row), and neither the
// backfill nor fetch-all buttons fix it — because the existing md5-identify keys mods via ZipModKeys,
// which filters to pak/ucas/utoc and so returns ZERO keys for a Scripts-only Lua archive. The fix:
// identify the dropped archive at install time and bind the metadata under the mod-FOLDER key (the
// same key the auto-located row uses), via INexusClient — no .pak required.
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

    // A Nexus stub that returns a fixed match for any md5 (simulating the archive being a known Nexus upload).
    private sealed class StubNexus : INexusClient
    {
        public int Calls;
        public Task<ModMeta?> GetModAsync(string d, int id) => Task.FromResult<ModMeta?>(null);
        public Task<NexusUser?> ValidateAsync() => Task.FromResult<NexusUser?>(new NexusUser("tester", false));
        public Task<NexusMd5Match?> GetByMd5Async(string domain, string md5)
        {
            Calls++;
            return Task.FromResult<NexusMd5Match?>(new NexusMd5Match(465, new ModMeta
            {
                Title = "Windrose Shanties Anywhere",
                Author = "SomeModder",
                Url = "https://www.nexusmods.com/windrose/mods/465",
                Source = "Nexus (Windrose)",
            }));
        }
        public Task<IReadOnlyList<NexusUpdateEntry>> GetRecentlyUpdatedAsync(string d, string p) => throw new NotSupportedException();
        public Task<EndorseOutcome> EndorseAsync(string d, int id, string v, EndorseAction a) => throw new NotSupportedException();
        public NexusRateLimit? LastRateLimit => null;
    }

    [Fact]
    public async Task IdentifyInstalledLuaMod_binds_nexus_metadata_under_the_mod_folder_key()
    {
        var ctx = Ctx();
        var nexus = new StubNexus();

        var matched = await Ue4ssLuaInstaller.IdentifyMetadataAsync(
            ctx, nexus, archivePath: ShantiesZip(), modName: "Windrose Shanties Anywhere");

        Assert.True(matched);
        Assert.Equal(1, nexus.Calls);

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
        var noHit = new NoMatchNexus();

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
            ctx, new StubNexus(), archivePath: ShantiesZip(), modName: "Windrose Shanties Anywhere");

        // Manual entries are locked — auto-identify must not overwrite the user's pick.
        Assert.Equal("My Hand-Picked Title", Scanner.LoadMetadata(ctx)["Windrose Shanties Anywhere"].Title);
    }

    private sealed class NoMatchNexus : INexusClient
    {
        public Task<ModMeta?> GetModAsync(string d, int id) => Task.FromResult<ModMeta?>(null);
        public Task<NexusUser?> ValidateAsync() => Task.FromResult<NexusUser?>(null);
        public Task<NexusMd5Match?> GetByMd5Async(string domain, string md5) => Task.FromResult<NexusMd5Match?>(null);
        public Task<IReadOnlyList<NexusUpdateEntry>> GetRecentlyUpdatedAsync(string d, string p) => throw new NotSupportedException();
        public Task<EndorseOutcome> EndorseAsync(string d, int id, string v, EndorseAction a) => throw new NotSupportedException();
        public NexusRateLimit? LastRateLimit => null;
    }
}
