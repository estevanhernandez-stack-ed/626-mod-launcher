using System.Text.Json;
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests;

/// <summary>
/// Provenance outlived the file (from the #397 audit; proven live on the 2026-11-18 smoke of #418). A replace record
/// meant "626 once replaced this path", and was read as "the file there now is 626's". After Steam's verify or a patch
/// put the game's real regulation.bin back, the record still claimed it: the game's own file listed as a mod, could be
/// turned off, and a return to vanilla moved it, leaving a game that won't start.
///
/// <para>Provenance is now about a COPY: 626's when it is the file 626 placed (a hash recorded at install), the game's
/// when it is a stashed original, and the game's when 626 knows what it placed and this is something else. Every case
/// that expects the game's file to stay asserts its bytes, and the held-row cases prove a 626 mod is still a mod.</para>
/// </summary>
public class ProvenanceByContentTests : IDisposable
{
    private const string Reg = "regulation.bin";
    private const string RowName = "Modded regulation.bin";
    private static readonly byte[] Vanilla = { 1, 1, 2, 3, 5, 8 };
    private static readonly byte[] Vanilla11 = { 1, 1, 0, 1, 1 };
    private static readonly byte[] Modded = { 9, 9, 9, 9 };

    private readonly string _root = TestSupport.TempDir("mmb-prov-");
    private string GameRoot => Path.Combine(_root, "ELDEN RING");
    private string Play => Path.Combine(GameRoot, "Game");
    private string DataDir => Path.Combine(_root, "data");
    private string RegPath => Path.Combine(Play, Reg);

