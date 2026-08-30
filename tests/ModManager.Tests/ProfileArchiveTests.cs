using System.IO.Compression;
using ModManager.Core.Transport;

namespace ModManager.Tests;

/// <summary>
/// One file holding a whole modded setup, so a fresh Windows install is cheap again. See
/// <c>docs/superpowers/specs/2026-08-20-profile-archive-design.md</c>.
///
/// <para>Step one only: <b>writing</b> the archive. Nothing here touches a game folder or a save, which
/// is what makes it shippable on its own as a backup before any restore path exists.</para>
/// </summary>
public class ProfileArchiveTests
{
    private static readonly DateTime Stamp = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    private const string Jwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJhY2NvdW50cy5leGFtcGxlLmNvbSJ9.c2ln";

    private static string Out(string prefix)
        => Path.Combine(TestSupport.TempDir(prefix), "profile" + ProfileArchive.Extension);

    /// <summary>A game with saves, mod files and launcher data — including snapshot history.</summary>
    private static ProfileGameSource Game(string id, string prefix, bool withToken = false)
    {
        var save = TestSupport.TempDir(prefix + "-save-");
        Directory.CreateDirectory(Path.Combine(save, "W1"));
        File.WriteAllText(Path.Combine(save, "W1", "Level.sav"), id + "-world");
        if (withToken) File.WriteAllText(Path.Combine(save, "user.gls"), "{\"t\":\"" + Jwt + "\"}");

        var modRoot = TestSupport.TempDir(prefix + "-mods-");
        File.WriteAllText(Path.Combine(modRoot, "cool.pak"), "mod-bytes");
        File.WriteAllText(Path.Combine(modRoot, "other.pak"), "more-mod-bytes");

        var data = TestSupport.TempDir(prefix + "-data-");
        File.WriteAllText(Path.Combine(data, "metadata.json"), "{}");
        Directory.CreateDirectory(Path.Combine(data, ProfileArchive.SnapshotSubfolder));
        File.WriteAllText(Path.Combine(data, ProfileArchive.SnapshotSubfolder, "20260819__Before mods.zip"),
                          "a big old snapshot");

        return new ProfileGameSource(
            new BundleGame(id, "1", id),
            save,
            new[]
            {
                new BundlePlanFile(Path.Combine(modRoot, "cool.pak"), "mods/cool.pak"),
                new BundlePlanFile(Path.Combine(modRoot, "other.pak"), "mods/other.pak"),
            },
            new[] { new BundleMod("Cool Mod", "1.0", 7, true) },
            data)
        { ModLocations = new[] { "mods" } };
    }

    private static List<string> Entries(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.Select(e => e.FullName).OrderBy(x => x).ToList();
    }

    [Fact]
    public void Every_game_lands_under_its_own_id_with_saves_mods_and_data()
    {
        var path = Out("prof-shape-");
        ProfileArchive.Create(new[] { Game("palworld", "pal"), Game("windrose", "wind") },
                              path, Stamp, "0.19.0");

        var entries = Entries(path);
        Assert.Contains(ProfileArchive.ManifestEntry, entries);
        Assert.Contains("games/palworld/bundle.json", entries);
        Assert.Contains("games/palworld/save/W1/Level.sav", entries);
        Assert.Contains("games/palworld/mods/mods/cool.pak", entries);
        Assert.Contains("games/palworld/data/metadata.json", entries);
        Assert.Contains("games/windrose/save/W1/Level.sav", entries);

        // Nothing escapes its game's folder except the archive's own manifest.
        Assert.All(entries, e => Assert.True(
            e == ProfileArchive.ManifestEntry || e.StartsWith("games/"), $"stray entry: {e}"));
    }

    [Fact]
    public void Each_games_saves_are_a_real_bundle_readable_on_its_own_terms()
    {
        // The composition claim, asserted rather than assumed: the archive is a bundle per game, so
        // SaveBundle can read one out of it with no special knowledge of the archive.
        var path = Out("prof-bundle-");
        ProfileArchive.Create(new[] { Game("palworld", "pal") }, path, Stamp, "0.19.0");

        var m = SaveBundle.ReadManifest(path, "games/palworld/");

        Assert.NotNull(m);
        Assert.Equal("palworld", m!.Game.Id);
        Assert.Equal("Cool Mod", Assert.Single(m.Mods).Name);
    }

