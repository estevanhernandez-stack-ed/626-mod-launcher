using System.IO.Compression;
using System.Text;
using ModManager.Core;
using ModManager.Core.Transport;

namespace ModManager.Tests;

/// <summary>
/// Finding auth tokens among save files. See
/// <c>docs/superpowers/plans/2026-08-20-save-transport-and-the-data-it-needs.md</c>.
/// </summary>
public class CredentialScanTests
{
    // Shaped like the real thing: three base64url segments, header and payload both starting eyJ.
    private const string Jwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJhY2NvdW50cy5leGFtcGxlLmNvbSJ9.c2lnbmF0dXJl";

    [Fact]
    public void A_token_is_found_even_when_it_is_buried_in_other_text()
    {
        // Cyberpunk's user.gls is a VDF-ish wrapper with the token inside it, not a bare JWT.
        var content = "\"user.gls\"\n{\n\t\"refresh\"\t\t\"" + Jwt + "\"\n}";
        Assert.True(CredentialScan.LooksLikeCredential(Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void The_games_own_data_is_not_a_credential()
    {
        // The load-bearing negative. A keyword sweep for "password" across these same save folders hit
        // Palworld's Level.sav and Windrose's RocksDB - a world's server password and an account
        // record. Both are save state the user would be furious to lose. Excluding them would be a bug
        // wearing a security badge.
        Assert.False(CredentialScan.LooksLikeCredential(Encoding.UTF8.GetBytes("ServerPassword=hunter2")));
        Assert.False(CredentialScan.LooksLikeCredential(Encoding.UTF8.GetBytes("{\"password\":\"abc\"}")));
        Assert.False(CredentialScan.LooksLikeCredential(Encoding.UTF8.GetBytes("PlM1\x01\x02 eyJ not a token")));
        Assert.False(CredentialScan.LooksLikeCredential(Array.Empty<byte>()));
    }

    [Fact]
    public void One_base64_looking_segment_is_not_enough()
    {
        // "eyJ" alone occurs in ordinary base64. A token needs a payload after a dot too.
        Assert.False(CredentialScan.LooksLikeCredential(Encoding.UTF8.GetBytes("eyJhbGciOiJIUzI1NiJ9")));
        Assert.False(CredentialScan.LooksLikeCredential(Encoding.UTF8.GetBytes("eyJhbGciOiJIUzI1NiJ9.notpayload.sig")));
    }

    [Fact]
    public void A_large_file_is_not_scanned_at_all()
    {
        // Tokens are small; a 32 MB world file is not one, and bundling a big save must not turn into
        // a full content scan.
        var dir = TestSupport.TempDir("cred-big-");
        var big = Path.Combine(dir, "Level.sav");
        File.WriteAllBytes(big, Encoding.UTF8.GetBytes(Jwt).Concat(new byte[CredentialScan.MaxScanBytes]).ToArray());

        Assert.False(CredentialScan.IsCredentialFile(big));
    }

    [Fact]
    public void An_unreadable_or_missing_file_is_not_claimed_to_hold_a_secret()
        => Assert.False(CredentialScan.IsCredentialFile(Path.Combine(TestSupport.TempDir("cred-none-"), "nope")));
}

/// <summary>
/// Packing a save into something that can move to another machine, and back out again.
/// </summary>
public class SaveBundleTests
{
    private const string Jwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJhY2NvdW50cy5leGFtcGxlLmNvbSJ9.c2ln";

    private static readonly DateTime Stamp = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly BundleGame Game = new("palworld", "1623730", "Palworld");

    private static string SaveDir(string prefix, bool withToken = false)
    {
        var dir = TestSupport.TempDir(prefix);
        Directory.CreateDirectory(Path.Combine(dir, "W1", "Players"));
        File.WriteAllText(Path.Combine(dir, "W1", "Level.sav"), "the-world");
        File.WriteAllText(Path.Combine(dir, "W1", "Players", "p1.sav"), "my-character");
        File.WriteAllText(Path.Combine(dir, "GlobalPalStorage.sav"), "shared");
        if (withToken) File.WriteAllText(Path.Combine(dir, "user.gls"), "{\"tok\":\"" + Jwt + "\"}");
        return dir;
    }

    private static string BundlePath(string prefix)
        => Path.Combine(TestSupport.TempDir(prefix), "out" + SaveBundle.Extension);

    [Fact]
    public void A_bundle_carries_the_save_and_leaves_the_source_untouched()
    {
        var save = SaveDir("bundle-src-");
        var before = Directory.GetFiles(save, "*", SearchOption.AllDirectories)
                              .ToDictionary(f => f, File.ReadAllBytes);

        var manifest = SaveBundle.Create(save, BundlePath("bundle-out-"), Game, Stamp);

        Assert.Equal(3, manifest.FileCount);
        foreach (var (f, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(f));
    }

    [Fact]
    public void A_credential_never_travels_and_the_bundle_says_it_was_left_out()
    {
        // Cyberpunk's save root holds a CDPR refresh token valid to 2035. A bundle carrying it would
        // be handing out an account. An honest artifact also says what it is missing.
        var save = SaveDir("bundle-token-", withToken: true);
        var path = BundlePath("bundle-token-out-");

        var manifest = SaveBundle.Create(save, path, Game, Stamp);

        var left = Assert.Single(manifest.Excluded);
        Assert.Equal("user.gls", left.Path);
        Assert.Equal("credential", left.Reason);

        using var zip = ZipFile.OpenRead(path);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("user.gls"));
        Assert.Contains(zip.Entries, e => e.FullName == "save/W1/Level.sav");
    }

    [Fact]
    public void The_manifest_is_readable_without_extracting_anything()
    {
        // The import screen has to say what is in a bundle - and which mods are missing - before it
        // touches a single save file.
        var path = BundlePath("bundle-read-");
        SaveBundle.Create(SaveDir("bundle-read-src-"), path, Game, Stamp, mods: new[]
        {
            new BundleMod("Enhanced Palworld Visuals", "1.2", 123, true),
        });

        var manifest = SaveBundle.ReadManifest(path)!;

        Assert.Equal(SaveBundle.CurrentVersion, manifest.BundleVersion);
        Assert.Equal("palworld", manifest.Game.Id);
        Assert.Equal("portable", manifest.Scope);
        Assert.Equal("Enhanced Palworld Visuals", Assert.Single(manifest.Mods).Name);
    }

    [Fact]
    public void The_manifest_is_camelCase_on_disk()
    {
        var path = BundlePath("bundle-case-");
        SaveBundle.Create(SaveDir("bundle-case-src-"), path, Game, Stamp,
            mods: new[] { new BundleMod("M", "1", 7, true) });

        using var zip = ZipFile.OpenRead(path);
        using var r = new StreamReader(zip.GetEntry(SaveBundle.ManifestEntry)!.Open());
        var json = r.ReadToEnd();

        Assert.Contains("\"bundleVersion\"", json);
        Assert.Contains("\"createdUtc\"", json);
        Assert.Contains("\"nexusModId\"", json);
        Assert.DoesNotContain("\"BundleVersion\"", json);
        Assert.DoesNotContain("\"CreatedUtc\"", json);
    }

    [Fact]
    public void A_round_trip_restores_every_file_byte_for_byte()
    {
        var save = SaveDir("bundle-rt-");
        var path = BundlePath("bundle-rt-out-");
        var original = Directory.GetFiles(save, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(save, f), File.ReadAllBytes);
        SaveBundle.Create(save, path, Game, Stamp);

        var target = TestSupport.TempDir("bundle-rt-dest-");
        SaveBundle.Restore(path, target, TestSupport.TempDir("bundle-rt-snaps-"), "palworld");

        var landed = Directory.GetFiles(target, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(target, f), File.ReadAllBytes);
        Assert.Equal(original.Count, landed.Count);
        foreach (var (rel, bytes) in original) Assert.Equal(bytes, landed[rel]);
    }

    [Fact]
    public void Restoring_over_an_existing_save_snapshots_it_first()
    {
        // The whole point is moving saves between machines, so the destination often already has one.
        var path = BundlePath("bundle-snap-");
        SaveBundle.Create(SaveDir("bundle-snap-src-"), path, Game, Stamp);

        var target = SaveDir("bundle-snap-dest-");
        File.WriteAllText(Path.Combine(target, "W1", "Level.sav"), "about-to-be-replaced");
        var snaps = TestSupport.TempDir("bundle-snap-snaps-");

        SaveBundle.Restore(path, target, snaps, "palworld");

        var kept = Assert.Single(SaveManager.ListSnapshots(snaps));
        Assert.Contains("before-restore", kept.Label);
        Assert.Equal("the-world", File.ReadAllText(Path.Combine(target, "W1", "Level.sav")));
    }

    [Fact]
    public void A_bundle_for_another_game_is_refused_before_anything_is_deleted()
    {
        // Restoring Palworld over Elden Ring would look like a success and destroy a save.
        var path = BundlePath("bundle-wrong-");
        SaveBundle.Create(SaveDir("bundle-wrong-src-"), path, Game, Stamp);

        var target = SaveDir("bundle-wrong-dest-");
        var before = File.ReadAllText(Path.Combine(target, "W1", "Level.sav"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => SaveBundle.Restore(path, target, TestSupport.TempDir("bundle-wrong-snaps-"), "elden-ring"));

        Assert.Contains("different game", ex.Message);
        Assert.Equal(before, File.ReadAllText(Path.Combine(target, "W1", "Level.sav")));
    }

    [Fact]
    public void A_bundle_from_a_newer_launcher_is_refused_rather_than_half_understood()
    {
        var dir = TestSupport.TempDir("bundle-future-");
        var path = Path.Combine(dir, "future" + SaveBundle.Extension);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry(SaveBundle.ManifestEntry).Open()))
            w.Write("{\"bundleVersion\":99,\"game\":{\"id\":\"palworld\"}}");

        var ex = Assert.Throws<InvalidOperationException>(
            () => SaveBundle.Restore(path, TestSupport.TempDir("bundle-future-dest-"),
                                     TestSupport.TempDir("bundle-future-snaps-"), "palworld"));
        Assert.Contains("newer version", ex.Message);
    }

    [Fact]
    public void A_bundle_that_writes_outside_the_save_folder_is_refused()
    {
        // A bundle arrives from somewhere else, so it is untrusted input.
        var dir = TestSupport.TempDir("bundle-evil-");
        var path = Path.Combine(dir, "evil" + SaveBundle.Extension);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry(SaveBundle.ManifestEntry).Open()))
                w.Write("{\"bundleVersion\":1,\"game\":{\"id\":\"palworld\"}}");
            using var e = new StreamWriter(zip.CreateEntry(SaveBundle.PayloadPrefix + "../../escaped.txt").Open());
            e.Write("nope");
        }

