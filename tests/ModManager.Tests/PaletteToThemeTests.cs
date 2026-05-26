using ModManager.Core;

namespace ModManager.Tests;

public class PaletteToThemeTests
{
    [Fact]
    public void Derives_all_15_required_fields()
    {
        var palette = new List<PaletteColor>
        {
            new(20, 20, 40, 100),
            new(80, 80, 100, 80),
            new(160, 160, 180, 60),
            new(220, 220, 230, 40),
            new(60, 180, 220, 30),
        };
        var raw = PaletteToTheme.Derive(palette, name: "Test theme");
        foreach (var f in Themes.RequiredFields)
            Assert.True(raw.Tokens.ContainsKey(f), $"Missing required field: {f}");
        Assert.Equal("Test theme", raw.Tokens["name"]);
    }

    [Fact]
    public void Background_is_darker_than_text_after_normalization()
    {
        // Even on a bright-only palette, the derived theme should still have a dark bg / light text.
        var palette = new List<PaletteColor>
        {
            new(180, 180, 180, 50),
            new(200, 210, 220, 50),
            new(230, 230, 240, 50),
            new(240, 220, 200, 50),
            new(220, 240, 200, 50),
        };
        var raw = PaletteToTheme.Derive(palette, "Bright palette");
        var bg = ParseHex(raw.Tokens["bg"]);
        var text = ParseHex(raw.Tokens["text"]);
        Assert.True(Brightness(bg) < Brightness(text),
            $"bg ({raw.Tokens["bg"]}) must be darker than text ({raw.Tokens["text"]}).");
    }

    [Fact]
    public void Accent_is_the_highest_chroma_color_when_one_is_clearly_colorful()
    {
        // 4 near-grays + 1 vivid blue → accent should pick the blue.
        var palette = new List<PaletteColor>
        {
            new(30, 30, 30, 100),
            new(80, 80, 82, 80),
            new(180, 180, 180, 60),
            new(220, 220, 222, 40),
            new(40, 100, 220, 30), // vivid blue
        };
        var raw = PaletteToTheme.Derive(palette, "Mostly gray + blue");
        var accent = ParseHex(raw.Tokens["accent"]);
        // The accent's blue channel should dominate.
        Assert.True(accent.B > accent.R + 40, $"accent {raw.Tokens["accent"]} should be blue-dominant.");
    }

    [Fact]
    public void Empty_palette_throws()
    {
        Assert.Throws<ArgumentException>(() => PaletteToTheme.Derive(new List<PaletteColor>(), "Empty"));
    }

    [Fact]
    public void Result_normalizes_into_a_valid_Theme()
    {
        var palette = new List<PaletteColor>
        {
            new(15, 20, 35, 200),
            new(70, 80, 110, 150),
            new(200, 210, 220, 100),
            new(58, 200, 230, 50),
            new(220, 90, 130, 40),
        };
        var raw = PaletteToTheme.Derive(palette, "From avatar");
        var normalized = Themes.NormalizeTheme("from-avatar", raw);
        Assert.NotNull(normalized);
        Assert.Equal("From avatar", normalized!.Name);
    }

    // --- helpers ---
    private static (int R, int G, int B) ParseHex(string hex)
    {
        var h = hex.TrimStart('#');
        return (Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
    }
    private static double Brightness((int R, int G, int B) c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
}
