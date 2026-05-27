using System.Text;

namespace ModManager.Core.SaveEditor.FromSoft;

/// <summary>
/// One file-table entry from a BND4 container — name, payload offset (in the underlying
/// buffer), and uncompressed payload size. Names come from the BND4 name table as UTF-16LE.
/// </summary>
public sealed record Bnd4Entry(string Name, long DataOffset, long DataSize);

/// <summary>
/// Pure reader for BND4 file tables (the FromSoftware container format used by Elden Ring
/// <c>.sl2</c> saves). Walks the file table starting at the header-provided offset, decodes
/// each 40-byte entry, and reads the UTF-16LE name from the name table.
///
/// Why it exists: the existing <see cref="EldenRingSave"/> reader uses hardcoded byte
/// offsets (e.g. <c>0x019003B0</c> for the save-header section). A future patch that adds an
/// entry (the DLC already did this — <c>USER_DATA012</c>) would shift the layout and silently
/// corrupt saves. Looking sections up by NAME instead survives layout shifts; a renamed save
/// header fails loud with a clear <see cref="InvalidDataException"/> listing the names that
/// WERE found.
///
/// BND4 layout (long-offsets + IDs + names format, the only one ER ships):
/// <code>
///   0x00..0x04: "BND4" magic (ASCII)
///   0x0C..0x10: file_count           (int32 LE)
///   0x10..0x18: file_header_offset   (int64 LE; the start of the file-table region)
///   file_header_offset + i * 0x28: entry i (40 bytes)
///     +0x00: file_flags (byte)
///     +0x04: reserved (int32) = -1
///     +0x08: compressed_size   (int64 LE)
///     +0x10: uncompressed_size (int64 LE) -> Bnd4Entry.DataSize
///     +0x18: data_offset       (int64 LE) -> Bnd4Entry.DataOffset
///     +0x20: id                (int32 LE)
///     +0x24: name_offset       (int32 LE) -> UTF-16LE null-terminated name in the buffer
/// </code>
///
/// Pure-core: NO Electron, NO WinUI, NO WinRT. System.* only — runs under xUnit headless.
/// </summary>
public static class Bnd4Reader
{
    private const int EntrySize = 0x28;             // 40 bytes per entry
    private const int FileCountOffset = 0x0C;
    private const int FileHeaderOffsetOffset = 0x10;
    private const int MinHeaderBytes = 0x18;        // through file_header_offset

    private const int EntryDataSizeOffset = 0x10;   // uncompressed_size
    private const int EntryDataOffsetOffset = 0x18; // data_offset
    private const int EntryNameOffsetOffset = 0x24; // name_offset

    private static readonly byte[] Bnd4Magic = Encoding.ASCII.GetBytes("BND4");

