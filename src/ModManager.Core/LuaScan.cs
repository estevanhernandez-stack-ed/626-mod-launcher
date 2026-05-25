using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>A keybind a UE4SS Lua mod registers statically (Key.NAME + optional ModifierKey.X list).</summary>
public sealed record LuaKeyBind(string Key, IReadOnlyList<string> Modifiers);
/// <summary>A console command a UE4SS Lua mod registers.</summary>
public sealed record LuaConsoleCommand(string Name);

/// <summary>
/// Best-effort, READ-ONLY extraction of UE4SS Lua registrations for display. Static `Key.NAME`
/// keybinds and quoted console-command names are captured; dynamic/computed forms are skipped
/// (we never guess). Pure regex over file text — no Lua execution.
/// </summary>
public static class LuaScan
{
    private static readonly Regex KeyBindRe =
        new(@"RegisterKeyBind\s*\(\s*Key\.(\w+)\s*(?:,\s*\{([^}]*)\})?", RegexOptions.Compiled);
    private static readonly Regex CmdRe =
        new("RegisterConsoleCommandHandler\\s*\\(\\s*\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex ModRe = new(@"ModifierKey\.(\w+)", RegexOptions.Compiled);

    public static IReadOnlyList<LuaKeyBind> Keybinds(string lua)
    {
        var result = new List<LuaKeyBind>();
        foreach (Match m in KeyBindRe.Matches(lua))
        {
            var mods = new List<string>();
            if (m.Groups[2].Success)
                foreach (Match mm in ModRe.Matches(m.Groups[2].Value)) mods.Add(mm.Groups[1].Value);
            result.Add(new LuaKeyBind(m.Groups[1].Value, mods));
        }
        return result;
    }

    public static IReadOnlyList<LuaConsoleCommand> Commands(string lua)
        => CmdRe.Matches(lua).Select(m => new LuaConsoleCommand(m.Groups[1].Value)).ToList();

    public static (IReadOnlyList<LuaKeyBind> Keybinds, IReadOnlyList<LuaConsoleCommand> Commands) ScanFolder(string modDir)
    {
        var binds = new List<LuaKeyBind>();
        var cmds = new List<LuaConsoleCommand>();
        IEnumerable<string> files;
        try { files = Directory.GetFiles(modDir, "*.lua", SearchOption.AllDirectories); }
        catch { return (binds, cmds); }
        foreach (var f in files)
        {
            string t;
            try { t = File.ReadAllText(f); } catch { continue; }
            binds.AddRange(Keybinds(t));
            cmds.AddRange(Commands(t));
        }
        return (binds, cmds);
    }
}
