using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests;

/// <summary>
/// Turning off a file 626 put in place OVER one of the game's own files must put the game's file back
/// (reversibility audit of #397, 2026-10-16, rated Important). Before this, DirectInject.Disable moved
/// the replacement (Elden Ring's regulation.bin) to holding and left nothing in its place: "off" and Safe
/// Clear's return to vanilla reported success on a game that would not start. The original was never lost,
/// it sat in <c>&lt;play&gt;/_626/replaced/&lt;batch&gt;/</c>, but nothing moved it back.
///
/// <para>Every case asserts bytes, both ways, so a swap that copies the wrong file or loses one cannot pass
/// on file existence alone.</para>
/// </summary>
public class DirectInjectReplacedSwapTests : IDisposable
{
    private const string Reg = "regulation.bin";
    private const string RowName = "Modded regulation.bin";
    private static readonly byte[] Vanilla = { 1, 1, 2, 3, 5, 8 };
    private static readonly byte[] V1 = { 7, 7, 7 };
    private static readonly byte[] V2 = { 9, 9, 9, 9 };

    private readonly string _root = TestSupport.TempDir("mmb-regswap-");
    private string GameRoot => Path.Combine(_root, "ELDEN RING");
    private string Play => Path.Combine(GameRoot, "Game");
    private string DataDir => Path.Combine(_root, "data");
    private string RegPath => Path.Combine(Play, Reg);

