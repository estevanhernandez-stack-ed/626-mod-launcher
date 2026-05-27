using ModManager.Core.Tools;

namespace ModManager.Tests.Tools;

public class ToolRegistryTests
{
    [Fact]
    public void Load_returns_empty_when_file_missing()
    {
        var dir = TestSupport.TempDir("toolreg-");
        var reg = ToolRegistry.Load(dir);
        Assert.Empty(reg.Tools);
    }

    [Fact]
    public void Save_then_load_round_trips_entries()
    {
        var dir = TestSupport.TempDir("toolreg-");
        var entries = new[]
        {
            new ToolEntry("wse-save-editor", "WSE Save Editor",
                @"C:\tools\wse-save-editor", "WSE_Save_Editor.exe",
                EditsSaves: true,
                GetUrl: "https://www.nexusmods.com/windrose/mods/153",
                Source: "catalog"),
        };
        ToolRegistry.Save(dir, entries);

        var reg = ToolRegistry.Load(dir);
        Assert.Single(reg.Tools);
        Assert.Equal("wse-save-editor", reg.Tools[0].ToolId);
        Assert.True(reg.Tools[0].EditsSaves);
    }

    [Fact]
    public void Save_writes_camelCase_json()
    {
        var dir = TestSupport.TempDir("toolreg-");
        var entries = new[]
        {
            new ToolEntry("wse-save-editor", "WSE Save Editor", @"C:\tools\wse",
                "WSE_Save_Editor.exe", EditsSaves: true, GetUrl: null, Source: "catalog"),
        };
        ToolRegistry.Save(dir, entries);

        var json = File.ReadAllText(Path.Combine(dir, "tools.json"));
        Assert.Contains("\"toolId\":", json);
        Assert.Contains("\"editsSaves\":", json);
        Assert.DoesNotContain("\"ToolId\":", json);
    }

    [Fact]
    public void Save_is_atomic_no_partial_file_left_on_disk()
    {
        var dir = TestSupport.TempDir("toolreg-");
        ToolRegistry.Save(dir, new[]
        {
            new ToolEntry("first", "First", "x", "x.exe", false, null, "user"),
        });
        ToolRegistry.Save(dir, new[]
        {
            new ToolEntry("second", "Second", "y", "y.exe", false, null, "user"),
        });

        var reg = ToolRegistry.Load(dir);
        Assert.Single(reg.Tools);
        Assert.Equal("second", reg.Tools[0].ToolId);

        Assert.False(File.Exists(Path.Combine(dir, "tools.json.tmp")));
    }

    [Fact]
    public void Load_throws_InvalidDataException_on_malformed_json()
    {
        var dir = TestSupport.TempDir("toolreg-");
        File.WriteAllText(Path.Combine(dir, "tools.json"), "not json");
        Assert.Throws<InvalidDataException>(() => ToolRegistry.Load(dir));
    }
}
