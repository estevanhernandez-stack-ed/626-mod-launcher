using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// A7: the intake sites compare <c>ctx.Exts</c> to a filename's extension by LITERAL equality
/// (<see cref="Intake.ClassifyDrop"/>), so the list they read must be the extensions themselves —
/// not a regex-escaped copy of them. A game declaring <c>mod+pak</c> used to get
/// <c>["mod\+pak"]</c> here, and every real <c>foo.mod+pak</c> classified as "skip": listed by the
/// scanner (whose regex escapes correctly), refused by intake.
///
/// <para>The other half of the contract is pinned below too: the empty→<c>["pak"]</c> substitution
/// STAYS. An extension-less registration is a pak game to <see cref="GameContext.FileRe"/>,
/// <c>Scanner.ModKeyFor</c>, and the listing lane; dropping the substitution here alone would make
/// intake the only reader that disagrees, and a listed mod would become un-addable.</para>
/// </summary>
public class IntakeExtensionComparisonTests
{
    private static (string src, string modsDir, GameContext c) Fixture(params string[] exts)
    {
        var root = TestSupport.TempDir("intake-ext-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(src);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = exts,
        });
        return (src, modsDir, c);
    }

    // ---------- the reported bug, through each reachable call path ----------

    [Fact]
    public void PlanIntake_takes_a_loose_file_whose_extension_holds_a_regex_metacharacter()
    {
        var (src, _, c) = Fixture("mod+pak");
        var file = Path.Combine(src, "Cool.mod+pak");
        File.WriteAllText(file, "MOD");

        var plan = Scanner.PlanIntake(new[] { file }, c);

        Assert.Contains(plan.ToAdd, i => i.Name == "Cool.mod+pak");
        Assert.Empty(plan.Unsafe);
    }

    [Fact]
    public void PlanIntake_takes_a_zip_entry_whose_extension_holds_a_regex_metacharacter()
    {
        var (src, _, c) = Fixture("mod+pak");
        var zip = Path.Combine(src, "pack.zip");
        TestSupport.WriteZip(zip, ("Cool.mod+pak", "MOD"), ("readme.txt", "notes"));

        var plan = Scanner.PlanIntake(new[] { zip }, c);

        Assert.Contains(plan.ToAdd, i => i.Name == "Cool.mod+pak");
    }

    [Fact]
    public async Task AddMods_places_a_metacharacter_extension_file_and_walks_a_dropped_folder()
    {
        var (src, modsDir, c) = Fixture("mod+pak");
        var drop = Path.Combine(src, "MyMod");
        Directory.CreateDirectory(drop);
        File.WriteAllText(Path.Combine(drop, "Cool.mod+pak"), "MOD");

        var r = await Scanner.AddModsAsync(new[] { drop }, c);

        Assert.Contains("Cool.mod+pak", r.Added);
        Assert.Equal("MOD", TestSupport.Read(Path.Combine(modsDir, "Cool.mod+pak")));
    }

    [Fact]
    public async Task AddMods_extracts_a_metacharacter_extension_entry_from_a_dropped_zip()
    {
        var (src, modsDir, c) = Fixture("mod+pak");
        var zip = Path.Combine(src, "pack.zip");
        TestSupport.WriteZip(zip, ("Cool.mod+pak", "MOD"));

        var r = await Scanner.AddModsAsync(new[] { zip }, c);

        Assert.Contains("Cool.mod+pak", r.Added);
        Assert.Equal("MOD", TestSupport.Read(Path.Combine(modsDir, "Cool.mod+pak")));
    }

    [Fact]
    public void ArchiveModKeysFor_keys_an_archive_by_its_metacharacter_extension_contents()
    {
        var (src, _, c) = Fixture("mod+pak");
        var zip = Path.Combine(src, "CoolMod-42-1-0-9999.zip");
        TestSupport.WriteZip(zip, ("Cool.mod+pak", "MOD"));

        var keys = Scanner.ArchiveModKeysFor(zip, c);

        Assert.Equal(new[] { "Cool" }, keys);
    }

    [Fact]
    public async Task CaptureReadmes_caches_under_the_key_a_metacharacter_extension_mod_uses()
    {
        var (src, _, c) = Fixture("mod+pak");
        var zip = Path.Combine(src, "pack.zip");
        TestSupport.WriteZip(zip, ("Cool.mod+pak", "MOD"), ("README.md", "# Cool"));

        await Scanner.AddModsAsync(new[] { zip }, c);

        Assert.Equal("# Cool", TestSupport.Read(Scanner.ReadmePathFor("Cool", c)!));
    }

    // ---------- the contract that must NOT change ----------

    [Fact]
    public void Exts_carries_the_extensions_themselves_never_a_regex_escaped_copy()
    {
        var (_, _, c) = Fixture("mod+pak", ".Suit");

        Assert.Equal(new[] { "mod+pak", "suit" }, c.Exts); // lowercased, dot-stripped, unescaped
    }

    [Fact]
    public void FileRe_still_escapes_so_a_metacharacter_extension_stays_data_not_pattern()
    {
        var (_, _, c) = Fixture("mod+pak");

        Assert.Equal(@"\.(mod\+pak)$", c.FileRe.ToString());
        Assert.Matches(c.FileRe, "Cool.mod+pak");
        Assert.DoesNotMatch(c.FileRe, "Cool.modpak");  // the "+" is a literal, not a quantifier
        Assert.DoesNotMatch(c.FileRe, "Cool.modddpak");
    }

    [Fact]
    public void FileRe_is_unchanged_for_the_ordinary_shapes()
    {
        Assert.Equal(@"\.(pak|ucas|utoc)$", Fixture("pak", "ucas", "utoc").c.FileRe.ToString());
        Assert.Equal(@"\.(smpcmod|suit)$", Fixture(".SMPCMOD", ".suit").c.FileRe.ToString());
        Assert.Equal(@"\.(pak)$", Fixture().c.FileRe.ToString());        // empty -> pak
        Assert.Equal(@"\.(pak)$", Fixture(".", "..").c.FileRe.ToString()); // nothing but dots -> pak
    }

    [Fact]
    public void An_extension_less_registration_still_intakes_pak_files_like_the_scanner_lists_them()
    {
        // The empty→["pak"] substitution is load-bearing for intake, not just for the regex: with no
        // declared extensions the scanner's own regex says "pak", so a .pak in the mod folder is
        // LISTED as a mod. Intake has to agree, or the user can see a mod they cannot add.
        var (src, modsDir, c) = Fixture();
        Assert.Matches(c.FileRe, "cool.pak"); // the listing lane's answer
        var file = Path.Combine(src, "cool.pak");
        File.WriteAllText(file, "MOD");

        var plan = Scanner.PlanIntake(new[] { file }, c);
        Scanner.ExecuteIntake(plan, new HashSet<string>(), c);

        Assert.Equal("MOD", TestSupport.Read(Path.Combine(modsDir, "cool.pak")));
    }

    [Fact]
    public void An_extension_less_registration_still_skips_what_the_scanner_would_not_list()
    {
        var (src, _, c) = Fixture();
        var file = Path.Combine(src, "notes.txt");
        File.WriteAllText(file, "x");

        var plan = Scanner.PlanIntake(new[] { file }, c);

        Assert.Empty(plan.ToAdd);
        Assert.Contains(plan.Unsafe, s => s.Name == "notes.txt" && s.Reason == "not a mod file");
    }
}
