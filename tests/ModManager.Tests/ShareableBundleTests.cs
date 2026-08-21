using System.IO.Compression;
using ModManager.Core.Transport;

namespace ModManager.Tests;

/// <summary>
/// The world, without the person who lived in it.
///
/// <para>Same save folder as a portable bundle, different cut — and the cut is curated per game in the
/// signed manifest, because it is a fact about how a studio arranged a folder. Palworld's seam is
/// <c>Players/**</c> and <c>LocalData.sav</c>, verified against a real install and confirmed by the
/// game itself: a world you JOINED keeps only <c>LocalData.sav</c> locally.</para>
///
/// <para>The scope also flips what happens to identifying data. A portable bundle carries the Steam
/// account marker and discloses it; a shareable one cannot, because it is going to a stranger.</para>
/// </summary>
public class ShareableBundleTests
{
    private static readonly DateTime Stamp = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly BundleGame Game = new("palworld", "1623730", "Palworld");

    /// <summary>Palworld's real seam, as curated in the feed.</summary>
    private static readonly string[] Seam = { "**/Players/**", "**/LocalData.sav" };

    /// <summary>One world folder, shaped like the real thing.</summary>
    private static string World(string prefix, bool withAutocloud = false)
    {
        var dir = TestSupport.TempDir(prefix);
        Directory.CreateDirectory(Path.Combine(dir, "Players"));
        Directory.CreateDirectory(Path.Combine(dir, "backup", "world", "2026.08.16-22.52.36"));
        File.WriteAllText(Path.Combine(dir, "Level.sav"), "the-place");
        File.WriteAllText(Path.Combine(dir, "LevelMeta.sav"), "its-name");
        File.WriteAllText(Path.Combine(dir, "WorldOption.sav"), "its-rules");
        File.WriteAllText(Path.Combine(dir, "LocalData.sav"), "MY-CHARACTER");
        File.WriteAllText(Path.Combine(dir, "Players", "0001.sav"), "MY-CHARACTER");
        File.WriteAllText(Path.Combine(dir, "backup", "world", "2026.08.16-22.52.36", "Level.sav"), "old-place");
        if (withAutocloud)
            File.WriteAllText(Path.Combine(dir, "steam_autocloud.vdf"), "\"accountid\" \"00000000\"");
        return dir;
    }

    private static string Out(string prefix) => Path.Combine(TestSupport.TempDir(prefix), "w" + SaveBundle.Extension);

    [Fact]
    public void The_character_does_not_travel_and_the_bundle_says_which_files_did_not()
    {
        var world = World("share-cut-");
        var path = Out("share-cut-out-");

        var m = SaveBundle.Create(world, path, Game, Stamp, BundleScope.Shareable, playerPaths: Seam);

        using var zip = ZipFile.OpenRead(path);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("save/Level.sav", entries);
        Assert.Contains("save/LevelMeta.sav", entries);
        Assert.DoesNotContain(entries, e => e.Contains("LocalData.sav"));
        Assert.DoesNotContain(entries, e => e.Contains("Players/"));

        // An honest artifact names what it left out and why.
        Assert.Equal(2, m.Excluded.Count);
        Assert.All(m.Excluded, x => Assert.Equal("character", x.Reason));
    }

    [Fact]
    public void A_portable_bundle_of_the_same_world_still_carries_the_character()
    {
        // Same folder, same seam available, different scope. If these two produced the same bundle the
        // scope would be decoration.
        var world = World("share-vs-portable-");

        var shared = SaveBundle.Plan(world, Game, Stamp, BundleScope.Shareable, playerPaths: Seam);
        var mine = SaveBundle.Plan(world, Game, Stamp, BundleScope.Portable, playerPaths: Seam);

        Assert.DoesNotContain(shared.Files, f => f.Relative.Contains("LocalData.sav"));
        Assert.Contains(mine.Files, f => f.Relative.Contains("LocalData.sav"));
        Assert.Contains(mine.Files, f => f.Relative.StartsWith("Players/"));
        Assert.Empty(mine.Manifest.Excluded);
    }

    [Fact]
    public void Identifying_data_is_carried_and_disclosed_when_portable_and_dropped_when_shared()
    {
        // The scope decides. Este's own framing: the Steam account id is fine between his machines,
        // but it identifies him, so it is not a file to post publicly.
        var world = World("share-personal-", withAutocloud: true);

        var mine = SaveBundle.Plan(world, Game, Stamp, BundleScope.Portable, playerPaths: Seam);
        Assert.Contains(mine.Files, f => f.Relative == "steam_autocloud.vdf");
        Assert.Equal("steam_autocloud.vdf", Assert.Single(mine.Manifest.Notices).Path);

        var shared = SaveBundle.Plan(world, Game, Stamp, BundleScope.Shareable, playerPaths: Seam);
        Assert.DoesNotContain(shared.Files, f => f.Relative == "steam_autocloud.vdf");
        Assert.Empty(shared.Manifest.Notices);                     // nothing left to disclose
        Assert.Contains(shared.Manifest.Excluded, x => x.Reason == "personal");
    }