    public ProvenanceByContentTests()
    {
        Directory.CreateDirectory(Play);
        File.WriteAllBytes(RegPath, Vanilla);
        File.WriteAllText(Path.Combine(Play, "eldenring.exe"), "game");
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private GameEntry Game() => new() { Id = "er-prov", GameName = "ER", Engine = "fromsoft", GameRoot = GameRoot, DataDir = DataDir };
    private Mod Row() => ModListing.Resolve(Game()).Single(m => m.Name == RowName);

    private void InstallModded()
    {
        var staging = Path.Combine(_root, "drop");
        Directory.CreateDirectory(staging);
        File.WriteAllBytes(Path.Combine(staging, Reg), Modded);
        var plan = DirectInject.Plan(Play, new[] { Path.Combine(staging, Reg) });
        DirectInject.Execute(Play, DirectInject.ReplacedRoot(Play), plan, new HashSet<string>(new[] { Reg }));
        Assert.Equal(Modded, File.ReadAllBytes(RegPath));
    }

    private string Batch() => Directory.GetDirectories(DirectInject.ReplacedRoot(Play)).Single();

    private void AssertGamesFileUntouched(byte[] expected)
    {
        Assert.True(File.Exists(RegPath), "regulation.bin left the game folder");
        Assert.Equal(expected, File.ReadAllBytes(RegPath));
    }

    [Fact]
    public void The_placed_file_is_still_626s_mod()
    {
        InstallModded();
        Assert.False(Row().IsBase);
        Assert.True(Row().Enabled);
    }

    [Fact]
    public void After_Steam_verify_restores_the_original_it_is_the_games_and_nothing_moves_it()
    {
        InstallModded();
        File.WriteAllBytes(RegPath, Vanilla);                     // Steam's verify: the real file is back

        Assert.True(Row().IsBase);
        ModToggle.SetDirectInjectEnabled(Game(), RowName, enabled: false);
        AssertGamesFileUntouched(Vanilla);
        var c = Scanner.GameContext(Game());
        Assert.DoesNotContain(RestorePointEngine.PlanVanillaTurnOffs(c, RestorePointEngine.PlanVanillaMoves(c)), t => t.Name == RowName);
    }

    [Fact]
    public void After_a_patch_ships_a_new_version_it_is_the_games_even_though_no_stash_matches_it()
    {
        InstallModded();
        File.WriteAllBytes(RegPath, Vanilla11);                   // the patch: neither the mod nor the stashed 1.0

        Assert.True(Row().IsBase);
        ModToggle.SetDirectInjectEnabled(Game(), RowName, enabled: false);
        AssertGamesFileUntouched(Vanilla11);
    }

    [Fact]
    public void A_batch_emptied_by_hand_no_longer_makes_the_games_file_a_mod()
    {
        // The #418 smoke's cleanup: the stashed original gone, the manifest left naming the path, the real file live.
        InstallModded();
        File.Delete(Path.Combine(Batch(), Reg));
        File.WriteAllBytes(RegPath, Vanilla);

        Assert.True(Row().IsBase);
    }

    [Fact]
    public void A_manifest_from_before_placed_hashes_still_proves_the_original_by_comparison()
    {
        // An older build wrote no placedSha256. The live file matching the stashed original is still the game's.
        InstallModded();
        StripPlacedHashes();
        File.WriteAllBytes(RegPath, Vanilla);

        Assert.True(Row().IsBase);
    }

    [Fact]
    public void A_manifest_from_before_placed_hashes_keeps_an_unknown_file_as_626s()
    {
        // No hash to compare and no original match: the path record is all there is, as before.
        InstallModded();
        StripPlacedHashes();

        Assert.False(Row().IsBase);
    }

    [Fact]
    public void A_held_626_mod_is_a_mod_while_the_games_original_is_live_at_the_same_path()
    {
        InstallModded();
        ModToggle.SetDirectInjectEnabled(Game(), RowName, enabled: false);   // swaps the original back in (#418)
        AssertGamesFileUntouched(Vanilla);

        var held = Assert.Single(ModListing.Resolve(Game()), m => m.Name == RowName);
        Assert.False(held.Enabled);
        Assert.False(held.IsBase);

        ModToggle.SetDirectInjectEnabled(Game(), RowName, enabled: true);
        Assert.Equal(Modded, File.ReadAllBytes(RegPath));
    }

    [Fact]
    public void The_manifest_records_the_placed_hash_in_camelCase()
    {
        InstallModded();
        var json = File.ReadAllText(Path.Combine(Batch(), "__626replaced.json"));
        Assert.Contains("\"placedSha256\"", json);
        Assert.DoesNotContain("\"PlacedSha256\"", json);
        var entry = Assert.Single(ReplacedStore.ReadBatch(Batch()));
        Assert.Equal(FileTally.Sha256(RegPath), entry.PlacedSha256, ignoreCase: true);
    }

    // ---------- the audit's findings ----------

    [Fact]
    public void A_data_folder_replace_with_an_install_record_still_gets_the_content_check()
    {
        // The Scanner lane: an install record claims the file AND a replace in <dataDir>/replaced records its hash.
        File.WriteAllBytes(RegPath, Modded);
        ModInstallRegistry.Save(DataDir, new ModInstallManifest("reg", "Reg.zip", "direct-inject", new[] { Reg }, DateTime.UtcNow,
            LocationPath: ModInstallRegistry.LocationPathFor(GameRoot, Play)));
        var batch = ReplacedStore.NewBatch(Path.Combine(DataDir, "replaced"));
        File.WriteAllBytes(Path.Combine(batch, Reg), Vanilla);
        ReplacedStore.WriteManifest(batch, new[] { new ReplacedStore.ReplacedEntry(RegPath, Reg, DateTime.UtcNow) { PlacedSha256 = FileTally.Sha256(RegPath) } });
        Assert.False(Row().IsBase);                               // the placed file: 626's

        File.WriteAllBytes(RegPath, Vanilla11);                   // the patch
        Assert.True(Row().IsBase);
    }

    [Fact]
    public void An_old_manifest_after_a_game_patch_reads_an_unmatched_file_as_the_games()
    {
        // No placed hash to compare, but the exe was written after the replace: the file there is the patch's.
        InstallModded();
        StripPlacedHashes();
        File.SetLastWriteTimeUtc(Path.Combine(Play, "eldenring.exe"), DateTime.UtcNow.AddMinutes(1));
        File.WriteAllBytes(RegPath, Vanilla11);

        Assert.True(Row().IsBase);
        ModToggle.SetDirectInjectEnabled(Game(), RowName, enabled: false);
        AssertGamesFileUntouched(Vanilla11);
    }

    [Fact]
    public void A_file_that_cannot_be_read_reads_as_the_games_when_a_hash_was_recorded()
    {
        InstallModded();
        using var locked = new FileStream(RegPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(Row().IsBase);                                // can't prove it is 626's: the safe side
    }

    [Fact]
    public void Return_to_vanilla_at_a_flat_game_root_turns_a_626_replace_off_instead_of_moving_it()
    {
        // The move plan has no swap-back; the turn-off does (#418). A 626 file with a stashed original goes that way.
        var root = Path.Combine(_root, "flatgame");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, Reg), Vanilla);
        File.WriteAllText(Path.Combine(root, "eldenring.exe"), "game");
        var game = new GameEntry { Id = "flat", GameName = "Flat", Engine = "fromsoft", GameRoot = root, DataDir = Path.Combine(_root, "flat-data") };
        var staging = Path.Combine(_root, "flat-drop");
        Directory.CreateDirectory(staging);
        File.WriteAllBytes(Path.Combine(staging, Reg), Modded);
        DirectInject.Execute(root, DirectInject.ReplacedRoot(root), DirectInject.Plan(root, new[] { Path.Combine(staging, Reg) }),
            new HashSet<string>(new[] { Reg }));
        var c = Scanner.GameContext(game);

        var moves = RestorePointEngine.PlanVanillaMoves(c);
        Assert.DoesNotContain(moves, mf => mf.Rel == Reg);
        Assert.Contains(RestorePointEngine.PlanVanillaTurnOffs(c, moves), t => t.Name == RowName);
    }

    private void StripPlacedHashes()
    {
        var mf = Path.Combine(Batch(), "__626replaced.json");
        var entries = ReplacedStore.ReadBatch(Batch()).Select(e => e with { PlacedSha256 = null }).ToList();
        File.WriteAllText(mf, JsonSerializer.Serialize(entries, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
