using ModManager.Core;

namespace ModManager.Tests;

public class PaksRootGuardTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "paksguard-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private (GameContext ctx, string paks) Setup()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var paks = Path.Combine(gameRoot, "Witchfire", "Content", "Paks");
        Directory.CreateDirectory(paks);
        File.WriteAllText(Path.Combine(paks, "pakchunk0-WindowsNoEditor.pak"), "base");
        File.WriteAllText(Path.Combine(paks, "zz_Funner_Witchfire.pak"), "mod");
        var game = new GameEntry
        {
            Id = "witchfire", GameName = "Witchfire", Engine = "ue-pak",
            GameRoot = gameRoot, GroupingRule = "strip_underscore_p_suffix",
            FileExtensions = new[] { "pak", "ucas", "utoc" },
            DataDir = Path.Combine(_tmp, "data"),
            ModLocations = new[] { new ModLocation("mods", "Paks", "Witchfire/Content/Paks") { Form = "paks-root" } },
        };
        Directory.CreateDirectory(game.DataDir!);
        return (Scanner.GameContext(game), paks);
    }

    [Fact]
    public void Guard_refuses_to_move_a_base_game_pak()
    {
        var (ctx, _) = Setup();
        var loc = ctx.Locations.First(l => l.Name == "mods");
        // A (hostile/buggy) Mod claiming a base pak as its file.
        var hostile = new Mod
        {
            Name = "pakchunk0-WindowsNoEditor", Location = "mods", Enabled = true, IsFolder = false,
            Files = new List<string> { "pakchunk0-WindowsNoEditor.pak" },
        };
        var ex = Assert.Throws<InvalidOperationException>(() => Scanner.GuardNoBasePakMove(hostile, loc));
        Assert.Contains("base", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Guard_allows_a_real_mod_pak()
    {
        var (ctx, _) = Setup();
        var loc = ctx.Locations.First(l => l.Name == "mods");
        var realMod = new Mod
        {
            Name = "zz_Funner_Witchfire", Location = "mods", Enabled = true, IsFolder = false,
            Files = new List<string> { "zz_Funner_Witchfire.pak" },
        };
        // Does not throw.
        Scanner.GuardNoBasePakMove(realMod, loc);
    }

    [Fact]
    public void Guard_is_a_noop_for_non_paks_root_locations()
    {
        var (ctx, _) = Setup();
        // A 'files'-form location: even a base-named file is not guarded here (base+mods never share a
        // dedicated mod folder in the files form). Construct a files-form ModLocationCtx.
        var filesLoc = ctx.Locations.First(l => l.Name == "mods") with { Form = "files" };
        var m = new Mod
        {
            Name = "pakchunk0-WindowsNoEditor", Location = "mods", Enabled = true, IsFolder = false,
            Files = new List<string> { "pakchunk0-WindowsNoEditor.pak" },
        };
        Scanner.GuardNoBasePakMove(m, filesLoc); // must not throw — guard only applies to paks-root
    }
}
