using ModManager.Core;

namespace ModManager.Tests;

// Best-effort, read-only extraction of UE4SS Lua registrations. Static Key.X forms are captured;
// dynamic forms (RegisterKeyBindAsync(Keybinds[x].Key, ...)) are intentionally skipped.
public class LuaScanTests
{
    [Fact]
    public void Keybinds_extracts_simple_key()
    {
        var b = LuaScan.Keybinds("RegisterKeyBind(Key.F3, GetObjectName)");
        Assert.Single(b);
        Assert.Equal("F3", b[0].Key);
        Assert.Empty(b[0].Modifiers);
    }

    [Fact]
    public void Keybinds_extracts_modifiers()
    {
        var b = LuaScan.Keybinds("RegisterKeyBind(Key.Y, {ModifierKey.CONTROL}, CreatePlayer)");
        Assert.Equal("Y", b[0].Key);
        Assert.Equal(new[] { "CONTROL" }, b[0].Modifiers.ToArray());
    }

    [Fact]
    public void Keybinds_skips_dynamic_registration()
    {
        var b = LuaScan.Keybinds("RegisterKeyBindAsync(Keybinds[name].Key, Keybinds[name].ModifierKeys, cb)");
        Assert.Empty(b);
    }

    [Fact]
    public void Commands_extracts_quoted_names()
    {
        var c = LuaScan.Commands("RegisterConsoleCommandHandler(\"summon\", function() end)\nRegisterConsoleCommandHandler(\"set\", f)");
        Assert.Equal(new[] { "summon", "set" }, c.Select(x => x.Name).ToArray());
    }

    [Fact]
    public void ScanFolder_aggregates_across_lua_files()
    {
        var d = TestSupport.TempDir("lua-");
        Directory.CreateDirectory(Path.Combine(d, "Scripts"));
        File.WriteAllText(Path.Combine(d, "Scripts", "main.lua"), "RegisterKeyBind(Key.INS, f)\nRegisterConsoleCommandHandler(\"dump\", g)");
        var (binds, cmds) = LuaScan.ScanFolder(d);
        Assert.Contains(binds, x => x.Key == "INS");
        Assert.Contains(cmds, x => x.Name == "dump");
    }
}
