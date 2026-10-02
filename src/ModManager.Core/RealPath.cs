using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Where a folder REALLY is: the final path behind junctions, symlinks, <c>subst</c> drives and the
/// <c>\\?\</c> prefix, normalised to a plain path. Every "is this under the game folder" or "is this an
/// ancestor of it" comparison in Safe Clear runs on these, never on the strings the user typed, because a
/// junction, <c>\\?\C:\...</c>, <c>P:\</c> from <c>subst</c> or <c>\\localhost\C$\...</c> all name the same
/// folder as a plain path and a string comparison misses every one of them (review r6, I-1).
///
/// <para>Pure Core in the sense the purity rule means: no WinUI, no WinRT. On Windows it asks the OS for the
/// final path of an open handle (<c>GetFinalPathNameByHandleW</c>); elsewhere, or when the folder can't be
/// opened, it falls back to following link targets segment by segment. A UNC path to a local share stays
/// UNC: it can't be proved equal to a local path, so it is simply never "inside" the game.</para>
/// </summary>
public static class RealPath
{
    private static readonly Regex DriveRelative = new(@"^[A-Za-z]:(?:$|[^\\/])", RegexOptions.CultureInvariant);

    /// <summary>True for a drive-relative path such as <c>C:</c> or <c>C:mods</c>: it means "wherever the current
    /// directory on that drive is", which is never a folder anyone chose. Treated as invalid.</summary>
    public static bool IsDriveRelative(string? path) => !string.IsNullOrEmpty(path) && DriveRelative.IsMatch(path.Trim());

    /// <summary>The final path of <paramref name="path"/>, without a trailing separator, or the normalised full
    /// path when it can't be resolved (it doesn't exist yet). Null when it can't even be made absolute.</summary>
    public static string? Final(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full;
        try { full = Path.GetFullPath(path); }
        catch { return null; }
        if (OperatingSystem.IsWindows() && FinalByHandle(full) is { } byHandle) return Trim(StripPrefix(byHandle));
        return Trim(StripPrefix(FollowLinks(full)));
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="dir"/> or inside it, both resolved.</summary>
    public static bool IsAtOrUnder(string path, string dir)
    {
        var p = Final(path);
        var d = Final(dir);
        if (p is null || d is null) return false;
        return string.Equals(p, d, StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(d.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Trim(string p) => p.Length > 3 ? Path.TrimEndingDirectorySeparator(p) : p;

    private static string StripPrefix(string p)
    {
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + p[8..];
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal)) return p[4..];
        return p;
    }

    // Each existing segment, its link target followed to the end. Portable, and the Windows fallback.
    private static string FollowLinks(string full)
    {
        try
        {
            var root = Path.GetPathRoot(full) ?? "";
            var current = root;
            foreach (var seg in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, seg);
                FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                if (!info.Exists) continue;
                if (info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                    current = Path.GetFullPath(target.FullName);
            }
            return current;
        }
        catch { return full; }
    }

    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint OpenExisting = 3;
    private const uint ShareAll = 0x7;

    private static string? FinalByHandle(string full)
    {
        if (!Directory.Exists(full) && !File.Exists(full)) return null;
        try
        {
            using var h = CreateFileW(full, 0, ShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
            if (h.IsInvalid) return null;
            var buffer = new char[1024];
            var n = GetFinalPathNameByHandleW(h, buffer, (uint)buffer.Length, 0);
            if (n == 0) return null;
            if (n > buffer.Length)
            {
                buffer = new char[n];
                n = GetFinalPathNameByHandleW(h, buffer, (uint)buffer.Length, 0);
                if (n == 0 || n > buffer.Length) return null;
            }
            return new string(buffer, 0, (int)n);
        }
        catch { return null; }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandleW(Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        [Out] char[] buffer, uint length, uint flags);
}
