using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Wave 4 / A25. Dropping a zip on Death Stranding 2 installed correctly and recorded nothing: no
/// <c>installs/</c> directory, and the row read <i>"Detected: loose .asi in game root"</i> — the
/// inference — about a file 626 had placed thirty seconds earlier.
///
/// <para><c>Scanner.ExecuteIntake</c> writes its manifest at the very end, deliberately, so a manifest
/// never claims a file that is not on disk. The loose-root branch returned into
/// <c>DirectInject.Execute</c> before ever reaching it.</para>
///
/// <para>It matters beyond bookkeeping: a loose-root row never shows the trash can, because the
/// launcher will not delete loose files in a folder holding the game's own executables when it cannot
/// prove which are the mod's. The manifest IS that proof.</para>
/// </summary>
public class LooseRootProvenanceTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "prov-" + Guid.NewGuid().ToString("N"));
    private readonly string _gameRoot;
    private readonly string _dataDir;
    private readonly string _incoming;

    public LooseRootProvenanceTests()
    {
        _gameRoot = Path.Combine(_sandbox, "game");
        _dataDir = Path.Combine(_sandbox, "data");
        _incoming = Path.Combine(_sandbox, "incoming");
        Directory.CreateDirectory(_gameRoot);
        Directory.CreateDirectory(_incoming);
    }

    public void Dispose() { try { Directory.Delete(_sandbox, true); } catch { } }

    private GameContext Ctx() => Scanner.GameContext(new GameEntry
    {
        Id = "prov-test",
        Engine = "decima",
        GameRoot = _gameRoot,
        DataDir = _dataDir,
        FileExtensions = Array.Empty<string>(),
        ModLocations = new[] { new ModLocation("mods", "mods", ".") { Form = "loose-root" } },
    });

    private string Incoming(string name, string body = "x")
    {
        var p = Path.Combine(_incoming, name);
        File.WriteAllText(p, body);
        return p;
    }

    private IntakeResult Install(params string[] paths)
    {
        var ctx = Ctx();
        var plan = Scanner.PlanIntake(paths, ctx);
        return Scanner.ExecuteIntake(plan, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ctx);
    }

    private IReadOnlyList<ModInstallManifest> Manifests() => ModInstallRegistry.List(_dataDir);

    [Fact]
    public void A_loose_root_install_records_what_it_placed()
    {
        var src = Incoming("SmokeDrop626.asi");

        var result = Install(src);

        Assert.Contains("SmokeDrop626.asi", result.Added);
        var manifest = Assert.Single(Manifests());
        Assert.Equal("SmokeDrop626.asi", Assert.Single(manifest.Files));
        Assert.Equal("SmokeDrop626.asi", manifest.SourceArchive);
    }

    [Fact]
    public void The_record_names_the_location_it_wrote_into()
    {
        Install(Incoming("Thing.asi"));

        Assert.Equal("mods", Assert.Single(Manifests()).Location);
    }

    [Fact]
    public void The_placed_file_can_be_traced_back_to_its_install()
    {
        // The question uninstall needs answered: which files are this mod's? Inference cannot answer
        // it in a folder full of the game's own DLLs; the manifest can.
        Install(Incoming("Traceable.asi"));

        var owner = Manifests().Single(m => m.Files.Contains("Traceable.asi", StringComparer.OrdinalIgnoreCase));

        Assert.Equal("Traceable.asi", owner.SourceArchive);
    }

    [Fact]
    public void Two_dropped_files_produce_two_records()
    {
        Install(Incoming("One.asi"), Incoming("Two.asi"));

        Assert.Equal(2, Manifests().Count);
    }

    [Fact]
    public void Nothing_placed_records_nothing()
    {
        // An empty drop must not leave an empty manifest behind — a record of an install that did not
        // happen is exactly the kind of claim an uninstall would later act on.
        Install();

        Assert.Empty(Manifests());
    }

    [Fact]
    public void A_manifest_never_claims_a_file_that_is_not_on_disk()
    {
        // The reason the write sits after every copy has settled. Whatever the result reports as
        // landed is what gets claimed, and every claim must resolve to a real file in the game root.
        Install(Incoming("Real.asi"), Incoming("AlsoReal.asi"));

        foreach (var manifest in Manifests())
            foreach (var file in manifest.Files)
                Assert.True(File.Exists(Path.Combine(_gameRoot, file)), $"manifest claims a missing file: {file}");
    }

    [Fact]
    public void A_second_drop_of_the_same_file_does_not_lose_the_first_record()
    {
        // Re-dropping is a real user action — the update path. Whatever the second run decides, the
        // launcher must not end up with no record of a file that is sitting in the game folder.
        Install(Incoming("Repeat.asi"));
        Install(Incoming("Repeat.asi"));

        Assert.NotEmpty(Manifests());
        Assert.Contains(Manifests(), m => m.Files.Contains("Repeat.asi", StringComparer.OrdinalIgnoreCase));
    }

}