        var ex = Assert.Throws<InvalidOperationException>(
            () => SaveBundle.Restore(path, TestSupport.TempDir("bundle-evil-dest-"),
                                     TestSupport.TempDir("bundle-evil-snaps-"), "palworld"));
        Assert.Contains("outside the save folder", ex.Message);
    }

    [Fact]
    public void A_shareable_bundle_is_refused_rather_than_approximated()
    {
        // It needs the world/character seam, which differs per game and is not guessable. Guessing it
        // ships a stranger somebody's character.
        var ex = Assert.Throws<NotSupportedException>(() => SaveBundle.Create(
            SaveDir("bundle-share-"), BundlePath("bundle-share-out-"), Game, Stamp, BundleScope.Shareable));

        Assert.Contains("character", ex.Message);
    }

    [Fact]
    public void The_missing_mods_are_the_sentence_the_import_screen_exists_to_say()
    {
        var manifest = new SaveBundleManifest
        {
            Mods = new[]
            {
                new BundleMod("Kept", "1", null, true),
                new BundleMod("Gone", "2", 99, true),
            },
        };

        var missing = SaveBundle.MissingMods(manifest, new[] { "kept" });   // case-insensitive

        Assert.Equal("Gone", Assert.Single(missing).Name);
        Assert.Empty(SaveBundle.MissingMods(manifest, new[] { "Kept", "Gone" }));
        Assert.Empty(SaveBundle.MissingMods(null, new[] { "Kept" }));
    }

    [Fact]
    public void A_file_that_is_not_a_bundle_reads_as_null_and_never_throws()
    {
        var dir = TestSupport.TempDir("bundle-junk-");
        var path = Path.Combine(dir, "junk" + SaveBundle.Extension);
        File.WriteAllText(path, "not a zip");

        Assert.Null(SaveBundle.ReadManifest(path));
        Assert.Throws<InvalidOperationException>(
            () => SaveBundle.Restore(path, TestSupport.TempDir("bundle-junk-dest-"),
                                     TestSupport.TempDir("bundle-junk-snaps-"), "palworld"));
    }
}

