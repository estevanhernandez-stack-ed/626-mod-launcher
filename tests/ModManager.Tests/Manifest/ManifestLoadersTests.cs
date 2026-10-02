using System.Text.Json;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

// Ban-safe loaders moved from compiled code into the manifest so a game flagged as ban-risk can be
// pointed at a safe loader the same day, not one release later. The feed stays descriptive: it names
// an exe, a URL and a claim; the compiled code still does the looking and the launching. These tests
// hold the gate, the merge and the on-disk shape. See
// docs/superpowers/specs/2026-10-01-safe-loaders-in-the-feed-design.md.
public class ManifestLoadersTests
{
    private static readonly IReadOnlySet<string> Engines = new HashSet<string> { "fromsoft", "bethesda" };

    private static LoaderManifestEntry Loader(
        string id = "test-loader",
        string engine = "fromsoft",
        string[]? exes = null,
        string getUrl = "https://example.com/loader",
        bool? banSafe = true,
        string? steamAppId = null)
        => new()
        {
            Id = id,
            DisplayName = "Test Loader",
            Engine = engine,
            SteamAppId = steamAppId,
            LauncherExeNames = exes ?? new[] { "test_launcher.exe" },
            GetUrl = getUrl,
            Author = "someone",
            BanSafe = banSafe,
        };

    private static ManifestValidationResult Validate(params LoaderManifestEntry[] loaders)
        => ManifestValidator.Validate(new GameManifest { Loaders = loaders }, Engines);

    [Fact]
    public void A_well_formed_loader_survives_validation()
    {
        var result = Validate(Loader());

        Assert.Single(result.Manifest.Loaders);
        Assert.Empty(result.RejectedLoaders);
        Assert.Empty(result.SkippedLoaders);
    }

    // Same forward-compatible rule as games: an old binary reading a newer feed simply does not see a
    // loader for an engine it cannot handle.
    [Fact]
    public void A_loader_for_an_unknown_engine_is_skipped_not_rejected()
    {
        var result = Validate(Loader(engine: "brand-new-engine"));

        Assert.Empty(result.Manifest.Loaders);
        Assert.Equal(new[] { "test-loader" }, result.SkippedLoaders);
        Assert.Empty(result.RejectedLoaders);
    }

    // The one trust-sensitive field: the app launches whatever file has this name in the game folder.
    // Anything but a bare *.exe filename could point it somewhere else, or at something that is not a
    // program at all.
    [Theory]
    [InlineData(@"..\loader.exe")]
    [InlineData("../loader.exe")]
    [InlineData(@"sub\loader.exe")]
    [InlineData("sub/loader.exe")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData("C:loader.exe")]
    [InlineData("loader.bat")]
    [InlineData("loader.exe.lnk")]
    [InlineData("loader")]
    [InlineData(" loader.exe")]
    [InlineData("loader.exe ")]
    [InlineData(".exe")]
    [InlineData("")]
    public void A_launcher_name_that_is_not_a_bare_exe_filename_rejects_the_loader(string exe)
    {
        var result = Validate(Loader(exes: new[] { "good_launcher.exe", exe }));

        Assert.Empty(result.Manifest.Loaders);
        Assert.Equal(new[] { "test-loader" }, result.RejectedLoaders);
    }

    // Windows' invalid filename characters. Checked from a fixed set, not the platform's: the miner
    // runs on Linux, where Path.GetInvalidFileNameChars() is only '/' and NUL, and it must not sign a
    // name every Windows client then drops.
    [Theory]
    [InlineData("er|launcher.exe")]
    [InlineData("launcher?.exe")]
    [InlineData("<x>.exe")]
    [InlineData("a*b.exe")]
    [InlineData("quote\"d.exe")]
    [InlineData("tab\there.exe")]
    public void Windows_invalid_filename_characters_reject_the_loader_on_every_platform(string exe)
        => Assert.Equal(new[] { "test-loader" }, Validate(Loader(exes: new[] { exe })).RejectedLoaders);

    // Review on #355: the merge matches ids exactly, so a case variant would have been appended beside
    // the built-in rather than replacing it. Refusing the spelling is what makes exact matching safe.
    [Theory]
    [InlineData("Seamless-Coop")]
    [InlineData("seamless_coop")]
    [InlineData("seamless coop")]
    [InlineData("-seamless")]
    [InlineData("seamless-")]
    [InlineData("seamless--coop")]
    public void A_loader_id_that_is_not_lowercase_kebab_is_rejected(string id)
        => Assert.Single(Validate(Loader(id: id)).RejectedLoaders);

    // An empty pin passes a null check, pins the loader to no game, and makes it vanish everywhere.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("12a45")]
    public void A_steam_app_id_that_is_not_digits_rejects_the_loader(string app)
        => Assert.Equal(new[] { "test-loader" }, Validate(Loader(steamAppId: app)).RejectedLoaders);

