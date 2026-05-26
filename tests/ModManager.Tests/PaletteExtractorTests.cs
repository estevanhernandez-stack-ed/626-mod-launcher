using ModManager.Core;

namespace ModManager.Tests;

public class PaletteExtractorTests
{
    // A 2-color image (half black, half white): k=2 should recover both clusters.
    [Fact]
    public void Extracts_two_dominant_colors_from_a_bicolor_image()
    {
        // 8 pixels: 4 black, 4 white. RGBA byte order.
        var pixels = new byte[]
        {
            0,0,0,255,  0,0,0,255,  0,0,0,255,  0,0,0,255,
            255,255,255,255,  255,255,255,255,  255,255,255,255,  255,255,255,255,
        };
        var palette = PaletteExtractor.Extract(pixels, width: 4, height: 2, k: 2);
        Assert.Equal(2, palette.Count);
        // Sorted by brightness ascending — first is darkest.
        Assert.True(palette[0].R < 40 && palette[0].G < 40 && palette[0].B < 40);
        Assert.True(palette[1].R > 200 && palette[1].G > 200 && palette[1].B > 200);
    }

    // A solid-color image: every cluster collapses to the same color.
    [Fact]
    public void Solid_color_image_yields_palette_of_that_one_color()
    {
        var pixels = new byte[16];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 80; pixels[i+1] = 120; pixels[i+2] = 200; pixels[i+3] = 255; }
        var palette = PaletteExtractor.Extract(pixels, width: 2, height: 2, k: 5);
        Assert.NotEmpty(palette);
        Assert.All(palette, c => { Assert.InRange(c.R, 78, 82); Assert.InRange(c.G, 118, 122); Assert.InRange(c.B, 198, 202); });
    }

    // k larger than pixel count must still return at most pixel-count distinct clusters.
    [Fact]
    public void K_larger_than_pixels_does_not_throw_and_returns_at_most_n_colors()
    {
        var pixels = new byte[] { 10,10,10,255,  200,200,200,255 };
        var palette = PaletteExtractor.Extract(pixels, width: 2, height: 1, k: 10);
        Assert.True(palette.Count <= 2);
        Assert.NotEmpty(palette);
    }

    // Transparent pixels (alpha == 0) should be excluded so a PNG with a transparent background
    // doesn't pull the palette toward an arbitrary fill color.
    [Fact]
    public void Fully_transparent_pixels_are_excluded_from_clustering()
    {
        var pixels = new byte[]
        {
            // 2 fully-transparent (could be any color)
            255,0,0,0,  0,255,0,0,
            // 2 solid blue
            0,0,200,255,  0,0,200,255,
        };
        var palette = PaletteExtractor.Extract(pixels, width: 2, height: 2, k: 1);
        Assert.Single(palette);
        Assert.True(palette[0].B > 150);
        Assert.True(palette[0].R < 50);
    }

    // Empty input is refused with an ArgumentException (caller must guard).
    [Fact]
    public void Empty_pixels_throws()
    {
        Assert.Throws<ArgumentException>(() => PaletteExtractor.Extract(Array.Empty<byte>(), 0, 0, 3));
    }
}
