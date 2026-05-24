using System.Text;
using ModManager.Core;

namespace ModManager.Tests;

// Ports smart-intake.test.js — fingerprintIdentify (hash a dropped file, ask CurseForge, merge).
public class SmartIntakeTests
{
    private static (GameContext c, string modsDir) Fixture()
    {
        var root = TestSupport.TempDir("smartintake-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "jar" }, GroupingRule = "filename_no_ext",
        });
        return (c, modsDir);
    }

    [Fact]
    public async Task Writes_cf_metadata_for_a_recognized_dropped_file()
    {
        var (c, modsDir) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "JustEnoughItems.jar"), "JEI BYTES");
        long fpVal = Fingerprint.CurseForgeFingerprint(Encoding.UTF8.GetBytes("JEI BYTES"));
        var client = new FakeCurseForgeClient
        {
            OnGetFingerprintMatches = fps => Task.FromResult<IReadOnlyList<FingerprintMatch>>(
                fps.Contains(fpVal) ? new[] { new FingerprintMatch(238222, fpVal) } : Array.Empty<FingerprintMatch>()),
            OnGetMods = ids => Task.FromResult<IReadOnlyList<ModMeta>>(
                ids.Contains(238222) ? new[] { new ModMeta { Title = "Just Enough Items", Author = "mezz", Url = "https://cf/jei", CurseforgeId = 238222 } } : Array.Empty<ModMeta>()),
        };

        var r = await Scanner.FingerprintIdentifyAsync(c, client, new[] { "JustEnoughItems.jar" });

        Assert.Equal(1, r.Matched);
        var meta = Scanner.LoadMetadata(c);
        Assert.Equal("mezz", meta["JustEnoughItems"].Author);
        Assert.Equal(238222, meta["JustEnoughItems"].CurseforgeId);
        Assert.Equal("Just Enough Items", meta["JustEnoughItems"].Title);
    }

    [Fact]
    public async Task Writes_nothing_when_curseforge_does_not_recognize()
    {
        var (c, modsDir) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "Custom.jar"), "unknown bytes");
        var client = new FakeCurseForgeClient
        {
            OnGetFingerprintMatches = _ => Task.FromResult<IReadOnlyList<FingerprintMatch>>(Array.Empty<FingerprintMatch>()),
            OnGetMods = _ => Task.FromResult<IReadOnlyList<ModMeta>>(Array.Empty<ModMeta>()),
        };

        var r = await Scanner.FingerprintIdentifyAsync(c, client, new[] { "Custom.jar" });

        Assert.Equal(0, r.Matched);
        Assert.Empty(Scanner.LoadMetadata(c));
    }

    [Fact]
    public async Task Preserves_curated_fields_fills_gaps_from_cf()
    {
        var (c, modsDir) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "JustEnoughItems.jar"), "JEI BYTES");
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta> { ["JustEnoughItems"] = new() { Title = "JEI (my note)" } });
        long fpVal = Fingerprint.CurseForgeFingerprint(Encoding.UTF8.GetBytes("JEI BYTES"));
        var client = new FakeCurseForgeClient
        {
            OnGetFingerprintMatches = _ => Task.FromResult<IReadOnlyList<FingerprintMatch>>(new[] { new FingerprintMatch(238222, fpVal) }),
            OnGetMods = _ => Task.FromResult<IReadOnlyList<ModMeta>>(new[] { new ModMeta { Title = "Just Enough Items", Author = "mezz", CurseforgeId = 238222 } }),
        };

        await Scanner.FingerprintIdentifyAsync(c, client, new[] { "JustEnoughItems.jar" });

        var e = Scanner.LoadMetadata(c)["JustEnoughItems"];
        Assert.Equal("JEI (my note)", e.Title);
        Assert.Equal("mezz", e.Author);
    }
}
