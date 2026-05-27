using ModManager.Core.Tools;

namespace ModManager.Tests.Tools;

public class ToolDetectorTests
{
    private static string MakeZip(string filename, params (string path, string content)[] entries)
    {
        var root = TestSupport.TempDir("tooldet-");
        var zipPath = Path.Combine(root, filename);
        // Use the project's existing zip-writer helper (System.IO.Compression-backed) to keep
        // the fixture creation aligned with every other test in this suite. The detector reads
        // via SharpCompress, which transparently handles standard zip output.
        TestSupport.WriteZip(zipPath, entries.Select(e => (e.path, e.content)).ToArray());
        return zipPath;
    }

    [Fact]
    public void Catalog_match_by_zip_filename_returns_known_tool()
    {
        var zip = MakeZip("WSE-Save-Editor-v1.2.zip",
            ("WSE_Save_Editor.exe", "binary"));
        var (cls, known) = ToolDetector.Classify(zip, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Tool, cls);
        Assert.NotNull(known);
        Assert.Equal("wse-save-editor", known!.ToolId);
    }

    [Fact]
    public void Heuristic_tool_returns_tool_with_null_known()
    {
        var zip = MakeZip("some-random-utility.zip",
            ("utility.exe", "binary"),
            ("README.md", "docs"));
        var (cls, known) = ToolDetector.Classify(zip, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Tool, cls);
        Assert.Null(known);
    }

    [Fact]
    public void Pak_file_in_archive_returns_mod_even_when_exe_present()
    {
        var zip = MakeZip("mixed-mod.zip",
            ("R5/Content/Paks/~mods/MyMod_P.pak", "binary"),
            ("installer.exe", "binary"));
        var (cls, _) = ToolDetector.Classify(zip, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Mod, cls);
    }

    [Fact]
    public void Lua_under_scripts_returns_mod()
    {
        var zip = MakeZip("ue4ss-lua-mod.zip",
            ("R5/Binaries/Win64/ue4ss/Mods/MyMod/Scripts/main.lua", "lua"));
        var (cls, _) = ToolDetector.Classify(zip, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Mod, cls);
    }

    [Fact]
    public void Archive_with_only_docs_and_no_exe_returns_mod_default()
    {
        var zip = MakeZip("docs-only.zip",
            ("README.md", "docs"),
            ("LICENSE.txt", "license"));
        var (cls, _) = ToolDetector.Classify(zip, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Mod, cls);
    }

    [Fact]
    public void Bat_file_only_returns_tool()
    {
        var zip = MakeZip("script-tool.zip", ("run.bat", "echo hi"));
        var (cls, known) = ToolDetector.Classify(zip, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Tool, cls);
        Assert.Null(known);
    }

    [Fact]
    public void Catalog_match_is_case_insensitive_on_zip_filename()
    {
        var zip = MakeZip("WSE_Save_Editor_1.5.zip",
            ("WSE_Save_Editor.exe", "binary"));
        var (cls, known) = ToolDetector.Classify(zip, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Tool, cls);
        Assert.Equal("wse-save-editor", known!.ToolId);
    }

    [Fact]
    public void Catalog_entry_for_wrong_engine_does_not_match()
    {
        var zip = MakeZip("WSE-Save-Editor.zip",
            ("WSE_Save_Editor.exe", "binary"));
        // FromSoft game — WSE entries shouldn't match
        var (cls, known) = ToolDetector.Classify(zip, engine: "fromsoft", steamAppId: "1245620");
        // Falls through to heuristic-tool (has .exe)
        Assert.Equal(ToolClassification.Tool, cls);
        Assert.Null(known);
    }

    [Fact]
    public void Manifest_json_with_mod_shape_returns_mod()
    {
        var zip = MakeZip("smapi-mod.zip",
            ("manifest.json", """{"Name":"MyMod","Author":"someone","Version":"1.0.0"}"""));
        var (cls, _) = ToolDetector.Classify(zip, engine: "smapi", steamAppId: "413150");
        Assert.Equal(ToolClassification.Mod, cls);
    }

    [Fact]
    public void Malformed_archive_does_not_throw_returns_mod_default()
    {
        var root = TestSupport.TempDir("tooldet-");
        var bogus = Path.Combine(root, "bogus.zip");
        File.WriteAllText(bogus, "not a zip");
        var (cls, _) = ToolDetector.Classify(bogus, engine: "ue-pak", steamAppId: "3041230");
        Assert.Equal(ToolClassification.Mod, cls);
    }
}