    [Fact]
    public void The_games_own_backup_folder_carries_the_world_but_not_the_character_copies_inside_it()
    {
        // FOUND ON A REAL SHARE, and it would have been a privacy bug. Palworld keeps its own dated
        // backups, each a full copy of the world INCLUDING the character. A seam anchored at the top
        // level ("Players/**") excluded two files and shipped thirty-six copies of the same character
        // in backup/local/ and backup/world/. A seam must match at any depth, because games keep
        // their own history.
        var world = World("share-backup-");
        Directory.CreateDirectory(Path.Combine(world, "backup", "world", "2026.08.16-22.52.36", "Players"));
        File.WriteAllText(Path.Combine(world, "backup", "world", "2026.08.16-22.52.36", "Level.sav"), "old-place");
        File.WriteAllText(Path.Combine(world, "backup", "world", "2026.08.16-22.52.36", "Players", "0001.sav"), "MY-CHARACTER");
        Directory.CreateDirectory(Path.Combine(world, "backup", "local", "2026.08.16-22.52.36"));
        File.WriteAllText(Path.Combine(world, "backup", "local", "2026.08.16-22.52.36", "LocalData.sav"), "MY-CHARACTER");

        var plan = SaveBundle.Plan(world, Game, Stamp, BundleScope.Shareable, playerPaths: Seam);
        var shipped = plan.Files.Select(f => f.Relative).ToList();

        // The world's history stays - that IS the place.
        Assert.Contains("backup/world/2026.08.16-22.52.36/Level.sav", shipped);

        // Not one copy of the character, at any depth.
        Assert.DoesNotContain(shipped, r => r.EndsWith("LocalData.sav"));
        Assert.DoesNotContain(shipped, r => r.Contains("/Players/") || r.StartsWith("Players/"));
    }

    [Fact]
    public void A_scope_with_no_seam_is_still_refused()
    {
        // The programming guard stands. The panel does not offer share-a-world for an uncurated game,
        // so reaching this means a caller asked something the UI cannot ask.
        var world = World("share-noseam-");

        Assert.Throws<NotSupportedException>(() =>
            SaveBundle.Plan(world, Game, Stamp, BundleScope.Shareable));
        Assert.Throws<NotSupportedException>(() =>
            SaveBundle.Plan(world, Game, Stamp, BundleScope.Shareable, playerPaths: Array.Empty<string>()));
    }

    [Fact]
    public void The_scope_is_recorded_so_the_far_end_knows_what_it_received()
    {
        var world = World("share-scope-");

        Assert.Equal("shareable",
            SaveBundle.Plan(world, Game, Stamp, BundleScope.Shareable, playerPaths: Seam).Manifest.Scope);
        Assert.Equal("portable",
            SaveBundle.Plan(world, Game, Stamp, BundleScope.Portable).Manifest.Scope);
    }

    [Fact]
    public void A_shared_world_restores_and_the_recipient_simply_has_no_character_in_it()
    {
        // Which is the correct outcome, and the same thing that happens when you enter any world you
        // have never played: the game makes you one.
        var world = World("share-rt-");
        var path = Out("share-rt-out-");
        SaveBundle.Create(world, path, Game, Stamp, BundleScope.Shareable, playerPaths: Seam);

        var target = TestSupport.TempDir("share-rt-dest-");
        SaveBundle.Restore(path, target, TestSupport.TempDir("share-rt-snaps-"), "palworld");

        Assert.Equal("the-place", File.ReadAllText(Path.Combine(target, "Level.sav")));
        Assert.False(File.Exists(Path.Combine(target, "LocalData.sav")));
        Assert.False(Directory.Exists(Path.Combine(target, "Players")));
    }

    [Fact]
    public void Windroses_seam_cuts_at_any_depth()
    {
        // The other curated shape: not a worlds layout, so the unit is the whole save folder and the
        // patterns have to reach through a nested tree.
        var dir = TestSupport.TempDir("share-windrose-");
        foreach (var rel in new[]
        {
            "0.10.0/Worlds/aaa/000123.sst",
            "0.10.0/Players/bbb/000001.sst",
            "0.10.0/Accounts/ccc/000076.sst",
            "steam-user/RocksDB/AccountDescription.json",
            "steam-user/RocksDB_v2/0.10.0/Worlds/aaa/1.sst",
            "steam-user/RocksDB_v2/0.10.0/Players/bbb/1.sst",
        })
        {
            var full = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, rel);
        }

        var seam = new[] { "**/Accounts/**", "**/Players/**", "**/AccountDescription.json" };
        var plan = SaveBundle.Plan(dir, new BundleGame("windrose", "3041230", "Windrose"),
                                   Stamp, BundleScope.Shareable, playerPaths: seam);

        Assert.All(plan.Files, f => Assert.Contains("Worlds/", f.Relative));
        Assert.Equal(4, plan.Manifest.Excluded.Count);
    }
}
