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
        var ex = Assert.Throws<BaseGameFileException>(() => Scanner.GuardNoBasePakMove(hostile, loc));
        Assert.Equal("pakchunk0-WindowsNoEditor.pak is part of the game, so 626 won't turn it off.", ex.Message);
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
    public void Guard_is_a_noop_for_a_dedicated_mod_folder()
    {
        var (ctx, paks) = Setup();
        // A files-form location that is a dedicated mod folder (~mods): even a base-named file is not guarded
        // there, because base and mods never share it. A files-form location pointed at Content/Paks itself
        // IS guarded now (BaseGameFiles.IsSharedPaksFolder), which the Content/Paks case below pins.
        var filesLoc = ctx.Locations.First(l => l.Name == "mods") with { Form = "files", Abs = Path.Combine(paks, "~mods") };
        var m = new Mod
        {
            Name = "pakchunk0-WindowsNoEditor", Location = "mods", Enabled = true, IsFolder = false,
            Files = new List<string> { "pakchunk0-WindowsNoEditor.pak" },
        };
        Scanner.GuardNoBasePakMove(m, filesLoc); // must not throw — a dedicated mod folder holds no base paks
    }

    [Fact]
    public void Guard_refuses_a_base_pak_in_a_files_form_content_paks_location()
    {
        var (ctx, _) = Setup();
        // The registration points a plain files-form location at Content/Paks: base and mods share it.
        var filesLoc = ctx.Locations.First(l => l.Name == "mods") with { Form = "files" };
        var m = new Mod
        {
            Name = "pakchunk0-WindowsNoEditor", Location = "mods", Enabled = true, IsFolder = false,
            Files = new List<string> { "pakchunk0-WindowsNoEditor.pak" },
        };
        Assert.Throws<BaseGameFileException>(() => Scanner.GuardNoBasePakMove(m, filesLoc));
    }

    [Fact]
    public void Guard_refuses_a_base_named_pak_resident_only_in_a_mirror()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot2");
        var paks = Path.Combine(gameRoot, "Witchfire", "Content", "Paks");
        var mirror = Path.Combine(gameRoot, "Mirror", "Paks");
        Directory.CreateDirectory(paks);
        Directory.CreateDirectory(mirror);
        // Base pak lives ONLY in the mirror, not in loc.Abs.
        File.WriteAllText(Path.Combine(mirror, "pakchunk0-WindowsNoEditor.pak"), "base");
        var game = new GameEntry
        {
            Id = "wf2", GameName = "WF2", Engine = "ue-pak",
            GameRoot = gameRoot, GroupingRule = "strip_underscore_p_suffix",
            FileExtensions = new[] { "pak", "ucas", "utoc" },
            DataDir = Path.Combine(_tmp, "data2"),
            ModLocations = new[] { new ModLocation("mods", "Paks", "Witchfire/Content/Paks")
                { Form = "paks-root", Mirrors = new[] { "Mirror/Paks" } } },
        };
        Directory.CreateDirectory(game.DataDir!);
        var ctx = Scanner.GameContext(game);
        var loc = ctx.Locations.First(l => l.Name == "mods");
        var hostile = new Mod
        {
            Name = "pakchunk0-WindowsNoEditor", Location = "mods", Enabled = true, IsFolder = false,
            Files = new List<string> { "pakchunk0-WindowsNoEditor.pak" },
        };
        var ex = Assert.Throws<BaseGameFileException>(() => Scanner.GuardNoBasePakMove(hostile, loc));
        Assert.Equal("pakchunk0-WindowsNoEditor.pak is part of the game, so 626 won't turn it off.", ex.Message);
    }
}