    public DirectInjectReplacedSwapTests()
    {
        Directory.CreateDirectory(Play);
        File.WriteAllBytes(RegPath, Vanilla);
        File.WriteAllText(Path.Combine(Play, "eldenring.exe"), "game");
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private GameEntry Game() => new() { Id = "er-swap", GameName = "ER", Engine = "fromsoft", GameRoot = GameRoot, DataDir = DataDir };

    private IReadOnlyList<Mod> Rows() => ModListing.Resolve(Game());

    // The real install route: DirectInject.Plan + Execute snapshot the file being replaced into a new batch.
    private void Install(byte[] bytes)
    {
        var staging = Path.Combine(_root, "drop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var src = Path.Combine(staging, Reg);
        File.WriteAllBytes(src, bytes);
        var plan = DirectInject.Plan(Play, new[] { src });
        var result = DirectInject.Execute(Play, DirectInject.ReplacedRoot(Play), plan, new HashSet<string>(new[] { Reg }));
        Assert.Contains(Reg, result.Updated.Concat(result.Added));
        Assert.Equal(bytes, File.ReadAllBytes(RegPath));
    }

    private static byte[]? Held(string root, string name)
    {
        var hit = Directory.Exists(root) ? Directory.GetFiles(root, name, SearchOption.AllDirectories).SingleOrDefault() : null;
        return hit is null ? null : File.ReadAllBytes(hit);
    }

    private void Off() => ModToggle.SetDirectInjectEnabled(Game(), RowName, enabled: false);
    private void On() => ModToggle.SetDirectInjectEnabled(Game(), RowName, enabled: true);

    [Fact]
    public void Turning_off_a_626_replaced_file_puts_the_games_original_back()
    {
        Install(V2);

        Off();

        Assert.Equal(Vanilla, File.ReadAllBytes(RegPath));                        // the game can start
        Assert.Equal(V2, Held(DirectInjectListing.Holding(Game()), Reg));        // the mod is held, not lost
        Assert.Null(Held(DirectInject.ReplacedRoot(Play), Reg));                 // the original left its batch
    }

    [Fact]
    public void Turning_it_back_on_returns_the_mod_and_the_original_to_its_batch()
    {
        Install(V2);
        Off();

        On();

        Assert.Equal(V2, File.ReadAllBytes(RegPath));
        Assert.Equal(Vanilla, Held(DirectInject.ReplacedRoot(Play), Reg));      // back where a Restore expects it
        Assert.Null(Held(DirectInjectListing.Holding(Game()), Reg));
    }

    [Fact]
    public void Off_and_on_twice_is_byte_identical_every_time()
    {
        Install(V2);
        for (var i = 0; i < 2; i++)
        {
            Off();
            Assert.Equal(Vanilla, File.ReadAllBytes(RegPath));
            On();
            Assert.Equal(V2, File.ReadAllBytes(RegPath));
        }
    }

    [Fact]
    public void Replaced_twice_turning_off_restores_the_games_file_not_the_previous_mod_version()
    {
        // vanilla -> v1 -> v2. The later batch holds v1, an older MOD; only the earliest holds the game's file.
        Install(V1);
        Install(V2);

        Off();
        Assert.Equal(Vanilla, File.ReadAllBytes(RegPath));

        On();
        Assert.Equal(V2, File.ReadAllBytes(RegPath));
        var stored = Directory.GetFiles(DirectInject.ReplacedRoot(Play), Reg, SearchOption.AllDirectories)
            .Select(File.ReadAllBytes).ToList();
        Assert.Equal(2, stored.Count);                                            // both batches whole again
        Assert.Contains(stored, b => b.SequenceEqual(Vanilla));
        Assert.Contains(stored, b => b.SequenceEqual(V1));
    }

    [Fact]
    public void While_off_the_listing_shows_one_disabled_row_for_the_mod_not_a_second_enabled_one()
    {
        Install(V2);
        Off();

        var rows = Rows().Where(m => m.Name == RowName).ToList();

        var row = Assert.Single(rows);
        Assert.False(row.Enabled);
        Assert.False(row.IsBase);                                                  // the held mod is still a mod
    }

    [Fact]
    public void While_off_return_to_vanilla_turns_nothing_off_for_it()
    {
        // A Game\ subfolder layout: vanilla reaches direct-inject files through the turn-off plan.
        Install(V2);
        Off();

        var c = Scanner.GameContext(Game());
        Assert.DoesNotContain(RestorePointEngine.PlanVanillaTurnOffs(c, RestorePointEngine.PlanVanillaMoves(c)), t => t.Name == RowName);
    }

    [Fact]
    public void At_a_game_root_that_holds_it_directly_return_to_vanilla_never_moves_the_restored_original()
    {
        // No Game\ subfolder: the play folder IS the game root, and vanilla reaches the file through the move plan.
        var root = Path.Combine(_root, "flatgame");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, Reg), Vanilla);
        File.WriteAllText(Path.Combine(root, "eldenring.exe"), "game");
        var game = new GameEntry { Id = "flat", GameName = "Flat", Engine = "fromsoft", GameRoot = root, DataDir = Path.Combine(_root, "flat-data") };
        var staging = Path.Combine(_root, "flat-drop");
        Directory.CreateDirectory(staging);
        File.WriteAllBytes(Path.Combine(staging, Reg), V2);
        DirectInject.Execute(root, DirectInject.ReplacedRoot(root), DirectInject.Plan(root, new[] { Path.Combine(staging, Reg) }),
            new HashSet<string>(new[] { Reg }));
        var c = Scanner.GameContext(game);
        // The mod goes through the turn-off (which swaps the original back), not the bare move plan.
        Assert.Contains(RestorePointEngine.PlanVanillaTurnOffs(c, RestorePointEngine.PlanVanillaMoves(c)), t => t.Name == RowName);

        ModToggle.SetDirectInjectEnabled(game, RowName, enabled: false);

        Assert.Equal(Vanilla, File.ReadAllBytes(Path.Combine(root, Reg)));
        Assert.DoesNotContain(RestorePointEngine.PlanVanillaMoves(c), mf => mf.Rel == Reg);   // the original: left
    }