/// <summary>
/// Where the import screen sends someone for a mod they are missing.
/// </summary>
public class BundleModLinkTests
{
    [Fact]
    public void The_link_is_built_from_the_id_and_our_domain_not_from_the_bundle()
    {
        var mod = new BundleMod("Enhanced Palworld Visuals", "1.33", 1481, true);

        Assert.Equal("https://www.nexusmods.com/palworld/mods/1481",
            SaveBundle.NexusUrlFor(mod, "palworld"));
    }

    [Fact]
    public void No_id_or_no_domain_means_no_link_rather_than_an_invented_one()
    {
        Assert.Null(SaveBundle.NexusUrlFor(new BundleMod("M", null, null, true), "palworld"));
        Assert.Null(SaveBundle.NexusUrlFor(new BundleMod("M", null, 0, true), "palworld"));
        Assert.Null(SaveBundle.NexusUrlFor(new BundleMod("M", null, 12, true), null));
        Assert.Null(SaveBundle.NexusUrlFor(new BundleMod("M", null, 12, true), ""));
        Assert.Null(SaveBundle.NexusUrlFor(null, "palworld"));
    }

    [Fact]
    public void A_domain_that_is_not_a_slug_is_refused_rather_than_interpolated()
    {
        // Defensive. The domain comes from our own manifest, but it still lands in a URL the user
        // clicks, and a feed entry is data rather than code.
        Assert.Null(SaveBundle.NexusUrlFor(new BundleMod("M", null, 12, true), "pal/../../evil"));
        Assert.Null(SaveBundle.NexusUrlFor(new BundleMod("M", null, 12, true), "evil.com/x"));
        Assert.Null(SaveBundle.NexusUrlFor(new BundleMod("M", null, 12, true), "pal world"));
    }

