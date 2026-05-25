using System.IO.Compression;

namespace ModManager.Tests;

internal static class TestSupport
{
    public static string TempDir(string prefix)
    {
        var d = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public static string Read(string path) => File.ReadAllText(path);

    public static void WriteZip(string zipPath, params (string Name, string Content)[] entries)
    {
        using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var e = zip.CreateEntry(name);
            using var w = new StreamWriter(e.Open());
            w.Write(content);
        }
    }

    // Build a real .tar via SharpCompress so the archive seam can be exercised on a non-zip
    // container (proving the abstraction isn't zip-only). 7z/rar have no managed writer, so they
    // stay fixture-gated; tar is the writable second format that goes through the same reader.
    public static void WriteTar(string tarPath, params (string Name, string Content)[] entries)
    {
        using var fs = File.Create(tarPath);
        using var writer = SharpCompress.Writers.WriterFactory.OpenWriter(
            fs, SharpCompress.Common.ArchiveType.Tar,
            SharpCompress.Writers.WriterOptions.ForTar(SharpCompress.Common.CompressionType.None));
        foreach (var (name, content) in entries)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            using var ms = new MemoryStream(bytes);
            writer.Write(name, ms, DateTime.UtcNow);
        }
    }
}
