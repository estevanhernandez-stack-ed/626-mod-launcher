using System.IO;
using ModManager.Core.Loaders;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Loaders;

// The point of moving loaders into the feed: a loader the signed feed adds reaches detection and the
// ban-risk gate without a release, and a ban-safe claim the feed withdraws stops being recommended.
[Collection("ManifestState")]
public class FeedLoaderTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null); // never leak state to other tests

    private static string TempPlayFolder(params string[] files)
    {
        var d = Path.Combine(Path.GetTempPath(), "mm-feed-loaders-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        foreach (var f in files) File.WriteAllText(Path.Combine(d, f), "x");
        return d;
    }

    private static readonly LoaderManifestEntry NewLoader = new()
    {
        Id = "nightreign-seamless",
        DisplayName = "Nightreign Seamless Co-op",
        Engine = "fromsoft",
        SteamAppId = "2622380",
        LauncherExeNames = new[] { "nrsc_launcher.exe" },
        GetUrl = "https://example.com/nrsc",
        Author = "someone",
        BanSafe = true,
    };

    [Fact]
    public void A_loader_the_feed_adds_is_detected_and_offered_by_the_gate()
    {
        var dir = TempPlayFolder("nrsc_launcher.exe");
        try
        {
            Assert.Empty(LoaderScan.Detect(dir, "fromsoft", "2622380"));   // before the feed: unknown

            // Through the same gate a fetched feed passes, so this proves the loader survives it.
            var remote = ManifestValidator.Validate(
                new GameManifest { Loaders = new[] { NewLoader } },
                ModManager.Core.EnginePresets.Presets.Keys.ToHashSet());
            Assert.Empty(remote.RejectedLoaders);
            EffectiveManifest.SetRemote(remote.Manifest);

            var found = Assert.Single(LoaderScan.Detect(dir, "fromsoft", "2622380"));
            Assert.Equal("nightreign-seamless", found.Loader.LoaderId);
            Assert.Equal(Path.Combine(dir, "nrsc_launcher.exe"), found.LauncherPath);
            Assert.Contains(LoaderScan.BanSafeFor("fromsoft", "2622380"), l => l.LoaderId == "nightreign-seamless");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_ban_safe_claim_the_feed_withdraws_leaves_the_gate()
    {
        Assert.Contains(LoaderScan.BanSafeFor("fromsoft", "1245620"), l => l.LoaderId == "seamless-coop");

        // The whole loader, restated with the claim withdrawn: a feed loader is always complete.
        var seamless = EmbeddedGameManifest.Current.Loaders.Single(l => l.Id == "seamless-coop");
        EffectiveManifest.SetRemote(new GameManifest { Loaders = new[] { seamless with { BanSafe = false } } });

        Assert.DoesNotContain(LoaderScan.BanSafeFor("fromsoft", "1245620"), l => l.LoaderId == "seamless-coop");
        // Still a loader, still detectable and launchable; just no longer recommended as the safe path.
        Assert.Contains(KnownLoaderCatalog.Catalog, l => l.LoaderId == "seamless-coop" && !l.BanSafe);
    }

    [Fact]
    public void Clearing_the_feed_returns_the_catalog_to_the_embedded_loaders()
    {
        EffectiveManifest.SetRemote(new GameManifest { Loaders = new[] { NewLoader } });
        Assert.Equal(3, KnownLoaderCatalog.Catalog.Count);

        EffectiveManifest.SetRemote(null);

        Assert.Equal(new[] { "mod-engine-2", "seamless-coop" }, KnownLoaderCatalog.Catalog.Select(l => l.LoaderId));
    }
}
