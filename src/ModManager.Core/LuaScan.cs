using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>A keybind a UE4SS Lua mod registers statically (Key.NAME + optional ModifierKey.X list).
/// SourceFile is set by ScanFolder (the .lua that contained the call); null when parsed from raw text.</summary>
public sealed record LuaKeyBind(string Key, IReadOnlyList<string> Modifiers, string? SourceFile = null);
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
            binds.AddRange(Keybinds(t).Select(b => b with { SourceFile = f }));
            cmds.AddRange(Commands(t));
        }
        return (binds, cmds);
    }

    /// <summary>Rewrite the key of the first RegisterKeyBind matching (fromKey, fromMods). Only the
    /// Key.X token is changed; everything else (callback, modifiers, surrounding code) is untouched.
    /// Returns the input unchanged if no confident match. Caller backs up the file before writing.</summary>
    public static string RemapKeyBind(string lua, string fromKey, IReadOnlyList<string> fromMods, string toKey)
    {
        var want = fromMods.Select(m => m.ToUpperInvariant()).OrderBy(x => x).ToList();
        foreach (Match m in KeyBindRe.Matches(lua))
        {
            if (!string.Equals(m.Groups[1].Value, fromKey, StringComparison.OrdinalIgnoreCase)) continue;
            var mods = new List<string>();
            if (m.Groups[2].Success)
                foreach (Match mm in ModRe.Matches(m.Groups[2].Value)) mods.Add(mm.Groups[1].Value.ToUpperInvariant());
            if (!want.SequenceEqual(mods.OrderBy(x => x))) continue;
            // Replace the "Key.{from}" inside this match only.
            var keyTokenStart = m.Index + m.Value.IndexOf("Key." + m.Groups[1].Value, StringComparison.Ordinal);
            var keyToken = "Key." + m.Groups[1].Value;
            return lua[..keyTokenStart] + "Key." + toKey + lua[(keyTokenStart + keyToken.Length)..];
        }
        return lua;
    }
}
