using System.Security.Cryptography;

namespace ModManager.Core.RestorePoints;

/// <summary>Small pure helpers for restore-point integrity figures: per-file SHA-256, recursive
/// byte size, recursive file count. Used to size the free-space pre-flight and to seal the manifest
/// with a verifiable total. System.Security.Cryptography + System.IO only — Core-legal.</summary>
public static class FileTally
{
    public static string Sha256(string file)
    {
        using var stream = File.OpenRead(file);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    public static long ByteSize(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        long total = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            total += new FileInfo(f).Length;
        return total;
    }

    public static int FileCount(string dir)
        => Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count() : 0;
}