    /// <summary>
    /// Walk the BND4 file table and return one <see cref="Bnd4Entry"/> per file, in file-table
    /// order. Throws <see cref="InvalidDataException"/> on any malformed read: missing magic,
    /// truncated header, <c>file_count</c> too large for the buffer, entry offsets outside the
    /// buffer, or a name that runs past the end of the buffer without a UTF-16 null terminator.
    /// </summary>
    public static IReadOnlyList<Bnd4Entry> Parse(byte[] bytes)
    {
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));

        if (bytes.Length < MinHeaderBytes)
        {
            throw new InvalidDataException(
                $"BND4 buffer is too small to contain a header (got {bytes.Length} bytes, need at least {MinHeaderBytes}).");
        }

        // Magic check.
        if (bytes[0] != Bnd4Magic[0] || bytes[1] != Bnd4Magic[1]
            || bytes[2] != Bnd4Magic[2] || bytes[3] != Bnd4Magic[3])
        {
            var actual = Encoding.ASCII.GetString(bytes, 0, 4);
            throw new InvalidDataException(
                $"Not a BND4 container — expected magic 'BND4', got '{actual}'.");
        }

        int fileCount = BitConverter.ToInt32(bytes, FileCountOffset);
        long fileHeaderOffset = BitConverter.ToInt64(bytes, FileHeaderOffsetOffset);

        if (fileCount < 0)
        {
            throw new InvalidDataException($"BND4 file_count is negative: {fileCount}.");
        }

        // file_header_offset must land inside the buffer, and the full entry table must fit.
        if (fileHeaderOffset < 0 || fileHeaderOffset > bytes.Length)
        {
            throw new InvalidDataException(
                $"BND4 file_header_offset 0x{fileHeaderOffset:X} is outside the buffer (length {bytes.Length}).");
        }

        long entryTableEnd = fileHeaderOffset + (long)fileCount * EntrySize;
        if (entryTableEnd > bytes.Length)
        {
            throw new InvalidDataException(
                $"BND4 file_count {fileCount} requires entry table through 0x{entryTableEnd:X}, " +
                $"but buffer is only {bytes.Length} bytes.");
        }

        var entries = new List<Bnd4Entry>(fileCount);
        for (int i = 0; i < fileCount; i++)
        {
            int entryStart = checked((int)fileHeaderOffset + i * EntrySize);

            long dataSize = BitConverter.ToInt64(bytes, entryStart + EntryDataSizeOffset);
            long dataOffset = BitConverter.ToInt64(bytes, entryStart + EntryDataOffsetOffset);
            int nameOffset = BitConverter.ToInt32(bytes, entryStart + EntryNameOffsetOffset);

            string name = ReadUtf16Name(bytes, nameOffset, entryIndex: i);
            entries.Add(new Bnd4Entry(name, dataOffset, dataSize));
        }

        return entries;
    }

    /// <summary>
    /// Return the entry whose <see cref="Bnd4Entry.Name"/> matches <paramref name="name"/>
    /// (ordinal, case-sensitive — BND4 names are stable identifiers like <c>USER_DATA011</c>).
    /// Throws <see cref="InvalidDataException"/> with the list of names that WERE found when
    /// no match exists, so callers can log exactly what shifted in a future patch.
    /// </summary>
    public static Bnd4Entry GetByName(IReadOnlyList<Bnd4Entry> entries, string name)
    {
        if (entries is null) throw new ArgumentNullException(nameof(entries));
        if (name is null) throw new ArgumentNullException(nameof(name));

        for (int i = 0; i < entries.Count; i++)
        {
            if (string.Equals(entries[i].Name, name, StringComparison.Ordinal))
            {
                return entries[i];
            }
        }

        var foundNames = string.Join(", ", entries.Select(e => e.Name));
        throw new InvalidDataException(
            $"BND4 entry '{name}' not found. Found entries: {foundNames}");
    }

    /// <summary>
    /// Read a UTF-16LE null-terminated string starting at <paramref name="nameOffset"/>. The
    /// terminator is the first 0x0000 16-bit code unit. Throws <see cref="InvalidDataException"/>
    /// if the offset is outside the buffer or the scan reaches the end without finding a
    /// terminator.
    /// </summary>
    private static string ReadUtf16Name(byte[] bytes, int nameOffset, int entryIndex)
    {
        if (nameOffset < 0 || nameOffset > bytes.Length)
        {
            throw new InvalidDataException(
                $"BND4 entry {entryIndex} name_offset 0x{nameOffset:X} is outside the buffer " +
                $"(length {bytes.Length}).");
        }

        // Scan for a 16-bit null. Each step is 2 bytes; we need both bytes to read.
        int end = nameOffset;
        while (end + 1 < bytes.Length)
        {
            if (bytes[end] == 0x00 && bytes[end + 1] == 0x00) break;
            end += 2;
        }

        if (end + 1 >= bytes.Length)
        {
            throw new InvalidDataException(
                $"BND4 entry {entryIndex} name at 0x{nameOffset:X} is not null-terminated " +
                $"within the buffer (length {bytes.Length}).");
        }

        int byteLength = end - nameOffset;
        return Encoding.Unicode.GetString(bytes, nameOffset, byteLength);
    }
}