    [Fact]
    public void Every_link_it_produces_points_at_nexus_and_passes_the_http_guard()
    {
        // The property that matters: a bundle from a stranger can name a mod, but it can never choose
        // where the user is sent.
        foreach (var domain in new[] { "palworld", "cyberpunk2077", "eldenring", "sons-of-the-forest" })
        {
            var url = SaveBundle.NexusUrlFor(new BundleMod("M", null, 7, true), domain);
            Assert.StartsWith("https://www.nexusmods.com/", url);
            Assert.True(SafeUrl.IsHttpUrl(url));
        }
    }
}

/// <summary>
/// One game's bundle standing alone as a file, and the same bundle sitting inside a bigger archive
/// beside others. See <c>docs/superpowers/specs/2026-08-20-profile-archive-design.md</c>.
///
/// <para>This is the load-bearing property behind the profile archive: it is a bundle per game plus a
/// registry, not a second format. If these two paths can disagree about credentials, exclusions or the
/// manifest, the archive is a rewrite rather than a composition.</para>
/// </summary>
public class ComposableBundleTests
{
    private static readonly DateTime Stamp = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    private static string Save(string prefix, string marker)
    {
        var dir = TestSupport.TempDir(prefix);
        Directory.CreateDirectory(Path.Combine(dir, "W1"));
        File.WriteAllText(Path.Combine(dir, "W1", "Level.sav"), marker);
        File.WriteAllText(Path.Combine(dir, "shared.sav"), marker + "-shared");
        return dir;
    }

