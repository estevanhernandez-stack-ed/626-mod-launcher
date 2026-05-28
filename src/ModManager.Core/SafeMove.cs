namespace ModManager.Core;

/// <summary>
/// Move a file or directory with cross-volume safety. Same-volume is a fast rename. Cross-volume
/// (or any other movable IOException) copies, VERIFIES per-file size, then deletes the source — an
/// unverified copy never deletes the original. A sharing violation (file in use / game running) is
/// NOT swallowed: it surfaces so the caller can tell the user to close the game, instead of being
/// retried as a doomed copy. Pure System.IO; runs headless.
/// </summary>
public static class SafeMove
{
    // Windows HRESULT for ERROR_SHARING_VIOLATION (0x20). Intentional — this launcher targets Windows only.
    private const int HrSharingViolation = unchecked((int)0x80070020);

    public static void Move(string src, string dest)
    {
        try
        {
            if (Directory.Exists(src)) Directory.Move(src, dest);
            else File.Move(src, dest);
        }
        catch (IOException ex) when (ex.HResult != HrSharingViolation)
        {
            if (Directory.Exists(src)) { CopyDirVerified(src, dest); Directory.Delete(src, recursive: true); }
            else { CopyFileVerified(src, dest); File.Delete(src); }
        }
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
