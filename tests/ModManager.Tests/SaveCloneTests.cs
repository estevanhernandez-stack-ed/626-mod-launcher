using ModManager.Core;

namespace ModManager.Tests;

// FromSoft mods invent their own save extension (Seamless Co-op = .co2 vs vanilla .sl2). The save
// manager recognizes the types and can safely clone one to another (copy + rename), never touching
// the source and never silently overwriting an existing save of the target type.
public class SaveCloneTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mmb-sc-" + Guid.NewGuid().ToString("N"));
    private static IReadOnlyList<SaveType> FromSoft => GameProfiles.Resolve("fromsoft", null).SaveTypes;

    public SaveCloneTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "ER0000.sl2"), "VANILLA");
        File.WriteAllText(Path.Combine(_dir, "ER0000.sl2.bak"), "BAK"); // not a primary save — ignored
    }
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    [Fact]
    public void Lists_only_known_save_types_with_labels()
    {
        var files = SaveManager.ListSaveFiles(_dir, FromSoft);
        var f = Assert.Single(files);
        Assert.Equal("ER0000.sl2", f.Name);
        Assert.Equal("Vanilla", f.TypeLabel);
    }

    [Fact]
    public void Clone_copies_to_the_other_extension_and_keeps_the_source()
    {
        var created = SaveManager.CloneToType(_dir, "ER0000.sl2", ".co2");

        Assert.Equal("ER0000.co2", created);
        Assert.Equal("VANILLA", File.ReadAllText(Path.Combine(_dir, "ER0000.co2"))); // copied content
        Assert.Equal("VANILLA", File.ReadAllText(Path.Combine(_dir, "ER0000.sl2"))); // source intact
        Assert.Contains(SaveManager.ListSaveFiles(_dir, FromSoft), x => x.TypeLabel == "Seamless Co-op");
    }

    [Fact]
    public void Clone_refuses_to_overwrite_an_existing_target_save()
    {
        File.WriteAllText(Path.Combine(_dir, "ER0000.co2"), "COOP-PROGRESS");

        Assert.Throws<IOException>(() => SaveManager.CloneToType(_dir, "ER0000.sl2", ".co2"));
        Assert.Equal("COOP-PROGRESS", File.ReadAllText(Path.Combine(_dir, "ER0000.co2"))); // untouched
    }

    [Fact]
    public void Clone_with_overwrite_replaces_the_target()
    {
        File.WriteAllText(Path.Combine(_dir, "ER0000.co2"), "OLD");

        var created = SaveManager.CloneToType(_dir, "ER0000.sl2", ".co2", overwrite: true);

        Assert.Equal("ER0000.co2", created);
        Assert.Equal("VANILLA", File.ReadAllText(Path.Combine(_dir, "ER0000.co2"))); // replaced from source
    }
}
