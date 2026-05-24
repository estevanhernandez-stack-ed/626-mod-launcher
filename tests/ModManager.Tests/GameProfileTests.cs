using ModManager.Core;

namespace ModManager.Tests;

// The declarable per-game profile the app consults to know which features apply. This round only
// the SaveTypes slice is populated; adding a game/engine's save types is a one-line catalog entry.
public class GameProfileTests
{
    [Fact]
    public void FromSoft_declares_its_three_save_types_in_order()
    {
        var p = GameProfiles.Resolve("fromsoft", "1245620");
        Assert.Equal(new[] { ".sl2", ".co2", ".err" }, p.SaveTypes.Select(s => s.Extension).ToArray());
        Assert.Equal("Vanilla", p.SaveTypes[0].Label);
        Assert.Equal("Seamless Co-op", p.SaveTypes[1].Label);
        Assert.Equal("Reforged", p.SaveTypes[2].Label);
    }

    [Fact]
    public void Unknown_engine_declares_no_save_types_baseline_only()
    {
        Assert.Empty(GameProfiles.Resolve("custom", null).SaveTypes);
        Assert.Empty(GameProfiles.Resolve(null, null).SaveTypes);
    }
}
