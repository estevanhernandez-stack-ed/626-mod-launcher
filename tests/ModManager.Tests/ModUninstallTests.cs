using ModManager.Core;

namespace ModManager.Tests;

/// <summary>E1, fifth slice: the uninstall rule and deletes the app's row and the agent share.</summary>
public class ModUninstallTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-uninstall-");
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    // The ME2 removal moved from the app's ModEngineService into Core (ModEngine2Writer.RemoveMod).
    [Fact]
    public void A_mod_engine_2_mod_loses_its_folder_and_its_config_line_and_keeps_the_others()
    {
        var gameRoot = Path.Combine(_root, "ELDEN RING");
        var me2 = Path.Combine(gameRoot, "ModEngine2");
        Directory.CreateDirectory(Path.Combine(me2, "mod", "ashes"));
        File.WriteAllText(Path.Combine(me2, "mod", "ashes", "a.dcx"), "x");
        Directory.CreateDirectory(Path.Combine(me2, "mod", "randomizer"));
        var cfg = Path.Combine(me2, "config_eldenring.toml");
        File.WriteAllText(cfg, """
[extension.mod_loader]
mods = [
    { enabled = true, name = "ashes", path = "mod/ashes" },
    { enabled = false, name = "rando", path = "mod/randomizer" }
]
""");
        var g = new GameEntry { Id = "er", GameName = "ER", Engine = "fromsoft", GameRoot = gameRoot, DataDir = Path.Combine(_root, "data"), ModEngineConfig = cfg };
        var ctx = Scanner.GameContext(g);
        var ashes = ModListing.Resolve(g).Single(m => m.Name == "ashes");

        Assert.Null(ModUninstall.Refusal(ctx, ashes));
        ModUninstall.Run(ctx, ashes);

        Assert.False(Directory.Exists(Path.Combine(me2, "mod", "ashes")));
        Assert.Equal(new[] { "rando" }, ModEngine2Config.ParseMods(File.ReadAllText(cfg)).Select(m => m.Name));
        Assert.True(Directory.Exists(Path.Combine(me2, "mod", "randomizer")));
    }

    // Review on #373: rows the listing appends are not installed mods; the scanner's uninstall can't find
    // them by name and the app reported "Uninstalled" having deleted nothing.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_proxy_loader_or_library_row_is_not_an_installed_mod(bool proxyLoader)
    {
        var row = proxyLoader
            ? new Mod { Name = "REFramework", Location = ProxyLoaderRows.LocationTag, IsLoader = true }
            : new Mod { Name = "UE4SS shared", Class = "library", Location = "mods" };

        Assert.Equal(UninstallBlock.NotAnInstalledMod, ModUninstall.Refusal(ListingMechanism.Scanner, row)!.Kind);
    }

    [Fact]
    public void A_family_with_one_refused_member_deletes_none_of_them()
    {
        var g = new GameEntry { Id = "g", GameName = "G", Engine = "ue-pak", GameRoot = _root, DataDir = Path.Combine(_root, "d"),
            ModLocations = new List<ModLocation> { new("mods", "Mods", "Mods") }, FileExtensions = new List<string> { "pak" } };
        Directory.CreateDirectory(Path.Combine(_root, "Mods"));
        var pak = Path.Combine(_root, "Mods", "Faster_5x_P.pak");
        File.WriteAllText(pak, "x");
        var ctx = Scanner.GameContext(g);
        var real = ModListing.Resolve(g).Single();
        var managed = new Mod { Name = "Faster_10x", ReadOnly = true, Files = new List<string> { "Faster_10x_P.pak" }, Location = "mods" };

        Assert.Throws<InvalidOperationException>(() => ModUninstall.RunAll(ctx, new[] { real, managed }));
        Assert.True(File.Exists(pak));
    }

    [Fact]
    public void A_mod_another_tool_manages_is_refused_and_running_it_throws()
    {
        var g = new GameEntry { Id = "g", GameName = "G", Engine = "ue-pak", GameRoot = _root, DataDir = Path.Combine(_root, "d"),
            ModLocations = new List<ModLocation> { new("mods", "Mods", "Mods") } };
        Directory.CreateDirectory(Path.Combine(_root, "Mods"));
        var ctx = Scanner.GameContext(g);
        var managed = new Mod { Name = "Managed", ReadOnly = true, Files = new List<string> { "Managed.pak" }, Location = "mods" };

        Assert.Equal(UninstallBlock.ManagedByAnotherTool, ModUninstall.Refusal(ctx, managed)!.Kind);
        Assert.Throws<InvalidOperationException>(() => ModUninstall.Run(ctx, managed));
    }
    // B4 stage two: a turned-off mod can hold its extra-tree entries in disabled-trees/<Mod>, and a live one
    // can hold leftovers there. Este, 2026-10-02: "let them know what it's going to do and let them choose to
    // cancel or to proceed." So uninstall previews the held folder and then deletes it with the mod.
    private (GameEntry Game, GameContext Ctx) TreeGame()
    {
        var gameRoot = Path.Combine(_root, "cp");
        Directory.CreateDirectory(Path.Combine(gameRoot, "archive", "pc", "mod"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "r6", "scripts", "CoolMod"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "r6", "tweaks"));
        File.WriteAllText(Path.Combine(gameRoot, "archive", "pc", "mod", "CoolMod.archive"), "MAIN");
        File.WriteAllText(Path.Combine(gameRoot, "r6", "scripts", "CoolMod", "main.reds"), "SCRIPTS");
        File.WriteAllText(Path.Combine(gameRoot, "r6", "tweaks", "CoolMod.yaml"), "TWEAK");
        File.WriteAllText(Path.Combine(gameRoot, "archive", "pc", "mod", "Plain.archive"), "PLAIN");
        var g = new GameEntry
        {
            Id = "cp", GameName = "CP", Engine = "custom", GameRoot = gameRoot, DataDir = Path.Combine(_root, "cp-data"),
            FileExtensions = new[] { "archive" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "archive/pc/mod") },
        };
        return (g, Scanner.GameContext(g, extraModTrees: new[] { "r6/scripts", "r6/tweaks" }));
    }

    // A held folder for a mod that is not the one being uninstalled: it must survive.
    private static string HoldForSomeoneElse(GameContext ctx)
    {
        var other = Path.Combine(TreeHolding.ModDir(ctx, "Bystander"), "r6", "scripts", "Bystander", "b.reds");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, "BYSTANDER");
        return other;
    }

    [Fact]
    public async Task A_turned_off_mod_with_held_extras_is_uninstalled_with_its_held_folder()
    {
        var (g, ctx) = TreeGame();
        await Scanner.DisableModAsync("CoolMod", ctx);
        var heldDir = TreeHolding.ModDir(ctx, "CoolMod");
        Assert.True(File.Exists(Path.Combine(heldDir, "r6", "scripts", "CoolMod", "main.reds"))); // pre-condition
        var bystander = HoldForSomeoneElse(ctx);
        var row = ModListing.Resolve(g).Single(m => m.Name == "CoolMod");

        Assert.Null(ModUninstall.Refusal(ctx, row));
        ModUninstall.Run(ctx, row);

        Assert.False(Directory.Exists(heldDir));
        Assert.DoesNotContain(ModListing.Resolve(g), m => m.Name == "CoolMod");
        Assert.False(Directory.Exists(Path.Combine(ctx.DisabledRoot, "CoolMod")));
        Assert.Equal("BYSTANDER", File.ReadAllText(bystander));
    }

    [Fact]
    public void A_live_mod_with_leftovers_held_is_uninstalled_with_its_held_folder()
    {
        var (g, ctx) = TreeGame();
        var leftoverDir = TreeHolding.ModDir(ctx, "Plain");
        var leftover = Path.Combine(leftoverDir, "r6", "scripts", "Plain", "old.reds");
        Directory.CreateDirectory(Path.GetDirectoryName(leftover)!);
        File.WriteAllText(leftover, "LEFT");
        var bystander = HoldForSomeoneElse(ctx);
        var row = ModListing.Resolve(g).Single(m => m.Name == "Plain");
        Assert.True(row.Enabled); // pre-condition: the mod is on

        Assert.Null(ModUninstall.Refusal(ctx, row));
        ModUninstall.Run(ctx, row);

        Assert.False(Directory.Exists(leftoverDir));
        Assert.DoesNotContain(ModListing.Resolve(g), m => m.Name == "Plain");
        Assert.Equal("BYSTANDER", File.ReadAllText(bystander));
        Assert.Equal("MAIN", File.ReadAllText(Path.Combine(ctx.GameRoot, "archive", "pc", "mod", "CoolMod.archive")));
    }

    [Fact]
    public async Task A_turned_off_mod_with_no_held_extras_uninstalls_as_before()
    {
        var (g, ctx) = TreeGame();
        await Scanner.DisableModAsync("Plain", ctx);
        var row = ModListing.Resolve(g).Single(m => m.Name == "Plain");

        Assert.Null(ModUninstall.Refusal(ctx, row));
        ModUninstall.Run(ctx, row);

        Assert.False(Directory.Exists(Path.Combine(ctx.DisabledRoot, "Plain")));
        Assert.Equal("SCRIPTS", File.ReadAllText(Path.Combine(ctx.GameRoot, "r6", "scripts", "CoolMod", "main.reds")));
    }

    // ---- Preview ----

    [Fact]
    public async Task Preview_lists_the_held_folder_and_its_trees_and_reads_nothing_away()
    {
        var (g, ctx) = TreeGame();
        await Scanner.DisableModAsync("CoolMod", ctx);
        var row = ModListing.Resolve(g).Single(m => m.Name == "CoolMod");

        var preview = ModUninstall.Preview(ctx, row);

        var mod = Assert.Single(preview.Mods);
        Assert.Equal("CoolMod", mod.Name);
        Assert.Equal(row.Files, mod.Files);
        var held = Assert.Single(preview.HeldFolders);
        Assert.Equal("CoolMod", held.ModName);
        Assert.Equal(TreeHolding.ModDir(ctx, "CoolMod"), held.Path);
        Assert.Equal(new[] { "r6/scripts", "r6/tweaks" }, held.Trees);
        Assert.False(held.Unreadable);
        Assert.False(preview.HeldFolderUnreadable);
        // Read-only: everything held is still held.
        Assert.True(File.Exists(Path.Combine(held.Path, "r6", "scripts", "CoolMod", "main.reds")));
        Assert.True(File.Exists(Path.Combine(held.Path, "r6", "tweaks", "CoolMod.yaml")));
    }

    [Fact]
    public void Preview_of_a_mod_holding_nothing_has_no_held_folders()
    {
        var (g, ctx) = TreeGame();
        var row = ModListing.Resolve(g).Single(m => m.Name == "Plain");

        var preview = ModUninstall.Preview(ctx, row);

        Assert.Equal("Plain", Assert.Single(preview.Mods).Name);
        Assert.Empty(preview.HeldFolders);
    }

    [Fact]
    public void Preview_lists_a_held_folder_whose_files_fit_no_declared_tree()
    {
        var (g, ctx) = TreeGame();
        var stray = Path.Combine(TreeHolding.ModDir(ctx, "Plain"), "r6", "gone", "Plain", "x.reds");
        Directory.CreateDirectory(Path.GetDirectoryName(stray)!);
        File.WriteAllText(stray, "STRAY");
        var row = ModListing.Resolve(g).Single(m => m.Name == "Plain");

        var held = Assert.Single(ModUninstall.Preview(ctx, row).HeldFolders);

        Assert.Equal(TreeHolding.ModDir(ctx, "Plain"), held.Path);
        Assert.Empty(held.Trees);
        Assert.True(File.Exists(stray));
    }

    [Fact]
    public void Preview_says_so_when_the_holding_folder_cant_be_read()
    {
        var (g, ctx) = TreeGame();
        var row = ModListing.Resolve(g).Single(m => m.Name == "Plain");
        TreeHolding.BeforeReadForTests = _ => throw new UnauthorizedAccessException("no");
        UninstallPreview preview;
        try { preview = ModUninstall.Preview(ctx, row); }
        finally { TreeHolding.BeforeReadForTests = null; }

        var held = Assert.Single(preview.HeldFolders);
        Assert.True(held.Unreadable);
        Assert.True(preview.HeldFolderUnreadable);
        Assert.Equal(TreeHolding.ModDir(ctx, "Plain"), held.Path);
    }

    [Fact]
    public async Task Preview_of_a_family_covers_every_member()
    {
        var (g, ctx) = TreeGame();
        await Scanner.DisableModAsync("CoolMod", ctx);
        var rows = ModListing.Resolve(g).ToList();

        var preview = ModUninstall.Preview(ctx, rows);

        Assert.Equal(rows.Select(r => r.Name), preview.Mods.Select(m => m.Name));
        Assert.Equal("CoolMod", Assert.Single(preview.HeldFolders).ModName);
    }

    // ---- The confirm dialog's held sentence (strings verbatim from the brief and round-1 rulings) ----

    private static UninstallPreview With(params UninstallHeldFolder[] held)
        => new(new[] { new UninstallPreviewMod("CoolMod", new[] { "CoolMod.archive" }) }, held);

    private static UninstallPreview Family(params UninstallHeldFolder[] held)
        => new(new[]
        {
            new UninstallPreviewMod("A", new[] { "A.archive" }),
            new UninstallPreviewMod("B", new[] { "B.archive" }),
        }, held);

    [Fact]
    public void The_held_sentence_names_the_trees_of_one_held_folder()
        => Assert.Equal("626 is also holding some of its files in r6/scripts, r6/tweaks, and will delete those too.",
            With(new UninstallHeldFolder("CoolMod", @"D:\d\disabled-trees\CoolMod", new[] { "r6/scripts", "r6/tweaks" }, false)).HeldSentence());

    [Fact]
    public void The_held_sentence_for_an_unreadable_folder_names_its_path()
        => Assert.Equal(@"626 couldn't read D:\d\disabled-trees\CoolMod to see what it's holding for it; anything there will be deleted too.",
            With(new UninstallHeldFolder("CoolMod", @"D:\d\disabled-trees\CoolMod", Array.Empty<string>(), true)).HeldSentence());

    [Fact]
    public void The_held_sentence_gives_a_folder_whose_files_fit_no_tree_its_own_sentence()
        => Assert.Equal(@"626 is also holding files for it in D:\d\disabled-trees\CoolMod and will delete those too.",
            With(new UninstallHeldFolder("CoolMod", @"D:\d\disabled-trees\CoolMod", Array.Empty<string>(), false)).HeldSentence());

    [Fact]
    public void The_held_sentence_never_splices_a_path_into_the_tree_list()
        => Assert.Equal("626 is also holding some of their files in r6/scripts, and will delete those too. "
                        + @"626 is also holding files for them in D:\d\disabled-trees\B and will delete those too.",
            Family(new UninstallHeldFolder("A", @"D:\d\disabled-trees\A", new[] { "r6/scripts" }, false),
                   new UninstallHeldFolder("B", @"D:\d\disabled-trees\B", Array.Empty<string>(), false)).HeldSentence());

    [Fact]
    public void The_held_sentence_aggregates_a_family_without_repeating_a_tree()
        => Assert.Equal("626 is also holding some of their files in r6/scripts, r6/tweaks, and will delete those too.",
            Family(new UninstallHeldFolder("A", @"D:\d\disabled-trees\A", new[] { "r6/scripts" }, false),
                   new UninstallHeldFolder("B", @"D:\d\disabled-trees\B", new[] { "r6/scripts", "r6/tweaks" }, false)).HeldSentence());

    [Fact]
    public void Nothing_held_means_no_held_sentence()
        => Assert.Null(With().HeldSentence());

    // ---- Safety rails on the held-folder delete ----

    // A genuine escape: the name resolves outside the holding root. Refused, because the main uninstall would
    // misbehave too: it deletes disabled/<name> recursively, which for ".." is the whole data folder.
    [Theory]
    [InlineData("..\\x")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("..\\..\\x")]
    public void A_mod_name_that_escapes_the_holding_root_throws_and_deletes_nothing(string name)
    {
        var (g, ctx) = TreeGame();
        var outside = Path.Combine(ctx.DataDir, "x", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.WriteAllText(outside, "KEEP");
        var bystander = HoldForSomeoneElse(ctx);
        var pak = Path.Combine(ctx.GameRoot, "archive", "pc", "mod", "Plain.archive");
        var row = new Mod { Name = name, Location = "mods", Enabled = true, Files = new List<string> { "Plain.archive" } };

        var e = Assert.ThrowsAny<InvalidOperationException>(() => ModUninstall.Run(ctx, row));
        Assert.Contains("holding folder", e.Message);   // refused by the containment rail, not by chance
        Assert.ThrowsAny<InvalidOperationException>(() => ModUninstall.Preview(ctx, row));

        Assert.Equal("KEEP", File.ReadAllText(outside));
        Assert.Equal("BYSTANDER", File.ReadAllText(bystander));
        Assert.Equal("PLAIN", File.ReadAllText(pak));
    }

    // Round 2: a name that stays inside the root but can't own a folder there as written (Windows strips a
    // trailing dot or space; ':' and separators aren't one folder name) holds nothing. It is not refused,
    // since that made such Mod Engine 2 mods impossible to uninstall, and it never reaches the folder Windows
    // would normalise it onto.
    [Theory]
    [InlineData("Bystander.")]
    [InlineData("Bystander ")]
    [InlineData("Bystander:alt")]
    [InlineData("x\\..\\Bystander")]
    public void A_name_that_cant_own_a_held_folder_holds_nothing_and_never_reaches_one(string name)
    {
        var (g, ctx) = TreeGame();
        var bystander = HoldForSomeoneElse(ctx);
        var row = new Mod { Name = name, Location = "mods", Enabled = true, Files = new List<string> { "Plain.archive" } };

        Assert.Empty(ModUninstall.Preview(ctx, row).HeldFolders);
        Assert.Null(ModUninstall.Preview(ctx, row).HeldSentence());
        Assert.Empty(ModUninstall.Run(ctx, row));

        Assert.Equal("BYSTANDER", File.ReadAllText(bystander));
    }

    // The same, for a mod the listing really shows: its main files go, and the held folder of the mod whose
    // name it normalises onto survives.
    [Theory]
    [InlineData("Foo.")]
    [InlineData("Foo ")]
    public void A_listed_mod_whose_name_ends_in_a_dot_or_space_uninstalls_and_leaves_Foos_held_folder(string name)
    {
        var (g, ctx) = TreeGame();
        var archive = Path.Combine(ctx.GameRoot, "archive", "pc", "mod", name + ".archive");
        File.WriteAllText(archive, "FOO-ISH");
        var foosHeld = Path.Combine(TreeHolding.ModDir(ctx, "Foo"), "r6", "scripts", "Foo", "f.reds");
        Directory.CreateDirectory(Path.GetDirectoryName(foosHeld)!);
        File.WriteAllText(foosHeld, "FOO");
        var row = ModListing.Resolve(g).Single(m => m.Name == name);   // pre-condition: listed under that name

        Assert.Empty(ModUninstall.Preview(ctx, row).HeldFolders);
        ModUninstall.Run(ctx, row);

        Assert.False(File.Exists(archive));
        Assert.DoesNotContain(ModListing.Resolve(g), m => m.Name == name);
        Assert.Equal("FOO", File.ReadAllText(foosHeld));
    }

    // An 8.3 short name resolves to the long-named folder it abbreviates. A mod literally named like the
    // alias must not reach another mod's held folder through it.
    [Fact]
    public void A_mod_named_like_another_held_folders_short_name_does_not_reach_it()
    {
        var (g, ctx) = TreeGame();
        var longDir = TreeHolding.ModDir(ctx, "Other Long Name Mod");
        var victim = Path.Combine(longDir, "r6", "scripts", "Other Long Name Mod", "o.reds");
        Directory.CreateDirectory(Path.GetDirectoryName(victim)!);
        File.WriteAllText(victim, "OTHER");
        var alias = ShortNameOf(longDir);
        if (alias is null)
        {
            // xUnit 2 has no runtime skip. 8.3 generation is off on this volume, so the alias this test
            // needs cannot exist and there is nothing to reach the folder through. Passing vacuously.
            return;
        }
        Assert.True(Directory.Exists(Path.Combine(TreeHolding.Root(ctx), alias))); // pre-condition: the alias resolves
        File.WriteAllText(Path.Combine(ctx.GameRoot, "archive", "pc", "mod", alias + ".archive"), "ALIAS");
        var row = ModListing.Resolve(g).Single(m => m.Name == alias);

        Assert.Empty(ModUninstall.Preview(ctx, row).HeldFolders);
        ModUninstall.Run(ctx, row);

        Assert.DoesNotContain(ModListing.Resolve(g), m => m.Name == alias);
        Assert.Equal("OTHER", File.ReadAllText(victim));
    }

    // The 8.3 alias of the last segment, from `dir /x`, or null when the volume makes none.
    private static string? ShortNameOf(string dir)
    {
        var parent = Path.GetDirectoryName(dir)!;
        var name = Path.GetFileName(dir);
        var psi = new System.Diagnostics.ProcessStartInfo("cmd", $"/c dir /x /ad \"{parent}\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var lines = p.StandardOutput.ReadToEnd().Split('\n');
        p.WaitForExit();
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (!line.EndsWith(" " + name, StringComparison.Ordinal)) continue;
            var before = line[..^(name.Length + 1)].TrimEnd();
            var token = before.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            return token is not null && token.Contains('~') ? token : null;
        }
        return null;
    }

    [Fact]
    public void A_link_at_the_holding_root_is_refused_and_nothing_is_deleted()
    {
        var (g, ctx) = TreeGame();
        var target = Path.Combine(_root, "elsewhere-root");
        var targetFile = Path.Combine(target, "Plain", "r6", "scripts", "Plain", "p.reds");
        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
        File.WriteAllText(targetFile, "NOT 626'S");
        Directory.CreateDirectory(ctx.DataDir);
        MakeJunction(TreeHolding.Root(ctx), target);
        var pak = Path.Combine(ctx.GameRoot, "archive", "pc", "mod", "Plain.archive");
        var row = ModListing.Resolve(g).Single(m => m.Name == "Plain");

        var e = Assert.ThrowsAny<InvalidOperationException>(() => ModUninstall.Run(ctx, row));

        Assert.Contains("link", e.Message);
        Assert.Equal("NOT 626'S", File.ReadAllText(targetFile));
        Assert.Equal("PLAIN", File.ReadAllText(pak));
    }

    [Fact]
    public async Task A_junction_inside_the_held_folder_is_removed_as_a_link_and_its_target_survives()
    {
        var (g, ctx) = TreeGame();
        await Scanner.DisableModAsync("CoolMod", ctx);
        var heldDir = TreeHolding.ModDir(ctx, "CoolMod");
        var target = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(Path.Combine(target, "deep"));
        File.WriteAllText(Path.Combine(target, "t.txt"), "NOT 626'S");
        File.WriteAllText(Path.Combine(target, "deep", "d.txt"), "NOT 626'S EITHER");
        MakeJunction(Path.Combine(heldDir, "r6", "scripts", "link"), target);
        var row = ModListing.Resolve(g).Single(m => m.Name == "CoolMod");

        ModUninstall.Run(ctx, row);

        Assert.False(Directory.Exists(heldDir));
        Assert.Equal("NOT 626'S", File.ReadAllText(Path.Combine(target, "t.txt")));
        Assert.Equal("NOT 626'S EITHER", File.ReadAllText(Path.Combine(target, "deep", "d.txt")));
    }

    [Fact]
    public async Task A_read_only_junction_is_removed_and_its_targets_attributes_are_untouched()
    {
        var (g, ctx) = TreeGame();
        await Scanner.DisableModAsync("CoolMod", ctx);
        var heldDir = TreeHolding.ModDir(ctx, "CoolMod");
        var target = Path.Combine(_root, "elsewhere-ro");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "t.txt"), "NOT 626'S");
        var targetAttrs = File.GetAttributes(target);
        var link = Path.Combine(heldDir, "r6", "scripts", "link");
        MakeJunction(link, target);
        new DirectoryInfo(link).Attributes |= FileAttributes.ReadOnly;
        Assert.Equal(targetAttrs, File.GetAttributes(target)); // pre-condition: the link's flag is its own
        var row = ModListing.Resolve(g).Single(m => m.Name == "CoolMod");

        ModUninstall.Run(ctx, row);

        Assert.False(Directory.Exists(heldDir));
        Assert.Equal("NOT 626'S", File.ReadAllText(Path.Combine(target, "t.txt")));
        Assert.Equal(targetAttrs, File.GetAttributes(target));
    }

    [Fact]
    public async Task A_held_folder_that_is_itself_a_junction_is_removed_as_a_link()
    {
        var (g, ctx) = TreeGame();
        var row = ModListing.Resolve(g).Single(m => m.Name == "Plain");
        var target = Path.Combine(_root, "elsewhere2");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "t.txt"), "NOT 626'S");
        Directory.CreateDirectory(TreeHolding.Root(ctx));
        MakeJunction(TreeHolding.ModDir(ctx, "Plain"), target);
        await Task.CompletedTask;

        ModUninstall.Run(ctx, row);

        Assert.False(Directory.Exists(TreeHolding.ModDir(ctx, "Plain")));
        Assert.Equal("NOT 626'S", File.ReadAllText(Path.Combine(target, "t.txt")));
    }

    // Preview must not read through a link either: a held folder whose only content is a junction is listed
    // by path, with no trees, rather than with trees found in the link's target.
    [Fact]
    public void Preview_does_not_read_through_a_junction_in_the_held_folder()
    {
        var (g, ctx) = TreeGame();
        var target = Path.Combine(_root, "elsewhere-preview");
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(target, "scripts", "Plain")).FullName, "p.reds"), "X");
        var heldDir = TreeHolding.ModDir(ctx, "Plain");
        Directory.CreateDirectory(heldDir);
        MakeJunction(Path.Combine(heldDir, "r6"), target);
        var row = ModListing.Resolve(g).Single(m => m.Name == "Plain");

        var held = Assert.Single(ModUninstall.Preview(ctx, row).HeldFolders);

        Assert.Equal(heldDir, held.Path);
        Assert.Empty(held.Trees);
        Assert.False(held.Unreadable);
    }

    [Fact]
    public async Task A_held_folder_still_there_after_the_delete_throws_naming_it()
    {
        var (g, ctx) = TreeGame();
        await Scanner.DisableModAsync("CoolMod", ctx);
        var heldDir = TreeHolding.ModDir(ctx, "CoolMod");
        var row = ModListing.Resolve(g).Single(m => m.Name == "CoolMod");
        ModUninstall.DeleteHeldForTests = _ => { };   // a delete that does nothing
        HeldFolderLeftException e;
        try { e = Assert.Throws<HeldFolderLeftException>(() => ModUninstall.Run(ctx, row)); }
        finally { ModUninstall.DeleteHeldForTests = null; }

        Assert.Contains(heldDir, e.Message);
        Assert.Equal(new[] { heldDir }, e.Left);
    }

    // A held file another process has open can't be deleted. The mod itself is gone, so the error says so,
    // names the folder, and keeps the cause; the rest of the run is not abandoned.
    [Fact]
    public async Task A_locked_held_file_leaves_the_mod_gone_and_says_which_folder_is_left()
    {
        var (g, ctx) = TreeGame();
        await Scanner.DisableModAsync("CoolMod", ctx);
        var heldDir = TreeHolding.ModDir(ctx, "CoolMod");
        var lockedFile = Path.Combine(heldDir, "r6", "scripts", "CoolMod", "main.reds");
        var bystander = HoldForSomeoneElse(ctx);
        var row = ModListing.Resolve(g).Single(m => m.Name == "CoolMod");

        HeldFolderLeftException e;
        using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            e = Assert.Throws<HeldFolderLeftException>(() => ModUninstall.Run(ctx, row));

        Assert.Equal($"CoolMod was uninstalled, but 626 couldn't delete everything it was holding for it in {heldDir}. "
                     + "Close anything using those files and delete the folder, or try again.", e.Message);
        Assert.NotNull(e.InnerException);
        Assert.DoesNotContain(ModListing.Resolve(g), m => m.Name == "CoolMod");
        Assert.Equal("BYSTANDER", File.ReadAllText(bystander));
        Assert.True(File.Exists(lockedFile));
    }

    [Fact]
    public void The_left_behind_message_has_a_plural_form()
        => Assert.Equal(@"A and B were uninstalled, but 626 couldn't delete everything it was holding for them in D:\h\A and D:\h\B. "
                        + "Close anything using those files and delete the folders, or try again.",
            HeldFolderLeftException.MessageFor(new[] { ("A", @"D:\h\A"), ("B", @"D:\h\B") }));

    // The same idiom as SafeMoveFallbackTests: a real junction, made by mklink.
    private static void MakeJunction(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var psi = new System.Diagnostics.ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0 && Directory.Exists(link),
            $"Could not create a junction for this test (mklink exit {p.ExitCode}): {err}");
    }
}