    [Fact]
    public void Two_games_write_into_one_archive_and_either_can_be_restored_alone()
    {
        var pal = Save("compose-pal-", "palworld-world");
        var eld = Save("compose-eld-", "elden-world");

        var archive = Path.Combine(TestSupport.TempDir("compose-out-"), "profile.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
        {
            SaveBundle.WriteInto(zip,
                SaveBundle.Plan(pal, new BundleGame("palworld", "1623730", "Palworld"), Stamp,
                                mods: new[] { new BundleMod("A Palworld Mod", "1", 5, true) }),
                "games/palworld/");
            SaveBundle.WriteInto(zip,
                SaveBundle.Plan(eld, new BundleGame("elden-ring", "1245620", "Elden Ring"), Stamp),
                "games/elden-ring/");
        }

        // Each game's manifest is readable on its own terms.
        var palManifest = SaveBundle.ReadManifest(archive, "games/palworld/")!;
        var eldManifest = SaveBundle.ReadManifest(archive, "games/elden-ring/")!;
        Assert.Equal("palworld", palManifest.Game.Id);
        Assert.Equal("elden-ring", eldManifest.Game.Id);
        Assert.Equal("A Palworld Mod", Assert.Single(palManifest.Mods).Name);
        Assert.Empty(eldManifest.Mods);

        // And restoring one brings back only that game's files.
        var target = TestSupport.TempDir("compose-dest-");
        SaveBundle.Restore(archive, target, TestSupport.TempDir("compose-snaps-"), "elden-ring", "games/elden-ring/");

        Assert.Equal("elden-world", File.ReadAllText(Path.Combine(target, "W1", "Level.sav")));
        Assert.Equal("elden-world-shared", File.ReadAllText(Path.Combine(target, "shared.sav")));
        Assert.Equal(2, Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void The_wrong_game_is_still_refused_inside_an_archive()
    {
        // The guard has to survive composition, or a profile restore could put one game's save into
        // another game's folder - twelve times, in one action.
        var pal = Save("compose-guard-", "palworld-world");
        var archive = Path.Combine(TestSupport.TempDir("compose-guard-out-"), "profile.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
            SaveBundle.WriteInto(zip,
                SaveBundle.Plan(pal, new BundleGame("palworld", "1623730", "Palworld"), Stamp),
                "games/palworld/");

        var ex = Assert.Throws<InvalidOperationException>(() => SaveBundle.Restore(
            archive, TestSupport.TempDir("compose-guard-dest-"), TestSupport.TempDir("compose-guard-snaps-"),
            "elden-ring", "games/palworld/"));

        Assert.Contains("different game", ex.Message);
    }

    [Fact]
    public void A_prefix_is_forgiving_about_slashes_because_a_caller_will_get_it_wrong()
    {
        var pal = Save("compose-slash-", "palworld-world");
        var archive = Path.Combine(TestSupport.TempDir("compose-slash-out-"), "profile.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
            SaveBundle.WriteInto(zip,
                SaveBundle.Plan(pal, new BundleGame("palworld", null, null), Stamp),
                @"\games\palworld");     // no trailing slash, wrong separators

        Assert.NotNull(SaveBundle.ReadManifest(archive, "games/palworld"));
        Assert.NotNull(SaveBundle.ReadManifest(archive, "games/palworld/"));
    }

    [Fact]
    public void The_standalone_file_and_the_composed_copy_hold_identical_payloads()
    {
        // The property that makes this ONE format rather than two that look alike.
        var save = Save("compose-same-", "the-world");
        var game = new BundleGame("palworld", "1623730", "Palworld");

        var standalone = Path.Combine(TestSupport.TempDir("compose-same-a-"), "one" + SaveBundle.Extension);
        SaveBundle.Create(save, standalone, game, Stamp);

        var composed = Path.Combine(TestSupport.TempDir("compose-same-b-"), "profile.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(composed, System.IO.Compression.ZipArchiveMode.Create))
            SaveBundle.WriteInto(zip, SaveBundle.Plan(save, game, Stamp), "games/palworld/");

        string[] Payload(string path, string prefix)
        {
            using var z = System.IO.Compression.ZipFile.OpenRead(path);
            return z.Entries.Select(e => e.FullName)
                    .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(n => n[prefix.Length..]).OrderBy(n => n).ToArray();
        }

        Assert.Equal(Payload(standalone, "save/"), Payload(composed, "games/palworld/save/"));
    }
}
