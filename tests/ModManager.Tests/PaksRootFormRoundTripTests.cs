using ModManager.Core;
using ModManager.Core.Persistence;

namespace ModManager.Tests;

public class PaksRootFormRoundTripTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "paksform-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { /* best-effort temp cleanup */ } }

    [Fact]
    public void ModLocation_form_paks_root_round_trips_through_registry_as_camelCase()
    {
        Directory.CreateDirectory(_tmp);
        var reg = Registry.EmptyRegistry();
        var game = new GameEntry
        {
            Id = "witchfire", GameName = "Witchfire", Engine = "ue-pak",
            GameRoot = @"C:\game",
            ModLocations = new[] { new ModLocation("mods", "Paks", "Witchfire/Content/Paks") { Form = "paks-root" } },
        };
        reg.Games.Add(game);

        RegistryStore.Save(_tmp, reg);

        // 1) On-disk shape: camelCase "form" key with the paks-root value.
        var json = File.ReadAllText(Path.Combine(_tmp, RegistryStore.FileName));
        Assert.Contains("\"form\"", json);
        Assert.Contains("paks-root", json);
        Assert.DoesNotContain("\"Form\"", json);

        // 2) Round-trips back through Load.
        var loaded = RegistryStore.Load(_tmp);
        var loc = loaded.Games.Single(g => g.Id == "witchfire").ModLocations.Single();
        Assert.Equal("paks-root", loc.Form);
        Assert.Equal("Witchfire/Content/Paks", loc.Path);
    }

    [Fact]
    public void ModLocation_multi_segment_nested_path_round_trips()  // Marvel Rivals: 2-wrapper project
    {
        Directory.CreateDirectory(_tmp);
        var reg = Registry.EmptyRegistry();
        // The OS-separator value detection produces for a two-wrapper project.
        var nested = Path.Combine("MarvelGame", "Marvel", "Content", "Paks", "~mods");
        var game = new GameEntry
        {
            Id = "marvel-rivals", GameName = "Marvel Rivals", Engine = "ue-pak",
            GameRoot = @"C:\game",
            ModLocations = new[] { new ModLocation("mods", "mods", nested) },
        };
        reg.Games.Add(game);

        RegistryStore.Save(_tmp, reg);
        var loaded = RegistryStore.Load(_tmp);
        var loc = loaded.Games.Single(g => g.Id == "marvel-rivals").ModLocations.Single();
        Assert.Equal(nested, loc.Path);  // the 2-segment path survives Save/Load unchanged
        Assert.Equal("MarvelGame/Marvel/Content/Paks/~mods", loc.Path.Replace('\\', '/'));
    }
}
