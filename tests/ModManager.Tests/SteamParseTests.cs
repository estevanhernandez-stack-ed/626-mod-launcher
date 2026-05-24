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
}
