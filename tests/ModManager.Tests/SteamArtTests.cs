using ModManager.Core;

namespace ModManager.Tests;

public class SteamArtTests
{
    [Fact]
    public void PickCover_prefers_header_jpg()
    {
        var files = new[]
        {
            @"C:\lc\1091500\7807d6dcd71d8161465619b4f041794b0353a6d0.jpg",
            @"C:\lc\1091500\header.jpg",
        };
        Assert.Equal(@"C:\lc\1091500\header.jpg", SteamArt.PickCover(files));
    }

    [Fact]
    public void PickCover_falls_back_to_any_jpg_when_no_header()
    {
        var files = new[] { @"C:\lc\1042420\dadc80fcc935495943969e0d3cd90cae6c79d8ff.jpg" };
        Assert.Equal(@"C:\lc\1042420\dadc80fcc935495943969e0d3cd90cae6c79d8ff.jpg", SteamArt.PickCover(files));
    }

    [Fact]
    public void PickCover_ignores_non_jpg_and_returns_null_when_none()
    {
        var files = new[] { @"C:\lc\1\icon.ico", @"C:\lc\1\notes.txt" };
        Assert.Null(SteamArt.PickCover(files));
    }

    [Fact]
    public void PickCover_handles_empty()
    {
        Assert.Null(SteamArt.PickCover(System.Array.Empty<string>()));
    }
}
