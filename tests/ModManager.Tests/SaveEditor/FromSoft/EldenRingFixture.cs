using System.Text;
using ModManager.Core.SaveEditor.FromSoft;

namespace ModManager.Tests.SaveEditor.FromSoft;

/// <summary>
/// Builds a synthetic ER <c>.sl2</c> byte array entirely in memory. The output is structurally
/// parseable by a BenGrn-style "use the SLOT_START_INDEX + stride formula" reader (Task 4 will
/// implement that). It is NOT a byte-identical real save — the BND4 file-table entries are
/// minimal (enough for slot location, not full BND4 v4 compliance). If Task 4 needs richer
/// envelope details to round-trip a real save, this fixture is the place to extend them.
///
/// NO real save files are committed to the repo. Test characters are synthesized with given
/// runes + stats; all other slot bytes are zero.
/// </summary>
public static class EldenRingFixture
{
    public const int SlotCount = 10;
    public const int SlotDataSize = SlotData.SlotSize;        // 0x280000
    public const int SlotStride = SlotDataSize + 0x10;         // 0x280010 (16-byte MD5 + data)
    public const int BndHeaderSize = 0x300;                    // BND4 header + file table

    public const int SlotsRegionStart = BndHeaderSize;         // 0x300
    public const int FirstSlotMd5Offset = SlotsRegionStart;    // 0x300
    public const int FirstSlotDataOffset = SlotsRegionStart + 0x10; // 0x310

    /// <summary>Build a minimal .sl2 with all 10 slots blank EXCEPT slot 0, which carries the
    /// given runes + stats. MD5s computed via SlotChecksum. The BND4 envelope is the bare
    /// minimum a BenGrn-style reader (Task 4) needs to find the slots by stride formula. Full
    /// BND4 v4 file-table compliance is deferred; see class-level comment.</summary>
    public static byte[] BuildSaveWithOneCharacter(uint runes,
        byte vig, byte mnd, byte end_, byte str, byte dex, byte int_, byte fai, byte arc)
    {
        var totalSize = BndHeaderSize + SlotCount * SlotStride;
        var buffer = new byte[totalSize];

        WriteBnd4Header(buffer);

        // Populate slot 0's data (runes + stats) at the fixture anchor.
        var slot0Data = buffer.AsSpan(FirstSlotDataOffset, SlotDataSize);
        SlotData.WriteRunes(slot0Data, runes);
        SlotData.WriteStats(slot0Data, vig, mnd, end_, str, dex, int_, fai, arc);

        // Compute and write slot 0's MD5.
        var slot0Md5Region = buffer.AsSpan(FirstSlotMd5Offset, 0x10);
        SlotChecksum.ComputeMd5(slot0Data).CopyTo(slot0Md5Region);

        // Slots 1-9 stay zero-filled; their MD5s match MD5(0x280000 zero bytes). Compute once
        // and copy into each slot's MD5 region.
        var zeroSlotMd5 = SlotChecksum.ComputeMd5(new byte[SlotDataSize]);
        for (int i = 1; i < SlotCount; i++)
        {
            var md5Region = buffer.AsSpan(FirstSlotMd5Offset + i * SlotStride, 0x10);
            zeroSlotMd5.CopyTo(md5Region);
        }

        return buffer;
    }

    /// <summary>The offset of slot <paramref name="slotIndex"/>'s payload (post-MD5) in the
    /// returned buffer. Mirrors BenGrn's <c>SLOT_START_INDEX + slotIndex * 0x10 + slotIndex *
    /// SLOT_LENGTH</c>.</summary>
    public static int GetSlotDataOffset(int slotIndex)
        => FirstSlotDataOffset + slotIndex * SlotStride;

    /// <summary>The offset of slot <paramref name="slotIndex"/>'s 16-byte MD5 region.</summary>
    public static int GetSlotMd5Offset(int slotIndex)
        => FirstSlotMd5Offset + slotIndex * SlotStride;

    private static void WriteBnd4Header(Span<byte> buffer)
    {
        // BND4 magic at 0x00.
        Encoding.ASCII.GetBytes("BND4").CopyTo(buffer);

        // unk04 / unk05 / big_endian / bit_big_endian flags at 0x04..0x0A — all zero (PC is LE).

        // file_count at 0x0C.
        BitConverter.TryWriteBytes(buffer.Slice(0x0C, 4), SlotCount);

        // file_header_offset at 0x10 (int64, always 0x40 for BND4 v4).
        BitConverter.TryWriteBytes(buffer.Slice(0x10, 8), 0x40L);

        // version_string at 0x18 — 8 bytes ASCII. ER ships "04E10231" in real saves; using
        // it here for verisimilitude, but Task 4 readers should not depend on the exact bytes.
        Encoding.ASCII.GetBytes("04E10231").CopyTo(buffer.Slice(0x18, 8));

        // file_header_size at 0x20 (int64). Each entry is 0x20 bytes in the long-offsets +
        // IDs + names format; size = SlotCount * 0x20.
        BitConverter.TryWriteBytes(buffer.Slice(0x20, 8), (long)(SlotCount * 0x20));

        // unicode flag at 0x30 — ER saves are unicode-named.
        buffer[0x30] = 1;
        // format byte at 0x31 — flag bits indicating long offsets + IDs + names (exact bits
        // unverified for this fixture, but a stride-based reader doesn't use this byte).
        // extended byte at 0x32 — leave 0.

        // File table starts at 0x40. 10 entries, 0x20 bytes each. For each slot i, write a
        // minimal entry pointing at the slot's MD5 region (treating MD5+data as one file
        // blob of size 0x280010).
        // NOTE: real BND4 entries carry per-flag-bit conditional fields. This minimal layout
        // is enough to let a stride-based reader find slots; it is NOT a fully-spec-compliant
        // BND4 entry. Task 4 either extends this or switches readers to the stride formula.
        for (int i = 0; i < SlotCount; i++)
        {
            int entryStart = 0x40 + i * 0x20;
            // file_flags at +0x00 (typically 0x40 for stored, not compressed).
            buffer[entryStart] = 0x40;
            // padding 3 bytes
            // reserved at +0x04 (int32, always -1).
            BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x04, 4), -1);
            // compressed_size at +0x08 (int64).
            BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x08, 8), (long)SlotStride);
            // uncompressed_size at +0x10 (int64).
            BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x10, 8), (long)SlotStride);
            // data_offset at +0x18 (int64).
            BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x18, 8), (long)(FirstSlotMd5Offset + i * SlotStride));
        }
        // Everything past 0x40 + 10*0x20 = 0x180 to 0x300 stays zero (padding / hash region).
    }
}
