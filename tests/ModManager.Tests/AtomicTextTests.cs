using ModManager.Core;

namespace ModManager.Tests;

// Atomic text writes for non-JSON state (the ME2 config toml). Same temp+rename guarantee.
public class AtomicTextTests
{
    [Fact]
    public void Writes_and_overwrites_atomically()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mmb-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "config.toml");
            AtomicJson.WriteTextAtomic(file, "first");
            Assert.Equal("first", File.ReadAllText(file));

            AtomicJson.WriteTextAtomic(file, "second");
            Assert.Equal("second", File.ReadAllText(file));

            // No temp files left behind — only the config file remains.
            Assert.Equal(new[] { file }, Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
