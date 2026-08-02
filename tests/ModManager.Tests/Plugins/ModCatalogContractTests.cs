using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using ModManager.Plugins.Abstractions;
using Xunit;

public class ModCatalogContractTests
{
    [Fact]
    public void IModCatalog_has_expected_shape()
    {
        var m = typeof(IModCatalog).GetMethod("SearchCatalogAsync")!;
        Assert.Equal(typeof(Task<IReadOnlyList<SourceSearchHit>>), m.ReturnType);
        var p = m.GetParameters();
        Assert.Equal(typeof(string), p[0].ParameterType);   // gameDomain
        Assert.Equal(typeof(string), p[1].ParameterType);   // query
    }

    [Fact]
    public void IModTextSearch_and_SourceSearchHit_unchanged_abi_safe()
    {
        // IModCatalog must be ADDITIVE — the identify search + the shared DTO are untouched.
        Assert.NotNull(typeof(IModTextSearch).GetMethod("SearchAsync"));
        var hit = typeof(SourceSearchHit);
        foreach (var n in new[] { "GameDomain", "ModId", "Name", "Author", "Summary", "EndorsementCount", "Url" })
            Assert.NotNull(hit.GetProperty(n));
    }
}
