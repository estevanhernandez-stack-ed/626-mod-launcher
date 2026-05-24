using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Pure resolver for Ludusavi save-path templates. The manifest expresses save locations with
/// tokens like <c>&lt;winLocalAppData&gt;</c>, <c>&lt;base&gt;</c> (install dir), <c>&lt;home&gt;</c>;
/// the App supplies the real folder for each token and this fills the template. Returns null if a
/// token can't be resolved (e.g. &lt;storeUserId&gt;), so callers skip it rather than emit a broken path.
/// </summary>
public static partial class LudusaviPaths
{
    [GeneratedRegex(@"<[a-zA-Z]+>")]
    private static partial Regex TokenRe();

    public static string? Resolve(string template, IReadOnlyDictionary<string, string> tokens)
    {
        var s = template;
        foreach (var (key, value) in tokens) s = s.Replace("<" + key + ">", value);
        if (TokenRe().IsMatch(s)) return null; // an unresolved token remains
        return s.Replace('/', System.IO.Path.DirectorySeparatorChar);
    }
}