    [Fact]
    public void The_hold_record_names_the_batch_in_camelCase()
    {
        Install(V2);
        Off();

        var meta = Directory.GetFiles(DirectInjectListing.Holding(Game()), "__626mod.json", SearchOption.AllDirectories).Single();
        var json = File.ReadAllText(meta);
        Assert.Contains("\"restored\"", json);
        Assert.Contains("\"entry\"", json);
        Assert.Contains("\"batch\"", json);
        Assert.DoesNotContain("\"Restored\"", json);
    }

    [Fact]
    public void A_replace_whose_stashed_original_is_gone_still_turns_off_with_nothing_swapped()
    {
        // The record says 626 replaced the file, but the original has left its batch (a Restore took it, or the user
        // removed it). There is nothing to swap back, so turn-off behaves as it always did and records no swap.
        Install(V2);
        File.Delete(Directory.GetFiles(DirectInject.ReplacedRoot(Play), Reg, SearchOption.AllDirectories).Single());

        Off();
        Assert.False(File.Exists(RegPath));
        Assert.Equal(V2, Held(DirectInjectListing.Holding(Game()), Reg));
        var meta = File.ReadAllText(Directory.GetFiles(DirectInjectListing.Holding(Game()), "__626mod.json", SearchOption.AllDirectories).Single());
        Assert.DoesNotContain("\"batch\"", meta);

        On();
        Assert.Equal(V2, File.ReadAllBytes(RegPath));
    }

    // ---------- the audit's findings: the original is known by content, not by path ----------

    [Fact]
    public void A_mod_dropped_over_the_restored_original_is_a_mod_not_the_original()
    {
        // X off (vanilla back from batch A), then Y dropped over it with replace (batch C now holds vanilla).
        // Y must list, must go on Return to vanilla, and turning X on must refuse rather than file Y away as vanilla.
        Install(V2);
        Off();
        Install(V1);

        Assert.Contains(Rows(), m => m.Name == RowName && m.Enabled);
        var c = Scanner.GameContext(Game());
        Assert.Contains(RestorePointEngine.PlanVanillaTurnOffs(c, RestorePointEngine.PlanVanillaMoves(c)), t => t.Name == RowName);

        Assert.ThrowsAny<InvalidOperationException>(On);
        Assert.Equal(V1, File.ReadAllBytes(RegPath));                             // Y untouched
        Assert.Equal(V2, Held(DirectInjectListing.Holding(Game()), Reg));        // X still held
    }

    [Fact]
    public void A_stale_hold_record_does_not_hide_a_live_mod_or_exempt_it_from_vanilla()
    {
        // The record outlived the turn-on (its delete failed): nothing it names is held any more.
        Install(V2);
        Off();
        var metaPath = Directory.GetFiles(DirectInjectListing.Holding(Game()), "__626mod.json", SearchOption.AllDirectories).Single();
        var record = File.ReadAllText(metaPath);
        On();
        Directory.CreateDirectory(Path.GetDirectoryName(metaPath)!);
        File.WriteAllText(metaPath, record);

        Assert.Contains(Rows(), m => m.Name == RowName && m.Enabled);
        var c = Scanner.GameContext(Game());
        Assert.Contains(RestorePointEngine.PlanVanillaTurnOffs(c, RestorePointEngine.PlanVanillaMoves(c)), t => t.Name == RowName);
    }

    [Fact]
    public void A_swap_that_fails_rolls_the_turn_off_back_completely()
    {
        Install(V2);
        var original = Directory.GetFiles(DirectInject.ReplacedRoot(Play), Reg, SearchOption.AllDirectories).Single();

        using (new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read))   // readable, so it hashes; not movable
            Assert.ThrowsAny<InvalidOperationException>(Off);