    // Review on #355: the ban-risk gate keys a dictionary on the loader id, so two loaders with one id
    // would throw there. The first wins; the second is rejected by name.
    [Fact]
    public void A_duplicate_loader_id_keeps_the_first_and_rejects_the_second()
    {
        var result = Validate(Loader(id: "a", getUrl: "https://example.com/first"), Loader(id: "a", getUrl: "https://example.com/second"));

        Assert.Equal("https://example.com/first", Assert.Single(result.Manifest.Loaders).GetUrl);
        Assert.Equal(new[] { "a" }, result.RejectedLoaders);
    }

    // Review on #355: "loaders": null, or a null entry, is reachable from JSON. It has to degrade like
    // any bad entry; a throw would escape LoadVerifiedRemote (which catches only JsonException) and
    // break the fall-back-to-embedded promise at startup.
    [Fact]
    public void A_null_loader_list_or_entry_is_dropped_rather_than_thrown()
    {
        var nullList = JsonSerializer.Deserialize<GameManifest>("{\"games\":[],\"loaders\":null}", ManifestJson.Options)!;
        var nullEntry = JsonSerializer.Deserialize<GameManifest>("{\"games\":[],\"loaders\":[null]}", ManifestJson.Options)!;

        Assert.Empty(ManifestValidator.Validate(nullList, Engines).Manifest.Loaders);
        var r = ManifestValidator.Validate(nullEntry, Engines);
        Assert.Empty(r.Manifest.Loaders);
        Assert.Single(r.RejectedLoaders);
    }

    // Same order as games: an unknown engine is judged first, so a newer feed's loader lands in the
    // forward-compat bucket even when it has something this binary would also object to.
    [Fact]
    public void An_unknown_engine_is_skipped_before_anything_else_is_judged()
    {
        var result = Validate(Loader(engine: "future-engine", exes: new[] { "future.launcher" }));

        Assert.Equal(new[] { "test-loader" }, result.SkippedLoaders);
        Assert.Empty(result.RejectedLoaders);
    }

    [Fact]
    public void Exe_names_are_matched_case_insensitively_on_the_extension()
        => Assert.Single(Validate(Loader(exes: new[] { "Loader_Launcher.EXE" })).Manifest.Loaders);

    [Fact]
    public void A_loader_with_no_launcher_names_is_rejected()
        => Assert.Equal(new[] { "test-loader" }, Validate(Loader(exes: Array.Empty<string>())).RejectedLoaders);

