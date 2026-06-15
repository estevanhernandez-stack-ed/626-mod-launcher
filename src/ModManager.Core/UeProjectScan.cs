using System.IO;

namespace ModManager.Core;

/// <summary>One Unreal "project" folder (the dir that owns a Content directory) found under a game root.</summary>
public sealed record UeProjectCandidate(
    string RelativeProjectPath,   // "" (root is the project) | "Pal" | "MarvelGame/Marvel"
    int    WrapperDepth,          // 0 = root, 1, or 2
    bool   HasShippingPak,        // a PakClassifier shipping-name pak inside its Content/Paks
    bool   HasBinariesSibling,    // a Binaries folder next to Content (the playable build signal)
    bool   HasUprojectSibling);   // a .uproject next to Content (tie-breaker bonus only)

/// <summary>Bound on the directory walk so it stays fast on huge installs.</summary>
public readonly record struct ScanBudget(int MaxDirs)
{
    public static ScanBudget Default => new(200);
}

public enum UeProjectPickKind { None, One, Ambiguous }

/// <summary>The outcome of picking the right project from the candidates. One = auto-pick; Ambiguous = don't guess.</summary>
public sealed record UeProjectPick(UeProjectPickKind Kind, UeProjectCandidate? Chosen)
{
    public static readonly UeProjectPick None = new(UeProjectPickKind.None, null);
    public static readonly UeProjectPick Ambiguous = new(UeProjectPickKind.Ambiguous, null);
    public static UeProjectPick One(UeProjectCandidate c) => new(UeProjectPickKind.One, c);
}

/// <summary>
/// Bounded discovery of Unreal project folders (the dir owning Content/Paks) under a game root, plus
/// the pure rules for picking the RIGHT one. Single source of truth so the engine-decision gate
/// (EngineScan), the add-wizard seeder (EnginePresets), and the runtime resolver (ModLocator) agree by
/// construction. Walks root + up to 2 wrapper levels, skips engine/anti-cheat/redist siblings, and is
/// hard-bounded by a directory budget. The walk does System.IO (allowed in Core); Pick is pure.
/// </summary>
public static class UeProjectScan
{
    /// <summary>Folder names that are never a game project wrapper — skipped before descending.</summary>
    public static IReadOnlyList<string> Denylist { get; } = new[]
    {
        "Engine", "Binaries", "EasyAntiCheat", "EasyAntiCheat_EOS", "BattlEye",
        "CommonRedist", "_CommonRedist", "Redist", "Redistributable", "Prerequisites",
        "DirectXRedist", "VCRedist", "DotNetRedist",
    };

    private static readonly HashSet<string> DenySet = new(Denylist, StringComparer.OrdinalIgnoreCase);

    private static bool IsDenied(string folderName) => DenySet.Contains(folderName);

    /// <summary>Pure decision over candidates. One when exactly one candidate, or one project-looking
    /// candidate strictly out-scores the rest; Ambiguous when two-or-more tie (don't guess); None when empty.</summary>
    public static UeProjectPick Pick(IReadOnlyList<UeProjectCandidate> candidates)
    {
        if (candidates is null || candidates.Count == 0) return UeProjectPick.None;
        if (candidates.Count == 1) return UeProjectPick.One(candidates[0]);

        var looking = candidates.Where(IsProjectLooking).ToList();
        if (looking.Count == 0) return UeProjectPick.Ambiguous; // multiple, none looks real — don't guess
        if (looking.Count == 1) return UeProjectPick.One(looking[0]);

        var ranked = looking.Select(c => (c, score: Score(c)))
                            .OrderByDescending(t => t.score).ToList();
        return ranked[0].score > ranked[1].score
            ? UeProjectPick.One(ranked[0].c)
            : UeProjectPick.Ambiguous;
    }

    private static bool IsProjectLooking(UeProjectCandidate c) => c.HasShippingPak || c.HasBinariesSibling;

    private static int Score(UeProjectCandidate c)
    {
        var s = 0;
        if (!LastSegment(c.RelativeProjectPath).EndsWith("Server", StringComparison.OrdinalIgnoreCase)) s += 1000;
        s += (2 - Math.Clamp(c.WrapperDepth, 0, 2)) * 100; // shallower wins
        if (c.HasShippingPak) s += 40;
        if (c.HasBinariesSibling) s += 20;
        if (c.HasUprojectSibling) s += 5;
        return s;
    }

    private static string LastSegment(string rel)
    {
        if (string.IsNullOrEmpty(rel)) return "";
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length > 0 ? parts[^1] : rel;
    }
}
