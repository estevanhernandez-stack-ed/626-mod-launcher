using System.Security.Cryptography;

namespace ModManager.Core;

/// <summary>
/// MD5 file-identity hashing for Nexus md5_search. Lowercase hex, no separators —
/// the exact shape the Nexus v1 md5 lookup endpoint expects. Pure (no IO beyond OfFile).
/// </summary>
public static class Md5Hash
{
    /// <summary>MD5 of the bytes as lowercase hex, no separators.</summary>
    public static string OfBytes(byte[] data)
    {
        var hash = MD5.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Reads the file and returns <see cref="OfBytes"/> of its contents.</summary>
    public static string OfFile(string path) => OfBytes(File.ReadAllBytes(path));
}
