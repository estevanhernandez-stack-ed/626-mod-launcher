namespace ModManager.Core;

/// <summary>
/// Pure MP/SP classification helpers. Auto-seed rule: a mod mirrored to the server
/// (onServer) is server-valid -> "both"; a client-only mod is cosmetic/SP-safe -> "sp".
/// Existing choices win. Mirrors classification-core.js.
/// </summary>
public static class Classification
{
    public static Dictionary<string, string> Seed(
        IReadOnlyDictionary<string, string>? existing,
        IEnumerable<(string Name, bool OnServer)> mods)
    {
        var outMap = existing is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(existing);
        foreach (var m in mods)
        {
            if (!outMap.ContainsKey(m.Name))
                outMap[m.Name] = m.OnServer ? "both" : "sp";
        }
        return outMap;
    }

    /// <summary>Whether a mod of class <paramref name="cls"/> is enabled under <paramref name="mode"/> (all|mp|sp).</summary>
    public static bool ModeFilter(string mode, string cls)
    {
        if (mode == "mp") return cls is "mp" or "both";
        if (mode == "sp") return cls is "sp" or "both";
        return true;
    }
}
