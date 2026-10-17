using System.Security.Cryptography;
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

// Patch Day item 7: the signed feed carries the newest release per version-locked loader as a NEW
// top-level `loaderReleases` list (not a field on loaders[], which only holds engine loaders). Keyed by
// the product name StaleLoaders uses. Descriptive only.
public class LoaderReleasesTests
{
    private static readonly IReadOnlySet<string> Engines = new HashSet<string> { "fromsoft", "bethesda" };
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private static LoaderReleaseEntry Rel(
        string? loader = "REFramework", string? version = "1.5.8",
        string? published = "2026-10-10T00:00:00Z", string? url = "https://github.com/praydog/REFramework/releases/tag/v1.5.8")
        => new() { Loader = loader, Version = version, PublishedUtc = published, Url = url };

    private static ManifestValidationResult Validate(params LoaderReleaseEntry[] rows)
        => ManifestValidator.Validate(new GameManifest { LoaderReleases = rows }, Engines, Now);

    [Fact]
    public void Round_trips_as_camelCase_with_key_assertions()
    {
        var original = new GameManifest { LoaderReleases = new[] { Rel() } };

        var json = JsonSerializer.Serialize(original, ManifestJson.Options);
        Assert.Contains("\"loaderReleases\"", json);
        Assert.Contains("\"loader\": \"REFramework\"", json);
        Assert.Contains("\"version\": \"1.5.8\"", json);
        Assert.Contains("\"publishedUtc\"", json);
        Assert.Contains("\"url\"", json);
        Assert.DoesNotContain("\"LoaderReleases\"", json);
        Assert.DoesNotContain("\"PublishedUtc\"", json);

        var back = Assert.Single(JsonSerializer.Deserialize<GameManifest>(json, ManifestJson.Options)!.LoaderReleases);
        Assert.Equal("REFramework", back.Loader);
        Assert.Equal("1.5.8", back.Version);
        Assert.Equal("2026-10-10T00:00:00Z", back.PublishedUtc);
    }

    [Fact]
    public void A_manifest_without_the_key_reads_as_empty()
    {
        var parsed = JsonSerializer.Deserialize<GameManifest>("{\"schemaVersion\":1,\"games\":[]}", ManifestJson.Options)!;
        Assert.Empty(parsed.LoaderReleases);
    }