/// <summary>
/// Wave 4 / A26. Uninstall deleted exactly the file its manifest claimed — and left the manifest,
/// claiming a path that no longer existed. <c>ModInstallRegistry.Remove</c> existed with zero call
/// sites: the invariant was held at write time and dropped at delete time.
///
/// <para>Tested on the FOLDER lane, because that is where uninstall exists at all. A loose-root row has
/// no trash can (A25), which is why this was found on Monster Hunter Wilds rather than on the game that
/// produced the rest of this wave.</para>
/// </summary>
public class UninstallForgetsTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "unin-" + Guid.NewGuid().ToString("N"));
    private readonly string _gameRoot;
    private readonly string _dataDir;
    private readonly string _mods;
    private readonly string _incoming;

    public UninstallForgetsTests()
    {
        _gameRoot = Path.Combine(_sandbox, "game");
        _dataDir = Path.Combine(_sandbox, "data");
        _mods = Path.Combine(_gameRoot, "mods");
        _incoming = Path.Combine(_sandbox, "incoming");
        Directory.CreateDirectory(_mods);
        Directory.CreateDirectory(_incoming);
    }

    public void Dispose() { try { Directory.Delete(_sandbox, true); } catch { } }

    private GameContext Ctx() => Scanner.GameContext(new GameEntry
    {
        Id = "unin-test",
        Engine = "custom",
        GameRoot = _gameRoot,
        DataDir = _dataDir,
        GroupingRule = "filename_no_ext",
        FileExtensions = new[] { "pak" },
        ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
    });

    private string Incoming(string name)
    {
        var p = Path.Combine(_incoming, name);
        File.WriteAllText(p, "x");
        return p;
    }

    private void Install(params string[] paths)
    {
        var ctx = Ctx();
        Scanner.ExecuteIntake(Scanner.PlanIntake(paths, ctx), new HashSet<string>(StringComparer.OrdinalIgnoreCase), ctx);
    }

    private IReadOnlyList<ModInstallManifest> Manifests() => ModInstallRegistry.List(_dataDir);

    [Fact]
    public async Task Uninstalling_forgets_the_record_of_what_it_deleted()
    {
        Install(Incoming("Doomed.pak"));
        Assert.Single(Manifests());

        await Scanner.UninstallModAsync("Doomed", Ctx());

        Assert.False(File.Exists(Path.Combine(_mods, "Doomed.pak")));
        Assert.Empty(Manifests());
    }

    [Fact]
    public async Task Uninstalling_one_mod_leaves_another_mods_record_alone()
    {
        // The claim is per file. Forgetting too much would strand a mod that is still installed, which
        // is the same class of error as remembering too much, in the other direction.
        Install(Incoming("Keeper.pak"), Incoming("Doomed.pak"));
        Assert.Equal(2, Manifests().Count);

        await Scanner.UninstallModAsync("Doomed", Ctx());

        var left = Assert.Single(Manifests());
        Assert.Equal("Keeper.pak", left.SourceArchive);
        Assert.True(File.Exists(Path.Combine(_mods, "Keeper.pak")));
    }

    [Fact]
    public async Task A_record_never_outlives_the_files_it_claims()
    {
        // The invariant stated at the write site, checked from the delete end: every claim on disk must
        // resolve to a file that is on disk.
        Install(Incoming("A.pak"), Incoming("B.pak"));

        await Scanner.UninstallModAsync("A", Ctx());

        foreach (var manifest in Manifests())
            foreach (var file in manifest.Files)
                Assert.True(File.Exists(Path.Combine(_mods, file)), $"stale claim: {file}");
    }
}
