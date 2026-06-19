// src/ModManager.Core/Plugins/PluginIntegrity.cs
using System.Security.Cryptography;

namespace ModManager.Core.Plugins;

/// <summary>Content integrity for downloaded plugin bytes — the sha256 the signed index pins. A
/// mismatch means the download was corrupted or swapped; the installer refuses it.</summary>
public static class PluginIntegrity
{
    public static string Sha256Hex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static bool Sha256Matches(ReadOnlySpan<byte> bytes, string expectedHex)
        => !string.IsNullOrWhiteSpace(expectedHex)
           && string.Equals(Sha256Hex(bytes), expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
}
