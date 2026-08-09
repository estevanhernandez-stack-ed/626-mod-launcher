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

    /// <summary>
    /// Carry out a plan. The ONLY method here that writes.
    ///
    /// <para>THE ORDERING IS THE SAFETY. The source is never removed until the target is verified in
    /// place, so a failure at any point leaves the user exactly where they started. A failure to
    /// remove the source at the very end is deliberately non-fatal: a harmless duplicate is a far
    /// better outcome than risking the surviving copy in order to tidy up.</para>
    /// </summary>
    public static DataDirMoveResult Execute(DataDirMovePlan plan)
    {
        if (!plan.CanProceed)
            return new DataDirMoveResult { Moved = false, SourceRemoved = false, Error = plan.Refusal };

        if (plan.Kind == DataDirMoveKind.Nothing)
            return new DataDirMoveResult { Moved = true, SourceRemoved = false, Error = null };

        try
        {
            if (plan.Kind == DataDirMoveKind.Rename)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(plan.To)!);
                Directory.Move(plan.From, plan.To);
                return new DataDirMoveResult { Moved = true, SourceRemoved = true, Error = null };
            }

            // Stage beside the target so the swap into place is a rename, not a second long copy.
            var staging = plan.To + ".moving-" + Environment.ProcessId;
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                CopyTree(plan.From, staging);
                if (!Verify(plan.From, staging, out var mismatch))
                    throw new IOException("The copy did not match the original: " + mismatch);

                Directory.CreateDirectory(Path.GetDirectoryName(plan.To)!);
                Directory.Move(staging, plan.To);
            }
            catch
            {
                // Roll back to untouched. The source has not been read destructively, so removing the
                // staging tree puts the user exactly back where they started.
                try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
                catch { /* nothing further we can safely do */ }
                throw;
            }

            // Tidy-up only. The data is already safe at the target, so a failure here must not be
            // reported as a failed move — that would invite a caller to "retry" onto a populated target.
            var sourceRemoved = true;
            try { Directory.Delete(plan.From, recursive: true); }
            catch { sourceRemoved = false; }

            return new DataDirMoveResult { Moved = true, SourceRemoved = sourceRemoved, Error = null };
        }
        catch (Exception e)
        {
            return new DataDirMoveResult { Moved = false, SourceRemoved = false, Error = e.Message };
        }
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: false);
    }

    /// <summary>
    /// Same set of relative paths, same byte length for each.
    ///
    /// <para>That catches the failures that actually happen — a truncated copy, a file that did not
    /// make it, a disk that filled. It deliberately does NOT hash contents: hashing gigabytes of
    /// disabled mods would add minutes to every move to catch a class of silent corruption the
    /// rename path does not have at all. Stated here rather than implied away, so no caller reads
    /// "verify" as a guarantee this does not provide.</para>
    /// </summary>
    private static bool Verify(string from, string to, out string mismatch)
    {
        var a = Directory.GetFiles(from, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(from, f), f => new FileInfo(f).Length, StringComparer.OrdinalIgnoreCase);
        var b = Directory.GetFiles(to, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(to, f), f => new FileInfo(f).Length, StringComparer.OrdinalIgnoreCase);

        foreach (var (rel, len) in a)
        {
            if (!b.TryGetValue(rel, out var copied)) { mismatch = rel + " is missing."; return false; }
            if (copied != len) { mismatch = rel + " is a different size."; return false; }
        }
        if (b.Count != a.Count) { mismatch = "the copy has extra files."; return false; }

        mismatch = "";
        return true;
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

/// <summary>The outcome of a <see cref="DataDirMove.Execute"/> call.</summary>
public sealed record DataDirMoveResult
{
    /// <summary>True when the data is at the target (or there was nothing to move).</summary>
    public required bool Moved { get; init; }

    /// <summary>False when the move succeeded but the old copy could not be deleted — a duplicate on
    /// disk, never a lost file.</summary>
    public required bool SourceRemoved { get; init; }

    /// <summary>Why the move did not happen, in the user's words, or null on success.</summary>
    public string? Error { get; init; }
}
