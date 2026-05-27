using ModManager.Core.Tools;

namespace ModManager.Tests.Tools;

public class ToolCatalogTests
{
    [Fact]
    public void Catalog_has_wse_save_editor_entry_for_windrose()
    {
        var entry = ToolCatalog.Catalog.Single(t => t.ToolId == "wse-save-editor");
        Assert.Equal("WSE Save Editor", entry.DisplayName);
        Assert.Equal("ue-pak", entry.Engine);
        Assert.Equal("3041230", entry.SteamAppId);
        Assert.True(entry.EditsSaves);
        Assert.Equal("https://www.nexusmods.com/windrose/mods/153", entry.GetUrl);
        Assert.Contains("RimmyCode", entry.Author);
    }

    [Fact]
    public void Catalog_has_wse_save_fix_entry_for_windrose()
    {
        var entry = ToolCatalog.Catalog.Single(t => t.ToolId == "wse-save-fix");
        Assert.Equal("ue-pak", entry.Engine);
        Assert.Equal("3041230", entry.SteamAppId);
        Assert.True(entry.EditsSaves);
        Assert.Contains("RimmyCode", entry.Author);
    }

    [Fact]
    public void Every_entry_has_at_least_one_zip_filename_hint()
    {
        foreach (var entry in ToolCatalog.Catalog)
        {
            Assert.NotEmpty(entry.ZipFilenameHints);
        }
    }

    [Fact]
    public void Every_entry_has_at_least_one_expected_runnable_hint()
    {
        foreach (var entry in ToolCatalog.Catalog)
        {
            Assert.NotEmpty(entry.ExpectedRunnableHints);
        }
    }
}
