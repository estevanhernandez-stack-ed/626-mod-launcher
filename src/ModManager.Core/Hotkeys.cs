namespace ModManager.Core;

/// <summary>Safe-hotkey helpers: a canonical signature for a key combo and detection of combos
/// bound more than once (so the UI can warn before two binds silently shadow each other). Pure.</summary>
public static class Hotkeys
{
    /// <summary>Canonical "KEY+MOD+MOD" signature — key upper-cased, modifiers upper-cased + sorted.</summary>
    public static string Signature(LuaKeyBind b)
    {
        var mods = b.Modifiers.Select(m => m.ToUpperInvariant()).OrderBy(x => x, StringComparer.Ordinal);
        return b.Key.ToUpperInvariant() + (b.Modifiers.Count > 0 ? "+" + string.Join("+", mods) : "");
    }

    /// <summary>Signatures that appear on more than one bind — i.e. real conflicts.</summary>
    public static IReadOnlySet<string> Conflicts(IEnumerable<LuaKeyBind> binds)
    {
        var counts = new Dictionary<string, int>();
        foreach (var b in binds) { var s = Signature(b); counts[s] = counts.GetValueOrDefault(s) + 1; }
        return counts.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToHashSet();
    }
}
