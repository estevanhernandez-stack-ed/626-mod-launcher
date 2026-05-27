using ModManager.Core.Tools;

namespace ModManager.Tests.Tools;

public class ToolEntryTests
{
    [Fact]
    public void ToolEntry_carries_id_name_paths_flags_and_source()
    {
        var entry = new ToolEntry(
            ToolId: "wse-save-editor",
            DisplayName: "WSE Save Editor",
            InstallDir: @"C:\_626mods\windrose\tools\wse-save-editor",
            Runnable: "WSE_Save_Editor.exe",
            EditsSaves: true,
            GetUrl: "https://www.nexusmods.com/windrose/mods/153",
            Source: "catalog");

        Assert.Equal("wse-save-editor", entry.ToolId);
        Assert.True(entry.EditsSaves);
        Assert.Equal("catalog", entry.Source);
    }
}