    [Fact]
    public void Snapshot_history_is_left_out_unless_asked_for()
    {
        // On a real machine this was 446 MB of a 482 MB data total - backups of backups. An archive
        // that hauls it silently is carrying a spare tyre for a spare tyre.
        var path = Out("prof-nosnap-");
        var m = ProfileArchive.Create(new[] { Game("palworld", "pal") }, path, Stamp, "0.19.0");

        Assert.False(m.SnapshotHistoryIncluded);
        Assert.DoesNotContain(Entries(path), e => e.Contains("/data/saves/"));
        Assert.Contains(Entries(path), e => e == "games/palworld/data/metadata.json");
    }

    [Fact]
    public void Snapshot_history_travels_when_it_is_asked_for()
    {
        var path = Out("prof-snap-");
        var m = ProfileArchive.Create(new[] { Game("palworld", "pal") }, path, Stamp, "0.19.0",
                                      includeSnapshotHistory: true);

        Assert.True(m.SnapshotHistoryIncluded);
        Assert.Contains(Entries(path), e => e.Contains("/data/saves/"));
    }

    [Fact]
    public void A_credential_anywhere_in_the_archive_is_left_out_and_recorded()
    {
        // Not just in saves. A mod folder or a launcher data folder can hold a token too, and an
        // archive is the most portable artifact this app produces.
        var path = Out("prof-cred-");
        var m = ProfileArchive.Create(new[] { Game("cyberpunk-2077", "cp", withToken: true) },
                                      path, Stamp, "0.19.0");

        var left = Assert.Single(m.Excluded);
        Assert.Equal("credential", left.Reason);
        Assert.Contains("user.gls", left.Path);
        Assert.DoesNotContain(Entries(path), e => e.Contains("user.gls"));
    }

    [Fact]
    public void The_manifest_counts_what_it_actually_wrote()
    {
        var path = Out("prof-counts-");
        var m = ProfileArchive.Create(new[] { Game("palworld", "pal") }, path, Stamp, "0.19.0");

        var g = Assert.Single(m.Games);
        Assert.True(g.SaveIncluded);
        Assert.Equal(1, g.SaveFileCount);
        Assert.Equal(2, g.ModFileCount);
        Assert.Equal(1, g.DataFileCount);              // metadata.json; the snapshot was skipped
        Assert.True(g.ModBytes > 0);
        Assert.Equal(m.TotalFiles, g.SaveFileCount + g.ModFileCount + g.DataFileCount);
    }

    [Fact]
    public void A_game_with_no_saves_still_carries_its_mods()
    {
        // The common case on a fresh machine: the game is installed and modded but never played.
        var src = Game("palworld", "pal") with { SaveDir = null };
        var path = Out("prof-nosave-");

        var m = ProfileArchive.Create(new[] { src }, path, Stamp, "0.19.0");

        var g = Assert.Single(m.Games);
        Assert.False(g.SaveIncluded);
        Assert.Equal(0, g.SaveFileCount);
        Assert.Equal(2, g.ModFileCount);
        Assert.DoesNotContain(Entries(path), e => e.EndsWith("bundle.json"));
    }

    [Fact]
    public void The_manifest_is_readable_without_extracting_and_is_camelCase_on_disk()
    {
        var path = Out("prof-read-");
        ProfileArchive.Create(new[] { Game("palworld", "pal") }, path, Stamp, "0.19.0");

        var m = ProfileArchive.ReadManifest(path)!;
        Assert.Equal(ProfileArchive.CurrentVersion, m.ArchiveVersion);
        Assert.Equal("0.19.0", m.LauncherVersion);
        Assert.Equal("palworld", Assert.Single(m.Games).Game.Id);

        using var zip = ZipFile.OpenRead(path);
        using var r = new StreamReader(zip.GetEntry(ProfileArchive.ManifestEntry)!.Open());
        var json = r.ReadToEnd();
        Assert.Contains("\"archiveVersion\"", json);
        Assert.Contains("\"snapshotHistoryIncluded\"", json);
        Assert.DoesNotContain("\"ArchiveVersion\"", json);
    }

