using ModManager.Core;

namespace ModManager.Tests;

public class SteamArtTests
{
    // A full named-art folder plus the 32x32 hashed icon Steam also caches.
    private static readonly string[] Named =
    {
        @"C:\lc\1245620\7807d6dcd71d8161465619b4f041794b0353a6d0.jpg", // 32x32 icon (hashed)
        @"C:\lc\1245620\header.jpg",
        @"C:\lc\1245620\library_600x900.jpg",
        @"C:\lc\1245620\library_hero.jpg",
    };

    [Fact]
    public void Portrait_prefers_library_600x900()
        => Assert.Equal(@"C:\lc\1245620\library_600x900.jpg", SteamArt.PickCover(Named, CoverShape.Portrait));

    [Fact]
    public void Landscape_prefers_header()
        => Assert.Equal(@"C:\lc\1245620\header.jpg", SteamArt.PickCover(Named, CoverShape.Landscape));

    [Fact]
    public void Default_shape_is_landscape()
        => Assert.Equal(@"C:\lc\1245620\header.jpg", SteamArt.PickCover(Named));

    [Fact]
    public void Portrait_falls_back_to_header_when_no_portrait_art()
    {
        var files = new[] { @"C:\lc\1\header.jpg", @"C:\lc\1\deadbeef0000000000000000000000000000dead.jpg" };
        Assert.Equal(@"C:\lc\1\header.jpg", SteamArt.PickCover(files, CoverShape.Portrait));
    }

    [Fact]
    public void Landscape_falls_back_to_portrait_when_no_header()
    {
        var files = new[] { @"C:\lc\1\library_600x900.jpg", @"C:\lc\1\deadbeef0000000000000000000000000000dead.jpg" };
        Assert.Equal(@"C:\lc\1\library_600x900.jpg", SteamArt.PickCover(files, CoverShape.Landscape));
    }

    [Fact]
    public void Hashed_icon_only_returns_null_not_the_icon()
    {
        // The Cyberpunk case: Steam cached only a 32x32 hashed icon, no named cover. We must NOT dress
        // that icon as a cover — return null so the caller shows the themed placeholder.
        var files = new[] { @"C:\lc\1091500\6897c3848f3e0350d512f59d5bae174a1e3739f9.jpg" };
        Assert.Null(SteamArt.PickCover(files, CoverShape.Portrait));
        Assert.Null(SteamArt.PickCover(files, CoverShape.Landscape));
    }

    [Fact]
    public void Returns_null_for_non_cover_files_and_empty()
    {
        Assert.Null(SteamArt.PickCover(new[] { @"C:\lc\1\icon.ico", @"C:\lc\1\notes.txt" }));
        Assert.Null(SteamArt.PickCover(System.Array.Empty<string>()));
    }
}
