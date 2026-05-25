using SharpCompress.Archives;
using SharpCompress.Common;

namespace ModManager.Core;

/// <summary>
/// The archive seam: one Core abstraction over mod-archive reading so 7z/rar work everywhere zip
/// does. Replaces the scattered raw <c>System.IO.Compression.ZipFile.OpenRead</c> calls at intake.
/// Pure-managed (SharpCompress) — no Electron/UI ref, so it bundles and stays headless-testable.
/// NOTE: this is for READING mod archives at intake only. Save-snapshot zips (SaveManager) keep
/// using <c>ZipFile</c> — they are written, not read as mod input.
/// </summary>
public interface IArchiveReader
{
    /// <summary>Open an archive once. Auto-detects zip / 7z / rar / tar from the file content.</summary>
    IArchiveHandle Open(string archivePath);
}

/// <summary>One opened archive. Lists its file entries and extracts them by name. Dispose closes it.</summary>
public interface IArchiveHandle : IDisposable
{
    /// <summary>FILE entry keys (directories excluded), full paths with forward-slash separators —
    /// matching the old <c>ZipArchiveEntry.FullName</c> shape the call sites expect.</summary>
    IReadOnlyList<string> EntryNames { get; }

    /// <summary>Extract one entry (by its <see cref="EntryNames"/> key) to an absolute destination,
    /// creating parent directories. <paramref name="overwrite"/> false throws if the dest exists —
    /// matching the old <c>entry.ExtractToFile(dest, overwrite: false)</c> contract.</summary>
    void Extract(string entryName, string destAbs, bool overwrite);
}

/// <summary>SharpCompress-backed <see cref="IArchiveReader"/>. The handle holds the open archive
/// for the life of its <c>using</c> scope, so callers iterate + extract against one handle.</summary>
public sealed class SharpCompressArchiveReader : IArchiveReader
{
    public IArchiveHandle Open(string archivePath)
        => new Handle(ArchiveFactory.OpenArchive(archivePath));

    private sealed class Handle : IArchiveHandle
    {
        private readonly IArchive _archive;

        public Handle(IArchive archive) => _archive = archive;

        // File entries only; normalize the key to forward slashes (some formats use '\').
        public IReadOnlyList<string> EntryNames => _archive.Entries
            .Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key))
            .Select(e => NormalizeKey(e.Key!))
            .ToList();

        public void Extract(string entryName, string destAbs, bool overwrite)
        {
            var want = NormalizeKey(entryName);
            var entry = _archive.Entries.FirstOrDefault(
                e => !e.IsDirectory && e.Key is not null && NormalizeKey(e.Key) == want)
                ?? throw new FileNotFoundException($"Archive entry not found: {entryName}");

            // Preserve the old ExtractToFile(overwrite:false) semantics exactly — refuse, don't clobber.
            if (!overwrite && File.Exists(destAbs))
                throw new IOException($"The file '{destAbs}' already exists.");

            var dir = Path.GetDirectoryName(destAbs);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            entry.WriteToFile(destAbs, new ExtractionOptions { Overwrite = overwrite });
        }

        public void Dispose() => _archive.Dispose();

        private static string NormalizeKey(string key) => key.Replace('\\', '/');
    }
}
