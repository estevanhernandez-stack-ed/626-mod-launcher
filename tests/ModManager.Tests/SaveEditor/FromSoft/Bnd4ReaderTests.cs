using System.Text;
using ModManager.Core.SaveEditor.FromSoft;

namespace ModManager.Tests.SaveEditor.FromSoft;

/// <summary>
/// Tests for the pure BND4 file-table reader. The reader walks a BND4 container, decodes
/// each 40-byte file entry, and reads the UTF-16LE name from the name table. These tests
/// build tiny synthetic BND4 buffers in-memory; they do NOT depend on EldenRingFixture
/// (that fixture currently writes a no-names variant that Task 2 will extend).
/// </summary>
public class Bnd4ReaderTests
{
    [Fact]
    public void Parse_returns_entries_in_file_table_order_with_names_and_offsets()
    {
        var bytes = BuildTinyBnd4(
            new TinyEntry("A",   DataOffset: 0x1000, DataSize: 0x100),
            new TinyEntry("BB",  DataOffset: 0x2000, DataSize: 0x200),
            new TinyEntry("CCC", DataOffset: 0x3000, DataSize: 0x300));

        var entries = Bnd4Reader.Parse(bytes);

        Assert.Equal(3, entries.Count);

        Assert.Equal("A", entries[0].Name);
        Assert.Equal(0x1000L, entries[0].DataOffset);
        Assert.Equal(0x100L, entries[0].DataSize);

        Assert.Equal("BB", entries[1].Name);
        Assert.Equal(0x2000L, entries[1].DataOffset);
        Assert.Equal(0x200L, entries[1].DataSize);

        Assert.Equal("CCC", entries[2].Name);
        Assert.Equal(0x3000L, entries[2].DataOffset);
        Assert.Equal(0x300L, entries[2].DataSize);
    }

    [Fact]
    public void Parse_throws_InvalidDataException_when_magic_is_not_BND4()
    {
        var bytes = BuildTinyBnd4(new TinyEntry("A", 0x1000, 0x100));
        // Corrupt the magic.
        bytes[0] = (byte)'X';
        bytes[1] = (byte)'X';
        bytes[2] = (byte)'X';
        bytes[3] = (byte)'X';

        var ex = Assert.Throws<InvalidDataException>(() => Bnd4Reader.Parse(bytes));
        Assert.Contains("BND4", ex.Message);
    }

    [Fact]
    public void Parse_throws_InvalidDataException_when_buffer_is_smaller_than_header()
    {
        // Header read needs at least 0x18 bytes (magic + file_count + file_header_offset).
        var bytes = new byte[8];
        Encoding.ASCII.GetBytes("BND4").CopyTo(bytes, 0);

        Assert.Throws<InvalidDataException>(() => Bnd4Reader.Parse(bytes));
    }

    [Fact]
    public void Parse_throws_InvalidDataException_when_file_count_exceeds_buffer()
    {
        var bytes = BuildTinyBnd4(new TinyEntry("A", 0x1000, 0x100));
        // Set file_count to something huge — entries can't possibly fit.
        BitConverter.TryWriteBytes(bytes.AsSpan(0x0C, 4), 1_000_000);

        Assert.Throws<InvalidDataException>(() => Bnd4Reader.Parse(bytes));
    }

    [Fact]
    public void Parse_throws_InvalidDataException_when_name_offset_points_outside_buffer()
    {
        var bytes = BuildTinyBnd4(new TinyEntry("A", 0x1000, 0x100));
        // The single entry sits at 0x40. name_offset is at entry+0x24.
        BitConverter.TryWriteBytes(bytes.AsSpan(0x40 + 0x24, 4), int.MaxValue);

        Assert.Throws<InvalidDataException>(() => Bnd4Reader.Parse(bytes));
    }

    [Fact]
    public void Parse_throws_InvalidDataException_when_name_is_not_null_terminated_in_buffer()
    {
        // Build a buffer where name_offset points to the very last 2 bytes (non-zero) so
        // the UTF-16 scanner walks off the end without ever finding 0x0000.
        var bytes = BuildTinyBnd4(new TinyEntry("A", 0x1000, 0x100));
        // Point name_offset at the last byte; reader needs two bytes per code unit but only
        // one remains (and even that has no terminator).
        BitConverter.TryWriteBytes(bytes.AsSpan(0x40 + 0x24, 4), bytes.Length - 1);

        Assert.Throws<InvalidDataException>(() => Bnd4Reader.Parse(bytes));
    }

