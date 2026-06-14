using ModManager.Core;

namespace ModManager.Tests;

// Ports steam-core.test.js — pure Steam parsing (app id from launch url, library folders).
public class SteamParseTests
{
    [Fact]
    public void ParseAppId_prefers_explicit()
        => Assert.Equal("999", SteamParse.ParseAppId("steam://rungameid/3041230", "999"));

    [Fact]
    public void ParseAppId_from_rungameid()
        => Assert.Equal("3041230", SteamParse.ParseAppId("steam://rungameid/3041230", null));

    [Fact]
    public void ParseAppId_from_app_url()
        => Assert.Equal("12345", SteamParse.ParseAppId("https://store.steampowered.com/app/12345/X/", null));

    [Fact]
    public void ParseAppId_none()
        => Assert.Null(SteamParse.ParseAppId(null, null));

    [Fact]
    public void ParseLibraryFolders_extracts_paths()
    {
        var vdf = @"""libraryfolders""{""0""{""path""  ""C:\\Program Files (x86)\\Steam""}""1""{""path""  ""D:\\SteamLibrary""}}";
        var paths = SteamParse.ParseLibraryFolders(vdf);
        Assert.Equal(2, paths.Count);
        Assert.Contains("SteamLibrary", paths[1]);
    }

    [Fact]
    public void ParseAppManifest_extracts_appid_name_installdir()
    {
        var acf = @"""AppState""
{
	""appid""		""1716740""
	""name""		""Starfield""
	""installdir""	""Starfield""
	""StateFlags""	""4""
}";
        var m = SteamParse.ParseAppManifest(acf);
        Assert.Equal("1716740", m.AppId);
        Assert.Equal("Starfield", m.Name);
        Assert.Equal("Starfield", m.InstallDir);
    }

    [Fact]
    public void ParseAppManifest_missing_fields_are_null()
    {
        var m = SteamParse.ParseAppManifest("{}");
        Assert.Null(m.AppId);
        Assert.Null(m.Name);
        Assert.Null(m.InstallDir);
    }

    [Fact]
    public void ParseAppManifest_extracts_buildId()
    {
        const string acf = """
        "AppState"
        {
            "appid"     "1091500"
            "name"      "Cyberpunk 2077"
            "installdir"    "Cyberpunk 2077"
            "buildid"   "17556649"
            "StateFlags"    "4"
        }
        """;
        var m = SteamParse.ParseAppManifest(acf);
        Assert.Equal("1091500", m.AppId);
        Assert.Equal("Cyberpunk 2077", m.Name);
        Assert.Equal("Cyberpunk 2077", m.InstallDir);
        Assert.Equal("17556649", m.BuildId);
        Assert.Equal("4", m.StateFlags);
    }

    [Fact]
    public void ParseAppManifest_buildId_is_null_when_absent()
    {
        var m = SteamParse.ParseAppManifest("\"AppState\" { \"appid\" \"1\" \"name\" \"X\" \"installdir\" \"X\" }");
        Assert.Null(m.BuildId);
    }

    [Fact]
    public void ParseAppManifest_extracts_stateFlags_and_null_when_absent()
    {
        Assert.Equal("6", SteamParse.ParseAppManifest("\"AppState\" { \"appid\" \"1\" \"StateFlags\" \"6\" }").StateFlags);
        Assert.Null(SteamParse.ParseAppManifest("\"AppState\" { \"appid\" \"1\" }").StateFlags);
    }
}
