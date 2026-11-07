using ModManager.Core;
using ModManager.Core.Persistence;

namespace ModManager.Tests;

/// <summary>"Don't ask for this game" on the save-type offer is stored on the game's registry entry.</summary>
public class SaveOfferDismissalTests : IDisposable
{
    private readonly string _tmp = TestSupport.TempDir("mmb-saveoffer-dismiss-");
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    [Fact]
    public void SaveOfferDismissed_round_trips_as_camelCase_and_is_omitted_when_null()
    {
        var reg = Registry.EmptyRegistry();
        reg.Games.Add(new GameEntry { Id = "elden-ring", GameName = "ELDEN RING", Engine = "fromsoft", SaveOfferDismissed = new[] { ".sl2" } });
        reg.Games.Add(new GameEntry { Id = "plain", GameName = "Plain", Engine = "custom" });

        RegistryStore.Save(_tmp, reg);
        var json = File.ReadAllText(Path.Combine(_tmp, RegistryStore.FileName));

        Assert.Contains("\"saveOfferDismissed\"", json);
        Assert.DoesNotContain("\"SaveOfferDismissed\"", json);
        Assert.Equal(1, json.Split("\"saveOfferDismissed\"").Length - 1);   // the null one writes no key

        var loaded = RegistryStore.Load(_tmp);
        Assert.Equal(new[] { ".sl2" }, loaded.Games.Single(g => g.Id == "elden-ring").SaveOfferDismissed);
        Assert.Null(loaded.Games.Single(g => g.Id == "plain").SaveOfferDismissed);
    }
}
