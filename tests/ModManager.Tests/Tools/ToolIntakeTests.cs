using ModManager.Core.Tools;

namespace ModManager.Tests.Tools;

public class ToolIntakeTests
{
    // Mirrors the MakeZip pattern in ToolDetectorTests — TestSupport.WriteZip takes a full
    // path, so each test builds its zip inside a throwaway temp dir.
    private static string MakeZip(string filename, params (string path, string content)[] entries)
    {
        var root = TestSupport.TempDir("toolintake-zip-");
        var zipPath = Path.Combine(root, filename);
        TestSupport.WriteZip(zipPath, entries.Select(e => (e.path, e.content)).ToArray());
        return zipPath;
    }

    [Fact]
    public void Install_extracts_zip_and_registers_entry()
    {
        var dataDir = TestSupport.TempDir("data-");
        var zip = MakeZip("WSE-Save-Editor-v1.zip",
            ("WSE_Save_Editor.exe", "binary"),
            ("README.md", "docs"));
        var known = ToolCatalog.Catalog.Single(t => t.ToolId == "wse-save-editor");

        var result = ToolIntake.Install(zip, dataDir, known);

        Assert.NotNull(result.Entry);
        Assert.Equal("wse-save-editor", result.Entry!.ToolId);
        Assert.Equal("WSE_Save_Editor.exe", result.Entry.Runnable);
        Assert.True(File.Exists(Path.Combine(result.Entry.InstallDir, "WSE_Save_Editor.exe")));

        var reg = ToolRegistry.Load(dataDir);
        Assert.Single(reg.Tools);
    }

    [Fact]
    public void Install_picks_catalog_hint_when_multiple_exe_present()
    {
        var dataDir = TestSupport.TempDir("data-");
        var zip = MakeZip("WSE-Save-Editor.zip",
            ("WSE_Save_Editor.exe", "binary"),
            ("setup.exe", "binary"));
        var known = ToolCatalog.Catalog.Single(t => t.ToolId == "wse-save-editor");

        var result = ToolIntake.Install(zip, dataDir, known);

        Assert.Equal("WSE_Save_Editor.exe", result.Entry!.Runnable);
    }

    [Fact]
    public void Install_picks_single_exe_when_no_catalog_match()
    {
        var dataDir = TestSupport.TempDir("data-");
        var zip = MakeZip("random-tool.zip",
            ("Cool_Utility.exe", "binary"));

        var result = ToolIntake.Install(zip, dataDir, knownTool: null);

        Assert.Equal("Cool_Utility.exe", result.Entry!.Runnable);
        Assert.Equal("user", result.Entry.Source);
    }

    [Fact]
    public void Install_returns_candidates_when_ambiguous()
    {
        var dataDir = TestSupport.TempDir("data-");
        var zip = MakeZip("multi-tool.zip",
            ("MainEditor.exe", "binary"),
            ("ItemBrowser.exe", "binary"));

        var result = ToolIntake.Install(zip, dataDir, knownTool: null);

        Assert.Equal("", result.Entry!.Runnable);
        Assert.Contains("MainEditor.exe", result.Candidates);
        Assert.Contains("ItemBrowser.exe", result.Candidates);
    }

    [Fact]
    public void Install_filters_install_setup_update_patterns_from_candidates()
    {
        var dataDir = TestSupport.TempDir("data-");
        var zip = MakeZip("toolwithsetup.zip",
            ("MainTool.exe", "binary"),
            ("install_dependencies.bat", "binary"),
            ("update_assets.exe", "binary"));

        var result = ToolIntake.Install(zip, dataDir, knownTool: null);

        Assert.Equal("MainTool.exe", result.Entry!.Runnable);
    }

    [Fact]
    public void Install_default_source_is_catalog_when_known_tool_provided()
    {
        var dataDir = TestSupport.TempDir("data-");
        var zip = MakeZip("WSE-Save-Editor.zip", ("WSE_Save_Editor.exe", "x"));
        var known = ToolCatalog.Catalog.Single(t => t.ToolId == "wse-save-editor");

        var result = ToolIntake.Install(zip, dataDir, known);

        Assert.Equal("catalog", result.Entry!.Source);
        Assert.True(result.Entry.EditsSaves);
        Assert.Equal("https://www.nexusmods.com/windrose/mods/153", result.Entry.GetUrl);
    }

    [Fact]
    public void Install_creates_install_dir_under_gameDataDir_tools_toolid()
    {
        var dataDir = TestSupport.TempDir("data-");
        var zip = MakeZip("WSE-Save-Editor.zip", ("WSE_Save_Editor.exe", "x"));
        var known = ToolCatalog.Catalog.Single(t => t.ToolId == "wse-save-editor");

        var result = ToolIntake.Install(zip, dataDir, known);

        var expected = Path.Combine(dataDir, "tools", "wse-save-editor");
        Assert.Equal(expected, result.Entry!.InstallDir);
        Assert.True(Directory.Exists(expected));
    }
}
