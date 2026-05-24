using ModManager.Core;

namespace ModManager.Tests;

// Ports url-core.test.js — the http(s)-only guard for open-external.
public class SafeUrlTests
{
    [Theory]
    [InlineData("https://www.curseforge.com/minecraft/mc-mods/jei")]
    [InlineData("http://example.com/x")]
    public void Accepts_http_and_https(string url)
    {
        Assert.True(SafeUrl.IsHttpUrl(url));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("steam://run/1")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a url")]
    public void Rejects_other_schemes_and_junk(string? url)
    {
        Assert.False(SafeUrl.IsHttpUrl(url));
    }
}
