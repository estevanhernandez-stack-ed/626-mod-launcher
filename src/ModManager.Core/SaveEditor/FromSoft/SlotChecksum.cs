using System.Security.Cryptography;

namespace ModManager.Core.SaveEditor.FromSoft;

/// <summary>
/// MD5 checksum primitives for FromSoft save slots. The Elden Ring .sl2 file layout puts a
/// 16-byte MD5 of each slot's data at the 16 bytes IMMEDIATELY PRECEDING the slot data on disk
/// (slot_offset - 0x10). This class doesn't know about file offsets — it just computes and
/// verifies. The caller (<see cref="EldenRingSave"/>) handles byte placement in the file.
///
/// Older FromSoft games (DS3/Sekiro/AC6) add AES-128-CBC encryption on top of this checksum
/// layer; ER does not. AES support is deferred to the DS3 family expansion. See
/// <c>docs/superpowers/research/2026-05-26-fromsoft-save-libs.md</c> for the format reference.
/// </summary>
public static class SlotChecksum
{
    public const int Md5Size = 16;

    /// <summary>Compute the MD5 of a slot's payload bytes. Returns 16 bytes.</summary>
    public static byte[] ComputeMd5(ReadOnlySpan<byte> payload)
        => MD5.HashData(payload);

    /// <summary>True when expectedMd5 (16 bytes) matches MD5(payload). False on any length
    /// mismatch or byte mismatch — never throws.</summary>
    public static bool VerifyMd5(ReadOnlySpan<byte> expectedMd5, ReadOnlySpan<byte> payload)
    {
        if (expectedMd5.Length != Md5Size) return false;
        Span<byte> actual = stackalloc byte[Md5Size];
        if (!MD5.TryHashData(payload, actual, out var written) || written != Md5Size) return false;
        return expectedMd5.SequenceEqual(actual);
    }
}