        Assert.Equal(V2, File.ReadAllBytes(RegPath));                             // the mod is back in place
        Assert.Equal(Vanilla, File.ReadAllBytes(original));                       // the original never left its batch
        Assert.Null(Held(DirectInjectListing.Holding(Game()), Reg));
        Assert.Empty(Directory.Exists(DirectInjectListing.Holding(Game()))
            ? Directory.GetFiles(DirectInjectListing.Holding(Game()), "__626mod.json", SearchOption.AllDirectories) : Array.Empty<string>());
    }

    [Fact]
    public void An_original_stashed_before_the_last_game_patch_loses_to_one_stashed_after()
    {
        // Mod on 1.0 (batch A holds 1.0). The patch rewrites the exe and puts 1.1's file over the mod. The mod is
        // dropped again (batch B holds 1.1). Off must restore 1.1: 1.0's file against a 1.1 exe won't start.
        byte[] vanilla11 = { 1, 1, 0, 1, 1 };
        Install(V1);
        Thread.Sleep(30);
        File.SetLastWriteTimeUtc(Path.Combine(Play, "eldenring.exe"), DateTime.UtcNow);   // the patch
        File.WriteAllBytes(RegPath, vanilla11);
        Thread.Sleep(30);
        Install(V2);

        Off();

        Assert.Equal(vanilla11, File.ReadAllBytes(RegPath));
        On();
        Assert.Equal(V2, File.ReadAllBytes(RegPath));
    }

    [Fact]
    public void Setting_aside_a_mod_reinstalled_over_itself_really_takes_it_out()
    {
        // Live on Neb, 2026-11-18: each TechieW DLL had been reinstalled over itself in May, so the replaced store held an
        // identical copy. #418's turn-off swapped that "original" back in, the mod stayed live, and the list hid it. A
        // stash under a MOD's own name holds an older copy of the mod, never the game's file: only a game file swaps.
        var gameRoot = Path.Combine(_root, "ER");
        var play = Path.Combine(gameRoot, "Game");
        Directory.CreateDirectory(Path.Combine(play, "mods"));
        File.WriteAllText(Path.Combine(play, "eldenring.exe"), "game");
        File.WriteAllText(Path.Combine(play, "dinput8.dll"), "loader");
        var dllRel = Path.Combine("mods", "AdjustTheFov.dll");
        File.WriteAllText(Path.Combine(play, dllRel), "fov");
        var staging = Path.Combine(_root, "redrop", "mods");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "AdjustTheFov.dll"), "fov");               // the same mod, dropped again
        var plan = DirectInject.Plan(play, new[] { Path.Combine(_root, "redrop") });
        DirectInject.Execute(play, DirectInject.ReplacedRoot(play), plan, plan.Collisions.Select(x => x.RelPath).ToHashSet());
        var game = new GameEntry { Id = "er-fov", GameName = "ER", Engine = "fromsoft", GameRoot = gameRoot, DataDir = Path.Combine(_root, "data") };

        ModToggle.SetDirectInjectEnabled(game, "Adjust The Fov", enabled: false);

        Assert.False(File.Exists(Path.Combine(play, dllRel)));                              // really out
        var row = Assert.Single(ModListing.Resolve(game), m => m.Name == "Adjust The Fov");
        Assert.False(row.Enabled);
        ModToggle.SetDirectInjectEnabled(game, "Adjust The Fov", enabled: true);
        Assert.Equal("fov", File.ReadAllText(Path.Combine(play, dllRel)));
    }

    [Fact]
    public void Turning_on_refuses_and_moves_nothing_when_the_batch_slot_was_refilled()
    {
        // Something put a file back into the batch while the mod was off. Moving the original over it would
        // destroy one of the two, so turn-on refuses and both stay where they are.
        Install(V2);
        Off();
        var batch = Directory.GetDirectories(DirectInject.ReplacedRoot(Play)).Single();
        File.WriteAllBytes(Path.Combine(batch, Reg), V1);

        Assert.ThrowsAny<InvalidOperationException>(On);

        Assert.Equal(Vanilla, File.ReadAllBytes(RegPath));
        Assert.Equal(V1, File.ReadAllBytes(Path.Combine(batch, Reg)));
        Assert.Equal(V2, Held(DirectInjectListing.Holding(Game()), Reg));
    }
}
