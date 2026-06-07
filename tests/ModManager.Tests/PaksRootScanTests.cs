using ModManager.Core;

namespace ModManager.Tests;

public class PaksRootScanTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "paksroot-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    // Witchfire-shaped: Content/Paks holds 2 base paks + 2 mod paks; location form = paks-root.
    private GameEntry Setup()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var paks = Path.Combine(gameRoot, "Witchfire", "Content", "Paks");
        Directory.CreateDirectory(paks);
        File.WriteAllText(Path.Combine(paks, "pakchunk0-WindowsNoEditor.pak"), "base");          // base (name)
        File.WriteAllText(Path.Combine(paks, "pakchunk0optional-WindowsNoEditor.pak"), "baseopt"); // base (name)
        File.WriteAllText(Path.Combine(paks, "pakchunk30-2x-witchfire_P.pak"), "mod1");           // mod
        File.WriteAllText(Path.Combine(paks, "zz_Funner_Witchfire.pak"), "mod2");                 // mod

        return new GameEntry
        {
            Id = "witchfire", GameName = "Witchfire", Engine = "ue-pak",
            GameRoot = gameRoot, GroupingRule = "strip_underscore_p_suffix",
            FileExtensions = new[] { "pak", "ucas", "utoc" },
            DataDir = Path.Combine(_tmp, "data"),
            ModLocations = new[] { new ModLocation("mods", "Paks", "Witchfire/Content/Paks") { Form = "paks-root" } },
        };
    }

    [Fact]
    public async Task PaksRoot_lists_the_mods_and_never_the_base_game()
    {
        var game = Setup();
        Directory.CreateDirectory(game.DataDir!);
        var ctx = Scanner.GameContext(game);

        var mods = await Scanner.BuildModListAsync(ctx);
        var names = mods.Select(m => m.Name).ToList();

        Assert.Contains("pakchunk30-2x-witchfire", names);   // _P stripped by groupingRule
        Assert.Contains("zz_Funner_Witchfire", names);
        Assert.DoesNotContain(names, n => n.Contains("WindowsNoEditor"));
        Assert.DoesNotContain(names, n => n.StartsWith("pakchunk0"));
        Assert.Equal(2, mods.Count);
    }
}
