namespace ModManager.Core;

/// <summary>
/// Pure load-order model: an ordered list of mod keys. Reconcile merges a saved order with the
/// live mod list (saved order wins for present mods, new mods append, missing drop); Move shifts
/// an entry. Engine-specific application (Bethesda plugins.txt, Unreal pak prefixes) is separate.
/// </summary>
public static class LoadOrder
{
    public static List<string> Reconcile(IEnumerable<string>? saved, IEnumerable<string> currentKeys)
    {
        var current = currentKeys.ToList();
        var currentSet = new HashSet<string>(current);
        var kept = (saved ?? Enumerable.Empty<string>()).Where(currentSet.Contains).ToList();
        var keptSet = new HashSet<string>(kept);
        var appended = current.Where(k => !keptSet.Contains(k));
        return kept.Concat(appended).ToList();
    }

    public static List<string> Move(IReadOnlyList<string> order, string key, int delta)
    {
        var list = order.ToList();
        var i = list.IndexOf(key);
        if (i < 0) return list;
        var j = Math.Clamp(i + delta, 0, list.Count - 1);
        if (i == j) return list;
        list.RemoveAt(i);
        list.Insert(j, key);
        return list;
    }
}
