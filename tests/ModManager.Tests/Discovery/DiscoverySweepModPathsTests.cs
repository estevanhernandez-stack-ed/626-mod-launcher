using ModManager.Core;
using ModManager.Core.Discovery;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Discovery;

/// <summary>
/// B2 from the registration-repair smoke. The discovery sweep took its extensions from the resolved
/// context (<c>ctx.DeclaredExts</c>) and its mod folders from the RAW registration
/// (<c>ctx.Game.ModLocations</c>). On a registration the game definition had corrected, the sweep
/// looked in a folder the launcher never lists from and never called a file in the folder it does
/// list engine-shaped. Folders and extensions now come from the same resolved context the mod list
/// uses.
///
/// <para>Mutates the process-global EffectiveManifest, so it joins the serialized ManifestState
/// collection and resets the remote on the way out.</para>
/// </summary>
[Collection("ManifestState")]
public class DiscoverySweepModPathsTests : IDisposable
{
    private const string Id = "sweep-corrected-path";

    public void Dispose() => EffectiveManifest.SetRemote(null);

    private static GameContext CorrectedContext(string root)
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = Id,
                    Name = "Sweep Corrected Path",
                    Engine = "custom",
                    ModPath = "nativePC",
                    FileExtensions = new[] { "arc" },
                    Provenance = new ManifestProvenance { Sources = new[] { "known-engines" } },
                },
            },
        });
        return Scanner.GameContext(new GameEntry
        {
            Id = Id,
            GameName = "Sweep Corrected Path",
            GameRoot = root,
            Engine = "custom",
            FileExtensions = new[] { "pak" },
            GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },   // the stale preset default
            DataDir = Path.Combine(root, "_data"),
        });
    }

    [Fact]
    public void The_sweep_looks_in_the_folder_the_mod_list_reads()
    {
        var ctx = CorrectedContext(TestSupport.TempDir("sweep-corrected-"));

        var paths = DiscoverySweep.ModPathsFor(ctx);

        var only = Assert.Single(paths);
        Assert.Equal("nativePC", only.Path);
        Assert.False(only.PaksRoot);
    }

    // The A5 repro, end to end through the classifier: the probe under the effective folder is
    // offered, the one under the stale raw folder is not.
    [Fact]
    public void A_probe_in_the_effective_folder_is_offered_and_one_in_the_stale_folder_is_not()
    {
        var ctx = CorrectedContext(TestSupport.TempDir("sweep-corrected-probe-"));
        var options = new DiscoverySweepOptions(
            ModPaths: DiscoverySweep.ModPathsFor(ctx),
            EngineExtensions: ctx.DeclaredExts,
            SkipFolders: new[] { "_626mods", "loose-disabled", "disabled" });
        var files = new[]
        {
            new SweptFile("nativePC/626smoke/A5SweepProbe.arc", 10),
            new SweptFile("mods/626smoke/RawPathProbe.arc", 10),
        };

        var found = DiscoverySweep.Classify(files, options);

        var offered = Assert.Single(found);
        Assert.Equal("A5SweepProbe.arc", offered.FileName);
        Assert.Equal(DiscoveryKind.EngineShaped, offered.Kind);
    }

    // The same resolver as the listing means the same appended folders too: a launcher-owned UE4SS
    // mods folder the scanner lists from is a folder the sweep looks in.
    [Fact]
    public void A_launcher_appended_location_is_swept_like_the_listing_reads_it()
    {
        var root = TestSupport.TempDir("sweep-ue4ss-");
        var win64 = Path.Combine(root, "Binaries", "Win64");
        Directory.CreateDirectory(Path.Combine(win64, "ue4ss", "Mods"));
        var ctx = CorrectedContext(root);
        GameShapeTests.WriteUe4ssManifest(ctx.DataDir, win64);
        ctx = Scanner.GameContext(ctx.Game);

        var paths = DiscoverySweep.ModPathsFor(ctx).Select(p => p.Path).ToList();

        Assert.Equal(new[] { "nativePC", "Binaries/Win64/ue4ss/Mods" }, paths);
    }

    // A location that resolves outside the game folder cannot be matched against a path relative to
    // it, so it is dropped rather than passed through to match the wrong thing.
    [Fact]
    public void A_location_outside_the_game_folder_is_dropped()
    {
        var root = TestSupport.TempDir("sweep-outside-");
        var outside = TestSupport.TempDir("sweep-elsewhere-");
        EffectiveManifest.SetRemote(null);
        var ctx = Scanner.GameContext(new GameEntry
        {
            Id = "sweep-outside-test", GameRoot = root, Engine = "custom",
            FileExtensions = new[] { "pak" },
            ModLocations = new[]
            {
                new ModLocation("mods", "Mods", "mods"),
                new ModLocation("mods2", "Elsewhere", outside),
            },
            DataDir = Path.Combine(root, "_data"),
        });

        var paths = DiscoverySweep.ModPathsFor(ctx).Select(p => p.Path).ToList();

        Assert.Equal(new[] { "mods" }, paths);
    }

    [Fact]
    public void A_paks_root_location_keeps_its_base_game_guard()
    {
        var root = TestSupport.TempDir("sweep-paksroot-");
        EffectiveManifest.SetRemote(null);
        var ctx = Scanner.GameContext(new GameEntry
        {
            Id = "sweep-paksroot-test", GameRoot = root, Engine = "custom",
            FileExtensions = new[] { "pak" },
            ModLocations = new[] { new ModLocation("mods", "Paks", "Game/Content/Paks") { Form = "paks-root" } },
            DataDir = Path.Combine(root, "_data"),
        });

        var only = Assert.Single(DiscoverySweep.ModPathsFor(ctx));

        Assert.Equal("Game/Content/Paks", only.Path);
        Assert.True(only.PaksRoot);
    }
}
