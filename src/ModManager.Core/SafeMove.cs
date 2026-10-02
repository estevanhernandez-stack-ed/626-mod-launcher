namespace ModManager.Core;

/// <summary>
/// Move a file or directory with cross-volume safety. Same-volume is a fast rename. Cross-volume
/// (or any other movable IOException) copies, VERIFIES per-file size, then deletes the source — an
/// unverified copy never deletes the original. If the copy or the delete fails partway, the fallback
/// undoes itself first, so the source is as it was and dest holds nothing of this call's (see
/// <see cref="MoveByCopy"/>). A sharing violation (file in use / game running) is NOT swallowed: it
/// surfaces so the caller can tell the user to close the game, instead of being retried as a doomed
/// copy. Pure System.IO; runs headless.
/// </summary>
public static class SafeMove
{
    // Windows HRESULT for ERROR_SHARING_VIOLATION (0x20). Intentional — this launcher targets Windows only.
    // Internal rather than private so DataDirMove can recognise the same failure and say "close the
    // game" in its own words; two copies of a magic number is how one of them ends up wrong.
    internal const int HrSharingViolation = unchecked((int)0x80070020);

    public static void Move(string src, string dest)
    {
        try
        {
            if (Directory.Exists(src)) Directory.Move(src, dest);
            else File.Move(src, dest);
        }
        catch (IOException ex) when (ex.HResult != HrSharingViolation)
        {
            MoveByCopy(src, dest);
        }
    }

    // Called with ("copy", source file) before each fallback copy and ("restore", copy file) before each
    // copy back, so a test can fail either phase partway. Null in production; the fallback is already
    // paying for a file copy per call, so one null check beside it costs nothing measurable.
    [ThreadStatic] internal static Action<string, string>? FallbackStepForTests;

    /// <summary>
    /// The copy, verify, delete half of <see cref="Move"/>, reached directly by tests because a fixture
    /// on one volume never fails the rename. Every way it can fail ends with the source as it was and
    /// nothing of this call's left at <paramref name="dest"/>, with one exception that is deliberate: if
    /// putting a half-deleted source back fails too, the copy at dest is the only full one, so it stays.
    /// </summary>
    internal static void MoveByCopy(string src, string dest)
    {
        // Everything at dest is about to be treated as this call's own, removable on failure. That is
        // only true if nothing was there first, so refuse rather than copy into someone else's folder.
        if (File.Exists(dest) || Directory.Exists(dest))
            throw new IOException($"Couldn't move \"{src}\": \"{dest}\" already exists. Nothing was copied.");

        var createdParents = MissingAncestors(dest);
        if (Directory.Exists(src)) MoveDirByCopy(src, dest, createdParents);
        else MoveFileByCopy(src, dest, createdParents);
    }

    private static void MoveFileByCopy(string src, string dest, List<string> createdParents)
    {
        try
        {
            FallbackStepForTests?.Invoke("copy", src);
            CopyFileVerified(src, dest);
        }
        catch { RemoveWhatThisCallCreated(dest, createdParents); throw; }

        // One delete is all or nothing, so a failure here leaves the source whole: only the copy goes.
        try { File.Delete(src); }
        catch { RemoveWhatThisCallCreated(dest, createdParents); throw; }
    }

