namespace ModManager.Core;

/// <summary>How a data-dir move will be carried out.</summary>
public enum DataDirMoveKind
{
    /// <summary>Nothing to do — no source, or source and target are the same place.</summary>
    Nothing,
    /// <summary>Same volume, target absent: an atomic directory rename.</summary>
    Rename,
    /// <summary>Different volumes: copy to staging, verify, swap, then delete the source.</summary>
    CopyVerifyDelete,
}

/// <summary>What a move would do. Produced by <see cref="DataDirMove.Plan"/>; writes nothing.</summary>
public sealed record DataDirMovePlan
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required DataDirMoveKind Kind { get; init; }
    public required int FileCount { get; init; }
    public required long TotalBytes { get; init; }

    /// <summary>Why this move must not happen, in the user's words, or null when it may proceed.</summary>
    public string? Refusal { get; init; }

    public bool CanProceed => Refusal is null;
}

/// <summary>
/// Moves a game's launcher data folder, safely.
///
/// <para>The data dir holds the ONLY copy of real user files — <c>disabled\</c>,
/// <c>direct-disabled\</c>, <c>loose-disabled\</c>, <c>frameworks\*\disabled-proxy\</c>,
/// <c>vortex-takeover\</c>, <c>tools\</c>. Its path is a pure function of
/// <c>(Id, GameRoot)</c> (see <see cref="Scanner.DataDirForGame"/>), so correcting a game folder
/// moves it. Getting that wrong does not lose metadata; it loses mods.</para>
///
/// <para>Split per the repo's validate-then-extract law: <see cref="Plan"/> inspects and refuses and
/// cannot write; <see cref="Execute"/> writes and cannot decide. A UI can therefore show a real path
/// and a real size before the user commits to anything.</para>
/// </summary>
public static class DataDirMove
{
    public static DataDirMovePlan Plan(string from, string to)
    {
        var src = Norm(from);
        var dst = Norm(to);

        if (src.Length == 0 || dst.Length == 0 || !Directory.Exists(src))
            return Empty(src, dst);

        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
            return Empty(src, dst);

        var files = Directory.GetFiles(src, "*", SearchOption.AllDirectories);
        var bytes = files.Sum(f => new FileInfo(f).Length);

        // Never merge two data dirs. Interleaving two games' disabled mods leaves no way to tell them
        // apart afterwards — the same stance the legacy MigrateDataDir already takes.
        if (Directory.Exists(dst) && Directory.EnumerateFileSystemEntries(dst).Any())
            return new DataDirMovePlan
            {
                From = src, To = dst, Kind = DataDirMoveKind.Nothing,
                FileCount = files.Length, TotalBytes = bytes,
                Refusal = "There is already launcher data in that location. Move or remove it first — "
                          + "merging two data folders would leave no way to tell the two games' files apart.",
            };

        var sameVolume = string.Equals(
            Path.GetPathRoot(src) ?? "", Path.GetPathRoot(dst) ?? "", StringComparison.OrdinalIgnoreCase);
        var kind = sameVolume && !Directory.Exists(dst) ? DataDirMoveKind.Rename : DataDirMoveKind.CopyVerifyDelete;

        // A rename needs no free space; a copy needs the whole thing on the far side before anything
        // is removed from this one. Checking here means we refuse before writing a single byte.
        if (kind == DataDirMoveKind.CopyVerifyDelete && !HasRoom(dst, bytes))
            return new DataDirMovePlan
            {
                From = src, To = dst, Kind = kind, FileCount = files.Length, TotalBytes = bytes,
                Refusal = $"There is not enough free space to move {Mb(bytes)} to that drive.",
            };

        return new DataDirMovePlan
        {
            From = src, To = dst, Kind = kind, FileCount = files.Length, TotalBytes = bytes,
        };
    }

    private static DataDirMovePlan Empty(string src, string dst) => new()
    {
        From = src, To = dst, Kind = DataDirMoveKind.Nothing, FileCount = 0, TotalBytes = 0,
    };

    private static bool HasRoom(string dst, long bytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dst));
            return string.IsNullOrEmpty(root) || new DriveInfo(root).AvailableFreeSpace >= bytes;
        }
        catch { return true; }   // unknowable free space is not a reason to refuse; the copy will say
    }

    private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:N0} MB";

    internal static string Norm(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        try { return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return p.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
    }
}
