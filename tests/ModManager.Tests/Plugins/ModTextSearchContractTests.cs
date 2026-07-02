using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Plugins;

public class ModTextSearchContractTests
{
    // A minimal in-test fake: proves IModTextSearch is a separate, assignable, callable contract —
    // never a member added to IModSource (that would break already-installed plugins at type load).
    private sealed class FakeTextSearchSource : IModTextSearch
    {
        public Task<IReadOnlyList<SourceSearchHit>> SearchAsync(string gameDomain, string query)
            => Task.FromResult<IReadOnlyList<SourceSearchHit>>(
                new[] { new SourceSearchHit(gameDomain, 42, "Fake Mod", "Fake Author", "A summary", 10, "https://example.test/42") });
    }

    [Fact]
    public void SourceSearchHit_records_are_value_equal()
    {
        var a = new SourceSearchHit("skyrimspecialedition", 42, "Fake Mod", "Fake Author", "A summary", 10, "https://example.test/42");
        var b = new SourceSearchHit("skyrimspecialedition", 42, "Fake Mod", "Fake Author", "A summary", 10, "https://example.test/42");
        var c = a with { ModId = 43 };

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public async Task Fake_source_implements_IModTextSearch_and_is_callable()
    {
        IModTextSearch search = new FakeTextSearchSource();

        var hits = await search.SearchAsync("skyrimspecialedition", "unofficial patch");

        Assert.Single(hits);
        Assert.Equal(42, hits[0].ModId);
        Assert.Equal("Fake Mod", hits[0].Name);
    }
}
