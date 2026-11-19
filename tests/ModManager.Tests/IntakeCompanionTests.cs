using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Intake dropped companion files (found building #396 and #398, 2026-10-16). Those PRs taught the SCANNER that a
/// Fallout 4 / Starfield plugin owns its <c>&lt;Plugin&gt; - Main.ba2</c> / <c>- Textures.ba2</c> archives, and that a
/// Cyberpunk <c>.xl</c> can be a mod on its own. Intake still kept only entries wearing a declared extension, and
/// neither preset declares those: a Fallout 4 zip installed its plugin without the archives it loads, and a Cyberpunk
/// zip holding only an <c>.xl</c> installed nothing and said "not a mod file".
///
/// <para>Intake now carries what the scanner understands, by the scanner's own rules, so a mod lands whole and
/// lists as the one row it is. Junk (readmes, images) still stays out.</para>
/// </summary>
public class IntakeCompanionTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-intake-comp-");
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static readonly string[] Fo4Exts = { "esp", "esl", "esm", "bsa" };

    private (string Mods, GameContext C) Game(string id, string[] exts, string engine, string modRel)
    {
        var gameRoot = Path.Combine(_root, id);
        var mods = Path.Combine(gameRoot, modRel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(mods);
        var game = new GameEntry
        {
            Id = id, GameName = id, Engine = engine, GameRoot = gameRoot, FileExtensions = exts,
            GroupingRule = "filename_no_ext", DataDir = Path.Combine(_root, "data-" + id),
            ModLocations = new[] { new ModLocation("mods", modRel, modRel) },
        };
        Directory.CreateDirectory(game.DataDir!);
        return (mods, Scanner.GameContext(game));
    }

    private (string Mods, GameContext C) Fallout4() => Game("fo4", Fo4Exts, "bethesda", "Data");
    private (string Mods, GameContext C) Cyberpunk() => Game("cp", new[] { "archive" }, "custom", "archive/pc/mod");

    private string Zip(string name, params string[] entries)
    {
        var path = Path.Combine(_root, name);
        TestSupport.WriteZip(path, entries.Select(e => (e, "bytes-of-" + Path.GetFileName(e))).ToArray());
        return path;
    }

    private static List<string> Planned(IntakePlan plan) => plan.ToAdd.Select(i => i.RelPath.Replace('\\', '/')).OrderBy(n => n, StringComparer.Ordinal).ToList();

    private static void Install(IntakePlan plan, GameContext c) => Scanner.ExecuteIntake(plan, new HashSet<string>(), c);

    [Fact]
    public void A_Fallout_4_zip_installs_its_plugin_with_the_archives_it_loads()
    {
        var (mods, c) = Fallout4();
        var zip = Zip("MyMod.zip", "MyMod/MyMod.esp", "MyMod/MyMod - Main.ba2", "MyMod/MyMod - Textures.ba2", "MyMod/readme.txt");

        var plan = Scanner.PlanIntake(new[] { zip }, c);

        Assert.Equal(new[] { "MyMod - Main.ba2", "MyMod - Textures.ba2", "MyMod.esp" }, Planned(plan));
        Install(plan, c);
        Assert.True(File.Exists(Path.Combine(mods, "MyMod - Main.ba2")));
        Assert.True(File.Exists(Path.Combine(mods, "MyMod - Textures.ba2")));
        Assert.False(File.Exists(Path.Combine(mods, "readme.txt")));
    }

    [Fact]
    public async Task The_installed_mod_is_one_row_and_its_archives_turn_off_and_on_with_it()
    {
        var (mods, c) = Fallout4();
        Install(Scanner.PlanIntake(new[] { Zip("MyMod.zip", "MyMod.esp", "MyMod - Main.ba2") }, c), c);

        var row = Assert.Single(await Scanner.BuildModListAsync(c), m => m.Name.StartsWith("MyMod", StringComparison.OrdinalIgnoreCase));
        await ModToggle.SetEnabledWithOutcomeAsync(c, row, false);
        Assert.False(File.Exists(Path.Combine(mods, "MyMod - Main.ba2")));
        await ModToggle.SetEnabledWithOutcomeAsync(c, (await Scanner.BuildModListAsync(c)).Single(m => m.Name == row.Name), true);
        Assert.Equal("bytes-of-MyMod - Main.ba2", File.ReadAllText(Path.Combine(mods, "MyMod - Main.ba2")));
    }

    [Fact]
    public void A_texture_only_Fallout_4_zip_installs_its_archives()
    {
        var (_, c) = Fallout4();
        var plan = Scanner.PlanIntake(new[] { Zip("Retex.zip", "Retex - Main.ba2", "Retex - Textures.ba2", "preview.jpg") }, c);
        Assert.Equal(new[] { "Retex - Main.ba2", "Retex - Textures.ba2" }, Planned(plan));
    }

    [Fact]
    public void A_Cyberpunk_zip_holding_only_an_xl_installs_it()
    {
        var (mods, c) = Cyberpunk();
        var plan = Scanner.PlanIntake(new[] { Zip("QTF.zip", "QuestTriggerFixes.xl", "README.md") }, c);

        Assert.Equal(new[] { "QuestTriggerFixes.xl" }, Planned(plan));
        Install(plan, c);
        Assert.True(File.Exists(Path.Combine(mods, "QuestTriggerFixes.xl")));
    }

    [Fact]
    public void A_Cyberpunk_archive_brings_its_paired_xl()
    {
        var (_, c) = Cyberpunk();
        var plan = Scanner.PlanIntake(new[] { Zip("Cronos.zip", "Cronos.archive", "Cronos.archive.xl") }, c);
        Assert.Equal(new[] { "Cronos.archive", "Cronos.archive.xl" }, Planned(plan));
    }

    [Fact]
    public void A_dropped_folder_carries_the_same_companions()
    {
        var (_, c) = Fallout4();
        var folder = Path.Combine(_root, "drop", "MyMod");
        Directory.CreateDirectory(folder);
        foreach (var f in new[] { "MyMod.esp", "MyMod - Main.ba2", "notes.txt" })
            File.WriteAllText(Path.Combine(folder, f), f);

        var plan = Scanner.PlanIntake(new[] { folder }, c);

        Assert.Equal(new[] { "MyMod - Main.ba2", "MyMod.esp" }, Planned(plan));
    }

    [Fact]
    public void A_lone_archive_dropped_by_itself_is_installed_not_refused()
    {
        var (_, c) = Fallout4();
        var file = Path.Combine(_root, "Retex - Main.ba2");
        File.WriteAllText(file, "x");

        var plan = Scanner.PlanIntake(new[] { file }, c);

        Assert.Equal(new[] { "Retex - Main.ba2" }, Planned(plan));
        Assert.Empty(plan.Unsafe);
    }

    [Fact]
    public void A_game_whose_extensions_carry_no_companions_is_unchanged()
    {
        // A pak game: a .ba2 or .xl in its zip is not a mod there, and stays out as before.
        var (_, c) = Game("pak", new[] { "pak" }, "ue-pak", "Content/Paks/~mods");
        var plan = Scanner.PlanIntake(new[] { Zip("Odd.zip", "Thing.pak", "Thing - Main.ba2", "Thing.xl") }, c);
        Assert.Equal(new[] { "Thing.pak" }, Planned(plan));
    }

    [Fact]
    public void A_companion_with_a_traversal_path_still_lands_inside_the_mod_folder()
    {
        var (mods, c) = Fallout4();
        var plan = Scanner.PlanIntake(new[] { Zip("Evil.zip", "MyMod.esp", "../../MyMod - Main.ba2") }, c);
        Install(plan, c);
        Assert.True(File.Exists(Path.Combine(mods, "MyMod - Main.ba2")));
        Assert.False(File.Exists(Path.Combine(_root, "MyMod - Main.ba2")));
    }
}