    private static void MoveDirByCopy(string src, string dest, List<string> createdParents)
    {
        // Sorted, so a failure partway is the same failure on every run, and relative so the same list
        // drives the copy, the delete and the undo.
        var dirs = Directory.GetDirectories(src, "*", SearchOption.AllDirectories)
            .Select(d => Path.GetRelativePath(src, d)).Order(StringComparer.Ordinal).ToList();
        var files = Directory.GetFiles(src, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(src, f)).Order(StringComparer.Ordinal).ToList();

        try
        {
            Directory.CreateDirectory(dest);
            foreach (var d in dirs) Directory.CreateDirectory(Path.Combine(dest, d));
            foreach (var f in files)
            {
                FallbackStepForTests?.Invoke("copy", Path.Combine(src, f));
                CopyFileVerified(Path.Combine(src, f), Path.Combine(dest, f));
            }
        }
        catch { RemoveWhatThisCallCreated(dest, createdParents); throw; }

        // File by file rather than Directory.Delete(recursive): only files this call copied and verified
        // are ever deleted, and when one refuses (the game holds it), the undo knows the tree's shape.
        // Folders go deepest first and never recursively, so a file that arrived after the copy keeps
        // its folder, and that failure is undone like any other.
        try
        {
            foreach (var f in files) File.Delete(Path.Combine(src, f));
            for (var i = dirs.Count - 1; i >= 0; i--) Directory.Delete(Path.Combine(src, dirs[i]));
            Directory.Delete(src);
        }
        catch (Exception deleteFailure)
        {
            PutSourceBack(src, dest, createdParents);
            // The original, not a wrapper: a sharing violation must still read as one, so callers keep
            // telling the user to close the game.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(deleteFailure).Throw();
        }
    }

    // The source is half-deleted and dest holds a full verified copy. Copy back only what is missing,
    // confirm the source is complete, and only then drop the copy: until that check passes, dest is the
    // last full copy anywhere.
    private static void PutSourceBack(string src, string dest, List<string> createdParents)
    {
        try
        {
            Directory.CreateDirectory(src);
            foreach (var d in Directory.GetDirectories(dest, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(src, Path.GetRelativePath(dest, d)));

            var copied = Directory.GetFiles(dest, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(dest, f)).Order(StringComparer.Ordinal).ToList();
            foreach (var f in copied)
            {
                var back = Path.Combine(src, f);
                if (File.Exists(back)) continue;
                FallbackStepForTests?.Invoke("restore", Path.Combine(dest, f));
                CopyFileVerified(Path.Combine(dest, f), back);
            }

            var missing = copied.FirstOrDefault(f => !File.Exists(Path.Combine(src, f)));
            if (missing is not null) throw new IOException($"\"{missing}\" is still missing after the copy back.");
        }
        catch (Exception restoreFailure)
        {
            throw new IOException(
                $"Moving \"{src}\" failed partway and it could not be put back, so the original is incomplete. "
                + $"The full copy is kept at \"{dest}\".", restoreFailure);
        }

        RemoveWhatThisCallCreated(dest, createdParents);
    }

    // Folders above dest that do not exist yet, deepest first. CopyFileVerified creates them, so they
    // are this call's to remove on failure, and only these: a parent that was already there stays.
    private static List<string> MissingAncestors(string dest)
    {
        var missing = new List<string>();
        for (var p = Path.GetDirectoryName(Path.GetFullPath(dest)); p is not null && !Directory.Exists(p); p = Path.GetDirectoryName(p))
            missing.Add(p);
        return missing;
    }

    // Best effort: by the time this runs the source is whole, so a leftover here is clutter rather than
    // loss, and the failure the caller needs to hear about is the one already in flight.
    private static void RemoveWhatThisCallCreated(string dest, List<string> createdParents)
    {
        try
        {
            if (Directory.Exists(dest))
            {
                // File.Copy carries a read-only attribute across, and a recursive delete refuses those.
                foreach (var f in Directory.GetFiles(dest, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dest, recursive: true);
            }
            else if (File.Exists(dest))
            {
                File.SetAttributes(dest, FileAttributes.Normal);
                File.Delete(dest);
            }
            foreach (var p in createdParents) Directory.Delete(p);   // non-recursive: empty or it stays
        }
        catch { /* the source is intact; nothing further is safe to do */ }
    }

    public static void CopyFileVerified(string src, string dest)
    {
        var srcLen = new FileInfo(src).Length;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(src, dest, overwrite: false);
        if (new FileInfo(dest).Length != srcLen)
            throw new IOException($"Verification failed copying \"{src}\" -> \"{dest}\" (size mismatch); source left intact.");
    }

    public static void CopyDirVerified(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src))
            CopyFileVerified(f, Path.Combine(dest, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src))
            CopyDirVerified(d, Path.Combine(dest, Path.GetFileName(d)));
    }
}