    [Fact]
    public void A_good_row_survives_and_maps_to_a_LoaderRelease()
    {
        var result = Validate(Rel());
        var row = Assert.Single(result.Manifest.LoaderReleases);
        Assert.Empty(result.DroppedLoaderReleases);

        var release = row.ToRelease();
        Assert.NotNull(release);
        Assert.Equal(new LoaderRelease("REFramework", "1.5.8", new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), row.Url!), release);
        Assert.Equal(DateTimeKind.Utc, release!.PublishedUtc.Kind);
    }

    [Theory]
    [InlineData("http://example.com/x")]
    [InlineData("ftp://example.com/x")]
    [InlineData("/relative/path")]
    [InlineData("")]
    [InlineData(null)]
    public void A_non_https_url_drops_the_row(string? url)
    {
        var result = Validate(Rel(url: url));
        Assert.Empty(result.Manifest.LoaderReleases);
        Assert.Equal(new[] { "REFramework" }, result.DroppedLoaderReleases);
    }

    [Fact]
    public void A_future_publishedUtc_drops_the_row()
    {
        var result = Validate(Rel(published: "2026-10-17T00:00:00Z"));
        Assert.Empty(result.Manifest.LoaderReleases);
        Assert.Single(result.DroppedLoaderReleases);
    }

    [Fact]
    public void A_publishedUtc_at_or_before_now_is_kept()
    {
        Assert.Single(Validate(Rel(published: "2026-10-16T12:00:00Z")).Manifest.LoaderReleases);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_loader_name_drops_the_row(string? loader)
    {
        var result = Validate(Rel(loader: loader));
        Assert.Empty(result.Manifest.LoaderReleases);
        Assert.Single(result.DroppedLoaderReleases);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_empty_version_drops_the_row(string? version)
        => Assert.Empty(Validate(Rel(version: version)).Manifest.LoaderReleases);

    [Theory]
    [InlineData("yesterday")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unreadable_date_drops_the_row(string? published)
        => Assert.Empty(Validate(Rel(published: published)).Manifest.LoaderReleases);

    [Fact]
    public void Bad_rows_are_dropped_alone_and_the_rest_of_the_manifest_applies()
    {
        var manifest = new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "g", Name = "G", Engine = "fromsoft" } },
            LoaderReleases = new[]
            {
                Rel(loader: "UE4SS", version: "3.0.1"),
                Rel(url: "http://nope"),
                Rel(loader: "Mod Engine 2", version: "2.1.0"),
            },
        };

        var result = ManifestValidator.Validate(manifest, Engines, Now);

        Assert.Single(result.Manifest.Games);
        Assert.Equal(new[] { "UE4SS", "Mod Engine 2" }, result.Manifest.LoaderReleases.Select(r => r.Loader));
        Assert.Equal(new[] { "REFramework" }, result.DroppedLoaderReleases);
    }

    // "loaderReleases": null overrides the empty default; it must degrade, never throw.
    [Fact]
    public void A_null_list_or_null_row_degrades()
    {
        var parsed = JsonSerializer.Deserialize<GameManifest>(
            "{\"loaderReleases\":[null]}", ManifestJson.Options)!;
        var result = ManifestValidator.Validate(parsed, Engines, Now);
        Assert.Empty(result.Manifest.LoaderReleases);
        Assert.Single(result.DroppedLoaderReleases);

        var nulled = JsonSerializer.Deserialize<GameManifest>("{\"loaderReleases\":null}", ManifestJson.Options)!;
        Assert.Empty(ManifestValidator.Validate(nulled, Engines, Now).Manifest.LoaderReleases);
    }

    [Fact]
    public void Remote_list_replaces_the_embedded_list_when_it_has_one()
    {
        var embedded = new GameManifest { LoaderReleases = new[] { Rel(loader: "UE4SS", version: "1") } };
        var remote = new GameManifest { LoaderReleases = new[] { Rel(loader: "REFramework", version: "2") } };

        var merged = EffectiveManifest.Merge(embedded, remote);

        var row = Assert.Single(merged.LoaderReleases);
        Assert.Equal("REFramework", row.Loader);
    }

    [Fact]
    public void Embedded_list_stands_when_the_remote_has_none()
    {
        var embedded = new GameManifest { LoaderReleases = new[] { Rel(loader: "UE4SS", version: "1") } };

        Assert.Equal("UE4SS", Assert.Single(EffectiveManifest.Merge(embedded, new GameManifest()).LoaderReleases).Loader);
        Assert.Equal("UE4SS", Assert.Single(EffectiveManifest.Merge(embedded, null).LoaderReleases).Loader);
    }

    // Compatibility: GameManifest has no strict-unknown-member handling, so a binary without this
    // field skips the key. This pins the current binary's tolerance for any extra top-level field.
    [Fact]
    public void An_unknown_top_level_field_still_loads_and_verifies()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var json = "{\"schemaVersion\":1,\"futureThing\":{\"a\":1},\"games\":[{\"id\":\"g\",\"name\":\"G\"}],\"loaderReleases\":[]}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var sig = ecdsa.SignData(bytes, HashAlgorithmName.SHA256, ManifestSignature.Format);

        var loaded = ManifestLoader.LoadVerifiedRemote(bytes, sig, ecdsa.ExportSubjectPublicKeyInfo(), new Version(0, 6, 0), Engines);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Games);
    }

    [Fact]
    public void A_feed_carrying_loaderReleases_verifies_and_keeps_schema_version_1()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var m = new GameManifest { SchemaVersion = 1, LoaderReleases = new[] { Rel() } };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(m, ManifestJson.Options);
        var sig = ecdsa.SignData(bytes, HashAlgorithmName.SHA256, ManifestSignature.Format);

        var loaded = ManifestLoader.LoadVerifiedRemote(bytes, sig, ecdsa.ExportSubjectPublicKeyInfo(), new Version(0, 6, 0), Engines);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.LoaderReleases);
        Assert.Equal(1, ManifestLoader.KnownSchemaVersion);
    }
}