    [Fact]
    public void The_source_folders_are_never_touched()
    {
        // The property that makes step one shippable on its own: it only reads.
        var src = Game("palworld", "pal");
        var before = Directory.GetFiles(src.SaveDir!, "*", SearchOption.AllDirectories)
                              .Concat(Directory.GetFiles(src.DataDir!, "*", SearchOption.AllDirectories))
                              .ToDictionary(f => f, File.ReadAllBytes);

        ProfileArchive.Create(new[] { src }, Out("prof-readonly-"), Stamp, "0.19.0");

        foreach (var (f, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(f));
    }

    [Fact]
    public void A_file_that_vanished_mid_archive_does_not_take_the_whole_thing_down()
    {
        // A scan and a write are not atomic, and a game updating in the background is normal.
        var src = Game("palworld", "pal");
        var ghost = src.ModFiles.Append(
            new BundlePlanFile(Path.Combine(TestSupport.TempDir("prof-ghost-"), "gone.pak"), "gone.pak")).ToList();

        var m = ProfileArchive.Create(new[] { src with { ModFiles = ghost } },
                                      Out("prof-ghost-out-"), Stamp, "0.19.0");

        Assert.Equal(2, Assert.Single(m.Games).ModFileCount);   // the two that exist
    }

    [Fact]
    public void Nothing_to_archive_is_an_empty_archive_not_a_throw()
    {
        var path = Out("prof-empty-");
        var m = ProfileArchive.Create(Array.Empty<ProfileGameSource>(), path, Stamp, "0.19.0");

        Assert.Empty(m.Games);
        Assert.Equal(0, m.TotalFiles);
        Assert.Equal(new[] { ProfileArchive.ManifestEntry }, Entries(path));
    }

    [Fact]
    public void A_file_that_is_not_an_archive_reads_as_null()
    {
        var path = Path.Combine(TestSupport.TempDir("prof-junk-"), "junk" + ProfileArchive.Extension);
        File.WriteAllText(path, "not a zip");
        Assert.Null(ProfileArchive.ReadManifest(path));
    }

    [Fact]
    public void Mods_are_filed_under_the_LOCATION_they_came_from()
    {
        // Windrose keeps mods in two places. Flattening them into one namespace loses which is which,
        // and two same-named mods in different locations collide outright - one silently wins.
        var a = TestSupport.TempDir("prof-loc-a-");
        var b = TestSupport.TempDir("prof-loc-b-");
        File.WriteAllText(Path.Combine(a, "x.pak"), "from-mods");
        File.WriteAllText(Path.Combine(b, "x.pak"), "from-mods2");

        var path = Out("prof-loc-");
        var m = ProfileArchive.Create(new[]
        {
            new ProfileGameSource(new BundleGame("windrose", "1", "Windrose"), null,
                new[]
                {
                    new BundlePlanFile(Path.Combine(a, "x.pak"), "mods/Same/x.pak"),
                    new BundlePlanFile(Path.Combine(b, "x.pak"), "mods2/Same/x.pak"),
                },
                new[] { new BundleMod("Same", "1", null, true) }, null)
            { ModLocations = new[] { "mods", "mods2" } },
        }, path, Stamp, "0.19.0");

        var entries = Entries(path);
        Assert.Contains("games/windrose/mods/mods/Same/x.pak", entries);
        Assert.Contains("games/windrose/mods/mods2/Same/x.pak", entries);
        Assert.Equal(2, Assert.Single(m.Games).ModFileCount);          // neither one lost to a collision
        Assert.Equal(new[] { "mods", "mods2" }, Assert.Single(m.Games).ModLocations);
    }

    [Fact]
    public void The_archive_declares_its_format_so_a_reader_knows_which_shape_it_holds()
    {
        var path = Out("prof-ver-");
        ProfileArchive.Create(new[] { Game("palworld", "pal") }, path, Stamp, "0.19.0");
        Assert.Equal(2, ProfileArchive.ReadManifest(path)!.ArchiveVersion);
    }
}