    // "Get it here" is a link the user clicks. https only, absolute only.
    [Theory]
    [InlineData("http://example.com/loader")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/loader.exe")]
    [InlineData("example.com/loader")]
    [InlineData("")]
    public void A_get_url_that_is_not_absolute_https_rejects_the_loader(string url)
        => Assert.Equal(new[] { "test-loader" }, Validate(Loader(getUrl: url)).RejectedLoaders);

    [Fact]
    public void Blank_identity_fields_reject_the_loader()
    {
        var result = Validate(
            Loader() with { Id = "" },
            Loader(id: "no-name") with { DisplayName = " " },
            Loader(id: "no-engine") with { Engine = null });

        Assert.Empty(result.Manifest.Loaders);
        Assert.Equal(3, result.RejectedLoaders.Count);
    }

    // A bad loader is dropped alone, the way an unsafe modPath drops one game: the rest of the feed,
    // games included, still applies.
    [Fact]
    public void A_rejected_loader_does_not_take_the_rest_of_the_feed_with_it()
    {
        var manifest = new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "elden-ring", Name = "Elden Ring", Engine = "fromsoft" } },
            Loaders = new[] { Loader(id: "good"), Loader(id: "bad", exes: new[] { @"..\x.exe" }) },
        };

        var result = ManifestValidator.Validate(manifest, Engines);

        Assert.Single(result.Manifest.Games);
        Assert.Equal("good", Assert.Single(result.Manifest.Loaders).Id);
    }

    [Fact]
    public void A_remote_loader_with_a_new_id_is_appended()
    {
        var embedded = new GameManifest { Loaders = new[] { Loader(id: "a") } };
        var remote = new GameManifest { Loaders = new[] { Loader(id: "b") } };

        var merged = EffectiveManifest.Merge(embedded, remote);

        Assert.Equal(new[] { "a", "b" }, merged.Loaders.Select(l => l.Id));
    }

    // A feed loader is always complete, because the remote manifest is validated on its own before the
    // merge. So a feed loader replaces the built-in with the same id outright.
    [Fact]
    public void A_remote_loader_with_an_existing_id_replaces_the_built_in()
    {
        var embedded = new GameManifest { Loaders = new[] { Loader(id: "a", steamAppId: "1245620") } };
        var remote = new GameManifest
        {
            Loaders = new[] { Loader(id: "a", getUrl: "https://example.com/moved") },   // no steamAppId
        };

        var merged = Assert.Single(EffectiveManifest.Merge(embedded, remote).Loaders);

        Assert.Equal("https://example.com/moved", merged.GetUrl);
        Assert.Null(merged.SteamAppId);   // replacement can widen a pinned loader to engine-wide
    }

    // What the real path does with a partial correction: the remote is validated before the merge, so
    // an entry carrying only the field to change is rejected and the built-in stands. A feed corrects a
    // loader by restating it whole.
    [Fact]
    public void A_partial_feed_loader_is_rejected_before_it_can_merge()
    {
        var embedded = new GameManifest { Loaders = new[] { Loader(id: "a", banSafe: true) } };
        var partial = new GameManifest { Loaders = new[] { new LoaderManifestEntry { Id = "a", BanSafe = false } } };

        var validated = ManifestValidator.Validate(partial, Engines);
        var merged = EffectiveManifest.Merge(embedded, validated.Manifest);

        Assert.Equal(new[] { "a" }, validated.RejectedLoaders);
        Assert.True(Assert.Single(merged.Loaders).BanSafe);
    }

    // A ban-safety claim can be corrected in either direction. Withdrawing one is the direction that
    // matters most: a loader found to trip anti-cheat must stop being recommended without a release.
    [Fact]
    public void The_feed_can_withdraw_a_ban_safe_claim()
    {
        var embedded = new GameManifest { Loaders = new[] { Loader(id: "a", banSafe: true) } };
        var remote = ManifestValidator.Validate(
            new GameManifest { Loaders = new[] { Loader(id: "a", banSafe: false) } }, Engines).Manifest;

        Assert.False(Assert.Single(EffectiveManifest.Merge(embedded, remote).Loaders).BanSafe);
    }

    [Fact]
    public void A_feed_without_loaders_keeps_the_embedded_ones()
    {
        var embedded = new GameManifest { Loaders = new[] { Loader(id: "a") } };

        Assert.Single(EffectiveManifest.Merge(embedded, new GameManifest()).Loaders);
    }

    // camelCase on disk (project rule). The string checks are what prove it: the manifest reads
    // case-insensitively, so a round trip alone would pass with PascalCase keys too.
    [Fact]
    public void Loaders_round_trip_as_camelCase()
    {
        var original = new GameManifest { Loaders = new[] { Loader(steamAppId: "1245620") with { EditsSaves = false } } };

        var json = JsonSerializer.Serialize(original, ManifestJson.Options);
        Assert.Contains("\"loaders\"", json);
        Assert.Contains("\"launcherExeNames\"", json);
        Assert.Contains("\"getUrl\"", json);
        Assert.Contains("\"banSafe\"", json);
        Assert.Contains("\"steamAppId\"", json);
        Assert.DoesNotContain("\"LauncherExeNames\"", json);

        var back = Assert.Single(JsonSerializer.Deserialize<GameManifest>(json, ManifestJson.Options)!.Loaders);
        Assert.Equal(original.Loaders[0].LauncherExeNames, back.LauncherExeNames);
        Assert.Equal("https://example.com/loader", back.GetUrl);
        Assert.Equal("1245620", back.SteamAppId);
        Assert.True(back.BanSafe);
        Assert.False(back.EditsSaves);
    }

    // An older feed has no "loaders" key at all. That must read as an empty list, never null.
    [Fact]
    public void A_manifest_without_a_loaders_key_reads_as_empty()
    {
        var parsed = JsonSerializer.Deserialize<GameManifest>("{\"schemaVersion\":1,\"games\":[]}", ManifestJson.Options)!;

        Assert.NotNull(parsed.Loaders);
        Assert.Empty(parsed.Loaders);
    }

    // The migration guard. The two loaders that were compiled into KnownLoaderCatalog moved into the
    // embedded manifest unchanged; this pins every field so the move cannot quietly change behaviour.
    [Fact]
    public void The_embedded_manifest_carries_the_two_formerly_compiled_loaders_unchanged()
    {
        var loaders = EmbeddedGameManifest.Current.Loaders;
        Assert.Equal(new[] { "mod-engine-2", "seamless-coop" }, loaders.Select(l => l.Id));

        var me2 = loaders[0];
        Assert.Equal("Mod Engine 2", me2.DisplayName);
        Assert.Equal("fromsoft", me2.Engine);
        Assert.Null(me2.SteamAppId);
        Assert.Equal(new[] { "modengine2_launcher.exe" }, me2.LauncherExeNames);
        Assert.Equal("https://github.com/soulsmods/ModEngine2/releases", me2.GetUrl);
        Assert.Equal("soulsmods (ModEngine2)", me2.Author);
        Assert.True(me2.BanSafe);
        Assert.NotEqual(true, me2.EditsSaves);

        var sc = loaders[1];
        Assert.Equal("Seamless Co-op", sc.DisplayName);
        Assert.Equal("fromsoft", sc.Engine);
        Assert.Equal("1245620", sc.SteamAppId);
        Assert.Equal(new[] { "launch_elden_ring_seamlesscoop.exe", "ersc_launcher.exe" }, sc.LauncherExeNames);
        Assert.Equal("https://www.nexusmods.com/eldenring/mods/510", sc.GetUrl);
        Assert.Equal("LukeYui", sc.Author);
        Assert.True(sc.BanSafe);
        Assert.NotEqual(true, sc.EditsSaves);
    }
}
