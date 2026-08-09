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
        if (kind == DataDirMoveKind.CopyVerifyDelete)
        {
            var refusal = SpaceRefusal(bytes, SpaceCheck.Require(dst, bytes));
            if (refusal is not null)
                return new DataDirMovePlan
                {
                    From = src, To = dst, Kind = kind, FileCount = files.Length, TotalBytes = bytes,
                    Refusal = refusal,
                };
        }

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
                SafeMove.CopyDirVerified(plan.From, staging);
                if (!Verify(plan.From, staging, out var mismatch))
                    throw new IOException("The copy did not match the original: " + mismatch);

                Directory.CreateDirectory(Path.GetDirectoryName(plan.To)!);
                // Plan guarantees the target is EMPTY if it exists at all (a non-empty one is refused
                // outright, never merged). Removing the empty shell keeps the swap a rename instead of
                // forcing a second copy — and without this, every move onto an existing empty folder
                // fails on Windows even though Plan said it could proceed.
                if (Directory.Exists(plan.To) && !Directory.EnumerateFileSystemEntries(plan.To).Any())
                    Directory.Delete(plan.To);
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
        // Moving a game's data dir is precisely when that game might be running, so a file held open
        // is the likeliest failure this path will ever see — and the raw Win32 sentence ("the process
        // cannot access the file '<long path>'") tells the user nothing they can act on. The rollback
        // above has already run by the time this is reached; only the words change.
        catch (IOException e) when (e.HResult == SafeMove.HrSharingViolation)
        {
            return new DataDirMoveResult
            {
                Moved = false, SourceRemoved = false,
                Error = "One of these files is in use, so nothing was moved. Close the game and any "
                        + "tool that has its folder open, then try again.",
            };
        }
        catch (Exception e)
        {
            return new DataDirMoveResult { Moved = false, SourceRemoved = false, Error = e.Message };
        }
    }

    /// <summary>
    /// Same set of relative paths, same byte length for each — a second pass over the SOURCE, taken
    /// after the copy has finished.
    ///
    /// <para>WHY THIS IS NOT REDUNDANT with <see cref="SafeMove.CopyDirVerified"/>, which already
    /// checks every file's size as it copies it: that check can only cover files the copy actually
    /// saw. <c>CopyDirVerified</c> enumerates each directory just before copying it, so a file that
    /// lands in — or grows in — an already-copied folder while the copy is still running is never
    /// enumerated and therefore never verified. It is also never copied. Without this pass that file
    /// goes to the target missing (or short) and then the source is deleted, which is a permanently
    /// lost user file: the data dir holds the ONLY copy. Re-reading the source at the end catches it
    /// while rolling back is still free.</para>
    ///
    /// <para>It deliberately does NOT hash contents: hashing gigabytes of disabled mods would add
    /// minutes to every move to catch a class of silent corruption the rename path does not have at
    /// all. Stated here rather than implied away, so no caller reads "verify" as a guarantee this
    /// does not provide.</para>
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

    /// <summary>
    /// The space decision and its words in one place, so a test can hold both to account without
    /// needing a full disk. Null means proceed.
    ///
    /// <para><see cref="SpaceCheck"/> asks for headroom (the payload plus the larger of 10% or 1 GB),
    /// not a byte-for-byte fit — a move that exactly fills the volume "succeeds" and leaves the user
    /// with a game and no room to launch it.</para>
    ///
    /// <para>Unknowable free space is NOT a refusal. <see cref="SpaceCheck.Require"/> reports a share
    /// or a volume DriveInfo cannot read as not-Ok with <c>AvailableBytes = -1</c>; refusing on that
    /// would block every legitimate move to a NAS. Let the copy report a real failure instead.</para>
    /// </summary>
    internal static string? SpaceRefusal(long payloadBytes, SpaceCheck.Result space)
    {
        if (space.Ok || space.AvailableBytes < 0) return null;
        return $"Moving {Mb(payloadBytes)} to {space.VolumeRoot} needs {Mb(space.RequiredBytes)} free, "
               + $"and there is {Mb(space.AvailableBytes)}. Free up some space and try again.";
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

    /// <summary>Meaningful only when a move actually happened. False on a real move means the data is
    /// safely at the target but the old copy could not be deleted — a duplicate on disk, never a lost
    /// file. It is also false for a no-op (<see cref="DataDirMoveKind.Nothing"/>), where there is no
    /// source and nothing was duplicated, so do not warn about a leftover copy on that alone.</summary>
    public required bool SourceRemoved { get; init; }

    /// <summary>Why the move did not happen, in the user's words, or null on success.</summary>
    public string? Error { get; init; }
}
