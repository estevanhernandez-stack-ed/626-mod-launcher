using System.Text;
using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>One Mod Engine 2 mod entry: <c>{ enabled, name, path }</c> from the config's mods array.</summary>
public sealed record Me2Mod(bool Enabled, string Name, string Path);

/// <summary>
/// Reads and edits the two arrays in a Mod Engine 2 <c>config_*.toml</c> that decide what loads:
/// <c>[extension.mod_loader] mods = [...]</c> (folder mods, in priority order — earlier wins
/// conflicts) and <c>[modengine] external_dlls = [...]</c> (DLL mods like Seamless Co-op).
///
/// Edits are surgical: only the real, uncommented array is replaced; every comment, section, and
/// byte outside it is left exactly as the user had it. The anchors require the key at line start
/// (optional indent, no leading '#') so the commented EXAMPLE arrays ME2 ships are never touched.
/// </summary>
public static class ModEngine2Config
{
    private static readonly RegexOptions Opt = RegexOptions.Multiline | RegexOptions.Compiled;
    // An array spans from `key = [` to the first `]`. ME2 entries use `{ }`, never `[ ]`, so the
    // first `]` is always the real close. `[ \t]*` (not `\s`) keeps a leading '#' from matching.
    private static readonly Regex ModsRe = new(@"^[ \t]*mods[ \t]*=[ \t]*\[[^\]]*\]", Opt);
    private static readonly Regex DllsRe = new(@"^[ \t]*external_dlls[ \t]*=[ \t]*\[[^\]]*\]", Opt);
    private static readonly Regex EntryRe = new(@"\{([^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex QuotedRe = new(@"""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    public static IReadOnlyList<Me2Mod> ParseMods(string toml)
    {
        var m = ModsRe.Match(toml ?? "");
        if (!m.Success) return Array.Empty<Me2Mod>();
        var list = new List<Me2Mod>();
        foreach (Match e in EntryRe.Matches(m.Value))
        {
            var body = e.Groups[1].Value;
            var name = StrField(body, "name") ?? "";
            var path = StrField(body, "path") ?? "";
            if (name.Length == 0 && path.Length == 0) continue;
            list.Add(new Me2Mod(BoolField(body, "enabled") ?? true, name, path));
        }
        return list;
    }

    public static IReadOnlyList<string> ParseExternalDlls(string toml)
    {
        var m = DllsRe.Match(toml ?? "");
        if (!m.Success) return Array.Empty<string>();
        var list = new List<string>();
        foreach (Match s in QuotedRe.Matches(m.Value)) list.Add(Unescape(s.Groups[1].Value));
        return list;
    }

    /// <summary>Replace the real mods array with these entries (priority order preserved). No-op if absent.</summary>
    public static string WriteMods(string toml, IReadOnlyList<Me2Mod> mods)
    {
        if (toml is null || !ModsRe.IsMatch(toml)) return toml ?? "";
        var sb = new StringBuilder("mods = [");
        for (var i = 0; i < mods.Count; i++)
        {
            var m = mods[i];
            sb.Append("\n    { enabled = ").Append(m.Enabled ? "true" : "false")
              .Append(", name = \"").Append(Escape(m.Name)).Append('"')
              .Append(", path = \"").Append(Escape(m.Path)).Append("\" }");
            if (i < mods.Count - 1) sb.Append(',');
        }
        if (mods.Count > 0) sb.Append('\n');
        sb.Append(']');
        var block = sb.ToString();
        return ModsRe.Replace(toml, _ => block, 1); // MatchEvaluator: no $-substitution surprises
    }

    /// <summary>Replace the real external_dlls array with these paths. No-op if absent.</summary>
    public static string WriteExternalDlls(string toml, IReadOnlyList<string> dlls)
    {
        if (toml is null || !DllsRe.IsMatch(toml)) return toml ?? "";
        var block = dlls.Count == 0
            ? "external_dlls = []"
            : "external_dlls = [ " + string.Join(", ", dlls.Select(d => "\"" + Escape(d) + "\"")) + " ]";
        return DllsRe.Replace(toml, _ => block, 1);
    }

    private static string? StrField(string body, string key)
    {
        var m = Regex.Match(body, key + @"[ \t]*=[ \t]*""((?:[^""\\]|\\.)*)""");
        return m.Success ? Unescape(m.Groups[1].Value) : null;
    }

    private static bool? BoolField(string body, string key)
    {
        var m = Regex.Match(body, key + @"[ \t]*=[ \t]*(true|false)");
        return m.Success ? m.Groups[1].Value == "true" : null;
    }

    // TOML basic-string escaping for the two we care about (backslash, quote).
    private static string Unescape(string s) => s.Replace("\\\\", "\\").Replace("\\\"", "\"");
    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
