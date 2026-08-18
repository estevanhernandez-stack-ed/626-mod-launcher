using ModManager.Core;
using ModManager.Core.LooseMods;

namespace ModManager.Tests;

/// <summary>
/// Wave 4 / A23. Death Stranding 2 listed 15 mods. Nine were mods. The others were
/// <c>LocalCacheWinGame</c>, <c>steaminput</c>, <c>tools</c>, <c>uds</c>, the user's own
/// <c>_MODS_STAGING</c>, and <c>reshade-shaders</c> — which was already listed inside the ReShade row.
/// The status line read <c>15 of 15 enabled</c>.
///
/// <para>Calling an unexplained directory a library is sound reasoning inside a folder dedicated to
/// mods. It is unsound when the mod location IS the game root, where an unexplained directory is just
/// the game.</para>
/// </summary>
public class LooseRootLibraryTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "looseroot-" + Guid.NewGuid().ToString("N"));
    private readonly string _gameRoot;

    public LooseRootLibraryTests()
    {
        _gameRoot = Path.Combine(_sandbox, "game");
        Directory.CreateDirectory(_gameRoot);
    }

    public void Dispose() { try { Directory.Delete(_sandbox, true); } catch { } }

    // The registration a loose-root game actually carries: one location whose path is the game root.
    private GameEntry LooseRootGame() => new()
    {
        Id = "loose-test",
        Engine = "decima",
        GameRoot = _gameRoot,
        DataDir = Path.Combine(_sandbox, "data"),
        FileExtensions = Array.Empty<string>(),
        ModLocations = new[] { new ModLocation("mods", "mods", ".") { Form = "loose-root" } },
    };

    private GameEntry DedicatedFolderGame()
    {
        Directory.CreateDirectory(Path.Combine(_gameRoot, "scripts"));
        return new GameEntry
        {
            Id = "folder-test",
            Engine = "custom",
            GameRoot = _gameRoot,
            DataDir = Path.Combine(_sandbox, "data2"),
            FileExtensions = new[] { "lua" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "scripts") },
        };
    }

    private void GameFolder(string name) => Directory.CreateDirectory(Path.Combine(_gameRoot, name));

    private void LoosePlugin(string name) => File.WriteAllText(Path.Combine(_gameRoot, name + ".asi"), "x");

    [Fact]
    public void The_games_own_folders_are_not_listed_as_mods()
    {
        // The exact five from the real install.
        foreach (var d in new[] { "LocalCacheWinGame", "steaminput", "tools", "uds", "_MODS_STAGING" })
            GameFolder(d);
        LoosePlugin("Zipliner_v1.1");

        var rows = ModListing.Resolve(LooseRootGame());

        Assert.DoesNotContain(rows, r => r.Class == "library");
        Assert.Contains(rows, r => r.Name.StartsWith("Zipliner", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_count_a_person_sees_matches_the_mods_that_are_there()
    {
        // "15 of 15 enabled" on an install with nine mods, in miniature.
        foreach (var d in new[] { "LocalCacheWinGame", "steaminput", "uds" }) GameFolder(d);
        LoosePlugin("Alpha");
        LoosePlugin("Beta");

        Assert.Equal(2, ModListing.Resolve(LooseRootGame()).Count);
    }

    [Fact]
    public void A_game_with_a_dedicated_mod_folder_still_infers_libraries()
    {
        // The rule must narrow to the game-root case ONLY. A library in a real mod folder is the whole
        // reason the inference exists, and _CatLib on Monster Hunter Wilds is the case it was built for.
        var game = DedicatedFolderGame();
        Directory.CreateDirectory(Path.Combine(_gameRoot, "scripts", "_CatLib"));
        File.WriteAllText(Path.Combine(_gameRoot, "scripts", "_CatLib", "init.lua"), "-- lib");
        File.WriteAllText(Path.Combine(_gameRoot, "scripts", "overlay.lua"), "-- mod");

        var rows = ModListing.Resolve(game);

        Assert.Contains(rows, r => r.Class == "library" && r.Name == "_CatLib");
    }

    [Fact]
    public void A_row_claims_the_files_it_owns_not_only_its_own_name()
    {
        // The ReShade case, tested where the decision actually lives. The row is called "ReShade" and
        // owns "reshade-shaders", so a claim set built from names alone let the same directory appear
        // twice — once inside ReShade, once as its own library row. That is not cosmetic: Play vanilla
        // moved the folder under the row that owns it while the duplicate sat there marked read-only,
        // which means a directory claimed twice is protected only as strongly as its weakest claim.
        var reshade = new Mod
        {
            Name = "ReShade",
            Files = new List<string> { "reshade-shaders", "ReShade.ini", "ReShadePreset.ini" },
        };

        var claimed = ModListing.ClaimedBy(new[] { reshade });

        Assert.Contains("ReShade", claimed);            // the name
        Assert.Contains("reshade-shaders", claimed);    // and the directory it owns
        Assert.Contains("ReShadePreset.ini", claimed);
    }

    [Fact]
    public void The_claim_set_ignores_case_the_way_the_filesystem_does()
    {
        var claimed = ModListing.ClaimedBy(new[] { new Mod { Name = "ReShade", Files = new List<string> { "reshade-shaders" } } });

        Assert.Contains("RESHADE-SHADERS", claimed);
    }

    [Fact]
    public void An_empty_listing_claims_nothing()
        => Assert.Empty(ModListing.ClaimedBy(Array.Empty<Mod>()));
}

/// <summary>
/// Wave 4 / A24. Toggling <c>version.dll</c> off warned that it was "the loader other mods inject
/// through" and that disabling it "disables every ASI plugin". The file's own version resource says
/// it is <b>DLSS Enabler 4.5.2.2</b>; the neighbouring <c>dxgi.dll</c> is <b>ReShade 6.7.3</b>.
/// Neither is an ASI loader. The label came from the filename and the consequence came from the label.
/// </summary>
public class LoaderIdentityTests
{
    [Fact]
    public void A_file_that_names_itself_is_named()
    {
        Assert.Equal("version.dll — DLSS Enabler 4.5.2.2",
            LoaderIdentity.Label("version.dll", "DLSS Enabler", "4.5.2.2"));
    }

    [Fact]
    public void A_file_that_says_nothing_is_not_given_an_invented_identity()
    {
        // Falls back to naming the file and calling it a loader — which is all we actually know.
        Assert.Equal("mystery.dll (loader)", LoaderIdentity.Label("mystery.dll", null, null));
        Assert.Equal("mystery.dll (loader)", LoaderIdentity.Label("mystery.dll", "   ", "1.0"));
    }

    [Fact]
    public void A_product_with_no_version_still_beats_the_filename()
        => Assert.Equal("dxgi.dll — ReShade", LoaderIdentity.Label("dxgi.dll", "ReShade", null));

    [Fact]
    public void The_consequence_for_a_known_product_is_the_real_one()
    {
        var reshade = LoaderIdentity.Consequence("dxgi.dll", "ReShade");
        Assert.Contains("ReShade", reshade);
        Assert.Contains("addons", reshade);

        var dlss = LoaderIdentity.Consequence("version.dll", "DLSS Enabler");
        Assert.Contains("frame generation", dlss);
        Assert.DoesNotContain("ASI", dlss);   // the sentence that was wrong
    }

    [Fact]
    public void An_unidentified_proxy_admits_what_it_does_not_know()
    {
        // The discovery review dialog's honest line, which this surface should have been using all
        // along instead of a confident claim about ASI plugins.
        var text = LoaderIdentity.Consequence("winmm.dll", null);

        Assert.Contains("can't tell which one", text);
        Assert.DoesNotContain("disables every ASI plugin", text);
    }

    [Fact]
    public void A_readable_but_unknown_product_is_quoted_without_a_guess_at_its_role()
    {
        var text = LoaderIdentity.Consequence("d3d11.dll", "Some Other Injector");

        Assert.Contains("Some Other Injector", text);
        Assert.Contains("rides on it", text);
    }

    [Fact]
    public void Reading_a_file_that_is_not_there_is_not_an_error()
    {
        // A loader we cannot read is a fallback case, not a failure.
        var (product, version) = LoaderIdentity.ReadProduct(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".dll"));

        Assert.Null(product);
        Assert.Null(version);
    }

    [Fact]
    public void The_row_KEY_never_moves_however_the_file_identifies_itself()
    {
        // The key names the holding folder of a disabled loader. If it changed, an already-disabled
        // loader's files would be orphaned — the row would come back looking for somewhere that no
        // longer matches. The identity work is display only, and this is the test that keeps it so.
        var identified = LooseModScan.Detect(
            new[] { "version.dll" }, Array.Empty<string>(), null, _ => ("DLSS Enabler", "4.5.2.2"));
        var anonymous = LooseModScan.Detect(new[] { "version.dll" }, Array.Empty<string>());

        Assert.Equal("version (ASI loader)", Assert.Single(identified).Name);
        Assert.Equal(Assert.Single(anonymous).Name, Assert.Single(identified).Name);
    }

    [Fact]
    public void The_row_says_what_the_file_is_when_the_file_says()
    {
        var row = Assert.Single(LooseModScan.Detect(
            new[] { "dxgi.dll" }, Array.Empty<string>(), null, _ => ("ReShade", "6.7.3.2148")));

        Assert.Contains("ReShade", row.Evidence);
        Assert.Contains("6.7.3.2148", row.Evidence);
    }

    [Fact]
    public void Without_an_identity_lookup_the_scan_behaves_exactly_as_before()
    {
        // The parameter is optional and the old callers pass nothing. This pins that.
        var row = Assert.Single(LooseModScan.Detect(new[] { "winhttp.dll" }, Array.Empty<string>()));

        Assert.Equal("proxy loader DLL in game root", row.Evidence);
        Assert.Equal("loader", row.Kind);
    }
}
