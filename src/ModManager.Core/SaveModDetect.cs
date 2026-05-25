using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>The verdict for a candidate save/world mod: whether it is one, and the world GUID we saw.</summary>
public sealed record SaveModVerdict(bool IsSaveMod, string? WorldGuid);

/// <summary>
/// Recognizes the save/world-mod CLASS from a zip's entry names. A save mod installs into the
/// game's SAVE TREE (e.g. Windrose: RocksDB\&lt;version&gt;\Worlds\&lt;GUID&gt;), not Content\Paks —
/// so the defining tell is the ABSENCE of pak/ucas/utoc content plus one of: a Worlds/&lt;GUID&gt;
/// path, a top-level GUID-named folder, or contents that match a declared save-type extension.
/// A pak anywhere VETOES (that's a content mod). Pure — no filesystem, no Electron.
/// </summary>
public static partial class SaveModDetect
{
    // pak/content tells — any of these means it's NOT a save mod (content mod veto).
    private static readonly string[] PakExtensions = { ".pak", ".ucas", ".utoc" };

    // A bare GUID folder: 32 hex chars, or the dashed 8-4-4-4-12 form.
    [GeneratedRegex(@"^[0-9a-fA-F]{32}$")]
    private static partial Regex Guid32Re();

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex GuidDashedRe();

    private static bool IsGuid(string segment) => Guid32Re().IsMatch(segment) || GuidDashedRe().IsMatch(segment);

    public static SaveModVerdict Detect(IEnumerable<string> zipEntryNames, IReadOnlyList<string> saveTypeExtensions)
    {
        var names = (zipEntryNames ?? Enumerable.Empty<string>())
            .Select(n => (n ?? "").Replace('\\', '/').TrimStart('/'))
            .Where(n => n.Length > 0)
            .ToList();

        // Veto: any pak/content file means this is a content mod, not a save mod.
        foreach (var n in names)
        {
            var ext = System.IO.Path.GetExtension(n);
            if (PakExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                return new SaveModVerdict(false, null);
        }

        // (a) a Worlds/<GUID>/... path, or (b) a top-level GUID-named folder.
        var guid = FindWorldGuid(names);
        if (guid is not null) return new SaveModVerdict(true, guid);

        // (c) contents match a declared save-type extension.
        var exts = new HashSet<string>(saveTypeExtensions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        if (exts.Count > 0 && names.Any(n => exts.Contains(System.IO.Path.GetExtension(n))))
            return new SaveModVerdict(true, null);

        return new SaveModVerdict(false, null);
    }

    // The GUID folder name from a Worlds/<GUID>/ path or a top-level <GUID>/ folder, else null.
    private static string? FindWorldGuid(IEnumerable<string> names)
    {
        foreach (var n in names)
        {
            var segs = n.Split('/');
            // Worlds/<GUID>/...
            for (var i = 0; i < segs.Length - 1; i++)
            {
                if (string.Equals(segs[i], "Worlds", StringComparison.OrdinalIgnoreCase) && IsGuid(segs[i + 1]))
                    return segs[i + 1];
            }
            // top-level <GUID>/... (a folder, so there must be a path segment after it)
            if (segs.Length > 1 && IsGuid(segs[0])) return segs[0];
        }
        return null;
    }
}