    [Fact]
    public void Parse_throws_InvalidDataException_when_data_offset_is_far_past_buffer_end()
    {
        // A malicious save with data_offset = long.MaxValue would overflow checked((int)long)
        // at the call site (EldenRingSave.cs) and throw OverflowException — NOT the
        // InvalidDataException UI code expects. The parser must reject this at the seam.
        var bytes = BuildTinyBnd4(new TinyEntry("A", 0x1000, 0x100));
        // The single entry sits at 0x40. data_offset is at entry+0x18.
        BitConverter.TryWriteBytes(bytes.AsSpan(0x40 + 0x18, 8), long.MaxValue);

        var ex = Assert.Throws<InvalidDataException>(() => Bnd4Reader.Parse(bytes));
        Assert.Contains("entry 0", ex.Message);
        Assert.Contains("data", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_throws_InvalidDataException_when_data_size_is_far_past_buffer_end()
    {
        // data_offset = 0 but data_size = long.MaxValue — the data range still overruns
        // the buffer. Use long arithmetic for offset+size or it wraps around to look valid.
        var bytes = BuildTinyBnd4(new TinyEntry("A", 0x1000, 0x100));
        // Set data_offset to 0 (in-bounds) and data_size to long.MaxValue (overruns).
        BitConverter.TryWriteBytes(bytes.AsSpan(0x40 + 0x18, 8), 0L);
        BitConverter.TryWriteBytes(bytes.AsSpan(0x40 + 0x10, 8), long.MaxValue);

        var ex = Assert.Throws<InvalidDataException>(() => Bnd4Reader.Parse(bytes));
        Assert.Contains("entry 0", ex.Message);
    }

    [Fact]
    public void Parse_throws_InvalidDataException_when_data_offset_is_negative()
    {
        // A small-negative data_offset would pass checked((int)long) and blow up at
        // AsSpan(offset, size) as ArgumentOutOfRangeException — wrong exception type for UI.
        var bytes = BuildTinyBnd4(new TinyEntry("A", 0x1000, 0x100));
        BitConverter.TryWriteBytes(bytes.AsSpan(0x40 + 0x18, 8), -1L);

        var ex = Assert.Throws<InvalidDataException>(() => Bnd4Reader.Parse(bytes));
        Assert.Contains("entry 0", ex.Message);
    }

    [Fact]
    public void Parse_throws_InvalidDataException_when_data_size_is_negative()
    {
        var bytes = BuildTinyBnd4(new TinyEntry("A", 0x1000, 0x100));
        // data_offset valid, data_size negative.
        BitConverter.TryWriteBytes(bytes.AsSpan(0x40 + 0x18, 8), 0L);
        BitConverter.TryWriteBytes(bytes.AsSpan(0x40 + 0x10, 8), -1L);

        var ex = Assert.Throws<InvalidDataException>(() => Bnd4Reader.Parse(bytes));
        Assert.Contains("entry 0", ex.Message);
    }

    [Fact]
    public void GetByName_returns_the_matching_entry()
    {
        var bytes = BuildTinyBnd4(
            new TinyEntry("A",   0x1000, 0x100),
            new TinyEntry("BB",  0x2000, 0x200),
            new TinyEntry("CCC", 0x3000, 0x300));
        var entries = Bnd4Reader.Parse(bytes);

        var match = Bnd4Reader.GetByName(entries, "BB");

        Assert.Equal("BB", match.Name);
        Assert.Equal(0x2000L, match.DataOffset);
        Assert.Equal(0x200L, match.DataSize);
    }

    [Fact]
    public void GetByName_throws_InvalidDataException_listing_found_names_when_missing()
    {
        var bytes = BuildTinyBnd4(
            new TinyEntry("A",   0x1000, 0x100),
            new TinyEntry("BB",  0x2000, 0x200),
            new TinyEntry("CCC", 0x3000, 0x300));
        var entries = Bnd4Reader.Parse(bytes);

        var ex = Assert.Throws<InvalidDataException>(
            () => Bnd4Reader.GetByName(entries, "USER_DATA011"));

        // The exception message must surface what WAS found so a future resilience test
        // (and a human reading the log) can see exactly which names exist.
        Assert.Contains("USER_DATA011", ex.Message);
        Assert.Contains("A", ex.Message);
        Assert.Contains("BB", ex.Message);
        Assert.Contains("CCC", ex.Message);
    }

    // -------- Test helpers --------

    /// <summary>One synthetic file-table entry for the tiny BND4 builder.</summary>
    private sealed record TinyEntry(string Name, long DataOffset, long DataSize);

    /// <summary>
    /// Build a minimal BND4 buffer with the given entries. Layout:
    ///   0x00..0x04: "BND4" magic
    ///   0x0C..0x10: file_count (int32 LE)
    ///   0x10..0x18: file_header_offset (int64 LE) = 0x40
    ///   0x40 + i*0x28: entry i (40 bytes)
    ///     +0x00: file_flags (byte) = 0x40
    ///     +0x04: reserved int32 = -1
    ///     +0x08: compressed_size (int64) = DataSize
    ///     +0x10: uncompressed_size (int64) = DataSize
    ///     +0x18: data_offset (int64) = DataOffset
    ///     +0x20: id (int32) = i
    ///     +0x24: name_offset (int32) -> name table region
    ///   Name table immediately follows the entry table; each name is UTF-16LE null-terminated.
    /// The buffer is sized to hold header + entries + names AND to extend through the
    /// largest <c>DataOffset + DataSize</c> any entry points at. That way the reader's
    /// per-entry data-range bounds check (which rejects ranges outside the buffer) doesn't
    /// fire on these happy-path fixtures. No file data is written — the reader records
    /// <c>DataOffset</c> verbatim and never reads bytes from it.
    /// </summary>
    private static byte[] BuildTinyBnd4(params TinyEntry[] entries)
    {
        const int fileHeaderOffset = 0x40;
        const int entrySize = 0x28;
        var entryTableEnd = fileHeaderOffset + entries.Length * entrySize;

        // Build the name table: each name is UTF-16LE + a 2-byte null terminator. Track each
        // name's offset within the overall buffer.
        var nameOffsets = new int[entries.Length];
        var nameBytes = new List<byte>();
        var cursor = entryTableEnd;
        for (int i = 0; i < entries.Length; i++)
        {
            nameOffsets[i] = cursor;
            var encoded = Encoding.Unicode.GetBytes(entries[i].Name);
            nameBytes.AddRange(encoded);
            nameBytes.Add(0x00);
            nameBytes.Add(0x00);
            cursor += encoded.Length + 2;
        }

        // Extend the buffer to cover every entry's data range (DataOffset + DataSize). The
        // happy-path fixtures point at 0x1000+ but never have payload bytes written — we
        // just need the buffer length to satisfy the parser's bounds check.
        long maxDataEnd = 0;
        foreach (var e in entries)
        {
            long end = e.DataOffset + e.DataSize;
            if (end > maxDataEnd) maxDataEnd = end;
        }

        var totalSize = (int)Math.Max(cursor, maxDataEnd);
        var buffer = new byte[totalSize];

        // Magic.
        Encoding.ASCII.GetBytes("BND4").CopyTo(buffer, 0);
        // file_count.
        BitConverter.TryWriteBytes(buffer.AsSpan(0x0C, 4), entries.Length);
        // file_header_offset.
        BitConverter.TryWriteBytes(buffer.AsSpan(0x10, 8), (long)fileHeaderOffset);

        // Entries.
        for (int i = 0; i < entries.Length; i++)
        {
            int start = fileHeaderOffset + i * entrySize;
            buffer[start] = 0x40;
            BitConverter.TryWriteBytes(buffer.AsSpan(start + 0x04, 4), -1);
            BitConverter.TryWriteBytes(buffer.AsSpan(start + 0x08, 8), entries[i].DataSize);
            BitConverter.TryWriteBytes(buffer.AsSpan(start + 0x10, 8), entries[i].DataSize);
            BitConverter.TryWriteBytes(buffer.AsSpan(start + 0x18, 8), entries[i].DataOffset);
            BitConverter.TryWriteBytes(buffer.AsSpan(start + 0x20, 4), i);
            BitConverter.TryWriteBytes(buffer.AsSpan(start + 0x24, 4), nameOffsets[i]);
        }

        // Names.
        nameBytes.ToArray().CopyTo(buffer, entryTableEnd);

        return buffer;
    }
}
