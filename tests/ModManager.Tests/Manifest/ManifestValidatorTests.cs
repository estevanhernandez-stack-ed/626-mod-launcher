using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

public class ManifestValidatorTests
{
    private static GameManifest Wrap(params GameManifestEntry[] games)
        => new() { Games = games };

    private static GameManifestEntry Entry(string id, string? engine = "bethesda", string? modPath = null)
        => new()
        {
            Id = id,
            Name = id,
            Engine = engine,
            ModPath = modPath,
            Stores = new StoreIds { SteamAppId = "1" },
            Provenance = new ManifestProvenance { Sources = new[] { ManifestSources.KnownEngines } },
        };

    private static readonly IReadOnlySet<string> KnownEngineKeys =
        new HashSet<string> { "bethesda", "ue-pak", "fromsoft", "custom" };

    [Fact]
    public void Unknown_engine_is_skipped_not_fatal_and_reported()
    {
        var result = ManifestValidator.Validate(
            Wrap(Entry("good", "bethesda"), Entry("future", "rpgmaker-mz")),
            KnownEngineKeys);

        Assert.DoesNotContain(result.Manifest.Games, g => g.Id == "future");
        Assert.Contains(result.Manifest.Games, g => g.Id == "good");
        Assert.Contains("future", result.SkippedUnknownEngines);
    }

    [Fact]
    public void Null_engine_entry_is_kept_not_skipped()
    {
        // nexus-only entries (Windrose / Witchfire) carry no engine and must survive.
        var result = ManifestValidator.Validate(Wrap(Entry("witchfire", engine: null)), KnownEngineKeys);
        Assert.Contains(result.Manifest.Games, g => g.Id == "witchfire");
        Assert.Empty(result.SkippedUnknownEngines);
    }

    [Theory]
    [InlineData("C:/Windows/System32")]   // absolute
    [InlineData("../../escape")]           // traversal
    [InlineData("a/../b")]                 // traversal mid-path
    [InlineData("D:relative")]             // drive-qualified
    public void Unsafe_modPath_is_rejected(string modPath)
    {
        var result = ManifestValidator.Validate(Wrap(Entry("bad", "bethesda", modPath)), KnownEngineKeys);
        Assert.DoesNotContain(result.Manifest.Games, g => g.Id == "bad");
        Assert.Contains("bad", result.RejectedEntries);
    }

    [Theory]
    [InlineData("Data")]
    [InlineData("Content/Paks/~mods")]
    [InlineData("Pal/Content/Paks/~mods")]
    public void Clean_relative_modPath_passes(string modPath)
    {
        var result = ManifestValidator.Validate(Wrap(Entry("ok", "bethesda", modPath)), KnownEngineKeys);
        Assert.Contains(result.Manifest.Games, g => g.Id == "ok");
        Assert.Empty(result.RejectedEntries);
    }
}
