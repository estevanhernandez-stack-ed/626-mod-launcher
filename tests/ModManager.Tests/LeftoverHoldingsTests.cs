using System.Diagnostics;
using ModManager.Core;

namespace ModManager.Tests;

public class LeftoverHoldingsTests
{
    [Fact]
    public void A_folder_matching_a_registered_game_is_not_an_orphan()
        => Assert.Empty(LeftoverHoldings.Orphans(new[] { "windrose" }, new[] { "windrose" }));

    [Fact]
    public void A_folder_matching_no_registered_game_is_an_orphan()
        => Assert.Equal(new[] { "demonologist" },
                        LeftoverHoldings.Orphans(new[] { "windrose" }, new[] { "windrose", "demonologist" }));

    // Ids are slugs and the folder is named from one, but a case difference between the registry and
    // the disk must never make a live game look abandoned — that is the one mistake here that offers
    // to delete files still in use.
    [Fact]
    public void Case_does_not_make_a_registered_game_look_orphaned()
        => Assert.Empty(LeftoverHoldings.Orphans(new[] { "Windrose" }, new[] { "windrose" }));

    [Fact]
    public void Find_describes_an_orphan_and_leaves_a_registered_game_out()
    {
        var lib = TestSupport.TempDir("leftovers-");
        var gameRoot = Path.Combine(lib, "steamapps", "common", "Windrose");
        Directory.CreateDirectory(gameRoot);

        var holdings = Path.Combine(lib, "_626mods");
        Directory.CreateDirectory(Path.Combine(holdings, "windrose"));
        var orphan = Path.Combine(holdings, "demonologist");
        Directory.CreateDirectory(Path.Combine(orphan, "SomeMod"));
        File.WriteAllText(Path.Combine(orphan, "SomeMod", "a.pak"), "0123456789");
        File.WriteAllText(Path.Combine(orphan, "profiles.json"), "{}");

        var found = LeftoverHoldings.Find(new[]
        {
            new GameEntry { Id = "windrose", GameName = "Windrose", GameRoot = gameRoot },
        });

        var one = Assert.Single(found);
        Assert.Equal("demonologist", one.FolderName);
        Assert.Equal(2, one.FileCount);                       // counts the whole tree, not just the top
        Assert.Equal(12, one.Bytes);
        // It is NOT only mods — the folder holds profiles and metadata too, and the UI has to be able
        // to say so rather than call it all "mods".
        Assert.Contains("SomeMod", one.TopLevelNames);
        Assert.Contains("profiles.json", one.TopLevelNames);
    }

    // The accepted blind spot, pinned so it is a decision rather than a surprise: roots come from the
    // registered games. A library whose every game has been removed is a library nothing points at.
    // The alternative is walking drives for a folder name, which is how a tool offers to delete a
    // directory it merely recognised.
    [Fact]
    public void A_root_no_registered_game_points_at_is_never_scanned()
    {
        var lib = TestSupport.TempDir("leftovers-unseen-");
        Directory.CreateDirectory(Path.Combine(lib, "_626mods", "demonologist"));

        Assert.Empty(LeftoverHoldings.Find(Array.Empty<GameEntry>()));
    }

    // Finding 1: Scanner.DataDirForGame returns game.DataDir verbatim when it's set, and DataDir comes
    // out of hand-editable games.json with no shape validation. Before the gate, a DataDir that merely
    // points at an ordinary folder made Find enumerate that folder and offer every sibling inside it
    // for permanent deletion. A root only counts if its own folder name is "_626mods".
    [Fact]
    public void A_dataDir_pointing_outside_a_626mods_folder_is_never_scanned()
    {
        var lib = TestSupport.TempDir("leftovers-notmods-");
        var ordinary = Path.Combine(lib, "SomeOrdinaryFolder");
        Directory.CreateDirectory(Path.Combine(ordinary, "windrose"));
        Directory.CreateDirectory(Path.Combine(ordinary, "SiblingStuff"));

        var found = LeftoverHoldings.Find(new[]
        {
            new GameEntry { Id = "windrose", GameName = "Windrose",
                            DataDir = Path.Combine(ordinary, "windrose") },
        });

        Assert.Empty(found);
    }

    // Finding 2: Scanner.DataDirForGame substitutes the literal folder name "game" when Id is empty.
    // Filtering empty ids out of the known-id set (instead of mirroring that substitution) drops the
    // game entirely, so the folder it is actively using reads as an orphan and gets offered for deletion.
    [Fact]
    public void A_game_with_an_empty_id_does_not_orphan_its_own_holding_folder()
    {
        var lib = TestSupport.TempDir("leftovers-emptyid-");
        var gameRoot = Path.Combine(lib, "steamapps", "common", "SomeGame");
        Directory.CreateDirectory(gameRoot);

        var holdings = Path.Combine(lib, "_626mods");
        Directory.CreateDirectory(Path.Combine(holdings, "game"));

        var found = LeftoverHoldings.Find(new[]
        {
            new GameEntry { Id = "", GameName = "Some Game", GameRoot = gameRoot },
        });

        Assert.Empty(found);
    }

    // Finding 3: Directory.GetFiles(..., AllDirectories) throws if any subfolder underneath it cannot
    // be walked (ACL-restricted, or removed mid-scan by a mod being toggled — a real race). Before the
    // fix that exception escaped Find entirely, hiding every leftover rather than just the bad one.
    // ACL-deny (via icacls, on a folder this test owns) reproduces that without needing admin rights.
    [Fact]
    public void An_unreadable_leftover_does_not_hide_a_healthy_one()
    {
        var lib = TestSupport.TempDir("leftovers-unreadable-");
        var holdings = Path.Combine(lib, "_626mods");

        var locked = Path.Combine(holdings, "locked-orphan");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(locked, "a.pak"), "x");

        var healthy = Path.Combine(holdings, "healthy-orphan");
        Directory.CreateDirectory(healthy);
        File.WriteAllText(Path.Combine(healthy, "b.pak"), "y");

        DenyRead(locked);
        try
        {
            var found = LeftoverHoldings.Find(new[]
            {
                new GameEntry { Id = "registered-game", GameName = "Registered",
                                DataDir = Path.Combine(holdings, "registered-game") },
            });

            var names = found.Select(h => h.FolderName).ToList();
            Assert.Contains("healthy-orphan", names);
            Assert.DoesNotContain("locked-orphan", names);
        }
        finally
        {
            // Undo the deny so the temp-dir cleanup (and any later run against the same OS profile)
            // isn't left holding a folder its own owner can no longer read.
            ResetAcl(locked);
        }
    }

    // icacls, not System.Security.AccessControl — the latter needs an extra package reference on the
    // plain net10.0 TFM this test project targets; icacls ships with every Windows box the suite runs on.
    private static void DenyRead(string path)
    {
        RunIcacls($"\"{path}\" /inheritance:r");
        RunIcacls($"\"{path}\" /deny \"{Environment.UserName}:(OI)(CI)(RX)\"");
    }

    private static void ResetAcl(string path) => RunIcacls($"\"{path}\" /reset /t");

    private static void RunIcacls(string arguments)
    {
        using var p = Process.Start(new ProcessStartInfo("icacls", arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        p.WaitForExit();
    }
}
