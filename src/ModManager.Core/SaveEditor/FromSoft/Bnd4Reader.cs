using System.Text;

namespace ModManager.Core.SaveEditor.FromSoft;

/// <summary>
/// One file-table entry from a BND4 container — name, payload offset (in the underlying
/// buffer), and uncompressed payload size. Names come from the BND4 name table as UTF-16LE.
/// </summary>
public sealed record Bnd4Entry(string Name, long DataOffset, long DataSize);

/// <summary>
/// Pure reader for BND4 file tables (the FromSoftware container format used by Elden Ring
/// <c>.sl2</c>/<c>.co2</c>/<c>.err</c> saves). Walks the file table starting at the
/// header-provided offset, decodes each entry, and reads the UTF-16LE name from the name table.
///
/// Why it exists: the original <see cref="EldenRingSave"/> reader used hardcoded byte offsets
/// (e.g. <c>0x019003B0</c> for the save-header section). A future patch that adds an entry
/// (the DLC already did this — <c>USER_DATA012</c>) would shift the layout and silently corrupt
/// saves. Looking sections up by NAME instead survives layout shifts; a renamed save header
/// fails loud with a clear <see cref="InvalidDataException"/> listing the names that WERE found.
///
/// Two entry strides are supported:
/// <list type="bullet">
///   <item><description><b>0x20 (32 bytes) — current real ER</b>: no compressed_size field.
///     uncompressed_size at +0x08 (int64); data_offset at +0x10 (int32); name_offset at +0x14
///     (int32).</description></item>
///   <item><description><b>0x28 (40 bytes) — older FromSoft / fixture</b>: full layout with
///     compressed_size. uncompressed_size at +0x10 (int64); data_offset at +0x18 (int64);
///     name_offset at +0x24 (int32).</description></item>
/// </list>
/// Stride is read from <c>file_header_size</c> at file offset 0x20 when the BND4 header is
/// long enough to carry it AND the value is a recognized stride. Otherwise we fall back to the
/// older 40-byte layout — that's what the synthetic <c>EldenRingFixture</c> writes there.
///
/// BND4 header layout (the part we depend on):
/// <code>
///   0x00..0x04: "BND4" magic (ASCII)
///   0x0C..0x10: file_count           (int32 LE)
///   0x10..0x18: file_header_offset   (int64 LE; the start of the file-table region)
///   0x20..0x28: file_header_size     (int64 LE; per-entry stride — 0x20 or 0x28)
/// </code>
///
/// Pure-core: NO Electron, NO WinUI, NO WinRT. System.* only — runs under xUnit headless.
/// </summary>
public static class Bnd4Reader
{
    // The two strides we actively decode. Anything else triggers the fallback to 0x28.
    private const long Stride32 = 0x20;
    private const long Stride40 = 0x28;

    private const int FileCountOffset = 0x0C;
    private const int FileHeaderOffsetOffset = 0x10;
    private const int FileHeaderSizeOffset = 0x20;     // int64 LE — per-entry stride in real ER
    private const int MinHeaderBytes = 0x18;           // through file_header_offset (older saves)
    private const int ExtendedHeaderBytes = 0x28;      // through file_header_size

    // 32-byte entry field offsets (real ER, no compressed_size).
    private const int Entry32DataSize = 0x08;          // uncompressed_size (int64)
    private const int Entry32DataOffset = 0x10;        // data_offset (int32)
    private const int Entry32NameOffset = 0x14;        // name_offset (int32)

    // 40-byte entry field offsets (older format + fixture).
    private const int Entry40DataSize = 0x10;          // uncompressed_size (int64)
    private const int Entry40DataOffset = 0x18;        // data_offset (int64)
    private const int Entry40NameOffset = 0x24;        // name_offset (int32)

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

        // Detect per-entry stride. file_header_size at 0x20 IS the stride in real ER saves;
        // the synthetic fixture writes a different value there (total entry-table size), so we
        // only trust it when it equals one of the two strides we know how to decode. Otherwise
        // fall back to Stride40 — the format the fixture exercises.
        long entryStride = Stride40;
        if (bytes.Length >= ExtendedHeaderBytes)
        {
            long candidate = BitConverter.ToInt64(bytes, FileHeaderSizeOffset);
            if (candidate == Stride32 || candidate == Stride40)
            {
                entryStride = candidate;
            }
        }

        // file_header_offset must land inside the buffer, and the full entry table must fit.
        if (fileHeaderOffset < 0 || fileHeaderOffset > bytes.Length)
        {
            throw new InvalidDataException(
                $"BND4 file_header_offset 0x{fileHeaderOffset:X} is outside the buffer (length {bytes.Length}).");
        }

        long entryTableEnd = fileHeaderOffset + (long)fileCount * entryStride;
        if (entryTableEnd > bytes.Length)
        {
            throw new InvalidDataException(
                $"BND4 file_count {fileCount} at stride 0x{entryStride:X} requires entry table " +
                $"through 0x{entryTableEnd:X}, but buffer is only {bytes.Length} bytes.");
        }

        int dataSizeFieldOff = entryStride == Stride32 ? Entry32DataSize : Entry40DataSize;
        int dataOffsetFieldOff = entryStride == Stride32 ? Entry32DataOffset : Entry40DataOffset;
        int nameOffsetFieldOff = entryStride == Stride32 ? Entry32NameOffset : Entry40NameOffset;

        var entries = new List<Bnd4Entry>(fileCount);
        for (int i = 0; i < fileCount; i++)
        {
            int entryStart = checked((int)fileHeaderOffset + i * (int)entryStride);

            long dataSize = BitConverter.ToInt64(bytes, entryStart + dataSizeFieldOff);
            long dataOffset = entryStride == Stride32
                ? BitConverter.ToInt32(bytes, entryStart + dataOffsetFieldOff)
                : BitConverter.ToInt64(bytes, entryStart + dataOffsetFieldOff);
            int nameOffset = BitConverter.ToInt32(bytes, entryStart + nameOffsetFieldOff);

            // Per-entry data-range bounds check. Without this, a malformed save with
            // data_offset = long.MaxValue would slip past Parse and only blow up later at
            // checked((int)long) inside EldenRingSave as OverflowException — not the
            // InvalidDataException UI code expects. Reject the individual fields first
            // (negative or already past the buffer) before computing the sum, otherwise
            // long.MaxValue + dataSize wraps to a negative value and looks valid.
            if (dataOffset < 0
                || dataSize < 0
                || dataOffset > bytes.Length
                || dataSize > bytes.Length
                || dataOffset + dataSize > bytes.Length)
            {
                throw new InvalidDataException(
                    $"BND4 entry {i} data range (offset 0x{dataOffset:X}, size 0x{dataSize:X}) " +
                    $"is outside the buffer (length {bytes.Length}, stride 0x{entryStride:X}).");
            }

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
